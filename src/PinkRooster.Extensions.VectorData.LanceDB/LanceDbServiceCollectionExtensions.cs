using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using PinkRooster.Extensions.VectorData.LanceDB;
using PinkRooster.Extensions.VectorData.LanceDB.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers local LanceDB vector stores and strongly typed collections with <see cref="IServiceCollection"/>.</summary>
public static class LanceDbServiceCollectionExtensions
{
    /// <summary>Registers a LanceDB vector store.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="databaseDirectory">The path to the local database directory.</param>
    /// <param name="lifetime">The lifetime of the registrations. The default is <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="LanceDbVectorStore"/> as itself and as <see cref="VectorStore"/>.
    /// The store looks for an unkeyed <see cref="IEmbeddingGenerator"/> when resolved.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="databaseDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="databaseDirectory"/> is empty or consists only of white-space characters.
    /// </exception>
    public static IServiceCollection AddLanceDbVectorStore(
        this IServiceCollection services,
        string databaseDirectory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);

        return services.AddLanceDbVectorStore(_ => databaseDirectory, lifetime);
    }

    /// <summary>Registers a LanceDB vector store with a database directory resolved by a factory.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="databaseDirectoryFactory">A factory that receives an <see cref="IServiceProvider"/> and returns the local database directory when the service is resolved.</param>
    /// <param name="lifetime">The lifetime of the registrations. The default is <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="LanceDbVectorStore"/> as itself and as <see cref="VectorStore"/>.
    /// The store looks for an unkeyed <see cref="IEmbeddingGenerator"/> when resolved.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="databaseDirectoryFactory"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddLanceDbVectorStore(
        this IServiceCollection services,
        Func<IServiceProvider, string> databaseDirectoryFactory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(databaseDirectoryFactory);

        services.Add(new ServiceDescriptor(typeof(LanceDbVectorStore), sp =>
        {
            string dir = databaseDirectoryFactory(sp);
            LanceDbVectorStoreOptions options = new()
            {
                EmbeddingGenerator = sp.GetService<IEmbeddingGenerator>(),
            };
            return new LanceDbVectorStore(dir, options);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), sp => sp.GetRequiredService<LanceDbVectorStore>(), lifetime));

        return services;
    }

    /// <summary>Registers a keyed LanceDB vector store.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="serviceKey">The key for the registration, or <see langword="null"/> for an unkeyed registration.</param>
    /// <param name="databaseDirectory">The path to the local database directory.</param>
    /// <param name="lifetime">The lifetime of the registrations. The default is <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="LanceDbVectorStore"/> as itself and as <see cref="VectorStore"/>.
    /// The store looks for a generator registered with the same non-null service key, then an unkeyed generator when resolved.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="databaseDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="databaseDirectory"/> is empty or consists only of white-space characters.
    /// </exception>
    public static IServiceCollection AddKeyedLanceDbVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        string databaseDirectory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);

        return services.AddKeyedLanceDbVectorStore(serviceKey, (_, _) => databaseDirectory, lifetime);
    }

    /// <summary>Registers a keyed LanceDB vector store with a database directory resolved by a factory.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="serviceKey">The key for the registration, or <see langword="null"/> for an unkeyed registration.</param>
    /// <param name="databaseDirectoryFactory">A factory that receives an <see cref="IServiceProvider"/> and the service key and returns the local database directory when the service is resolved.</param>
    /// <param name="lifetime">The lifetime of the registrations. The default is <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="LanceDbVectorStore"/> as itself and as <see cref="VectorStore"/>.
    /// The store looks for a generator registered with the same non-null service key, then an unkeyed generator when resolved.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="databaseDirectoryFactory"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddKeyedLanceDbVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, object?, string> databaseDirectoryFactory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(databaseDirectoryFactory);

        services.Add(new ServiceDescriptor(typeof(LanceDbVectorStore), serviceKey, (sp, key) =>
        {
            string dir = databaseDirectoryFactory(sp, key);
            LanceDbVectorStoreOptions options = new()
            {
                EmbeddingGenerator = (key is not null ? sp.GetKeyedService<IEmbeddingGenerator>(key) : null) ?? sp.GetService<IEmbeddingGenerator>(),
            };
            return new LanceDbVectorStore(dir, options);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStore), serviceKey, (sp, key) => sp.GetRequiredKeyedService<LanceDbVectorStore>(key), lifetime));

        return services;
    }

    /// <summary>Registers a LanceDB collection.</summary>
    /// <typeparam name="TKey">The type of the record key.</typeparam>
    /// <typeparam name="TRecord">The type that represents a record in the collection.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="name">The table name. Use only ASCII letters, digits, underscores, hyphens, and periods.</param>
    /// <param name="databaseDirectory">The path to the local database directory.</param>
    /// <param name="options">The collection options, or <see langword="null"/> to use the defaults.</param>
    /// <param name="lifetime">The lifetime of the registrations. The default is <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="LanceDbCollection{TKey, TRecord}"/> as itself and as <see cref="VectorStoreCollection{TKey, TRecord}"/>,
    /// <see cref="IVectorSearchable{TRecord}"/>, and <see cref="IKeywordHybridSearchable{TRecord}"/>.
    /// The collection uses the generator supplied in <paramref name="options"/>. If none is supplied, it looks for an unkeyed <see cref="IEmbeddingGenerator"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/>, or <paramref name="databaseDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, consists only of white-space characters, or contains an invalid character.
    /// This exception also occurs if <paramref name="databaseDirectory"/> is empty or consists only of white-space characters.
    /// </exception>
    public static IServiceCollection AddLanceDbCollection<TKey, TRecord>(
        this IServiceCollection services,
        string name,
        string databaseDirectory,
        LanceDbCollectionOptions? options = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);

        return services.AddLanceDbCollection<TKey, TRecord>(name, _ => databaseDirectory, options, lifetime);
    }

    /// <summary>Registers a LanceDB collection with a database directory resolved by a factory.</summary>
    /// <typeparam name="TKey">The type of the record key.</typeparam>
    /// <typeparam name="TRecord">The type that represents a record in the collection.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="name">The table name. Use only ASCII letters, digits, underscores, hyphens, and periods.</param>
    /// <param name="databaseDirectoryFactory">A factory that receives an <see cref="IServiceProvider"/> and returns the local database directory when the service is resolved.</param>
    /// <param name="options">The collection options, or <see langword="null"/> to use the defaults.</param>
    /// <param name="lifetime">The lifetime of the registrations. The default is <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="LanceDbCollection{TKey, TRecord}"/> as itself and as <see cref="VectorStoreCollection{TKey, TRecord}"/>,
    /// <see cref="IVectorSearchable{TRecord}"/>, and <see cref="IKeywordHybridSearchable{TRecord}"/>.
    /// The collection uses the generator supplied in <paramref name="options"/>. If none is supplied, it looks for an unkeyed <see cref="IEmbeddingGenerator"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/>, or <paramref name="databaseDirectoryFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, consists only of white-space characters, or contains an invalid character.
    /// </exception>
    public static IServiceCollection AddLanceDbCollection<TKey, TRecord>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, string> databaseDirectoryFactory,
        LanceDbCollectionOptions? options = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);
        ArgumentNullException.ThrowIfNull(databaseDirectoryFactory);

        services.Add(new ServiceDescriptor(typeof(LanceDbCollection<TKey, TRecord>), sp =>
        {
            string dir = databaseDirectoryFactory(sp);
            LanceDbCollectionOptions collOptions = options != null
                ? new LanceDbCollectionOptions
                {
                    Definition = options.Definition,
                    EmbeddingGenerator = options.EmbeddingGenerator ?? sp.GetService<IEmbeddingGenerator>(),
                    IndexThreshold = options.IndexThreshold,
                    DefaultDistanceFunction = options.DefaultDistanceFunction,
                }
                : new LanceDbCollectionOptions
                {
                    EmbeddingGenerator = sp.GetService<IEmbeddingGenerator>(),
                };
            return new LanceDbCollection<TKey, TRecord>(dir, name, collOptions);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStoreCollection<TKey, TRecord>), sp => sp.GetRequiredService<LanceDbCollection<TKey, TRecord>>(), lifetime));
        services.Add(new ServiceDescriptor(typeof(IVectorSearchable<TRecord>), sp => sp.GetRequiredService<LanceDbCollection<TKey, TRecord>>(), lifetime));
        services.Add(new ServiceDescriptor(typeof(IKeywordHybridSearchable<TRecord>), sp => sp.GetRequiredService<LanceDbCollection<TKey, TRecord>>(), lifetime));

        return services;
    }

    /// <summary>Registers a keyed LanceDB collection.</summary>
    /// <typeparam name="TKey">The type of the record key.</typeparam>
    /// <typeparam name="TRecord">The type that represents a record in the collection.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="serviceKey">The key for the registration, or <see langword="null"/> for an unkeyed registration.</param>
    /// <param name="name">The table name. Use only ASCII letters, digits, underscores, hyphens, and periods.</param>
    /// <param name="databaseDirectory">The path to the local database directory.</param>
    /// <param name="options">The collection options, or <see langword="null"/> to use the defaults.</param>
    /// <param name="lifetime">The lifetime of the registrations. The default is <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="LanceDbCollection{TKey, TRecord}"/> as itself and as <see cref="VectorStoreCollection{TKey, TRecord}"/>,
    /// <see cref="IVectorSearchable{TRecord}"/>, and <see cref="IKeywordHybridSearchable{TRecord}"/>.
    /// The collection uses the generator supplied in <paramref name="options"/>. If none is supplied, it looks for a generator registered with the same non-null service key, then an unkeyed generator.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/>, or <paramref name="databaseDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, consists only of white-space characters, or contains an invalid character.
    /// This exception also occurs if <paramref name="databaseDirectory"/> is empty or consists only of white-space characters.
    /// </exception>
    public static IServiceCollection AddKeyedLanceDbCollection<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        string databaseDirectory,
        LanceDbCollectionOptions? options = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);

        return services.AddKeyedLanceDbCollection<TKey, TRecord>(serviceKey, name, (_, _) => databaseDirectory, options, lifetime);
    }

    /// <summary>Registers a keyed LanceDB collection with a database directory resolved by a factory.</summary>
    /// <typeparam name="TKey">The type of the record key.</typeparam>
    /// <typeparam name="TRecord">The type that represents a record in the collection.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="serviceKey">The key for the registration, or <see langword="null"/> for an unkeyed registration.</param>
    /// <param name="name">The table name. Use only ASCII letters, digits, underscores, hyphens, and periods.</param>
    /// <param name="databaseDirectoryFactory">A factory that receives an <see cref="IServiceProvider"/> and the service key and returns the local database directory when the service is resolved.</param>
    /// <param name="options">The collection options, or <see langword="null"/> to use the defaults.</param>
    /// <param name="lifetime">The lifetime of the registrations. The default is <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="LanceDbCollection{TKey, TRecord}"/> as itself and as <see cref="VectorStoreCollection{TKey, TRecord}"/>,
    /// <see cref="IVectorSearchable{TRecord}"/>, and <see cref="IKeywordHybridSearchable{TRecord}"/>.
    /// The collection uses the generator supplied in <paramref name="options"/>. If none is supplied, it looks for a generator registered with the same non-null service key, then an unkeyed generator.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="name"/>, or <paramref name="databaseDirectoryFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, consists only of white-space characters, or contains an invalid character.
    /// </exception>
    public static IServiceCollection AddKeyedLanceDbCollection<TKey, TRecord>(
        this IServiceCollection services,
        object? serviceKey,
        string name,
        Func<IServiceProvider, object?, string> databaseDirectoryFactory,
        LanceDbCollectionOptions? options = null,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TKey : notnull
        where TRecord : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LanceDbNameValidator.Validate(name);
        ArgumentNullException.ThrowIfNull(databaseDirectoryFactory);

        services.Add(new ServiceDescriptor(typeof(LanceDbCollection<TKey, TRecord>), serviceKey, (sp, key) =>
        {
            string dir = databaseDirectoryFactory(sp, key);
            IEmbeddingGenerator? generator = options?.EmbeddingGenerator
                ?? (key is not null ? sp.GetKeyedService<IEmbeddingGenerator>(key) : null)
                ?? sp.GetService<IEmbeddingGenerator>();

            LanceDbCollectionOptions collOptions = options != null
                ? new LanceDbCollectionOptions
                {
                    Definition = options.Definition,
                    EmbeddingGenerator = generator,
                    IndexThreshold = options.IndexThreshold,
                    DefaultDistanceFunction = options.DefaultDistanceFunction,
                }
                : new LanceDbCollectionOptions
                {
                    EmbeddingGenerator = generator,
                };
            return new LanceDbCollection<TKey, TRecord>(dir, name, collOptions);
        }, lifetime));

        services.Add(new ServiceDescriptor(typeof(VectorStoreCollection<TKey, TRecord>), serviceKey, (sp, key) => sp.GetRequiredKeyedService<LanceDbCollection<TKey, TRecord>>(key), lifetime));
        services.Add(new ServiceDescriptor(typeof(IVectorSearchable<TRecord>), serviceKey, (sp, key) => sp.GetRequiredKeyedService<LanceDbCollection<TKey, TRecord>>(key), lifetime));
        services.Add(new ServiceDescriptor(typeof(IKeywordHybridSearchable<TRecord>), serviceKey, (sp, key) => sp.GetRequiredKeyedService<LanceDbCollection<TKey, TRecord>>(key), lifetime));

        return services;
    }
}
