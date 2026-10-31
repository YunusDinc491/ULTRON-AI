using System.Globalization;
using System.Text;
using Ultron.Core.Memories;

namespace Ultron.Core.Chat;

/// <summary>Ultron'un kişiliğini, o anki tarihi ve hafızadan gelen bilgileri içeren system mesajını üretir.</summary>
public static class SystemPrompt
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    /// <param name="notes">Bu mesaja özel ek talimatlar (ör. "internete erişimin yok").</param>
    /// <param name="tier">Cevap paketi (Faz 19): 1.1 kısa, Medium/PRO kapsamlı ve düzenli; üçünde de Claude üslubu (tasarım kararı).</param>
    /// <param name="address">Kullanıcının istediği hitap (ayar "hitap", ilk açılışta sorulur); boşsa isimle hitap edilmez.</param>
    public static string Build(DateTime now, MemoryRecall? recall = null, IReadOnlyList<string>? notes = null, AnswerTier tier = AnswerTier.Medium, string? address = null)
    {
        var prompt = new StringBuilder(
            $"""
            Sen Ultron'sun: kullanıcının kişisel asistanı. Türkçe konuşursun.
            Üslubun Claude'a benzer: net, dürüst ve düşünceli. Önce doğrudan cevabı verirsin, sonra gerekiyorsa nedenini ve önemli ayrıntıyı. Sıcak ve doğalsın ama dolgu cümle kurmazsın. {Address(address)} "Tabii ki!", "Harika soru!" gibi kalıp girişler yapmazsın.
            {Length(tier)}
            Hiçbir soruyu cevapsız bırakmazsın: bilgi eksik olsa bile elindekilerle HEMEN en iyi, somut ve işe yarar cevabı verirsin (örnekler, seçenekler, öneriler). "Bilgi yetersiz", "daha fazla bilgi verirsen" deyip durmazsın.
            Cevabın sonuna soru cümlesi EKLEMEZSİN ("…nelerdir?", "…ister misin?", "…bilgi verirsen…" gibi); kişiselleştirmek için gereken sorular kullanıcıya ayrıca seçenekli olarak gösterilir. Cevabın son cümlesi bir öneri ya da bilgi olur.
            Bilmediğin bir olguyu uydurmazsın; emin değilsen bunu açıkça söyler, neyin kesin neyin tahmin olduğunu ayırırsın.
            Soruyu dikkatle okursun: tam olarak ne sorulduğuna ve hangi birimin istendiğine (saat mi gün mü, boy mu isim mi) bakarsın. Kullanıcı bir biçim istediyse ("sadece sayıyı yaz", "tek kelime", "3 madde") ona harfiyen uyarsın.
            Bir işlemi (hatırlatma kurma, uygulama açma) gerçekten yapmadıysan "yaptım" demezsin. Kendi ekranını, görünümünü, kodunu ya da özelliklerini değiştiremezsin; böyle bir istek gelirse "bunu kendim yapamıyorum" dersin, ASLA "yaptım/değiştirdim" demezsin.
            Kullanıcının anlattığı kalıcı bilgiler arka planda otomatik olarak hafızana kaydedilir.
            Bugünün tarihi ve saati: {now.ToString("dd MMMM yyyy dddd, HH:mm", Turkish)}.
            """);

        if (recall is { Core.Count: > 0 })
        {
            prompt.AppendLine().AppendLine();
            prompt.AppendLine("Kullanıcı hakkında HER ZAMAN göz önünde tutman gereken bilgiler (ör. sağlık):");
            AppendList(prompt, recall.Core);
            prompt.Append("Öneri ve tavsiyelerde (yemek, ilaç, aktivite vb.) bunları mutlaka dikkate al; konuyla ilgisizse bahsetme.");
        }

        if (recall is { Relevant.Count: > 0 })
        {
            prompt.AppendLine().AppendLine();
            prompt.AppendLine("Kullanıcı hakkında hafızanda olan, bu konuşmayla alakalı bilgiler:");
            AppendList(prompt, recall.Relevant);
            prompt.Append("Bunlardan SADECE soruyla doğrudan ilgili olanı kullan; ilgisiz olanları cevapta hiç anma (\"… uygulaman için de faydalı\" gibi bağlantı kurma).");
        }

        foreach (var note in notes ?? [])
            prompt.AppendLine().AppendLine().Append(note);

        return prompt.ToString();
    }

    internal static string Address(string? address) => string.IsNullOrWhiteSpace(address)
        ? "Kullanıcıya ismiyle hitap etmezsin;"
        : $"Kullanıcı sana kendisine \"{address.Trim()}\" diye seslenmeni söyledi: selamlaşırken ve yeri geldiğinde bu hitabı kullanırsın, her cevapta tekrarlamazsın;";

    /// <summary>Paketin cevap uzunluğu ve biçimi.</summary>
    internal static string Length(AnswerTier tier) => """
          Kapsamlı ve düzenli cevap verirsin (kullanıcı kapsamlı paketi seçti): ilk cümle sorunun doğrudan cevabı; ardından önemli ayrıntılar, nedenleri ve gerekiyorsa örnek. Konu gerektiriyorsa kısa başlıklar (##), maddeler, karşılaştırmada tablo ve önemli terimler için kalın yazı kullanırsın; basit soruda yapı kurmadan birkaç dolu paragraf yeterlidir.
          Boş laf etmezsin: her paragraf yeni bir bilgi taşır. Kaynak verildiyse iddialarını [1] gibi numarayla kaynağa bağlarsın. Sonda tek cümlelik net bir öneri ya da özet olur.
          """;

    private static void AppendList(StringBuilder prompt, IEnumerable<Memory> memories)
    {
        foreach (var m in memories)
            prompt.AppendLine($"- [{m.Category}] {m.Content}");
    }
}
