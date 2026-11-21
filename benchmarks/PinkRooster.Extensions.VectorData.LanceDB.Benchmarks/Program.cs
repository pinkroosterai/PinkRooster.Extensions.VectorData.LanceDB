using System.Diagnostics;
using Microsoft.Extensions.VectorData;
using PinkRooster.Extensions.VectorData.LanceDB;

// Seeds a temporary database, then reports the upsert time and filtered vector search latency.
// It is not part of `dotnet test`.
const int RecordCount = 10_000;
const int SearchRuns = 30;
const int Top = 10;

string directory = Path.Combine(Path.GetTempPath(), "lancedb-benchmark-" + Guid.NewGuid().ToString("N"));
try
{
    Random random = new(42);
    float[] NextVector()
    {
        float[] vector = new float[Chunk.Dimensions];
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2 - 1);
        }

        return vector;
    }

    List<Chunk> records = [.. Enumerable.Range(0, RecordCount).Select(i => new Chunk
    {
        Id = $"chunk-{i}",
        Category = i % 10,
        Embedding = NextVector(),
    })];

    using LanceDbVectorStore store = new(directory);
    using var collection = store.GetCollection<string, Chunk>("chunks");
    await collection.EnsureCollectionExistsAsync();

    Stopwatch stopwatch = Stopwatch.StartNew();
    await collection.UpsertAsync(records);
    stopwatch.Stop();
    Console.WriteLine($"Upsert of {RecordCount} records x {Chunk.Dimensions} dimensions: {stopwatch.ElapsedMilliseconds} ms");

    float[] query = NextVector();
    VectorSearchOptions<Chunk> options = new() { Filter = c => c.Category == 3 };

    async Task<int> SearchAsync()
    {
        int hits = 0;
        await foreach (var _ in collection.SearchAsync(query, top: Top, options))
        {
            hits++;
        }

        return hits;
    }

    static void ValidateHits(int hits)
    {
        if (hits != Top)
        {
            throw new InvalidOperationException($"Expected {Top} hits, got {hits}.");
        }
    }

    // One warm-up search, then timed runs.
    ValidateHits(await SearchAsync());
    double[] timings = new double[SearchRuns];
    for (int run = 0; run < timings.Length; run++)
    {
        stopwatch.Restart();
        int hits = await SearchAsync();
        stopwatch.Stop();
        timings[run] = stopwatch.Elapsed.TotalMilliseconds;
        ValidateHits(hits);
    }

    Array.Sort(timings);
    double median = (timings[(timings.Length - 1) / 2] + timings[timings.Length / 2]) / 2;
    double p95 = timings[(int)Math.Ceiling(timings.Length * 0.95) - 1];
    Console.WriteLine($"Filtered top-{Top} search over {timings.Length} runs: median {median:F1} ms, p95 {p95:F1} ms, max {timings[^1]:F1} ms");
    Console.WriteLine($"Machine: {Environment.OSVersion}, {Environment.ProcessorCount} logical cores, .NET {Environment.Version}");
}
finally
{
    try
    {
        Directory.Delete(directory, recursive: true);
    }
    catch (IOException)
    {
    }
}

internal sealed class Chunk
{
    public const int Dimensions = 768;

    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData(IsIndexed = true)]
    public int Category { get; set; }

    [VectorStoreVector(Dimensions)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
