using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Core.Memories;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Memories;

public class MemoryServiceTests
{
    // Basit 3 boyutlu "anlam uzayı": x = kedi, y = şehir.
    private static readonly Dictionary<string, float[]> Vectors = new()
    {
        ["Kedisinin adı Pamuk"] = [1f, 0f, 0f],
        ["İstanbul'da yaşıyor"] = [0f, 1f, 0f],
        ["Ankara'da yaşıyor"] = [0.1f, 0.95f, 0f],
        ["Kedimin adı neydi?"] = [0.9f, 0.1f, 0f],
    };

    [Fact]
    public async Task Benzer_kayit_yoksa_modele_sormadan_eklenir()
    {
        var chat = new FakeChatModel("""{"bilgiler":[{"kategori":"OzelHayat","bilgi":"Kedisinin adı Pamuk"}]}""");
        var (service, store) = Create(chat);

        await service.ProcessAsync("Kedimin adı Pamuk", sourceMessageId: 7);

        var m = Assert.Single(store.All);
        Assert.Equal("OzelHayat", m.Category);
        Assert.Equal(7, m.SourceMessageId);
        Assert.Single(chat.JsonRequests); // sadece çıkarma; karar çağrısı yok
    }

    [Fact]
    public async Task Celisen_bilgi_eski_kaydi_gunceller()
    {
        var chat = new FakeChatModel(
            """{"bilgiler":[{"kategori":"OzelHayat","bilgi":"Ankara'da yaşıyor"}]}""",
            """{"islem":"GUNCELLE","id":1,"icerik":"Ankara'da yaşıyor"}""");
        var (service, store) = Create(chat);
        await store.AddAsync(new Memory { Category = "OzelHayat", Content = "İstanbul'da yaşıyor", Embedding = Vectors["İstanbul'da yaşıyor"] });

        await service.ProcessAsync("Ankara'ya taşındım", null);

        var m = Assert.Single(store.All);
        Assert.Equal("Ankara'da yaşıyor", m.Content);
        Assert.Contains("İstanbul'da yaşıyor", chat.JsonRequests[1][1].Content); // karar isteğine aday kayıt gitti
    }

    [Fact]
    public async Task Zaten_bilinen_bilgi_tekrar_eklenmez()
    {
        var chat = new FakeChatModel(
            """{"bilgiler":[{"kategori":"OzelHayat","bilgi":"Kedisinin adı Pamuk"}]}""",
            """{"islem":"YOK"}""");
        var (service, store) = Create(chat);
        await store.AddAsync(new Memory { Category = "OzelHayat", Content = "Kedisinin adı Pamuk", Embedding = Vectors["Kedisinin adı Pamuk"] });

        await service.ProcessAsync("Kedimin adı Pamuk demiştim", null);

        Assert.Single(store.All);
    }

    [Fact]
    public async Task Gecersiz_id_ile_guncelleme_istenirse_guvenli_tarafta_kalip_ekler()
    {
        var chat = new FakeChatModel(
            """{"bilgiler":[{"kategori":"OzelHayat","bilgi":"Ankara'da yaşıyor"}]}""",
            """{"islem":"GUNCELLE","id":999}""");
        var (service, store) = Create(chat);
        await store.AddAsync(new Memory { Category = "OzelHayat", Content = "İstanbul'da yaşıyor", Embedding = Vectors["İstanbul'da yaşıyor"] });

        await service.ProcessAsync("Ankara'ya taşındım", null);

        Assert.Equal(2, store.All.Count);
        Assert.Equal("İstanbul'da yaşıyor", store.All[0].Content); // mevcut kayda dokunulmadı
    }

    [Fact]
    public async Task Sil_karari_kaydi_pasiflestirir()
    {
        var chat = new FakeChatModel(
            """{"bilgiler":[{"kategori":"OzelHayat","bilgi":"Kedisinin adı Pamuk"}]}""",
            """{"islem":"SIL","id":1}""");
        var (service, store) = Create(chat);
        await store.AddAsync(new Memory { Category = "OzelHayat", Content = "Kedisinin adı Pamuk", Embedding = Vectors["Kedisinin adı Pamuk"] });

        await service.ProcessAsync("Artık kedim yok", null);

        Assert.False(Assert.Single(store.All).IsActive);
    }

    [Fact]
    public async Task Hatirlama_sadece_esigi_gecen_alakali_kayitlari_dondurur()
    {
        var (service, store) = Create(new FakeChatModel());
        await store.AddAsync(new Memory { Category = "OzelHayat", Content = "Kedisinin adı Pamuk", Embedding = Vectors["Kedisinin adı Pamuk"] });
        await store.AddAsync(new Memory { Category = "OzelHayat", Content = "İstanbul'da yaşıyor", Embedding = Vectors["İstanbul'da yaşıyor"] });

        var recalled = await service.RecallAsync("Kedimin adı neydi?");

        Assert.Empty(recalled.Core);
        Assert.Equal(["Kedisinin adı Pamuk"], recalled.Relevant.Select(m => m.Content));
    }

