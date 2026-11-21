using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbHybridSearchTests(
    LanceDbHybridSearchTests.VectorAndStringFixture vectorAndStringFixture,
    LanceDbHybridSearchTests.MultiTextFixture multiTextFixture)
    : HybridSearchTests<string>(vectorAndStringFixture, multiTextFixture),
        IClassFixture<LanceDbHybridSearchTests.VectorAndStringFixture>,
        IClassFixture<LanceDbHybridSearchTests.MultiTextFixture>
{
    public new class VectorAndStringFixture : HybridSearchTests<string>.VectorAndStringFixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }

    public new class MultiTextFixture : HybridSearchTests<string>.MultiTextFixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;
    }
}
