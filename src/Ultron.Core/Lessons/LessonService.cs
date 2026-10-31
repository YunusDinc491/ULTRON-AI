using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Calendar;
using Ultron.Core.Chat;
using Ultron.Core.Coding;
using Ultron.Core.Documents;
using Ultron.Core.Library;
using Ultron.Core.Reminders;
using Ultron.Core.Settings;
using Ultron.Core.Text;

namespace Ultron.Core.Lessons;

/// <summary>appsettings "Lessons" bölümü.</summary>
public sealed class LessonOptions
{
    public const string SectionName = "Lessons";

    /// <summary>Claude Code'un ders sorularını incelediği klasör.</summary>
    public string WorkFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "ders");

    /// <summary>Gece denetiminde en fazla kaç ders cevabı incelenir.</summary>
    public int MaxReviewPerNight { get; set; } = 30;

    /// <summary>Çalışma planı hatırlatmalarının saati.</summary>
    public int StudyHour { get; set; } = 20;
}

/// <summary>
/// Ders arayüzünün beyni (Faz 17): oturumun kaynaklarından soru-cevap (akarak), sayısal problemde adımları model yazar ve
/// sayıları <see cref="MathExpression"/> hesaplar; özet, formül kağıdı, karışık quiz (yanlışlar zayıf konu olur), sınava
/// göre çalışma planı ve hatırlatmaları. Emin olmadığında kullanıcının onayıyla Claude Code'a sorar; gece o günün cevaplarını
/// Claude denetler, kaynaklı düzeltmeler O DERSİN notlarına girer (ana hafızaya yazılmaz).
/// </summary>
public sealed partial class LessonService(
    ILessonStore store,
    IChatModel chat,
    DocumentService documents,
    ReminderService reminders,
    ICodeAgent agent,
    ILessonNotifier notifier,
    UltronSettings settings,
    IOptions<LessonOptions> options,
    TimeProvider time,
    ILogger<LessonService> logger,
    LibraryService? library = null,
    ICalendarStore? calendar = null,
    Growth.IGrowthStore? growth = null,
    Self.SelfRequests? self = null)
{
    public const string LastReviewKey = "ders_denetim_son";
    private const string EngineeringSource = "muhendislik";

    private readonly SemaphoreSlim _claudeGate = new(1, 1);
    private readonly SemaphoreSlim _reviewGate = new(1, 1);

    public bool ClaudeBusy => _claudeGate.CurrentCount == 0;

    // ---- Oturumlar ----

    public Task<IReadOnlyList<LessonSession>> SessionsAsync(CancellationToken cancellationToken = default) => store.GetSessionsAsync(cancellationToken);

    /// <summary>Ders programındaki dersler (oturumu olmayanlar listede "+ Ders" önerisi olur).</summary>
    public async Task<IReadOnlyList<string>> CoursesAsync(CancellationToken cancellationToken = default) =>
        calendar is null ? [] : (await calendar.GetAllAsync(cancellationToken)).Select(e => e.Title.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();

    public Task<LessonSession> CreateSessionAsync(string name, string? course, CancellationToken cancellationToken = default)
    {
        name = string.IsNullOrWhiteSpace(name) ? course ?? "Yeni ders" : name.Trim();
        return store.AddSessionAsync(new LessonSession
        {
            Name = Clip(name, 100), Course = string.IsNullOrWhiteSpace(course) ? null : Clip(course.Trim(), 100),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime, UpdatedAtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);
    }

    public async Task<bool> UpdateSessionAsync(long sessionId, string name, string instructions, CancellationToken cancellationToken = default)
    {
        if (await store.GetSessionAsync(sessionId, cancellationToken) is not { } session)
            return false;
        if (!string.IsNullOrWhiteSpace(name))
            session.Name = Clip(name.Trim(), 100);
        session.Instructions = Clip(instructions.Trim(), 2000);
        session.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await store.UpdateSessionAsync(session, cancellationToken);
        return true;
    }

    /// <summary>Oturumu ve kaynak belgelerini siler.</summary>
    public async Task<bool> DeleteSessionAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        foreach (var doc in await SourcesAsync(sessionId, cancellationToken))
            await documents.DeleteAsync(doc.Id, cancellationToken);
        return await store.DeleteSessionAsync(sessionId, cancellationToken);
    }

    public Task<LessonSession?> GetSessionAsync(long sessionId, CancellationToken cancellationToken = default) => store.GetSessionAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<LessonMessage>> MessagesAsync(long sessionId, CancellationToken cancellationToken = default) => store.GetMessagesAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<LessonNote>> NotesAsync(long sessionId, CancellationToken cancellationToken = default) => store.GetNotesAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<LessonMaterial>> MaterialsAsync(long sessionId, CancellationToken cancellationToken = default) => store.GetMaterialsAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<WeakTopic>> TopicsAsync(long sessionId, CancellationToken cancellationToken = default) => store.GetTopicsAsync(sessionId, cancellationToken);

    // ---- Kaynaklar ----

    public async Task<IReadOnlyList<Document>> SourcesAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var collection = LessonSession.CollectionOf(sessionId);
        return (await documents.ListAsync(cancellationToken)).Where(d => string.Equals(d.Collection, collection, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.AddedAtUtc).ToList();
    }

    /// <param name="exam">Geçmiş sınav soruları: quiz bunların üslubunu örnek alır.</param>
    public async Task<DocumentAddResult> AddSourceFileAsync(long sessionId, string fileName, byte[] content, bool exam, CancellationToken cancellationToken = default)
    {
        var result = await documents.AddFileAsync((exam ? LessonPrompts.ExamPrefix : "") + Path.GetFileName(fileName), content, LessonSession.CollectionOf(sessionId), cancellationToken);
        await TouchAsync(sessionId, cancellationToken);
        return result;
    }

    /// <summary>Kullanıcının düzeltip onayladığı fotoğraf metni (ya da elle yazılan not) kaynak olur.</summary>
    public async Task<DocumentAddResult> AddSourceTextAsync(long sessionId, string title, string text, bool exam, CancellationToken cancellationToken = default)
    {
        title = string.IsNullOrWhiteSpace(title) ? $"Defter {time.GetLocalNow():dd.MM HH-mm}" : title.Trim();
        var result = await documents.AddTextAsync((exam ? LessonPrompts.ExamPrefix : "") + title, text.Trim(), LessonSession.CollectionOf(sessionId), DocumentKind.Text, cancellationToken);
        await TouchAsync(sessionId, cancellationToken);
        return result;
    }

    public async Task<bool> RemoveSourceAsync(long sessionId, long documentId, CancellationToken cancellationToken = default) =>
        (await SourcesAsync(sessionId, cancellationToken)).Any(d => d.Id == documentId) && await documents.DeleteAsync(documentId, cancellationToken);

    /// <summary>Defter/tahta fotoğrafı → metin taslağı (kullanıcı düzeltip onaylayınca kaynak olur; kendiliğinden kaydedilmez).</summary>
    public async Task<string> ReadPhotoAsync(byte[] image, CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        await foreach (var part in chat.StreamAsync([
            new ChatMessage(Role.System, LessonPrompts.Photo),
            new ChatMessage(Role.User, "Bu fotoğraftaki yazıyı metne çevir.", Images: [image]),
        ], cancellationToken))
            text.Append(part);
        return text.ToString().Trim();
    }

    // ---- Oturum hafızası ----

    public async Task<LessonNote?> AddNoteAsync(long sessionId, string text, string source = "kullanıcı", CancellationToken cancellationToken = default)
    {
        text = text.Trim();
        if (text.Length < 3)
            return null;
        var existing = await store.GetNotesAsync(sessionId, cancellationToken);
        if (existing.FirstOrDefault(n => TurkishText.Normalize(n.Text) == TurkishText.Normalize(text)) is { } same)
            return same;
        return await store.AddNoteAsync(new LessonNote { SessionId = sessionId, Text = Clip(text, 900), Source = source, CreatedAtUtc = time.GetUtcNow().UtcDateTime }, cancellationToken);
    }

    public Task<bool> RemoveNoteAsync(long noteId, CancellationToken cancellationToken = default) => store.DeactivateNoteAsync(noteId, cancellationToken);

    private static readonly string[] NoteCues = ["not al", "notlara ekle", "aklinda tut", "unutma", "bunu hatirla", "hatirla:", "kaydet:"];

    internal static bool WantsNote(string text)
    {
        var n = " " + TurkishText.Normalize(text) + " ";
        return NoteCues.Any(c => n.Contains(c, StringComparison.Ordinal));
    }

    // ---- Soru-cevap ----

    /// <summary>Ders sorusu: kaynaklardan cevap akar; sayısal problemde adımlar kodla hesaplanır.</summary>
    public async IAsyncEnumerable<LessonEvent> AskAsync(long sessionId, string text, byte[]? image = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (await store.GetSessionAsync(sessionId, cancellationToken) is not { } session)
        {
            yield return new LessonFailed("Ders oturumu bulunamadı.");
            yield break;
        }
        text = text.Trim();
        var history = await store.GetMessagesAsync(sessionId, cancellationToken);
        await store.AddMessageAsync(new LessonMessage { SessionId = sessionId, FromUser = true, Content = text.Length > 0 ? text : "(görsel)", CreatedAtUtc = time.GetUtcNow().UtcDateTime }, cancellationToken);
        await TouchAsync(sessionId, cancellationToken);

        // "Formülleri büyüt": görünüm ayarı gerçekten değişir; yapamadığı istekse "yaptım" demez, geliştirme listesine yazar.
        if (image is null && self is not null
            && await self.HandleAsync(text, "ders", history.LastOrDefault(m => !m.FromUser)?.Content, cancellationToken) is { } selfReply)
        {
            yield return new LessonStarted([], false);
            yield return new LessonCompleted(await SaveAnswerAsync(sessionId, selfReply, [], "ultron", false, cancellationToken));
            yield break;
        }

        if (text.Length > 0 && WantsNote(text) && await TryNoteAsync(sessionId, text, cancellationToken) is { } noted)
        {
            yield return new LessonStarted([], false);
            yield return new LessonCompleted(await SaveAnswerAsync(sessionId, $"Not aldım (sadece bu ders için): {noted.Text}", [], "ultron", false, cancellationToken));
            yield break;
        }

        var notes = await store.GetNotesAsync(sessionId, cancellationToken);
        var query = text.Length > 0 ? text : session.Name;
        var docs = await documents.PrepareInCollectionAsync(query, session.Collection, cancellationToken);
        var libraryHits = await EngineeringNotesAsync(query, docs.Sources.Count > 0 ? 1 : 2, cancellationToken);
        var labels = docs.Sources.Select(s => s.Label).Concat(libraryHits.Select(h => "Ders notları · " + h.Page.Title)).Distinct().ToList();
        var noSource = docs.Sources.Count == 0 && libraryHits.Count == 0;

        if (image is null && IsProblem(text))
        {
            yield return new LessonStarted(labels, true);
            var solved = await SolveAsync(session, notes, text, LessonPrompts.SourceBlock(docs, libraryHits), cancellationToken);
            if (solved is not null)
            {
                yield return new LessonCompleted(await SaveAnswerAsync(sessionId, solved.Value.Text, labels, "hesap", !solved.Value.Ok, cancellationToken));
                yield break;
            }
            // Model adımları yazamadıysa düz anlatıma düşer.
        }
        else
            yield return new LessonStarted(labels, false);

        var system = new List<string> { LessonPrompts.Lesson(session, notes) };
        if (docs.Sources.Count > 0)
            system.Add(DocumentQuery.Note(docs));
        if (LessonPrompts.LibraryNote(libraryHits) is { } libNote)
            system.Add(libNote);
        var messages = new List<ChatMessage> { new(Role.System, SystemPrompt.Build(time.GetLocalNow().DateTime, null, system, AnswerTier.Medium)) };
        foreach (var m in history.TakeLast(8))
            messages.Add(new ChatMessage(m.FromUser ? Role.User : Role.Assistant, Clip(m.Content, 1500)));
        messages.Add(new ChatMessage(Role.User, text.Length > 0 ? text : "Bu görseldeki soruyu çöz ya da konuyu anlat.", Images: image is null ? null : [image]));

        var answer = new StringBuilder();
        var stream = chat.StreamAsync(messages, cancellationToken).GetAsyncEnumerator(cancellationToken);
        string? error = null;
        while (true)
        {
            try
            {
                if (!await stream.MoveNextAsync())
                    break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Ders modu: model cevabı yarıda kaldı");
                error = "Model cevabı yarıda kaldı: " + ex.Message;
                break;
            }
            answer.Append(stream.Current);
            yield return new LessonChunk(stream.Current);
        }
        await stream.DisposeAsync();

        var final = AnswerCleanup.DisplayFormulas(AnswerCleanup.Tidy(answer.ToString()));
        if (final.Length == 0)
        {
            yield return new LessonFailed(error ?? "Cevap üretilemedi.");
            yield break;
        }
        // Kaynakta bulunamayan ders sorusu: "Claude'a sorayım mı?" (selamlaşma gibi kısa mesajlarda değil).
        var unsure = noSource && text.Length >= 15 || final.Contains("kaynaklarda bulamad", StringComparison.OrdinalIgnoreCase);
        yield return new LessonCompleted(await SaveAnswerAsync(sessionId, final, labels, "ultron", unsure, cancellationToken));
    }

    private async Task<LessonMessage> SaveAnswerAsync(long sessionId, string text, IReadOnlyList<string> sources, string author, bool unsure, CancellationToken cancellationToken) =>
        await store.AddMessageAsync(new LessonMessage
        {
            SessionId = sessionId, FromUser = false, Content = text, Author = author, Unsure = unsure,
            Sources = sources.Count > 0 ? string.Join("\n", sources) : null, CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);

    private async Task<LessonNote?> TryNoteAsync(long sessionId, string text, CancellationToken cancellationToken)
    {
        try
        {
            var json = await chat.CompleteJsonAsync([new ChatMessage(Role.System, LessonPrompts.Note), new ChatMessage(Role.User, text)], LessonPrompts.NoteSchema, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("kayit", out var k) && k.ValueKind == JsonValueKind.True
                && doc.RootElement.TryGetProperty("metin", out var m) && m.GetString() is { Length: > 2 } note
                ? await AddNoteAsync(sessionId, note, "Konuşma", cancellationToken)
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Ders notu çıkarılamadı");
            return null;
        }
    }

    private async Task<IReadOnlyList<LibraryHit>> EngineeringNotesAsync(string query, int count, CancellationToken cancellationToken)
    {
        if (library is null || string.IsNullOrWhiteSpace(query))
            return [];
        try
        {
            return (await library.SearchAsync([query], 10, cancellationToken)).Where(h => h.Page.SourceId == EngineeringSource).Take(count).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Ders notları aranamadı");
            return [];
        }
    }

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex NumberPattern();

    private static readonly string[] ProblemCues = ["hesapla", "bul", "kac", "ne kadar", "nedir", "ne olur", "coz", "belirle", "elde edilir"];

    /// <summary>En az iki sayı + hesap isteyen bir ifade: adım adım çözüm (sayıları hesaplayıcı bulur).</summary>
    internal static bool IsProblem(string text)
    {
        if (NumberPattern().Matches(text).Count < 2)
            return false;
        var n = " " + TurkishText.Normalize(text) + " ";
        return ProblemCues.Any(c => n.Contains(c, StringComparison.Ordinal));
    }

    /// <summary>
    /// Quiz partisi ve adım adım çözüm için JSON sınırı. Ölçüm (2026-10-28): 2 quiz sorusu kaynaklı istemle ~550-1000 token;
    /// varsayılan 900'e takılınca quiz boş kalıyordu.
    /// </summary>
    private const int LongJsonTokens = 1800;

    private async Task<(string Text, bool Ok)?> SolveAsync(LessonSession session, IReadOnlyList<LessonNote> notes, string text, string sources, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(Role.System, LessonPrompts.Problem(LessonPrompts.Lesson(session, notes), sources)),
            new(Role.User, text),
        };
        (string Text, bool Ok)? result = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            string json;
            try
            {
                json = await chat.CompleteJsonAsync(messages, LessonPrompts.ProblemSchema, LongJsonTokens, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Ders modu: problem adımları alınamadı");
                return result;
            }
            var rendered = RenderSolution(json, out var missing);
            if (rendered is null)
                return result;
            result = rendered;
            if (rendered.Value.Ok || missing.Count == 0)
                break;
            // Ölçüm: model sorudaki T1, P1 gibi değerleri "verilenler"e yazmayı unutabiliyor; bir kez hatırlatılır.
            messages.Add(new ChatMessage(Role.Assistant, json));
            messages.Add(new ChatMessage(Role.User,
                $"Şu adlar hiçbir yerde tanımlı değil: {string.Join(", ", missing)}. Sorudaki değerleri \"verilenler\"e ekle (ör. T1 = 300), bir adımın sonucunu sonraki adımlarda aynı adla kullan ve JSON'u baştan yaz."));
        }
        return result;
    }

    internal static (string Text, bool Ok)? RenderSolution(string json) => RenderSolution(json, out _);

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex NamePattern();

    private static readonly HashSet<string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
        { "sqrt", "karekok", "ln", "log", "exp", "abs", "sin", "cos", "tan", "pi", "p", "e" };

    /// <summary>Modelin adımlarını hesaplayıp okunur çözüm yazar; bir adım hesaplanamazsa Ok=false (Claude'a sorulabilir).</summary>
    /// <param name="missing">Hesaplanamayan adımlardaki tanımsız adlar (yeniden deneme için).</param>
    internal static (string Text, bool Ok)? RenderSolution(string json, out IReadOnlyList<string> missing)
    {
        var undefined = new List<string>();
        missing = undefined;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (root?["adimlar"] is not JsonArray steps || steps.Count == 0)
            return null;

        var vars = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var units = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        var ok = true;

        if (root["verilenler"] is JsonArray given && given.Count > 0)
        {
            var parts = new List<string>();
            foreach (var g in given)
            {
                var name = Str(g?["ad"]);
                var unit = Str(g?["birim"]);
                if (!MathExpression.IsName(name) || !MathExpression.TryEvaluate(Str(g?["deger"]), vars, out var value))
                    continue;
                vars[name] = value;
                units[name] = unit;
                parts.Add($"{name} = {MathExpression.Format(value)}{(unit.Length > 0 ? " " + unit : "")}");
            }
            if (parts.Count > 0)
                sb.AppendLine("**Verilenler:** " + string.Join(", ", parts)).AppendLine();
        }

        sb.AppendLine("**Çözüm**");
        var number = 0;
        foreach (var s in steps)
        {
            var name = Str(s?["ad"]);
            var expression = Str(s?["ifade"]);
            var unit = Str(s?["birim"]);
            number++;
            sb.AppendLine($"{number}. {Str(s?["aciklama"])}");
            if (expression.Contains('=') && expression.Split('=') is [var left, var right] && MathExpression.IsName(left.Trim()))
            {
                name = name.Length > 0 ? name : left.Trim();
                expression = right; // "T2 = 300*…" yazdıysa
            }
            if (MathExpression.TryEvaluate(expression, vars, out var value))
            {
                if (MathExpression.IsName(name))
                {
                    vars[name] = value;
                    units[name] = unit;
                }
                sb.AppendLine($"   `{name} = {expression.Trim()}` → **{MathExpression.Format(value)}{(unit.Length > 0 ? " " + unit : "")}**");
            }
            else
            {
                ok = false;
                undefined.AddRange(NamePattern().Matches(expression).Select(m => m.Value)
                    .Where(n => !KnownNames.Contains(n) && !vars.ContainsKey(n) && !undefined.Contains(n, StringComparer.OrdinalIgnoreCase)).Distinct());
                sb.AppendLine($"   `{name} = {expression.Trim()}` → hesaplanamadı (ifade okunamadı ya da tanımsız bir ad var)");
            }
        }

        var result = Str(root["sonuc"]);
        if (vars.TryGetValue(result, out var final))
            sb.AppendLine().AppendLine($"**Sonuç:** {result} = **{MathExpression.Format(final)}{(units.TryGetValue(result, out var u) && u.Length > 0 ? " " + u : "")}**");
        else
            ok = false;
        if (Str(root["yorum"]) is { Length: > 0 } comment)
            sb.AppendLine().AppendLine(comment);
        sb.AppendLine().Append(ok
            ? "_Sayılar Ultron'un hesaplayıcısıyla hesaplandı; formülleri kontrol etmeyi unutma._"
            : "_Bir adım hesaplanamadı; istersen Claude'a sorayım._");
        return (sb.ToString(), ok);
    }

    private static string Str(JsonNode? node) =>
        FixLatex(node is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : node?.ToString().Trim() ?? "");

    /// <summary>
    /// Model JSON içinde LaTeX'i tek ters bölüyle yazınca "\text", "\frac", "\beta", "\rho" kaçış karakterine dönüşüyor
    /// (sekme, form feed, backspace, satır başı; ölçümde görüldü). Metinde bunların yeri yok: geri ters bölüye çevrilir.
    /// </summary>
    internal static string FixLatex(string s) =>
        s.IndexOfAny(['\t', '\f', '\b', '\r']) < 0 ? s : LatexEscape().Replace(s, m => "\\" + m.Value[0] switch { '\t' => 't', '\f' => 'f', '\b' => 'b', _ => 'r' });

    // Ardından harf gelen kaçış karakteri ("\r\n" satır sonu harf değil, dokunulmaz).
    [GeneratedRegex("[\t\f\b\r](?=[a-zA-Z])")]
    private static partial Regex LatexEscape();

    // ---- Özet ve formül kağıdı ----

    /// <param name="kind"><see cref="MaterialKinds.Summary"/> ya da <see cref="MaterialKinds.Formulas"/>.</param>
    public async Task<LessonMaterial?> MakeMaterialAsync(long sessionId, string kind, string? topic, CancellationToken cancellationToken = default)
    {
        if (await store.GetSessionAsync(sessionId, cancellationToken) is not { } session || kind is not (MaterialKinds.Summary or MaterialKinds.Formulas))
            return null;
        topic = string.IsNullOrWhiteSpace(topic) ? session.Name : topic.Trim();
        var sources = await MaterialSourcesAsync(session, kind == MaterialKinds.Formulas ? topic + " formül denklem" : topic, cancellationToken);
        var lesson = LessonPrompts.Lesson(session, await store.GetNotesAsync(sessionId, cancellationToken));
        var prompt = kind == MaterialKinds.Formulas ? LessonPrompts.Formulas(lesson, topic, sources) : LessonPrompts.Summary(lesson, topic, sources);
        var text = new StringBuilder();
        await foreach (var part in chat.StreamAsync([new ChatMessage(Role.System, prompt), new ChatMessage(Role.User, (kind == MaterialKinds.Formulas ? "Formül kağıdı: " : "Özet: ") + topic)], cancellationToken))
            text.Append(part);
        var content = AnswerCleanup.DisplayFormulas(AnswerCleanup.Tidy(text.ToString()));
        if (content.Length == 0)
            return null;
        var title = (kind == MaterialKinds.Formulas ? "Formül kağıdı · " : "Özet · ") + Clip(topic, 80);
        return await store.AddMaterialAsync(new LessonMaterial
        {
            SessionId = sessionId, Kind = kind, Title = title, Content = content,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime, UpdatedAtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);
    }

    /// <summary>Kullanıcı panelde düzenledi.</summary>
    public async Task<bool> SaveMaterialAsync(long materialId, string title, string content, CancellationToken cancellationToken = default)
    {
        if (await store.GetMaterialAsync(materialId, cancellationToken) is not { } material || material.Kind == MaterialKinds.Quiz)
            return false;
        if (!string.IsNullOrWhiteSpace(title))
            material.Title = Clip(title.Trim(), 190);
        material.Content = content;
        material.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await store.UpdateMaterialAsync(material, cancellationToken);
        return true;
    }

    public Task<bool> DeleteMaterialAsync(long materialId, CancellationToken cancellationToken = default) => store.DeleteMaterialAsync(materialId, cancellationToken);

    /// <summary>Malzeme için daha geniş kaynak: konu araması + (varsa) ders notları.</summary>
    private async Task<string> MaterialSourcesAsync(LessonSession session, string query, CancellationToken cancellationToken)
    {
        var docs = await documents.PrepareInCollectionAsync(query, session.Collection, cancellationToken);
        var lib = await EngineeringNotesAsync(query, docs.Sources.Count > 0 ? 1 : 3, cancellationToken);
        return LessonPrompts.SourceBlock(docs, lib);
    }

    // ---- Quiz ----

    public async Task<LessonMaterial?> MakeQuizAsync(long sessionId, string? topic, int count = 6, CancellationToken cancellationToken = default)
    {
        if (await store.GetSessionAsync(sessionId, cancellationToken) is not { } session)
            return null;
        count = Math.Clamp(count, 3, 10);
        var weak = (await store.GetTopicsAsync(sessionId, cancellationToken)).Where(t => t.IsWeak).Select(t => t.Topic).Take(5).ToList();
        topic = string.IsNullOrWhiteSpace(topic) ? (weak.Count > 0 ? "zayıf konular ve " + session.Name : session.Name) : topic.Trim();
        var sources = await MaterialSourcesAsync(session, topic + (weak.Count > 0 ? " " + string.Join(" ", weak) : ""), cancellationToken);
        var lesson = LessonPrompts.Lesson(session, await store.GetNotesAsync(sessionId, cancellationToken));
        // JSON cevabı uzunluk sınırına (900 token) 6 soru sığmıyor (ölçüm 2026-10-28): 2'şer soruluk partiler, türleri belli.
        var questions = new List<QuizQuestion>();
        // Ölçüm: bazı partiler bozuk soru verip eleniyordu (6 istenince 4 geliyordu); eksik türler bir tur daha istenir.
        var wanted = QuizTypes(count);
        for (var round = 0; round < 2 && questions.Count < count; round++)
        {
            var missing = wanted.ToList();
            foreach (var q in questions)
                missing.Remove(q.Type);
            foreach (var types in missing.Chunk(2))
            {
                try
                {
                    var json = await chat.CompleteJsonAsync([
                        new ChatMessage(Role.System, LessonPrompts.Quiz(lesson, topic, types, weak, questions.Select(q => q.Question).ToList(), sources)),
                        new ChatMessage(Role.User, $"{types.Length} soru: {topic}"),
                    ], LessonPrompts.QuizSchema, LongJsonTokens, cancellationToken);
                    // Sadece istenen türler alınır (model türü karıştırırsa fazlası eklenmez).
                    var need = types.ToList();
                    foreach (var q in ParseQuiz(json))
                        if (need.Remove(q.Type))
                            questions.Add(q);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Quiz partisi üretilemedi");
                }
            }
        }
        // Her sıraya istenen türden ilk soru: karışık düzen korunur (coktan, sayisal, coktan, acik…).
        var pool = questions.ToList();
        questions = [];
        foreach (var type in wanted)
            if (pool.FirstOrDefault(q => q.Type == type) is { } q)
            {
                questions.Add(q);
                pool.Remove(q);
            }
        if (questions.Count == 0)
            return null;
        return await store.AddMaterialAsync(new LessonMaterial
        {
            SessionId = sessionId, Kind = MaterialKinds.Quiz, Title = $"Quiz · {Clip(topic, 70)} · {questions.Count} soru",
            Content = JsonSerializer.Serialize(questions, QuizJson), CreatedAtUtc = time.GetUtcNow().UtcDateTime, UpdatedAtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);
    }

    /// <summary>Karışık quizin türleri: yarısı çoktan seçmeli, sayısal ve açık uçlu dönüşümlü (6 → coktan, sayisal, coktan, acik, coktan, sayisal).</summary>
    internal static IReadOnlyList<string> QuizTypes(int count) =>
        Enumerable.Range(0, count).Select(i => i % 2 == 0 ? "coktan" : (i / 2) % 2 == 0 ? "sayisal" : "acik").ToList();

    private static readonly JsonSerializerOptions QuizJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static IReadOnlyList<QuizQuestion> ReadQuiz(LessonMaterial material) =>
        material.Kind == MaterialKinds.Quiz ? JsonSerializer.Deserialize<List<QuizQuestion>>(material.Content, QuizJson) ?? [] : [];

    /// <summary>Modelin quiz JSON'u → geçerli sorular: sayısal sorunun cevabını hesaplayıcı bulur, bozuk soru atılır.</summary>
    internal static IReadOnlyList<QuizQuestion> ParseQuiz(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }
        if (root?["sorular"] is not JsonArray list)
            return [];
        var result = new List<QuizQuestion>();
        foreach (var q in list)
        {
            var type = Str(q?["tur"]);
            var question = Str(q?["soru"]);
            if (question.Length < 8)
                continue;
            var topic = Str(q?["konu"]) is { Length: > 0 } t ? Clip(t, 60) : "Genel";
            var explanation = Str(q?["aciklama"]);
            switch (type)
            {
                case "coktan":
                    var choices = (q?["secenekler"] as JsonArray)?.Select(c => ChoicePrefix().Replace(Str(c), "")).Where(c => c.Length > 0).ToList() ?? [];
                    var letter = Str(q?["dogru"]).ToUpperInvariant().TrimStart('(').FirstOrDefault();
                    if (choices.Count is < 3 or > 5 || letter < 'A' || letter - 'A' >= choices.Count)
                        continue;
                    result.Add(new QuizQuestion("coktan", question, choices, letter.ToString(), null, topic, explanation));
                    break;
                case "sayisal":
                    if (!MathExpression.TryEvaluate(Str(q?["cozum_ifadesi"]), out var value))
                        continue; // cevabı hesaplanamayan sayısal soru sorulmaz
                    var unit = Str(q?["birim"]);
                    result.Add(new QuizQuestion("sayisal", question, [], MathExpression.Format(value), unit.Length > 0 ? unit : null, topic,
                        (explanation + $" (Çözüm: {Str(q?["cozum_ifadesi"])})").Trim()));
                    break;
                case "acik":
                    if (Str(q?["dogru"]) is not { Length: > 2 } sample)
                        continue;
                    result.Add(new QuizQuestion("acik", question, [], sample, null, topic, explanation));
                    break;
            }
        }
        return result;
    }

    [GeneratedRegex(@"^\s*\(?[A-Ea-e][\)\.:\-]\s+")]
    private static partial Regex ChoicePrefix();

    /// <summary>Cevapları puanlar (çoktan ve sayısal kodla, açık uçlu modelle); konular zayıf konu listesine işlenir.</summary>
    public async Task<QuizGrade?> GradeQuizAsync(long materialId, IReadOnlyList<string> answers, CancellationToken cancellationToken = default)
    {
        if (await store.GetMaterialAsync(materialId, cancellationToken) is not { Kind: MaterialKinds.Quiz } material)
            return null;
        var questions = ReadQuiz(material);
        var results = new QuizResult?[questions.Count];
        var open = new List<int>();
        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var a = i < answers.Count ? answers[i]?.Trim() ?? "" : "";
            switch (q.Type)
            {
                case "coktan":
                    var right = a.Length > 0 && char.ToUpperInvariant(a[0]) == q.Answer[0];
                    results[i] = new QuizResult(i, right, right ? "Doğru. " + q.Explanation : $"Doğru cevap {q.Answer}) {q.Choices[q.Answer[0] - 'A']}. {q.Explanation}");
                    break;
                case "sayisal":
                    var correct = NumericMatch(a, q.Answer);
                    results[i] = new QuizResult(i, correct, (correct ? "Doğru. " : $"Doğru cevap ≈ {q.Answer}{(q.Unit is null ? "" : " " + q.Unit)}. ") + q.Explanation);
                    break;
                default:
                    if (a.Length == 0)
                        results[i] = new QuizResult(i, false, "Boş bıraktın. Örnek cevap: " + q.Answer);
                    else
                        open.Add(i);
                    break;
            }
        }
        if (open.Count > 0)
            await GradeOpenAsync(questions, answers, open, results, cancellationToken);

        var final = results.Select((r, i) => r ?? new QuizResult(i, false, "Puanlanamadı.")).ToList();
        var weak = await UpdateTopicsAsync(material.SessionId, questions, final, cancellationToken);
        if (growth is not null) // Gelişim panosu: quiz puanı gidişatı (Faz 18)
            await growth.AddQuizResultAsync(new Growth.QuizResult { SessionId = material.SessionId, Correct = final.Count(r => r.Correct), Total = final.Count, AtUtc = time.GetUtcNow().UtcDateTime }, cancellationToken);
        return new QuizGrade(final.Count(r => r.Correct), final.Count, final, weak);
    }

    /// <summary>"543", "543,4", "5,43e2", "300*8^(0,4/1,4)": %2 tolerans (yuvarlama farkı).</summary>
    internal static bool NumericMatch(string answer, string expected)
    {
        var cleaned = NumberWithUnit().Match(answer) is { Success: true } m ? m.Value : answer;
        return MathExpression.TryEvaluate(cleaned, out var a) && MathExpression.TryEvaluate(expected, out var e)
            && Math.Abs(a - e) <= Math.Max(Math.Abs(e) * 0.02, 1e-9);
    }

    [GeneratedRegex(@"^[\s\d.,+\-*/^()eE]+")]
    private static partial Regex NumberWithUnit();

    private async Task GradeOpenAsync(IReadOnlyList<QuizQuestion> questions, IReadOnlyList<string> answers, List<int> open, QuizResult?[] results, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        for (var k = 0; k < open.Count; k++)
        {
            var q = questions[open[k]];
            sb.AppendLine($"{k + 1}) Soru: {q.Question}\n   Örnek cevap: {q.Answer}\n   Öğrencinin cevabı: {answers[open[k]]}");
        }
        try
        {
            var json = await chat.CompleteJsonAsync([new ChatMessage(Role.System, LessonPrompts.Grade), new ChatMessage(Role.User, sb.ToString())], LessonPrompts.GradeSchema, cancellationToken);
            var list = JsonNode.Parse(json)?["sonuclar"] as JsonArray;
            for (var k = 0; k < open.Count; k++)
            {
                var node = list is not null && k < list.Count ? list[k] : null;
                var right = node?["dogru"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
                var feedback = Str(node?["geri_bildirim"]);
                var q = questions[open[k]];
                results[open[k]] = new QuizResult(open[k], right, (right ? "Doğru. " : "") + (feedback.Length > 0 ? feedback + " " : "") + (right ? "" : "Örnek cevap: " + q.Answer));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Açık uçlu cevaplar puanlanamadı");
            foreach (var i in open)
                results[i] = new QuizResult(i, false, "Puanlanamadı; örnek cevapla karşılaştır: " + questions[i].Answer);
        }
    }

    private async Task<IReadOnlyList<string>> UpdateTopicsAsync(long sessionId, IReadOnlyList<QuizQuestion> questions, IReadOnlyList<QuizResult> results, CancellationToken cancellationToken)
    {
        var topics = (await store.GetTopicsAsync(sessionId, cancellationToken)).ToList();
        foreach (var group in questions.Select((q, i) => (q.Topic, results[i].Correct)).GroupBy(x => TurkishText.Normalize(x.Topic)))
        {
            var topic = topics.FirstOrDefault(t => TurkishText.Normalize(t.Topic) == group.Key);
            if (topic is null)
            {
                topic = new WeakTopic { SessionId = sessionId, Topic = group.First().Topic };
                topics.Add(topic);
            }
            topic.Hits += group.Count(x => x.Correct);
            topic.Misses += group.Count(x => !x.Correct);
            topic.LastSeenUtc = time.GetUtcNow().UtcDateTime;
            await store.SaveTopicAsync(topic, cancellationToken);
        }
        return topics.Where(t => t.IsWeak).Select(t => t.Topic).ToList();
    }

    // ---- Çalışma planı ----

    /// <summary>Sınav tarihine göre kalan günlere konu dağıtımı (zayıf konular önde) ve isteğe bağlı günlük hatırlatma.</summary>
    public async Task<(LessonMaterial? Plan, string Message)> MakePlanAsync(long sessionId, string? examName, DateTime examDateLocal, bool addReminders, CancellationToken cancellationToken = default)
    {
        if (await store.GetSessionAsync(sessionId, cancellationToken) is not { } session)
            return (null, "Ders oturumu bulunamadı.");
        var today = time.GetLocalNow().Date;
        if (examDateLocal.Date < today)
            return (null, "Sınav tarihi geçmiş görünüyor.");

        var sources = await MaterialSourcesAsync(session, session.Name + " konu başlıkları içindekiler bölümler", cancellationToken);
        var topics = new List<string>();
        try
        {
            var json = await chat.CompleteJsonAsync([new ChatMessage(Role.System, LessonPrompts.Topics(session.Name, sources)), new ChatMessage(Role.User, "Konu listesi")],
                LessonPrompts.TopicsSchema, cancellationToken);
            topics = (JsonNode.Parse(json)?["konular"] as JsonArray)?.Select(Str).Where(t => t.Length > 1).Take(12).ToList() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Plan: konu listesi alınamadı");
        }
        var weak = (await store.GetTopicsAsync(sessionId, cancellationToken)).Where(t => t.IsWeak).Select(t => t.Topic).ToList();
        var classDays = calendar is not null && session.Course is { } course
            ? (await calendar.GetAllAsync(cancellationToken)).Where(e => string.Equals(e.Title.Trim(), course, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];

        var days = BuildPlan(today, examDateLocal.Date, weak, topics);
        var name = string.IsNullOrWhiteSpace(examName) ? "Sınav" : examName.Trim();
        var sb = new StringBuilder();
        sb.AppendLine($"## {session.Name} · {name} — {examDateLocal:d MMMM dddd}".Replace("  ", " "));
        sb.AppendLine($"Kalan gün: {(examDateLocal.Date - today).Days}. Zayıf konular önde{(weak.Count > 0 ? ": " + string.Join(", ", weak) : "")}.");
        if (classDays.Count > 0)
            sb.AppendLine("Ders saatleri: " + string.Join(", ", classDays.Select(Days.Describe)) + ".");
        sb.AppendLine();
        foreach (var (date, work) in days)
            sb.AppendLine($"- **{date.ToString("d MMMM ddd", Tr)}:** {work}");
        sb.AppendLine($"- **{examDateLocal.ToString("d MMMM ddd", Tr)}:** {name} — başarılar!");

        session.ExamName = Clip(name, 100);
        session.ExamAtUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(examDateLocal.Date.AddHours(9), DateTimeKind.Unspecified), time.LocalTimeZone);
        session.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await store.UpdateSessionAsync(session, cancellationToken);

        var existing = (await store.GetMaterialsAsync(sessionId, cancellationToken)).FirstOrDefault(m => m.Kind == MaterialKinds.Plan);
        LessonMaterial plan;
        if (existing is not null)
        {
            existing.Title = $"Plan · {name}";
            existing.Content = sb.ToString().TrimEnd();
            existing.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
            await store.UpdateMaterialAsync(existing, cancellationToken);
            plan = existing;
        }
        else
            plan = await store.AddMaterialAsync(new LessonMaterial
            {
                SessionId = sessionId, Kind = MaterialKinds.Plan, Title = $"Plan · {name}", Content = sb.ToString().TrimEnd(),
                CreatedAtUtc = time.GetUtcNow().UtcDateTime, UpdatedAtUtc = time.GetUtcNow().UtcDateTime,
            }, cancellationToken);

        var added = 0;
        if (addReminders)
        {
            var now = time.GetLocalNow().DateTime;
            foreach (var (date, work) in days.Take(30))
            {
                var due = date.AddHours(options.Value.StudyHour);
                if (due <= now)
                    continue;
                await reminders.AddAsync($"{session.Name} çalış: {Clip(work, 120)}", due, ReminderRecurrence.Yok, ReminderPriority.Onemli, cancellationToken: cancellationToken);
                added++;
            }
            var examMorning = examDateLocal.Date.AddHours(8);
            if (examMorning > now)
            {
                await reminders.AddAsync($"Bugün {session.Name} — {name}. Başarılar!", examMorning, ReminderRecurrence.Yok, ReminderPriority.Onemli, cancellationToken: cancellationToken);
                added++;
            }
        }
        return (plan, $"Plan hazır: {days.Count} çalışma günü" + (added > 0 ? $", {added} hatırlatma kuruldu (her gün {options.Value.StudyHour}:00)." : "."));
    }

    /// <summary>
    /// Bugünden sınavın bir gün öncesine kadar her güne konu dağıtır: önce zayıf konular, sonra dersin konuları; üç gün ve
    /// fazlasında son gün genel tekrar + quiz. Konu günden azsa kalan günler zayıf konuların tekrarı olur.
    /// </summary>
    internal static IReadOnlyList<(DateTime Date, string Work)> BuildPlan(DateTime today, DateTime exam, IReadOnlyList<string> weak, IReadOnlyList<string> topics)
    {
        var dayCount = (exam - today).Days;
        if (dayCount <= 0)
            return [];
        var ordered = weak.Concat(topics).DistinctBy(TurkishText.Normalize).ToList();
        if (ordered.Count == 0)
            ordered = ["Kaynakları baştan oku", "Formül kağıdını çıkar", "Örnek soru çöz"];
        var review = dayCount >= 3;
        var studyDays = review ? dayCount - 1 : dayCount;
        var plan = new List<(DateTime, string)>();
        var perDay = (int)Math.Ceiling(ordered.Count / (double)studyDays);
        for (var d = 0; d < studyDays; d++)
        {
            var chunk = ordered.Skip(d * perDay).Take(perDay).ToList();
            var work = chunk.Count > 0
                ? string.Join(", ", chunk) + " + kısa quiz"
                : "Tekrar: " + string.Join(", ", (weak.Count > 0 ? weak : ordered).Take(3)) + " + quiz";
            plan.Add((today.AddDays(d), work));
        }
        if (review)
            plan.Add((today.AddDays(dayCount - 1), "Genel tekrar: formül kağıdı + zayıf konulardan karışık quiz"));
        return plan;
    }

    // ---- Usta: Claude Code ----

    /// <summary>Kullanıcının "Claude'a sor" onayıyla: Claude Code soruyu dersin kaynaklarına bakarak (salt okuma) cevaplar.</summary>
    public async Task<string> AskClaudeAsync(long sessionId, long messageId, CancellationToken cancellationToken = default)
    {
        if (await store.GetSessionAsync(sessionId, cancellationToken) is not { } session)
            return "Ders oturumu bulunamadı.";
        var messages = await store.GetMessagesAsync(sessionId, cancellationToken);
        var index = messages.ToList().FindIndex(m => m.Id == messageId);
        var question = index > 0 ? messages.Take(index).LastOrDefault(m => m.FromUser) : messages.LastOrDefault(m => m.FromUser);
        if (question is null)
            return "Sorulacak soru bulunamadı.";
        var status = await agent.GetStatusAsync(cancellationToken);
        if (!status.LoggedIn)
            return status.Message;
        if (!await _claudeGate.WaitAsync(0, cancellationToken))
            return "Claude şu an başka bir ders sorusuna bakıyor.";
        var ultronAnswer = index >= 0 ? messages[index].Content : null;
        _ = Task.Run(async () =>
        {
            try
            {
                await RunClaudeAsync(session, question.Content, ultronAnswer, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Claude ders sorusu hata verdi");
                await notifier.ClaudeProgressAsync(sessionId, "Claude işi yarıda kaldı: " + ex.Message, true, null);
            }
            finally
            {
                _claudeGate.Release();
            }
        }, CancellationToken.None);
        return "Claude Code soruya bakıyor; cevabı birazdan sohbete gelecek.";
    }

    private async Task RunClaudeAsync(LessonSession session, string question, string? ultronAnswer, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(options.Value.WorkFolder, $"oturum-{session.Id}", time.GetLocalNow().ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var sources = await SourcesAsync(session.Id, cancellationToken);
        var notes = await store.GetNotesAsync(session.Id, cancellationToken);
        var brief = new StringBuilder()
            .AppendLine($"# {session.Name} — ders sorusu").AppendLine()
            .AppendLine("## Soru").AppendLine(question).AppendLine();
        if (ultronAnswer is not null)
            brief.AppendLine("## Ultron'un (küçük yerel model) cevabı").AppendLine(ultronAnswer).AppendLine();
        brief.AppendLine("## Dersin kaynak dosyaları");
        foreach (var s in sources)
            brief.AppendLine($"- {s.FileName}: {s.StoredPath}");
        if (sources.Count == 0)
            brief.AppendLine("- (kaynak yok)");
        brief.AppendLine().AppendLine(LessonPrompts.Lesson(session, notes));
        await File.WriteAllTextAsync(Path.Combine(folder, "SORU.md"), brief.ToString(), cancellationToken);

        var prompt = """
            Ultron'un Ders arayüzünde kullanıcı bir soru sordu; Ultron emin olamadı. SORU.md'de soru, Ultron'un cevabı, dersin notları ve kaynak dosyaların yolları var.
            Kaynak dosyalara bakarak soruyu doğru ve öğretici biçimde Türkçe cevapla: kısa açıklama, gerekiyorsa adım adım çözüm (hesapları dikkatle yap), önemli formüller kendi satırında $$...$$ LaTeX (öncesi ve sonrası boş satır), cümle içindeki semboller $...$. Kaynaktan aldığın yeri "dosya adı · s. N" diye belirt. Ultron'un cevabında hata varsa açıkça söyle.
            Dosya yazma, komut çalıştırma; sadece oku. Çıktın SADECE kullanıcıya gidecek cevap olsun.
            """;
        await notifier.ClaudeProgressAsync(session.Id, "Claude Code kaynaklara bakıyor…", false, null, cancellationToken);
        var result = await agent.RunAsync(
            new CodeRunRequest(folder, prompt)
            {
                AllowedTools = ["Read", "Glob", "Grep"], ReadOnly = true,
                ExtraFolders = sources.Select(s => Path.GetDirectoryName(s.StoredPath)).OfType<string>().Distinct().ToList(),
            },
            step => _ = notifier.ClaudeProgressAsync(session.Id, step, false, null, CancellationToken.None), cancellationToken);
        if (!result.Ok || string.IsNullOrWhiteSpace(result.Text))
        {
            await notifier.ClaudeProgressAsync(session.Id, "Claude cevap veremedi: " + Clip(result.Text, 200), true, null, cancellationToken);
            return;
        }
        var answer = result.Text.Trim();
        var message = await store.AddMessageAsync(new LessonMessage
        {
            SessionId = session.Id, FromUser = false, Content = answer, Author = "claude", Sources = sources.Count > 0 ? "Claude Code · dersin kaynakları" : "Claude Code",
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);
        // Bir dahakine Ultron kendisi bilsin: sadece bu dersin hafızasına.
        await AddNoteAsync(session.Id, $"Claude'un cevabı — soru: {Clip(question, 160)} → {Clip(answer, 600)}", "Claude", cancellationToken);
        await notifier.ClaudeProgressAsync(session.Id, "Claude Code cevapladı.", true, message, cancellationToken);
    }

    // ---- Gece denetimi ----

    /// <summary>Zamanlayıcı çağırır (04:00, diğer denetimlerle birlikte).</summary>
    public async Task<string?> TryReviewAsync(CancellationToken cancellationToken = default)
    {
        var localNow = time.GetLocalNow().DateTime;
        if (DateTime.TryParse(await settings.GetInternalAsync(LastReviewKey, cancellationToken), null, DateTimeStyles.RoundtripKind, out var last)
            && TimeZoneInfo.ConvertTimeFromUtc(last, time.LocalTimeZone) >= Learning.LearningService.LastSlot(localNow))
            return null;
        return await ReviewAsync(cancellationToken);
    }

    public async Task<string?> ReviewAsync(CancellationToken cancellationToken = default)
    {
        if (!await _reviewGate.WaitAsync(0, cancellationToken))
            return null;
        try
        {
            var pending = (await store.GetUnreviewedAsync(options.Value.MaxReviewPerNight, cancellationToken)).Where(m => m.Content.Length > 40).ToList();
            if (pending.Count == 0)
            {
                await MarkReviewedAsync(cancellationToken);
                return "İncelenecek ders cevabı yok.";
            }
            var status = await agent.GetStatusAsync(cancellationToken);
            if (!status.LoggedIn)
                return "Ders denetimi yapılamadı: " + status.Message;

            var items = new JsonArray();
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in pending.GroupBy(m => m.SessionId))
            {
                if (await store.GetSessionAsync(group.Key, cancellationToken) is not { } session)
                    continue;
                var all = await store.GetMessagesAsync(session.Id, cancellationToken);
                var files = await SourcesAsync(session.Id, cancellationToken);
                foreach (var f in files)
                    if (Path.GetDirectoryName(f.StoredPath) is { } dir)
                        folders.Add(dir);
                foreach (var m in group)
                {
                    var question = all.Where(x => x.Id < m.Id && x.FromUser).LastOrDefault()?.Content ?? "";
                    items.Add(new JsonObject
                    {
                        ["id"] = m.Id, ["ders"] = session.Name, ["soru"] = question, ["cevap"] = m.Content, ["kullanilan_kaynaklar"] = m.Sources,
                        ["ders_dosyalari"] = new JsonArray(files.Select(f => (JsonNode)JsonValue.Create(f.StoredPath)!).ToArray()),
                    });
                }
            }

            var folder = Path.Combine(options.Value.WorkFolder, "denetim", time.GetLocalNow().ToString("yyyyMMdd-HHmm"));
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "girdi.json"),
                new JsonObject { ["cevaplar"] = items }.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
                cancellationToken);
            var result = await agent.RunAsync(new CodeRunRequest(folder, ReviewPrompt(items.Count)) { AllowedTools = ["Read", "Glob", "Grep"], ReadOnly = true, ExtraFolders = folders.ToList() },
                step => logger.LogDebug("Ders denetimi: {Step}", step), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, "claude-cevabi.txt"), result.Text, cancellationToken);
            if (!result.Ok || ParseReview(result.Text) is not { } review)
                return "Ders denetimi bitti ama sonuç okunamadı (claude-cevabi.txt).";

            var learned = 0;
            foreach (var m in pending)
            {
                if (review.TryGetValue(m.Id, out var item) && item.Correction is { Length: > 5 } correction && item.Reference is { Length: > 2 } reference)
                {
                    // Kaynaklı düzeltme kendiliğinden öğrenilir (kullanıcının Faz 15 kararı); panelde "Geri al" var.
                    await AddNoteAsync(m.SessionId, $"Düzeltme ({reference}): {correction}", "Gece denetimi", cancellationToken);
                    learned++;
                }
                m.ReviewedAtUtc = time.GetUtcNow().UtcDateTime;
                await store.UpdateMessageAsync(m, cancellationToken);
            }
            await MarkReviewedAsync(cancellationToken);
            var report = $"Ders denetimi: {pending.Count} cevap incelendi, {learned} düzeltme ilgili dersin notlarına eklendi.";
            await File.WriteAllTextAsync(Path.Combine(folder, "rapor.md"), report, cancellationToken);
            return report;
        }
        finally
        {
            _reviewGate.Release();
        }
    }

    private Task MarkReviewedAsync(CancellationToken cancellationToken) =>
        settings.SetInternalAsync(LastReviewKey, time.GetUtcNow().UtcDateTime.ToString("O"), cancellationToken);

    internal static string ReviewPrompt(int count) => $$"""
        Sen Ultron'un ders denetçisisin. Ultron (küçük yerel model) bugün kullanıcının {{count}} ders sorusunu cevapladı: girdi.json (soru, cevap, kullanılan kaynaklar, dersin dosyaları).
        Her cevabı dersin dosyalarına bakarak denetle: kavram doğru mu, formül ve hesap doğru mu (hesapları kendin yap), kaynakla çelişiyor mu?
        - "dogru": sorun yok.
        - "duzeltilmeli": "duzeltme" alanına doğru bilgiyi kısa ve kendi başına anlaşılır yaz (bir dahaki sefere Ultron bu notu okuyacak); "kaynak" alanına dayandığın yeri yaz ("dosya adı · s. N" ya da standart ders kitabı bilgisi ise "genel: <kısa açıklama>").
        Dosya yazma, komut çalıştırma; sadece oku. Sonunda SADECE şu JSON'u yaz:
        {"cevaplar":[{"id":1,"karar":"dogru|duzeltilmeli","duzeltme":"","kaynak":""}],"rapor":"en fazla 2 cümle"}
        """;

    internal sealed record ReviewItem(string? Correction, string? Reference);

    internal static IReadOnlyDictionary<long, ReviewItem>? ParseReview(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        try
        {
            if (JsonNode.Parse(text[start..(end + 1)])?["cevaplar"] is not JsonArray list)
                return null;
            var items = new Dictionary<long, ReviewItem>();
            foreach (var node in list)
            {
                if (node?["id"] is not JsonValue idValue || !(idValue.TryGetValue<long>(out var id) || long.TryParse(idValue.ToString(), out id)))
                    continue;
                var fix = Str(node["karar"]).Contains("duzelt", StringComparison.OrdinalIgnoreCase);
                items[id] = fix ? new ReviewItem(Str(node["duzeltme"]), Str(node["kaynak"])) : new ReviewItem(null, null);
            }
            return items;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // ---- Yardımcılar ----

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private async Task TouchAsync(long sessionId, CancellationToken cancellationToken)
    {
        if (await store.GetSessionAsync(sessionId, cancellationToken) is { } session)
        {
            session.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
            await store.UpdateSessionAsync(session, cancellationToken);
        }
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
