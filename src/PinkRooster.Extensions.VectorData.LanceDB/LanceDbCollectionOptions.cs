using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB;

/// <summary>Options for configuring a <see cref="LanceDbCollection{TKey, TRecord}"/> or <see cref="LanceDbDynamicCollection"/>.</summary>
public sealed class LanceDbCollectionOptions
{
    /// <summary>Gets or sets the definition that describes the collection's key, data, and vector properties.</summary>
    /// <remarks>
    /// A directly constructed <see cref="LanceDbDynamicCollection"/> requires a definition.
    /// For a strongly typed collection, the schema can be inferred from the record type when no definition is available.
    /// </remarks>
    public VectorStoreCollectionDefinition? Definition { get; set; }

    /// <summary>Gets or sets the embedding generator used to convert record values and search inputs to vectors.</summary>
    /// <value>The collection-level generator, or <see langword="null"/> to leave it unset.</value>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }

    /// <summary>Gets or sets the minimum table row count for attempting to build pending vector indexes.</summary>
    /// <value>The row count threshold. The default is 100,000.</value>
    /// <remarks>The threshold is checked after an upsert and applies to configured IVF Flat and HNSW indexes.</remarks>
    public int IndexThreshold { get; set; } = 100_000;

    /// <summary>Gets or sets the distance function used when a vector property does not specify one.</summary>
    /// <value>The distance function name. The default is <see cref="DistanceFunction.CosineSimilarity"/>.</value>
    public string DefaultDistanceFunction { get; set; } = DistanceFunction.CosineSimilarity;
}
