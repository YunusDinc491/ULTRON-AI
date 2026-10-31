using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ultron.Core.Coding;
using Ultron.Core.Routing;
using Ultron.Core.Tools;
using Ultron.Infrastructure.Coding;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Tools;

public sealed class CodingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ultron-kodlama-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryCodingStore _store = new();
    private readonly FakeAgent _agent = new();
    private readonly RecordingCodingNotifier _notifier = new();
    private readonly CodingService _coding;

    public CodingTests() =>
        _coding = new CodingService(_store, _agent, _notifier, TimeProvider.System, NullLogger<CodingService>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private async Task<CodingJob> WaitAsync(long id, CodingStatus status)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await _store.GetAsync(id) is { } job && job.Status == status && !_coding.IsBusy)
                return job;
            await Task.Delay(20);
        }
        throw new TimeoutException($"#{id} {status} olmadı");
    }

    [Fact]
    public async Task Is_PLAN_md_yazar_claude_soru_sorarsa_soruyor_cevapla_ayni_oturumla_surer()
    {
        _agent.Results.Enqueue(new CodeRunResult(true, "Index sayfasını hazırladım.\nSORU: Tema koyu mu açık mı olsun?", "oturum-1", 0.12));
        _agent.Results.Enqueue(new CodeRunResult(true, "- Koyu tema eklendi\n- `npm start` ile çalışır", "oturum-1", 0.08));

        var job = await _coding.StartAsync("Kitap Listesi", _folder, "Kitap ekleyip silinen basit bir web sitesi");
        var asked = await WaitAsync(job.Id, CodingStatus.Soruyor);

        Assert.Contains("Kitap ekleyip silinen basit bir web sitesi", await File.ReadAllTextAsync(Path.Combine(_folder, "PLAN.md")));
        Assert.Equal("oturum-1", asked.SessionId);
        Assert.Equal("Tema koyu mu açık mı olsun?", CodingService.QuestionText(asked.LastMessage!));
        Assert.Contains("PLAN.md", _agent.Requests[0].Prompt);
        Assert.Null(_agent.Requests[0].ResumeSessionId);
        Assert.Contains(_notifier.Updates, u => u.Important && u.Status == CodingStatus.Soruyor);

        await _coding.ReplyAsync("koyu olsun");
        var done = await WaitAsync(job.Id, CodingStatus.Bitti);

        Assert.Equal("oturum-1", _agent.Requests[1].ResumeSessionId);
        Assert.Contains("koyu olsun", _agent.Requests[1].Prompt);
        Assert.Equal(0.20, done.CostUsd, 3);
    }

    [Fact]
    public async Task Limit_dolunca_bekliyor_devam_et_ile_kaldigi_yerden()
    {
        _agent.Results.Enqueue(new CodeRunResult(false, "Claude usage limit reached.", "o-2", 0, LimitReached: true));
        _agent.Results.Enqueue(new CodeRunResult(true, "Tamamlandı.", "o-2", 0));

        var job = await _coding.StartAsync("Oyun", _folder, "Yılan oyunu");
        await WaitAsync(job.Id, CodingStatus.Bekliyor);
        await _coding.ReplyAsync("devam et");
        await WaitAsync(job.Id, CodingStatus.Bitti);

        Assert.StartsWith("Kaldığın yerden devam et", _agent.Requests[1].Prompt);
    }

    [Fact]
    public async Task Ayni_anda_tek_is_sistem_klasoru_yasak()
    {
        _agent.Gate = new TaskCompletionSource();
        _agent.Results.Enqueue(new CodeRunResult(true, "ok", "o", 0));
        await _coding.StartAsync("A", _folder, "a");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _coding.StartAsync("B", _folder + "-b", "b"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _coding.StartAsync("C", @"C:\Windows\x", "c"));
        _agent.Gate.SetResult();
    }

    [Fact]
    public async Task Baslatma_araci_onayli_giris_yoksa_baslamaz_klasor_adlandirma()
    {
        ITool tool = new StartCodingTool(_coding);
        Assert.Equal(PermissionLevel.Onayli, await tool.LevelForAsync(Args(new { proje = "x", istek = "y" })));
        Assert.Contains("kişisel hafıza gitmez", tool.Describe(Args(new { proje = "Hava Sitesi", istek = "hava" })));

        _agent.Status = new CodeAgentStatus(@"C:\claude.exe", false, "GİRİŞ YAPILMADI");
        var result = await tool.ExecuteAsync(Args(new { proje = "x", istek = "y" }));
        Assert.False(result.Ok);
        Assert.Contains("GİRİŞ YAPILMADI", result.ForModel);
        Assert.Empty(_agent.Requests);

        Assert.Equal(Path.Combine(CodingService.ProjectsRoot, "hava-durumu-sitesi"), CodingService.FolderFor("Hava Durumu Sitesi", null));
    }

    [Theory]
    [InlineData("Kitap listemi tutan basit bir web sitesi yapalım")]
    [InlineData("Claude'a söyle SQLite kullansın")]
    [InlineData("Kodlama işi ne durumda?")]
    public void Kodlama_istekleri_kodlama_araclarini_getirir(string text) =>
        Assert.Contains("kodlama_baslat", ToolRouting.Select(MessageIntent.Komut, text)!);

    [Fact]
    public void Stream_json_ilerleme_ve_sonuc_okunur()
    {
        var parser = new StreamJsonParser();
        Assert.Equal("Claude Code başladı", parser.Feed("""{"type":"system","subtype":"init","session_id":"s-1"}"""));
        Assert.Equal("Dosya yazıyor: index.html", parser.Feed("""{"type":"assistant","message":{"content":[{"type":"text","text":"Başlıyorum"},{"type":"tool_use","name":"Write","input":{"file_path":"C:\\p\\index.html"}}]}}"""));
        Assert.Equal("Ultron'dan proje bilgisini alıyor", parser.Feed("""{"type":"assistant","message":{"content":[{"type":"tool_use","name":"mcp__ultron__proje_bilgisi","input":{}}]}}"""));
        Assert.Equal("Komut: npm install", parser.Feed("""{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"npm install"}}]}}"""));
        Assert.Null(parser.Feed("çöp satır"));
        parser.Feed("""{"type":"result","subtype":"success","is_error":false,"result":"- Site hazır","session_id":"s-1","total_cost_usd":0.31}""");

        var result = parser.Result(0, "");
        Assert.Equal((true, "- Site hazır", "s-1", 0.31), (result.Ok, result.Text, result.SessionId, result.CostUsd));
    }

    [Fact]
    public void Giris_yok_ve_limit_mesajlari_tanınır()
    {
        var notLogged = new StreamJsonParser();
        notLogged.Feed("""{"type":"assistant","message":{"content":[{"type":"text","text":"Not logged in · Please run /login"}]}}""");
        notLogged.Feed("""{"type":"result","is_error":true,"session_id":"x"}""");
        var r1 = notLogged.Result(1, "");
        Assert.False(r1.Ok);
        Assert.Contains("giriş yap", r1.Text);

        var limited = new StreamJsonParser();
        limited.Feed("""{"type":"result","is_error":true,"result":"Claude AI usage limit reached|1761580800","session_id":"y"}""");
        Assert.True(limited.Result(1, "").LimitReached);
    }

    private sealed class FakeAgent : ICodeAgent
    {
        public Queue<CodeRunResult> Results { get; } = new();
        public List<CodeRunRequest> Requests { get; } = [];
        public CodeAgentStatus Status { get; set; } = new(@"C:\claude.exe", true, "hazır");
        public TaskCompletionSource? Gate { get; set; }

        public Task<CodeAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);

        public async Task<CodeRunResult> RunAsync(CodeRunRequest request, Action<string> progress, CancellationToken cancellationToken = default)
        {
            lock (Requests)
                Requests.Add(request);
            progress("Dosya yazıyor: index.html");
            if (Gate is not null)
                await Gate.Task;
            return Results.Dequeue();
        }
    }

    internal sealed class InMemoryCodingStore : ICodingJobStore
    {
        private readonly List<CodingJob> _jobs = [];
        private long _next = 1;

        public Task<CodingJob> AddAsync(CodingJob job, CancellationToken cancellationToken = default)
        {
            var stored = new CodingJob { Id = _next++, Title = job.Title, Folder = job.Folder, Request = job.Request, StartedAtUtc = job.StartedAtUtc };
            lock (_jobs)
                _jobs.Add(stored);
            return Task.FromResult(stored);
        }

        public Task UpdateAsync(CodingJob job, CancellationToken cancellationToken = default) => Task.CompletedTask; // aynı nesne

        public Task<CodingJob?> GetAsync(long id, CancellationToken cancellationToken = default)
        {
            lock (_jobs)
                return Task.FromResult(_jobs.FirstOrDefault(j => j.Id == id));
        }

        public Task<IReadOnlyList<CodingJob>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
        {
            lock (_jobs)
                return Task.FromResult<IReadOnlyList<CodingJob>>(_jobs.OrderByDescending(j => j.Id).Take(limit).ToList());
        }
    }

    private sealed class RecordingCodingNotifier : ICodingNotifier
    {
        public List<(CodingStatus Status, bool Important)> Updates { get; } = [];

        public Task NotifyAsync(CodingJob job, bool important, CancellationToken cancellationToken = default)
        {
            lock (Updates)
                Updates.Add((job.Status, important));
            return Task.CompletedTask;
        }
    }
}
