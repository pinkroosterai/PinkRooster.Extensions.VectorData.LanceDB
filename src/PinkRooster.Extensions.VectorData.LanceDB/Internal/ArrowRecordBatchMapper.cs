using System.Buffers;
using System.Collections;
using System.Runtime.InteropServices;
using Apache.Arrow;
using Apache.Arrow.Arrays;
using Apache.Arrow.Memory;
using Apache.Arrow.Types;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

internal static class ArrowRecordBatchMapper
{
    public static RecordBatch BuildBatch<TRecord>(
        CollectionModel model,
        Schema schema,
        IReadOnlyList<TRecord> records,
        IReadOnlyDictionary<VectorPropertyModel, IReadOnlyList<object?>>? generatedEmbeddings = null)
    {
        int rowCount = records.Count;
        IArrowArray[] columns = new IArrowArray[model.Properties.Count];

        for (int i = 0; i < model.Properties.Count; i++)
        {
            PropertyModel property = model.Properties[i];
            columns[i] = BuildColumnArray(property, records, generatedEmbeddings);
        }

        return new RecordBatch(schema, columns, rowCount);
    }

    private static IArrowArray BuildColumnArray<TRecord>(
        PropertyModel property,
        IReadOnlyList<TRecord> records,
        IReadOnlyDictionary<VectorPropertyModel, IReadOnlyList<object?>>? generatedEmbeddings = null)
    {
        if (property is KeyPropertyModel keyProperty)
        {
            return BuildKeyArray(keyProperty, records);
        }

        if (property is VectorPropertyModel vectorProperty)
        {
            IReadOnlyList<object?>? generated = null;
            generatedEmbeddings?.TryGetValue(vectorProperty, out generated);
            return BuildVectorArray(vectorProperty, records, generated);
        }

        if (property is DataPropertyModel dataProperty)
        {
            return BuildDataArray(dataProperty, records);
        }

        throw new NotSupportedException($"Unsupported property model kind '{property.GetType().Name}'.");
    }

    private static IArrowArray BuildKeyArray<TRecord>(KeyPropertyModel keyProperty, IReadOnlyList<TRecord> records)
    {
        Type keyType = keyProperty.Type;

        LanceDbDataKind kind = LanceDbDataTypes.Classify(keyType);
        if (kind == LanceDbDataKind.String)
        {
            StringArray.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = keyProperty.GetValueAsObject(record!);
                builder.Append(val is not null ? (string)val : string.Empty);
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Int32)
        {
            Int32Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = keyProperty.GetValueAsObject(record!);
                builder.Append(val is not null ? (int)val : 0);
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Int64)
        {
            Int64Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = keyProperty.GetValueAsObject(record!);
                builder.Append(val is not null ? (long)val : 0L);
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Guid)
        {
            StringArray.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = keyProperty.GetValueAsObject(record!);
                builder.Append(val is not null ? ((Guid)val).ToString("D") : Guid.Empty.ToString("D"));
            }

            return builder.Build();
        }

