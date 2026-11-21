using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbEmbeddingGenerationTests : EmbeddingGenerationTests<string>,
    IClassFixture<LanceDbEmbeddingGenerationTests.StringVectorFixture>,
    IClassFixture<LanceDbEmbeddingGenerationTests.RomOfFloatVectorFixture>
{
    public LanceDbEmbeddingGenerationTests(StringVectorFixture stringVectorFixture, RomOfFloatVectorFixture romOfFloatVectorFixture)
        : base(stringVectorFixture, romOfFloatVectorFixture)
    {
    }

    public new class StringVectorFixture : EmbeddingGenerationTests<string>.StringVectorFixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services.AddLanceDbVectorStore(((LanceDbVectorStore)VectorStore).DatabaseDirectory),
            services => services.AddLanceDbVectorStore(sp => ((LanceDbVectorStore)VectorStore).DatabaseDirectory),
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services
                .AddLanceDbCollection<string, Record>(CollectionName, ((LanceDbVectorStore)VectorStore).DatabaseDirectory)
                .AddLanceDbCollection<string, RecordWithAttributes>(CollectionName, ((LanceDbVectorStore)VectorStore).DatabaseDirectory),
            services => services
                .AddLanceDbCollection<string, Record>(CollectionName, sp => ((LanceDbVectorStore)VectorStore).DatabaseDirectory)
                .AddLanceDbCollection<string, RecordWithAttributes>(CollectionName, sp => ((LanceDbVectorStore)VectorStore).DatabaseDirectory),
        ];

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator) =>
            new LanceDbVectorStore(((LanceDbVectorStore)TestStore.DefaultVectorStore!).DatabaseDirectory, new LanceDbVectorStoreOptions
            {
                EmbeddingGenerator = embeddingGenerator,
            });
    }

    public new class RomOfFloatVectorFixture : EmbeddingGenerationTests<string>.RomOfFloatVectorFixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionStoreRegistrationDelegates =>
        [
            services => services.AddLanceDbVectorStore(((LanceDbVectorStore)VectorStore).DatabaseDirectory),
            services => services.AddLanceDbVectorStore(sp => ((LanceDbVectorStore)VectorStore).DatabaseDirectory),
        ];

        public override Func<IServiceCollection, IServiceCollection>[] DependencyInjectionCollectionRegistrationDelegates =>
        [
            services => services.AddLanceDbCollection<string, RecordWithRomOfFloatVectorProperty>(CollectionName, ((LanceDbVectorStore)VectorStore).DatabaseDirectory),
            services => services.AddLanceDbCollection<string, RecordWithRomOfFloatVectorProperty>(CollectionName, sp => ((LanceDbVectorStore)VectorStore).DatabaseDirectory),
        ];

        public override VectorStore CreateVectorStore(IEmbeddingGenerator? embeddingGenerator) =>
            new LanceDbVectorStore(((LanceDbVectorStore)TestStore.DefaultVectorStore!).DatabaseDirectory, new LanceDbVectorStoreOptions
            {
                EmbeddingGenerator = embeddingGenerator,
            });
    }
}
