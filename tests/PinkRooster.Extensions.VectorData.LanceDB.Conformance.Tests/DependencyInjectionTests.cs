using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbDependencyInjectionTests : DependencyInjectionTests<
    LanceDbVectorStore,
    LanceDbCollection<string, DependencyInjectionTests<string>.Record>,
    string,
    DependencyInjectionTests<string>.Record>,
    IClassFixture<LanceDbFixture>
{
    private static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "lancedb-di-" + Guid.NewGuid().ToString("N"));

    private sealed class MetricRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreVector(2, IndexKind = IndexKind.IvfFlat)]
        public ReadOnlyMemory<float> DefaultVector { get; set; }

        [VectorStoreVector(2, IndexKind = IndexKind.IvfFlat, DistanceFunction = DistanceFunction.CosineSimilarity)]
        public ReadOnlyMemory<float> ExplicitVector { get; set; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollectionOptions_PreserveIndexThresholdAndMetricPrecedence(bool keyed)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string directory = Path.Combine(Path.GetTempPath(), $"lancedb-di-options-{Guid.NewGuid():N}");
        const string name = "metric_options";
        LanceDbCollectionOptions options = new() { IndexThreshold = 2, DefaultDistanceFunction = DistanceFunction.EuclideanDistance };
        ServiceCollection services = new();
        if (keyed)
        {
            services.AddKeyedLanceDbCollection<string, MetricRecord>(name, name, (_, _) => directory, options);
        }
        else
        {
            services.AddLanceDbCollection<string, MetricRecord>(name, _ => directory, options);
        }

        try
        {
            using var provider = services.BuildServiceProvider();
            var collection = keyed
                ? provider.GetRequiredKeyedService<LanceDbCollection<string, MetricRecord>>(name)
                : provider.GetRequiredService<LanceDbCollection<string, MetricRecord>>();
            await collection.EnsureCollectionExistsAsync(ct);
            float[] aligned = [3, 0];
            float[] nearby = [1, 1];
            await collection.UpsertAsync(new MetricRecord { Id = "aligned", DefaultVector = aligned, ExplicitVector = aligned }, ct);
            using (var table = (lancedb.Table)collection.GetService(typeof(lancedb.Table))!)
            {
                (await table.ListIndices()).Should().BeEmpty();
            }

            await collection.UpsertAsync(new MetricRecord { Id = "nearby", DefaultVector = nearby, ExplicitVector = nearby }, ct);
            using (var table = (lancedb.Table)collection.GetService(typeof(lancedb.Table))!)
            {
                (await table.IndexStats("DefaultVector_vec_idx"))!.DistanceType.Should().Be(lancedb.DistanceType.L2);
                (await table.IndexStats("ExplicitVector_vec_idx"))!.DistanceType.Should().Be(lancedb.DistanceType.Cosine);
                (await table.ListIndices()).Should().HaveCount(2);
            }

            float[] query = [1, 0];
            VectorSearchOptions<MetricRecord> distance = new() { VectorProperty = r => r.DefaultVector, ScoreThreshold = 1.5 };
            VectorSearchOptions<MetricRecord> similarity = new() { VectorProperty = r => r.ExplicitVector, ScoreThreshold = 0.9 };
            var l2 = (await collection.SearchAsync(query, 2, distance, ct).ToListAsync(ct)).Should().ContainSingle().Which;
            var cosine = (await collection.SearchAsync(query, 2, similarity, ct).ToListAsync(ct)).Should().ContainSingle().Which;
            l2.Record.Id.Should().Be("nearby");
            l2.Score.Should().Be(1.0);
            cosine.Record.Id.Should().Be("aligned");
            cosine.Score.Should().Be(1.0);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    protected override void PopulateConfiguration(ConfigurationManager configuration, object? serviceKey)
    {
        configuration[CreateConfigKey("LanceDb", serviceKey, "DatabaseDirectory")] = TempDirectory;
    }

    public override IEnumerable<Func<IServiceCollection, object?, ServiceLifetime, IServiceCollection>> StoreDelegates =>
    [
        (services, serviceKey, lifetime) => serviceKey is null
            ? services.AddLanceDbVectorStore(TempDirectory, lifetime)
            : services.AddKeyedLanceDbVectorStore(serviceKey, TempDirectory, lifetime),
        (services, serviceKey, lifetime) => serviceKey is null
            ? services.AddLanceDbVectorStore(sp => sp.GetRequiredService<IConfiguration>()[CreateConfigKey("LanceDb", null, "DatabaseDirectory")]!, lifetime)
            : services.AddKeyedLanceDbVectorStore(serviceKey, (sp, key) => sp.GetRequiredService<IConfiguration>()[CreateConfigKey("LanceDb", key, "DatabaseDirectory")]!, lifetime),
    ];

    public override IEnumerable<Func<IServiceCollection, object?, string, ServiceLifetime, IServiceCollection>> CollectionDelegates =>
    [
        (services, serviceKey, collectionName, lifetime) => serviceKey is null
            ? services.AddLanceDbCollection<string, Record>(collectionName, TempDirectory, lifetime: lifetime)
            : services.AddKeyedLanceDbCollection<string, Record>(serviceKey, collectionName, TempDirectory, lifetime: lifetime),
        (services, serviceKey, collectionName, lifetime) => serviceKey is null
            ? services.AddLanceDbCollection<string, Record>(collectionName, sp => sp.GetRequiredService<IConfiguration>()[CreateConfigKey("LanceDb", null, "DatabaseDirectory")]!, lifetime: lifetime)
            : services.AddKeyedLanceDbCollection<string, Record>(serviceKey, collectionName, (sp, key) => sp.GetRequiredService<IConfiguration>()[CreateConfigKey("LanceDb", key, "DatabaseDirectory")]!, lifetime: lifetime),
    ];
}
