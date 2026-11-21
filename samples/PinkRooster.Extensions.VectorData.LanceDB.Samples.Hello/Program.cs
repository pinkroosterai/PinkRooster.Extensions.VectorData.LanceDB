using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB.Samples.Hello;

public static class Program
{
    public sealed class Item
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;
    }

    public static async Task Main() => Console.WriteLine(await RunAsync());

    public static Task<string> RunAsync()
    {
        // quickstart:start
        string databasePath = Path.Combine(Path.GetTempPath(), "lancedb-hello");
        using LanceDbVectorStore store = new(databasePath);
        var collection = store.GetCollection<string, Item>("items");
        // quickstart:end
        return Task.FromResult(collection.Name);
    }
}
