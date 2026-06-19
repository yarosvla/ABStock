namespace ABStock.AI.Internal;

internal static class CosineSimilarityHelper
{
    public static decimal Calculate(
        float[] vectorA,
        float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException(
                "Embedding vectors must have same length.");
        }

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];

            magnitudeA += vectorA[i] * vectorA[i];

            magnitudeB += vectorB[i] * vectorB[i];
        }

        if (magnitudeA == 0 || magnitudeB == 0)
        {
            return 0m;
        }

        var similarity =
            dotProduct /
            (Math.Sqrt(magnitudeA) *
             Math.Sqrt(magnitudeB));

        return (decimal) similarity;
    }
}