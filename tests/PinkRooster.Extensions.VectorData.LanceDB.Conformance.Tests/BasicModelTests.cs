using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbBasicModelTests : BasicModelTests<string>, IClassFixture<LanceDbBasicModelTests.Fixture>
{
    public LanceDbBasicModelTests(Fixture fixture) : base(fixture)
    {
    }

    public new class Fixture : BasicModelTests<string>.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
