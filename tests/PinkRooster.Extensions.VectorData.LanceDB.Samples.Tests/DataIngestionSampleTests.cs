using System.Text;
using AwesomeAssertions;
using PinkRooster.Extensions.VectorData.LanceDB.Samples.DataIngestion;

namespace PinkRooster.Extensions.VectorData.LanceDB.Samples.Tests;

public sealed class DataIngestionSampleTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lancedb-dataingestion-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_RejectsNonemptyDatabaseDirectory_WithoutChangingExistingDocument()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        string handbook = Path.Combine(_directory, "handbook.txt");
        await File.WriteAllTextAsync(handbook, "Do not replace.", ct);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Program.RunAsync(_directory, ct));
        error.ParamName.Should().Be("databaseDirectory");
        (await File.ReadAllTextAsync(handbook, ct)).Should().Be("Do not replace.");
        Directory.GetFileSystemEntries(_directory).Should().Equal(handbook);
    }

    [Fact]
    public async Task Embeddings_ObserveCancellationBeforeAndBetweenInputs()
    {
        using HashingEmbeddingGenerator generator = new(8);
        using CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int enumerated = 0;
        IEnumerable<string> Inputs()
        {
            enumerated++;
            yield return "First";
            source.Cancel();
            yield return "Second";
            enumerated++;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GenerateAsync(Inputs(), cancellationToken: source.Token));
        enumerated.Should().Be(1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GenerateAsync(Inputs(), cancellationToken: source.Token));
        enumerated.Should().Be(1);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task TextReader_ProducesSameParagraphsForBothLineEndings(string newline)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes($"first paragraph{newline}{newline}second paragraph"));
        var document = await new TextFileReader().ReadAsync(stream, "handbook", "text/plain", ct);
        document.Sections.SelectMany(section => section.Elements).Select(element => element.GetMarkdown())
            .Should().Equal("first paragraph", "second paragraph");
    }

    [Fact]
    public async Task RunAsync_AfterReingesting_HoldsOnlyTheNewVersionAndHybridSearchFindsIt()
    {
        Program.Result result = await Program.RunAsync(_directory, TestContext.Current.CancellationToken);

        result.Chunks.Should().BeEquivalentTo(
            "Employees accrue vacation days monthly.",
            "Expense reports are due on the fifth of each month.",
            "Badge code QX7731 opens the lab door.");
        result.Chunks.Should().NotContain(c => c.Contains("old badge policy", StringComparison.Ordinal));
        result.TopHit.Should().Be("Badge code QX7731 opens the lab door.");
    }
}
