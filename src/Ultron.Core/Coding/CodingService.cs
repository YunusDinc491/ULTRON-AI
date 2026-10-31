using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Ultron.Core.Computer;
using Ultron.Core.Text;

namespace Ultron.Core.Coding;

/// <summary>
/// Kodlama işlerinin şantiye şefi (Faz 13): Ultron isteği PLAN.md'ye yazar, işi Claude Code'a (taşerona) devreder,
/// ilerlemeyi izler; Claude bir karar için soru sorarsa kullanıcıya iletir, cevabı aynı oturuma geri verir.
/// Aynı anda tek iş çalışır. Claude'a sadece istek, PLAN.md ve proje kodu gider; kişisel hafıza gitmez.
/// </summary>
public sealed partial class CodingService(ICodingJobStore store, ICodeAgent agent, ICodingNotifier notifier, TimeProvider time, ILogger<CodingService> logger, CodingAccess? access = null)
{
    public static string ProjectsRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "projeler");

    private static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(3);
    private readonly Lock _gate = new();
    private CancellationTokenSource? _running;
    private long? _runningId;

    public bool IsBusy
    {
        get { lock (_gate) return _running is not null; }
    }

    public Task<IReadOnlyList<CodingJob>> RecentAsync(int limit = 5, CancellationToken cancellationToken = default) =>
        store.GetRecentAsync(limit, cancellationToken);

    public Task<CodeAgentStatus> AgentStatusAsync(CancellationToken cancellationToken = default) => agent.GetStatusAsync(cancellationToken);

    /// <summary>Proje adı → klasör ("Hava Durumu Sitesi" → …\Ultron\projeler\hava-durumu-sitesi); tam yol verildiyse o.</summary>
    public static string FolderFor(string project, string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder) && PathGuard.Resolve(folder) is { } path && Path.IsPathRooted(folder.Trim().Trim('"')))
            return path;
        var slug = string.Join('-', TurkishText.Normalize(project).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(ProjectsRoot, slug.Length == 0 ? "proje" : slug[..Math.Min(slug.Length, 50)]);
    }

    /// <summary>Yeni iş: klasörü hazırlar, PLAN.md'ye isteği yazar, Claude Code'u arka planda başlatır.</summary>
    public async Task<CodingJob> StartAsync(string project, string? folder, string request, CancellationToken cancellationToken = default)
    {
        var path = FolderFor(project, folder);
        if (PathGuard.IsSystem(path))
            throw new InvalidOperationException("Sistem klasöründe kodlama işi başlatılamaz.");
        lock (_gate)
        {
            if (_running is not null)
                throw new InvalidOperationException($"Şu an #{_runningId} numaralı kodlama işi çalışıyor; bitince ya da iptal edince yenisini başlatabilirim.");
        }

        Directory.CreateDirectory(path);
        await WritePlanAsync(path, project, request, cancellationToken);
        var job = await store.AddAsync(new CodingJob { Title = project.Trim(), Folder = path, Request = request.Trim(), StartedAtUtc = time.GetUtcNow().UtcDateTime }, cancellationToken);
        logger.LogInformation("Kodlama işi #{Id} başladı: {Title} ({Folder})", job.Id, job.Title, path);
        Launch(job, FirstPrompt);
        return job;
    }

    /// <summary>Claude'un sorusuna cevap ya da limit sonrası "devam et": son işin oturumuyla sürer.</summary>
    public async Task<CodingJob> ReplyAsync(string answer, long? jobId = null, CancellationToken cancellationToken = default)
    {
        var job = jobId is { } id ? await store.GetAsync(id, cancellationToken) : (await store.GetRecentAsync(5, cancellationToken))
            .FirstOrDefault(j => j.Status is CodingStatus.Soruyor or CodingStatus.Bekliyor or CodingStatus.Bitti or CodingStatus.Hata);
        if (job is null)
            throw new InvalidOperationException("Devam ettirilecek bir kodlama işi yok.");
        if (job.SessionId is null)
            throw new InvalidOperationException($"#{job.Id} işinin Claude Code oturumu yok; yeni iş başlat.");
        lock (_gate)
        {
            if (_running is not null)
                throw new InvalidOperationException($"#{_runningId} şu an çalışıyor; bitmesini bekle.");
        }

        var prompt = string.IsNullOrWhiteSpace(answer) || IsContinue(answer)
            ? "Kaldığın yerden devam et. " + Rules
            : $"Kullanıcının cevabı: {answer.Trim()}\nBuna göre devam et. " + Rules;
        job.Status = CodingStatus.Calisiyor;
        job.Progress = "devam ediyor";
        job.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await store.UpdateAsync(job, cancellationToken);
        Launch(job, prompt);
        return job;
    }

    public bool Cancel()
    {
        lock (_gate)
        {
            if (_running is null)
                return false;
            _running.Cancel();
            return true;
        }
    }

    private void Launch(CodingJob job, string prompt)
    {
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _running = cts;
            _runningId = job.Id;
        }

        _ = Task.Run(() => RunAsync(job, prompt, cts));
    }

    private async Task RunAsync(CodingJob job, string prompt, CancellationTokenSource cts)
    {
        var lastNotify = DateTime.MinValue;
        // Bu çalıştırmaya özel MCP anahtarı: Claude Code sadece bu projenin notlarını ve izinli hafızayı görür; iş bitince iptal.
        var token = access?.Issue(job.Id);
        try
        {
            await notifier.NotifyAsync(job, important: false, CancellationToken.None);
            var result = await agent.RunAsync(new CodeRunRequest(job.Folder, prompt, job.SessionId, token), line =>
            {
                job.Progress = line;
                var now = time.GetUtcNow().UtcDateTime;
                if (now - lastNotify < ProgressEvery)
                    return;
                lastNotify = now;
                job.UpdatedAtUtc = now;
                _ = SafeNotifyAsync(job, important: false);
                _ = SafeSaveAsync(job); // "kodlama ne durumda?" iş sürerken de güncel ilerlemeyi görsün
            }, cts.Token);

            job.SessionId = result.SessionId ?? job.SessionId;
            job.CostUsd += result.CostUsd;
            job.LastMessage = result.Text.Trim();
            job.Status = result.LimitReached ? CodingStatus.Bekliyor
                : !result.Ok ? CodingStatus.Hata
                : IsQuestion(result.Text) ? CodingStatus.Soruyor
                : CodingStatus.Bitti;
            job.Progress = null;
        }
        catch (OperationCanceledException)
        {
            job.Status = CodingStatus.Iptal;
            job.LastMessage = "İptal edildi.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kodlama işi #{Id} çalıştırılamadı", job.Id);
            job.Status = CodingStatus.Hata;
            job.LastMessage = "Claude Code çalıştırılamadı: " + ex.Message;
        }
        finally
        {
            if (token is not null)
                access!.Revoke(token);
            lock (_gate)
            {
                _running = null;
                _runningId = null;
            }
            cts.Dispose();
        }

        job.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await store.UpdateAsync(job, CancellationToken.None);
        logger.LogInformation("Kodlama işi #{Id}: {Status}", job.Id, job.Status);
        await SafeNotifyAsync(job, important: true);
    }

    private async Task SafeSaveAsync(CodingJob job)
    {
        try
        {
            await store.UpdateAsync(job, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Kodlama ilerlemesi kaydedilemedi #{Id}", job.Id);
        }
    }

    private async Task SafeNotifyAsync(CodingJob job, bool important)
    {
        try
        {
            await notifier.NotifyAsync(job, important, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kodlama bildirimi gönderilemedi #{Id}", job.Id);
        }
    }

    private async Task WritePlanAsync(string folder, string project, string request, CancellationToken cancellationToken)
    {
        var plan = Path.Combine(folder, "PLAN.md");
        var stamp = time.GetLocalNow().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        var section = new StringBuilder();
        if (!File.Exists(plan))
            section.AppendLine($"# {project.Trim()}").AppendLine().AppendLine("_Bu dosyayı Ultron, kullanıcının isteğinden yazdı; Claude Code bu plana göre çalışır._").AppendLine();
        section.AppendLine($"## İstek ({stamp})").AppendLine().AppendLine(request.Trim()).AppendLine();
        await File.AppendAllTextAsync(plan, section.ToString(), cancellationToken);
    }

    internal const string Rules =
        "Kurallar: kişisel bilgi isteme ya da arama; sadece bu proje klasöründe çalış. Bitince Türkçe, 3-6 kısa maddeyle ne yaptığını, " +
        "nasıl çalıştırılacağını ve kalan işleri yaz. Devam etmek için kullanıcının bir KARAR vermesi gerekiyorsa (teknoloji seçimi, belirsiz gereksinim) " +
        "işi orada bırak ve son mesajını 'SORU:' ile başlayan tek kısa soruyla bitir.";

    internal const string FirstPrompt =
        "Bu klasördeki PLAN.md dosyası kullanıcının isteğini anlatıyor. PLAN.md'yi oku ve uygula: gereken dosyaları oluştur ya da düzenle, " +
        "mümkünse derleyip çalıştırarak dene. " + McpHint + Rules;

    /// <summary>Ultron MCP araçları varsa (Host'ta) kullanılsın: önceki kararlar ve kullanıcının paylaştığı tercihler.</summary>
    internal const string McpHint =
        "Ultron MCP araçların varsa: işe başlarken proje_bilgisi ve kullanici_tercihleri'ni çağır, önemli kararlarını proje_notu_ekle ile kaydet. ";

    /// <summary>Son mesaj "SORU:" ile başlayan bir satır içeriyor mu?</summary>
    public static bool IsQuestion(string text) => Question().IsMatch(text);

    /// <summary>Claude'un sorusu ("SORU:" satırı); yoksa son mesajın kendisi.</summary>
    public static string QuestionText(string text) =>
        Question().Match(text) is { Success: true } m ? m.Groups[1].Value.Trim() : text.Trim();

    private static bool IsContinue(string text) =>
        TurkishText.Normalize(text) is "devam" or "devam et" or "devam edelim" or "kaldigin yerden devam et" or "surdur";

    [GeneratedRegex(@"(?im)^\W*SORU\s*:\s*(.+)$")]
    private static partial Regex Question();
}
