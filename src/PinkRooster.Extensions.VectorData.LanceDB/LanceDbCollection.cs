using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using lancedb;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using PinkRooster.Extensions.VectorData.LanceDB.Internal;
using LanceTable = lancedb.Table;

namespace PinkRooster.Extensions.VectorData.LanceDB;

/// <summary>Manages strongly typed records in a local LanceDB table.</summary>
/// <typeparam name="TKey">The type of the record key: <see cref="string"/>, <see cref="Guid"/>, <see cref="int"/>, or <see cref="long"/>.</typeparam>
/// <typeparam name="TRecord">The type that represents a record in the collection.</typeparam>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Follows Microsoft.Extensions.VectorData VectorStoreCollection naming convention.")]
public sealed class LanceDbCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>, IKeywordHybridSearchable<TRecord>
    where TKey : notnull
    where TRecord : class
{
    private readonly string _name;
    private readonly LanceDbVectorStore? _store;
    private readonly LanceDbAdapter _adapter;
    private readonly LanceDbCollectionOptions _options;
    private readonly CollectionModel _model;
    private readonly LanceDbCollectionOperations<TKey, TRecord> _operations;
    private readonly bool _ownsStore;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="LanceDbCollection{TKey, TRecord}"/> class.</summary>
    /// <param name="databaseDirectory">The path to the local database directory.</param>
    /// <param name="name">The table name. Use only ASCII letters, digits, underscores, hyphens, and periods.</param>
    /// <param name="options">The collection options, or <see langword="null"/> to use the defaults and infer the schema from <typeparamref name="TRecord"/>.</param>
    /// <remarks>This constructor does not create the table. Call <see cref="EnsureCollectionExistsAsync"/> to create it explicitly.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="databaseDirectory"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="databaseDirectory"/> or <paramref name="name"/> is empty or consists only of white-space characters,
    /// or <paramref name="name"/> contains an invalid character.
    /// </exception>
    /// <exception cref="NotSupportedException">The collection schema uses a feature that this provider does not support.</exception>
    public LanceDbCollection(string databaseDirectory, string name, LanceDbCollectionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);

        _name = name;
        _options = options ?? new LanceDbCollectionOptions();
        _store = new LanceDbVectorStore(databaseDirectory);
        _adapter = _store.Adapter;
        _ownsStore = true;

        _model = new LanceDbModelBuilder(_options.DefaultDistanceFunction).Build(
            typeof(TRecord),
            typeof(TKey),
            _options.Definition ?? _store.Options.DefaultCollectionDefinition,
            _options.EmbeddingGenerator ?? _store.Options.EmbeddingGenerator);
        _operations = new(_adapter, _name, _options, _model);
    }

    internal LanceDbCollection(LanceDbVectorStore store, string name, LanceDbCollectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);

        _store = store;
        _name = name;
        _options = options ?? new LanceDbCollectionOptions();
        _adapter = store.Adapter;
        _ownsStore = false;

        _model = new LanceDbModelBuilder(_options.DefaultDistanceFunction).Build(
            typeof(TRecord),
            typeof(TKey),
            _options.Definition ?? _store.Options.DefaultCollectionDefinition,
            _options.EmbeddingGenerator ?? _store.Options.EmbeddingGenerator);
        _operations = new(_adapter, _name, _options, _model);
    }

    /// <inheritdoc/>
    public override string Name => _name;

    /// <inheritdoc/>
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
        => _operations.CollectionExistsAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>If the table already exists, its schema is checked against the collection definition.</remarks>
    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
        => _operations.EnsureCollectionExistsAsync(cancellationToken);

    /// <inheritdoc/>
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
        => _operations.EnsureCollectionDeletedAsync(cancellationToken);

    /// <inheritdoc/>
    public override Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
        => _operations.GetAsync(key, options, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<TKey> keys,
        RecordRetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
        => _operations.GetAsync(keys, options, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord, bool>> filter,
        int top = 10,
        FilteredRecordRetrievalOptions<TRecord>? options = null,
        CancellationToken cancellationToken = default)
        => _operations.GetAsync(filter, top, options, cancellationToken);

    /// <inheritdoc/>
    public override Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
        => _operations.DeleteAsync(key, cancellationToken);

    /// <inheritdoc/>
    public override Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default)
        => _operations.DeleteAsync(keys, cancellationToken);

    /// <inheritdoc/>
    public override Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
        => _operations.UpsertAsync(record, cancellationToken);

    /// <inheritdoc/>
    public override Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
        => _operations.UpsertAsync(records, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TVector>(
        TVector vector,
        int top = 10,
        VectorSearchOptions<TRecord>? options = null,
        CancellationToken cancellationToken = default)
        => _operations.SearchAsync(vector, top, options, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// The collection must have exactly one full-text indexed property unless
    /// <see cref="HybridSearchOptions{TRecord}.AdditionalProperty"/> selects one.
    /// Scores reflect the combined ranks of the vector and full-text matches, not vector similarity alone.
    /// </remarks>
    public IAsyncEnumerable<VectorSearchResult<TRecord>> HybridSearchAsync<TInput>(
        TInput searchValue,
        ICollection<string> keywords,
        int top,
        HybridSearchOptions<TRecord>? options = null,
        CancellationToken cancellationToken = default)
        where TInput : notnull
        => _operations.HybridSearchAsync(searchValue, keywords, top, options, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// Supports the collection instance, <see cref="VectorStoreCollectionMetadata"/>, <see cref="CollectionModel"/>,
    /// the shared LanceDB <see cref="Connection"/>, and a LanceDB <see cref="LanceTable"/> handle.
    /// Dispose the returned table handle when finished, but do not dispose the shared connection.
    /// <paramref name="serviceKey"/> is ignored.
    /// </remarks>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(LanceDbCollection<TKey, TRecord>))
        {
            return this;
        }

        if (serviceType == typeof(VectorStoreCollectionMetadata))
        {
            return new VectorStoreCollectionMetadata
            {
                VectorStoreSystemName = "LanceDB",
                VectorStoreName = _store?.DatabaseDirectory,
                CollectionName = _name,
            };
        }

        if (serviceType == typeof(CollectionModel))
        {
            return _model;
        }

        if (serviceType == typeof(Connection))
        {
            return _adapter.UnderlyingConnection;
        }

        if (serviceType == typeof(LanceTable))
        {
            Connection conn = _adapter.UnderlyingConnection;
            return conn.OpenTable(_name).GetAwaiter().GetResult();
        }

        return null;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing && _ownsStore)
            {
                _store?.Dispose();
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }
}
