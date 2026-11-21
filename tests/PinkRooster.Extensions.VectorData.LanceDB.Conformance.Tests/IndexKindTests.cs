using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbIndexKindTests : IndexKindTests<string>, IClassFixture<LanceDbIndexKindTests.Fixture>
{
    public LanceDbIndexKindTests(Fixture fixture) : base(fixture)
    {
    }

    [Fact]
    public virtual Task IvfFlat() => Test(IndexKind.IvfFlat);

    [Fact]
    public virtual Task Hnsw() => Test(IndexKind.Hnsw);

    public new class Fixture : IndexKindTests<string>.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
