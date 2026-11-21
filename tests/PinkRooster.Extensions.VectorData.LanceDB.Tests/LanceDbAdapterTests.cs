using AwesomeAssertions;
using lancedb;
using PinkRooster.Extensions.VectorData.LanceDB.Internal;

namespace PinkRooster.Extensions.VectorData.LanceDB.Tests;

public sealed class LanceDbAdapterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lancedb-adapter-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task MixedFirstUse_SharesConnection_WhenAnotherWaiterCancels()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int opens = 0;
        await using LanceDbAdapter adapter = new(_directory, async (connection, directory) =>
        {
            Interlocked.Increment(ref opens);
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            await connection.Connect(directory);
        });
        Task<Connection> sync = Task.Run(adapter.GetConnectionSync, ct);
        try
        {
            await started.Task.WaitAsync(ct);
            Task<Connection> async = adapter.GetConnectionAsync(ct);
            using CancellationTokenSource canceled = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task<Connection> waiter = adapter.GetConnectionAsync(canceled.Token);
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
            sync.IsCompleted.Should().BeFalse();
            async.IsCompleted.Should().BeFalse();
            release.TrySetResult();
            (await async).Should().BeSameAs(await sync);
            opens.Should().Be(1);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task FailedOpen_ReleasesHandleAndCanBeRetried()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Connection? failed = null;
        await using LanceDbAdapter adapter = new(_directory, async (connection, directory) =>
        {
            await connection.Connect(directory);
            if (failed is null)
            {
                failed = connection;
                throw new IOException("Simulated initialization failure after acquiring the native handle.");
            }
        });
        await Assert.ThrowsAsync<IOException>(() => adapter.GetConnectionAsync(ct));
        failed!.IsOpen().Should().BeFalse();
        Connection current = await adapter.GetConnectionAsync(ct);
        current.Should().NotBeSameAs(failed);
        current.IsOpen().Should().BeTrue();
    }

    [Fact]
    public async Task DisposeDuringOpen_WaitsForReleaseWithoutPublishingConnection()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        TaskCompletionSource<Connection> acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using LanceDbAdapter adapter = new(_directory, async (connection, directory) =>
        {
            await connection.Connect(directory);
            acquired.SetResult(connection);
            await release.Task.WaitAsync(ct);
        });
        Task<Connection> opening = adapter.GetConnectionAsync(ct);
        try
        {
            Connection nativeConnection = await acquired.Task.WaitAsync(ct);
            Task closing = adapter.DisposeAsync().AsTask();
            closing.IsCompleted.Should().BeFalse();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => adapter.GetConnectionAsync(ct));
            release.TrySetResult();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => opening);
            await closing;
            nativeConnection.IsOpen().Should().BeFalse();
            Assert.Throws<ObjectDisposedException>(() => adapter.GetConnectionSync());
        }
        finally
        {
            release.TrySetResult();
        }
    }
}
