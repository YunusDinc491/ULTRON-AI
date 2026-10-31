using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Memories;
using Ultron.Core.Settings;
using Ultron.Infrastructure.Ollama;
using Ultron.Infrastructure.Services;
using Ultron.Infrastructure.Voice;
using Ultron.Infrastructure.Web;

namespace Ultron.Infrastructure.Setup;

/// <summary>
/// İlk açılış sihirbazının Host tarafı (Faz 26). Başka bir bilgisayarda Setup'ın getirmediği her şeyi yerine koyar: Ollama ve
/// dil modelleri, ses modelleri (Whisper, Piper), web araması (Tavily anahtarı ya da Docker + SearXNG), isteğe bağlı bulut
/// anahtarları ve kısa tanışma formu. Benzetme: taşınma kamyonu eşyaları getirdi (Setup); bu servis elektriği, suyu ve interneti
/// bağlatan usta. Uzun işler ilerleme akışı olarak döner; hiçbir adım kullanıcı düğmeye basmadan başlamaz.
/// </summary>
public sealed class SetupService(
    HttpClient http,
    IOptions<OllamaOptions> ollama,
    IOptions<VoiceOptions> voice,
    IOptions<WebOptions> web,
    UltronSettings settings,
    MemoryService memory,
    IServiceProvider services,
    ILogger<SetupService> logger)
{
    /// <summary>İç kayıt: sihirbaz bitirildi (tarih). Doluysa sihirbaz kendiliğinden açılmaz.</summary>
    public const string CompletedKey = "kurulum_tamam";

    private const string PiperZipUrl = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";
    private const string PiperVoiceUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/main/tr/tr_TR/dfki/medium/";
    private const string WhisperUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    /// <summary>Ses modelleri: dosya adı → indirme adresi ve yaklaşık boyut (adresler 2026-10-30'da doğrulandı).</summary>
    internal static readonly IReadOnlyDictionary<string, (string Url, long Bytes)> VoiceDownloads =
        new Dictionary<string, (string Url, long Bytes)>(StringComparer.OrdinalIgnoreCase)
        {
            ["ggml-large-v3-turbo-q5_0.bin"] = (WhisperUrl + "ggml-large-v3-turbo-q5_0.bin", 574_000_000),
            ["ggml-small-q5_1.bin"] = (WhisperUrl + "ggml-small-q5_1.bin", 190_000_000),
            ["piper.exe"] = (PiperZipUrl, 22_400_000), // zip olarak iner, açılır
            ["tr_TR-dfki-medium.onnx"] = (PiperVoiceUrl + "tr_TR-dfki-medium.onnx", 63_200_000),
            ["tr_TR-dfki-medium.onnx.json"] = (PiperVoiceUrl + "tr_TR-dfki-medium.onnx.json", 5_000),
        };

    /// <summary>Anahtar dosyalarının ve SearXNG ayarının klasörü (%USERPROFILE%\Ultron). Testler geçici klasör verir.</summary>
    internal string Root { get; init; } = CloudKeys.Folder;

    public async Task<SetupStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var o = ollama.Value;
        var running = await IsUpAsync(new Uri(o.BaseUrl, "api/version"), cancellationToken);
        IReadOnlyList<string> missingModels = running ? await MissingModelsAsync(cancellationToken) : [o.ChatModel, o.EmbeddingModel];
        var missingVoice = MissingVoiceFiles();
        return new SetupStatus(
            Completed: await settings.GetInternalAsync(CompletedKey, cancellationToken) is { Length: > 0 },
            OllamaInstalled: running || OllamaApp() is not null,
            OllamaRunning: running,
            MissingModels: missingModels,
            MissingVoiceFiles: missingVoice,
            VoiceDownloadBytes: missingVoice.Sum(f => VoiceDownloads.TryGetValue(Path.GetFileName(f), out var d) ? d.Bytes : 0),
            TavilyKey: HasKey(web.Value.TavilyKeyFile),
            SearxngRunning: await IsUpAsync(web.Value.SearxngUrl, cancellationToken),
            DockerInstalled: DependencyStarter.DockerDesktop() is not null,
            GroqKey: HasKey(Path.Combine(Root, GroqSpeechToText.KeyFile)),
            ElevenLabsKey: HasKey(Path.Combine(Root, ElevenLabsTextToSpeech.KeyFile)),
            Address: await settings.GetAsync(UltronSettings.UserAddress, cancellationToken),
            City: await settings.GetAsync(UltronSettings.DefaultCity, cancellationToken));
    }

    // ---- Yapay zekâ motoru: Ollama + modeller ----

    /// <summary>Ollama yoksa winget ile kurar (resmi paket), sonra başlatıp cevap vermesini bekler. winget yoksa Error = "winget".</summary>
    public async IAsyncEnumerable<SetupProgress> InstallOllamaAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var version = new Uri(ollama.Value.BaseUrl, "api/version");
        if (await IsUpAsync(version, cancellationToken))
        {
            yield return new("Ollama zaten çalışıyor.", 100, Done: true);
            yield break;
        }
        if (OllamaApp() is null)
        {
            if (!WingetAvailable())
            {
                yield return new("Bu bilgisayarda winget yok.", Error: "winget");
                yield break;
            }
            yield return new("Ollama indiriliyor ve kuruluyor (Windows paket yöneticisi winget)…");
            await foreach (var line in RunAsync("winget", ["install", "-e", "--id", "Ollama.Ollama", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"], TimeSpan.FromMinutes(30), cancellationToken))
                if (line.Text is { } text)
                    yield return new(text);
            if (OllamaApp() is null)
            {
                yield return new("Ollama kurulamadı.", Error: "Kurulum tamamlanmadı. İndirme sayfasından elle kurup \"Tekrar kontrol et\"e bas.");
                yield break;
            }
        }
        yield return new("Ollama başlatılıyor…");
        StartDetached(OllamaApp()!);
        for (var i = 0; i < 45 && !await IsUpAsync(version, cancellationToken); i++)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        yield return await IsUpAsync(version, cancellationToken)
            ? new("Ollama hazır.", 100, Done: true)
            : new("Ollama cevap vermedi.", Error: "Ollama kuruldu ama 90 saniyede açılmadı. Başlat menüsünden Ollama'yı açıp \"Tekrar kontrol et\"e bas.");
    }

    /// <summary>Eksik modelleri Ollama'ya indirtir (/api/pull akışı); yüzde 1'lik adımlarla ilerleme bildirir.</summary>
    public async IAsyncEnumerable<SetupProgress> PullModelsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!await IsUpAsync(new Uri(ollama.Value.BaseUrl, "api/version"), cancellationToken))
        {
            yield return new("Ollama çalışmıyor.", Error: "Önce Ollama'yı kur ya da başlat.");
            yield break;
        }
        var missing = await MissingModelsAsync(cancellationToken);
        for (var i = 0; i < missing.Count; i++)
        {
            var model = missing[i];
            var label = $"{model} ({i + 1}/{missing.Count})";
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ollama.Value.BaseUrl, "api/pull")) { Content = JsonContent.Create(new { model, stream = true }) };
            var (response, error) = await SendAsync(request, cancellationToken);
            if (response is null)
            {
                yield return new($"{label} indirilemedi.", Error: error);
                yield break;
            }
            using (response)
            {
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
                var lastPercent = -1;
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    var step = ParsePull(line);
                    if (step.Error is not null)
                    {
                        yield return new($"{label} indirilemedi.", Error: step.Error);
                        yield break;
                    }
                    var percent = step.Total > 0 ? (int)(step.Completed * 100 / step.Total) : -1;
                    if (percent == lastPercent && percent >= 0)
                        continue;
                    lastPercent = percent;
                    yield return new($"{label}: {DescribePull(step)}", percent >= 0 ? percent : null);
                }
            }
        }
        var left = await MissingModelsAsync(cancellationToken);
        yield return left.Count == 0
            ? new("Modeller hazır.", 100, Done: true)
            : new("Bazı modeller inmedi.", Error: "İnmeyen: " + string.Join(", ", left) + ". İnternet bağlantısını kontrol edip tekrar dene.");
    }

    internal sealed record PullStep(string Status, long Total, long Completed, string? Error);

    /// <summary>Ollama /api/pull satırı: {"status":"pulling …","total":…,"completed":…} ya da {"error":"…"}.</summary>
    internal static PullStep ParsePull(string line)
    {
        try
        {
            var root = JsonDocument.Parse(line).RootElement;
            if (root.TryGetProperty("error", out var error))
                return new("", 0, 0, error.GetString() ?? "bilinmeyen hata");
            return new(root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
                root.TryGetProperty("total", out var t) && t.TryGetInt64(out var total) ? total : 0,
                root.TryGetProperty("completed", out var c) && c.TryGetInt64(out var completed) ? completed : 0,
                null);
        }
        catch (JsonException)
        {
            return new(line, 0, 0, null);
        }
    }

    private static string DescribePull(PullStep step) =>
        step.Total > 0 ? $"{Gb(step.Completed)} / {Gb(step.Total)}"
        : step.Status switch { "success" => "tamam", "verifying sha256 digest" => "doğrulanıyor", "writing manifest" => "yazılıyor", _ => "hazırlanıyor" };

    private static string Gb(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1e9:0.0} GB" : $"{bytes / 1e6:0} MB";

    private async Task<IReadOnlyList<string>> MissingModelsAsync(CancellationToken cancellationToken)
    {
        var o = ollama.Value;
        var installed = new List<string>();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var tags = await http.GetFromJsonAsync<JsonElement>(new Uri(o.BaseUrl, "api/tags"), timeout.Token);
            if (tags.TryGetProperty("models", out var models))
                installed.AddRange(models.EnumerateArray().Select(m => m.GetProperty("name").GetString() ?? ""));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama model listesi alınamadı");
        }
        return MissingModels([o.ChatModel, o.EmbeddingModel], installed);
    }

    /// <summary>"qwen3-embedding:0.6b" kurulu mu: ad aynen ya da etiketsiz adlar için ":latest" ile eşleşir.</summary>
    internal static IReadOnlyList<string> MissingModels(IEnumerable<string> wanted, IReadOnlyCollection<string> installed) =>
        wanted.Where(m => !string.IsNullOrWhiteSpace(m))
            .Where(m => !installed.Any(i => string.Equals(i, m, StringComparison.OrdinalIgnoreCase)
                || (!m.Contains(':') && string.Equals(i, m + ":latest", StringComparison.OrdinalIgnoreCase))))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string? OllamaApp() =>
        new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama app.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
        }.FirstOrDefault(File.Exists);

    // ---- Ses modelleri ----

    internal IReadOnlyList<string> MissingVoiceFiles()
    {
        var o = voice.Value;
        return new[] { o.WhisperModel, o.WakeModel, o.PiperExecutable, o.PiperVoice, o.PiperVoice + ".json" }
            .Where(f => !File.Exists(o.Resolve(f))).ToList();
    }

    /// <summary>Eksik ses dosyalarını indirir (önce .part, bitince adı verilir; Piper zip'i açılıp silinir).</summary>
    public async IAsyncEnumerable<SetupProgress> DownloadVoiceModelsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var missing = MissingVoiceFiles();
        for (var i = 0; i < missing.Count; i++)
        {
            var relative = missing[i];
            var name = Path.GetFileName(relative);
            if (!VoiceDownloads.TryGetValue(name, out var source))
            {
                yield return new($"{name} için indirme adresi bilinmiyor.", Error: $"{name} ayarlarda değiştirilmiş; bu dosyayı elle koyman gerekiyor.");
                yield break;
            }
            var target = voice.Value.Resolve(relative);
            var isZip = source.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            var file = isZip ? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(target)!)!, Path.GetFileName(source.Url)) : target;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var label = $"{VoiceLabel(name)} ({i + 1}/{missing.Count})";
            await foreach (var p in DownloadAsync(source.Url, file, label, cancellationToken))
            {
                yield return p;
                if (p.Error is not null)
                    yield break;
            }
            if (isZip)
            {
                // piper_windows_amd64.zip içinde "piper/" klasörü var: models\piper içine açılınca models\piper\piper\piper.exe olur.
                ZipFile.ExtractToDirectory(file, Path.GetDirectoryName(file)!, overwriteFiles: true);
                File.Delete(file);
            }
        }
        var left = MissingVoiceFiles();
        yield return left.Count == 0
            ? new("Ses modelleri hazır.", 100, Done: true)
            : new("Bazı ses dosyaları eksik.", Error: "Eksik: " + string.Join(", ", left.Select(Path.GetFileName)));
    }

    private static string VoiceLabel(string name) => name switch
    {
        "ggml-large-v3-turbo-q5_0.bin" => "Konuşma tanıma (büyük model)",
        "ggml-small-q5_1.bin" => "\"Ultron\" algılama (küçük model)",
        "piper.exe" => "Yerel ses motoru (Piper)",
        _ => "Türkçe ses",
    };

    private async IAsyncEnumerable<SetupProgress> DownloadAsync(string url, string file, string label, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var (response, error) = await SendAsync(request, cancellationToken);
        if (response is null)
        {
            yield return new($"{label} indirilemedi.", Error: error);
            yield break;
        }
        using (response)
        {
            var total = response.Content.Headers.ContentLength ?? 0;
            var part = file + ".part";
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(part))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                var lastPercent = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    done += read;
                    var percent = total > 0 ? (int)(done * 100 / total) : -1;
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        yield return new($"{label}: {Gb(done)}{(total > 0 ? " / " + Gb(total) : "")}", percent >= 0 ? percent : null);
                    }
                }
            }
            File.Move(part, file, overwrite: true);
        }
    }

    // ---- Web araması: Tavily anahtarı ya da Docker + SearXNG ----

    /// <summary>Docker Desktop'ı winget ile kurar (Windows yönetici izni sorar; WSL için yeniden başlatma isteyebilir).</summary>
    public async IAsyncEnumerable<SetupProgress> InstallDockerAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (DependencyStarter.DockerDesktop() is not null)
        {
            yield return new("Docker Desktop zaten kurulu.", 100, Done: true);
            yield break;
        }
        if (!WingetAvailable())
        {
            yield return new("Bu bilgisayarda winget yok.", Error: "winget");
            yield break;
        }
        yield return new("Docker Desktop indiriliyor ve kuruluyor (birkaç GB; Windows izin isterse \"Evet\" de)…");
        await foreach (var line in RunAsync("winget", ["install", "-e", "--id", "Docker.DockerDesktop", "--accept-package-agreements",
            "--accept-source-agreements", "--disable-interactivity"], TimeSpan.FromMinutes(60), cancellationToken))
            if (line.Text is { } text)
                yield return new(text);
        yield return DependencyStarter.DockerDesktop() is not null
            ? new("Docker Desktop kuruldu. Windows yeniden başlatma isterse yeniden başlat; sonra \"Arama motorunu kur\"a bas.", 100, Done: true)
            : new("Docker Desktop kurulamadı.", Error: "Kurulum tamamlanmadı. docker.com'dan elle kurup \"Tekrar kontrol et\"e bas.");
    }

    /// <summary>
    /// SearXNG kapsayıcısını kurar ya da başlatır: Docker kapalıysa açar, ayar dosyasını %USERPROFILE%\Ultron\searxng'ye kopyalar,
    /// rastgele gizli anahtarla "ultron-searxng" kapsayıcısını yalnızca 127.0.0.1:8888'de çalıştırır. Var olan kapsayıcıya dokunmaz, sadece başlatır.
    /// </summary>
    public async IAsyncEnumerable<SetupProgress> SetupSearxngAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var url = web.Value.SearxngUrl;
        if (await IsUpAsync(url, cancellationToken))
        {
            yield return new("Arama motoru zaten çalışıyor.", 100, Done: true);
            yield break;
        }
        if (!await DockerReadyAsync(cancellationToken))
        {
            if (DependencyStarter.DockerDesktop() is not { } desktop)
            {
                yield return new("Docker Desktop kurulu değil.", Error: "Önce Docker Desktop'ı kur.");
                yield break;
            }
            yield return new("Docker Desktop açılıyor (ilk açılış 1-2 dakika sürebilir)…");
            StartDetached(desktop);
            for (var i = 0; i < 60 && !await DockerReadyAsync(cancellationToken); i++)
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            if (!await DockerReadyAsync(cancellationToken))
            {
                yield return new("Docker hazır olmadı.", Error: "Docker Desktop 3 dakikada hazır olmadı. Docker Desktop'ı açıp kullanım koşullarını kabul et, sonra tekrar dene.");
                yield break;
            }
        }
        var exists = false;
        await foreach (var line in RunAsync("docker", ["container", "inspect", DependencyStarter.SearxngContainer], TimeSpan.FromSeconds(30), cancellationToken))
            if (line.ExitCode is { } code)
                exists = code == 0;
        string[] args;
        if (exists)
        {
            yield return new("Arama motoru başlatılıyor…");
            args = ["start", DependencyStarter.SearxngContainer];
        }
        else
        {
            if (FindSearxngSettings() is not { } source)
            {
                yield return new("SearXNG ayar dosyası bulunamadı.", Error: "Ultron kurulumu eksik (searxng\\settings.yml yok). Ultron'u yeniden kur.");
                yield break;
            }
            var folder = Path.Combine(Root, "searxng");
            Directory.CreateDirectory(folder);
            var settingsFile = Path.Combine(folder, "settings.yml");
            File.Copy(source, settingsFile, overwrite: true);
            yield return new("Arama motoru indiriliyor ve kuruluyor (SearXNG, ~200 MB)…");
            args = ["run", "-d", "--name", DependencyStarter.SearxngContainer, "--restart", "unless-stopped",
                "-p", $"127.0.0.1:{url.Port}:8080",
                "-e", $"SEARXNG_BASE_URL={url}",
                "-e", "SEARXNG_SECRET=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
                "-v", $"{settingsFile}:/etc/searxng/settings.yml:ro",
                "searxng/searxng:latest"];
        }
        int? exit = null;
        await foreach (var line in RunAsync("docker", args, TimeSpan.FromMinutes(15), cancellationToken))
        {
            if (line.Text is { } text)
                yield return new(text);
            exit = line.ExitCode ?? exit;
        }
        if (exit is not 0)
        {
            yield return new("Arama motoru kurulamadı.", Error: "docker komutu başarısız oldu (çıkış kodu " + exit + "). Docker Desktop açık mı?");
            yield break;
        }
        for (var i = 0; i < 30 && !await IsUpAsync(url, cancellationToken); i++)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        yield return await IsUpAsync(url, cancellationToken)
            ? new("Arama motoru hazır.", 100, Done: true)
            : new("Arama motoru cevap vermedi.", Error: "Kapsayıcı çalışıyor ama 60 saniyede cevap vermedi; birazdan tekrar kontrol et.");
    }

    private async Task<bool> DockerReadyAsync(CancellationToken cancellationToken)
    {
        int? exit = null;
        await foreach (var line in RunAsync("docker", ["info", "--format", "{{.ServerVersion}}"], TimeSpan.FromSeconds(20), cancellationToken))
            exit = line.ExitCode ?? exit;
        return exit == 0;
    }

    /// <summary>Kurulu Ultron'da {app}\searxng\settings.yml; geliştirmede proje kökündeki docker\searxng\settings.yml.</summary>
    private static string? FindSearxngSettings()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            foreach (var candidate in new[] { Path.Combine(dir.FullName, "searxng", "settings.yml"), Path.Combine(dir.FullName, "docker", "searxng", "settings.yml") })
                if (File.Exists(candidate))
                    return candidate;
        return null;
    }

    // ---- Anahtarlar ----

    /// <summary>Anahtarı Ultron'un baktığı dosyaya yazar. Tavily anahtarı tek bir aramayla denenir. Hata mesajı; başarılıysa null.</summary>
    public async Task<string?> SaveKeyAsync(string kind, string value, CancellationToken cancellationToken = default)
    {
        var key = (value ?? "").Trim();
        if (key.Length < 12 || key.Any(char.IsWhiteSpace))
            return "Anahtar eksik ya da hatalı görünüyor (boşluksuz, uzun bir metin olmalı).";
        var path = kind switch
        {
            "tavily" => web.Value.TavilyKeyFile,
            "groq" => Path.Combine(Root, GroqSpeechToText.KeyFile),
            "elevenlabs" => Path.Combine(Root, ElevenLabsTextToSpeech.KeyFile),
            _ => null,
        };
        if (path is null)
            return "Bilinmeyen anahtar türü.";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // ElevenLabs dosyasının 2. satırı (seçilen ses) varsa korunur.
        var extra = File.Exists(path) ? (await File.ReadAllLinesAsync(path, cancellationToken)).Skip(1).Where(l => l.Trim().Length > 0).ToArray() : [];
        await File.WriteAllLinesAsync(path, [key, .. extra], cancellationToken);
        if (kind == "tavily" && services.GetService<TavilyWebSearch>() is { } tavily)
        {
            var (outcome, _) = await tavily.SearchAsync("Ultron kurulum denemesi", [], cancellationToken);
            if (outcome == TavilyWebSearch.Outcome.BadKey)
            {
                File.Delete(path);
                return "Tavily bu anahtarı reddetti. Panelden kopyalarken eksik kalmış olabilir.";
            }
            if (outcome != TavilyWebSearch.Outcome.Ok)
                return "Anahtar kaydedildi ama deneme araması yapılamadı (internet?). Sonra tekrar denenecek.";
        }
        return null;
    }

    // ---- Tanışma formu ----

    /// <summary>Hitap ve şehir ayara; iş/okul ve ilgi alanları sohbetteki hafıza çıkarma hattından hafızaya. Uyarı; sorunsuzsa null.</summary>
    public async Task<string?> SaveProfileAsync(SetupProfile profile, CancellationToken cancellationToken = default)
    {
        var problems = new List<string>();
        if (await settings.SetAsync(UltronSettings.UserAddress, profile.Address ?? "", cancellationToken) is { } addressError)
            problems.Add(addressError);
        if (!string.IsNullOrWhiteSpace(profile.City) && await settings.SetAsync(UltronSettings.DefaultCity, profile.City, cancellationToken) is { } cityError)
            problems.Add(cityError);
        if (ProfileText(profile) is { } text)
        {
            try
            {
                await memory.ProcessAsync(text, null, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Tanışma bilgileri hafızaya yazılamadı");
                problems.Add("Bilgiler hafızaya yazılamadı (yapay zekâ motoru hazır değil); sohbette anlatabilirsin.");
            }
        }
        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    /// <summary>Formdaki bilgiler, kullanıcı sohbette anlatıyormuş gibi tek bir mesaj; boşsa null.</summary>
    internal static string? ProfileText(SetupProfile profile)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.City))
            parts.Add($"Yaşadığım şehir: {profile.City.Trim()}."); // "İzmir'da" gibi yanlış ek olmasın diye ek yok
        if (!string.IsNullOrWhiteSpace(profile.Work))
            parts.Add($"Ne iş yaptığım ya da ne okuduğum: {profile.Work.Trim()}.");
        if (!string.IsNullOrWhiteSpace(profile.Interests))
            parts.Add($"İlgi alanlarım: {profile.Interests.Trim()}.");
        return parts.Count == 0 ? null : "Kendimi tanıtayım. " + string.Join(" ", parts);
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default) =>
        settings.SetInternalAsync(CompletedKey, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), cancellationToken);

    // ---- Yardımcılar ----

    private static bool HasKey(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadLines(path).FirstOrDefault()?.Trim() is { Length: > 10 };
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool WingetAvailable() =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe"));

    private async Task<bool> IsUpAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await http.GetAsync(url, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return false;
        }
    }

    private async Task<(HttpResponseMessage? Response, string? Error)> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
                return (response, null);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            return (null, $"Sunucu {(int)response.StatusCode} döndü. {body[..Math.Min(body.Length, 200)]}");
        }
        catch (HttpRequestException ex)
        {
            return (null, "Bağlantı kurulamadı: " + ex.Message);
        }
    }

    private void StartDetached(string file)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); // Host kapanınca açık kalır
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "{File} başlatılamadı", file);
        }
    }

    internal sealed record ProcessLine(string? Text, int? ExitCode);

    /// <summary>Komutu çalıştırır, anlamlı çıktı satırlarını akış olarak verir; sonda ExitCode'lu satır (zaman aşımı/başlatılamazsa -1).</summary>
    private static async IAsyncEnumerable<ProcessLine> RunAsync(string file, IReadOnlyList<string> args, TimeSpan limit, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (process, error) = Start(file, args);
        if (process is null)
        {
            yield return new(error, -1);
            yield break;
        }
        using (process)
        {
            var lines = Channel.CreateUnbounded<string>();
            async Task PumpAsync(StreamReader reader)
            {
                while (await reader.ReadLineAsync() is { } line)
                    lines.Writer.TryWrite(line);
            }
            _ = Task.WhenAll(PumpAsync(process.StandardOutput), PumpAsync(process.StandardError)).ContinueWith(_ => lines.Writer.TryComplete(), TaskScheduler.Default);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(limit);
            while (await WaitAsync(lines.Reader, timeout.Token))
                while (lines.Reader.TryRead(out var line))
                    if (Meaningful(line) is { } text)
                        yield return new(text, null);
            if (timeout.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                yield return new("Komut zaman aşımına uğradı.", -1);
                yield break;
            }
            await process.WaitForExitAsync(cancellationToken);
            yield return new(null, process.ExitCode);
        }
    }

    private static (Process? Process, string? Error) Start(string file, IReadOnlyList<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            return (Process.Start(psi), null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (null, $"{file} çalıştırılamadı: {ex.Message}");
        }
    }

    private static async Task<bool> WaitAsync(ChannelReader<string> reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.WaitToReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>winget/docker çıktısındaki ilerleme çubuğu ve dönen çizgi karakterlerini atar; harf içermeyen satır gösterilmez.</summary>
    internal static string? Meaningful(string line)
    {
        var text = new string(line.Where(c => c is not ('█' or '▒' or '░' or '\b')).ToArray()).Trim();
        return text.Length > 1 && text.Any(char.IsLetter) ? text[..Math.Min(text.Length, 160)] : null;
    }
}
