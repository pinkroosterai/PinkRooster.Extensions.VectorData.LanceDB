#pragma warning disable MEVD9001
using PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;
using VectorData.ConformanceTests.TypeTests;
using VectorData.ConformanceTests.Support;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests;

public class LanceDbDataTypeTests : DataTypeTests<string, DataTypeTests<string>.DefaultRecord>, IClassFixture<LanceDbDataTypeTests.Fixture>
{
    public LanceDbDataTypeTests(Fixture fixture) : base(fixture)
    {
    }

    [Fact(Skip = "TimeOnly filtering is not supported by the LanceDB / DataFusion query engine.")]
    public override Task TimeOnly() => Task.CompletedTask;

    public new class Fixture : DataTypeTests<string, DataTypeTests<string>.DefaultRecord>.Fixture
    {
        public override TestStore TestStore => LanceDbTestStore.Instance;

        public override Type[] UnsupportedDefaultTypes => [typeof(decimal)];
    }
}
