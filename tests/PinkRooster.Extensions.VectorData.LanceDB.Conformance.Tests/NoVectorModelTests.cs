using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbNoVectorModelTests : NoVectorModelTests<string>, IClassFixture<LanceDbNoVectorModelTests.Fixture>
{
    public LanceDbNoVectorModelTests(Fixture fixture) : base(fixture)
    {
    }

    public new class Fixture : NoVectorModelTests<string>.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
