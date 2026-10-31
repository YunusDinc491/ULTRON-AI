using Ultron.Core.Embeddings;

namespace Ultron.Tests.Memories;

public class VectorMathTests
{
    [Fact]
    public void Ayni_yon_1_dik_yon_0_verir()
    {
        Assert.Equal(1, VectorMath.Cosine([1f, 2f, 3f], [2f, 4f, 6f]), precision: 6); // uzunluk farklı, yön aynı
        Assert.Equal(0, VectorMath.Cosine([1f, 0f], [0f, 1f]), precision: 6);
    }

    [Fact]
    public void Sifir_vektor_0_verir() => Assert.Equal(0, VectorMath.Cosine([0f, 0f], [1f, 1f]));

    [Fact]
    public void Farkli_boyut_hata_verir() => Assert.Throws<ArgumentException>(() => VectorMath.Cosine([1f], [1f, 2f]));
}
