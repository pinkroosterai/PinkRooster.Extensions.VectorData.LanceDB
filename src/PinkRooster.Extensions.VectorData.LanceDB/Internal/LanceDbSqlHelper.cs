using System.Globalization;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

internal static class LanceDbSqlHelper
{
    public static string EscapeIdentifier(string identifier) =>
        $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    public static string FormatKeyLiteral(object key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key switch
        {
            string s => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'",
            Guid g => "'" + g.ToString("D") + "'",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException($"Key type '{key.GetType().Name}' is not supported.")
        };
    }

    public static string BuildKeyFilter(string keyStorageName, IReadOnlyList<object> keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyStorageName);
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            throw new ArgumentException("Keys list must not be empty.", nameof(keys));
        }

        string escaped = EscapeIdentifier(keyStorageName);
        if (keys.Count == 1)
        {
            return $"{escaped} = {FormatKeyLiteral(keys[0])}";
        }

        string inList = string.Join(", ", keys.Select(FormatKeyLiteral));
        return $"{escaped} IN ({inList})";
    }
}
