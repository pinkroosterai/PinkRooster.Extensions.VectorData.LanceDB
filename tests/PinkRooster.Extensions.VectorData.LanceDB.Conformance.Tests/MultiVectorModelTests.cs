using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbMultiVectorModelTests : MultiVectorModelTests<string>, IClassFixture<LanceDbMultiVectorModelTests.Fixture>
{
    public LanceDbMultiVectorModelTests(Fixture fixture) : base(fixture)
    {
    }

    public new class Fixture : MultiVectorModelTests<string>.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
