using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Core.Chat;
using Ultron.Core.Conversations;
using Ultron.Core.Documents;
using Ultron.Core.Memories;
using Ultron.Core.Research;
using System.Text.Json;
using Ultron.Core.Routing;
using Ultron.Core.Tools;
using Ultron.Core.Web;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Chat;

public class ChatOrchestratorTests
{
    [Fact]
    public async Task Sohbet_olaylari_sirayla_gelir_mesajlar_kaydedilir_bilgi_cikarma_kuyruga_girer()
    {
        var h = new Harness();

        var events = await h.SendAsync("Merhaba");

        Assert.Collection(events,
            e => Assert.Equal(MessageIntent.Sohbet, Assert.IsType<IntentDetected>(e).Intent),
            e => Assert.IsType<MemoriesRecalled>(e),
            e => Assert.Equal("Mer", Assert.IsType<AnswerChunk>(e).Text),
            e => Assert.Equal("haba.", Assert.IsType<AnswerChunk>(e).Text),
            e => Assert.Equal("Merhaba.", Assert.IsType<AnswerCompleted>(e).Text));
        Assert.Equal([(Role.User, "Merhaba"), (Role.Assistant, "Merhaba.")], h.Conversations.Messages.Select(m => (m.Role, m.Content)));
        Assert.Equal("Merhaba", Assert.Single(h.Queue.Jobs).UserText);
    }

    [Fact]
    public async Task Onceki_mesajlar_gecmis_olarak_modele_gider()
    {
        var h = new Harness();
        await h.SendAsync("Kedimin adı Pamuk.");

        await h.SendAsync("Adı neydi?");

        var sent = h.Chat.StreamRequests[^1];
        Assert.Equal([Role.System, Role.User, Role.Assistant, Role.User], sent.Select(m => m.Role));
        Assert.Equal("Kedimin adı Pamuk.", sent[1].Content);
    }

    [Fact]
    public async Task Kisisel_soruda_cevaptan_sonra_secenekli_takip_sorulari_gelir()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Kisisel"}""";
        h.Chat.FollowUpJson = """{"sorular":[{"soru":"Bütçen ne kadar?","secenekler":["Bütçem 1000 TL'ye kadar","Bütçem 1000-3000 TL"]}]}""";

        var events = await h.SendAsync("Sevgilime ne hediye alabilirim?");

