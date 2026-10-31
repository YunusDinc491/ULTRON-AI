using Ultron.Desktop.Voice;

namespace Ultron.Tests.Voice;

/// <summary>Sesli cevabın hızlı başlaması (2026-10-27): ilk virgüllü parça cümle bitmeden seslendirilir.</summary>
public class QuickStartTests
{
    private static List<string> Feed(SentenceSplitter s, params string[] chunks)
    {
        var all = new List<string>();
        foreach (var c in chunks)
            all.AddRange(s.Push(c));
        if (s.Flush() is { } rest)
            all.Add(rest);
        return all;
    }

    [Fact]
    public void Ilk_virgule_kadar_hemen_gider_cumle_sayisina_girmez()
    {
        var s = new SentenceSplitter { MaxSentences = 2, StopAtList = true, QuickStart = true };
        var first = s.Push("Patron, yarın sabah İstanbul'da, ");
        Assert.Equal(["Patron, yarın sabah İstanbul'da"], first); // cümle bitmeden

        var rest = Feed(s, "hava 17 derece ve yağmurlu olacak. Şemsiyeni almayı unutma. Akşam da serin.");
        Assert.Equal(["hava 17 derece ve yağmurlu olacak.", "Şemsiyeni almayı unutma."], rest); // + en fazla 2 cümle
        Assert.True(s.Truncated);
    }

    [Fact]
    public void Cok_kisa_ilk_parca_beklenir_hizli_baslangic_kapaliyken_eski_davranis()
    {
        var quick = new SentenceSplitter { QuickStart = true };
        Assert.Empty(quick.Push("Evet, ")); // 14 karakterden kısa: bölünmez

        var normal = new SentenceSplitter();
        Assert.Empty(normal.Push("Patron, yarın sabah İstanbul'da, "));
    }
}
