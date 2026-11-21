using AwesomeAssertions;
using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB.Tests;

public sealed class LanceDbVectorStoreTests
{
    private sealed class SampleRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;
    }

    [Fact]
    public void Constructor_ThrowsOnNullOrWhitespaceDirectory()
    {
        Action act1 = () => new LanceDbVectorStore(null!);
        Action act2 = () => new LanceDbVectorStore(string.Empty);
        Action act3 = () => new LanceDbVectorStore("   ");

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
        act3.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_SetsDatabaseDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "test-db");
        using LanceDbVectorStore store = new(path);

        store.DatabaseDirectory.Should().Be(path);
    }

    [Fact]
    public void GetCollection_ReturnsLanceDbCollection()
    {
        string path = Path.Combine(Path.GetTempPath(), "test-db");
        using LanceDbVectorStore store = new(path);

        VectorStoreCollection<string, SampleRecord> collection = store.GetCollection<string, SampleRecord>("items");

        collection.Should().NotBeNull();
        collection.Name.Should().Be("items");
        collection.Should().BeOfType<LanceDbCollection<string, SampleRecord>>();
    }

    [Fact]
    public void GetDynamicCollection_ReturnsLanceDbDynamicCollection()
    {
        string path = Path.Combine(Path.GetTempPath(), "test-db");
        using LanceDbVectorStore store = new(path);

        VectorStoreCollectionDefinition definition = new()
        {
            Properties = [new VectorStoreKeyProperty("key", typeof(string))],
        };
        VectorStoreCollection<object, Dictionary<string, object?>> collection = store.GetDynamicCollection("dynamic-items", definition);

        collection.Should().NotBeNull();
        collection.Name.Should().Be("dynamic-items");
        collection.Should().BeOfType<LanceDbDynamicCollection>();
    }

    [Fact]
    public void GetService_ReturnsSelfForLanceDbVectorStore()
    {
        string path = Path.Combine(Path.GetTempPath(), "test-db");
        using LanceDbVectorStore store = new(path);

        store.GetService(typeof(LanceDbVectorStore)).Should().BeSameAs(store);
    }
}
