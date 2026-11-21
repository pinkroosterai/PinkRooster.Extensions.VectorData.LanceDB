using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB;

/// <summary>Options for configuring a <see cref="LanceDbCollection{TKey, TRecord}"/> or <see cref="LanceDbDynamicCollection"/>.</summary>
public sealed class LanceDbCollectionOptions
{
    /// <summary>Gets or sets the collection definition specifying property schemas and attributes.</summary>
    public VectorStoreCollectionDefinition? Definition { get; set; }

    /// <summary>Gets or sets the embedding generator for this collection.</summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }

    /// <summary>Gets or sets the row count at which a pending vector index is built automatically. Default is 100,000.</summary>
    public int IndexThreshold { get; set; } = 100_000;

    /// <summary>Gets or sets the default distance function. Default is <see cref="DistanceFunction.CosineSimilarity"/>.</summary>
    public string DefaultDistanceFunction { get; set; } = DistanceFunction.CosineSimilarity;
}
