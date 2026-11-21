using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbFilterTests : FilterTests<string>, IClassFixture<LanceDbFilterTests.Fixture>
{
    public LanceDbFilterTests(Fixture fixture) : base(fixture)
    {
    }

    public new class Fixture : FilterTests<string>.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
