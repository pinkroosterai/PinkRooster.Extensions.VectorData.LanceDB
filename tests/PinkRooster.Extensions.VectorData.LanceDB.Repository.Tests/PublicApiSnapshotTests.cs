using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PinkRooster.Extensions.VectorData.LanceDB.Repository.Tests;

/// <summary>
/// Keeps a text listing of each package's public API under <c>PublicApi/</c>, so a change to it, wanted or not, shows up in the diff of
/// the change that made it. The API may change freely before 1.0; this only makes the change visible.
/// </summary>
/// <remarks>To accept a change, run the tests once with the environment variable <c>PINKROOSTER_UPDATE_PUBLIC_API=1</c> and commit the files.</remarks>
public sealed class PublicApiSnapshotTests
{
    private const string UpdateVariable = "PINKROOSTER_UPDATE_PUBLIC_API";

    public static TheoryData<string> Packages => new("PinkRooster.Extensions.VectorData.LanceDB");

    [Theory]
    [MemberData(nameof(Packages))]
    public void ThePublicApi_IsTheOneOnFile(string package)
    {
        string actual = Describe(Assembly.Load(package));
        string path = Path.Combine(RepositoryRoot.Path, "tests", "PinkRooster.Extensions.VectorData.LanceDB.Repository.Tests", "PublicApi", package + ".txt");

        if (string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        string expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : "";
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return;
        }

        HashSet<string> before = [.. expected.Split('\n')];
        HashSet<string> after = [.. actual.Split('\n')];
        IEnumerable<string> changes = before.Except(after, StringComparer.Ordinal).Select(line => "- " + line).Concat(after.Except(before, StringComparer.Ordinal).Select(line => "+ " + line)).Take(40);
        Assert.Fail(
            $"The public API of {package} differs from PublicApi/{package}.txt. If the change is meant, run the tests once with {UpdateVariable}=1 " +
            $"and commit the file; if not, the type or member below became public, or changed, by accident.\n{string.Join("\n", changes)}");
    }

    private static readonly NullabilityInfoContext _nullability = new();

    private static string Describe(Assembly assembly) => DescribeTypes(assembly.GetExportedTypes());

