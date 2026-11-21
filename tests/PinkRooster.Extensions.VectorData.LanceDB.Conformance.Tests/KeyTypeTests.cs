using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests.TypeTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbKeyTypeTests : KeyTypeTests, IClassFixture<LanceDbKeyTypeTests.Fixture>
{
    public LanceDbKeyTypeTests(Fixture fixture) : base(fixture)
    {
    }

    [Fact]
    public override Task Guid() => Test(System.Guid.NewGuid(), System.Guid.NewGuid(), supportsAutoGeneration: false);

    [Fact]
    public Task StringKey() => Test("key1", "key2", supportsAutoGeneration: false);

    [Fact]
    public Task IntKey() => Test(1, 2, supportsAutoGeneration: false);

    [Fact]
    public Task LongKey() => Test(1L, 2L, supportsAutoGeneration: false);

    public new class Fixture : KeyTypeTests.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
