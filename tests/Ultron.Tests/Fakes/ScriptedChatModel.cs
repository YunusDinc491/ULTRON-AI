using System.Runtime.CompilerServices;
using Ultron.Core.Chat;
using Ultron.Core.Tools;

namespace Ultron.Tests.Fakes;

/// <summary>
/// Hangi iç işin istendiğini system prompt'tan anlayıp ona göre JSON döndüren sahte model.
/// Sıraya bağlı değildir: arka plandaki bilgi çıkarma ile router aynı anda çağırsa da doğru cevap gider.
/// </summary>
internal sealed class ScriptedChatModel : IChatModel
{
    public string RouterJson { get; set; } = """{"sinif":"Sohbet"}""";
    public string ExtractionJson { get; set; } = """{"bilgiler":[]}""";
    public string WebPlanJson { get; set; } = """{"sorgu":"dolar kuru","anahtar":"dolar tl kuru 2026-10-26","sure":"saat"}""";
    public string FollowUpJson { get; set; } = """{"sorular":[]}""";
    public string SectionNotes { get; set; } = "- Bölüm notu (s. 1)";
    public string ResearchPlanJson { get; set; } = """{"konu":"ısı pompaları","baslik":"Isı Pompaları","sorgular":["ısı pompası nedir","ısı pompası verimi cop"]}""";
    /// <summary>Null: model hata veriyormuş gibi (hazır şablona düşülür).</summary>
    public string? BriefingJson { get; set; } = """{"metin":"Günaydın Patron. Bugün 09:00'da Termodinamik dersin var, hava 17 derece."}""";
    /// <summary>Null: model hata veriyormuş gibi (hazır cümleye düşülür).</summary>
    public string? ReminderPhraseJson { get; set; } = """{"cumle":"Patron, hatırlatmamı istemiştin, su içmen gerekiyor."}""";
    /// <summary>Öneri motoru; null: model hata veriyormuş gibi.</summary>
    public string? SuggestionJson { get; set; } = """{"oneri_var":false,"metin":"","neden":""}""";
    public int SuggestionCalls { get; private set; }
    public string[] AnswerChunks { get; set; } = ["Mer", "haba."];
    public string GuestReminderJson { get; set; } = """{"hatirlatma":false,"metin":"","zaman":"","tekrar":"yok"}""";
    public string LibraryQueryJson { get; set; } = """{"sorgu":"useEffect runs twice"}""";
    public string LessonProblemJson { get; set; } = """{"verilenler":[{"ad":"T1","deger":"300","birim":"K"},{"ad":"r","deger":"8","birim":""},{"ad":"k","deger":"1,4","birim":""}],"adimlar":[{"aciklama":"İzantropik bağıntı","ad":"T2","ifade":"T1*r^((k-1)/k)","birim":"K"}],"sonuc":"T2","yorum":"Sıkıştırmada sıcaklık artar."}""";
    public Func<string>? LessonProblemJsonFactory { get; set; }
    public Func<string>? LessonQuizJsonFactory { get; set; }
    public string PhotoKindJson { get; set; } = """{"tur":"kahve_fali"}""";
    public string CupObservationJson { get; set; } = """{"genel":{"dip":"dipte koyu bir çöküntü","acikliklar":"ağza doğru beyaz bir su yolu","yogunluk":"orta"},"sekiller":[{"sekil":"kuş","bolge":"agiz","detay":"küçük, kanatları kulba doğru açık"},{"sekil":"zürafa","bolge":"orta","detay":"uzun boyunlu, belli belirsiz"}],"harf_rakam":["S"]}""";
    public string TarotPhotoJson { get; set; } = """{"kartlar":[{"ad":"The Tower","ters":false},{"ad":"Three of Cups","ters":true}]}""";
    public string BirthJson { get; set; } = """{"tarih":"2002-05-14","saat":"08:30","sehir":"İzmir"}""";
    public string LessonQuizJson { get; set; } = """{"sorular":[]}""";
    public string LessonGradeJson { get; set; } = """{"sonuclar":[]}""";
    public string LessonTopicsJson { get; set; } = """{"konular":["Birinci yasa","Entropi","Carnot çevrimi"]}""";
    public string LessonNoteJson { get; set; } = """{"kayit":false,"metin":""}""";

    public List<IReadOnlyList<ChatMessage>> StreamRequests { get; } = [];

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lock (StreamRequests)
            StreamRequests.Add(messages);
        foreach (var chunk in messages[0].Content.Contains("bir belgenin bir bölümünden") ? [SectionNotes] : AnswerChunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    /// <summary>Araçlı turlarda sırayla verilecek cevaplar; bitince "Tamam." (araçsız son cevap).</summary>
    public Queue<ToolTurn> ToolTurns { get; } = new();

    public List<(IReadOnlyList<ChatMessage> Messages, IReadOnlyList<ToolDefinition> Tools)> ToolRequests { get; } = [];

    public Task<ToolTurn> CompleteWithToolsAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
    {
        lock (ToolRequests)
            ToolRequests.Add((messages.ToList(), tools));
        return Task.FromResult(ToolTurns.TryDequeue(out var turn) ? turn : new ToolTurn("Tamam.", []));
    }

    private string Suggest()
    {
        SuggestionCalls++;
        return SuggestionJson ?? throw new InvalidOperationException("model yok");
    }

    public Task<string> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, string jsonSchema, CancellationToken cancellationToken = default)
    {
        var system = messages[0].Content;
        return Task.FromResult(
            system.Contains("tek bir sınıfa ayırmak") ? RouterJson :
            system.Contains("KALICI kişisel bilgileri çıkarmak") ? ExtractionJson :
            system.Contains("web araması hazırlamak") ? WebPlanJson :
            system.Contains("takip sorusu üret") ? FollowUpJson :
            system.Contains("araştırma isteği için bir plan") ? ResearchPlanJson :
            system.Contains("hatırlatmanın zamanı geldi") ? ReminderPhraseJson ?? throw new InvalidOperationException("model yok") :
            system.Contains("SON ÖNERİLER listesindekilerin") ? Suggest() :
            system.Contains("sabah brifingi veriyorsun") ? BriefingJson ?? throw new InvalidOperationException("model yok") :
            system.Contains("KISA bir İngilizce arama ifadesine") ? LibraryQueryJson :
            system.Contains("Mesaj bir hatırlatma isteğiyse") ? GuestReminderJson :
            system.Contains("SAYILARI SEN HESAPLAMAYACAKSIN") ? LessonProblemJsonFactory?.Invoke() ?? LessonProblemJson :
            system.Contains("KARIŞIK quiz") ? LessonQuizJsonFactory?.Invoke() ?? LessonQuizJson :
            system.Contains("quiz cevaplarını puanlıyorsun") ? LessonGradeJson :
            system.Contains("KONU listesi çıkar") ? LessonTopicsJson :
            system.Contains("aklında tutmanı istiyor") ? LessonNoteJson :
            system.Contains("Fotoğraf türünü belirle") ? PhotoKindJson :
            system.Contains("GÖZLEMCİSİN") ? CupObservationJson :
            system.Contains("tarot kartlarını soldan sağa") ? TarotPhotoJson :
            system.Contains("doğum tarihini (YYYY-MM-DD)") ? BirthJson :
            throw new InvalidOperationException("Beklenmeyen JSON isteği: " + system[..Math.Min(60, system.Length)]));
    }
}
