using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB;

/// <summary>Options for configuring a <see cref="LanceDbVectorStore"/>.</summary>
public sealed class LanceDbVectorStoreOptions
{
    /// <summary>Gets or sets the default schema for strongly typed collections that do not supply a definition.</summary>
    /// <value>The default definition, or <see langword="null"/> to infer the schema from the record type.</value>
    public VectorStoreCollectionDefinition? DefaultCollectionDefinition { get; set; }

    /// <summary>Gets or sets the embedding generator passed to collections created by the store.</summary>
    /// <value>The generator to use, or <see langword="null"/> to leave the store-level generator unset.</value>
    public IEmbeddingGenerator? EmbeddingGenerator { get; set; }
}
