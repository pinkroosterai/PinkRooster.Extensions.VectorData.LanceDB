using Apache.Arrow;
using Apache.Arrow.Types;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

internal static class ArrowSchemaMapper
{
    public static Schema BuildSchema(CollectionModel model)
    {
        Schema.Builder builder = new();

        foreach (PropertyModel property in model.Properties)
        {
            Field field = BuildField(property);
            builder.Field(field);
        }

        return builder.Build();
    }

    private static Field BuildField(PropertyModel property)
    {
        string storageName = property.StorageName;

        if (property is KeyPropertyModel keyProperty)
        {
            IArrowType arrowType = LanceDbDataTypes.Classify(keyProperty.Type) switch
            {
                LanceDbDataKind.String => StringType.Default,
                LanceDbDataKind.Int32 => Int32Type.Default,
                LanceDbDataKind.Int64 => Int64Type.Default,
                LanceDbDataKind.Guid => StringType.Default,
                _ => throw new NotSupportedException($"Key type '{keyProperty.Type.Name}' is not supported.")
            };

            return new Field(storageName, arrowType, nullable: false);
        }

        if (property is VectorPropertyModel vectorProperty)
        {
            int dimensions = vectorProperty.Dimensions;
            if (dimensions <= 0)
            {
                throw new InvalidOperationException($"Vector property '{vectorProperty.ModelName}' must declare a positive dimension count.");
            }

            // LanceDB vector item fields are nullable: true
            FixedSizeListType vectorType = new(new Field("item", FloatType.Default, nullable: true), dimensions);
            return new Field(storageName, vectorType, nullable: vectorProperty.IsNullable);
        }

        if (property is DataPropertyModel dataProperty)
        {
            IArrowType arrowType = LanceDbDataTypes.Classify(dataProperty.Type) switch
            {
                LanceDbDataKind.String => StringType.Default,
                LanceDbDataKind.Boolean => BooleanType.Default,
                LanceDbDataKind.Int8 => Int8Type.Default,
                LanceDbDataKind.UInt8 => UInt8Type.Default,
                LanceDbDataKind.Int16 => Int16Type.Default,
                LanceDbDataKind.UInt16 => UInt16Type.Default,
                LanceDbDataKind.Int32 => Int32Type.Default,
                LanceDbDataKind.UInt32 => UInt32Type.Default,
                LanceDbDataKind.Int64 => Int64Type.Default,
                LanceDbDataKind.UInt64 => UInt64Type.Default,
                LanceDbDataKind.Float32 => FloatType.Default,
                LanceDbDataKind.Float64 => DoubleType.Default,
                LanceDbDataKind.Guid => StringType.Default,
                LanceDbDataKind.DateTime => new TimestampType(TimeUnit.Microsecond, "UTC"),
                LanceDbDataKind.DateTimeOffset => new TimestampType(TimeUnit.Microsecond, "UTC"),
                LanceDbDataKind.DateOnly => Date32Type.Default,
                LanceDbDataKind.TimeOnly => new Time64Type(TimeUnit.Microsecond),
                LanceDbDataKind.StringArray or LanceDbDataKind.StringList =>
                    new ListType(new Field("item", StringType.Default, nullable: true)),
                _ => throw new NotSupportedException($"Data property type '{dataProperty.Type.Name}' is not supported.")
            };

            return new Field(storageName, arrowType, nullable: dataProperty.IsNullable);
        }

        throw new NotSupportedException($"Property '{property.ModelName}' has unsupported model kind '{property.GetType().Name}'.");
    }

    public static void ValidateExistingTableSchema(Schema existingSchema, Schema expectedSchema, string tableName)
    {
        foreach (Field expectedField in expectedSchema.FieldsList)
        {
            Field? existingField = existingSchema.GetFieldByName(expectedField.Name);
            if (existingField is null)
            {
                throw new VectorStoreException(
                    $"Table '{tableName}' schema does not match record definition: column '{expectedField.Name}' is missing in the database table.")
                {
                    VectorStoreSystemName = "LanceDB",
                    CollectionName = tableName,
                };
            }

            if (!AreDataTypesCompatible(expectedField.DataType, existingField.DataType))
            {
                throw new VectorStoreException(
                    $"Table '{tableName}' schema does not match record definition: column '{expectedField.Name}' has mismatched type '{existingField.DataType.Name}', expected '{expectedField.DataType.Name}'.")
                {
                    VectorStoreSystemName = "LanceDB",
                    CollectionName = tableName,
                };
            }
        }
    }

    private static bool AreDataTypesCompatible(IArrowType expected, IArrowType existing)
    {
        if (expected.TypeId == existing.TypeId)
        {
            if (expected is FixedSizeListType expectedList && existing is FixedSizeListType existingList)
            {
                return expectedList.ListSize == existingList.ListSize &&
                       expectedList.ValueDataType.TypeId == existingList.ValueDataType.TypeId;
            }

            if (expected is ListType expectedL && existing is ListType existingL)
            {
                return expectedL.ValueDataType.TypeId == existingL.ValueDataType.TypeId;
            }

            return true;
        }

        return false;
    }
}