    private static string DescribeTypes(IEnumerable<Type> types)
    {
        StringBuilder text = new();
        foreach (Type type in types.OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            text.Append(Kind(type)).Append(' ').Append(Name(type, declaration: true));
            string[] bases =
            [
                .. type.IsEnum ? new[] { Name(Enum.GetUnderlyingType(type)) }
                    : type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(MulticastDelegate) ? new[] { Name(baseType) } : [],
                .. type.IsEnum ? Enumerable.Empty<string>() : type.GetInterfaces().Except(type.BaseType?.GetInterfaces() ?? []).Select(item => Name(item)).Order(StringComparer.Ordinal)
            ];
            if (bases.Length > 0)
            {
                text.Append(" : ").AppendJoin(", ", bases);
            }
            text.Append(Constraints(type.GetGenericArguments())).Append('\n');

            IEnumerable<string> members = type.IsEnum
                ? type.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(field => field.Name, StringComparer.Ordinal)
                    .Select(field => field.Name + " = " + Literal(field.GetRawConstantValue(), Enum.GetUnderlyingType(type)))
                : type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(member => !member.Name.StartsWith('<') && member.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
                    .Select(Member).OfType<string>().Order(StringComparer.Ordinal);
            foreach (string member in members)
            {
                text.Append("    ").Append(member).Append('\n');
            }
        }
        return text.ToString();
    }

    private static string Kind(Type type) =>
        type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct"
        : (type.IsAbstract && type.IsSealed ? "static " : type.IsAbstract ? "abstract " : type.IsSealed ? "sealed " : "") + "class";

    private static string? Member(MemberInfo member) => member switch
    {
        ConstructorInfo { IsStatic: false } constructor when Access(constructor) is { } access => $"{access}.ctor({Parameters(constructor)})",
        MethodInfo { IsSpecialName: false } method when Access(method) is { } access =>
            $"{access}{Modifiers(method)}{method.Name}{Generic(method.GetGenericArguments())}({Parameters(method)}) : {Name(method.ReturnType, info: _nullability.Create(method.ReturnParameter), annotations: new NullableAnnotations(method.ReturnParameter.CustomAttributes, method.ReturnParameter.Member))}{Constraints(method.GetGenericArguments())}",
        PropertyInfo property when Accessors(property) is { Length: > 0 } accessors =>
            $"{Required(property)}{Modifiers(property.GetMethod ?? property.SetMethod!)}{property.Name} : {Name(property.PropertyType, info: _nullability.Create(property), annotations: new NullableAnnotations(property.CustomAttributes, property))} {{ {accessors} }}",
        FieldInfo field when Access(field.IsPublic, field.IsFamily || field.IsFamilyOrAssembly) is { } access =>
            $"{access}{Required(field)}{(field.IsLiteral ? "const " : (field.IsStatic ? "static " : "") + (field.IsInitOnly ? "readonly " : ""))}{field.Name} : {Name(field.FieldType, info: _nullability.Create(field), annotations: new NullableAnnotations(field.CustomAttributes, field))}{(field.IsLiteral ? " = " + Literal(field.GetRawConstantValue(), field.FieldType) : "")}",
        _ => null
    };

    private static string Required(MemberInfo member) => member.IsDefined(typeof(RequiredMemberAttribute)) ? "required " : "";

    private static string? Access(MethodBase method) => Access(method.IsPublic, method.IsFamily || method.IsFamilyOrAssembly);

    private static string? Access(bool isPublic, bool isProtected) => isPublic ? "" : isProtected ? "protected " : null;

    private static string Modifiers(MethodInfo method) =>
        (method.IsStatic ? "static " : "") + (method.IsAbstract ? "abstract " : method.IsVirtual && !method.IsFinal && method.GetBaseDefinition() != method ? "override "
        : method.IsVirtual && !method.IsFinal ? "virtual " : "");

    private static string Accessors(PropertyInfo property) =>
        string.Join(" ", new[] { (property.GetMethod, "get;"), (property.SetMethod, property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) == true ? "init;" : "set;") }
            .Where(accessor => accessor.Item1 is not null && Access(accessor.Item1) is not null)
            .Select(accessor => Access(accessor.Item1!) + accessor.Item2));

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(parameter =>
            (parameter.GetCustomAttribute<ParamArrayAttribute>() is not null || parameter.GetCustomAttribute<ParamCollectionAttribute>() is not null ? "params " : "")
            + Name(parameter.ParameterType, info: _nullability.Create(parameter), annotations: new NullableAnnotations(parameter.CustomAttributes, parameter.Member)) + " " + parameter.Name
            + (parameter.HasDefaultValue ? " = " + Literal(parameter.DefaultValue, parameter.ParameterType) : parameter.IsOptional ? " = missing" : "")));

    private static string Literal(object? value, Type type)
    {
        if (value is null)
        {
            return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? $"default({Name(type)})" : "null";
        }
        if (type.IsEnum)
        {
            return $"({Name(type)}){Convert.ToString(Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)}";
        }
        return value switch
        {
            string text => JsonSerializer.Serialize(text),
            char character => "'" + (character switch { '\'' => "\\'", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\0' => "\\0", _ => character.ToString() }) + "'",
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "missing",
        };
    }

    private static string Constraints(Type[] arguments) => string.Concat(arguments.Where(argument => argument.IsGenericParameter).Select(Constraint));

    private static string Constraint(Type argument)
    {
        List<string> constraints = [];
        GenericParameterAttributes flags = argument.GenericParameterAttributes;
        bool unmanaged = argument.CustomAttributes.Any(attribute => string.Equals(attribute.AttributeType.FullName, "System.Runtime.CompilerServices.IsUnmanagedAttribute", StringComparison.Ordinal));
        byte? nullable = NullableFlag(argument);
        if ((flags & GenericParameterAttributes.NotNullableValueTypeConstraint) != GenericParameterAttributes.None)
        {
            constraints.Add(unmanaged ? "unmanaged" : "struct");
        }
        else if ((flags & GenericParameterAttributes.ReferenceTypeConstraint) != GenericParameterAttributes.None)
        {
            constraints.Add(nullable == 2 ? "class?" : "class");
        }
        else if (nullable == 1 && argument.GetGenericParameterConstraints().Length == 0)
        {
            constraints.Add("notnull");
        }
        constraints.AddRange(argument.GetGenericParameterConstraints().Where(type => type != typeof(ValueType)).Select(type => Name(type)));
        if ((flags & GenericParameterAttributes.DefaultConstructorConstraint) != GenericParameterAttributes.None && (flags & GenericParameterAttributes.NotNullableValueTypeConstraint) == GenericParameterAttributes.None)
        {
            constraints.Add("new()");
        }
        if ((flags & GenericParameterAttributes.AllowByRefLike) != GenericParameterAttributes.None)
        {
            constraints.Add("allows ref struct");
        }
        return constraints.Count == 0 ? "" : $" where {argument.Name} : {string.Join(", ", constraints)}";
    }

    private static byte? NullableFlag(Type argument) =>
        new NullableAnnotations(argument.CustomAttributes, argument.DeclaringMethod ?? (MemberInfo?)argument.DeclaringType).Next();

    private sealed class NullableAnnotations
    {
        private readonly byte[] _flags;
        private readonly byte _context;
        private int _index;

        public NullableAnnotations(IEnumerable<CustomAttributeData> attributes, MemberInfo? context)
        {
            CustomAttributeData? annotation = attributes.FirstOrDefault(attribute => string.Equals(attribute.AttributeType.FullName, "System.Runtime.CompilerServices.NullableAttribute", StringComparison.Ordinal));
            object? value = annotation?.ConstructorArguments.FirstOrDefault().Value;
            _flags = value switch
            {
                byte flag => [flag],
                IReadOnlyCollection<CustomAttributeTypedArgument> flags => [.. flags.Select(item => (byte)item.Value!)],
                _ => [],
            };
            for (; context is not null; context = context.DeclaringType)
            {
                CustomAttributeData? inherited = context.CustomAttributes.FirstOrDefault(attribute => string.Equals(attribute.AttributeType.FullName, "System.Runtime.CompilerServices.NullableContextAttribute", StringComparison.Ordinal));
                if (inherited?.ConstructorArguments.FirstOrDefault().Value is byte contextFlag)
                {
                    _context = contextFlag;
                    break;
                }
            }
        }

        public byte Next() => _flags.Length == 1 ? _flags[0] : _index < _flags.Length ? _flags[_index++] : _context;
    }

    private static string Variance(Type argument) => (argument.GenericParameterAttributes & GenericParameterAttributes.VarianceMask) switch
    {
        GenericParameterAttributes.Covariant => "out ",
        GenericParameterAttributes.Contravariant => "in ",
        _ => "",
    };

    private static string Generic(Type[] arguments) => arguments.Length == 0 ? "" : $"<{string.Join(", ", arguments.Select(argument => Name(argument)))}>";

    private static string Name(Type type, NullabilityInfo? info = null, bool declaration = false, NullableAnnotations? annotations = null)
    {
        if (type.IsByRef)
        {
            return "ref " + Name(type.GetElementType()!, info: info, annotations: annotations);
        }
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Name(underlying, info: info?.GenericTypeArguments.FirstOrDefault(), annotations: annotations) + "?";
        }
        byte? annotation = !type.IsValueType || type.IsGenericType || type.IsGenericParameter ? annotations?.Next() : null;
        if (type.IsArray)
        {
            return Name(type.GetElementType()!, info: info?.ElementType, annotations: annotations) + "[" + new string(',', type.GetArrayRank() - 1) + "]" + NullableSuffix(type, info);
        }
        if (type.IsGenericParameter)
        {
            return type.Name + (annotation is { } flag ? flag == 2 ? "?" : "" : NullableSuffix(type, info));
        }

        Type[] arguments = type.GetGenericArguments();
        int index = 0;
        return QualifiedName(type) + NullableSuffix(type, info);

        string QualifiedName(Type segment)
        {
            string prefix = segment.DeclaringType is { } parent ? QualifiedName(parent) + "." : segment.Namespace is { } scope ? scope + "." : "";
            int tick = segment.Name.IndexOf('`');
            if (tick < 0)
            {
                return prefix + segment.Name;
            }

            int end = segment.GetGenericArguments().Length;
            List<string> names = [];
            while (index < end)
            {
                int argumentIndex = index++;
                Type argument = arguments[argumentIndex];
                names.Add((declaration ? Variance(argument) : "") + Name(argument, info: info?.GenericTypeArguments.ElementAtOrDefault(argumentIndex), annotations: annotations));
            }

            return prefix + segment.Name[..tick] + "<" + string.Join(", ", names) + ">";
        }
    }

    private static string NullableSuffix(Type type, NullabilityInfo? info) => !type.IsValueType && info?.ReadState == NullabilityState.Nullable ? "?" : "";

    [Fact]
    public void SnapshotFormatter_RecordsValuesDefaultsNullabilityAndConstraints()
    {
        string actual = DescribeTypes([typeof(Shape<>), typeof(ShapeState), typeof(IFactory<>), typeof(Unmanaged<>), typeof(NotNull<>), typeof(IGeneric<>)]);
        Assert.Contains("Ready = 7", actual, StringComparison.Ordinal);
        Assert.Contains("const Limit : System.Int32 = 37", actual, StringComparison.Ordinal);
        Assert.Contains("required Values : System.Collections.Generic.List<System.String?>? { get; init; }", actual, StringComparison.Ordinal);
        Assert.Contains("System.String? text = \"a\\nb\", System.Int32 count = 37, System.Boolean enabled = true", actual, StringComparison.Ordinal);
        Assert.Contains($"state = ({typeof(ShapeState).FullName!.Replace('+', '.')})7", actual, StringComparison.Ordinal);
        Assert.Contains("token = default(System.Threading.CancellationToken)", actual, StringComparison.Ordinal);
        Assert.Contains("where T : class, System.IComparable<T>, new()", actual, StringComparison.Ordinal);
        Assert.Contains("static abstract Create() : T", actual, StringComparison.Ordinal);
        Assert.Contains("where T : unmanaged", actual, StringComparison.Ordinal);
        Assert.Contains("where T : notnull", actual, StringComparison.Ordinal);
        Assert.Contains("where TResult : notnull", actual, StringComparison.Ordinal);
        Assert.Contains("Raw(T item) : T", actual, StringComparison.Ordinal);
        Assert.Contains("Maybe(T? item) : T?", actual, StringComparison.Ordinal);
        Assert.Contains("Nested(System.Collections.Generic.List<T> items) : System.Collections.Generic.List<T?>", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotFormatter_DistinguishesSameNamedTypesInNestedSignatures()
    {
        Type first = typeof(Outer<System.Timers.Timer>.Inner<System.Threading.Timer>);
        Type second = typeof(Outer<System.Threading.Timer>.Inner<System.Timers.Timer>);

        Assert.EndsWith(".Outer<System.Timers.Timer>.Inner<System.Threading.Timer>", Name(first), StringComparison.Ordinal);
        Assert.EndsWith(".Outer<System.Threading.Timer>.Inner<System.Timers.Timer>", Name(second), StringComparison.Ordinal);
        string actual = DescribeTypes([typeof(TimerSignatures)]);
        Assert.Contains("Convert(System.Threading.Timer[] timers) : System.Collections.Generic.List<System.Timers.Timer>", actual, StringComparison.Ordinal);
    }

    private sealed class Outer<T>
    {
        public sealed class Inner<TInner>;
    }

    private interface TimerSignatures
    {
        List<System.Timers.Timer> Convert(System.Threading.Timer[] timers);
    }

    private sealed class Shape<T> where T : class, IComparable<T>, new()
    {
        public static TResult Constrained<TResult>(TResult value) where TResult : notnull => value;
        public const int Limit = 37;
        public required List<string?>? Values { get; init; }
        public string? Echo(string? text = "a\nb", int count = 37, bool enabled = true, ShapeState state = ShapeState.Ready, CancellationToken token = default) => text;
    }

    private enum ShapeState { Ready = 7 }

    private interface IFactory<T> where T : IFactory<T>
    {
        static abstract T Create();
    }

    private interface IGeneric<T>
    {
        T Raw(T item);
        T? Maybe(T? item);
        List<T?> Nested(List<T> items);
    }

    private sealed class Unmanaged<T> where T : unmanaged;

    private sealed class NotNull<T> where T : notnull;

}