        Assert.IsType<AnswerCompleted>(events[^2]); // önce cevap tamamlanır, sorular sonra gelir
        var q = Assert.Single(Assert.IsType<FollowUpsSuggested>(events[^1]).Questions);
        Assert.Equal("Bütçen ne kadar?", q.Question);
        Assert.Equal(2, q.Options.Count);
    }

    // ---- Cevap paketleri (Faz 19) ----

    [Fact]
    public async Task Pro_bilgi_sorusunu_Claude_arastirir_cevap_hafizaya_girer_sonra_hangi_pakette_olursa_Claude_a_gitmeden_cevaplanir()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"GenelBilgi"}""";
        await h.TierAsync("pro");

        var events = await h.SendAsync("Kuantum dolanıklığı nedir?");

        var request = Assert.Single(h.Agent.Requests);
        Assert.Contains("Soru: Kuantum dolanıklığı nedir?", request.Prompt);
        Assert.Contains(events.OfType<TierProgress>(), p => p.Tier == "Ultron Pro" && p.Step.Contains("Arıyor"));
        var answer = events.OfType<AnswerCompleted>().Single().Text;
        Assert.StartsWith("Kuantum dolanıklığı, iki parçacığın", answer);
        Assert.Contains("**Kaynaklar**", answer);
        Assert.Empty(h.Chat.StreamRequests); // yerel model yazmadı

        await h.TierAsync("1.1");
        var again = await h.SendAsync("Kuantum dolanıklığı nedir?");

        Assert.Single(h.Agent.Requests); // Claude'a ikinci kez gidilmedi
        Assert.Contains(again.OfType<TierProgress>(), p => p.Tier == "PRO hafızası");
        Assert.StartsWith("Kuantum dolanıklığı, iki parçacığın", again.OfType<AnswerCompleted>().Single().Text);
        Assert.Equal(1, Assert.Single(await h.Pro.LearnedAsync()).Uses);
    }

    [Fact]
    public async Task Pro_kisisel_soruyu_Claude_a_gondermez()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Kisisel"}""";
        await h.TierAsync("pro");

        await h.SendAsync("Benim en sevdiğim yemek neydi?");

        Assert.Empty(h.Agent.Requests);
        Assert.Contains("Kapsamlı ve düzenli", h.Chat.StreamRequests.Single()[0].Content); // yerelde, kapsamlı üslupla
    }

    [Fact]
    public async Task Pro_Claude_cevap_veremezse_Medium_a_duser()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"GenelBilgi"}""";
        h.Agent.Ok = false;
        await h.TierAsync("pro");

        var events = await h.SendAsync("Kuantum dolanıklığı nedir?");

        Assert.Contains(events.OfType<TierProgress>(), p => p.Tier == "Ultron" && p.Step.Contains("Ultron ile"));
        Assert.Single(events.OfType<WebPrepared>());
        Assert.Empty(await h.Pro.LearnedAsync());
    }

    [Fact]
    public async Task Medium_genel_bilgi_sorusunda_derin_web_ve_kapsamli_uslup()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"GenelBilgi"}""";
        await h.TierAsync("medium");

        var events = await h.SendAsync("Dolar neden yükseliyor?");

        Assert.Contains(events.OfType<TierProgress>(), p => p.Tier == "Ultron" && p.Step.Contains("kaynak okunuyor"));
        Assert.Single(events.OfType<WebPrepared>());
        var system = h.Chat.StreamRequests.Single()[0].Content;
        Assert.Contains("Kapsamlı ve düzenli", system);
        Assert.Contains("41,25 TL", system); // seçilen parça bağlamda
        Assert.Empty(h.Agent.Requests);
    }

    [Fact]
    public void Pro_ciktisi_ayrisir_etiketsizse_temkinli_sureli()
    {
        var (answer, sources, life) = Ultron.Core.Pro.ProService.Parse("<cevap>A</cevap><kaynaklar>[1] x — https://x</kaynaklar><kalicilik>gün</kalicilik>");
        Assert.Equal(("A", "[1] x — https://x", "gun"), (answer, sources, life));
        Assert.Equal(("düz metin", "", "hafta"), Ultron.Core.Pro.ProService.Parse("düz metin"));
    }

    [Fact]
    public async Task Guncel_soruda_takip_sorusu_uretilmez()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Guncel"}""";
        h.Chat.FollowUpJson = """{"sorular":[{"soru":"x","secenekler":["a","b"]}]}""";

        var events = await h.SendAsync("Dolar kaç TL?");

        Assert.Empty(events.OfType<FollowUpsSuggested>());
    }

    [Theory]
    [InlineData("""{"sorular":[{"soru":"Tek seçenek?","secenekler":["a"]}]}""", 0)] // zahmetsiz değil
    [InlineData("""{"sorular":[{"soru":"S1","secenekler":["a","b"]},{"soru":"S2","secenekler":["a","b"]},{"soru":"S3","secenekler":["a","b"]},{"soru":"S4","secenekler":["a","b"]}]}""", 3)]
    [InlineData("""{"sorular":[]}""", 0)]
    public void Takip_sorulari_ayristirilir_ve_sinirlanir(string json, int expected) =>
        Assert.Equal(expected, FollowUpService.Parse(json).Count);

    [Fact]
    public void Secenekte_kisa_etiket_gorunur_tam_cumle_gonderilir()
    {
        var q = Assert.Single(FollowUpService.Parse("""
            {"sorular":[{"soru":"Bütçen?","secenekler":[
              {"etiket":"1000 TL'ye kadar","cumle":"Bütçem 1000 TL'ye kadar."},
              {"etiket":"Evde, sakin ve yaratıcı hobilerle vakit geçirmeyi tercih ediyor","cumle":"Sevgilim evde yaratıcı hobileri seviyor."}]}]}
            """));

        Assert.Equal(new FollowUpOption("1000 TL'ye kadar", "Bütçem 1000 TL'ye kadar."), q.Options[0]);
        Assert.True(q.Options[1].Label.Length <= 33);   // uzun etiket kısaltılır
        Assert.EndsWith("…", q.Options[1].Label);
        Assert.Equal("Sevgilim evde yaratıcı hobileri seviyor.", q.Options[1].Answer);
    }

    [Fact]
    public async Task Komutta_arac_dongusu_calisir_ve_hatirlatici_kurulur()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Komut"}""";
        h.Chat.ToolTurns.Enqueue(new ToolTurn("", [new ToolCall("hatirlatici_kur",
            JsonDocument.Parse("""{"metin":"Annemi ara","zaman":"2026-10-27T09:00","onem":"onemli"}""").RootElement)]));
        h.Chat.ToolTurns.Enqueue(new ToolTurn("Tamam, yarın 09:00'da annemi aramanı hatırlatacağım.", []));

        var events = await h.SendAsync("Yarın 9'da annemi aramamı hatırlat, önemli");

        var used = Assert.Single(Assert.Single(events.OfType<ToolsUsed>()).Uses);
        Assert.True(used.Ok);
        Assert.Equal("Annemi ara", Assert.Single(h.ToolSet.Store.Items).Text);
        Assert.Contains("09:00", Assert.Single(events.OfType<AnswerChunk>()).Text);
        Assert.Empty(h.Chat.StreamRequests); // akışlı cevap değil, araç döngüsü
        Assert.Contains("aracı ÇAĞIR", h.Chat.ToolRequests[0].Messages[0].Content);
        Assert.Empty(events.OfType<FollowUpsSuggested>());
    }

    [Fact]
    public async Task Hava_sorusunda_web_aranmaz_sadece_hava_araci_sunulur()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Guncel"}""";

        var events = await h.SendAsync("Yarın hava nasıl olacak?");

        Assert.Empty(events.OfType<WebPrepared>());
        Assert.Empty(events.OfType<WebUnavailable>());
        Assert.Equal("hava_durumu", Assert.Single(h.Chat.ToolRequests[0].Tools).Name);
    }

    [Fact]
    public async Task Guncel_soruda_web_kaynaklari_verilir_ve_onbellege_yazilir()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Guncel"}""";

        var events = await h.SendAsync("Dolar kaç TL?");

        Assert.False(Assert.Single(events.OfType<WebPrepared>()).Context.FromCache);
        Assert.Equal("https://a.com", Assert.Single(Assert.Single(events.OfType<SourcesListed>()).Sources).Url);
        Assert.Contains("<kaynak no=\"1\"", h.Chat.StreamRequests[0][0].Content); // kaynak notu system prompt'ta
        Assert.Single(h.WebCache.Entries);
    }

    [Fact]
    public async Task Web_cokerse_erisim_yok_notuyla_devam_edilir()
    {
        var h = new Harness(searchFails: true);
        h.Chat.RouterJson = """{"sinif":"Guncel"}""";

        var events = await h.SendAsync("Dolar kaç TL?");

        Assert.Single(events.OfType<WebUnavailable>());
        Assert.Contains(IntentNotes.NoWebAccess, h.Chat.StreamRequests[0][0].Content);
        Assert.IsType<AnswerCompleted>(events[^1]);
    }

    [Fact]
    public async Task Kelime_yaniltirsa_arac_secilmez_normal_sohbet_cevabi_verilir()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Kisisel"}""";
        h.Chat.ToolTurns.Enqueue(new ToolTurn("", []));
        h.Chat.ToolTurns.Enqueue(new ToolTurn("", [])); // uyarıdan sonra da araç yok

        var events = await h.SendAsync("Derslere slaytlardan özet çıkararak çalışıyorum.");

        Assert.Empty(events.OfType<ToolsUsed>());
        Assert.Equal("Merhaba.", Assert.IsType<AnswerCompleted>(events.Last(e => e is AnswerCompleted)).Text);
        Assert.Single(h.Chat.StreamRequests); // kişilikli sohbet cevabı
    }

    [Fact]
    public async Task Belge_sorusunda_parcalar_veri_olarak_verilir_anilan_kaynak_listelenir_ders_araci_secilmez()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Belge"}""";
        h.Chat.AnswerChunks = ["Notlarına göre entropi düzensizliğin ölçüsü [1]."];
        await h.Documents.AddFileAsync("termo.pdf", System.Text.Encoding.UTF8.GetBytes("Entropi düzensizliğin ölçüsüdür."), "Termo II");

        var events = await h.SendAsync("Termo II ders notlarımda entropi tanımı ne?");

        var searched = Assert.Single(events.OfType<DocumentsSearched>());
        Assert.Equal("Termo II", searched.Context.Scope);
        Assert.Contains("<parca no=\"1\"", h.Chat.StreamRequests[0][0].Content);
        Assert.Empty(h.Chat.ToolRequests); // "ders" kelimesi ders programı aracına götürmedi
        var listed = Assert.Single(events.OfType<DocumentSourcesListed>());
        Assert.Equal(1, Assert.Single(listed.Sources).Page);
        Assert.Empty(events.OfType<FollowUpsSuggested>());
    }

    [Fact]
    public async Task Yazilim_sorusunda_kutuphane_bolumleri_verilir_anilan_kaynak_kart_olur()
    {
        var h = new Harness();
        h.LibraryStore.Add("useEffect", "https://react.dev/reference/react/useEffect", "useEffect › Usage\nuseEffect runs twice in development with Strict Mode.", [0f, 0f, 1f]);
        h.Chat.RouterJson = """{"sinif":"Yazilim"}""";
        h.Chat.AnswerChunks = ["Geliştirme modunda Strict Mode yüzünden iki kez çalışır [1]."];

        var events = await h.SendAsync("useEffect neden iki kez çalışıyor?");

        Assert.Equal(1, Assert.Single(events.OfType<LibrarySearched>()).Count);
        var system = h.Chat.StreamRequests[0][0].Content;
        Assert.Contains("<kaynak no=\"1\"", system);
        Assert.Contains("Claude Code'a vereyim mi?", system); // büyük iş için devretme kuralı
        // Ultron paketinde (eski Medium) derin web de çalışır: kütüphane kartı web kaynaklarının yanında listelenir.
        Assert.Contains(events.OfType<SourcesListed>().SelectMany(s => s.Sources), c => c.Url == "https://react.dev/reference/react/useEffect");
        Assert.Empty(events.OfType<FollowUpsSuggested>());
    }

    [Fact]
    public async Task Router_GenelBilgi_dese_de_teknoloji_adi_gecen_soruda_kutuphaneye_bakilir_bos_kutuphanede_not_yok()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"GenelBilgi"}""";
        await h.SendAsync("Next.js nedir?");
        Assert.DoesNotContain("Kütüphaneden", h.Chat.StreamRequests[0][0].Content); // kütüphane boş

        h.LibraryStore.Add("Next.js", "https://nextjs.org/docs", "Next.js\nNext.js is a React framework.", [0f, 0f, 1f]);
        await h.SendAsync("Next.js nedir?");
        Assert.Contains("Kütüphaneden", h.Chat.StreamRequests[^1][0].Content);
    }

    [Fact]
    public void Kaynaklarda_gecmeyen_API_adi_yakalanir_gercek_olan_yakalanmaz()
    {
        var page = new Ultron.Core.Library.LibraryPage { SourceId = "react", Path = "p", Title = "useEffect", Url = "u", Version = "v", ContentHash = "h" };
        var hits = new[] { new Ultron.Core.Library.LibraryHit(new Ultron.Core.Library.LibraryChunk { Text = "useEffect runs after render; see useState too." }, page, "React", 1) };

        Assert.Equal(["useServerMagic"], Ultron.Core.Library.LibraryAnswer.UnknownApis("Next.js 15'teki useServerMagic hook'u ne işe yarar? useEffect ile farkı?", hits));
        Assert.Equal(["AutoMagicSave"], Ultron.Core.Library.LibraryAnswer.UnknownApis("EF Core'daki AutoMagicSave() metodu nasıl kullanılır?", hits));
        Assert.Empty(Ultron.Core.Library.LibraryAnswer.UnknownApis("useEffect neden iki kez çalışıyor?", hits));
    }

    [Fact]
    public void Kaynaklarda_gecmeyen_ozel_isimli_kavram_yakalanir()
    {
        var page = new Ultron.Core.Library.LibraryPage { SourceId = "muhendislik", Path = "p", Title = "Akışkanlar", Url = "u", Version = "v", ContentHash = "h" };
        var hits = new[] { new Ultron.Core.Library.LibraryHit(new Ultron.Core.Library.LibraryChunk { Text = "Reynolds sayısı Re = ρVD/μ." }, page, "Notlar", 1) };

        Assert.Equal(["Kayalar"], Ultron.Core.Library.LibraryAnswer.UnknownNamedConcepts("Akışkanlarda kullanılan 'Kayalar katsayısı' nedir?", hits));
        Assert.Empty(Ultron.Core.Library.LibraryAnswer.UnknownNamedConcepts("Reynolds sayısı nedir?", hits));
    }

    [Theory]
    [InlineData("EF Core'da Include ne işe yarar", true)]
    [InlineData("Array.map() ile forEach farkı", true)]
    [InlineData("useEffect neden iki kez çalışıyor", true)]
    [InlineData("git commit mesajı nasıl düzeltilir", true)]
    [InlineData("Eve git ve dinlen", false)]
    [InlineData("Entropi nedir", true)] // ders notları da kütüphanede (tur6)
    [InlineData("Bugün çok yoruldum", false)]
    public void Yazilim_terimi_tanima(string text, bool expected) => Assert.Equal(expected, Ultron.Core.Library.LibraryAnswer.Mentions(text));

    [Fact]
    public async Task Belge_yoksa_durustce_soyleme_notu_eklenir_yanlis_alarmda_not_eklenmez()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Belge"}""";
        await h.SendAsync("Yüklediğim PDF'te Carnot verimi kaç?");
        Assert.Contains("hiç belge yok", h.Chat.StreamRequests[0][0].Content);

        h.Chat.RouterJson = """{"sinif":"Sohbet"}""";
        var events = await h.SendAsync("Notlarımı boşver, nasılsın?");
        Assert.Empty(events.OfType<DocumentsSearched>());
        Assert.DoesNotContain("hiç belge yok", h.Chat.StreamRequests[1][0].Content);
    }

    [Fact]
    public async Task Arastirma_istegi_plan_arama_rapor_ve_bilgi_tabanina_kayit_yapar()
    {
        var h = new Harness();
        h.Chat.AnswerChunks = ["# Isı Pompaları\n", "Isı pompası ısıyı taşır [1]."];

        var events = await h.SendAsync("Isı pompalarını araştır");

        Assert.Contains(events.OfType<ResearchProgress>(), p => p.Step.StartsWith("Plan: 2 arama"));
        Assert.Contains("VERİDİR", h.Chat.StreamRequests[0][0].Content);
        Assert.Contains("<kaynak no=\"1\"", h.Chat.StreamRequests[0][0].Content);
        Assert.Single(events.OfType<SourcesListed>());
        var saved = Assert.Single(events.OfType<ResearchSaved>()).Document!;
        Assert.Equal(("Araştırmalar", DocumentKind.Research, "Isı Pompaları.md"), (saved.Collection, saved.Kind, saved.FileName));
        var completed = Assert.IsType<AnswerCompleted>(events[^1]);
        Assert.Contains("bilgi tabanına kaydedildi", completed.Text);
        Assert.Empty(events.OfType<IntentDetected>()); // router'a hiç gitmez
        Assert.Contains(h.WebCache.Outgoing, q => q == "ısı pompası verimi cop"); // dışarı giden sorgu kayıtlı
    }

    [Theory]
    [InlineData("Isı pompalarını araştır", true)]
    [InlineData("Güneş panellerinin verimini araştırır mısın?", true)]
    [InlineData("Elektrikli araç bataryaları hakkında rapor hazırla", true)]
    [InlineData("Araştırma ödevim var, yarın teslim", false)]
    [InlineData("Araştırmada COP için ne demiştin?", false)]
    [InlineData("Yarın 9'da araştırma yapmamı hatırlat", true)] // IsRequest true ama hatırlatma aracı öncelikli (orkestratör)
    public void Arastirma_istegi_algilanir(string message, bool expected) =>
        Assert.Equal(expected, ResearchService.IsRequest(message));

    [Fact]
    public async Task Gorselli_soru_gorselle_modele_gider_gorsel_kaydedilmez_yonlendirici_calismaz()
    {
        var h = new Harness();
        h.Chat.AnswerChunks = ["Görselde bir devre şeması var."];

        var events = await h.SendWithImageAsync("Bu devre ne işe yarar?", new ImageInput([9, 9, 9]));

        Assert.IsType<ImageAttached>(events[0]);
        Assert.Empty(events.OfType<IntentDetected>());
        var request = h.Chat.StreamRequests.Single();
        Assert.Equal([9, 9, 9], Assert.Single(request[^1].Images!));
        Assert.Equal("Bu devre ne işe yarar?", request[^1].Content);
        Assert.Contains("VERİDİR", request[0].Content);
        Assert.Equal("[Görsel] Bu devre ne işe yarar?", h.Conversations.Messages[0].Content);
        Assert.Equal("Görselde bir devre şeması var.", Assert.IsType<AnswerCompleted>(events[^1]).Text);
    }

    [Fact]
    public async Task Bos_soruyla_ekran_goruntusu_varsayilan_soruyla_ve_ekran_notuyla_gider()
    {
        var h = new Harness();

        var events = await h.SendWithImageAsync("", new ImageInput([1], FromScreen: true));

        Assert.True(Assert.IsType<ImageAttached>(events[0]).FromScreen);
        var request = h.Chat.StreamRequests.Single();
        Assert.Equal(ImagePrompts.DefaultQuestion, request[^1].Content);
        Assert.Contains("ekrana bak", request[0].Content);
        Assert.Equal("[Ekran görüntüsü]", h.Conversations.Messages[0].Content);
        Assert.Empty(h.Queue.Jobs); // boş metin hafızaya gitmez
    }

    [Fact]
    public async Task Ozet_isteginde_belgenin_tamami_verilir_okunamayan_sayfalar_bildirilir()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Kisisel"}""";
        // 4 sayfa: 2. ve 3. sayfa resim (yazı yok).
        await h.Documents.AddFileAsync("week_1.pdf", System.Text.Encoding.UTF8.GetBytes("Isı iletimi Fourier yasasıyla anlatılır.\f \f \fTaşınım katsayısı h ile gösterilir."), null);

        var events = await h.SendAsync("Yüklediğim belgeyi incele ve bana detaylı bir özet çıkar");

        var context = Assert.Single(events.OfType<DocumentsSearched>()).Context;
        Assert.True(context.Whole);
        Assert.Equal([2, 3], context.UnreadPages!);
        var system = h.Chat.StreamRequests[0][0].Content;
        Assert.Contains("TAMAMI", system);
        Assert.Contains("Fourier", system);
        Assert.Contains("Taşınım", system);
        Assert.Contains("s. 2-3", system);
    }

    [Fact]
    public async Task Uzun_belge_bolum_bolum_notlanip_ozetlenir()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Belge"}""";
        var pages = Enumerable.Range(1, 40).Select(i => string.Join(" ", Enumerable.Range(0, 80).Select(j => $"Sayfa {i} cümle {j} ısı transferi.")));
        await h.Documents.AddFileAsync("uzun.pdf", System.Text.Encoding.UTF8.GetBytes(string.Join("\f", pages)), "Heat Transfer");

        var events = await h.SendAsync("Heat Transfer notlarını özetle");

        var context = Assert.Single(events.OfType<DocumentsSearched>()).Context;
        Assert.True(context.Whole);
        Assert.InRange(context.Sources.Count, 2, 12); // bölüm notları
        Assert.All(context.Sources, s => Assert.Equal("- Bölüm notu (s. 1)", s.Text));
    }

    [Theory]
    [InlineData("Yüklediğim belgeyi incele ve özet çıkar", true)]
    [InlineData("Heat Transfer notlarını özetle", true)]
    [InlineData("Bu PDF neden bahsediyor?", true)]
    [InlineData("Termo II notlarımda entropi tanımı ne?", false)]
    public void Butun_belge_istegi_algilanir(string message, bool expected) =>
        Assert.Equal(expected, DocumentReader.IsWholeRequest(message));

    [Fact]
    public void Yazisiz_sayfalar_eklemede_bildirilir()
    {
        Assert.Equal("1, 4-6, 10", DocumentService.PageList([1, 4, 5, 6, 10]));
        Assert.Equal([2, 3], DocumentService.BlankPages([new(1, "Yeterince uzun bir sayfa metni burada."), new(2, " "), new(3, "12"), new(null, "")]));
    }

    [Fact]
    public async Task Bilgi_tabaninda_yoksa_dosya_sorusu_bilgisayar_aracina_gider()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Belge"}""";

        var events = await h.SendAsync("ultron-deneme.txt dosyasında ne yazıyor?");

        Assert.Empty(events.OfType<DocumentsSearched>());
        Assert.NotEmpty(h.Chat.ToolRequests); // araç turu (dosya_oku sunuldu)
    }

    [Fact]
    public async Task Notlar_kelimesi_gecen_hatirlatma_istegi_belgeye_degil_araca_gider()
    {
        var h = new Harness();
        h.Chat.RouterJson = """{"sinif":"Belge"}""";
        await h.Documents.AddFileAsync("n.txt", System.Text.Encoding.UTF8.GetBytes("Notlar çalışma planı."), null);

        var events = await h.SendAsync("Yarın 9'da notlarımı çalışmamı hatırlat");

        Assert.Empty(events.OfType<DocumentsSearched>());
        Assert.NotEmpty(h.Chat.ToolRequests);
    }

    [Fact]
    public async Task Arastirma_kelimesi_gecen_hatirlatma_istegi_arastirmaya_gitmez()
    {
        var h = new Harness();
        var events = await h.SendAsync("Yarın 9'da araştırma yapmamı hatırlat");
        Assert.Empty(events.OfType<ResearchProgress>());
    }

    [Fact]
    public void Bozuk_plan_guvenli_varsayilana_duser_en_fazla_dort_sorgu()
    {
        Assert.Equal(["X'i araştır"], ResearchService.ParsePlan("bozuk", "X'i araştır").Queries);
        Assert.Equal(4, ResearchService.ParsePlan("""{"konu":"k","baslik":"b","sorgular":["a","b","c","d","e","A"]}""", "x").Queries.Count);
    }

    [Fact]
    public void Kaynak_kartinda_sadece_anilan_parcalar_hic_anilmadiysa_kart_yok()
    {
        DocumentSource S(int n) => new(n, 1, "a.pdf", "Genel", n, "metin", "a.pdf");
        Assert.Equal([2], DocumentCitations.Cited([S(1), S(2), S(3)], "Şöyle [2].").Select(s => s.Number));
        Assert.Empty(DocumentCitations.Cited([S(1), S(2), S(3)], "Notlarda bulunmuyor."));
    }

    [Fact]
    public void Numarasiz_cevapta_acikca_ortusen_parca_kart_olur_bulamadim_cevabinda_olmaz()
    {
        DocumentSource[] sources =
        [
            new(1, 1, "t.pdf", "Termo II", 1, "TERMODİNAMİK II DERS NOTLARI Hafta 5-7 Entropi, Carnot ve Rankine çevrimleri", "t.pdf"),
            new(2, 1, "t.pdf", "Termo II", 6, "Değerlendirme. Ara sınav 14 Kasım'da yapılacak; 1-6. haftalar dahil.", "t.pdf"),
        ];

        Assert.Equal(6, Assert.Single(DocumentCitations.Cited(sources, "Ara sınav 14 Kasım'da yapılacakmış, 1-6. haftaları kapsıyor.")).Page);
        Assert.Empty(DocumentCitations.Cited(sources, "Notlarda Stirling motoruna dair bir bilgi bulamadım; Carnot ve Rankine çevrimleri var."));
    }

    [Fact]
    public void Yanlis_parcaya_verilen_numara_kelimeleri_ortusen_parcaya_duzeltilir()
    {
        DocumentSource[] sources =
        [
            new(1, 1, "t.pdf", "Termo II", 1, "TERMODİNAMİK II DERS NOTLARI Hafta 5-7 Entropi, Carnot ve Rankine çevrimleri", "t.pdf"),
            new(2, 1, "t.pdf", "Termo II", 5, "Buhar güç santrallerinin ideal çevrimi Rankine çevrimidir: pompa, kazan, türbin, kondenser.", "t.pdf"),
        ];

        var fixedText = DocumentCitations.Repair("Rankine çevrimi buhar güç santrallerinin ideal çevrimidir, pompa ve kazandan oluşur [1]. Notlar Carnot ve entropiyi de kapsıyor [1].", sources);

        Assert.Equal("Rankine çevrimi buhar güç santrallerinin ideal çevrimidir, pompa ve kazandan oluşur [2]. Notlar Carnot ve entropiyi de kapsıyor [1].", fixedText);
    }

    [Fact]
    public async Task Kod_modeli_ayarliysa_yazilim_cevabini_o_yazar_sohbeti_yazmaz()
    {
        var code = new FakeCodeModel();
        var h = new Harness(code: code);
        h.Chat.RouterJson = """{"sinif":"Yazilim"}""";
        await h.SendAsync("C#'ta record ile class farkı ne?");
        Assert.Equal(1, code.Calls);

        h.Chat.RouterJson = """{"sinif":"Sohbet"}""";
        await h.SendAsync("Bugün çok yoruldum");
        Assert.Equal(1, code.Calls);
    }

    private sealed class FakeCodeModel : ICodeChatModel
    {
        public int Calls { get; private set; }
        public string Name => "kod-modeli";

        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            yield return "Record değer eşitliği kullanır.";
        }
    }

    private sealed class Harness
    {
        public ScriptedChatModel Chat { get; } = new();
        public InMemoryConversations Conversations { get; } = new();
        public RecordingQueue Queue { get; } = new();
        public SimpleWebCache WebCache { get; } = new();
        public ToolHarness ToolSet { get; } = new();
        private readonly ChatOrchestrator _orchestrator;
        private readonly Guid _conversationId = Guid.NewGuid();

        public Ultron.Core.Settings.UltronSettings Settings { get; private set; } = null!;

        public ProAgent Agent { get; } = new();

        public Ultron.Core.Pro.ProService Pro { get; private set; } = null!;

        /// <summary>Cevap paketini seç (Faz 19): "1.1", "medium", "pro".</summary>
        public Task TierAsync(string tier) => AnswerTiers.SetAsync(Settings, AnswerTiers.Parse(tier));

        public Harness(bool searchFails = false, ICodeChatModel? code = null)
        {
            var embedder = new FakeEmbeddingModel([]);
            var memoryStore = new InMemoryMemoryStore();
            var categories = new InMemoryCategoryStore();
            var memory = new MemoryService(new MemoryExtractor(Chat, categories), Chat, embedder, memoryStore, categories,
                Options.Create(new MemoryOptions()), NullLogger<MemoryService>.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
            var search = new SimpleSearch(searchFails);
            var web = new WebAnswerService(Chat, search, new SimpleReader(), WebCache,
                Options.Create(new WebAnswerOptions()), time, NullLogger<WebAnswerService>.Instance);
            Settings = new Ultron.Core.Settings.UltronSettings(new InMemorySettingsStore(), time);
            var deep = new DeepWebService(Chat, search, new SimpleReader(), WebCache, embedder, web, time, NullLogger<DeepWebService>.Instance);
            Pro = new Ultron.Core.Pro.ProService(new InMemoryProStore(), Agent, embedder,
                Options.Create(new Ultron.Core.Pro.ProOptions { WorkFolder = Path.Combine(Path.GetTempPath(), "ultron-pro-test", Guid.NewGuid().ToString("N")) }),
                time, NullLogger<Ultron.Core.Pro.ProService>.Instance);
            _orchestrator = new ChatOrchestrator(Chat, Conversations, memory, Queue,
                new IntentRouter(Chat, NullLogger<IntentRouter>.Instance), web,
                new FollowUpService(Chat, NullLogger<FollowUpService>.Instance),
                ToolSet.Runner(Chat), Documents,
                new DocumentReader(Chat, DocumentStore, NullLogger<DocumentReader>.Instance),
                new ResearchService(Chat, search, new SimpleReader(), WebCache, Documents, time, NullLogger<ResearchService>.Instance),
                new Ultron.Core.Library.LibraryService(LibraryStore, null!, new FakeEmbeddingModel([]), new Ultron.Core.Settings.UltronSettings(new InMemorySettingsStore(), time), time, NullLogger<Ultron.Core.Library.LibraryService>.Instance),
                Options.Create(new ChatOptions { FollowUps = true }), time, NullLogger<ChatOrchestrator>.Instance, code,
                settings: Settings, deepWeb: deep, pro: Pro);
        }

        public InMemoryDocumentStore DocumentStore { get; } = new();

        public InMemoryLibraryStore LibraryStore { get; } = new();

        /// <summary>Sahte çıkarıcı (".pdf": "\f" ile ayrılmış sayfalar), sahte embedding; kopyalar geçici klasöre.</summary>
        public DocumentService Documents => _documents ??= new DocumentService(DocumentStore, new FakeTextExtractor(), new FakeEmbeddingModel([]),
            Options.Create(new DocumentOptions { Folder = Path.Combine(Path.GetTempPath(), "ultron-test-belgeler") }), NullLogger<DocumentService>.Instance);

        private DocumentService? _documents;

        public async Task<List<ChatEvent>> SendWithImageAsync(string text, ImageInput image)
        {
            var events = new List<ChatEvent>();
            await foreach (var e in _orchestrator.SendAsync(_conversationId, text, image))
                events.Add(e);
            return events;
        }

        public async Task<List<ChatEvent>> SendAsync(string text)
        {
            var events = new List<ChatEvent>();
            await foreach (var e in _orchestrator.SendAsync(_conversationId, text))
                events.Add(e);
            return events;
        }
    }

    private sealed class InMemoryConversations : IConversationStore
    {
        public List<Message> Messages { get; } = [];

        public Task<Conversation> StartAsync(CancellationToken cancellationToken = default) => Task.FromResult(new Conversation());

        public Task<Conversation?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromResult<Conversation?>(null);

        public Task<IReadOnlyList<Message>> GetRecentMessagesAsync(Guid conversationId, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Message>>(Messages.TakeLast(limit).ToList());

        public Task<Message> AddMessageAsync(Guid conversationId, Role role, string content, CancellationToken cancellationToken = default)
        {
            var m = new Message { Id = Messages.Count + 1, ConversationId = conversationId, Role = role, Content = content };
            Messages.Add(m);
            return Task.FromResult(m);
        }
    }

    private sealed class RecordingQueue : IMemoryQueue
    {
        public List<MemoryJob> Jobs { get; } = [];

        public void Enqueue(MemoryJob job) => Jobs.Add(job);

        public Task WaitUntilIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    internal sealed class ProAgent : Ultron.Core.Pro.IProResearcher
    {
        public string Reply { get; set; } = "<cevap>\nKuantum dolanıklığı, iki parçacığın durumlarının birbirine bağlı olmasıdır [1].\n</cevap>\n<kaynaklar>\n[1] Vikipedi — https://tr.wikipedia.org/wiki/Kuantum_dolanıklığı\n</kaynaklar>\n<kalicilik>kalici</kalicilik>";
        public bool Ok { get; set; } = true;
        public List<(string Folder, string Prompt)> Requests { get; } = [];

        public Task<Ultron.Core.Pro.ProAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new Ultron.Core.Pro.ProAgentStatus(true, "Claude", "hazır"));

        public Task<Ultron.Core.Pro.ProAgentResult> RunAsync(string folder, string prompt, Action<string> progress, CancellationToken cancellationToken = default)
        {
            Requests.Add((folder, prompt));
            progress("Arıyor: kuantum dolanıklığı");
            return Task.FromResult(new Ultron.Core.Pro.ProAgentResult(Ok, Ok ? Reply : ""));
        }
    }

    internal sealed class InMemoryProStore : Ultron.Core.Pro.IProAnswerStore
    {
        private readonly List<Ultron.Core.Pro.ProAnswer> _items = [];

        public Task<Ultron.Core.Pro.ProAnswer> AddAsync(Ultron.Core.Pro.ProAnswer answer, CancellationToken cancellationToken = default)
        {
            var copy = new Ultron.Core.Pro.ProAnswer
            {
                Id = _items.Count + 1, Question = answer.Question, Answer = answer.Answer, Sources = answer.Sources, Embedding = answer.Embedding,
                Lifetime = answer.Lifetime, ExpiresAtUtc = answer.ExpiresAtUtc, CreatedAtUtc = answer.CreatedAtUtc, Origin = answer.Origin,
            };
            _items.Add(copy);
            return Task.FromResult(copy);
        }

        public Task UpdateAsync(Ultron.Core.Pro.ProAnswer answer, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Ultron.Core.Pro.ProAnswer>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Ultron.Core.Pro.ProAnswer>>(_items.Where(a => a.IsActive).ToList());

        public Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken = default)
        {
            if (_items.FirstOrDefault(a => a.Id == id) is not { } a)
                return Task.FromResult(false);
            a.IsActive = false;
            return Task.FromResult(true);
        }
    }

    private sealed class SimpleSearch(bool fails) : IWebSearch
    {
        public Task<IReadOnlyList<WebResult>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            fails ? throw new HttpRequestException("SearXNG kapalı")
                  : Task.FromResult<IReadOnlyList<WebResult>>([new("A", "https://a.com", "41,25 TL")]);
    }

    private sealed class SimpleReader : IWebPageReader
    {
        public Task<string?> ReadTextAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult<string?>("1 USD = 41,25 TL");
    }

    internal sealed class SimpleWebCache : IWebCacheStore
    {
        public List<WebCacheEntry> Entries { get; } = [];

        public Task<WebCacheEntry?> FindValidAsync(string cacheKey, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.LastOrDefault(e => e.CacheKey == cacheKey));

        public Task AddAsync(WebCacheEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public List<string> Outgoing { get; } = [];

        public Task LogOutgoingAsync(string query, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            lock (Outgoing)
                Outgoing.Add(query);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutgoingQuery>> GetRecentOutgoingAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutgoingQuery>>([]);
    }
}
