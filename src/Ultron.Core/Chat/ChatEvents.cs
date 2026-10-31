using Ultron.Core.Memories;
using Ultron.Core.Routing;
using Ultron.Core.Tools;
using Ultron.Core.Web;

namespace Ultron.Core.Chat;

/// <summary>
/// Bir mesaja cevap verilirken olanlar, sırasıyla. Arayüz (terminal, masaüstü, Telegram) bunları
/// istediği gibi gösterir: metin parçalarını ekrana akıtır, kaynakları kart olarak çizer, tanılamayı gizler…
/// </summary>
public abstract record ChatEvent;

/// <param name="Detailed">Kullanıcı ayrıntı istedi ("detaylı yaz", "madde madde", "5 madde"): sesli okunmaz, ekrana yazılır (2026-10-27).</param>
public sealed record IntentDetected(MessageIntent Intent, bool Detailed = false) : ChatEvent;

public sealed record MemoriesRecalled(MemoryRecall Recall) : ChatEvent;

/// <summary>Güncel soru için web bağlamı hazır (önbellekten ya da aramadan).</summary>
public sealed record WebPrepared(WebContext Context) : ChatEvent;

/// <summary>Web'e ulaşılamadı; cevap "erişimim yok" notuyla üretilecek.</summary>
public sealed record WebUnavailable(string Reason) : ChatEvent;

public sealed record AnswerChunk(string Text) : ChatEvent;

/// <summary>Cevap verilirken araç(lar) çalıştı (ör. hatırlatıcı kuruldu).</summary>
public sealed record ToolsUsed(IReadOnlyList<ToolUse> Uses) : ChatEvent;

/// <summary>Cevapta kullanılan web kaynakları ([1], [2]… numaralarıyla).</summary>
public sealed record SourcesListed(IReadOnlyList<WebSource> Sources) : ChatEvent;

public sealed record AnswerCompleted(long MessageId, string Text) : ChatEvent;

/// <summary>Cevabı kişiselleştirmek için tek tıkla cevaplanabilecek sorular (cevaptan sonra gelir).</summary>
public sealed record FollowUpsSuggested(IReadOnlyList<FollowUpQuestion> Questions) : ChatEvent;

/// <summary>Soru için kullanıcının belgelerinden parça arandı (bulunan parça sayısı 0 olabilir).</summary>
public sealed record DocumentsSearched(Ultron.Core.Documents.DocumentContext Context) : ChatEvent;

/// <summary>Yazılım sorusu için kütüphaneden bölüm bulundu (Faz 15 b).</summary>
public sealed record LibrarySearched(int Count) : ChatEvent;

/// <summary>Cevapta kullanılan belge parçaları ([1], [2]… numaralarıyla; dosya + sayfa).</summary>
public sealed record DocumentSourcesListed(IReadOnlyList<Ultron.Core.Documents.DocumentSource> Sources) : ChatEvent;

/// <summary>Araştırma modunda bir adım ("3 arama yapılıyor", "6 kaynak okunuyor").</summary>
public sealed record ResearchProgress(string Step) : ChatEvent;

/// <summary>Cevap paketi ilerlemesi (Faz 19: Medium derin web, PRO Claude araştırması): sesli okumayı kesmez, durum satırına yazılır.</summary>
/// <param name="Tier">"Ultron Medium", "ULTRON PRO" ya da "PRO hafızası".</param>
public sealed record TierProgress(string Tier, string Step) : ChatEvent;

/// <summary>Araştırma raporu bilgi tabanına kaydedildi (kaydedilemediyse null).</summary>
public sealed record ResearchSaved(Ultron.Core.Documents.Document? Document) : ChatEvent;

/// <summary>Soruya görsel eklendi (<paramref name="FromScreen"/>: "ekrana bak" ekran görüntüsü); cevap görsele bakılarak verilecek.</summary>
public sealed record ImageAttached(bool FromScreen) : ChatEvent;
