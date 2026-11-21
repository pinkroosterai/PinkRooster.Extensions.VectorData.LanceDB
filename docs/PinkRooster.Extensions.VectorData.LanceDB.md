![PinkRooster logo](https://raw.githubusercontent.com/pinkroosterai/PinkRooster/5b6d303a7ab57cf6374b210eaf0e36ca9c2ef403/assets/logo-120.png)

# PinkRooster.Extensions.VectorData.LanceDB

**Microsoft.Extensions.VectorData provider for local embedded LanceDB in .NET 10.**

[The idea](#the-idea) · [Install](#install) · [Quickstart](#quickstart) · [Options](#options) · [Distance functions and scores](#distance-functions-and-scores) · [Indexes](#indexes) · [Full-text and hybrid search](#full-text-and-hybrid-search) · [Benchmark](#benchmark) · [Supported types](#supported-types) · [Cancellation](#cancellation) · [Conformance tests](#conformance-tests) · [Packages](#packages) · [Samples](https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/blob/main/samples/README.md) · [Contributing](https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/blob/main/CONTRIBUTING.md)

---

## Status

The first public preview is [`0.1.0-preview.1`](https://www.nuget.org/packages/PinkRooster.Extensions.VectorData.LanceDB/0.1.0-preview.1). See [Compatibility](#compatibility) for the supported platforms.

## The idea

`PinkRooster.Extensions.VectorData.LanceDB` is an embedded vector database provider implementing the standard `Microsoft.Extensions.VectorData` abstractions (`VectorStore` and `VectorStoreCollection`) on top of LanceDB's native columnar storage engine.

It provides in-process vector storage and similarity search for .NET 10 AI workloads and Semantic Kernel applications without requiring external services, Docker containers, or cloud connections. All data and vector indexes are stored in local file-system directories.

## Install

```text
dotnet add package PinkRooster.Extensions.VectorData.LanceDB --prerelease
```

## Quickstart

In a .NET 10 console app, add these imports at the top of `Program.cs`:

```csharp
using Microsoft.Extensions.VectorData;
using PinkRooster.Extensions.VectorData.LanceDB;
```

Get a handle to a typed collection:

```csharp
string databasePath = Path.Combine(Path.GetTempPath(), "lancedb-hello");
using LanceDbVectorStore store = new(databasePath);
var collection = store.GetCollection<string, Item>("items");
```

Create the collection on disk, upsert a record, and search with a four-dimensional vector:

```csharp
await collection.EnsureCollectionExistsAsync();

float[] queryVector = [1f, 0f, 0f, 0f];
await collection.UpsertAsync(new Item { Id = "badge-1", Text = "Lab badge QX7731", Vector = queryVector });

await foreach (var hit in collection.SearchAsync(queryVector, top: 1))
{
    Console.WriteLine($"{hit.Record.Text} ({hit.Score})");
}
```

Place the record model after the top-level statements. `IsFullTextIndexed` also lets this collection run the hybrid-search example below:

```csharp
public sealed class Item
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData(IsFullTextIndexed = true)]
    public string Text { get; set; } = string.Empty;

    [VectorStoreVector(4)]
    public ReadOnlyMemory<float> Vector { get; set; }
}
```

### Dependency Injection

Register the store and collections using standard extension methods:

```csharp
services.AddLanceDbVectorStore("/path/to/data");
services.AddLanceDbCollection<string, Item>("items", "/path/to/data");
```

Keyed registrations and factory overloads accepting `IServiceProvider` are also supported:

```csharp
services.AddKeyedLanceDbVectorStore("my-store", (sp, key) => sp.GetRequiredService<IConfiguration>()["LanceDb:Path"]!);
```

## Options

Configure store and collection behavior using `LanceDbVectorStoreOptions` and `LanceDbCollectionOptions`:

```csharp
LanceDbVectorStoreOptions storeOptions = new()
{
    EmbeddingGenerator = customEmbeddingGenerator, // Default generator for collections
    DefaultCollectionDefinition = customDefinition,
};

LanceDbCollectionOptions collectionOptions = new()
{
    EmbeddingGenerator = customEmbeddingGenerator, // Overrides store generator
    Definition = customDefinition,
};
```

## Distance functions and scores

The provider converts LanceDB distances into scores. Depending on the configured metric, a better match can have a higher or lower score:

| Distance Function | LanceDB Metric | Distance Range | Score Formula | Score Meaning |
|---|---|---|---|---|
| `DistanceFunction.CosineSimilarity` (default) | Cosine | `[0, 2]` | `1 - distance` | Higher is better (range `[-1, 1]`) |
| `DistanceFunction.CosineDistance` | Cosine | `[0, 2]` | `distance` | Lower is better |
| `DistanceFunction.DotProductSimilarity` | Dot | Unbounded | `1 - distance` | Higher is better |
| `DistanceFunction.NegativeDotProductSimilarity` | Dot | Unbounded | `distance - 1` | Lower is better |
| `DistanceFunction.EuclideanSquaredDistance` | L2 | Nonnegative | `distance` | Lower is better |
| `DistanceFunction.EuclideanDistance` | L2 | Nonnegative | `sqrt(max(0, distance))` | Lower is better |

## Indexes

Mark a data property `IsIndexed` and the provider creates a scalar index when the collection is created: `BitmapIndex` for `bool`, `LabelListIndex` for string lists, and `BTreeIndex` for every other type. Mark a string property `IsFullTextIndexed` and it also gets a full-text index (`IsFullTextIndexed` on a property that is not a string throws `NotSupportedException`).

Vector properties search by exact scan unless you ask for an index. Declare `IndexKind.IvfFlat` or `IndexKind.Hnsw` on the vector property (`IndexKind.Flat` builds nothing) and the provider builds that index for you once the table reaches `LanceDbCollectionOptions.IndexThreshold` rows (default 100,000), using the property's distance function. Searches include rows added after the index was built. The provider does not automatically rebuild an existing index.

For a record model `IndexedItem` with an indexed vector property, pass collection options when constructing the collection (or registering it with dependency injection):

```csharp
LanceDbCollectionOptions options = new() { IndexThreshold = 50_000 };
using var indexedCollection = new LanceDbCollection<string, IndexedItem>(databasePath, "indexed-items", options);
```

The build runs inline after data is written in `UpsertAsync`, so that call may take longer. A failed build is retried on a later upsert. Indexed search refines candidates with exact distances, but the candidate set is still approximate.

## Full-text and hybrid search

Collections implement `IKeywordHybridSearchable<TRecord>` (also registered by `AddLanceDbCollection`). The full-text property is the one marked `IsFullTextIndexed`; with several, name one in `HybridSearchOptions.AdditionalProperty`, otherwise the call throws `InvalidOperationException` (as it does when there is none).

`GetCollection` returns the base collection type. Use its hybrid-search interface to run the query below after the quickstart upsert (before the `Item` declaration):

```csharp
IKeywordHybridSearchable<Item> hybrid = (IKeywordHybridSearchable<Item>)collection;
await foreach (var hit in hybrid.HybridSearchAsync(queryVector, ["QX7731"], top: 3))
{
    Console.WriteLine($"{hit.Record.Id} {hit.Score}");
}
```

The provider runs the vector search and the full-text search separately, each with the filter, fuses the two ranked lists with reciprocal-rank fusion (k = 60), applies `Skip` and `top` to the fused list, and returns the fused score. Multiple keywords match with OR semantics. A full-text match can contribute even if the record is absent from the vector results. Hybrid search runs on typed and dynamic collections and supports `IncludeVectors`, a `ScoreThreshold` on the fused score, and embedding generation for a text query.

## Benchmark

```text
dotnet run -c Release --project benchmarks/PinkRooster.Extensions.VectorData.LanceDB.Benchmarks
```

The command seeds 10,000 records of 768 dimensions into a temporary directory and prints the upsert time and the latency of a filtered top-10 search. It is not part of `dotnet test`. One local run (no vector index, so an exact scan), on Linux x64 with an AMD Ryzen 7 8845HS (16 logical cores), .NET 10.0.15:

| Measure | Result |
|---|---|
| Upsert of 10,000 records × 768 dimensions | 553 ms |
| Filtered top-10 search, 30 runs | median 25.7 ms, p95 238.8 ms |

## Supported types

### Key types
- `string`
- `int`
- `long`
- `Guid`

### Data types
- Primitives: `string`, `bool`, `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`
- Identifiers and dates: `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`
- Collections: `string[]`, `List<string>`, `IEnumerable<string>`, `IReadOnlyList<string>`
- Nullable value types (e.g. `int?`, `bool?`, `Guid?`)

### Vector types
- `ReadOnlyMemory<float>`
- `float[]`
- `Embedding<float>`

### Unsupported types and settings
- `decimal` data properties.
- `Half` / Float16 vectors: v1 targets 32-bit floating point vectors only.
- Remote / cloud LanceDB URIs: v1 targets embedded local file-system directories only.

## Cancellation

The provider checks cancellation before starting database work and at checkpoints between operations and batches. It cannot interrupt a native operation already in progress. Cancellation is not a rollback: writes that have already completed remain in the database.

## Conformance tests

The repository runs the upstream `Microsoft.Extensions.VectorData.ConformanceTests` suite, including the hybrid-search and index-kind tests. These three cases are skipped:

| Skipped Test | Reason |
|---|---|
| `LanceDbDistanceFunctionTests.HammingDistance` | HammingDistance is not supported by the LanceDB provider. |
| `LanceDbDistanceFunctionTests.ManhattanDistance` | ManhattanDistance is not supported by the LanceDB provider. |
| `LanceDbDataTypeTests.TimeOnly` | TimeOnly filtering is not supported by the LanceDB / DataFusion query engine. |

## Packages

| Package | What it adds | Dependencies |
|---|---|---|
| [`PinkRooster.Extensions.VectorData.LanceDB`](https://www.nuget.org/packages/PinkRooster.Extensions.VectorData.LanceDB) | Microsoft.Extensions.VectorData provider for local embedded LanceDB in .NET 10. | `LanceDB`, `Microsoft.Extensions.VectorData.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions` |

## Samples

See [samples](https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/blob/main/samples/README.md). The [DataIngestion sample](https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/blob/main/samples/PinkRooster.Extensions.VectorData.LanceDB.Samples.DataIngestion/README.md) writes a document through a DataIngestion pipeline, re-ingests a changed version, then runs a hybrid search.

## Compatibility

Requires .NET 10. The package targets Linux x64, macOS ARM64, and Windows x64; CI tests all three.

## Feedback, contributing, security and license

Read [CONTRIBUTING.md](https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/blob/main/CONTRIBUTING.md) first. Report a vulnerability as [SECURITY.md](https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/blob/main/SECURITY.md) describes. The code is [MIT](https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/blob/main/LICENSE) licensed.
