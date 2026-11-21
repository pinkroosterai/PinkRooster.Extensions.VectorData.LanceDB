using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbDynamicModelTests : DynamicModelTests<string>, IClassFixture<LanceDbDynamicModelTests.Fixture>
{
    public LanceDbDynamicModelTests(Fixture fixture) : base(fixture)
    {
    }

    public new class Fixture : DynamicModelTests<string>.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
