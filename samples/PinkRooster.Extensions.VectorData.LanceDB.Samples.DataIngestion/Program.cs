using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB.Samples.DataIngestion;

public static class Program
{
    private const string CollectionName = "chunks";
    private const int Dimensions = 64;

    /// <summary>What the sample found after ingesting a document twice.</summary>
    /// <param name="Chunks">The text of every chunk the collection holds.</param>
    /// <param name="TopHit">The text of the best match for the hybrid search.</param>
    public sealed record Result(IReadOnlyList<string> Chunks, string TopHit);

    public static async Task Main()
    {
        string databaseDirectory = Path.Combine(Path.GetTempPath(), "lancedb-dataingestion-" + Guid.NewGuid().ToString("N"));
        try
        {
            Result result = await RunAsync(databaseDirectory);
            Console.WriteLine($"Chunks now in the collection: {result.Chunks.Count}");
            foreach (string chunk in result.Chunks)
            {
                Console.WriteLine($"  {chunk}");
            }

            Console.WriteLine($"Top hybrid hit: {result.TopHit}");
        }
        finally
        {
            DeleteDirectory(databaseDirectory);
        }
    }

    public static async Task<Result> RunAsync(string databaseDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(databaseDirectory);
        if (Directory.EnumerateFileSystemEntries(databaseDirectory).Any())
        {
            throw new ArgumentException("The sample requires an empty scratch database directory.", nameof(databaseDirectory));
        }

        DirectoryInfo documentDirectory = Directory.CreateTempSubdirectory("lancedb-handbook-");
        try
        {
            string documentPath = Path.Combine(documentDirectory.FullName, "handbook.txt");
            // The writer creates its collection with no full-text index, so the sample creates the collection first.
            using (LanceDbVectorStore setupStore = CreateStore(databaseDirectory))
            {
                using var collection = setupStore.GetDynamicCollection(CollectionName, CreateDefinition());
                await collection.EnsureCollectionExistsAsync(cancellationToken);
            }

            await File.WriteAllTextAsync(
                documentPath,
                "Employees accrue vacation days monthly.\n\nExpense reports are due on the fifth of each month.\n\nThe old badge policy requires a photo ID.",
                cancellationToken);
            await IngestAsync(databaseDirectory, documentPath, cancellationToken);

            // The document changes: the badge paragraph is replaced.
            await File.WriteAllTextAsync(
                documentPath,
                "Employees accrue vacation days monthly.\n\nExpense reports are due on the fifth of each month.\n\nBadge code QX7731 opens the lab door.",
                cancellationToken);
            await IngestAsync(databaseDirectory, documentPath, cancellationToken);
        }
        finally
        {
            DeleteDirectory(documentDirectory.FullName);
        }

        using LanceDbVectorStore store = CreateStore(databaseDirectory);
        using var chunks = store.GetDynamicCollection(CollectionName, CreateDefinition());

        List<string> texts = [];
        await foreach (var record in chunks.GetAsync(r => (string)r["documentid"]! != string.Empty, top: 100, cancellationToken: cancellationToken))
        {
            texts.Add((string)record["content"]!);
        }

        string topHit = string.Empty;
        var hybrid = (IKeywordHybridSearchable<Dictionary<string, object?>>)chunks;
        await foreach (var hit in hybrid.HybridSearchAsync("which badge code opens the lab", ["QX7731"], top: 1, cancellationToken: cancellationToken))
        {
            topHit = (string)hit.Record["content"]!;
        }

        return new Result(texts, topHit);
    }

    private static async Task IngestAsync(string databaseDirectory, string documentPath, CancellationToken cancellationToken)
    {
        // The writer disposes the store it is given, so each ingestion opens its own.
        using IngestionPipeline<string> pipeline = new(
            new TextFileReader(),
            new ParagraphChunker(),
            new VectorStoreWriter<string>(CreateStore(databaseDirectory), Dimensions, new VectorStoreWriterOptions { CollectionName = CollectionName }));

        await foreach (IngestionResult result in pipeline.ProcessAsync([new FileInfo(documentPath)], cancellationToken))
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Ingestion failed.", result.Exception);
            }
        }
    }

    private static LanceDbVectorStore CreateStore(string databaseDirectory) =>
        new(databaseDirectory, new LanceDbVectorStoreOptions { EmbeddingGenerator = new HashingEmbeddingGenerator(Dimensions) });

    // The writer's schema, plus a full-text index on the content.
    private static VectorStoreCollectionDefinition CreateDefinition() => new()
    {
        Properties =
        {
            new VectorStoreKeyProperty("key", typeof(Guid)),
            new VectorStoreVectorProperty("embedding", typeof(string), Dimensions)
            {
                DistanceFunction = DistanceFunction.CosineDistance,
                IndexKind = IndexKind.Flat,
            },
            new VectorStoreDataProperty("content", typeof(string)) { IsFullTextIndexed = true },
            new VectorStoreDataProperty("context", typeof(string)),
            new VectorStoreDataProperty("documentid", typeof(string)) { IsIndexed = true },
        },
    };

    private static void DeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
