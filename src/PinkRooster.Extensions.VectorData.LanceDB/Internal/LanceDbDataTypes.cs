namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

internal enum LanceDbDataKind
{
    Unsupported,
    String,
    Boolean,
    Int8,
    UInt8,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float32,
    Float64,
    Guid,
    DateTime,
    DateTimeOffset,
    DateOnly,
    TimeOnly,
    StringArray,
    StringList,
}

internal static class LanceDbDataTypes
{
    private static readonly Dictionary<Type, LanceDbDataKind> Kinds = new()
    {
        [typeof(string)] = LanceDbDataKind.String,
        [typeof(bool)] = LanceDbDataKind.Boolean,
        [typeof(sbyte)] = LanceDbDataKind.Int8,
        [typeof(byte)] = LanceDbDataKind.UInt8,
        [typeof(short)] = LanceDbDataKind.Int16,
        [typeof(ushort)] = LanceDbDataKind.UInt16,
        [typeof(int)] = LanceDbDataKind.Int32,
        [typeof(uint)] = LanceDbDataKind.UInt32,
        [typeof(long)] = LanceDbDataKind.Int64,
        [typeof(ulong)] = LanceDbDataKind.UInt64,
        [typeof(float)] = LanceDbDataKind.Float32,
        [typeof(double)] = LanceDbDataKind.Float64,
        [typeof(Guid)] = LanceDbDataKind.Guid,
        [typeof(DateTime)] = LanceDbDataKind.DateTime,
        [typeof(DateTimeOffset)] = LanceDbDataKind.DateTimeOffset,
        [typeof(DateOnly)] = LanceDbDataKind.DateOnly,
        [typeof(TimeOnly)] = LanceDbDataKind.TimeOnly,
        [typeof(string[])] = LanceDbDataKind.StringArray,
        [typeof(List<string>)] = LanceDbDataKind.StringList,
        [typeof(IEnumerable<string>)] = LanceDbDataKind.StringList,
        [typeof(IReadOnlyList<string>)] = LanceDbDataKind.StringList,
    };

    public static LanceDbDataKind Classify(Type type) => Kinds.GetValueOrDefault(Nullable.GetUnderlyingType(type) ?? type);

    public static bool IsKey(Type type)
        => Nullable.GetUnderlyingType(type) is null
            && Classify(type) is LanceDbDataKind.String or LanceDbDataKind.Guid or LanceDbDataKind.Int32 or LanceDbDataKind.Int64;
}