        throw new NotSupportedException($"Key type '{keyType.Name}' is not supported.");
    }

    private static FixedSizeListArray BuildVectorArray<TRecord>(
        VectorPropertyModel vectorProperty,
        IReadOnlyList<TRecord> records,
        IReadOnlyList<object?>? generatedForProperty = null)
    {
        int dimensions = vectorProperty.Dimensions;
        FloatArray.Builder floatValuesBuilder = new();
        ArrowBuffer.BitmapBuilder nullBitmapBuilder = new();
        int nullCount = 0;

        for (int i = 0; i < records.Count; i++)
        {
            TRecord record = records[i];
            object? val = (generatedForProperty is not null && i < generatedForProperty.Count && generatedForProperty[i] is not null)
                ? generatedForProperty[i]
                : vectorProperty.GetValueAsObject(record!);

            if (val is null)
            {
                nullBitmapBuilder.Append(false);
                nullCount++;
                for (int d = 0; d < dimensions; d++)
                {
                    floatValuesBuilder.Append(0.0f);
                }
            }
            else
            {
                nullBitmapBuilder.Append(true);

                if (val is ReadOnlyMemory<float> rom)
                {
                    if (rom.Length != dimensions)
                    {
                        throw new ArgumentException(
                            $"Vector property '{vectorProperty.ModelName}' length {rom.Length} does not match declared dimensions {dimensions}.",
                            vectorProperty.ModelName);
                    }

                    ReadOnlySpan<float> span = rom.Span;
                    for (int d = 0; d < span.Length; d++)
                    {
                        floatValuesBuilder.Append(span[d]);
                    }
                }
                else if (val is float[] arr)
                {
                    if (arr.Length != dimensions)
                    {
                        throw new ArgumentException(
                            $"Vector property '{vectorProperty.ModelName}' length {arr.Length} does not match declared dimensions {dimensions}.",
                            vectorProperty.ModelName);
                    }

                    for (int d = 0; d < arr.Length; d++)
                    {
                        floatValuesBuilder.Append(arr[d]);
                    }
                }
                else if (val is Embedding<float> emb)
                {
                    if (emb.Vector.Length != dimensions)
                    {
                        throw new ArgumentException(
                            $"Vector property '{vectorProperty.ModelName}' length {emb.Vector.Length} does not match declared dimensions {dimensions}.",
                            vectorProperty.ModelName);
                    }

                    ReadOnlySpan<float> span = emb.Vector.Span;
                    for (int d = 0; d < span.Length; d++)
                    {
                        floatValuesBuilder.Append(span[d]);
                    }
                }
                else
                {
                    throw new NotSupportedException($"Vector property value of type '{val.GetType().Name}' is not supported.");
                }
            }
        }

        FixedSizeListType vectorType = new(new Field("item", FloatType.Default, nullable: true), dimensions);
        ArrowBuffer validityBuffer = nullCount > 0 ? nullBitmapBuilder.Build() : ArrowBuffer.Empty;

        ArrayData arrayData = new(
            vectorType,
            records.Count,
            nullCount,
            0,
            [validityBuffer],
            [floatValuesBuilder.Build().Data]);

        return new FixedSizeListArray(arrayData);
    }

    private static IArrowArray BuildDataArray<TRecord>(DataPropertyModel dataProperty, IReadOnlyList<TRecord> records)
    {
        LanceDbDataKind kind = LanceDbDataTypes.Classify(dataProperty.Type);

        if (kind == LanceDbDataKind.String)
        {
            StringArray.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((string)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Boolean)
        {
            BooleanArray.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((bool)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Int8)
        {
            Int8Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((sbyte)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.UInt8)
        {
            UInt8Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((byte)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Int16)
        {
            Int16Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((short)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.UInt16)
        {
            UInt16Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((ushort)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Int32)
        {
            Int32Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((int)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.UInt32)
        {
            UInt32Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((uint)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Int64)
        {
            Int64Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((long)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.UInt64)
        {
            UInt64Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((ulong)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Float32)
        {
            FloatArray.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((float)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Float64)
        {
            DoubleArray.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append((double)val);
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.Guid)
        {
            StringArray.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(((Guid)val).ToString("D"));
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.DateTime)
        {
            TimestampArray.Builder builder = new(new TimestampType(TimeUnit.Microsecond, "UTC"));
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    DateTime dt = (DateTime)val;
                    DateTime utc = dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                    builder.Append(new DateTimeOffset(utc, TimeSpan.Zero));
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.DateTimeOffset)
        {
            TimestampArray.Builder builder = new(new TimestampType(TimeUnit.Microsecond, "UTC"));
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    DateTimeOffset dto = (DateTimeOffset)val;
                    builder.Append(dto.ToUniversalTime());
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.DateOnly)
        {
            Date32Array.Builder builder = new();
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    DateOnly date = (DateOnly)val;
                    builder.Append(date.ToDateTime(TimeOnly.MinValue));
                }
            }

            return builder.Build();
        }

        if (kind == LanceDbDataKind.TimeOnly)
        {
            Time64Array.Builder builder = new(new Time64Type(TimeUnit.Microsecond));
            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    TimeOnly time = (TimeOnly)val;
                    builder.Append(time.ToTimeSpan().Ticks / 10);
                }
            }

            return builder.Build();
        }

        if (kind is LanceDbDataKind.StringArray or LanceDbDataKind.StringList)
        {
            ListArray.Builder builder = new(StringType.Default);
            StringArray.Builder valueBuilder = (StringArray.Builder)builder.ValueBuilder;

            foreach (TRecord record in records)
            {
                object? val = dataProperty.GetValueAsObject(record!);
                if (val is null)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append();
                    IEnumerable strings = (IEnumerable)val;
                    foreach (object? item in strings)
                    {
                        if (item is string s)
                        {
                            valueBuilder.Append(s);
                        }
                        else
                        {
                            valueBuilder.AppendNull();
                        }
                    }
                }
            }

            return builder.Build();
        }

        throw new NotSupportedException($"Data property type '{dataProperty.Type.Name}' is not supported.");
    }

    public static TRecord ReadRecord<TRecord>(RecordBatch batch, int rowIndex, CollectionModel model, bool includeVectors = true)
        where TRecord : class
    {
        if (typeof(TRecord) == typeof(Dictionary<string, object?>))
        {
            Dictionary<string, object?> dict = new();
            PopulateRecord(batch, rowIndex, model, dict, includeVectors);
            return (TRecord)(object)dict;
        }

        TRecord record = Activator.CreateInstance<TRecord>();
        PopulateRecord(batch, rowIndex, model, record, includeVectors);
        return record;
    }

    private static void PopulateRecord(RecordBatch batch, int rowIndex, CollectionModel model, object record, bool includeVectors = true)
    {
        Dictionary<string, object?>? dict = record as Dictionary<string, object?>;

        foreach (PropertyModel property in model.Properties)
        {
            if (!includeVectors && property is VectorPropertyModel)
            {
                continue;
            }

            IArrowArray? array = batch.Column(property.StorageName);
            if (array is null)
            {
                continue;
            }

            if (array.IsNull(rowIndex))
            {
                if (dict is not null)
                {
                    dict[property.ModelName] = null;
                }
                else
                {
                    property.SetValueAsObject(record, null);
                }

                continue;
            }

            object? value = ReadValue(array, rowIndex, property);
            if (dict is not null)
            {
                dict[property.ModelName] = value;
            }
            else
            {
                property.SetValueAsObject(record, value);
            }
        }
    }

    private static object? ReadValue(IArrowArray array, int rowIndex, PropertyModel property)
    {
        if (property is KeyPropertyModel keyProperty)
        {
            return ReadKeyValue(array, rowIndex, keyProperty.Type);
        }

        if (property is VectorPropertyModel vectorProperty)
        {
            return ReadVectorValue(array, rowIndex, vectorProperty.Type);
        }

        if (property is DataPropertyModel dataProperty)
        {
            return ReadDataValue(array, rowIndex, dataProperty.Type);
        }

        throw new NotSupportedException($"Unsupported property kind '{property.GetType().Name}'.");
    }

    private static object? ReadKeyValue(IArrowArray array, int rowIndex, Type targetType)
    {
        LanceDbDataKind kind = LanceDbDataTypes.Classify(targetType);
        if (kind == LanceDbDataKind.String)
        {
            return ((StringArray)array).GetString(rowIndex);
        }

        if (kind == LanceDbDataKind.Int32)
        {
            return ((Int32Array)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.Int64)
        {
            return ((Int64Array)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.Guid)
        {
            string? s = ((StringArray)array).GetString(rowIndex);
            return s is not null ? Guid.Parse(s) : Guid.Empty;
        }

        throw new NotSupportedException($"Key type '{targetType.Name}' is not supported.");
    }

    private static object? ReadVectorValue(IArrowArray array, int rowIndex, Type targetType)
    {
        FixedSizeListArray listArray = (FixedSizeListArray)array;
        FloatArray floatSlice = (FloatArray)listArray.GetSlicedValues(rowIndex);
        float[] rawFloats = floatSlice.Values.ToArray();

        Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(ReadOnlyMemory<float>))
        {
            return new ReadOnlyMemory<float>(rawFloats);
        }

        if (underlying == typeof(float[]))
        {
            return rawFloats;
        }

        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(Embedding<>) && underlying.GenericTypeArguments[0] == typeof(float))
        {
            return new Embedding<float>(rawFloats);
        }

        // For dynamic records (where targetType might be object)
        return new ReadOnlyMemory<float>(rawFloats);
    }

    private static object? ReadDataValue(IArrowArray array, int rowIndex, Type targetType)
    {
        LanceDbDataKind kind = LanceDbDataTypes.Classify(targetType);

        if (kind == LanceDbDataKind.String)
        {
            return ((StringArray)array).GetString(rowIndex);
        }

        if (kind == LanceDbDataKind.Boolean)
        {
            return ((BooleanArray)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.Int8)
        {
            return ((Int8Array)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.UInt8)
        {
            return ((UInt8Array)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.Int16)
        {
            return ((Int16Array)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.UInt16)
        {
            return ((UInt16Array)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.Int32)
        {
            return ((Int32Array)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.UInt32)
        {
            return ((UInt32Array)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.Int64)
        {
            return ((Int64Array)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.UInt64)
        {
            return ((UInt64Array)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.Float32)
        {
            return ((FloatArray)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.Float64)
        {
            return ((DoubleArray)array).GetValue(rowIndex)!.Value;
        }

        if (kind == LanceDbDataKind.Guid)
        {
            string? s = ((StringArray)array).GetString(rowIndex);
            return s is not null ? Guid.Parse(s) : Guid.Empty;
        }

        if (kind == LanceDbDataKind.DateTime)
        {
            DateTimeOffset? dto = ((TimestampArray)array).GetTimestamp(rowIndex);
            return dto.HasValue ? DateTime.SpecifyKind(dto.Value.DateTime, DateTimeKind.Unspecified) : null;
        }

        if (kind == LanceDbDataKind.DateTimeOffset)
        {
            DateTimeOffset? dto = ((TimestampArray)array).GetTimestamp(rowIndex);
            return dto;
        }

        if (kind == LanceDbDataKind.DateOnly)
        {
            DateTime? dt = ((Date32Array)array).GetDateTime(rowIndex);
            return dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null;
        }

        if (kind == LanceDbDataKind.TimeOnly)
        {
            long? microsecs = ((Time64Array)array).GetValue(rowIndex);
            return microsecs.HasValue ? TimeOnly.FromTimeSpan(TimeSpan.FromMicroseconds(microsecs.Value)) : null;
        }

        if (kind == LanceDbDataKind.StringArray)
        {
            ListArray listArray = (ListArray)array;
            StringArray slice = (StringArray)listArray.GetSlicedValues(rowIndex);
            return Enumerable.Range(0, slice.Length).Select(k => slice.GetString(k)!).ToArray();
        }

        if (kind == LanceDbDataKind.StringList)
        {
            ListArray listArray = (ListArray)array;
            StringArray slice = (StringArray)listArray.GetSlicedValues(rowIndex);
            return Enumerable.Range(0, slice.Length).Select(k => slice.GetString(k)!).ToList();
        }

        throw new NotSupportedException($"Data property type '{targetType.Name}' is not supported.");
    }
}
