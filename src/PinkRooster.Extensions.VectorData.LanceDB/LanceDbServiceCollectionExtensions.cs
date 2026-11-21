using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using PinkRooster.Extensions.VectorData.LanceDB;
using PinkRooster.Extensions.VectorData.LanceDB.Internal;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering LanceDB vector store and collections with <see cref="IServiceCollection"/>.</summary>
public static class LanceDbServiceCollectionExtensions
{
    /// <summary>Registers a <see cref="LanceDbVectorStore"/> and <see cref="VectorStore"/> as a service.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="databaseDirectory">The path to the local database directory.</param>
    /// <param name="lifetime">The lifetime of the service.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddLanceDbVectorStore(
        this IServiceCollection services,
        string databaseDirectory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);

        return services.AddLanceDbVectorStore(_ => databaseDirectory, lifetime);
    }

    /// <summary>Registers a <see cref="LanceDbVectorStore"/> and <see cref="VectorStore"/> using a factory for the database directory.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="databaseDirectoryFactory">A factory delegate that produces the database directory path from an <see cref="IServiceProvider"/>.</param>
    /// <param name="lifetime">The lifetime of the service.</param>
    /// <returns>The same service collection for chaining.</returns>
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

    /// <summary>Registers a keyed <see cref="LanceDbVectorStore"/> and <see cref="VectorStore"/> as a service.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="serviceKey">The key for the service.</param>
    /// <param name="databaseDirectory">The path to the local database directory.</param>
    /// <param name="lifetime">The lifetime of the service.</param>
    /// <returns>The same service collection for chaining.</returns>
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

    /// <summary>Registers a keyed <see cref="LanceDbVectorStore"/> and <see cref="VectorStore"/> using a factory for the database directory.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="serviceKey">The key for the service.</param>
    /// <param name="databaseDirectoryFactory">A factory delegate that produces the database directory path from an <see cref="IServiceProvider"/> and the service key.</param>
    /// <param name="lifetime">The lifetime of the service.</param>
    /// <returns>The same service collection for chaining.</returns>
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

    /// <summary>Registers a <see cref="LanceDbCollection{TKey, TRecord}"/> and <see cref="VectorStoreCollection{TKey, TRecord}"/> as a service.</summary>
    /// <typeparam name="TKey">The type of the record key.</typeparam>
    /// <typeparam name="TRecord">The record type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="name">The collection name.</param>
    /// <param name="databaseDirectory">The path to the local database directory.</param>
    /// <param name="options">Optional collection options.</param>
    /// <param name="lifetime">The lifetime of the service.</param>
    /// <returns>The same service collection for chaining.</returns>
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

    /// <summary>Registers a <see cref="LanceDbCollection{TKey, TRecord}"/> and <see cref="VectorStoreCollection{TKey, TRecord}"/> using a factory for the database directory.</summary>
    /// <typeparam name="TKey">The type of the record key.</typeparam>
    /// <typeparam name="TRecord">The record type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="name">The collection name.</param>
    /// <param name="databaseDirectoryFactory">A factory delegate that produces the database directory path from an <see cref="IServiceProvider"/>.</param>
    /// <param name="options">Optional collection options.</param>
    /// <param name="lifetime">The lifetime of the service.</param>
    /// <returns>The same service collection for chaining.</returns>
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

    /// <summary>Registers a keyed <see cref="LanceDbCollection{TKey, TRecord}"/> and <see cref="VectorStoreCollection{TKey, TRecord}"/> as a service.</summary>
    /// <typeparam name="TKey">The type of the record key.</typeparam>
    /// <typeparam name="TRecord">The record type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="serviceKey">The key for the service.</param>
    /// <param name="name">The collection name.</param>
    /// <param name="databaseDirectory">The path to the local database directory.</param>
    /// <param name="options">Optional collection options.</param>
    /// <param name="lifetime">The lifetime of the service.</param>
    /// <returns>The same service collection for chaining.</returns>
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

    /// <summary>Registers a keyed <see cref="LanceDbCollection{TKey, TRecord}"/> and <see cref="VectorStoreCollection{TKey, TRecord}"/> using a factory for the database directory.</summary>
    /// <typeparam name="TKey">The type of the record key.</typeparam>
    /// <typeparam name="TRecord">The record type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="serviceKey">The key for the service.</param>
    /// <param name="name">The collection name.</param>
    /// <param name="databaseDirectoryFactory">A factory delegate that produces the database directory path from an <see cref="IServiceProvider"/> and the service key.</param>
    /// <param name="options">Optional collection options.</param>
    /// <param name="lifetime">The lifetime of the service.</param>
    /// <returns>The same service collection for chaining.</returns>
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
