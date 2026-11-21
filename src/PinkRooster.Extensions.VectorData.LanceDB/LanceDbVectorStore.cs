using System.Runtime.CompilerServices;
using lancedb;
using Microsoft.Extensions.VectorData;
using PinkRooster.Extensions.VectorData.LanceDB.Internal;

namespace PinkRooster.Extensions.VectorData.LanceDB;

/// <summary>Manages vector collections stored as tables in a local, embedded LanceDB database.</summary>
public sealed class LanceDbVectorStore : VectorStore
{
    private readonly string _databaseDirectory;
    private readonly LanceDbVectorStoreOptions _options;
    private readonly LanceDbAdapter _adapter;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="LanceDbVectorStore"/> class.</summary>
    /// <param name="databaseDirectory">The path to the local directory where LanceDB stores its tables.</param>
    /// <param name="options">The store options, or <see langword="null"/> to use the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="databaseDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="databaseDirectory"/> is empty or consists only of white-space characters.</exception>
    public LanceDbVectorStore(string databaseDirectory, LanceDbVectorStoreOptions? options = null)
        : this(databaseDirectory, options, connect: null)
    {
    }

    internal LanceDbVectorStore(
        string databaseDirectory,
        LanceDbVectorStoreOptions? options,
        Func<Connection, string, Task>? connect,
        Func<Connection, string, Task<lancedb.Table>>? openTable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);
        _databaseDirectory = databaseDirectory;
        _options = options ?? new LanceDbVectorStoreOptions();
        _adapter = new LanceDbAdapter(databaseDirectory, connect, openTable);
    }

    /// <summary>Gets the path to the local database directory.</summary>
    public string DatabaseDirectory => _databaseDirectory;

    internal LanceDbAdapter Adapter => _adapter;

    internal LanceDbVectorStoreOptions Options => _options;

    /// <inheritdoc/>
    public override async Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _adapter.TableExistsAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: name, operationName: nameof(CollectionExistsAsync));
        }
    }

    /// <inheritdoc/>
    public override async Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _adapter.DropTableAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: name, operationName: nameof(EnsureCollectionDeletedAsync));
        }
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<string> tableNames;
        try
        {
            tableNames = await _adapter.ListTableNamesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, operationName: nameof(ListCollectionNamesAsync));
        }

        foreach (string name in tableNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return name;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// When <paramref name="definition"/> is <see langword="null"/>, this store uses
    /// <see cref="LanceDbVectorStoreOptions.DefaultCollectionDefinition"/> if configured.
    /// Otherwise, the schema is inferred from <typeparamref name="TRecord"/>.
    /// </remarks>
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);
        return new LanceDbCollection<TKey, TRecord>(this, name, new LanceDbCollectionOptions
        {
            Definition = definition ?? _options.DefaultCollectionDefinition,
            EmbeddingGenerator = _options.EmbeddingGenerator,
        });
    }

    /// <inheritdoc/>
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(definition);
        LanceDbNameValidator.Validate(name);
        return new LanceDbDynamicCollection(this, name, new LanceDbCollectionOptions
        {
            Definition = definition,
            EmbeddingGenerator = _options.EmbeddingGenerator,
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Supports <see cref="LanceDbVectorStore"/>, <see cref="VectorStoreMetadata"/>, and the underlying LanceDB
    /// <see cref="Connection"/>. The store owns the connection; callers should not dispose it.
    /// <paramref name="serviceKey"/> is ignored.
    /// </remarks>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(LanceDbVectorStore))
        {
            return this;
        }

        if (serviceType == typeof(VectorStoreMetadata))
        {
            return new VectorStoreMetadata
            {
                VectorStoreSystemName = "LanceDB",
                VectorStoreName = _databaseDirectory,
            };
        }

        if (serviceType == typeof(Connection))
        {
            return _adapter.UnderlyingConnection;
        }

        return null;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _adapter.Dispose();
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }
}
