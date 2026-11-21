using System.Text.RegularExpressions;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

internal static partial class LanceDbNameValidator
{
    [GeneratedRegex(@"^[a-zA-Z0-9_.-]+$", RegexOptions.Compiled)]
    private static partial Regex ValidTableNameRegex();

    public static void Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!ValidTableNameRegex().IsMatch(name))
        {
            throw new ArgumentException(
                $"Collection name '{name}' contains invalid characters. LanceDB table names can only contain alphanumeric characters, underscores, hyphens, and periods (^[a-zA-Z0-9_.-]+$).",
                nameof(name));
        }
    }
}
