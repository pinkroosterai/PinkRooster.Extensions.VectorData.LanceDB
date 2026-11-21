using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB;

/// <summary>Options for configuring a <see cref="LanceDbVectorStore"/>.</summary>
public sealed class LanceDbVectorStoreOptions
{
    /// <summary>Gets or sets the default collection definition to use when none is supplied.</summary>
    public VectorStoreCollectionDefinition? DefaultCollectionDefinition { get; set; }

    /// <summary>Gets or sets the embedding generator to use when generating vector embeddings.</summary>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
