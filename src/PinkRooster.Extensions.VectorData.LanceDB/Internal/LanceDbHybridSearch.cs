using Apache.Arrow;
using lancedb;
using LanceTable = lancedb.Table;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

/// <summary>Helpers for provider-side hybrid search: a full-text query and reciprocal-rank fusion.</summary>
internal static class LanceDbHybridSearch
{
    /// <summary>The offset added to each one-based rank before computing its reciprocal.</summary>
    public const int RankConstant = 60;

    /// <summary>The lower bound on the candidate limit for each search before fusion.</summary>
    public const int MinimumDepth = 50;

    public static async Task<RecordBatch> RunFullTextQueryAsync(
        LanceTable table,
        string text,
        string column,
        string? sqlFilter,
        IReadOnlyList<string>? select,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using lancedb.Query baseQuery = table.Query();
        using lancedb.FTSQuery query = baseQuery.NearestToText(text, [column]);
        if (sqlFilter is not null)
        {
            query.Where(sqlFilter);
        }

        if (select is not null)
        {
            query.Select(select);
        }

        query.Limit(limit);
        cancellationToken.ThrowIfCancellationRequested();
        return await query.ToArrow().ConfigureAwait(false);
    }

    /// <summary>Combines ranked result lists by summing reciprocal-rank contributions for each record key.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="rankedLists">The input lists, each ordered from highest to lowest rank.</param>
    /// <param name="cancellationToken">The token to observe for cancellation.</param>
    /// <returns>One result per key, ordered by descending score, using the first record encountered for each key.</returns>
    public static List<(T Record, double Score)> Fuse<T>(IEnumerable<IReadOnlyList<(object Key, T Record)>> rankedLists, CancellationToken cancellationToken)
    {
        Dictionary<object, int> positions = [];
        List<(T Record, double Score)> entries = [];
        foreach (IReadOnlyList<(object Key, T Record)> list in rankedLists)
        {
            for (int rank = 0; rank < list.Count; rank++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double contribution = 1.0 / (RankConstant + rank + 1);
                (object key, T record) = list[rank];
                if (positions.TryGetValue(key, out int index))
                {
                    entries[index] = (entries[index].Record, entries[index].Score + contribution);
                }
                else
                {
                    positions[key] = entries.Count;
                    entries.Add((record, contribution));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return [.. entries.OrderByDescending(e => e.Score)];
    }
}
