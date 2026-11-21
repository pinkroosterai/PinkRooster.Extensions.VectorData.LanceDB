namespace PinkRooster.Extensions.VectorData.LanceDB.Repository.Tests;

internal static class RepositoryRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        const string solution = "PinkRooster.Extensions.VectorData.LanceDB.slnx";
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, solution)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"{solution} not found above the test output folder.");
    }
}
