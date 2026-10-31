namespace Ultron.Core.Embeddings;

public static class VectorMath
{
    /// <summary>
    /// Kosinüs benzerliği: iki vektör aynı yöne bakıyorsa 1, alakasızsa 0'a yakın.
    /// Uzunluklarına değil yönlerine bakar; yani cümlenin uzunluğu değil anlamı önemlidir.
    /// </summary>
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vektör boyutları farklı: {a.Length} ve {b.Length}.");

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
