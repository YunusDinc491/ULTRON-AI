using Ultron.Core.Text;

namespace Ultron.Core.Chat;

/// <summary>
/// Cevabın uzunluğu: varsayılan kısa ve sohbet havasında; kullanıcı açıkça "detaylı anlat", "madde madde listele"
/// gibi bir şey derse ayrıntılı. Karar kodla verilir (modele bırakılınca her cevabı uzun uzun yazıyordu).
/// </summary>
public static class AnswerStyle
{
    // Normalleştirilmiş (TurkishText.Normalize) ifadeler; kelime başında eşleşir: "detayli" → "detaylıca", "detaylandır".
    private static readonly string[] DetailPhrases =
    [
        "detay", "ayrinti", "madde madde", "maddeler halinde", "maddele", "listele", "liste halinde", "liste yap", "liste yaz",
        "uzun uzun", "uzunca", "adim adim", "derinlemesine", "kapsamli", "enine boyuna", "tablo halinde", "tablo yap","her seyiyle", "tum yonleriyle",
    ];

    public static bool IsDetailRequested(string userText)
    {
        var text = " " + TurkishText.Normalize(userText);
        return DetailPhrases.Any(p => text.Contains(" " + p, StringComparison.Ordinal));
    }

    /// <summary>Kullanıcı ayrıntı istediğinde system prompt'a eklenen not.</summary>
    public const string DetailNote =
        "Kullanıcı bu sefer AYRINTILI cevap istedi: İLK cümle konunun tek cümlelik, konuşur gibi özeti olsun (sesli okunan kısım sadece bu); " +
        "sonra konuyu kapsamlı ama DERLİ TOPLU anlat: kısa başlıklar, kısa maddeler (her madde en fazla 1-2 satır), gerekiyorsa numaralı adımlar ve tablo. " +
        "Paragraf paragraf, deneme ya da tez gibi yazma.";

