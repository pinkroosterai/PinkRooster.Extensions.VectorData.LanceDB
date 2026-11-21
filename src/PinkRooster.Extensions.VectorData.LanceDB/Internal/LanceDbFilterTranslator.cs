#pragma warning disable MEVD9001 // FilterTranslatorBase is experimental/evaluation

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Extensions.VectorData.ProviderServices.Filter;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

internal sealed class LanceDbFilterTranslator : FilterTranslatorBase
{
    public static string Translate(LambdaExpression filter, CollectionModel model)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(model);

        LanceDbFilterTranslator translator = new();
        return translator.TranslateCore(filter, model);
    }

    private string TranslateCore(LambdaExpression filter, CollectionModel model)
    {
        Expression preprocessed = PreprocessFilter(filter, model, new FilterPreprocessingOptions { SupportsParameterization = false });
        return VisitPredicate(preprocessed);
    }

    private string VisitPredicate(Expression node)
    {
        if (node is BinaryExpression binary)
        {
            if (binary.NodeType == ExpressionType.AndAlso)
            {
                return $"({VisitPredicate(binary.Left)} AND {VisitPredicate(binary.Right)})";
            }

            if (binary.NodeType == ExpressionType.OrElse)
            {
                return $"({VisitPredicate(binary.Left)} OR {VisitPredicate(binary.Right)})";
            }

            return VisitComparison(binary);
        }

        if (node is UnaryExpression unary)
        {
            if (unary.NodeType == ExpressionType.Not)
            {
                Expression unwrapped = UnwrapConvert(unary.Operand);
                if (TryBindProperty(unwrapped, out PropertyModel? boolProp))
                {
                    return $"(`{boolProp.StorageName}` = false)";
                }

                return $"({VisitPredicate(unary.Operand)}) IS NOT TRUE";
            }

            if (unary.NodeType == ExpressionType.Convert)
            {
                return VisitPredicate(unary.Operand);
            }

            throw new NotSupportedException($"Unary operator '{unary.NodeType}' is not supported in LanceDB filters.");
        }

        Expression unwrappedNode = UnwrapConvert(node);
        if (TryBindProperty(unwrappedNode, out PropertyModel? property))
        {
            if (property.Type == typeof(bool) || Nullable.GetUnderlyingType(property.Type) == typeof(bool))
            {
                return $"(`{property.StorageName}` = true)";
            }
        }

        CheckForInvalidPropertyAccess(unwrappedNode);

        if (unwrappedNode is MethodCallExpression methodCall)
        {
            return VisitMethodCall(methodCall);
        }

        if (unwrappedNode is ConstantExpression constant && constant.Value is bool b)
        {
            return b ? "(1 = 1)" : "(1 = 0)";
        }

        throw new NotSupportedException($"Expression of type '{node.NodeType}' ({node}) is not supported in LanceDB filters.");
    }

    private string VisitComparison(BinaryExpression binary)
    {
        Expression left = binary.Left;
        Expression right = binary.Right;

        bool leftIsNull = IsNullConstant(left);
        bool rightIsNull = IsNullConstant(right);

        if (leftIsNull || rightIsNull)
        {
            Expression nonNull = leftIsNull ? right : left;
            string target = VisitOperand(nonNull);

            return binary.NodeType switch
            {
                ExpressionType.Equal => $"{target} IS NULL",
                ExpressionType.NotEqual => $"{target} IS NOT NULL",
                _ => throw new NotSupportedException($"Null comparison with operator '{binary.NodeType}' is not supported in LanceDB filters.")
            };
        }

        string leftStr = VisitOperand(left);
        string rightStr = VisitOperand(right);

        if (binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual &&
            TryBindProperty(UnwrapConvert(left), out PropertyModel? leftProperty) &&
            TryBindProperty(UnwrapConvert(right), out PropertyModel? rightProperty) &&
            (CanBeNull(leftProperty.Type) || CanBeNull(rightProperty.Type)))
        {
            string equal = $"(({leftStr} IS NULL AND {rightStr} IS NULL) OR " +
                $"({leftStr} IS NOT NULL AND {rightStr} IS NOT NULL AND {leftStr} = {rightStr}))";
            return binary.NodeType == ExpressionType.Equal ? equal : $"NOT {equal}";
        }

        if (binary.NodeType == ExpressionType.NotEqual)
        {
            if (TryBindProperty(UnwrapConvert(left), out _) && !TryBindProperty(UnwrapConvert(right), out _))
            {
                return $"({leftStr} != {rightStr} OR {leftStr} IS NULL)";
            }

            if (TryBindProperty(UnwrapConvert(right), out _) && !TryBindProperty(UnwrapConvert(left), out _))
            {
                return $"({leftStr} != {rightStr} OR {rightStr} IS NULL)";
            }
        }

        string op = binary.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            _ => throw new NotSupportedException($"Binary comparison operator '{binary.NodeType}' is not supported in LanceDB filters.")
        };

        return $"{leftStr} {op} {rightStr}";
    }

    private string VisitOperand(Expression expr)
    {
        Expression unwrapped = UnwrapConvert(expr);

        if (TryBindProperty(unwrapped, out PropertyModel? property))
        {
            return $"`{property.StorageName}`";
        }

        CheckForInvalidPropertyAccess(unwrapped);

        if (unwrapped is ConstantExpression constant)
        {
            return FormatLiteral(constant.Value);
        }

        object? value = EvaluateExpression(unwrapped);
        return FormatLiteral(value);
    }

    private string VisitMethodCall(MethodCallExpression methodCall)
    {
        if (TryMatchContains(methodCall, out Expression? source, out Expression? item))
        {
            Expression unwrappedSource = UnwrapConvert(source!);
            if (TryBindProperty(unwrappedSource, out PropertyModel? listProp))
            {
                object? itemVal = EvaluateExpression(item!);
                string formattedItem = FormatLiteral(itemVal);
                return $"array_has(`{listProp.StorageName}`, {formattedItem})";
            }

            Expression unwrappedItem = UnwrapConvert(item!);
            if (TryBindProperty(unwrappedItem, out PropertyModel? elemProp))
            {
                object? valuesObj = EvaluateExpression(source!);
                if (valuesObj is IEnumerable values)
                {
                    List<string> formatted = [];
                    foreach (object? val in values)
                    {
                        formatted.Add(FormatLiteral(val));
                    }

                    if (formatted.Count == 0)
                    {
                        return "(1 = 0)";
                    }

                    return $"(`{elemProp.StorageName}` IN ({string.Join(", ", formatted)}))";
                }
            }

            CheckForInvalidPropertyAccess(unwrappedSource);
            CheckForInvalidPropertyAccess(unwrappedItem);

            throw new NotSupportedException("Contains method call must target a collection property or a collection of values containing a record property.");
        }

        if (methodCall.Method.Name == "Any" &&
            (methodCall.Arguments.Count == 2 || (methodCall.Object is not null && methodCall.Arguments.Count == 1)))
        {
            Expression sourceExpr = methodCall.Object ?? methodCall.Arguments[0];
            Expression lambdaArg = methodCall.Object is not null ? methodCall.Arguments[0] : methodCall.Arguments[1];

            Expression unwrappedSource = UnwrapConvert(sourceExpr);
            if (!TryBindProperty(unwrappedSource, out PropertyModel? listProp))
            {
                CheckForInvalidPropertyAccess(unwrappedSource);
                throw new NotSupportedException("Source of Any() method call must be a collection property.");
            }

            if (lambdaArg is LambdaExpression lambda)
            {
                ParameterExpression param = lambda.Parameters[0];
                Expression body = lambda.Body;

                if (body is MethodCallExpression innerCall &&
                    TryMatchContains(innerCall, out Expression? innerSource, out Expression? innerItem))
                {
                    Expression unwrappedInnerItem = UnwrapConvert(innerItem!);
                    if (unwrappedInnerItem == param)
                    {
                        object? valuesObj = EvaluateExpression(innerSource!);
                        if (valuesObj is IEnumerable values)
                        {
                            List<string> formatted = [];
                            foreach (object? val in values)
                            {
                                formatted.Add(FormatLiteral(val));
                            }

                            if (formatted.Count == 0)
                            {
                                return "(1 = 0)";
                            }

                            return $"array_has_any(`{listProp.StorageName}`, [{string.Join(", ", formatted)}])";
                        }
                    }
                }

                if (body is BinaryExpression { NodeType: ExpressionType.Equal } eq)
                {
                    Expression left = UnwrapConvert(eq.Left);
                    Expression right = UnwrapConvert(eq.Right);

                    if (left == param)
                    {
                        return $"array_has(`{listProp.StorageName}`, {FormatLiteral(EvaluateExpression(right))})";
                    }

                    if (right == param)
                    {
                        return $"array_has(`{listProp.StorageName}`, {FormatLiteral(EvaluateExpression(left))})";
                    }
                }
            }

            throw new NotSupportedException($"Unsupported predicate in Any() call: {lambdaArg}");
        }

        throw new NotSupportedException($"Method call '{methodCall.Method.Name}' is not supported in LanceDB filters.");
    }

    private void CheckForInvalidPropertyAccess(Expression expr)
    {
        Expression unwrapped = UnwrapConvert(expr);

        if (unwrapped is MethodCallExpression call &&
            call.Method.Name == "get_Item" &&
            call.Object == RecordParameter)
        {
            object? propName = EvaluateExpression(call.Arguments[0]);
            throw new InvalidOperationException($"Property '{propName}' was not found in the collection model.");
        }

        if (unwrapped is MemberExpression member && member.Expression == RecordParameter)
        {
            throw new InvalidOperationException($"Property '{member.Member.Name}' was not found in the collection model.");
        }
    }

    private static bool CanBeNull(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static bool IsNullConstant(Expression expr)
    {
        Expression unwrapped = UnwrapConvert(expr);
        return unwrapped is ConstantExpression { Value: null };
    }

    private static Expression UnwrapConvert(Expression expr)
    {
        while (expr is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            expr = unary.Operand;
        }

        return expr;
    }

    private static object? EvaluateExpression(Expression expr)
    {
        Expression unwrapped = UnwrapConvert(expr);

        if (unwrapped is ConstantExpression constant)
        {
            return constant.Value;
        }

        Expression<Func<object?>> lambda = Expression.Lambda<Func<object?>>(
            Expression.Convert(unwrapped, typeof(object)));
        return lambda.Compile()();
    }

    private static string FormatLiteral(object? value)
    {
        if (value is null)
        {
            return "NULL";
        }

        if (value is bool b)
        {
            return b ? "true" : "false";
        }

        if (value is string s)
        {
            return $"'{s.Replace("'", "''")}'";
        }

        if (value is Guid g)
        {
            return $"'{g:D}'";
        }

        if (value is sbyte or byte or short or ushort or int or uint or long or ulong)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }

        if (value is float f)
        {
            return f.ToString("R", CultureInfo.InvariantCulture);
        }

        if (value is double d)
        {
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        if (value is DateTime dt)
        {
            DateTime utc = dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return FormattableString.Invariant($"TIMESTAMP '{utc:yyyy-MM-dd HH:mm:ss.ffffff}'");
        }

        if (value is DateTimeOffset dto)
        {
            return FormattableString.Invariant($"TIMESTAMP '{dto.UtcDateTime:yyyy-MM-dd HH:mm:ss.ffffff}'");
        }

        if (value is DateOnly date)
        {
            return FormattableString.Invariant($"DATE '{date:yyyy-MM-dd}'");
        }

        if (value is TimeOnly time)
        {
            return FormattableString.Invariant($"TIME '{time:HH:mm:ss.ffffff}'");
        }

        if (value is IEnumerable enumerable and not string)
        {
            List<string> items = [];
            foreach (object? item in enumerable)
            {
                items.Add(FormatLiteral(item));
            }

            return $"[{string.Join(", ", items)}]";
        }

        throw new NotSupportedException($"Literal of type '{value.GetType().Name}' is not supported in LanceDB filters.");
    }
}
