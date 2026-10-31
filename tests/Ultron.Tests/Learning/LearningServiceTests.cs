using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Core.Coding;
using Ultron.Core.Learning;
using Ultron.Core.Settings;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Learning;

public sealed class LearningServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ultron-denetim-test-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryReviewStore _store = new();
    private readonly FakeAuditAgent _agent = new();
    private readonly RecordingNotifier _notifier = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 4, 30, 0, TimeSpan.Zero));
    private readonly LearningService _service;

    // Soru vektörleri: aynı yönde = benzer soru.
    private static readonly Dictionary<string, float[]> Vectors = new()
    {
        ["CommonJS ile ESM farkı ne?"] = [1f, 0f, 0f],
        ["require ile import farkı nedir?"] = [0.95f, 0.31f, 0f],
        ["Flexbox mı Grid mi?"] = [0f, 1f, 0f],
    };

    public LearningServiceTests()
    {
        _service = new LearningService(_store, new FakeEmbeddingModel(Vectors), _agent, _notifier,
            new UltronSettings(new InMemorySettingsStore(), _time),
            Options.Create(new LearningOptions { Folder = _folder, ReferenceFolders = [] }), _time, NullLogger<LearningService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task Denetim_yanlis_cevaba_oneri_cikarir_onaylaninca_benzer_soruda_nota_girer()
    {
        await _service.RecordAsync("CommonJS ile ESM farkı ne?", "CommonJS eşzamansız yükler.", "");
        await _service.RecordAsync("Flexbox mı Grid mi?", "Flexbox tek boyut, Grid iki boyut.", "");
        _agent.Reply = """
            İnceledim.
            {"degerlendirmeler":[
              {"id":1,"karar":"yanlis","duzeltme":"require eşzamanlı (senkron) yükler; ESM import statiktir.","gerekce":"CommonJS'i eşzamansız sandı.","kaynak":"nodejs.org/api/modules.html"},
              {"id":2,"karar":"dogru","duzeltme":"","gerekce":"","kaynak":""}],
             "rapor":"Modül sistemlerinde karışıklık var."}
            """;

        var report = await _service.AuditAsync();

        Assert.Contains("2 cevap okundu, 0 düzeltme kaynaklı olduğu için öğrenildi, 1 öneri onay bekliyor", report);
        Assert.Equal((2, 0, 1), _notifier.Last); // "nodejs.org/…" adres değil (https yok): onaya gelir
        Assert.Equal(["Read", "Glob", "Grep"], _agent.Requests.Single().AllowedTools);
        Assert.True(_agent.Requests.Single().ReadOnly);
        Assert.True(File.Exists(Path.Combine(_agent.Requests.Single().Folder, LearningService.InputFile)));
        var pending = Assert.Single(await _service.PendingAsync());
        Assert.Equal(ReviewVerdict.Yanlis, pending.Verdict);

        Assert.Null(await _service.NoteForAsync("require ile import farkı nedir?")); // henüz onaylanmadı
        Assert.True(await _service.DecideAsync(pending.Id, accept: true));
        Assert.Contains("eşzamanlı", await _service.NoteForAsync("require ile import farkı nedir?"));
        Assert.Null(await _service.NoteForAsync("Flexbox mı Grid mi?")); // alakasız soru
        Assert.Empty(await _service.PendingAsync());
    }

    [Fact]
    public async Task Reddedilen_oneriye_benzeyen_yeni_oneri_gosterilmez()
    {
        await _service.RecordAsync("CommonJS ile ESM farkı ne?", "Cevap A", "");
        _agent.Reply = """{"degerlendirmeler":[{"id":1,"karar":"eksik","duzeltme":"X","gerekce":"","kaynak":""}]}""";
        await _service.AuditAsync();
        Assert.True(await _service.DecideAsync(1, accept: false));

        _time.Advance(TimeSpan.FromDays(1));
        await _service.RecordAsync("require ile import farkı nedir?", "Cevap B", "");
        _agent.Reply = """{"degerlendirmeler":[{"id":2,"karar":"eksik","duzeltme":"Y","gerekce":"","kaynak":""}]}""";
        await _service.AuditAsync();

        Assert.Empty(await _service.PendingAsync());
    }

    [Fact]
    public async Task Atlanan_madde_denetlenmemis_kalir_okunamayan_cevapta_isaret_yazilmaz()
    {
        await _service.RecordAsync("Flexbox mı Grid mi?", "Cevap", "");
        _agent.Reply = "JSON yok, üzgünüm.";
        Assert.Contains("okunamadı", await _service.AuditAsync());
        Assert.Null(await _service.LastAuditUtcAsync());
        Assert.Single(await _store.GetUnauditedAsync(10));

        _agent.Reply = """{"degerlendirmeler":[]}""";
        await _service.AuditAsync();
        Assert.Single(await _store.GetUnauditedAsync(10)); // Claude atladı: sonraki gece tekrar bakılır
    }

    [Fact]
    public async Task Yeni_cevap_yokken_de_bildirim_gider()
    {
        Assert.Contains("yok", await _service.AuditAsync());
        Assert.Equal((0, 0, 0), _notifier.Last); // "Şimdi denetle" bekleyen istemci asılı kalmasın
        Assert.Empty(_agent.Requests);
    }

    [Fact]
    public async Task Claude_Code_girisi_yoksa_denetim_ertelenir()
    {
        await _service.RecordAsync("Flexbox mı Grid mi?", "Cevap", "");
        _agent.Status = new CodeAgentStatus(null, false, "Claude Code bulunamadı.");

        Assert.Contains("yapılamadı", await _service.TryRunScheduledAsync());
        Assert.Empty(_agent.Requests);
        Assert.Null(await _service.LastAuditUtcAsync());
    }

    [Fact]
    public async Task Resmi_kaynakli_duzeltme_kendiliginden_ogrenilir_geri_alinabilir()
    {
        await _service.RecordAsync("CommonJS ile ESM farkı ne?", "CommonJS eşzamansız yükler.", "");
        await _service.RecordAsync("Flexbox mı Grid mi?", "Flexbox iki boyutludur.", "");
        _agent.Reply = """
            {"degerlendirmeler":[
              {"id":1,"karar":"yanlis","duzeltme":"require senkron yükler.","gerekce":"g","kaynak":"https://nodejs.org/api/modules.html","sade":"Node dosyaları sırayla yükler."},
              {"id":2,"karar":"yanlis","duzeltme":"Flexbox tek boyutludur.","gerekce":"g","kaynak":"https://github.com/biri/blog","sade":"s"}]}
            """;

        await _service.AuditAsync();

        Assert.Equal((2, 1, 1), _notifier.Last); // github.com resmi kaynak sayılmaz
        var learned = Assert.Single(await _service.LearnedAsync());
        Assert.True(learned.AutoApproved);
        Assert.Equal("Node dosyaları sırayla yükler.", learned.PlainSummary);
        Assert.Contains("senkron", await _service.NoteForAsync("require ile import farkı nedir?"));

        Assert.True(await _service.UndoAsync(learned.Id));
        Assert.Null(await _service.NoteForAsync("require ile import farkı nedir?"));
        Assert.Empty(await _service.LearnedAsync());
    }

    [Theory]
    [InlineData("https://react.dev/reference/react-dom/components/input", true)]
    [InlineData("C:\\Users\\x\\kutuphane\\kaynaklar\\nodejs\\doc\\api\\modules.md (Loading ECMAScript)", true)]
    [InlineData("https://learn.microsoft.com/en-us/ef/core/providers/cosmos/modeling", true)]
    [InlineData("https://stackoverflow.com/questions/1", false)]
    [InlineData("react.dev", false)]
    [InlineData(null, false)]
    public void Resmi_kaynak_tanima(string? reference, bool expected)
    {
        var service = new LearningService(_store, new FakeEmbeddingModel(Vectors), _agent, _notifier,
            new UltronSettings(new InMemorySettingsStore(), _time),
            Options.Create(new LearningOptions { Folder = _folder, ReferenceFolders = ["C:\\Users\\x\\kutuphane\\kaynaklar"] }), _time, NullLogger<LearningService>.Instance);
        Assert.Equal(expected, service.HasOfficialReference(reference));
    }

    [Fact]
    public void Son_slot_04_00()
    {
        Assert.Equal(new DateTime(2026, 10, 29, 4, 0, 0), LearningService.LastSlot(new DateTime(2026, 10, 29, 9, 15, 0)));
        Assert.Equal(new DateTime(2026, 10, 28, 4, 0, 0), LearningService.LastSlot(new DateTime(2026, 10, 29, 3, 59, 0)));
    }

    [Fact]
    public void Id_metin_olarak_gelse_de_okunur_bilinmeyen_karar_atlanir()
    {
        var parsed = LearningService.ParseVerdicts("""
            ```json
            {"degerlendirmeler":[{"id":"7","karar":"Yanlış","duzeltme":"D"},{"id":8,"karar":"belki"}]}
            ```
            """);

        Assert.NotNull(parsed);
        Assert.Equal(ReviewVerdict.Yanlis, Assert.Single(parsed.Items).Value.Verdict);
        Assert.Equal(7, parsed.Items.Keys.Single());
    }

    private sealed class FakeAuditAgent : ICodeAgent
    {
        public string Reply { get; set; } = """{"degerlendirmeler":[]}""";
        public CodeAgentStatus Status { get; set; } = new(@"C:\claude.exe", true, "hazır");
        public List<CodeRunRequest> Requests { get; } = [];

        public Task<CodeAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);

        public Task<CodeRunResult> RunAsync(CodeRunRequest request, Action<string> progress, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new CodeRunResult(true, Reply, "oturum", 0.05));
        }
    }

    private sealed class RecordingNotifier : ILearningNotifier
    {
        public (int Reviewed, int Learned, int Pending)? Last { get; private set; }

        public Task NotifyAuditedAsync(int reviewed, int learned, int pending, CancellationToken cancellationToken = default)
        {
            Last = (reviewed, learned, pending);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryReviewStore : IAnswerReviewStore
    {
        private readonly List<AnswerReview> _items = [];

        public Task<AnswerReview> AddAsync(AnswerReview review, CancellationToken cancellationToken = default)
        {
            var stored = new AnswerReview
            {
                Id = _items.Count + 1, AskedAtUtc = review.AskedAtUtc, Question = review.Question, Answer = review.Answer,
                Sources = review.Sources, Embedding = review.Embedding,
            };
            _items.Add(stored);
            return Task.FromResult(stored);
        }

        public Task UpdateAsync(AnswerReview review, CancellationToken cancellationToken = default)
        {
            _items[_items.FindIndex(r => r.Id == review.Id)] = review;
            return Task.CompletedTask;
        }

        public Task<AnswerReview?> GetAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(r => r.Id == id));

        public Task<IReadOnlyList<AnswerReview>> GetUnauditedAsync(int max, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AnswerReview>>(_items.Where(r => r.AuditedAtUtc is null).Take(max).ToList());

        public Task<IReadOnlyList<AnswerReview>> GetPendingSuggestionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AnswerReview>>(_items.Where(r => r.IsPendingSuggestion).Reverse().ToList());

        public Task<IReadOnlyList<AnswerReview>> GetDecidedAsync(ReviewDecision decision, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AnswerReview>>(_items.Where(r => r.Decision == decision).ToList());
    }
}
