using lancedb;
using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

internal readonly record struct LanceDbDistanceMetric(DistanceType DistanceType, Func<float, double> ToScore, bool HigherIsBetter)
{
    public static LanceDbDistanceMetric FromName(string? name) => name switch
    {
        DistanceFunction.CosineSimilarity => new(DistanceType.Cosine, distance => 1.0 - distance, true),
        DistanceFunction.CosineDistance => new(DistanceType.Cosine, distance => distance, false),
        DistanceFunction.DotProductSimilarity => new(DistanceType.Dot, distance => 1.0 - distance, true),
        DistanceFunction.NegativeDotProductSimilarity => new(DistanceType.Dot, distance => distance - 1.0, false),
        DistanceFunction.EuclideanSquaredDistance => new(DistanceType.L2, distance => distance, false),
        DistanceFunction.EuclideanDistance => new(DistanceType.L2, distance => Math.Sqrt(Math.Max(0.0, distance)), false),
        _ => throw new NotSupportedException($"Distance function '{name}' is not supported by the LanceDB provider."),
    };
}
