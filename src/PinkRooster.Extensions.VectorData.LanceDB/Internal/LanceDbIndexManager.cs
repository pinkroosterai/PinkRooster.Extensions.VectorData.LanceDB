using lancedb;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;
using LanceTable = lancedb.Table;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

internal static class LanceDbIndexManager
{
    public static async Task EnsureInitialIndexesAsync(LanceTable table, CollectionModel model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<IndexConfig>? existing = null;
        foreach (DataPropertyModel property in model.Properties.OfType<DataPropertyModel>())
        {
            if (!property.IsIndexed && !property.IsFullTextIndexed)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            existing ??= await table.ListIndices().ConfigureAwait(false);
            string column = property.StorageName;
            if (property.IsIndexed)
            {
                (lancedb.Index index, IndexType type) = GetScalarIndex(property.Type);
                await EnsureIndexAsync(table, existing, column, $"{column}_idx", index, type, cancellationToken).ConfigureAwait(false);
            }

            if (property.IsFullTextIndexed)
            {
                await EnsureIndexAsync(table, existing, column, $"{column}_fts_idx", new FtsIndex(), IndexType.FTS, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task EnsureIndexAsync(
        LanceTable table, IReadOnlyList<IndexConfig> existing, string column, string name,
        lancedb.Index index, IndexType indexType, CancellationToken cancellationToken)
    {
        bool Matches(IndexConfig i) =>
            i.Name == name && i.IndexType == indexType && i.Columns.Count == 1 && i.Columns[0] == column;

        cancellationToken.ThrowIfCancellationRequested();
        if (existing.Any(Matches))
        {
            return;
        }

        try
        {
            await table.CreateIndex([column], index, replace: false, name: name).ConfigureAwait(false);
        }
        catch (LanceDbException ex) when (
            ex.Message.Contains($"Index name '{name}' already exists,", StringComparison.Ordinal) ||
            ex.Message.Contains("Retryable commit conflict", StringComparison.Ordinal))
        {
            // A concurrent writer can win after our metadata read. Refresh and only accept the expected index.
            cancellationToken.ThrowIfCancellationRequested();
            await table.CheckoutLatest().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!(await table.ListIndices().ConfigureAwait(false)).Any(Matches))
            {
                throw;
            }
        }
    }

    public static async Task BuildPendingVectorIndexesAsync(LanceTable table, CollectionModel model, int threshold, CancellationToken cancellationToken)
    {
        List<VectorPropertyModel> candidates = model.VectorProperties
            .Where(vp => vp.IndexKind == IndexKind.IvfFlat || vp.IndexKind == IndexKind.Hnsw)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        try
        {
            long rows = await table.CountRows().ConfigureAwait(false);
            if (rows < threshold)
            {
                return;
            }

            HashSet<string> existing = (await table.ListIndices().ConfigureAwait(false)).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
            foreach (VectorPropertyModel vp in candidates)
            {
                string indexName = $"{vp.StorageName}_vec_idx";
                if (existing.Contains(indexName))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                DistanceType distance = LanceDbDistanceMetric.FromName(vp.DistanceFunction).DistanceType;
                // Roughly 256 rows per partition keeps small tables from being split into near-empty partitions.
                int partitions = (int)Math.Clamp(rows / 256, 1, int.MaxValue);
                lancedb.Index index = vp.IndexKind == IndexKind.Hnsw
                    ? new HnswSqIndex { DistanceType = distance, NumPartitions = partitions }
                    : new IvfFlatIndex { DistanceType = distance, NumPartitions = partitions };
                await table.CreateIndex([vp.StorageName], index, name: indexName).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // The data is already written; a failed build is retried after the next upsert.
        }
    }

    private static (lancedb.Index Index, IndexType Type) GetScalarIndex(Type propertyType)
    {
        return LanceDbDataTypes.Classify(propertyType) switch
        {
            LanceDbDataKind.Boolean => (new BitmapIndex(), IndexType.Bitmap),
            LanceDbDataKind.StringArray or LanceDbDataKind.StringList => (new LabelListIndex(), IndexType.LabelList),
            _ => (new BTreeIndex(), IndexType.BTree),
        };
    }
}