    [Fact]
    public async Task Saglik_bilgisi_alakasiz_gorunen_soruda_bile_cekirdek_olarak_gelir()
    {
        var (service, store) = Create(new FakeChatModel());
        await store.AddAsync(new Memory { Category = MemoryCategories.Saglik, Content = "Fıstığa alerjisi var", Embedding = [0f, 0f, 1f] });
        await store.AddAsync(new Memory { Category = "OzelHayat", Content = "İstanbul'da yaşıyor", Embedding = Vectors["İstanbul'da yaşıyor"] });

        // Soru vektörü ne alerjiye ne şehre benziyor: anlamsal arama ikisini de bulamaz.
        var recalled = await service.RecallAsync("Kedimin adı neydi?");

        Assert.Equal(["Fıstığa alerjisi var"], recalled.Core.Select(m => m.Content));
        Assert.DoesNotContain(recalled.Relevant, m => m.Content == "Fıstığa alerjisi var"); // iki kez gönderilmez
    }

    [Fact]
    public async Task Anahtar_kelime_anlamsal_aramanin_kacirdigi_ozel_ismi_bulur()
    {
        var (service, store) = Create(new FakeChatModel());
        // Vektörü soruyla alakasız (eşiğin altında), ama "Pamuk" adı geçiyor.
        await store.AddAsync(new Memory { Category = "OzelHayat", Content = "Pamuk'un veteriner randevusu cuma", Embedding = [0f, 1f, 0f] });

        var recalled = await service.RecallAsync("Pamuk'u ne zaman götürecektim?");

        Assert.Equal(["Pamuk'un veteriner randevusu cuma"], recalled.Relevant.Select(m => m.Content));
    }

    [Fact]
    public async Task Kisa_cevap_onceki_ultron_mesajiyla_birlikte_cikariciya_gider()
    {
        var chat = new FakeChatModel("""{"bilgiler":[{"kategori":"OzelHayat","bilgi":"İki kedisi var"}]}""");
        var (service, store) = Create(chat);
        await service.ProcessAsync("Evet, iki tane", null, previousAssistant: "Patron, kedin var mı?");
        Assert.Contains("ÖNCEKİ ULTRON MESAJI", chat.JsonRequests[0][1].Content);
        Assert.Contains("kedin var mı", chat.JsonRequests[0][1].Content);
        Assert.Equal("İki kedisi var", Assert.Single(store.All).Content);
    }

    // Klasör etiketleri ("Ad: açıklama") için vektörler: Evcil hayvanlar ≈ Hayvanlar (0,99), Araç ayrı, Spor ↔ Futbol arada (~0,6).
    private static readonly Dictionary<string, float[]> Labels = new()
    {
        ["Evcil hayvanlar: Kedi, köpek"] = [1f, 0f, 0f],
        ["Hayvanlar: Sahip olduğu hayvanlar"] = [0.99f, 0.1f, 0f],
        ["Araç: Araba"] = [0f, 1f, 0f],
        ["Spor: Futbol, koşu"] = [0f, 0f, 1f],
        ["Futbol: Halı saha"] = [0f, 0.8f, 0.6f],
        ["Sağlık: Sağlık durumu, alerji, ilaç, hastalık"] = [-1f, 0f, 0f], // yerleşik klasör hiçbirine benzemesin
    };

    private static string NewFact(string drawer, string name, string description, string fact) =>
        $$"""{"bilgiler":[{"cekmece":"{{drawer}}","kategori":"YENI","yeni_ad":"{{name}}","yeni_aciklama":"{{description}}","bilgi":"{{fact}}"}]}""";

    [Fact]
    public async Task Uygun_klasor_yoksa_cekmecede_yenisi_acilir_ve_haber_verilir()
    {
        var categories = new InMemoryCategoryStore();
        var notifier = new RecordingCategoryNotifier();
        var (service, store) = Create(new FakeChatModel(NewFact("Is", "Frontend", "React, arayüz", "Next.js öğreniyor")), categories, notifier);

        await service.ProcessAsync("Next.js öğreniyorum", null);

        var frontend = Assert.Single(categories.All, c => c.Name == "Frontend"); // tasarım kararı: onay beklemeden açılır
        Assert.Equal(MemoryDrawers.Is, frontend.Drawer);
        Assert.Equal(frontend.Key, Assert.Single(store.All).Category);
        Assert.Equal(("Frontend", "Next.js öğreniyor"), Assert.Single(notifier.Created));
    }

