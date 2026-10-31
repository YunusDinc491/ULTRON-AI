using Ultron.Core.Settings;
using Ultron.Core.Tools;
using Ultron.Core.Routing;
using Ultron.Desktop.Voice;

namespace Ultron.Tests.Voice;

public class WakeWordTests
{
    // Çağrı kelimesi "Ultron" (Faz 26): Whisper'ın yazdığı biçimler (Chatterbox/Piper denemesinden: "Old Tron. Tron", "Uztran").
    [Theory]
    [InlineData("Ultron", true)]
    [InlineData("Ultron, yarın hava nasıl?", true)]
    [InlineData("Hey Uztran, müzik aç.", true)]
    [InlineData("Old Tron. Tron", true)]
    [InlineData("Altron", true)]
    [InlineData("Ultron'un sesi", true)]
    [InlineData("Patron toplantı saat üçte.", false)]
    [InlineData("Ultra hızlı bir bilgisayar aldım.", false)]
    [InlineData("Altın fiyatları yükseldi mi?", false)]
    [InlineData("Para transferi yaptım", false)]
    [InlineData("elektron mikroskobu", false)]
    [InlineData("", false)]
    public void Whisper_metninde_cagri_kelimesi(string text, bool expected) => Assert.Equal(expected, WakeWordRules.IsWakeText(text));

    [Fact]
    public void Windowsun_varsayilan_mikrofonu_secilir()
    {
        string[] names = ["Mikrofon Dizisi (Realtek(R) Aud", "Mikrofon (USBAudio1.0)"];

        Assert.Equal((1, "Mikrofon (USBAudio1.0)"), WakeWordRules.ChooseMicrophone(names, "Mikrofon (USBAudio1.0)"));
        Assert.Equal(0, WakeWordRules.ChooseMicrophone(names, "Mikrofon Dizisi (Realtek(R) Audio)").Number); // WaveIn adı 31 karakterde kesik
        Assert.Equal(0, WakeWordRules.ChooseMicrophone(names, null).Number); // varsayılan okunamazsa ilk cihaz
    }

    [Fact]
    public void Ornek_numarasi_artar_ustune_yazilmaz()
    {
        Assert.Equal(1, WakeWordRules.NextSampleNumber([]));
        Assert.Equal(2, WakeWordRules.NextSampleNumber([@"C:\x\ultron-1.wav"]));
        Assert.Equal(11, WakeWordRules.NextSampleNumber([@"C:\x\ultron-2.wav", @"C:\x\ultron-10.wav", @"C:\x\ultron-3.wav"]));
    }

    // ---- Ses örnekleriyle eşleştirme: sentetik "heceler" (tonlar) ile ----

    private const int Rate = VoiceTemplateMatcher.SampleRate;

    /// <summary>Art arda tonlar ("heceler"): (frekans, saniye). Sessizlik için frekans 0.</summary>
    private static float[] Tones(params (double Hz, double Seconds)[] parts)
    {
        var list = new List<float>();
        var rnd = new Random(7);
        foreach (var (hz, seconds) in parts)
            for (var i = 0; i < (int)(seconds * Rate); i++)
                list.Add(hz == 0 ? (float)(rnd.NextDouble() - 0.5) * 0.002f
                    : (float)(0.3 * Math.Sin(2 * Math.PI * hz * i / Rate) + 0.1 * Math.Sin(2 * Math.PI * hz * 2.7 * i / Rate)));
        return list.ToArray();
    }

    private static float[] Word(double stretch = 1) =>
        Tones((0, 0.2), (350, 0.15 * stretch), (900, 0.12 * stretch), (2200, 0.2 * stretch), (0, 0.2));

    [Fact]
    public void Ayni_kelime_hizli_ya_da_yavas_soylense_de_yakin_farkli_kelime_uzak()
    {
        var matcher = new VoiceTemplateMatcher();
        Assert.True(matcher.AddTemplate(Word()));
        Assert.True(matcher.AddTemplate(Word(1.1)));

        var same = matcher.Distance(Word(0.85));
        var slower = matcher.Distance(Word(1.3));
        var other = matcher.Distance(Tones((0, 0.2), (2200, 0.2), (900, 0.12), (350, 0.15), (0, 0.2))); // heceler ters sırada
        Assert.True(same < other / 2, $"aynı {same:0.00}, farklı {other:0.00}");
        Assert.True(slower < other / 2, $"yavaş {slower:0.00}, farklı {other:0.00}");
    }

