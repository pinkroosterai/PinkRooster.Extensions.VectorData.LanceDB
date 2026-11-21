using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;

public class LanceDbFixture : VectorStoreFixture
{
    public override TestStore TestStore => LanceDbTestStore.Instance;
}
