using lancedb;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

/// <summary>Internal adapter over the community LanceDB SDK, isolating direct SDK calls.</summary>
internal sealed class LanceDbAdapter : IDisposable, IAsyncDisposable
{
    private readonly string _databaseDirectory;
    private readonly Func<Connection, string, Task> _connect;
    private readonly Func<Connection, string, Task<Table>> _openTable;
    private readonly Lock _gate = new();
    private Task? _opening;
    private Connection? _connection;
    private bool _disposed;

    public LanceDbAdapter(
        string databaseDirectory,
        Func<Connection, string, Task>? connect = null,
        Func<Connection, string, Task<Table>>? openTable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);
        _databaseDirectory = databaseDirectory;
        _connect = connect ?? ((connection, directory) => connection.Connect(directory));
        _openTable = openTable ?? ((connection, name) => connection.OpenTable(name));
    }

    public string DatabaseDirectory => _databaseDirectory;

    public Connection UnderlyingConnection => GetConnectionSync();

    public Connection GetConnectionSync() => GetConnectionAsync().GetAwaiter().GetResult();

    public async Task<Connection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task opening;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is not null)
            {
                return _connection;
            }

            if (_opening is null || _opening.IsCompleted)
            {
                // A synchronous caller can wait on the same attempt without capturing its context.
                _opening = Task.Run(OpenConnectionAsync, CancellationToken.None);
            }

            opening = _opening;
        }

        await opening.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _connection!;
        }
    }

    private async Task OpenConnectionAsync()
    {
        Connection connection = new();
        try
        {
            await _connect(connection, _databaseDirectory).ConfigureAwait(false);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _connection = connection;
            }
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> ListTableNamesAsync(CancellationToken cancellationToken)
    {
        Connection connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await connection.TableNames().ConfigureAwait(false);
    }

    public async Task<bool> TableExistsAsync(string name, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> names = await ListTableNamesAsync(cancellationToken).ConfigureAwait(false);
        return names.Contains(name, StringComparer.Ordinal);
    }

    public async Task<Table?> OpenExistingTableAsync(string name, CancellationToken cancellationToken)
    {
        Connection connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> names = await connection.TableNames().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return names.Contains(name, StringComparer.Ordinal)
            ? await _openTable(connection, name).ConfigureAwait(false)
            : null;
    }

    public async Task DropTableAsync(string name, CancellationToken cancellationToken)
    {
        Connection connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await connection.DropTable(name, ignoreMissing: true).ConfigureAwait(false);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        Task? opening;
        lock (_gate)
        {
            _disposed = true;
            _connection?.Dispose();
            _connection = null;
            opening = _opening;
        }

        if (opening is not null)
        {
            // A pending open owns its local handle and closes it before completing after disposal.
            await opening.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
