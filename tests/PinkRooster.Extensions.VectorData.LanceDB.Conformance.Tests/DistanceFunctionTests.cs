using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbDistanceFunctionTests : DistanceFunctionTests<string>, IClassFixture<LanceDbDistanceFunctionTests.Fixture>
{
    public LanceDbDistanceFunctionTests(Fixture fixture) : base(fixture)
    {
    }

    [Fact(Skip = "HammingDistance is not supported by the LanceDB provider.")]
    public override Task HammingDistance() => Task.CompletedTask;

    [Fact(Skip = "ManhattanDistance is not supported by the LanceDB provider.")]
    public override Task ManhattanDistance() => Task.CompletedTask;

    public new class Fixture : DistanceFunctionTests<string>.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
