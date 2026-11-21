using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using lancedb;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using PinkRooster.Extensions.VectorData.LanceDB.Internal;
using LanceTable = lancedb.Table;

namespace PinkRooster.Extensions.VectorData.LanceDB;

/// <summary>LanceDB implementation of <see cref="VectorStoreCollection{TKey, TRecord}"/> for dynamic dictionary records.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Follows Microsoft.Extensions.VectorData VectorStoreCollection naming convention.")]
public sealed class LanceDbDynamicCollection : VectorStoreCollection<object, Dictionary<string, object?>>, IKeywordHybridSearchable<Dictionary<string, object?>>
{
    private readonly string _name;
    private readonly LanceDbVectorStore? _store;
    private readonly LanceDbAdapter _adapter;
    private readonly LanceDbCollectionOptions _options;
    private readonly CollectionModel _model;
    private readonly LanceDbCollectionOperations<object, Dictionary<string, object?>> _operations;
    private readonly bool _ownsStore;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="LanceDbDynamicCollection"/> class for a database directory.</summary>
    /// <param name="databaseDirectory">The path to the local database directory.</param>
    /// <param name="name">The collection name.</param>
    /// <param name="options">Optional collection options.</param>
    public LanceDbDynamicCollection(string databaseDirectory, string name, LanceDbCollectionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);

        _name = name;
        _options = options ?? new LanceDbCollectionOptions();
        _store = new LanceDbVectorStore(databaseDirectory);
        _adapter = _store.Adapter;
        _ownsStore = true;

        VectorStoreCollectionDefinition definition = _options.Definition
            ?? _store.Options.DefaultCollectionDefinition
            ?? throw new ArgumentException("A collection definition must be provided for dynamic collections.", nameof(options));

        _model = new LanceDbModelBuilder(_options.DefaultDistanceFunction).BuildDynamic(
            definition,
            _options.EmbeddingGenerator ?? _store.Options.EmbeddingGenerator);
        _operations = new(_adapter, _name, _options, _model);
    }

    internal LanceDbDynamicCollection(LanceDbVectorStore store, string name, LanceDbCollectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);

        _store = store;
        _name = name;
        _options = options ?? new LanceDbCollectionOptions();
        _adapter = store.Adapter;
        _ownsStore = false;

        VectorStoreCollectionDefinition definition = _options.Definition
            ?? _store.Options.DefaultCollectionDefinition
            ?? throw new ArgumentException("A collection definition must be provided for dynamic collections.", nameof(options));

        _model = new LanceDbModelBuilder(_options.DefaultDistanceFunction).BuildDynamic(
            definition,
            _options.EmbeddingGenerator ?? _store.Options.EmbeddingGenerator);
        _operations = new(_adapter, _name, _options, _model);
    }

    /// <inheritdoc/>
    public override string Name => _name;

    /// <inheritdoc/>
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
        => _operations.CollectionExistsAsync(cancellationToken);

    /// <inheritdoc/>
    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
        => _operations.EnsureCollectionExistsAsync(cancellationToken);

    /// <inheritdoc/>
    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
        => _operations.EnsureCollectionDeletedAsync(cancellationToken);

    /// <inheritdoc/>
    public override Task<Dictionary<string, object?>?> GetAsync(object key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
        => _operations.GetAsync(key, options, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncEnumerable<Dictionary<string, object?>> GetAsync(
        IEnumerable<object> keys,
        RecordRetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
        => _operations.GetAsync(keys, options, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncEnumerable<Dictionary<string, object?>> GetAsync(
        Expression<Func<Dictionary<string, object?>, bool>> filter,
        int top = 10,
        FilteredRecordRetrievalOptions<Dictionary<string, object?>>? options = null,
        CancellationToken cancellationToken = default)
        => _operations.GetAsync(filter, top, options, cancellationToken);

    /// <inheritdoc/>
    public override Task DeleteAsync(object key, CancellationToken cancellationToken = default)
        => _operations.DeleteAsync(key, cancellationToken);

    /// <inheritdoc/>
    public override Task DeleteAsync(IEnumerable<object> keys, CancellationToken cancellationToken = default)
        => _operations.DeleteAsync(keys, cancellationToken);

    /// <inheritdoc/>
    public override Task UpsertAsync(Dictionary<string, object?> record, CancellationToken cancellationToken = default)
        => _operations.UpsertAsync(record, cancellationToken);

    /// <inheritdoc/>
    public override Task UpsertAsync(IEnumerable<Dictionary<string, object?>> records, CancellationToken cancellationToken = default)
        => _operations.UpsertAsync(records, cancellationToken);

    /// <inheritdoc/>
    public override IAsyncEnumerable<VectorSearchResult<Dictionary<string, object?>>> SearchAsync<TVector>(
        TVector vector,
        int top = 10,
        VectorSearchOptions<Dictionary<string, object?>>? options = null,
        CancellationToken cancellationToken = default)
        => _operations.SearchAsync(vector, top, options, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<VectorSearchResult<Dictionary<string, object?>>> HybridSearchAsync<TInput>(
        TInput searchValue,
        ICollection<string> keywords,
        int top,
        HybridSearchOptions<Dictionary<string, object?>>? options = null,
        CancellationToken cancellationToken = default)
        where TInput : notnull
        => _operations.HybridSearchAsync(searchValue, keywords, top, options, cancellationToken);

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(LanceDbDynamicCollection))
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
