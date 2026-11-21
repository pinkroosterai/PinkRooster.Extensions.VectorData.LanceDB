using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Apache.Arrow;
using lancedb;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using LanceTable = lancedb.Table;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

internal sealed class LanceDbCollectionOperations<TKey, TRecord>(
    LanceDbAdapter adapter, string name, LanceDbCollectionOptions options, CollectionModel model)
    where TKey : notnull
    where TRecord : class
{
    private readonly LanceDbAdapter _adapter = adapter;
    private readonly string _name = name;
    private readonly LanceDbCollectionOptions _options = options;
    private readonly CollectionModel _model = model;
    private readonly Schema _expectedSchema = ArrowSchemaMapper.BuildSchema(model);

    public async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await _adapter.TableExistsAsync(_name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(CollectionExistsAsync));
        }
    }

    public async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Connection conn = await _adapter.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> tableNames = await conn.TableNames().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using LanceTable table = tableNames.Contains(_name, StringComparer.Ordinal)
                ? await conn.OpenTable(_name).ConfigureAwait(false)
                : await conn.CreateEmptyTable(_name, new CreateTableOptions
                {
                    Schema = _expectedSchema,
                    ExistOk = true,
                }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Schema schema = await table.Schema().ConfigureAwait(false);
            ArrowSchemaMapper.ValidateExistingTableSchema(schema, _expectedSchema, _name);
            await LanceDbIndexManager.EnsureInitialIndexesAsync(table, _model, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(EnsureCollectionExistsAsync));
        }
    }

    public async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _adapter.DropTableAsync(_name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(EnsureCollectionDeletedAsync));
        }
    }

    public async Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        await foreach (TRecord record in GetAsync([key], options, cancellationToken).ConfigureAwait(false))
        {
            return record;
        }

        return null;
    }

    public async IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<TKey> keys,
        RecordRetrievalOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();

        if (options?.IncludeVectors == true && _model.VectorProperties.Any(vp => !IsVectorTypeSupported(vp.Type)))
        {
            throw new NotSupportedException("When an embedding generator is configured, `Include Vectors` cannot be enabled.");
        }

        List<object> keyList = [];
        foreach (TKey key in keys)
        {
            ArgumentNullException.ThrowIfNull(key);
            keyList.Add(key);
        }

        if (keyList.Count == 0)
        {
            yield break;
        }

        Connection conn;
        try
        {
            conn = await _adapter.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> tableNames = await conn.TableNames().ConfigureAwait(false);
            if (!tableNames.Contains(_name, StringComparer.Ordinal))
            {
                yield break;
            }
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(GetAsync));
        }

        LanceTable table;
        try
        {
            table = await conn.OpenTable(_name).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(GetAsync));
        }

        using (table)
        {
            const int maxKeyBatchSize = 1000;
            for (int i = 0; i < keyList.Count; i += maxKeyBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(maxKeyBatchSize, keyList.Count - i);
                IReadOnlyList<object> chunk = keyList.GetRange(i, count);
                string filter = LanceDbSqlHelper.BuildKeyFilter(_model.KeyProperty.StorageName, chunk);

                RecordBatch batch;
                bool includeVectors = options?.IncludeVectors ?? false;
                try
                {
                    using lancedb.Query query = table.Query().Where(filter);
                    if (!includeVectors && _model.VectorProperties.Count > 0)
                    {
                        List<string> nonVectorCols = _model.Properties
                            .Where(p => p is not VectorPropertyModel)
                            .Select(p => p.StorageName)
                            .ToList();
                        query.Select(nonVectorCols);
                    }

                    batch = await query.ToArrow().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(GetAsync));
                }

                using (batch)
                {
                    for (int row = 0; row < batch.Length; row++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return ArrowRecordBatchMapper.ReadRecord<TRecord>(batch, row, _model, includeVectors);
                    }
                }
            }
        }
    }

    public async IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord, bool>> filter,
        int top = 10,
        FilteredRecordRetrievalOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (top <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top), "Top must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (options?.IncludeVectors == true && _model.VectorProperties.Any(vp => !IsVectorTypeSupported(vp.Type)))
        {
            throw new NotSupportedException("When an embedding generator is configured, `Include Vectors` cannot be enabled.");
        }

        string? sqlFilter = null;
        if (filter.Body is not ConstantExpression { Value: true })
        {
            sqlFilter = LanceDbFilterTranslator.Translate(filter, _model);
        }

        LanceTable? openedTable;
        try
        {
            openedTable = await _adapter.OpenExistingTableAsync(_name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(GetAsync));
        }

        using LanceTable? table = openedTable;
        cancellationToken.ThrowIfCancellationRequested();
        if (table is null)
        {
            yield break;
        }

        RecordBatch batch;
        bool includeVectors = options?.IncludeVectors ?? false;
        FilteredRecordRetrievalOptions<TRecord>.OrderByDefinition? orderByDef = null;
        if (options?.OrderBy is not null)
        {
            orderByDef = options.OrderBy(new FilteredRecordRetrievalOptions<TRecord>.OrderByDefinition());
        }

        bool hasOrderBy = orderByDef is { Values.Count: > 0 };

        try
        {
            using lancedb.Query query = table.Query();

            if (sqlFilter is not null)
            {
                query.Where(sqlFilter);
            }

            if (!hasOrderBy)
            {
                query.Limit(top);
                if (options?.Skip > 0)
                {
                    query.Offset(options.Skip);
                }
            }

            if (!includeVectors && _model.VectorProperties.Count > 0)
            {
                List<string> nonVectorCols = _model.Properties
                    .Where(p => p is not VectorPropertyModel)
                    .Select(p => p.StorageName)
                    .ToList();
                query.Select(nonVectorCols);
            }

            cancellationToken.ThrowIfCancellationRequested();
            batch = await query.ToArrow().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(GetAsync));
        }

        using (batch)
        {
            List<TRecord> records = new(batch.Length);
            for (int row = 0; row < batch.Length; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                records.Add(ArrowRecordBatchMapper.ReadRecord<TRecord>(batch, row, _model, includeVectors));
            }

            IEnumerable<TRecord> resultSequence = records;
            if (hasOrderBy && orderByDef is not null)
            {
                IOrderedEnumerable<TRecord>? ordered = null;
                for (int i = 0; i < orderByDef.Values.Count; i++)
                {
                    FilteredRecordRetrievalOptions<TRecord>.OrderByDefinition.SortInfo sortInfo = orderByDef.Values[i];
                    Func<TRecord, object?> selector = sortInfo.PropertySelector.Compile();
                    if (i == 0)
                    {
                        ordered = sortInfo.Ascending
                            ? resultSequence.OrderBy(selector)
                            : resultSequence.OrderByDescending(selector);
                    }
                    else
                    {
                        ordered = sortInfo.Ascending
                            ? ordered!.ThenBy(selector)
                            : ordered!.ThenByDescending(selector);
                    }
                }

                if (ordered is not null)
                {
                    resultSequence = ordered;
                }

                if (options?.Skip > 0)
                {
                    resultSequence = resultSequence.Skip(options.Skip);
                }

                resultSequence = resultSequence.Take(top);
            }

            foreach (TRecord item in resultSequence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
    }

    public async Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        await DeleteAsync([key], cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();

        List<object> keyList = [];
        foreach (TKey key in keys)
        {
            ArgumentNullException.ThrowIfNull(key);
            keyList.Add(key);
        }

        if (keyList.Count == 0)
        {
            return;
        }

        try
        {
            Connection conn = await _adapter.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> tableNames = await conn.TableNames().ConfigureAwait(false);
            if (!tableNames.Contains(_name, StringComparer.Ordinal))
            {
                return;
            }

            using LanceTable table = await conn.OpenTable(_name).ConfigureAwait(false);
            const int maxKeyBatchSize = 1000;
            for (int i = 0; i < keyList.Count; i += maxKeyBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(maxKeyBatchSize, keyList.Count - i);
                IReadOnlyList<object> chunk = keyList.GetRange(i, count);
                string filter = LanceDbSqlHelper.BuildKeyFilter(_model.KeyProperty.StorageName, chunk);
                await table.Delete(filter).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(DeleteAsync));
        }
    }

    public async Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        await UpsertAsync([record], cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<object, TRecord> deduplicated = [];
        foreach (TRecord record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            object key = _model.KeyProperty.GetValueAsObject(record)
                ?? throw new InvalidOperationException("Record key must not be null.");
            deduplicated[key] = record;
        }

        if (deduplicated.Count == 0)
        {
            return;
        }

        List<TRecord> list = deduplicated.Values.ToList();

        Dictionary<VectorPropertyModel, IReadOnlyList<object?>>? generatedEmbeddings = null;
        foreach (VectorPropertyModel vp in _model.VectorProperties)
        {
            if (vp.EmbeddingGenerationDispatcher is not null)
            {
                Type[] supportedTypes = vp.GetSupportedInputTypes();
                List<object> inputs = new(list.Count);
                List<int> indices = new(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    object? val = vp.GetValueAsObject(list[i]);
                    if (val is not null && supportedTypes.Any(t => t.IsInstanceOfType(val)))
                    {
                        inputs.Add(val);
                        indices.Add(i);
                    }
                }

                if (inputs.Count > 0)
                {
                    IReadOnlyList<Embedding> generated = await vp.GenerateEmbeddingsAsync(inputs, cancellationToken).ConfigureAwait(false);
                    object?[] forRecords = new object?[list.Count];
                    for (int j = 0; j < inputs.Count; j++)
                    {
                        int recordIndex = indices[j];
                        Embedding emb = generated[j];
                        forRecords[recordIndex] = emb;

                        if (vp.Type == typeof(ReadOnlyMemory<float>) && emb is Embedding<float> floatEmb)
                        {
                            vp.SetValueAsObject(list[recordIndex], floatEmb.Vector);
                        }
                        else if (vp.Type.IsInstanceOfType(emb))
                        {
                            vp.SetValueAsObject(list[recordIndex], emb);
                        }
                    }

                    generatedEmbeddings ??= [];
                    generatedEmbeddings[vp] = forRecords;
                }
            }
        }

        try
        {
            Connection conn = await _adapter.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

            using LanceTable table = await conn.OpenTable(_name).ConfigureAwait(false);

            const int maxBatchSize = 5000;
            for (int i = 0; i < list.Count; i += maxBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(maxBatchSize, list.Count - i);
                IReadOnlyList<TRecord> chunk = list.GetRange(i, count);

                Dictionary<VectorPropertyModel, IReadOnlyList<object?>>? chunkGenerated = null;
                if (generatedEmbeddings is not null)
                {
                    chunkGenerated = new(generatedEmbeddings.Count);
                    foreach (KeyValuePair<VectorPropertyModel, IReadOnlyList<object?>> kvp in generatedEmbeddings)
                    {
                        chunkGenerated[kvp.Key] = kvp.Value.Skip(i).Take(count).ToList();
                    }
                }

                using RecordBatch batch = ArrowRecordBatchMapper.BuildBatch(_model, _expectedSchema, chunk, chunkGenerated);

                const int maxRetries = 3;
                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        await table.MergeInsert(_model.KeyProperty.StorageName)
                            .WhenMatchedUpdateAll()
                            .WhenNotMatchedInsertAll()
                            .Execute(batch)
                            .ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex) when (attempt < maxRetries - 1 && ex.Message.Contains("CommitConflict", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(20 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            await LanceDbIndexManager.BuildPendingVectorIndexesAsync(table, _model, _options.IndexThreshold, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(UpsertAsync));
        }
    }

    public async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TVector>(
        TVector vector,
        int top = 10,
        VectorSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (top <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top), "Top must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        VectorPropertyModel targetProp = _model.GetVectorPropertyOrSingle(options ?? new VectorSearchOptions<TRecord>());

        if (options?.IncludeVectors == true && _model.VectorProperties.Any(vp => !IsVectorTypeSupported(vp.Type)))
        {
            throw new NotSupportedException("When an embedding generator is configured, `Include Vectors` cannot be enabled.");
        }

        float[] floatArray;
        if (vector is ReadOnlyMemory<float> rom)
        {
            floatArray = rom.ToArray();
        }
        else if (vector is float[] arr)
        {
            floatArray = arr;
        }
        else if (vector is Embedding<float> emb)
        {
            floatArray = emb.Vector.ToArray();
        }
        else if (targetProp.EmbeddingGenerationDispatcher is not null)
        {
            Embedding embeddingObj = await targetProp.GenerateEmbeddingAsync(vector, cancellationToken).ConfigureAwait(false);
            if (embeddingObj is Embedding<float> embF)
            {
                floatArray = embF.Vector.ToArray();
            }
            else
            {
                throw new NotSupportedException($"Generated embedding type '{embeddingObj?.GetType().Name}' is not supported.");
            }
        }
        else
        {
            throw new NotSupportedException($"A value of type '{(typeof(TVector) == typeof(string) ? "string" : typeof(TVector).Name)}' was passed to 'SearchAsync', but that isn't a supported vector type by your provider and no embedding generator was configured. The supported vector types are: ReadOnlyMemory<float>, float[], Embedding<float>.");
        }

        if (targetProp.Dimensions > 0 && floatArray.Length != targetProp.Dimensions)
        {
            throw new ArgumentException($"Vector dimension {floatArray.Length} does not match expected dimension {targetProp.Dimensions}.", nameof(vector));
        }

        LanceDbDistanceMetric metric = LanceDbDistanceMetric.FromName(targetProp.DistanceFunction);

        LanceTable? openedTable;
        try
        {
            openedTable = await _adapter.OpenExistingTableAsync(_name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(SearchAsync));
        }

        using LanceTable? table = openedTable;
        cancellationToken.ThrowIfCancellationRequested();
        if (table is null)
        {
            yield break;
        }

        RecordBatch batch;
        bool includeVectors = options?.IncludeVectors ?? false;
        try
        {
            using lancedb.Query tq = table.Query();
            using lancedb.VectorQuery vq = tq.NearestTo(floatArray).DistanceType(metric.DistanceType);

            if (!string.Equals(targetProp.StorageName, "vector", StringComparison.Ordinal))
            {
                vq.Column(targetProp.StorageName);
            }

            int limit = Math.Max(1, top);
            vq.Limit(limit);

            // Approximate (HNSW) indexes search with an ef of about 1.5 * limit, which loses recall on small limits; refining widens the candidate pool and re-ranks with exact distances.
            vq.RefineFactor(10);

            if (options?.Skip > 0)
            {
                vq.Offset(options.Skip);
            }

            if (options?.Filter is not null)
            {
                string sqlFilter = LanceDbFilterTranslator.Translate(options.Filter, _model);
                vq.Where(sqlFilter);
            }

            if (!includeVectors && _model.VectorProperties.Count > 0)
            {
                List<string> nonVectorCols = _model.Properties
                    .Where(p => p is not VectorPropertyModel)
                    .Select(p => p.StorageName)
                    .ToList();
                vq.Select(nonVectorCols);
            }

            cancellationToken.ThrowIfCancellationRequested();
            batch = await vq.ToArrow().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(SearchAsync));
        }

        using (batch)
        {
            FloatArray? distArray = batch.Column("_distance") as FloatArray;
            for (int row = 0; row < batch.Length; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double? score = null;
                if (distArray is not null && !distArray.IsNull(row))
                {
                    float distVal = distArray.GetValue(row)!.Value;
                    score = metric.ToScore(distVal);
                }

                if (options?.ScoreThreshold is double threshold)
                {
                    if (!score.HasValue)
                    {
                        continue;
                    }

                    bool qualifies = metric.HigherIsBetter ? score.Value >= threshold : score.Value <= threshold;
                    if (!qualifies)
                    {
                        continue;
                    }
                }

                TRecord record = ArrowRecordBatchMapper.ReadRecord<TRecord>(batch, row, _model, includeVectors);
                yield return new VectorSearchResult<TRecord>(record, score);
            }
        }
    }

    public async IAsyncEnumerable<VectorSearchResult<TRecord>> HybridSearchAsync<TInput>(
        TInput searchValue,
        ICollection<string> keywords,
        int top,
        HybridSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TInput : notnull
    {
        ArgumentNullException.ThrowIfNull(searchValue);
        ArgumentNullException.ThrowIfNull(keywords);
        if (top <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top), "Top must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        DataPropertyModel textProperty;
        try
        {
            textProperty = _model.GetFullTextDataPropertyOrSingle(options?.AdditionalProperty);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"Hybrid search on collection '{_name}' needs exactly one full-text indexed property, or one named in the options: {ex.Message}", ex);
        }

        string queryText = string.Join(' ', keywords.Where(k => !string.IsNullOrWhiteSpace(k)));
        if (queryText.Length == 0)
        {
            throw new ArgumentException("At least one keyword is required.", nameof(keywords));
        }

        int skip = Math.Max(0, options?.Skip ?? 0);
        int depth = Math.Max(top + skip, LanceDbHybridSearch.MinimumDepth);
        bool includeVectors = options?.IncludeVectors ?? false;

        List<(object Key, TRecord Record)> vectorList = [];
        await foreach (VectorSearchResult<TRecord> hit in SearchAsync(
            searchValue,
            depth,
            new VectorSearchOptions<TRecord>
            {
                VectorProperty = options?.VectorProperty,
                Filter = options?.Filter,
                IncludeVectors = includeVectors,
            },
            cancellationToken).ConfigureAwait(false))
        {
            vectorList.Add((_model.KeyProperty.GetValueAsObject(hit.Record)!, hit.Record));
        }

        List<(object Key, TRecord Record)> textList = [];
        try
        {
            using LanceTable? table = await _adapter.OpenExistingTableAsync(_name, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (table is not null)
            {
                string? sqlFilter = options?.Filter is null ? null : LanceDbFilterTranslator.Translate(options.Filter, _model);
                List<string>? select = includeVectors
                    ? null
                    : _model.Properties.Where(p => p is not VectorPropertyModel).Select(p => p.StorageName).ToList();
                using RecordBatch batch = await LanceDbHybridSearch.RunFullTextQueryAsync(table, queryText, textProperty.StorageName, sqlFilter, select, depth, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                for (int row = 0; row < batch.Length; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TRecord record = ArrowRecordBatchMapper.ReadRecord<TRecord>(batch, row, _model, includeVectors);
                    textList.Add((_model.KeyProperty.GetValueAsObject(record)!, record));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw VectorStoreExceptionHelper.Wrap(ex, collectionName: _name, operationName: nameof(HybridSearchAsync));
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach ((TRecord record, double score) in LanceDbHybridSearch.Fuse([vectorList, textList], cancellationToken).Skip(skip).Take(top))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options?.ScoreThreshold is double threshold && score < threshold)
            {
                continue;
            }

            yield return new VectorSearchResult<TRecord>(record, score);
        }
    }

    private static bool IsVectorTypeSupported(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(ReadOnlyMemory<float>)
            || underlying == typeof(float[])
            || (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(Embedding<>) && underlying.GenericTypeArguments[0] == typeof(float))
            || underlying == typeof(object);
    }
}
