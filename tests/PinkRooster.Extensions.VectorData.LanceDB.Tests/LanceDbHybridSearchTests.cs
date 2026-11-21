using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB.Tests;

public sealed class LanceDbHybridSearchTests : IDisposable
{
    private const string Identifier = "QX7731";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lancedb-hybrid-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class Doc
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(IsFullTextIndexed = true)]
        public string Text { get; set; } = string.Empty;

        [VectorStoreData(IsIndexed = true)]
        public int Group { get; set; }

        [VectorStoreVector(4)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    private sealed class NoTextDoc
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(4)]
        public ReadOnlyMemory<float> Vector { get; set; }
    }

    private static readonly float[] Query = [1f, 0f, 0f, 0f];

    // Twelve records close to the query, and one whose text holds the identifier but whose vector points away from it.
    private static List<Doc> Fixture()
    {
        List<Doc> docs = [];
        for (int i = 0; i < 12; i++)
        {
            docs.Add(new Doc { Id = $"near-{i}", Text = $"general notes about topic {i}", Group = 1, Vector = new float[] { 1f, 0.01f * i, 0f, 0f } });
        }

        docs.Add(new Doc { Id = "doc-far", Text = $"reference {Identifier} appears here", Group = 2, Vector = new float[] { -1f, 0f, 0f, 0f } });
        return docs;
    }

    [Fact]
    public async Task HybridSearch_FindsExactIdentifierThatVectorSearchMisses()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var collection = new LanceDbCollection<string, Doc>(_directory, "identifier_typed");
        await collection.EnsureCollectionExistsAsync(ct);
        await collection.UpsertAsync(Fixture(), ct);

        List<string> vectorTop = [];
        await foreach (var r in collection.SearchAsync(Query, top: 3, cancellationToken: ct))
        {
            vectorTop.Add(r.Record.Id);
        }

        List<string> hybridTop = [];
        await foreach (var r in collection.HybridSearchAsync(Query, [Identifier], top: 3, cancellationToken: ct))
        {
            hybridTop.Add(r.Record.Id);
        }

        vectorTop.Should().NotContain("doc-far");
        hybridTop.Should().Contain("doc-far");
        hybridTop[0].Should().Be("doc-far");
    }

    [Fact]
    public async Task HybridSearch_Filter_AppliesToBothHalves()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var collection = new LanceDbCollection<string, Doc>(_directory, "identifier_filter");
        await collection.EnsureCollectionExistsAsync(ct);
        await collection.UpsertAsync(Fixture(), ct);

        List<string> ids = [];
        await foreach (var r in collection.HybridSearchAsync(Query, [Identifier], top: 5, new HybridSearchOptions<Doc> { Filter = d => d.Group == 1 }, ct))
        {
            ids.Add(r.Record.Id);
        }

        ids.Should().HaveCount(5).And.NotContain("doc-far");
    }

    [Fact]
    public async Task HybridSearch_IncludeVectors_ReturnsVectors()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var collection = new LanceDbCollection<string, Doc>(_directory, "identifier_vectors");
        await collection.EnsureCollectionExistsAsync(ct);
        await collection.UpsertAsync(Fixture(), ct);

        await foreach (var r in collection.HybridSearchAsync(Query, [Identifier], top: 1, new HybridSearchOptions<Doc> { IncludeVectors = true }, ct))
        {
            r.Record.Id.Should().Be("doc-far");
            r.Record.Vector.ToArray().Should().Equal(-1f, 0f, 0f, 0f);
            r.Score.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task DynamicCollection_HybridSearch_FindsExactIdentifier()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        VectorStoreCollectionDefinition definition = new()
        {
            Properties =
            [
                new VectorStoreKeyProperty("Id", typeof(string)),
                new VectorStoreDataProperty("Text", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreVectorProperty("Vector", typeof(ReadOnlyMemory<float>), 4),
            ],
        };
        using var collection = new LanceDbDynamicCollection(_directory, "identifier_dynamic", new LanceDbCollectionOptions { Definition = definition });
        await collection.EnsureCollectionExistsAsync(ct);
        await collection.UpsertAsync(
            Fixture().Select(d => new Dictionary<string, object?> { ["Id"] = d.Id, ["Text"] = d.Text, ["Vector"] = d.Vector }),
            ct);

        List<object?> hybridTop = [];
        await foreach (var r in collection.HybridSearchAsync(Query, [Identifier], top: 3, cancellationToken: ct))
        {
            hybridTop.Add(r.Record["Id"]);
        }

        hybridTop.Should().Contain("doc-far");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HybridSearch_CancellationAfterKeywordTableOpen_ClosesTable(bool dynamic)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lancedb.Table? keywordTable = null;
        int opened = 0;
        using LanceDbVectorStore store = new(_directory, null, null, async (connection, name) =>
        {
            lancedb.Table table = await connection.OpenTable(name);
            if (++opened == 2)
            {
                keywordTable = table;
                cancellation.Cancel();
            }

            return table;
        });
        using var typed = store.GetCollection<string, Doc>("cancel_hybrid");
        await typed.EnsureCollectionExistsAsync(ct);
        await typed.UpsertAsync(Fixture(), ct);

        VectorStoreCollectionDefinition definition = new()
        {
            Properties =
            [
                new VectorStoreKeyProperty(nameof(Doc.Id), typeof(string)),
                new VectorStoreDataProperty(nameof(Doc.Text), typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreDataProperty(nameof(Doc.Group), typeof(int)) { IsIndexed = true },
                new VectorStoreVectorProperty(nameof(Doc.Vector), typeof(ReadOnlyMemory<float>), 4),
            ],
        };
        using var dictionary = store.GetDynamicCollection("cancel_hybrid", definition);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dynamic
            ? ConsumeAsync(((IKeywordHybridSearchable<Dictionary<string, object?>>)dictionary).HybridSearchAsync(Query, [Identifier], 3, cancellationToken: cancellation.Token))
            : ConsumeAsync(((IKeywordHybridSearchable<Doc>)typed).HybridSearchAsync(Query, [Identifier], 3, cancellationToken: cancellation.Token)));
        opened.Should().Be(2);
        keywordTable!.IsOpen().Should().BeFalse();
    }

    [Fact]
    public async Task Queries_CloseAcquiredTable_WhenOrderingThrowsOrEnumerationStops()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        lancedb.Table? acquired = null;
        using LanceDbVectorStore store = new(_directory, null, null, async (connection, name) => acquired = await connection.OpenTable(name));
        using var collection = store.GetCollection<string, Doc>("query_ownership");
        await collection.EnsureCollectionExistsAsync(ct);
        await collection.UpsertAsync(Fixture(), ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConsumeAsync(collection.GetAsync(
            _ => true, 3, new FilteredRecordRetrievalOptions<Doc> { OrderBy = _ => throw new InvalidOperationException() }, ct)));
        acquired!.IsOpen().Should().BeFalse();

        await using (var results = collection.SearchAsync(Query, 3, cancellationToken: ct).GetAsyncEnumerator(ct))
        {
            (await results.MoveNextAsync()).Should().BeTrue();
        }

        acquired.IsOpen().Should().BeFalse();
    }

    private static async Task ConsumeAsync<T>(IAsyncEnumerable<T> results)
    {
        await foreach (T _ in results)
        {
        }
    }

    [Fact]
    public async Task HybridSearch_WithoutFullTextProperty_ThrowsInvalidOperationNamingCollection()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var collection = new LanceDbCollection<string, NoTextDoc>(_directory, "no_text_collection");
        await collection.EnsureCollectionExistsAsync(ct);

        Func<Task> act = async () =>
        {
            await foreach (var _ in collection.HybridSearchAsync(Query, ["anything"], top: 3, cancellationToken: ct))
            {
            }
        };

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("no_text_collection");
    }

    [Fact]
    public void AddLanceDbCollection_RegistersKeywordHybridSearchable()
    {
        ServiceCollection services = new();
        services.AddLanceDbCollection<string, Doc>("registered_hybrid", _directory);
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetService<IKeywordHybridSearchable<Doc>>().Should().NotBeNull();
    }
}
