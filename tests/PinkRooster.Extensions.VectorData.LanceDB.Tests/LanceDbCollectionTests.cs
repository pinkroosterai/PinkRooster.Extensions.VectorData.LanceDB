using System.Globalization;
using System.Linq.Expressions;
using AwesomeAssertions;
using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB.Tests;

public sealed class LanceDbCollectionTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly LanceDbVectorStore _store;

    public LanceDbCollectionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "lancedb-coll-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _store = new LanceDbVectorStore(_tempDirectory);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class ValidRecordOriginal
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public int Age { get; set; }
    }

    private sealed class MismatchedTypeRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public string Age { get; set; } = string.Empty;
    }

    private sealed class MissingColumnRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public int Age { get; set; }

        [VectorStoreData]
        public string Description { get; set; } = string.Empty;
    }

    private sealed class UnsupportedDistanceFunctionRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(4, DistanceFunction = DistanceFunction.HammingDistance)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    private sealed class UnsupportedHalfRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(4)]
        public ReadOnlyMemory<Half> Vector { get; set; }
    }

    private sealed class HnswRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(16, IndexKind = IndexKind.Hnsw, DistanceFunction = DistanceFunction.CosineDistance)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    // Rows 0-2 sit close to the query direction (e0); every other row has a negative e0 component and is random elsewhere.
    private const int NearHnswCount = 120;

    private static List<HnswRecord> MakeHnsw(int from, int count)
    {
        List<HnswRecord> rows = [];
        for (int i = from; i < from + count; i++)
        {
            Random rng = new(1000 + i);
            float[] v = new float[16];
            bool near = i < NearHnswCount;
            for (int d = 0; d < v.Length; d++)
            {
                v[d] = (float)(rng.NextDouble() - 0.5) * (near ? 0.1f : 1f);
            }

            v[0] = near ? 1f : -1f - (float)rng.NextDouble();
            rows.Add(new HnswRecord { Id = $"k{i}", Vector = v });
        }

        return rows;
    }

    private static readonly float[] QueryVector = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];

    private static async Task<string[]> Top3Async(VectorStoreCollection<string, HnswRecord> c, CancellationToken ct)
    {
        List<string> ids = [];
        await foreach (var r in c.SearchAsync(QueryVector, top: 3, cancellationToken: ct))
        {
            ids.Add(r.Record.Id);
        }

        return [.. ids];
    }

    private sealed class TextRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public string? Text { get; set; }

        [VectorStoreVector(2)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    private sealed class IndexedRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(IsIndexed = true)]
        public int Age { get; set; }

        [VectorStoreData(IsIndexed = true)]
        public bool IsActive { get; set; }

        [VectorStoreData(IsIndexed = true)]
        public string[] Tags { get; set; } = [];

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Content { get; set; } = string.Empty;

        [VectorStoreData(IsIndexed = true, IsFullTextIndexed = true)]
        public string Title { get; set; } = string.Empty;

        [VectorStoreVector(2)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    private sealed class InvalidFtsRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(IsFullTextIndexed = true)]
        public int Number { get; set; }
    }

    [Fact]
    public async Task EnsureCollectionExistsAsync_ThrowsVectorStoreException_WhenColumnTypeMismatched()
    {
        string collectionName = "mismatch_test";
        VectorStoreCollection<string, ValidRecordOriginal> originalCollection = _store.GetCollection<string, ValidRecordOriginal>(collectionName);
        await originalCollection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        VectorStoreCollection<string, MismatchedTypeRecord> mismatchedCollection = _store.GetCollection<string, MismatchedTypeRecord>(collectionName);
        Func<Task> act = async () => await mismatchedCollection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        VectorStoreException ex = (await act.Should().ThrowAsync<VectorStoreException>()).Which;
        ex.Message.Should().Contain("Age");
        ex.VectorStoreSystemName.Should().Be("LanceDB");
        ex.CollectionName.Should().Be(collectionName);
    }





    [Fact]
    public async Task EnsureCollectionExistsAsync_CreatesScalarAndFullTextIndexes_ForTypedCollection()
    {
        string collectionName = "typed_idx_test";
        VectorStoreCollection<string, IndexedRecord> collection = _store.GetCollection<string, IndexedRecord>(collectionName);
        await collection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        using var table = collection.GetService(typeof(lancedb.Table)) as lancedb.Table;
        table.Should().NotBeNull();

        var indices = await table!.ListIndices();
        var indexMap = indices.ToDictionary(i => i.Name);

        indexMap.Should().ContainKey("Age_idx");
        indexMap["Age_idx"].IndexType.ToString().Should().Be("BTree");
        indexMap["Age_idx"].Columns.Should().Equal(["Age"]);

        indexMap.Should().ContainKey("IsActive_idx");
        indexMap["IsActive_idx"].IndexType.ToString().Should().Be("Bitmap");
        indexMap["IsActive_idx"].Columns.Should().Equal(["IsActive"]);

        indexMap.Should().ContainKey("Tags_idx");
        indexMap["Tags_idx"].IndexType.ToString().Should().Be("LabelList");
        indexMap["Tags_idx"].Columns.Should().Equal(["Tags"]);

        indexMap.Should().ContainKey("Content_fts_idx");
        indexMap["Content_fts_idx"].IndexType.ToString().Should().Be("FTS");
        indexMap["Content_fts_idx"].Columns.Should().Equal(["Content"]);

        indexMap.Should().ContainKey("Title_idx");
        indexMap["Title_idx"].IndexType.ToString().Should().Be("BTree");
        indexMap["Title_idx"].Columns.Should().Equal(["Title"]);

        indexMap.Should().ContainKey("Title_fts_idx");
        indexMap["Title_fts_idx"].IndexType.ToString().Should().Be("FTS");
        indexMap["Title_fts_idx"].Columns.Should().Equal(["Title"]);
    }

    [Fact]
    public async Task EnsureCollectionExistsAsync_CreatesScalarAndFullTextIndexes_ForDynamicCollection()
    {
        string collectionName = "dynamic_idx_test";
        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty("id", typeof(string)),
                new VectorStoreDataProperty("score", typeof(int)) { IsIndexed = true },
                new VectorStoreDataProperty("body", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreVectorProperty("vec", typeof(ReadOnlyMemory<float>), 2),
            ],
        };

        var collection = _store.GetDynamicCollection(collectionName, definition);
        await collection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        using var table = collection.GetService(typeof(lancedb.Table)) as lancedb.Table;
        table.Should().NotBeNull();

        var indices = await table!.ListIndices();
        var indexMap = indices.ToDictionary(i => i.Name);

        indexMap.Should().ContainKey("score_idx");
        indexMap["score_idx"].IndexType.ToString().Should().Be("BTree");
        indexMap["score_idx"].Columns.Should().Equal(["score"]);

        indexMap.Should().ContainKey("body_fts_idx");
        indexMap["body_fts_idx"].IndexType.ToString().Should().Be("FTS");
        indexMap["body_fts_idx"].Columns.Should().Equal(["body"]);
    }

    [Theory]
    [InlineData("typed")]
    [InlineData("dynamic")]
    [InlineData("store")]
    public async Task EnsureCollectionDeletedAsync_PreservesTable_WhenColdConnectionCancels(string entryPoint)
    {
        const string name = "canceled_drop";
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var seed = _store.GetCollection<string, ValidRecordOriginal>(name);
        await seed.EnsureCollectionExistsAsync(ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var coldStore = new LanceDbVectorStore(_tempDirectory, options: null, async (connection, directory) =>
        {
            await connection.Connect(directory);
            cancellation.Cancel();
        });
        using var typed = coldStore.GetCollection<string, ValidRecordOriginal>(name);
        using var dynamic = coldStore.GetDynamicCollection(name, new VectorStoreCollectionDefinition
        {
            Properties = [new VectorStoreKeyProperty("Id", typeof(string))],
        });
        Func<Task> delete = entryPoint switch
        {
            "typed" => () => typed.EnsureCollectionDeletedAsync(cancellation.Token),
            "dynamic" => () => dynamic.EnsureCollectionDeletedAsync(cancellation.Token),
            _ => () => coldStore.EnsureCollectionDeletedAsync(name, cancellation.Token),
        };

        (await delete.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        (await seed.CollectionExistsAsync(ct)).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureCollectionExistsAsync_RestoresMissingIndexes_WithoutReplacingExistingIndexes(bool removeAll)
    {
        const string name = "recover_indexes";
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var typed = _store.GetCollection<string, RecoverableRecord>(name);
        await typed.EnsureCollectionExistsAsync(ct);
        await typed.UpsertAsync(new RecoverableRecord { Id = "a", Age = 42, Content = "searchable" }, ct);

        string? originalAgeIndex;
        using (var table = (lancedb.Table)typed.GetService(typeof(lancedb.Table))!)
        {
            originalAgeIndex = (await table.ListIndices()).Single(i => i.Name == "Age_idx").IndexUuid;
            originalAgeIndex.Should().NotBeNullOrEmpty();
            await table.DropIndex("Content_fts_idx");
            if (removeAll)
            {
                await table.DropIndex("Age_idx");
            }
        }

        using var peer = new LanceDbVectorStore(_tempDirectory);
        using var dynamic = peer.GetDynamicCollection(name, new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("Age", typeof(int)) { IsIndexed = true },
                new VectorStoreDataProperty("Content", typeof(string)) { IsFullTextIndexed = true },
            ],
        });
        await Task.WhenAll(typed.EnsureCollectionExistsAsync(ct), dynamic.EnsureCollectionExistsAsync(ct));

        string? recoveredAgeIndex;
        string? recoveredContentIndex;
        using (var table = (lancedb.Table)typed.GetService(typeof(lancedb.Table))!)
        {
            var indexes = (await table.ListIndices()).ToDictionary(i => i.Name);
            indexes.Keys.Should().BeEquivalentTo(["Age_idx", "Content_fts_idx"]);
            recoveredAgeIndex = indexes["Age_idx"].IndexUuid;
            recoveredContentIndex = indexes["Content_fts_idx"].IndexUuid;
            recoveredAgeIndex.Should().NotBeNullOrEmpty();
            recoveredContentIndex.Should().NotBeNullOrEmpty();
            if (!removeAll)
            {
                recoveredAgeIndex.Should().Be(originalAgeIndex);
            }
        }

        await typed.EnsureCollectionExistsAsync(ct);
        await dynamic.EnsureCollectionExistsAsync(ct);
        using var latest = (lancedb.Table)typed.GetService(typeof(lancedb.Table))!;
        var repeated = (await latest.ListIndices()).ToDictionary(i => i.Name, i => i.IndexUuid);
        repeated["Age_idx"].Should().Be(recoveredAgeIndex);
        repeated["Content_fts_idx"].Should().Be(recoveredContentIndex);
        (await typed.GetAsync("a", cancellationToken: ct)).Should().BeEquivalentTo(
            new RecoverableRecord { Id = "a", Age = 42, Content = "searchable" });
    }

    private sealed class RecoverableRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(IsIndexed = true)]
        public int Age { get; set; }

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Content { get; set; } = string.Empty;
    }

    [Fact]
    public void GetCollection_ThrowsNotSupportedException_WhenNonStringPropertyIsFullTextIndexed()
    {
        Action actTyped = () => _store.GetCollection<string, InvalidFtsRecord>("invalid_fts_typed");
        actTyped.Should().Throw<NotSupportedException>().WithMessage("*Full-text indexing is only supported on string properties*");

        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty("id", typeof(string)),
                new VectorStoreDataProperty("number", typeof(int)) { IsFullTextIndexed = true },
            ],
        };
        Action actDynamic = () => _store.GetDynamicCollection("invalid_fts_dynamic", definition);
        actDynamic.Should().Throw<NotSupportedException>().WithMessage("*Full-text indexing is only supported on string properties*");
    }

    [Fact]
    public async Task EnsureCollectionExistsAsync_ThrowsVectorStoreException_WhenColumnMissingInDatabase()
    {
        string collectionName = "missing_col_test";
        VectorStoreCollection<string, ValidRecordOriginal> originalCollection = _store.GetCollection<string, ValidRecordOriginal>(collectionName);
        await originalCollection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        VectorStoreCollection<string, MissingColumnRecord> missingColCollection = _store.GetCollection<string, MissingColumnRecord>(collectionName);
        Func<Task> act = async () => await missingColCollection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        VectorStoreException ex = (await act.Should().ThrowAsync<VectorStoreException>()).Which;
        ex.Message.Should().Contain("Description");
        ex.VectorStoreSystemName.Should().Be("LanceDB");
        ex.CollectionName.Should().Be(collectionName);
    }

    [Fact]
    public async Task VectorSearch_FindsRowsAddedAfterIndexBuilt()
    {
        string collectionName = "vector_after_index_test";
        var collection = _store.GetCollection<string, TextRecord>(collectionName);
        await collection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        // Upsert 15 records
        var initialRecords = Enumerable.Range(0, 15).Select(i => new TextRecord
        {
            Id = $"k{i}",
            Text = $"initial {i}",
            Vector = new float[] { 1f, 0f },
        }).ToList();
        await collection.UpsertAsync(initialRecords, cancellationToken: TestContext.Current.CancellationToken);

        // Build HNSW index via escape hatch
        using var table = collection.GetService(typeof(lancedb.Table)) as lancedb.Table;
        table.Should().NotBeNull();
        await table!.CreateIndex(["Vector"], new lancedb.HnswSqIndex { DistanceType = lancedb.DistanceType.Cosine });

        // Upsert row 16 with vector [0, 1]
        var newRecord = new TextRecord
        {
            Id = "k15-new",
            Text = "new record after index",
            Vector = new float[] { 0f, 1f },
        };
        await collection.UpsertAsync([newRecord], cancellationToken: TestContext.Current.CancellationToken);

        // Search for [0, 1]
        List<VectorSearchResult<TextRecord>> searchResults = [];
        await foreach (var item in collection.SearchAsync(new float[] { 0f, 1f }, top: 1, cancellationToken: TestContext.Current.CancellationToken))
        {
            searchResults.Add(item);
        }
        searchResults.Should().HaveCount(1);
        searchResults[0].Record.Id.Should().Be("k15-new");
    }

    [Fact]
    public async Task Upsert_BuildsHnswIndexOnceThresholdReached_AndFindsNearNeighbors()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var collection = new LanceDbCollection<string, HnswRecord>(_tempDirectory, "hnsw_threshold_test", new LanceDbCollectionOptions { IndexThreshold = 500 });
        await collection.EnsureCollectionExistsAsync(ct);
        using var table = (lancedb.Table)collection.GetService(typeof(lancedb.Table))!;

        await collection.UpsertAsync(MakeHnsw(0, 300), ct);
        (await table.ListIndices()).Should().NotContain(i => i.Columns.Contains("Vector"));
        string[] before = await Top3Async(collection, ct);

        await collection.UpsertAsync(MakeHnsw(300, 300), ct);
        using var latest = (lancedb.Table)collection.GetService(typeof(lancedb.Table))!;
        (await latest.ListIndices()).Should().Contain(i => i.Columns.Contains("Vector"));
        string[] after = await Top3Async(collection, ct);

        // Approximate search need not return the same three IDs as the exact scan.
        HashSet<string> nearIds = Enumerable.Range(0, NearHnswCount).Select(i => $"k{i}").ToHashSet(StringComparer.Ordinal);
        before.Should().HaveCount(3).And.OnlyContain(id => nearIds.Contains(id));
        after.Should().HaveCount(3).And.OnlyContain(id => nearIds.Contains(id));
    }

    [Fact]
    public async Task Upsert_WhenIndexBuildFails_StillWritesData()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var collection = new LanceDbCollection<string, HnswRecord>(_tempDirectory, "hnsw_failed_build_test", new LanceDbCollectionOptions { IndexThreshold = 1 });
        await collection.EnsureCollectionExistsAsync(ct);

        // Zero vectors cannot be indexed under cosine distance on a single row; the build may fail.
        Func<Task> act = () => collection.UpsertAsync([new HnswRecord { Id = "a", Vector = new float[16] }], ct);
        await act.Should().NotThrowAsync();

        HnswRecord? stored = await collection.GetAsync("a", cancellationToken: ct);
        stored.Should().NotBeNull();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    [InlineData("has:colon")]
    [InlineData("has?question")]
    [InlineData("has*asterisk")]
    public async Task CollectionName_ThrowsArgumentException_WhenNameContainsInvalidCharacters(string invalidName)
    {
        Action act1 = () => _store.GetCollection<string, ValidRecordOriginal>(invalidName);
        act1.Should().Throw<ArgumentException>();

        Func<Task> act2 = async () => await _store.CollectionExistsAsync(invalidName, TestContext.Current.CancellationToken);
        await act2.Should().ThrowAsync<ArgumentException>();

        Func<Task> act3 = async () => await _store.EnsureCollectionDeletedAsync(invalidName, TestContext.Current.CancellationToken);
        await act3.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void GetCollection_ThrowsNotSupportedException_ForUnsupportedDistanceFunction()
    {
        Action act = () => _store.GetCollection<string, UnsupportedDistanceFunctionRecord>("unsupported_dist");
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void GetCollection_ThrowsNotSupportedException_ForHalfPrecisionVectors()
    {
        Action act = () => _store.GetCollection<string, UnsupportedHalfRecord>("unsupported_half");
        act.Should().Throw<NotSupportedException>();
    }

    private const string EscapingFilterText = "' OR '1'='1";

    private static Expression<Func<TextRecord, bool>> EscapingFilter => r => r.Text == EscapingFilterText;

    private static TextRecord[] CreateEscapingFilterRecords() =>
    [
        new() { Id = "1", Text = EscapingFilterText, Vector = new float[] { 1f, 0f } },
        new() { Id = "2", Text = "innocent data", Vector = new float[] { 0f, 1f } },
    ];

    [Fact]
    public async Task Filter_EscapesMaliciousPayload_PreventingSqlInjection()
    {
        string collectionName = "sql_injection_filter_test";
        using VectorStoreCollection<string, TextRecord> collection = _store.GetCollection<string, TextRecord>(collectionName);
        await collection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        await collection.UpsertAsync(CreateEscapingFilterRecords(), TestContext.Current.CancellationToken);

        List<TextRecord> results = [];
        await foreach (TextRecord item in collection.GetAsync(EscapingFilter, top: 10, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(item);
        }

        results.Should().HaveCount(1);
        results[0].Id.Should().Be("1");
        results[0].Text.Should().Be(EscapingFilterText);
    }

    [Fact]
    public async Task SearchAsync_Filter_EscapesMaliciousPayload_PreventingSqlInjection()
    {
        string collectionName = "sql_injection_search_test";
        using VectorStoreCollection<string, TextRecord> collection = _store.GetCollection<string, TextRecord>(collectionName);
        await collection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        await collection.UpsertAsync(CreateEscapingFilterRecords(), TestContext.Current.CancellationToken);

        VectorSearchOptions<TextRecord> options = new()
        {
            Filter = EscapingFilter
        };

        List<VectorSearchResult<TextRecord>> results = [];
        await foreach (VectorSearchResult<TextRecord> item in collection.SearchAsync(new float[] { 1f, 0f }, top: 10, options: options, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(item);
        }

        results.Should().HaveCount(1);
        results[0].Record.Id.Should().Be("1");
    }

    [Fact]
    public async Task Filter_NotEqual_WithNullInDatabase()
    {
        string collectionName = "test_null_filter";
        VectorStoreCollection<string, TextRecord> collection = _store.GetCollection<string, TextRecord>(collectionName);
        await collection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        TextRecord r1 = new() { Id = "1", Text = "a", Vector = new float[] { 1f, 0f } };
        TextRecord r2 = new() { Id = "2", Text = "b", Vector = new float[] { 0f, 1f } };
        TextRecord r3 = new() { Id = "3", Text = "c", Vector = new float[] { 1f, 1f } };
        TextRecord r4 = new() { Id = "4", Text = null, Vector = new float[] { 0f, 0f } };

        await collection.UpsertAsync([r1, r2, r3, r4], TestContext.Current.CancellationToken);

        Expression<Func<TextRecord, bool>> filter = r => r.Text != "a";
        List<TextRecord> results = [];
        await foreach (TextRecord item in collection.GetAsync(filter, top: 10, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(item);
        }

        results.Should().HaveCount(3);
        results.Select(r => r.Id).Should().Contain(["2", "3", "4"]);
    }

    [Theory]
    [InlineData(null, "nearby")]
    [InlineData(DistanceFunction.CosineSimilarity, "aligned")]
    public async Task DynamicSearch_UsesExplicitMetricBeforeCollectionDefault(string? propertyMetric, string expected)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var collection = new LanceDbDynamicCollection(_tempDirectory, "dynamic_metric", new LanceDbCollectionOptions
        {
            DefaultDistanceFunction = DistanceFunction.EuclideanSquaredDistance,
            Definition = new VectorStoreCollectionDefinition
            {
                Properties =
                [
                    new VectorStoreKeyProperty("Id", typeof(string)),
                    new VectorStoreVectorProperty("Vector", typeof(float[]), 2) { DistanceFunction = propertyMetric },
                ],
            },
        });
        await collection.EnsureCollectionExistsAsync(ct);
        await collection.UpsertAsync(
            [new() { ["Id"] = "aligned", ["Vector"] = new float[] { 3, 0 } },
             new() { ["Id"] = "nearby", ["Vector"] = new float[] { 1, 1 } }], ct);
        var result = await collection.SearchAsync(new float[] { 1, 0 }, 1, cancellationToken: ct).ToListAsync(ct);
        result.Should().ContainSingle().Which.Record["Id"].Should().Be(expected);
    }

    private sealed class FilterRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public int? Left { get; set; }

        [VectorStoreData]
        public int? Right { get; set; }

        [VectorStoreData]
        public DateOnly Date { get; set; }

        [VectorStoreData]
        public DateTime Timestamp { get; set; }

        [VectorStoreData]
        public DateTimeOffset Offset { get; set; }
    }

    [Fact]
    public async Task Filter_NullableColumnComparisons_MatchLinqUnderNegation()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var collection = _store.GetCollection<string, FilterRecord>("column_comparison");
        await collection.EnsureCollectionExistsAsync(ct);
        FilterRecord[] rows =
        [
            new() { Id = "both_null" },
            new() { Id = "left_null", Right = 1 },
            new() { Id = "right_null", Left = 1 },
            new() { Id = "equal", Left = 1, Right = 1 },
            new() { Id = "unequal", Left = 1, Right = 2 },
        ];
        await collection.UpsertAsync(rows, ct);

        Expression<Func<FilterRecord, bool>>[] filters =
        [r => r.Left == r.Right, r => r.Left != r.Right, r => !(r.Left == r.Right), r => !(r.Left != r.Right)];
        foreach (var filter in filters)
        {
            var actual = await collection.GetAsync(filter, top: rows.Length, cancellationToken: ct).ToListAsync(ct);
            actual.Select(r => r.Id).Should().BeEquivalentTo(rows.Where(filter.Compile()).Select(r => r.Id));
        }
    }

    [Fact]
    public async Task Filter_TemporalLiterals_UseInvariantCalendarAndMatchUtcMicroseconds()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var collection = _store.GetCollection<string, FilterRecord>("temporal_filter");
        await collection.EnsureCollectionExistsAsync(ct);
        DateOnly date = new(2026, 11, 21);
        DateTime timestamp = new DateTime(2026, 11, 21, 12, 34, 56, DateTimeKind.Utc).AddTicks(1_234_560);
        DateTimeOffset offset = new DateTimeOffset(timestamp).ToOffset(TimeSpan.FromHours(2));
        await collection.UpsertAsync(
            [new() { Id = "exact", Date = date, Timestamp = timestamp, Offset = offset },
             new() { Id = "adjacent", Date = date, Timestamp = timestamp.AddTicks(10), Offset = offset.AddTicks(10) }], ct);

        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            Expression<Func<FilterRecord, bool>>[] filters = [r => r.Date == date, r => r.Timestamp == timestamp, r => r.Offset == offset];
            foreach (var filter in filters)
            {
                var actual = await collection.GetAsync(filter, top: 2, cancellationToken: ct).ToListAsync(ct);
                actual.Select(r => r.Id).Should().BeEquivalentTo(filter == filters[0] ? ["exact", "adjacent"] : new[] { "exact" });
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Filter_ComposedConstant_PreservesNegation(bool dynamic, bool operand)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        if (dynamic)
        {
            using var collection = _store.GetDynamicCollection("constant_filter", new VectorStoreCollectionDefinition
            {
                Properties = [new VectorStoreKeyProperty("Id", typeof(string))],
            });
            await collection.EnsureCollectionExistsAsync(ct);
            await collection.UpsertAsync(new Dictionary<string, object?> { ["Id"] = "record" }, ct);
            await AssertConstantFilterAsync(collection, operand, ct);
        }
        else
        {
            using var collection = _store.GetCollection<string, ValidRecordOriginal>("constant_filter");
            await collection.EnsureCollectionExistsAsync(ct);
            await collection.UpsertAsync(new ValidRecordOriginal { Id = "record" }, ct);
            await AssertConstantFilterAsync(collection, operand, ct);
        }
    }

    private static async Task AssertConstantFilterAsync<TKey, TRecord>(VectorStoreCollection<TKey, TRecord> collection, bool operand, CancellationToken ct)
        where TKey : notnull
        where TRecord : class
    {
        var parameter = Expression.Parameter(typeof(TRecord), "record");
        var filter = Expression.Lambda<Func<TRecord, bool>>(Expression.Not(Expression.Constant(operand)), parameter);
        (await collection.GetAsync(filter, top: 1, cancellationToken: ct).ToListAsync(ct)).Should().HaveCount(operand ? 0 : 1);
        (await collection.GetAsync(_ => true, top: 1, cancellationToken: ct).ToListAsync(ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAndUpsert_WithCancelledToken_ThrowsOperationCanceledException_WithoutOpeningDatabase()
    {
        string nonExistentPath = Path.Combine(Path.GetTempPath(), "lancedb-cancel-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            LanceDbCollection<string, TextRecord> collection = new(nonExistentPath, "cancel_test");
            using CancellationTokenSource cts = new();
            cts.Cancel();

            // Upsert with cancelled token
            TextRecord record = new() { Id = "key1", Text = "test", Vector = new float[] { 1f, 2f } };
            Func<Task> actUpsert = async () => await collection.UpsertAsync(record, cts.Token);
            await actUpsert.Should().ThrowAsync<OperationCanceledException>();

            // Directory must not have been created
            Directory.Exists(nonExistentPath).Should().BeFalse();

            // Search with cancelled token
            Func<Task> actSearch = async () =>
            {
                await foreach (VectorSearchResult<TextRecord> _ in collection.SearchAsync(new float[] { 1f, 2f }, top: 1, cancellationToken: cts.Token))
                {
                }
            };
            await actSearch.Should().ThrowAsync<OperationCanceledException>();

            // Directory still must not have been created
            Directory.Exists(nonExistentPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(nonExistentPath))
            {
                Directory.Delete(nonExistentPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetService_ExposesSdkConnectionAndTable()
    {
        string collectionName = "test_get_service";
        VectorStoreCollection<string, TextRecord> collection = _store.GetCollection<string, TextRecord>(collectionName);
        await collection.EnsureCollectionExistsAsync(TestContext.Current.CancellationToken);

        object? connectionObj = collection.GetService(typeof(lancedb.Connection));
        connectionObj.Should().NotBeNull();
        connectionObj.Should().BeOfType<lancedb.Connection>();

        using var table = collection.GetService(typeof(lancedb.Table)) as lancedb.Table;
        table.Should().NotBeNull();
        table!.IsOpen().Should().BeTrue();

        object? storeConnection = _store.GetService(typeof(lancedb.Connection));
        storeConnection.Should().NotBeNull();
        storeConnection.Should().BeOfType<lancedb.Connection>();
    }
}
