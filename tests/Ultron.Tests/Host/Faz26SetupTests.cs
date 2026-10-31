using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Core.Settings;
using Ultron.Infrastructure.Ollama;
using Ultron.Infrastructure.Setup;
using Ultron.Infrastructure.Voice;
using Ultron.Infrastructure.Web;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Host;

/// <summary>Faz 26: ilk açılış sihirbazı — Ollama ilerlemesi, eksik model/ses dosyası, anahtar dosyaları, tanışma formu.</summary>
public sealed class Faz26SetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ultron-kurulum-" + Guid.NewGuid().ToString("N"));
    private readonly UltronSettings _settings = new(new InMemorySettingsStore(), new ManualTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero)));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Ağa çıkmayan servis: Ollama ve SearXNG adresleri kullanılmayan bir porta bakar, dosyalar geçici klasörde.</summary>
    private SetupService Service() =>
        new(new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = new Uri("http://127.0.0.1:9") }),
            Options.Create(new VoiceOptions { ModelsDirectory = Path.Combine(_root, "models") }),
            Options.Create(new WebOptions { TavilyKeyFile = Path.Combine(_root, "tavily-anahtar.txt"), SearxngUrl = new Uri("http://127.0.0.1:9") }),
            _settings, memory: null!, new ServiceCollection().BuildServiceProvider(), NullLogger<SetupService>.Instance)
        { Root = _root };

    [Fact]
    public void Ollama_indirme_satirlari_okunur()
    {
        var layer = SetupService.ParsePull("""{"status":"pulling 4c8f","digest":"sha256:4c8f","total":6146501801,"completed":3073250900}""");
        Assert.Equal((6146501801L, 3073250900L, (string?)null), (layer.Total, layer.Completed, layer.Error));

        Assert.Equal("success", SetupService.ParsePull("""{"status":"success"}""").Status);
        Assert.Equal("pull model manifest: file does not exist", SetupService.ParsePull("""{"error":"pull model manifest: file does not exist"}""").Error);
        Assert.Null(SetupService.ParsePull("yarım satır {").Error); // bozuk satır adımı düşürmez
    }

    [Fact]
    public void Eksik_model_etiketiyle_ya_da_latest_ile_bulunur()
    {
        string[] installed = ["gemma4:e4b-it-qat", "llama3:latest"];

        Assert.Empty(SetupService.MissingModels(["gemma4:e4b-it-qat", "llama3"], installed));
        Assert.Equal(["qwen3-embedding:0.6b"], SetupService.MissingModels(["gemma4:e4b-it-qat", "qwen3-embedding:0.6b"], installed));
        Assert.Equal(["gemma4:e2b"], SetupService.MissingModels(["gemma4:e2b"], installed)); // başka etiket kurulu sayılmaz
    }

    [Fact]
    public void Eksik_ses_dosyalari_ve_indirme_adresleri()
    {
        var service = Service();
        Assert.Equal(5, service.MissingVoiceFiles().Count);

        var whisper = Path.Combine(_root, "models", "whisper", "ggml-small-q5_1.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(whisper)!);
        File.WriteAllText(whisper, "x");

        var missing = service.MissingVoiceFiles();
        Assert.Equal(4, missing.Count);
        Assert.DoesNotContain(missing, f => f.EndsWith("ggml-small-q5_1.bin"));
        Assert.All(missing, f => Assert.True(SetupService.VoiceDownloads.ContainsKey(Path.GetFileName(f)), f)); // her eksik dosyanın adresi var
    }

    [Fact]
    public void Ilerleme_cubugu_ve_donen_cizgi_gosterilmez()
    {
        Assert.Null(SetupService.Meaningful("  ██████████▒▒▒▒▒▒  "));
        Assert.Null(SetupService.Meaningful(@" \ "));
        Assert.Equal("Successfully installed", SetupService.Meaningful("  Successfully installed  "));
    }

    [Fact]
    public async Task Anahtar_dogru_dosyaya_yazilir_kisa_anahtar_reddedilir()
    {
        var service = Service();

        Assert.NotNull(await service.SaveKeyAsync("groq", "kisa"));
        Assert.NotNull(await service.SaveKeyAsync("bilinmeyen", "gsk_cokuzunbiranahtar123"));
        Assert.Null(await service.SaveKeyAsync("groq", "  gsk_cokuzunbiranahtar123 "));
        Assert.Equal("gsk_cokuzunbiranahtar123", File.ReadAllText(Path.Combine(_root, "groq-anahtar.txt")).Trim());

        // ElevenLabs dosyasının 2. satırı (seçilen ses) yeni anahtarla korunur.
        File.WriteAllLines(Path.Combine(_root, "elevenlabs-anahtar.txt"), ["sk_eskianahtar1234567", "ses-kimligi"]);
        Assert.Null(await service.SaveKeyAsync("elevenlabs", "sk_yenianahtar12345678"));
        Assert.Equal(["sk_yenianahtar12345678", "ses-kimligi"], File.ReadAllLines(Path.Combine(_root, "elevenlabs-anahtar.txt")));
    }

    [Fact]
    public async Task Durum_hazir_olmayanlari_gosterir()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "tavily-anahtar.txt"), "tvly-cokuzunbiranahtar");

        var status = await Service().GetStatusAsync();

        Assert.False(status.Completed);
        Assert.False(status.OllamaRunning);
        Assert.Equal(2, status.MissingModels.Count); // Ollama kapalıyken iki model de eksik sayılır
        Assert.Equal(5, status.MissingVoiceFiles.Count);
        Assert.True(status.VoiceDownloadBytes > 800_000_000);
        Assert.True(status.TavilyKey);
        Assert.False(status.GroqKey);
    }

    [Fact]
    public async Task Tanisma_formu_hitap_ve_sehri_ayara_yazar_bitirince_tamam()
    {
        var service = Service();

        Assert.Null(await service.SaveProfileAsync(new SetupProfile("Ece", "", "", ""))); // sadece hitap: hafızaya gidecek bilgi yok
        Assert.Equal("Ece", await _settings.GetAsync(UltronSettings.UserAddress));
        Assert.NotNull(await service.SaveProfileAsync(new SetupProfile("<Ece>", "", "", ""))); // geçersiz hitap bildirilir

        await service.CompleteAsync();
        Assert.True((await service.GetStatusAsync()).Completed);
    }

    [Fact]
    public void Tanisma_bilgileri_sohbet_mesaji_gibi_yazilir()
    {
        Assert.Null(SetupService.ProfileText(new SetupProfile("Ece", " ", "", "")));
        var text = SetupService.ProfileText(new SetupProfile("Ece", "İzmir", "bilgisayar mühendisliği", "gitar, futbol"));
        Assert.Equal("Kendimi tanıtayım. Yaşadığım şehir: İzmir. Ne iş yaptığım ya da ne okuduğum: bilgisayar mühendisliği. İlgi alanlarım: gitar, futbol.", text);
    }
}
