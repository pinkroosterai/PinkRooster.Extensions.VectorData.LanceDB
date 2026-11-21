using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests.ModelTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbNoDataModelTests : NoDataModelTests<string>, IClassFixture<LanceDbNoDataModelTests.Fixture>
{
    public LanceDbNoDataModelTests(Fixture fixture) : base(fixture)
    {
    }

    public new class Fixture : NoDataModelTests<string>.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
