using Microsoft.Extensions.AI;

namespace PinkRooster.Extensions.VectorData.LanceDB.Samples.DataIngestion;

/// <summary>An offline, deterministic embedding generator: it hashes each word into a bucket and normalizes the counts.</summary>
internal sealed class HashingEmbeddingGenerator(int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GeneratedEmbeddings<Embedding<float>> result = [];
        using IEnumerator<string> inputs = values.GetEnumerator();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!inputs.MoveNext())
            {
                return Task.FromResult(result);
            }

            cancellationToken.ThrowIfCancellationRequested();
            string value = inputs.Current;
            float[] vector = new float[dimensions];
            foreach (string word in value.Split([' ', '.', ',', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                vector[Bucket(word.ToUpperInvariant())]++;
            }

            float norm = MathF.Sqrt(vector.Sum(x => x * x));
            if (norm > 0)
            {
                for (int i = 0; i < vector.Length; i++)
                {
                    vector[i] /= norm;
                }
            }

            result.Add(new Embedding<float>(vector));
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    // A stable hash: string.GetHashCode is randomized per process.
    private int Bucket(string word)
    {
        uint hash = 2166136261;
        foreach (char c in word)
        {
            hash = (hash ^ c) * 16777619;
        }

        return (int)(hash % (uint)dimensions);
    }
}
