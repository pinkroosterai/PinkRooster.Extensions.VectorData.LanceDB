# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0-preview.2] - 2026-11-21

### Changed

- Make the quickstart runnable and clarify hybrid search, indexing, and cancellation behavior.
- Expand XML API documentation for collection schemas, constructor validation, index defaults, hybrid search, and dependency injection.

## [0.1.0-preview.1] - 2026-11-21

### Added

- `LanceDbVectorStore : VectorStore` and `LanceDbCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>` for embedded local file-based LanceDB in .NET 10.
- Dynamic dictionary collection support (`LanceDbDynamicCollection`).
- Vector search with cosine, dot product, and Euclidean (L2) metrics, and exact distance refinement of indexed candidates.
- LINQ filters translated to DataFusion SQL, with SQL literal escaping, null handling, and list operations (`array_has`, `array_has_any`).
- Record operations (`UpsertAsync`, `GetAsync`, `DeleteAsync`) with chunked batching and Arrow columnar serialization.
- Embedding generation on record upsert and search with `IEmbeddingGenerator` resolution from DI or options.
- Dependency injection extensions (`AddLanceDbVectorStore`, `AddKeyedLanceDbVectorStore`, `AddLanceDbCollection`, `AddKeyedLanceDbCollection`) for typed and keyed lifetime management.
- Native SDK escape hatch via `GetService` (`lancedb.Connection`, `lancedb.Table`).
- Collection lifecycle management (`CollectionExistsAsync`, `EnsureCollectionExistsAsync`, `EnsureCollectionDeletedAsync`, `ListCollectionNamesAsync`) with table schema validation.
- Scalar indexes for `IsIndexed` data properties (BTree, Bitmap, LabelList) and full-text indexes for `IsFullTextIndexed` string properties, created with the collection.
- Automatic IVF_FLAT and IVF_HNSW_SQ vector index builds once a collection reaches `LanceDbCollectionOptions.IndexThreshold` rows.
- `IKeywordHybridSearchable<TRecord>` on typed and dynamic collections, with reciprocal-rank fusion (k = 60) of the vector and full-text results, and its dependency injection registration.
- DataIngestion sample (offline pipeline, re-ingestion, hybrid search) and a console benchmark under `benchmarks/`.
- Support for caller-assigned `Guid` keys, including collections created by `Microsoft.Extensions.DataIngestion`'s `VectorStoreWriter`. Auto-generated keys are not supported.

[Unreleased]: https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/compare/v0.1.0-preview.2...main
[0.1.0-preview.2]: https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/compare/v0.1.0-preview.1...v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/pinkroosterai/PinkRooster.Extensions.VectorData.LanceDB/releases/tag/v0.1.0-preview.1
