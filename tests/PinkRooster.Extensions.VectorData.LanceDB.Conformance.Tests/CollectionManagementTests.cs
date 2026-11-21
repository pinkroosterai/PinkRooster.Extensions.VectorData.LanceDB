using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbCollectionManagementTests : CollectionManagementTests<string>, IClassFixture<LanceDbFixture>
{
    public LanceDbCollectionManagementTests(LanceDbFixture fixture) : base(fixture)
    {
    }
}
