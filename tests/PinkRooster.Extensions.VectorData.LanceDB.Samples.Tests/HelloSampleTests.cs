using AwesomeAssertions;
using PinkRooster.Extensions.VectorData.LanceDB.Samples.Hello;

namespace PinkRooster.Extensions.VectorData.LanceDB.Samples.Tests;

public sealed class HelloSampleTests
{
    [Fact]
    public async Task RunAsync_ReturnsCollectionName() =>
        (await Program.RunAsync()).Should().Be("items");
}