    [Fact]
    public void Devam_eden_cumlede_bastaki_kelime_eslesir()
    {
        var matcher = new VoiceTemplateMatcher();
        matcher.AddTemplate(Word());
        var sentence = Tones((0, 0.2), (350, 0.15), (900, 0.12), (2200, 0.2), (600, 0.3), (1500, 0.4), (0, 0.2)); // "Ultron, yarın…"
        var unrelated = Tones((0, 0.2), (1500, 0.4), (600, 0.3), (1800, 0.3), (0, 0.2));
        var start = matcher.Distance(sentence);
        var none = matcher.Distance(unrelated);
        Assert.True(start < WakeWordRules.DistanceThreshold && none > WakeWordRules.DistanceThreshold, $"cümle başı {start:0.00}, ilgisiz {none:0.00}");
    }

    [Fact]
    public void Ornek_yoksa_ya_da_sessizse()
    {
        var matcher = new VoiceTemplateMatcher();
        Assert.Equal(double.PositiveInfinity, matcher.Distance(Word()));
        Assert.False(matcher.AddTemplate(Tones((0, 1.0)))); // sessizlik örnek olmaz
        Assert.Equal(0, matcher.TemplateCount);
    }

    // ---- Konuşma başlangıcı parçacı ----

    private static List<float[]> Segments(SpeechOnsetSegmenter segmenter, float[] audio)
    {
        var result = new List<float[]>();
        for (var i = 0; i < audio.Length; i += 1600) // uygulamadaki gibi 100 ms parçalar
            result.AddRange(segmenter.Feed(audio.AsSpan(i, Math.Min(1600, audio.Length - i))));
        return result;
    }

    [Fact]
    public void Iki_ayri_kelime_iki_parca_basi_kirpilmaz()
    {
        var segments = Segments(new SpeechOnsetSegmenter(), Tones((0, 1.0), (500, 0.6), (0, 0.6), (800, 0.5), (0, 0.6)));
        Assert.Equal(2, segments.Count);
        Assert.InRange(segments[0].Length / (double)Rate, 0.65, 1.0); // 0,6 sn + ön pay + bitiş sessizliği
    }

    [Fact]
    public void Uzun_konusma_tek_parca_sinirda_kesilir_konusma_bitmeden_yenisi_acilmaz()
    {
        var segmenter = new SpeechOnsetSegmenter { MaxMs = 1200 };
        var segments = Segments(segmenter, Tones((0, 1.0), (500, 1.5), (900, 1.5), (0, 0.6)));
        Assert.Single(segments);
        Assert.InRange(segments[0].Length / (double)Rate, 1.1, 1.3);
    }

    [Fact]
    public void Kisa_tik_sesi_parca_acmaz()
    {
        Assert.Empty(Segments(new SpeechOnsetSegmenter(), Tones((0, 1.0), (500, 0.05), (0, 1.0))));
    }

    [Theory]
    [InlineData("kapat", "kapali")]
    [InlineData("açık", "acik")]
    public void Ayar_bicimi(string value, string expected) => Assert.Equal(expected, UltronSettings.ParseOnOff(value));

    [Theory]
    [InlineData("Dinlemeyi kapat")]
    [InlineData("Ultron deyince dinleme")]
    [InlineData("şimdi yedek al")]
    [InlineData("dünkü yedekten geri yükle")]
    public void Yonlendirme_ilgili_araclari_verir(string text) =>
        Assert.NotEmpty(ToolRouting.Select(MessageIntent.Sohbet, text)!);

    [Fact]
    public void Yedek_ve_dinleme_dogru_gruba_gider()
    {
        Assert.Contains("ayar_degistir", ToolRouting.Select(MessageIntent.Sohbet, "Dinlemeyi kapat")!);
        Assert.Contains("yedek_al", ToolRouting.Select(MessageIntent.Sohbet, "şimdi yedek al")!);
        Assert.Contains("yedekten_geri_yukle", ToolRouting.Select(MessageIntent.Sohbet, "dünkü yedekten geri yükle")!);
        Assert.Contains("ayar_degistir", ToolRouting.Select(MessageIntent.Sohbet, "yedekleri harici diske de al")!);
        Assert.Null(ToolRouting.Select(MessageIntent.Sohbet, "bugün çok yoruldum"));
    }
}
