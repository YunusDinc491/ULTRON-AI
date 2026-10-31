using System.Globalization;
using System.Text;

namespace Ultron.Core.Memories;

/// <summary>
/// Hafıza işlemlerinde modele verilen talimatlar ve JSON şemaları. Kategori listesi sabit değil:
/// veritabanından gelir (yerleşik + kullanıcının eklediği).
/// </summary>
public static class MemoryPrompts
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    /// <summary>Çekmeceler ve içlerindeki mevcut klasörler: "PATRON (Patron): … \n  - Saglik (Sağlık): …".</summary>
    public static string DrawerList(IReadOnlyList<MemoryCategory> categories, IEnumerable<MemoryDrawer>? drawers = null)
    {
        var list = new StringBuilder();
        foreach (var d in drawers ?? MemoryDrawers.All)
        {
            list.AppendLine($"{d.Name} (cekmece={d.Key}): {d.Description}");
            var inside = categories.Where(c => c.Drawer == d.Key).ToList();
            if (inside.Count == 0)
                list.AppendLine("  (henüz klasör yok)");
            foreach (var c in inside)
                list.AppendLine($"  - {c.Key} ({c.Name}): {c.Description}");
        }
        return list.ToString().TrimEnd();
    }

    public static string Extraction(DateTime now, IReadOnlyList<MemoryCategory> categories)
    {
        // tasarım kararı (2026-10-27): "söylediğim her şeyi kaydetsin, hafıza sorunumuz yok"; kısa cevaplar sorusuyla birlikte anlam kazanır.
        // Çekmeceler (2026-10-28): PATRON / İŞ / OKUL / BİLGİ; klasörleri Ultron açar.
        return $"""
            Görevin: kullanıcının mesajından HAFIZAYA yazılacak bilgileri çıkarmak. Kullanıcı kendisi hakkında söylediği HER şeyin hatırlanmasını istiyor; önemsiz görünse bile kaydet.
            ÇIKAR:
            1. Kullanıcı hakkında her bilgi: kim olduğu, yaşamı, sevdikleri ve sevmedikleri, planları, alışkanlıkları, fikirleri, kararları, eşyaları, çevresindeki kişiler, okulu, yazılım işleri.
            2. Kısa cevaplar: ÖNCEKİ ULTRON MESAJI bir soru sorduysa kullanıcının kısa cevabını ("Evet", "Hayır", "Mavi", "İkisi de") o soruyla birleştirip olgu yaz. Örnek: Ultron "Kedin var mı?" → kullanıcı "Evet, iki tane" → "İki kedisi var".
            3. Kullanıcının Ultron'a öğrettiği ya da "bunu bil, unutma" dediği bilgiler (genel bilgi olsa bile) → BİLGİ çekmecesi.
            4. Bir isteğin içindeki kalıcı bilgi: "Annemin doğum günü 5 Mayıs, hatırlat" → "Annesinin doğum günü 5 Mayıs".
            ÇIKARMA: selamlaşma, teşekkür; kullanıcının kendisi hakkında bir şey söylemeden sadece sorduğu sorular ("… nedir?", "dolar kaç?"); ÖNCEKİ ULTRON MESAJINDAKİ bilgiler (o sadece bağlam).
            Bilgiyi üçüncü şahıs, düz ve kısa bir OLGU cümlesi olarak yaz. Doğru: "Kedisinin adı Pamuk", "Sevgilisi kitap okumayı seviyor", "Fıstığa alerjisi var". Yanlış: "Kullanıcının kedisinin adı Pamuk olduğunu belirtiyor", "Kullanıcı … söylüyor". Cümleye "Kullanıcı", "belirtiyor", "söylüyor", "ifade ediyor" gibi kelimeler koyma. Kaydedilecek bilgi yoksa boş liste döndür.
            Hafıza dört ÇEKMECEDEN oluşur, her çekmecede konu KLASÖRLERİ vardır:
            {DrawerList(categories)}
            Her bilgi için: önce "cekmece" seç. Sonra o çekmecede konusu açıkça bu bilgiyi kapsayan bir klasör varsa "kategori" alanına onun anahtarını yaz.
            Uygun klasör yoksa "kategori" alanına YENI yaz; "yeni_ad" alanına konuyu anlatan kısa ve GENEL bir klasör adı (1-2 kelime), "yeni_aciklama" alanına bu klasöre ne tür bilgilerin gireceğini yaz.
            Örnek adlar: kedi/köpek → "Evcil hayvanlar"; anne/kardeş → "Aile"; araba → "Araç"; React/Next.js → "Frontend"; .NET API → "Backend"; Flutter → "Mobil"; dersler → "Dersler"; vize/final → "Sınavlar".
            Çok dar ad verme ("Pamuk" değil "Evcil hayvanlar"), çok geniş de verme ("Hayat", "Genel", "Diğer" değil).
            SADECE JSON döndür.
            Bugünün tarihi: {now.ToString("dd MMMM yyyy dddd", Turkish)}.
            """;
    }

    /// <summary>Anlamca yakın ama emin olunamayan iki klasör: yenisi açılmalı mı, mevcut olan mı kullanılmalı.</summary>
    public static string CategoryMatch(string drawerName, string newName, string newDescription, MemoryCategory existing) =>
        $"""
        Görevin: {drawerName} çekmecesinde yeni bir klasör açılmalı mı karar vermek.
        Açılmak istenen: "{newName}" ({newDescription}).
        Mevcut klasör: "{existing.Name}" ({existing.Description}).
        Yeni klasörün konusu mevcut klasörün konusuyla AYNI ya da onun bir parçasıysa ayni=true (ör. "Alerjiler" → "Sağlık", "Ailesi" → "Aile", "Futbol" → "Spor").
        Farklı konularsa ayni=false (ör. "Frontend" ile "Backend", "Dersler" ile "Sınavlar", "Aile" ile "Arkadaşlar").
        SADECE JSON döndür.
        """;

    public const string CategoryMatchSchema =
        """
        { "type": "object", "properties": { "ayni": { "type": "boolean" } }, "required": ["ayni"] }
        """;

    /// <summary>Kısa cevabın anlaşılması için kullanıcı mesajının önüne eklenen bağlam.</summary>
    public static string WithContext(string userText, string? previousAssistant) =>
        string.IsNullOrWhiteSpace(previousAssistant)
            ? userText
            : $"ÖNCEKİ ULTRON MESAJI (sadece bağlam, buradan bilgi çıkarma): {Clip(previousAssistant, 600)}\n\nKULLANICININ MESAJI: {userText}";

    /// <summary>
    /// Web araması ya da araştırma cevabından öğrenilen bilgiler (kullanıcı: "internetten araştırıp öğrendiklerini de hafızasında tutsun").
    /// </summary>
    public static string Learned(IReadOnlyList<MemoryCategory> categories) =>
        $"""
        Görevin: Ultron'un kullanıcı için web'den araştırıp verdiği cevaptan, ileride işe yarayacak KALICI bilgileri çıkarmak (en fazla 3).
        Sadece zamanla değişmeyen ya da yavaş değişen bilgiler: tanımlar, nasıl çalıştığı, önemli gerçekler (ör. "Retro etkisi, … demektir").
        Anlık ve hızla eskiyen bilgileri ÇIKARMA: döviz kuru, fiyat, hava durumu, skor, günün haberi.
        Her bilgiyi kısa, düz bir olgu cümlesi olarak yaz; kaynak numarası ([1]) koyma. Kalıcı bilgi yoksa boş liste döndür.
        Bilgiler BİLGİ çekmecesine girer:
        {DrawerList(categories, [MemoryDrawers.Find(MemoryDrawers.Bilgi)!])}
        Her bilgi için konusu açıkça uyan klasörün anahtarını "kategori" alanına yaz; uyan yoksa YENI yaz ve "yeni_ad" (1-2 kelimelik genel konu: "Termodinamik", "Ekonomi", "Yazılım") ile "yeni_aciklama" ver.
        SADECE JSON döndür.
        """;

    public static string LearnedSchema(IReadOnlyList<MemoryCategory> categories) =>
        $$"""
        { "type": "object",
          "properties": {
            "bilgiler": { "type": "array", "items": { "type": "object",
                "properties": { "bilgi": { "type": "string" },
                                "kategori": { "type": "string", "enum": [{{Keys(categories.Where(c => c.Drawer == MemoryDrawers.Bilgi))}}] },
                                "yeni_ad": { "type": "string" },
                                "yeni_aciklama": { "type": "string" } },
                "required": ["bilgi", "kategori"] } } },
          "required": ["bilgiler"] }
        """;

    /// <summary>JSON enum için anahtar listesi; sonuna YENI eklenir.</summary>
    private static string Keys(IEnumerable<MemoryCategory> categories) =>
        string.Join(", ", categories.Select(c => $"\"{c.Key}\"").Append($"\"{NewCategory}\""));

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    /// <summary>Kategori alanında "uygun klasör yok, yenisini aç" anlamına gelen değer.</summary>
    public const string NewCategory = "YENI";

    public static string ExtractionSchema(IReadOnlyList<MemoryCategory> categories) =>
        $$"""
        { "type": "object",
          "properties": {
            "bilgiler": { "type": "array", "items": { "type": "object",
                "properties": { "bilgi": { "type": "string" },
                                "cekmece": { "type": "string", "enum": [{{string.Join(", ", MemoryDrawers.All.Select(d => $"\"{d.Key}\""))}}] },
                                "kategori": { "type": "string", "enum": [{{Keys(categories)}}] },
                                "yeni_ad": { "type": "string" },
                                "yeni_aciklama": { "type": "string" } },
                "required": ["bilgi", "cekmece", "kategori"] } } },
          "required": ["bilgiler"] }
        """;

    public const string Decision =
        """
        Görevin: kullanıcı hakkında gelen YENİ bilginin hafızadaki MEVCUT kayıtlarla ilişkisine karar vermek.
        - EKLE: yeni bilgi mevcut kayıtlardan farklı bir konu; ayrı kayıt olarak eklenmeli.
        - GUNCELLE: yeni bilgi mevcut bir kaydın güncel hâli veya onunla çelişiyor (ör. "İstanbul'da yaşıyor" → "Ankara'da yaşıyor"). O kaydın id'sini ve birleştirilmiş güncel içeriği ver.
        - SIL: yeni bilgi mevcut bir kaydı geçersiz kılıyor ve kendisi saklanmaya değmez (ör. "Artık kedisi yok"). O kaydın id'sini ver.
        - YOK: yeni bilgi zaten aynen biliniyor.
        SADECE JSON döndür.
        """;

    public const string DecisionSchema =
        """
        { "type": "object",
          "properties": { "islem": { "type": "string", "enum": ["EKLE", "GUNCELLE", "SIL", "YOK"] },
                          "id": { "type": "integer" },
                          "icerik": { "type": "string" } },
          "required": ["islem"] }
        """;
}
