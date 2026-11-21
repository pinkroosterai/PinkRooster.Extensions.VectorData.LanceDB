using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests.TypeTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbEmbeddingTypeTests : EmbeddingTypeTests<string>, IClassFixture<LanceDbEmbeddingTypeTests.Fixture>
{
    public LanceDbEmbeddingTypeTests(Fixture fixture) : base(fixture)
    {
    }

    public new class Fixture : EmbeddingTypeTests<string>.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