    /// <summary>"tam 4 madde", "5 adımda", "3 maddede" → 4, 5, 3.</summary>
    public static int? RequestedCount(string userText)
    {
        var m = System.Text.RegularExpressions.Regex.Match(TurkishText.Normalize(userText), @"\b(\d{1,2}) (madde|adim|nokta|oneri|ornek|sebep|neden|yol)");
        return m.Success && int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) is var n and >= 1 and <= 20 ? n : null;
    }

    /// <summary>
    /// Bu mesaj için cevap biçimi notları. Sayı verildiyse ayrıntı notu EKLENMEZ: "kapsamlı anlat" notu sayının önüne geçip
    /// "tam 4 madde" isteğine 16 satır yazdırıyordu (değerlendirme, Deney 5).
    /// </summary>
    public static IReadOnlyList<string> NotesFor(string userText)
    {
        List<string> notes = RequestedCount(userText) is { } n
            ? [$"Kullanıcı TAM {n} madde istedi: bu sefer NUMARALI LİSTE kullan (1. 2. 3. …), tam {n} madde yaz, her madde tek satır olsun; alt madde, başlık ve fazladan madde ekleme."]
            : IsDetailRequested(userText) ? [DetailNote]
            : NeedsReasoning(userText) && !NeedsArithmetic(userText) ? [ReasoningNote] : []; // hesapta "ilk cümle cevap" kuralı sonucu adımlardan önce tahmin ettiriyordu (tur 9: 1140 sn, sonra 167 sn)
        if (IsFormulaQuestion(userText))
            notes.Add(MathNote);
        if (IsAboutUltron(userText))
            notes.Add(CapabilitiesNote);
        if (IsWritingRequest(userText))
            notes.Add(WritingNote);
        if (NeedsArithmetic(userText))
            notes.Add(ArithmeticNote);
        // Biçim isteği ("tam 3 madde", "sadece sayıyı yaz") öncelikli: o zaman mühendislik notu eklenmez.
        if (Library.LibraryAnswer.MentionsEngineering(userText) && RequestedCount(userText) is null
            && !BriefPhrases.Any(p => (" " + TurkishText.Normalize(userText)).Contains(" " + p, StringComparison.Ordinal)))
            notes.Add(EngineeringNote);
        return notes;
    }

    /// <summary>Ders sorusu (termodinamik, akışkanlar, ısı transferi): tanım denklemiyle, birim ve varsayımlarıyla.</summary>
    public const string EngineeringNote =
        "Bu bir mühendislik (ders) sorusu. Tanımı DENKLEMİYLE ver (semboller ve birimlerle, satır içinde; ör. h = u + Pv, kJ/kg). " +
        "Sadece \"X nedir?\" diye sorulduysa 1-3 kısa cümle yeter, madde işareti kullanma. Sayısal soruda adım adım hesapla; varsayım gerekiyorsa yaz. " +
        "Kütüphanedeki ders notları verildiyse onlara dayan ve [1] gibi numarasını yaz. Emin olmadığın sayı ya da formülü uydurma.";

    // Sayılı soru problemi ("her saat 3 dk geri kalıyor… kaçı gösterir?", "20 kafa 56 bacak… kaç koyun?"): model kafadan hesapta
    // yanılıyordu (tur3 t04, tur5 e24). Adımları yazdırınca küçük model belirgin daha doğru sayıyor.
    private static readonly string[] AmountQuestions = ["kac", "kaci", "kacta", "ne kadar", "kacini", "kac tane"];

    public static bool NeedsArithmetic(string userText)
    {
        var text = " " + TurkishText.Normalize(userText) + " ";
        var numbers = System.Text.RegularExpressions.Regex.Matches(text, @"\d+").Count;
        var engineeringAmount = (numbers >= 1 || text.Contains(" hesapla", StringComparison.Ordinal)) && Library.LibraryAnswer.MentionsEngineering(userText);
        var percentChange = numbers >= 2 && (userText.Contains('%') || text.Contains(" yuzde", StringComparison.Ordinal)) && text.Contains(" ne olur", StringComparison.Ordinal);
        return (numbers >= 2 || engineeringAmount) && (percentChange || AmountQuestions.Any(q => text.Contains(" " + q + " ", StringComparison.Ordinal)))
            && !BriefPhrases.Any(p => text.Contains(" " + p, StringComparison.Ordinal));
    }

    public const string ArithmeticNote =
        "Bu bir hesap sorusu. Önce verilenleri ve işlemleri kısa adımlarla tek tek yaz (ör. \"18:00 − 12:00 = 6 saat; 6 × 3 dk = 18 dk geri\"), " +
        "her adımı kontrol et, sonucu EN SONDA tek cümleyle ver; adımlardan önce sonuç yazma. Kafadan tahmin etme. " +
        "Dikkat: dakikayı saate çevirirken 60'a böl (45 dk = 0,75 saat, 30 dk = 0,5 saat); art arda yüzde değişimleri toplanmaz, çarpılır " +
        "(%10 artış sonra %10 azalış = 1,10 × 0,90 = 0,99); birimleri her adımda yaz.";

    // "Hocama e-posta yaz", "şu cümleyi kibar yaz", "özür mesajı yaz": gece sınavında model metni yazmak yerine nasıl yazılacağını anlattı.
    private static readonly string[] WritingThings = ["e posta", "eposta", "mail", "mesaj", "dilekce", "metin", "cumle", "paragraf", "yazi", "not", "davet", "tebrik", "ozur", "cv", "on yazi", "aciklama"];
    private static readonly string[] WritingVerbs = ["yaz", "yazar misin", "yazsana", "yazabilir", "hazirla", "duzelt", "yeniden yaz", "kibar", "resmi", "toparla", "tasla"];

    public static bool IsWritingRequest(string userText)
    {
        var text = " " + TurkishText.Normalize(userText) + " ";
        return WritingThings.Any(t => text.Contains(" " + t, StringComparison.Ordinal)) && WritingVerbs.Any(v => text.Contains(" " + v, StringComparison.Ordinal))
            && !text.Contains(" kod", StringComparison.Ordinal);
    }

    public const string WritingNote =
        "Kullanıcı senden bir METİN yazmanı istiyor (e-posta, mesaj, cümle…). Metnin KENDİSİNİ yaz, nasıl yazılacağını anlatma. " +
        "E-postada konu satırı, hitap, 2-4 cümlelik gövde ve kapanış olsun; bilmediğin ad ve tarih yerine [Ders adı] gibi yer tutucu koy. " +
        "Tek cümle istendiyse sadece o cümleyi yaz. Metinden önce ya da sonra açıklama ekleme.";

    // "Neler yapabiliyorsun?" sorusuna model "metin üretebilirim" diyordu (gece sınavı t05): gerçek yetenekleri bilmiyor.
    // Sadece "sen"e yönelik biçimler: "bu hatayla ne yapabilirim?" yakalanmasın.
    private static readonly string[] AboutPhrases =
    [
        "neler yapabiliyorsun", "ne yapabiliyorsun", "neler yapabilirsin", "ne yapabilirsin", "yeteneklerin", "ozelliklerin",
        "kimsin", "sen nesin", "neye yararsin", "ne ise yararsin", "neler biliyorsun",
    ];

    public static bool IsAboutUltron(string userText)
    {
        var text = " " + TurkishText.Normalize(userText);
        return AboutPhrases.Any(p => text.Contains(" " + p, StringComparison.Ordinal));
    }

    /// <summary>
    /// "Neler yapabiliyorsun?" sabit cevabı: not verildiğinde bile model tek cümleyle "sohbet edebilirim" diyordu (tur3-b). Kendini
    /// tanıtması her seferinde aynı ve doğru olmalı; model çağrılmaz.
    /// </summary>
    public const string CapabilitiesAnswer =
        "Seninle sohbet edip sorularını cevaplıyorum ve anlattıklarını hafızamda tutuyorum (PATRON, İŞ, OKUL, BİLGİ çekmeceleri). " +
        "Hatırlatma kurar, hava durumuna bakar, hesap yapar, web'de arayıp kaynaklı araştırma raporu hazırlarım; yüklediğin PDF ve Word notlarından sayfa göstererek cevap veririm. " +
        "Yazılım sorularında web, mobil ve .NET'in resmi dokümantasyonundan oluşan kütüphaneme bakarım, büyük kodlama işlerini senin onayınla Claude Code'a devrederim. " +
        "Ayrıca bilgisayarında uygulama açıp dosya işleri yapabilirim, sabah brifingi veririm, Telegram'dan da ulaşırsın ve \"Ultron\" deyince seni dinlerim.";

    /// <summary>Ultron'un gerçekte yapabildikleri (uydurma yetenek söylemesin).</summary>
    public const string CapabilitiesNote =
        "Kullanıcı senin ne yapabildiğini soruyor. Gerçek yeteneklerin şunlar (başka şey uydurma): sohbet ve soru-cevap; kullanıcıyı hatırlayan kalıcı hafıza " +
        "(PATRON, İŞ, OKUL, BİLGİ çekmeceleri); hatırlatıcı; hava durumu; hesaplama; web araması ve kaynaklı araştırma raporu; yüklenen PDF/Word notlarından " +
        "sayfa göstererek cevap; görsel ve ekran görüntüsü anlama; yazılım soruları için 14 kaynaklı resmi dokümantasyon kütüphanesi (web, mobil, .NET); " +
        "bilgisayarda uygulama açma, dosya ve komut işleri (onaylı); büyük kodlama işlerini Claude Code'a devretme; sabah brifingi ve ders programı; " +
        "Telegram'dan erişim; \"Ultron\" deyince dinleme ve sesli cevap. 2-3 cümleyle, en işe yarar olanları öne alarak özetle.";

    // Karşılaştırma, nasıl, neden, hata, kod soruları (normalleştirilmiş, kelime başı). Gece sınavı (2026-10-28): bu sorularda
    // 1-2 cümlelik varsayılan cevap yüzeysel kalıyordu (sebep-sonuç zinciri, örnek, kod yok). Kural genel talimata konunca
    // "Entropi nedir?" gibi tanım sorularında da listeye kaydı (eval tal-05, anl-02); bu yüzden kodla, sadece bu sorulara.
    private static readonly string[] ReasoningPhrases =
    [
        "fark", "karsilastir", "hangisi", "hangisini", "mi yoksa", "nasil yap", "nasil kur", "nasil duzelt", "nasil coz", "nasil onle",
        "nasil bul", "nasil olustur", "nasil kullan", "nasil calis", "nasil yakala", "nasil saklan", "nasil dogrula", "neden", "niye", "sebeb", "sebep",
        "sonuc", "hata", "error", "exception", "calismiyor", "yavas", "ne yazdirir", "odunlesim", "avantaj", "dezavantaj", "ne zaman kullan",
        "ne ise yarar", "nereden baslay", "teshis", "problem", "cozer", "getirir", "risk", "tehlike", "karsi ornek", "dogru mu", "plan ver",
        "plan yap", "plani", "gun gun", "hafta hafta", "ay ay", "ne calismaliyim", "ne ogrenmeliyim", "nasil ogren", "secmeliyim", "secim",
    ];

    /// <summary>"Sadece sayıyı yaz", "tek cümleyle" gibi biçim istekleri her zaman öndedir: o zaman not eklenmez.</summary>
    private static readonly string[] BriefPhrases = ["sadece", "tek cumle", "tek kelime", "kisaca", "kisa bir", "bir cumle", "evet ya da hayir", "evet veya hayir"];

    public static bool NeedsReasoning(string userText)
    {
        var text = " " + TurkishText.Normalize(userText);
        return ReasoningPhrases.Any(p => text.Contains(" " + p, StringComparison.Ordinal))
            && !BriefPhrases.Any(p => text.Contains(" " + p, StringComparison.Ordinal));
    }

    /// <summary>Claude'a benzer cevap biçimi: önce doğrudan cevap, sonra sadece gereken kadar yapı.</summary>
    public const string ReasoningNote =
        "Bu bir karşılaştırma / nasıl / neden / hata / kod sorusu. İLK cümlen sorunun doğrudan cevabı olsun (sesli okunan kısım sadece bu). " +
        "Sonra en önemli 2-5 noktayı kısa maddelerle ver: farkta iki tarafı ve hangisinin ne zaman seçileceğini; nasılda numaralı adımları; " +
        "nedende sebep → sonuç zincirini (\"X olduğu için Y olur, bu da Z'ye yol açar\"); hatada en olası sebebi ve çözümü. " +
        "Seçim sorusunda (\"hangisini seçmeliyim\", \"X mi Y mi\") sonunda TEK net öneri ver (\"Senin durumunda X'i seç, çünkü …\"); " +
        "\"ihtiyaca bağlı\" deyip bırakma. Plan istendiyse gün gün / adım adım somut plan ver. Kod gerekiyorsa ```dil bloğunda kısa ve çalışır örnek yaz. " +
        "Kullanıcıya geri soru SORMA: eksik bilgi varsa makul bir varsayım yap, onu bir cümleyle belirt ve cevabı ver. " +
        "Emin olmadığın ayrıntıyı uydurma. Toplam 12 satırı geçme, giriş ve kapanış cümlesi yazma.";

    // Formül/denklem soruları: "formülü", "denklemi", "nasıl hesaplanır", "türet", "ispatla", "bağıntı".
    private static readonly string[] FormulaPhrases = ["formul", "denklem", "nasil hesaplanir", "turet", "ispat", "bagint", "esitlig"];

    public static bool IsFormulaQuestion(string userText)
    {
        var text = " " + TurkishText.Normalize(userText);
        return FormulaPhrases.Any(p => text.Contains(" " + p, StringComparison.Ordinal));
    }

    /// <summary>
    /// Sadece formül sorularında: denklemleri LaTeX yaz (masaüstü KaTeX ile matematik yazı tipinde çizer). Genel talimata
    /// konunca "sadece sayıyı yaz" sorularında da sayıyı $ içine alıp hesabı şaşırıyordu (değerlendirme: tutarlılık 2/2 → 0/2).
    /// </summary>
    public const string MathNote =
        "Önemli formülü ve denklemi KENDİ SATIRINDA, öncesinde ve sonrasında boş satır bırakarak $$...$$ içinde LaTeX ile yaz (ekranda ortalı ve büyük gösterilir; ör. $$\eta = 1 - \frac{T_L}{T_H}$$); cümle içindeki tek sembolleri $...$ ile yaz. Değişkenlerin ne olduğunu kısa maddelerle açıkla.";
}
