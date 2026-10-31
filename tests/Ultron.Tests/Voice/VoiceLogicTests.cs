using Ultron.Core.Voice;
using Ultron.Desktop.Voice;

namespace Ultron.Tests.Voice;

public sealed class SpeechTextTests
{
    [Fact]
    public void Markdown_isaretleri_baglantilar_ve_kaynak_numaralari_okunmaz()
    {
        var text = SpeechText.ForSpeech("## Özet\n- **Dolar** 41,2 TL [1]\n- Kaynak: [TCMB](https://tcmb.gov.tr) https://x.com/a");

        Assert.Equal("Özet Dolar 41,2 TL Kaynak: TCMB", text);
    }

    [Fact]
    public void Kod_blogu_okunmaz_yerine_kisa_not_gelir()
    {
        Assert.Equal("Şöyle: Kod ekranda.", SpeechText.ForSpeech("Şöyle:\n```csharp\nvar x = 1;\n```"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("**  **")]
    [InlineData("[1]")]
    public void Okunacak_bir_sey_yoksa_bos_doner(string markdown)
    {
        Assert.Equal("", SpeechText.ForSpeech(markdown));
    }

    [Theory]
    [InlineData("Altyazı M.K.", true)]
    [InlineData("İzlediğiniz için teşekkürler.", true)]
    [InlineData("...", true)]
    [InlineData("", true)]
    [InlineData("Yarın hava nasıl olacak?", false)]
    [InlineData("Bu videonun altyazısını nasıl eklerim, uzun bir anlatım istiyorum lütfen detaylı olsun", false)]
    public void Whisper_uydurmalari_ayiklanir(string transcript, bool hallucination)
    {
        Assert.Equal(hallucination, SpeechText.IsLikelyHallucination(transcript));
    }
}

public sealed class SilenceDetectorTests
{
    private static ListenResult Run(SilenceDetector d, float rms, int ms)
    {
        var result = ListenResult.Continue;
        for (var t = 0; t < ms && result == ListenResult.Continue; t += 30)
            result = d.Feed(rms, 30);
        return result;
    }

    [Fact]
    public void Konusma_sonrasi_sessizlikte_kayit_biter()
    {
        var d = new SilenceDetector();
        Assert.Equal(ListenResult.Continue, Run(d, 0.003f, 300));  // ortam gürültüsü ölçülür
        Assert.Equal(ListenResult.Continue, Run(d, 0.08f, 1500));  // konuşma
        Assert.True(d.HeardSpeech);
        Assert.Equal(ListenResult.Continue, Run(d, 0.003f, 1500)); // düşünme duraklaması (1,5 sn): devam (2026-10-29: 1,2 sn kesiyordu)
        Assert.Equal(ListenResult.SpeechEnded, Run(d, 0.003f, 600));  // 2 sn sessizlik: biter
    }

    [Fact]
    public void Kisa_duraklama_konusmayi_bitirmez()
    {
        var d = new SilenceDetector();
        Run(d, 0.003f, 300);
        Run(d, 0.08f, 600);
        Assert.Equal(ListenResult.Continue, Run(d, 0.003f, 600));
        Assert.Equal(ListenResult.Continue, Run(d, 0.08f, 600)); // konuşma sürünce sessizlik sayacı sıfırlanır
        Assert.Equal(ListenResult.Continue, Run(d, 0.003f, 900));
    }

    [Fact]
    public void Hic_konusulmazsa_bos_doner()
    {
        var d = new SilenceDetector();
        Assert.Equal(ListenResult.NoSpeech, Run(d, 0.003f, 10_000));
        Assert.False(d.HeardSpeech);
    }

    [Fact]
    public void Kisa_tik_sesi_konusma_sayilmaz()
    {
        var d = new SilenceDetector();
        Run(d, 0.003f, 300);
        Run(d, 0.2f, 90);
        Assert.False(d.HeardSpeech);
    }

    [Fact]
    public void Gurultulu_odada_esik_yukselir()
    {
        var d = new SilenceDetector();
        Run(d, 0.03f, 300); // fan sesi
        Run(d, 0.04f, 1000); // gürültüden biraz yüksek: konuşma değil
        Assert.False(d.HeardSpeech);
        Run(d, 0.15f, 500);
        Assert.True(d.HeardSpeech);
    }

    [Fact]
    public void Hemen_konusmaya_baslaninca_da_duyulur()
    {
        // Düğmeye basar basmaz konuşma: ilk 300 ms gürültü sanılıp eşik konuşmanın üstüne çıkıyordu ("sesimi tam duymuyor").
        var d = new SilenceDetector();
        Run(d, 0.05f, 300);
        Run(d, 0.05f, 400);
        Assert.True(d.HeardSpeech);
    }

    [Fact]
    public void Kisik_mikrofonda_konusma_duyulur()
    {
        var d = new SilenceDetector();
        Run(d, 0.002f, 300);
        Run(d, 0.02f, 400); // kısık USB mikrofon
        Assert.True(d.HeardSpeech);
    }

    [Fact]
    public void Ust_sureye_ulasilinca_kayit_kesilir()
    {
        var d = new SilenceDetector { MaxRecordingMs = 3000 };
        Run(d, 0.003f, 300);
        Assert.Equal(ListenResult.TooLong, Run(d, 0.1f, 5000));
    }
}

public sealed class SentenceSplitterTests
{
    [Fact]
    public void Akan_metinden_tamamlanan_cumleler_ayrilir()
    {
        var s = new SentenceSplitter();
        var out1 = s.Push("Bugün hava yağmurlu görünüyor. Şem");
        var out2 = s.Push("siye almanız iyi olur. Ders saat");
        var out3 = s.Push(" on dörtte.");

        Assert.Equal(["Bugün hava yağmurlu görünüyor."], out1);
        Assert.Equal(["Şemsiye almanız iyi olur."], out2);
        Assert.Empty(out3); // son nokta: ardından ne geleceği belli değil
        Assert.Equal("Ders saat on dörtte.", s.Flush());
        Assert.Null(s.Flush());
    }

    [Fact]
    public void Ondalik_sayi_ve_kisa_cumle_bolunmez()
    {
        var s = new SentenceSplitter();
        var sentences = s.Push("Evet. Dolar bugün 41.25 lira oldu, dün 41.10 idi. ");

        Assert.Equal(["Evet. Dolar bugün 41.25 lira oldu, dün 41.10 idi."], sentences);
    }

    [Fact]
    public void Satir_sonu_cumle_siniridir()
    {
        var s = new SentenceSplitter { MinLength = 5 };
        Assert.Equal(["- Birinci madde", "- İkinci madde"], s.Push("- Birinci madde\n- İkinci madde\n"));
    }
}

public sealed class SentenceLimitTests
{
    [Fact]
    public void Sinirdan_sonrasi_okunmaz_ve_kesildigi_bildirilir()
    {
        var splitter = new SentenceSplitter { MinLength = 1, MaxSentences = 2 };

        var spoken = splitter.Push("Bir cümle. İki cümle. Üç cümle. Dört").ToList();
        spoken.AddRange(splitter.Push(" cümle. Beş."));

        Assert.Equal(["Bir cümle.", "İki cümle."], spoken);
        Assert.Null(splitter.Flush());
        Assert.True(splitter.Truncated);
    }

    [Fact]
    public void Kisa_cevapta_kesilme_olmaz()
    {
        var splitter = new SentenceSplitter { MinLength = 1, MaxSentences = 4 };

        var spoken = splitter.Push("Bir cümle. İki cümle").ToList();

        Assert.Equal(["Bir cümle."], spoken);
        Assert.Equal("İki cümle", splitter.Flush());
        Assert.False(splitter.Truncated);
    }
}

public sealed class SpokenSummaryTests
{
    [Fact]
    public void Liste_gelince_okuma_durur_ve_listeye_gelindigi_bildirilir()
    {
        var splitter = new SentenceSplitter { MaxSentences = 2, StopAtList = true };

        var spoken = splitter.Push("Retro etkisi geçmişin estetiğinin geri dönmesidir. Örnek alanlar:\n\n*   **Moda:** Vintage desenler.\n").ToList();
        spoken.AddRange(splitter.Push("*   **Tasarım:** Retro renkler.\n"));

        Assert.Equal(["Retro etkisi geçmişin estetiğinin geri dönmesidir."], spoken); // "Örnek alanlar:" listeye giriş, okunmaz
        Assert.Null(splitter.Flush());
        Assert.True(splitter.ReachedList);
        Assert.True(splitter.Truncated);
    }

    [Fact]
    public void Basliktan_once_ozet_okunur()
    {
        var splitter = new SentenceSplitter { MaxSentences = 2, StopAtList = true };

        var spoken = splitter.Push("Entropi, düzensizliğin ölçüsüdür ve hep artma eğilimindedir.\n\n### 1. Termodinamikte\n\nUzun açıklama.").ToList();

        Assert.Equal(["Entropi, düzensizliğin ölçüsüdür ve hep artma eğilimindedir."], spoken);
        Assert.True(splitter.ReachedList);
    }

    [Fact]
    public void Numarali_liste_de_durdurur_ama_ondalik_sayi_durdurmaz()
    {
        var splitter = new SentenceSplitter { MaxSentences = 3, StopAtList = true };

        var spoken = splitter.Push("Dolar bugün 41.5 lira civarında, dün biraz daha düşüktü. Adımlar şöyle:\n1. Bankaya git.\n").ToList();

        Assert.Equal(["Dolar bugün 41.5 lira civarında, dün biraz daha düşüktü."], spoken);
        Assert.True(splitter.ReachedList);
    }
}

public sealed class TimeStretchTests
{
    [Fact]
    public void Hizlandirma_sureyi_kisaltir_perdeyi_korur()
    {
        const int rate = 24000;
        var tone = Enumerable.Range(0, rate * 2).Select(i => MathF.Sin(2 * MathF.PI * 200 * i / rate) * 0.5f).ToArray();

        var fast = Ultron.Infrastructure.Voice.VoiceEffects.TimeStretch(tone, rate, 1.25);

        Assert.InRange(fast.Length / (double)rate, 1.5, 1.7); // 2 sn / 1,25 = 1,6 sn
        // Perde: sıfır geçişi sayısı saniyede ~400 (200 Hz) kalmalı.
        var middle = fast[(rate / 4)..(rate * 5 / 4)];
        var crossings = Enumerable.Range(1, middle.Length - 1).Count(i => middle[i - 1] < 0 && middle[i] >= 0);
        Assert.InRange(crossings, 190, 210);
    }
}

public sealed class WhisperTuningTests
{
    [Fact]
    public void Kisik_ses_yukseltilir_yuksek_ses_ve_sessizlik_degismez()
    {
        var quiet = Ultron.Infrastructure.Voice.WhisperSpeechToText.Normalize(new(new[] { 0.05f, -0.1f, 0.02f }, 16000));
        var loud = new Ultron.Infrastructure.Voice.Wav.Audio(new[] { 0.6f, -0.7f }, 16000);
        var silent = new Ultron.Infrastructure.Voice.Wav.Audio(new[] { 0.001f, -0.002f }, 16000);

        Assert.Equal(0.8f, quiet.Samples.Max(Math.Abs), 3); // 8 kat sınırı (0,9/0,1 = 9 → 8)
        Assert.Same(loud, Ultron.Infrastructure.Voice.WhisperSpeechToText.Normalize(loud));
        Assert.Same(silent, Ultron.Infrastructure.Voice.WhisperSpeechToText.Normalize(silent));
    }

    [Theory]
    [InlineData("YouTube'da şarkı açabilir misin?", true)]
    [InlineData("Merhaba, bugün derslerim", true)]
    [InlineData("Termodinamik vizesi ne zaman?", false)]
    public void Ipucunun_tekrari_atilir(string text, bool echo)
    {
        Assert.Equal(echo, Ultron.Infrastructure.Voice.WhisperSpeechToText.IsPromptEcho(text));
    }
}
