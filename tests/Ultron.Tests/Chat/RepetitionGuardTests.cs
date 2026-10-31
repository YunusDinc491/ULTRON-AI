using Ultron.Core.Chat;

namespace Ultron.Tests.Chat;

public class RepetitionGuardTests
{
    [Fact]
    public void Ayni_parca_art_arda_tekrarlaninca_yakalar_ve_kirpar()
    {
        var guard = new RepetitionGuard();
        Assert.False(guard.Append("Bu kod ilk çağrıda [1] yazdırır, sonra "));
        var tripped = false;
        for (var i = 0; i < 20 && !tripped; i++)
            tripped = guard.Append("[1] ");
        Assert.True(tripped);
        Assert.Equal("Bu kod ilk çağrıda [1] yazdırır, sonra …", RepetitionGuard.Trim("Bu kod ilk çağrıda [1] yazdırır, sonra " + string.Concat(Enumerable.Repeat("[1] ", 20))));
    }

    [Theory]
    [InlineData("1. Adım\n2. Adım\n3. Adım\n4. Adım")]
    [InlineData("Normal bir cümle, hiçbir tekrar yok. İkinci cümle de farklı.")]
    [InlineData("```\n========\n```")]
    [InlineData("Başlık\n====================")]
    public void Normal_metinde_tetiklenmez(string text) => Assert.False(new RepetitionGuard().Append(text));
}