    [Fact]
    public async Task Anlamca_ayni_klasor_varsa_yenisi_acilmaz()
    {
        var categories = new InMemoryCategoryStore();
        categories.All.Add(new MemoryCategory { Key = "EvcilHayvanlar", Name = "Evcil hayvanlar", Description = "Kedi, köpek", Drawer = MemoryDrawers.Patron });
        var notifier = new RecordingCategoryNotifier();
        var (service, store) = Create(new FakeChatModel(NewFact("Patron", "Hayvanlar", "Sahip olduğu hayvanlar", "İki kedisi var")), categories, notifier, Labels);

        await service.ProcessAsync("İki kedim var", null);

        Assert.Equal("EvcilHayvanlar", Assert.Single(store.All).Category);
        Assert.Empty(notifier.Created);
    }

    [Fact]
    public async Task Ayni_ad_baska_cekmecede_degil_once_bu_cekmeceye_bakilir_onek_de_ayni_sayilir()
    {
        var categories = new InMemoryCategoryStore();
        categories.All.Add(new MemoryCategory { Key = "Aile", Name = "Aile", Description = "Anne, baba", Drawer = MemoryDrawers.Patron });
        var (service, store) = Create(new FakeChatModel(NewFact("Patron", "Ailesi", "Aile üyeleri", "Annesi öğretmen")), categories);

        await service.ProcessAsync("Annem öğretmen", null);

        Assert.Equal("Aile", Assert.Single(store.All).Category); // "Ailesi" → "Aile"; model çağrısı da gerekmedi
    }

    [Fact]
    public async Task Arada_kalan_benzerlikte_modele_sorulur()
    {
        var categories = new InMemoryCategoryStore();
        categories.All.Add(new MemoryCategory { Key = "Spor", Name = "Spor", Description = "Futbol, koşu", Drawer = MemoryDrawers.Patron });
        var chat = new FakeChatModel(NewFact("Patron", "Futbol", "Halı saha", "Hafta sonu halı saha oynuyor"), """{"ayni":true}""");
        var (service, store) = Create(chat, categories, labels: Labels);

        await service.ProcessAsync("Hafta sonu halı saha oynarım", null);

        Assert.Equal("Spor", Assert.Single(store.All).Category);
        Assert.Contains("Futbol", chat.JsonRequests[1][0].Content); // ikinci çağrı: "aynı konu mu?"
    }

    private sealed class RecordingCategoryNotifier : ICategoryNotifier
    {
        public List<(string Name, string Example)> Created { get; } = [];

        public Task NotifyCategoryCreatedAsync(MemoryCategory category, string example, CancellationToken cancellationToken = default)
        {
            Created.Add((category.Name, example));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Cekirdek_kategori_panelden_degistirilebilir()
    {
        var categories = new InMemoryCategoryStore();
        categories.All.Add(new MemoryCategory { Key = "Finans", Name = "Finans", Description = "Bütçe", Drawer = MemoryDrawers.Patron });
        var (service, store) = Create(new FakeChatModel(), categories);
        await store.AddAsync(new Memory { Category = "Finans", Content = "Aylık bütçesi 15000 TL", Embedding = [0f, 0f, 1f] });

        Assert.Empty((await service.RecallAsync("Kedimin adı neydi?")).Core);

        await categories.UpdateAsync("Finans", "Finans", "", isCore: true);

        Assert.Equal(["Aylık bütçesi 15000 TL"], (await service.RecallAsync("Kedimin adı neydi?")).Core.Select(m => m.Content));
    }

    private static (MemoryService Service, InMemoryMemoryStore Store) Create(FakeChatModel chat, InMemoryCategoryStore? categories = null, ICategoryNotifier? notifier = null, Dictionary<string, float[]>? labels = null)
    {
        if (categories is null)
        {
            categories = new InMemoryCategoryStore();
            categories.All.Add(new MemoryCategory { Key = "OzelHayat", Name = "Özel hayat", Description = "Ev, evcil hayvan, şehir", Drawer = MemoryDrawers.Patron });
            categories.All.Add(new MemoryCategory { Key = "Finans", Name = "Finans", Description = "Bütçe", Drawer = MemoryDrawers.Patron });
        }
        var store = new InMemoryMemoryStore();
        var service = new MemoryService(
            new MemoryExtractor(chat, categories),
            chat,
            new FakeEmbeddingModel(labels is null ? Vectors : Vectors.Concat(labels).ToDictionary()),
            store,
            categories,
            Options.Create(new MemoryOptions { RecallMinScore = 0.35, ConflictCandidateMinScore = 0.5 }),
            NullLogger<MemoryService>.Instance,
            notifier);
        return (service, store);
    }
}

