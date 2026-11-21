using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests.Support;

namespace PinkRooster.Extensions.VectorData.LanceDB.Conformance.Tests.Support;

public sealed class LanceDbTestStore : TestStore
{
    public static LanceDbTestStore Instance { get; } = new();

    private string? _tempDirectory;
    private LanceDbVectorStore? _store;

    public override string DefaultDistanceFunction => DistanceFunction.CosineSimilarity;

    protected override Task StartAsync()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "lancedb-conformance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _store = new LanceDbVectorStore(_tempDirectory);
        DefaultVectorStore = _store;
        return Task.CompletedTask;
    }

    public override Task WaitForDataAsync<TKey, TRecord>(
        VectorStoreCollection<TKey, TRecord> collection,
        int recordCount,
        System.Linq.Expressions.Expression<Func<TRecord, bool>>? filter,
        System.Linq.Expressions.Expression<Func<TRecord, object?>>? vectorProperty,
        int? vectorSize,
        object? dummyVector)
    {
        return Task.CompletedTask;
    }

    protected override async Task StopAsync()
    {
        _store?.Dispose();
        _store = null;
        if (_tempDirectory is not { } directory)
        {
            return;
        }

        const int maxAttempts = 4;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == maxAttempts)
                {
                    throw new IOException($"Could not clean up the LanceDB test directory '{directory}' after {maxAttempts} attempts.", ex);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt)).ConfigureAwait(false);
            }
        }

        _tempDirectory = null;
    }
}
