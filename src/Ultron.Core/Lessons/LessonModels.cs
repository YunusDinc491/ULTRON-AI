namespace Ultron.Core.Lessons;

/// <summary>
/// Ders arayüzü (Faz 17): her dersin ayrı oturumu. Benzetme: her dersin kendi defteri ve dosyası var; Ultron o dersin
/// dosyasındaki kaynaklardan (PDF, slayt, defter fotoğrafı, geçmiş sınav) anlatır, sorar, plan yapar. Oturumun kendi notları
/// (hoca notasyonu, sınav kapsamı, düzeltmeler) sadece o derste kullanılır; ana hafızaya yazılmaz.
/// </summary>
public sealed class LessonSession
{
    public long Id { get; init; }

    public required string Name { get; set; }

    /// <summary>Ders programındaki dersin adı (bağlıysa).</summary>
    public string? Course { get; set; }

    /// <summary>Kullanıcının bu ders için talimatı ("hoca SI birim kullanıyor, kısa anlat").</summary>
    public string Instructions { get; set; } = "";

    public string? ExamName { get; set; }

    public DateTime? ExamAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Oturumun belge koleksiyonu: kaynaklar sadece buradan aranır.</summary>
    public string Collection => CollectionOf(Id);

    public static string CollectionOf(long sessionId) => "ders:" + sessionId;
}

/// <summary>Ders sohbetindeki bir mesaj.</summary>
public sealed class LessonMessage
{
    public long Id { get; init; }

    public long SessionId { get; init; }

    public bool FromUser { get; init; }

    public required string Content { get; init; }

    /// <summary>Cevapta kullanılan kaynaklar ("Termo.pdf · s. 12"), satır satır.</summary>
    public string? Sources { get; init; }

    /// <summary>"ultron", "hesap" (adımları kodla hesaplanmış çözüm) ya da "claude".</summary>
    public string Author { get; init; } = "ultron";

    /// <summary>Ultron emin değildi (kaynak bulunamadı ya da hesap tutmadı): "Claude'a sorayım mı?" kartı.</summary>
    public bool Unsure { get; init; }

    /// <summary>Gece denetiminde Claude'un incelediği an.</summary>
    public DateTime? ReviewedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Oturumun hafızası: sadece bu derste kullanılan bilgi.</summary>
public sealed class LessonNote
{
    public long Id { get; init; }

    public long SessionId { get; init; }

    public required string Text { get; init; }

    /// <summary>"kullanıcı", "Konuşma", "Claude" ya da "Gece denetimi".</summary>
    public required string Source { get; init; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Sağ paneldeki çalışma malzemesi.</summary>
public static class MaterialKinds
{
    public const string Summary = "ozet";
    public const string Formulas = "formul";
    public const string Quiz = "quiz";
    public const string Plan = "plan";
}

/// <summary>Özet, formül kağıdı, quiz (JSON) ya da çalışma planı: kalıcı, düzenlenebilir.</summary>
public sealed class LessonMaterial
{
    public long Id { get; init; }

    public long SessionId { get; init; }

    /// <summary><see cref="MaterialKinds"/>.</summary>
    public required string Kind { get; init; }

    public required string Title { get; set; }

    public required string Content { get; set; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Quizde yanlış yapılan konu: plan ve yeni quiz bunları öne alır.</summary>
public sealed class WeakTopic
{
    public long Id { get; init; }

    public long SessionId { get; init; }

    public required string Topic { get; init; }

    public int Misses { get; set; }

    public int Hits { get; set; }

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Yanlış doğrudan fazlaysa hâlâ zayıf.</summary>
    public bool IsWeak => Misses > Hits;
}

public interface ILessonStore
{
    Task<LessonSession> AddSessionAsync(LessonSession session, CancellationToken cancellationToken = default);

    Task UpdateSessionAsync(LessonSession session, CancellationToken cancellationToken = default);

    Task<LessonSession?> GetSessionAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LessonSession>> GetSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Oturumu mesajları, notları, malzemeleri ve zayıf konularıyla siler (belgeleri ayrıca silinir).</summary>
    Task<bool> DeleteSessionAsync(long id, CancellationToken cancellationToken = default);

    Task<LessonMessage> AddMessageAsync(LessonMessage message, CancellationToken cancellationToken = default);

    Task UpdateMessageAsync(LessonMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LessonMessage>> GetMessagesAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<LessonMessage?> GetMessageAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Gece denetimi: incelenmemiş, Ultron'un yazdığı cevaplar.</summary>
    Task<IReadOnlyList<LessonMessage>> GetUnreviewedAsync(int max, CancellationToken cancellationToken = default);

    Task<LessonNote> AddNoteAsync(LessonNote note, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LessonNote>> GetNotesAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<bool> DeactivateNoteAsync(long id, CancellationToken cancellationToken = default);

    Task<LessonMaterial> AddMaterialAsync(LessonMaterial material, CancellationToken cancellationToken = default);

    Task UpdateMaterialAsync(LessonMaterial material, CancellationToken cancellationToken = default);

    Task<LessonMaterial?> GetMaterialAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LessonMaterial>> GetMaterialsAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<bool> DeleteMaterialAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WeakTopic>> GetTopicsAsync(long sessionId, CancellationToken cancellationToken = default);

    Task SaveTopicAsync(WeakTopic topic, CancellationToken cancellationToken = default);
}

/// <summary>Ders arayüzüne haber: Claude'un cevabı ve gece denetiminin sonucu.</summary>
public interface ILessonNotifier
{
    /// <param name="message">İş bittiyse Claude'un cevabı; aksi halde null (ilerleme ya da hata).</param>
    Task ClaudeProgressAsync(long sessionId, string status, bool finished, LessonMessage? message, CancellationToken cancellationToken = default);
}

public sealed class LogLessonNotifier : ILessonNotifier
{
    public Task ClaudeProgressAsync(long sessionId, string status, bool finished, LessonMessage? message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Soru-cevap akışının olayları (hub üzerinden Ders arayüzüne akar).</summary>
public abstract record LessonEvent;

/// <param name="Sources">Bağlama giren kaynaklar ("Termo.pdf · s. 12").</param>
/// <param name="Solving">Sayısal problem: adımlar kodla hesaplanıyor (akış yok, sonuç bir kerede gelir).</param>
public sealed record LessonStarted(IReadOnlyList<string> Sources, bool Solving) : LessonEvent;

public sealed record LessonChunk(string Text) : LessonEvent;

public sealed record LessonCompleted(LessonMessage Message) : LessonEvent;

public sealed record LessonFailed(string Message) : LessonEvent;

/// <summary>Quiz sorusu (malzeme içeriğinde JSON olarak saklanır).</summary>
/// <param name="Type">"coktan", "sayisal" ya da "acik".</param>
/// <param name="Answer">Çoktan seçmelide doğru şıkkın harfi, sayısalda kodla hesaplanan sonuç, açık uçluda örnek cevap.</param>
public sealed record QuizQuestion(string Type, string Question, IReadOnlyList<string> Choices, string Answer, string? Unit, string Topic, string Explanation);

public sealed record QuizResult(int Index, bool Correct, string Feedback);

public sealed record QuizGrade(int Correct, int Total, IReadOnlyList<QuizResult> Results, IReadOnlyList<string> WeakTopics);
