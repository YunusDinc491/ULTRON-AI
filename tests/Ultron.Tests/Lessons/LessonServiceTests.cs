using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Core.Calendar;
using Ultron.Core.Coding;
using Ultron.Core.Documents;
using Ultron.Core.Lessons;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Lessons;

public sealed class LessonServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ultron-ders-test-" + Guid.NewGuid().ToString("N"));
    private readonly ScriptedChatModel _chat = new() { AnswerChunks = ["Entropi ", "düzensizliğin ölçüsüdür [1]."] };
    private readonly ToolHarness _tools = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLessonStore _store = new();
    private readonly ReviewAgent _agent = new();
    private readonly DocumentService _documents;
    private readonly LessonService _service;

    public LessonServiceTests()
    {
        _documents = new DocumentService(new InMemoryDocumentStore(), new FakeTextExtractor(), new FakeEmbeddingModel([]),
            Options.Create(new DocumentOptions { Folder = Path.Combine(_root, "belgeler") }), NullLogger<DocumentService>.Instance);
        _service = new LessonService(_store, _chat, _documents, _tools.Reminders, _agent, new LogLessonNotifier(), _tools.Settings,
            Options.Create(new LessonOptions { WorkFolder = Path.Combine(_root, "ders") }), _tools.Time, NullLogger<LessonService>.Instance,
            calendar: _tools.Calendar);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Soru_sadece_o_dersin_kaynaklariyla_ve_notlariyla_cevaplanir()
    {
        var termo = await _service.CreateSessionAsync("Termodinamik", null);
        var akiskan = await _service.CreateSessionAsync("Akışkanlar", null);
        await _service.AddSourceTextAsync(termo.Id, "Hafta 5", "Entropi: düzensizliğin ölçüsü, ds = dq/T.", exam: false);
        await _service.AddSourceTextAsync(akiskan.Id, "Hafta 2", "Bernoulli denklemi: basınç ve hız ilişkisi.", exam: false);
        await _service.AddNoteAsync(termo.Id, "Hoca entropiyi s ile gösteriyor.");
        await _service.UpdateSessionAsync(termo.Id, "", "Kısa anlat.");

        var events = await Collect(_service.AskAsync(termo.Id, "Entropi nedir, kısaca anlatır mısın?"));

        Assert.False(Assert.IsType<LessonStarted>(events[0]).Solving);
        var done = Assert.IsType<LessonCompleted>(events[^1]);
        Assert.Equal("Entropi düzensizliğin ölçüsüdür [1].", done.Message.Content);
        Assert.False(done.Message.Unsure);
        Assert.Contains("Hafta 5", done.Message.Sources);
        var system = _chat.StreamRequests.Single()[0].Content;
        Assert.Contains("\"Termodinamik\" dersinde", system);
        Assert.Contains("Hoca entropiyi s ile gösteriyor.", system);
        Assert.Contains("Kısa anlat.", system);
        Assert.Contains("ds = dq/T", system);
        Assert.DoesNotContain("Bernoulli", system); // dersler karışmaz
        Assert.Equal(2, (await _service.MessagesAsync(termo.Id)).Count);
    }

    [Fact]
    public async Task Kaynaksiz_ders_sorusunda_Claude_karti_cikar()
    {
        var session = await _service.CreateSessionAsync("Isı transferi", null);

        var done = Assert.IsType<LessonCompleted>((await Collect(_service.AskAsync(session.Id, "Biot sayısı neyi ifade eder?")))[^1]);

        Assert.True(done.Message.Unsure);
    }

    [Fact]
    public async Task Sayisal_problemde_sayilari_hesaplayici_bulur()
    {
        var session = await _service.CreateSessionAsync("Termodinamik", null);

        var events = await Collect(_service.AskAsync(session.Id, "Hava 300 K'den sıkıştırma oranı 8 ile izantropik sıkıştırılıyor (k=1,4). Son sıcaklığı hesapla."));

        Assert.True(Assert.IsType<LessonStarted>(events[0]).Solving);
        var done = Assert.IsType<LessonCompleted>(events[^1]);
        Assert.Equal("hesap", done.Message.Author);
        Assert.False(done.Message.Unsure);
        Assert.Contains("**Sonuç:** T2 = **543,4 K**", done.Message.Content);
        Assert.Empty(_chat.StreamRequests); // akışlı model çağrılmadı: sayıyı model yazmadı
    }

    [Fact]
    public void Hesaplanamayan_adim_isaretlenir_ve_Claude_onerilir()
    {
        var (text, ok) = LessonService.RenderSolution("""{"verilenler":[],"adimlar":[{"aciklama":"Verim","ad":"eta","ifade":"1 - TL/TH","birim":""}],"sonuc":"eta","yorum":""}""")!.Value;

        Assert.False(ok);
        Assert.Contains("hesaplanamadı", text);
    }

    [Fact]
    public async Task Not_al_dersen_sadece_o_dersin_notuna_girer()
    {
        var session = await _service.CreateSessionAsync("Termodinamik", null);
        _chat.LessonNoteJson = """{"kayit":true,"metin":"Vizede ideal gaz tabloları verilecek."}""";

        var done = Assert.IsType<LessonCompleted>((await Collect(_service.AskAsync(session.Id, "Not al: vizede ideal gaz tabloları verilecekmiş")))[^1]);

        Assert.Contains("Not aldım", done.Message.Content);
        Assert.Equal("Vizede ideal gaz tabloları verilecek.", Assert.Single(await _service.NotesAsync(session.Id)).Text);
        Assert.Empty(_chat.StreamRequests);
    }

    [Fact]
    public void Quiz_ayrisir_sayisal_cevabi_hesaplayici_bulur_bozuk_soru_atilir()
    {
        var questions = LessonService.ParseQuiz("""
            {"sorular":[
              {"tur":"coktan","soru":"Entropi hangi durumda artar?","secenekler":["A) Tersinir adyabatik","Tersinmez süreçte","Hiçbir zaman","Sabit sıcaklıkta her zaman"],"dogru":"B","cozum_ifadesi":"","birim":"","konu":"Entropi","aciklama":"Tersinmezlik entropi üretir."},
              {"tur":"sayisal","soru":"300 K ve 600 K arasında Carnot verimi kaçtır?","secenekler":[],"dogru":"","cozum_ifadesi":"1-300/600","birim":"","konu":"Carnot","aciklama":"η = 1 - TL/TH"},
              {"tur":"sayisal","soru":"Bozuk ifadeli soru burada","secenekler":[],"dogru":"","cozum_ifadesi":"TL/TH","birim":"","konu":"Carnot","aciklama":""},
              {"tur":"acik","soru":"Birinci yasayı bir cümleyle yaz.","secenekler":[],"dogru":"Enerji korunur; yoktan var edilemez.","cozum_ifadesi":"","birim":"","konu":"Birinci yasa","aciklama":""}
            ]}
            """);

        Assert.Equal(["coktan", "sayisal", "acik"], questions.Select(q => q.Type));
        Assert.Equal("Tersinir adyabatik", questions[0].Choices[0]); // "A) " öneki temizlendi
        Assert.Equal("0,5", questions[1].Answer);
    }

    [Fact]
    public async Task Quiz_puanlanir_yanlislar_zayif_konu_olur()
    {
        var session = await _service.CreateSessionAsync("Termodinamik", null);
        _chat.LessonQuizJson = """
            {"sorular":[
              {"tur":"coktan","soru":"Entropi hangi durumda artar?","secenekler":["Tersinir adyabatik","Tersinmez süreçte","Hiçbir zaman","Sabit hacimde"],"dogru":"B","cozum_ifadesi":"","birim":"","konu":"Entropi","aciklama":"Tersinmezlik entropi üretir."},
              {"tur":"sayisal","soru":"300 K ve 600 K arasında Carnot verimi kaçtır?","secenekler":[],"dogru":"","cozum_ifadesi":"1-300/600","birim":"","konu":"Carnot","aciklama":"η = 1 - TL/TH"},
              {"tur":"acik","soru":"Birinci yasayı bir cümleyle yaz.","secenekler":[],"dogru":"Enerji korunur.","cozum_ifadesi":"","birim":"","konu":"Birinci yasa","aciklama":""}
            ]}
            """;
        _chat.LessonGradeJson = """{"sonuclar":[{"dogru":true,"geri_bildirim":"Ana fikir doğru."}]}""";

        // 4 soru = 2 parti (coktan+sayisal, coktan+acik); sahte model her partiye aynı listeyi verir, partiden sadece istenen türler alınır.
        var quiz = await _service.MakeQuizAsync(session.Id, "Termodinamik", 4);
        Assert.NotNull(quiz);
        Assert.Equal(["coktan", "sayisal", "coktan", "acik"], LessonService.ReadQuiz(quiz).Select(q => q.Type));
        var grade = await _service.GradeQuizAsync(quiz.Id, ["A", "0,5", "A", "Enerji yoktan var olmaz, korunur."]);

        Assert.NotNull(grade);
        Assert.Equal(2, grade.Correct);
        Assert.False(grade.Results[0].Correct);
        Assert.True(grade.Results[3].Correct);
        Assert.Equal(["Entropi"], grade.WeakTopics);
        Assert.Contains(await _service.TopicsAsync(session.Id), t => t.Topic == "Carnot" && t.Hits == 1);
    }

    [Fact]
    public async Task Bozuk_parti_icin_eksik_sorular_bir_kez_daha_istenir()
    {
        var session = await _service.CreateSessionAsync("Termodinamik", null);
        var calls = 0;
        _chat.LessonQuizJsonFactory = () => ++calls == 1
            ? """{"sorular":[{"tur":"sayisal","soru":"Bozuk ifadeli bir soru burada","secenekler":[],"dogru":"","cozum_ifadesi":"TL/TH","birim":"","konu":"Carnot","aciklama":""}]}"""
            : """{"sorular":[{"tur":"coktan","soru":"Entropi hangi durumda artar?","secenekler":["Tersinir adyabatik","Tersinmez süreçte","Hiçbir zaman","Sabit hacimde"],"dogru":"B","cozum_ifadesi":"","birim":"","konu":"Entropi","aciklama":""},{"tur":"sayisal","soru":"300 K ve 600 K arasında Carnot verimi kaçtır?","secenekler":[],"dogru":"","cozum_ifadesi":"1-300/600","birim":"","konu":"Carnot","aciklama":""}]}""";

        var quiz = await _service.MakeQuizAsync(session.Id, "Termodinamik", 3);

        Assert.Equal(["coktan", "sayisal", "coktan"], LessonService.ReadQuiz(quiz!).Select(q => q.Type));
    }

    [Fact]
    public void Quiz_turleri_karisik_dagilir() =>
        Assert.Equal(["coktan", "sayisal", "coktan", "acik", "coktan", "sayisal"], LessonService.QuizTypes(6));

    [Fact]
    public async Task Acik_uclu_cevap_modelle_puanlanir()
    {
        var session = await _service.CreateSessionAsync("Termodinamik", null);
        _chat.LessonQuizJson = """{"sorular":[{"tur":"acik","soru":"Birinci yasayı bir cümleyle yaz.","secenekler":[],"dogru":"Enerji korunur.","cozum_ifadesi":"","birim":"","konu":"Birinci yasa","aciklama":""}]}""";
        _chat.LessonGradeJson = """{"sonuclar":[{"dogru":true,"geri_bildirim":"Ana fikir doğru."}]}""";

        var quiz = await _service.MakeQuizAsync(session.Id, "Birinci yasa", 4); // 4. soru açık uçlu; diğer türleri sahte model vermiyor
        var grade = await _service.GradeQuizAsync(quiz!.Id, ["Enerji yoktan var olmaz, korunur."]);

        Assert.True(grade!.Results[0].Correct);
        Assert.Contains("Ana fikir doğru", grade.Results[0].Feedback);
    }

    [Theory]
    [InlineData("543", "543,4", true)]
    [InlineData("543,4 K", "543,4", true)]
    [InlineData("5,434e2", "543,4", true)]
    [InlineData("600", "543,4", false)]
    [InlineData("", "543,4", false)]
    public void Sayisal_cevap_yuzde_iki_toleransla_kabul(string answer, string expected, bool ok) =>
        Assert.Equal(ok, LessonService.NumericMatch(answer, expected));

    [Fact]
    public void Plan_zayif_konulari_one_alir_son_gun_genel_tekrar()
    {
        var today = new DateTime(2026, 10, 28);
        var plan = LessonService.BuildPlan(today, today.AddDays(4), ["Entropi"], ["Birinci yasa", "entropi", "Carnot çevrimi", "Rankine"]);

        Assert.Equal(4, plan.Count);
        Assert.StartsWith("Entropi, Birinci yasa", plan[0].Work);
        Assert.Contains("Genel tekrar", plan[^1].Work);
        Assert.Equal(today.AddDays(3), plan[^1].Date);
        Assert.DoesNotContain(plan, p => p.Work.Contains("entropi,", StringComparison.Ordinal)); // tekrar eden konu tek
    }

    [Fact]
    public async Task Plan_kaydedilir_hatirlatma_kurulur_sinav_tarihi_derse_yazilir()
    {
        await _tools.Calendar.AddAsync(new CalendarEvent { Title = "Termodinamik", Day = DayOfWeek.Monday, Start = new TimeOnly(10, 0), End = new TimeOnly(12, 0) });
        var session = await _service.CreateSessionAsync("", "Termodinamik");

        var (plan, message) = await _service.MakePlanAsync(session.Id, "Vize", new DateTime(2026, 11, 2), addReminders: true);

        Assert.NotNull(plan);
        Assert.Contains("Ders saatleri: Pazartesi", plan.Content);
        Assert.Contains("hatırlatma kuruldu", message);
        Assert.Equal(6, _tools.Store.Items.Count); // 28 Ekim–1 Kasım beş çalışma akşamı + sınav sabahı
        Assert.Equal("Vize", (await _service.GetSessionAsync(session.Id))!.ExamName);
        Assert.Contains("Termodinamik", await _service.CoursesAsync());
    }

    [Fact]
    public async Task Gece_denetiminde_kaynakli_duzeltme_o_dersin_notuna_girer()
    {
        var session = await _service.CreateSessionAsync("Termodinamik", null);
        _chat.AnswerChunks = ["Entropi bir sistemdeki düzensizliğin ölçüsüdür; ", "tersinir süreçte hep artar."];
        await Collect(_service.AskAsync(session.Id, "Entropi nedir, kısaca anlatır mısın?"));
        var answer = (await _service.MessagesAsync(session.Id)).Last();
        _agent.Reply = $$"""Tamam. {"cevaplar":[{"id":{{answer.Id}},"karar":"duzeltilmeli","duzeltme":"Entropi tersinmez süreçte artar.","kaynak":"Hafta 5 · s. 2"}],"rapor":"1 düzeltme"}""";

        var report = await _service.ReviewAsync();

        Assert.Contains("1 düzeltme", report);
        Assert.True(Assert.Single(_agent.Requests).ReadOnly);
        var note = Assert.Single(await _service.NotesAsync(session.Id));
        Assert.Equal("Gece denetimi", note.Source);
        Assert.Contains("Hafta 5 · s. 2", note.Text);
        Assert.Null(await _service.TryReviewAsync()); // aynı gece ikinci kez çalışmaz
    }

    [Fact]
    public void Tanimsiz_ad_bildirilir()
    {
        var (_, ok) = LessonService.RenderSolution("""{"verilenler":[{"ad":"k","deger":"1,4","birim":""}],"adimlar":[{"aciklama":"İzantropik","ad":"T2","ifade":"T1*(P2/P1)^((k-1)/k)","birim":"K"}],"sonuc":"T2","yorum":""}""", out var missing)!.Value;

        Assert.False(ok);
        Assert.Equal(["T1", "P2", "P1"], missing);
    }

    [Fact]
    public async Task Tanimsiz_adda_model_bir_kez_uyarilir_ve_duzeltir()
    {
        var session = await _service.CreateSessionAsync("Termodinamik", null);
        var queue = new Queue<string>([
            """{"verilenler":[{"ad":"k","deger":"1,4","birim":""}],"adimlar":[{"aciklama":"İzantropik","ad":"T2","ifade":"T1*8^((k-1)/k)","birim":"K"}],"sonuc":"T2","yorum":""}""",
            """{"verilenler":[{"ad":"T1","deger":"300","birim":"K"},{"ad":"k","deger":"1,4","birim":""}],"adimlar":[{"aciklama":"İzantropik","ad":"T2","ifade":"T1*8^((k-1)/k)","birim":"K"}],"sonuc":"T2","yorum":""}""",
        ]);
        _chat.LessonProblemJsonFactory = () => queue.Dequeue();

        var done = Assert.IsType<LessonCompleted>((await Collect(_service.AskAsync(session.Id, "300 K'deki hava basınç oranı 8 ile izantropik sıkıştırılıyor (k=1,4). Son sıcaklığı hesapla.")))[^1]);

        Assert.False(done.Message.Unsure);
        Assert.Contains("543,4 K", done.Message.Content);
        Assert.Empty(queue);
    }

    [Fact]
    public void LaTeX_kacislari_geri_cevrilir() =>
        Assert.Equal("$\\text{K}$ ve $\\frac{a}{b}$\r\nyeni satır", LessonService.FixLatex("$\text{K}$ ve $\frac{a}{b}$\r\nyeni satır"));

    [Fact]
    public void Problem_algilama()
    {
        Assert.True(LessonService.IsProblem("300 K ve 600 K arasında çalışan Carnot makinesinin verimini hesapla"));
        Assert.False(LessonService.IsProblem("Entropi nedir?"));
        Assert.False(LessonService.IsProblem("2. yasa ne der?"));
    }

    private static async Task<List<LessonEvent>> Collect(IAsyncEnumerable<LessonEvent> events)
    {
        var list = new List<LessonEvent>();
        await foreach (var e in events)
            list.Add(e);
        return list;
    }

    private sealed class ReviewAgent : ICodeAgent
    {
        public string Reply { get; set; } = """{"cevaplar":[]}""";
        public List<CodeRunRequest> Requests { get; } = [];

        public Task<CodeAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new CodeAgentStatus(@"C:\claude.exe", true, "hazır"));

        public Task<CodeRunResult> RunAsync(CodeRunRequest request, Action<string> progress, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new CodeRunResult(true, Reply, "oturum", 0.05));
        }
    }

    private sealed class InMemoryLessonStore : ILessonStore
    {
        private long _id;
        private readonly List<LessonSession> _sessions = [];
        private readonly List<LessonMessage> _messages = [];
        private readonly List<LessonNote> _notes = [];
        private readonly List<LessonMaterial> _materials = [];
        private readonly List<WeakTopic> _topics = [];

        private long Next() => ++_id;

        public Task<LessonSession> AddSessionAsync(LessonSession session, CancellationToken cancellationToken = default)
        {
            var copy = new LessonSession { Id = Next(), Name = session.Name, Course = session.Course, CreatedAtUtc = session.CreatedAtUtc, UpdatedAtUtc = session.UpdatedAtUtc };
            _sessions.Add(copy);
            return Task.FromResult(copy);
        }

        public Task UpdateSessionAsync(LessonSession session, CancellationToken cancellationToken = default)
        {
            _sessions[_sessions.FindIndex(s => s.Id == session.Id)] = session;
            return Task.CompletedTask;
        }

        public Task<LessonSession?> GetSessionAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_sessions.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<LessonSession>> GetSessionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LessonSession>>(_sessions.ToList());

        public Task<bool> DeleteSessionAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_sessions.RemoveAll(s => s.Id == id) > 0);

        public Task<LessonMessage> AddMessageAsync(LessonMessage message, CancellationToken cancellationToken = default)
        {
            var copy = new LessonMessage
            {
                Id = Next(), SessionId = message.SessionId, FromUser = message.FromUser, Content = message.Content, Sources = message.Sources,
                Author = message.Author, Unsure = message.Unsure, CreatedAtUtc = message.CreatedAtUtc,
            };
            _messages.Add(copy);
            return Task.FromResult(copy);
        }

        public Task UpdateMessageAsync(LessonMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<LessonMessage>> GetMessagesAsync(long sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LessonMessage>>(_messages.Where(m => m.SessionId == sessionId).ToList());

        public Task<LessonMessage?> GetMessageAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_messages.FirstOrDefault(m => m.Id == id));

        public Task<IReadOnlyList<LessonMessage>> GetUnreviewedAsync(int max, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LessonMessage>>(_messages.Where(m => !m.FromUser && m.ReviewedAtUtc is null && m.Author != "claude").Take(max).ToList());

        public Task<LessonNote> AddNoteAsync(LessonNote note, CancellationToken cancellationToken = default)
        {
            var copy = new LessonNote { Id = Next(), SessionId = note.SessionId, Text = note.Text, Source = note.Source, CreatedAtUtc = note.CreatedAtUtc };
            _notes.Add(copy);
            return Task.FromResult(copy);
        }

        public Task<IReadOnlyList<LessonNote>> GetNotesAsync(long sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LessonNote>>(_notes.Where(n => n.SessionId == sessionId && n.IsActive).ToList());

        public Task<bool> DeactivateNoteAsync(long id, CancellationToken cancellationToken = default)
        {
            if (_notes.FirstOrDefault(n => n.Id == id) is not { } note)
                return Task.FromResult(false);
            note.IsActive = false;
            return Task.FromResult(true);
        }

        public Task<LessonMaterial> AddMaterialAsync(LessonMaterial material, CancellationToken cancellationToken = default)
        {
            var copy = new LessonMaterial { Id = Next(), SessionId = material.SessionId, Kind = material.Kind, Title = material.Title, Content = material.Content };
            _materials.Add(copy);
            return Task.FromResult(copy);
        }

        public Task UpdateMaterialAsync(LessonMaterial material, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LessonMaterial?> GetMaterialAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_materials.FirstOrDefault(m => m.Id == id));

        public Task<IReadOnlyList<LessonMaterial>> GetMaterialsAsync(long sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LessonMaterial>>(_materials.Where(m => m.SessionId == sessionId).ToList());

        public Task<bool> DeleteMaterialAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_materials.RemoveAll(m => m.Id == id) > 0);

        public Task<IReadOnlyList<WeakTopic>> GetTopicsAsync(long sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WeakTopic>>(_topics.Where(t => t.SessionId == sessionId).ToList());

        public Task SaveTopicAsync(WeakTopic topic, CancellationToken cancellationToken = default)
        {
            if (topic.Id == 0)
                _topics.Add(new WeakTopic { Id = Next(), SessionId = topic.SessionId, Topic = topic.Topic, Hits = topic.Hits, Misses = topic.Misses });
            return Task.CompletedTask;
        }
    }
}
