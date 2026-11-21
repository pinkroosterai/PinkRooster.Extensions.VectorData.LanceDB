<div align="center">

<img src="assets/logo.png" alt="PinkRooster logo" width="160" />

# PinkRooster.Extensions.VectorData.LanceDB

**Microsoft.Extensions.VectorData provider for local embedded LanceDB in .NET 10.**

[![CI](https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/actions/workflows/ci.yml/badge.svg)](https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/PinkRooster.Extensions.VectorData.LanceDB.svg)](https://www.nuget.org/packages/PinkRooster.Extensions.VectorData.LanceDB)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

[The idea](#the-idea) · [Install](#install) · [Quickstart](#quickstart) · [Options](#options) · [Distance functions and scores](#distance-functions-and-scores) · [Indexes](#indexes) · [Full-text and hybrid search](#full-text-and-hybrid-search) · [Benchmark](#benchmark) · [Supported types](#supported-types) · [Cancellation](#cancellation) · [Conformance tests](#conformance-tests) · [Packages](#packages) · [Samples](#samples) · [Docs](#docs) · [Contributing](#feedback-contributing-security-and-license)

</div>

---

## Status

Preview (`0.1.0-preview.1`), developed against the .NET 10 SDK on Linux, macOS, and Windows.

## The idea

`PinkRooster.Extensions.VectorData.LanceDB` is an embedded vector database provider implementing the standard `Microsoft.Extensions.VectorData` abstractions (`VectorStore` and `VectorStoreCollection`) on top of LanceDB's native columnar storage engine.

It provides serverless, in-process vector storage and similarity search for .NET 10 AI workloads and Semantic Kernel applications without requiring external services, Docker containers, or cloud connections. All data and vector indexes are stored in local file-system directories.

## Install

```text
dotnet add package PinkRooster.Extensions.VectorData.LanceDB --prerelease
```

## Quickstart

<!-- quickstart:start -->
```csharp
string databasePath = Path.Combine(Path.GetTempPath(), "lancedb-hello");
using LanceDbVectorStore store = new(databasePath);
var collection = store.GetCollection<string, Item>("items");
```
<!-- quickstart:end -->

Define your record model with standard `Microsoft.Extensions.VectorData` attributes:

```csharp
public sealed class Item
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
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

LanceDB maps vector distance metrics to normalized similarity scores:

| Distance Function | LanceDB Metric | Distance Range | Score Formula | Score Meaning |
|---|---|---|---|---|
| `DistanceFunction.CosineSimilarity` (default) | Cosine | $[0, 2]$ | $1 - \text{distance}$ | Higher is better (range $[-1, 1]$) |
| `DistanceFunction.CosineDistance` | Cosine | $[0, 2]$ | $\text{distance}$ | Lower is better |
| `DistanceFunction.DotProductSimilarity` | Dot | $(-\infty, \infty)$ | $1 - \text{distance}$ | Higher is better |
| `DistanceFunction.NegativeDotProductSimilarity` | Dot | $(-\infty, \infty)$ | $\text{distance} - 1$ | Lower is better |
| `DistanceFunction.EuclideanSquaredDistance` | L2 | $[0, \infty)$ | $\text{distance}$ | Lower is better |
| `DistanceFunction.EuclideanDistance` | L2 | $[0, \infty)$ | $\sqrt{\max(0, \text{distance})}$ | Lower is better |

## Indexes

Mark a data property `IsIndexed` and the provider creates a scalar index when the collection is created: `BitmapIndex` for `bool`, `LabelListIndex` for string lists, and `BTreeIndex` for every other type. Mark a string property `IsFullTextIndexed` and it also gets a full-text index (`IsFullTextIndexed` on a property that is not a string throws `NotSupportedException`).

Vector properties search by exact scan unless you ask for an index. Declare `IndexKind.IvfFlat` or `IndexKind.Hnsw` on the vector property (`IndexKind.Flat` builds nothing) and the provider builds that index for you once the table reaches `LanceDbCollectionOptions.IndexThreshold` rows (default 100,000), using the property's distance function. Rows added after the build are still found, and a built index is never rebuilt.

```csharp
LanceDbCollectionOptions options = new() { IndexThreshold = 50_000 };
```

The build runs inline, inside the `UpsertAsync` call that takes the table past the threshold, so that one upsert is slow. If the build fails the data is still written, and the build is retried after the next upsert. Vector searches ask LanceDB to refine candidates with exact distances, because the approximate HNSW search lost recall at small limits.

## Full-text and hybrid search

Collections implement `IKeywordHybridSearchable<TRecord>` (also registered by `AddLanceDbCollection`). The full-text property is the one marked `IsFullTextIndexed`; with several, name one in `HybridSearchOptions.AdditionalProperty`, otherwise the call throws `InvalidOperationException` (as it does when there is none).

```csharp
await foreach (var hit in collection.HybridSearchAsync(queryVector, ["QX7731"], top: 3))
{
    Console.WriteLine($"{hit.Record.Id} {hit.Score}");
}
```

The provider runs the vector search and the full-text search separately, each with the filter, fuses the two ranked lists with reciprocal-rank fusion (k = 60), applies `Skip` and `top` to the fused list, and returns the fused score. Several keywords match as OR. An exact identifier in one record's text therefore ranks high even when that record's vector is far from the query. Hybrid search runs on typed and dynamic collections and honours `IncludeVectors`, `ScoreThreshold` and embedding generation of a text query.

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
- `decimal`: excluded due to Rust Arrow FFI 16-byte buffer alignment requirements on 64-bit .NET heap.
- `Half` / Float16 vectors: v1 targets 32-bit floating point vectors only.
- Remote / cloud LanceDB URIs: v1 targets embedded local file-system directories only.

## Cancellation

The native LanceDB engine and Rust FFI worker do not support cancellation of in-flight operations. The provider accepts a `CancellationToken` on all public asynchronous methods and checks `ThrowIfCancellationRequested()` prior to initiating native operations and between batch iterations. Calls with an already-cancelled token return immediately without opening or creating the database on disk.

## Conformance tests

The test suite runs against the official `Microsoft.Extensions.VectorData.ConformanceTests` test suite (252 passed, 3 skipped; the hybrid-search and index-kind classes run too):

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

See [samples/README.md](samples/README.md). The [DataIngestion sample](samples/PinkRooster.Extensions.VectorData.LanceDB.Samples.DataIngestion/README.md) writes a document through a DataIngestion pipeline, re-ingests a changed version, then runs a hybrid search.

## Docs

- [PinkRooster.Extensions.VectorData.LanceDB](docs/PinkRooster.Extensions.VectorData.LanceDB.md)

## Compatibility

.NET 10 SDK; tested on Linux (x64), macOS (ARM64), and Windows (x64).

## Build and test

```text
dotnet build PinkRooster.Extensions.VectorData.LanceDB.slnx -c Release
dotnet test --solution PinkRooster.Extensions.VectorData.LanceDB.slnx -c Release
```

## Feedback, contributing, security and license

Read [CONTRIBUTING.md](CONTRIBUTING.md) first. Report a vulnerability as [SECURITY.md](SECURITY.md) describes. The code is [MIT](LICENSE) licensed.
