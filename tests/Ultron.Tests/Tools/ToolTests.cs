using System.Text.Json;
using Ultron.Core.Chat;
using Ultron.Core.Reminders;
using Ultron.Core.Routing;
using Ultron.Core.Tools;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Tools;

public class ToolRunnerTests
{
    private static ToolCall Call(string name, string json) => new(name, JsonDocument.Parse(json).RootElement);

    private static readonly IReadOnlyList<ChatMessage> Ask = [new(Role.System, "s"), new(Role.User, "u")];

    [Fact]
    public async Task Arac_sonucu_modele_geri_verilir_ve_denetim_kaydina_yazilir()
    {
        var h = new ToolHarness();
        var chat = new ScriptedChatModel();
        chat.ToolTurns.Enqueue(new ToolTurn("", [Call("hesapla", """{"ifade":"1250*12"}""")]));
        chat.ToolTurns.Enqueue(new ToolTurn("15.000 eder.", []));

        var result = await h.Runner(chat).RunAsync(Ask);

        Assert.Equal("15.000 eder.", result.Answer);
        // İkinci tur devam turu: araç sonucu + "kalan iş var mı" notu (tek mesajda birden çok istek, 2026-10-27).
        var second = chat.ToolRequests[1].Messages;
        Assert.Equal(ToolRunner.ContinueNote, second[^1].Content);
        Assert.Equal(Role.Tool, second[^2].Role);
        Assert.Equal("hesapla", second[^2].ToolName);
        Assert.Contains("15.000", second[^2].Content);
        Assert.Single(second[^3].ToolCalls!);
        var audit = Assert.Single(h.Audit.Entries);
        Assert.True(audit.Ok);
        Assert.Equal("hesapla", audit.Tool);
    }

    [Fact]
    public async Task Arac_cagrilmadan_yaptim_denirse_cevap_engellenir()
    {
        var chat = new ScriptedChatModel();
        chat.ToolTurns.Enqueue(new ToolTurn("Hatırlatıcıyı kurdum, yarın 9'da hatırlatacağım.", []));
        chat.ToolTurns.Enqueue(new ToolTurn("Tamam, kuruyorum.", []));

        var result = await new ToolHarness().Runner(chat).RunAsync(Ask);

        Assert.Equal(ToolRunner.NotDoneAnswer, result.Answer);
        // Önce bir kez "aracı çağır" diye uyarıldı.
        Assert.Equal(ToolRunner.NudgeMessage, chat.ToolRequests[1].Messages[^1].Content);
    }

    [Fact]
    public async Task Uyarilinca_araci_cagirirsa_islem_yapilir()
    {
        var h = new ToolHarness();
        var chat = new ScriptedChatModel();
        chat.ToolTurns.Enqueue(new ToolTurn("Hesaplamayı yapıyorum.", []));
        chat.ToolTurns.Enqueue(new ToolTurn("", [Call("hesapla", """{"ifade":"2+2"}""")]));
        chat.ToolTurns.Enqueue(new ToolTurn("4 eder.", []));

        var result = await h.Runner(chat).RunAsync(Ask);

        Assert.Equal("4 eder.", result.Answer);
        Assert.True(Assert.Single(result.Uses).Ok);
    }

    [Fact]
    public async Task Bilgi_sormak_serbesttir()
    {
        var chat = new ScriptedChatModel();
        chat.ToolTurns.Enqueue(new ToolTurn("Saat kaçta hatırlatayım?", []));

        var result = await new ToolHarness().Runner(chat).RunAsync(Ask);

        Assert.Equal("Saat kaçta hatırlatayım?", result.Answer);
    }

    [Fact]
    public async Task Olmayan_arac_ve_donguye_giren_model_guvenle_biter()
    {
        var chat = new ScriptedChatModel();
        for (var i = 0; i < 5; i++)
            chat.ToolTurns.Enqueue(new ToolTurn("", [Call("dosya_sil", "{}")]));

        var result = await new ToolHarness().Runner(chat).RunAsync(Ask);

        // Devam turunda aynı çağrı tekrar gelince döngü kesilir; son tur araçsız özet (gelen çağrılar yok sayılır).
        Assert.False(Assert.Single(result.Uses).Ok);
        Assert.Equal(3, chat.ToolRequests.Count);
        Assert.Empty(chat.ToolRequests[^1].Tools);
        Assert.Equal(ToolRunner.NotDoneAnswer.Length > 0, result.Answer.Length > 0);
    }

    [Fact]
    public async Task Tek_mesajdaki_birden_cok_istek_ve_birbirine_bagli_isler_yapilir()
    {
        var h = new ToolHarness();
        var chat = new ScriptedChatModel();
        // 1. tur: iki bağımsız iş birlikte; 2. tur (devam): ilk sonuca bağlı iş; 3. tur: kalan iş yok; sonra özet.
        chat.ToolTurns.Enqueue(new ToolTurn("", [Call("hesapla", """{"ifade":"2+2"}"""), Call("hesapla", """{"ifade":"10*3"}""")]));
        chat.ToolTurns.Enqueue(new ToolTurn("", [Call("hesapla", """{"ifade":"4+30"}""")]));
        chat.ToolTurns.Enqueue(new ToolTurn("", []));
        chat.ToolTurns.Enqueue(new ToolTurn("2+2=4, 10*3=30, toplamları 34.", []));

        var result = await h.Runner(chat).RunAsync(Ask);

        Assert.Equal(3, result.Uses.Count);
        Assert.Equal(3, h.Audit.Entries.Count);
        Assert.Equal("2+2=4, 10*3=30, toplamları 34.", result.Answer);
        // Özet turu araçsız ve devam notları olmadan.
        var summary = chat.ToolRequests[^1];
        Assert.Empty(summary.Tools);
        Assert.DoesNotContain(summary.Messages, m => m.Content == ToolRunner.ContinueNote);
    }

    [Theory]
    [InlineData("Tamam, hatırlatıcıyı kurdum.", true)]
    [InlineData("İptal ettim.", true)]
    [InlineData("Yarın saat kaçta?", false)]
    [InlineData("Bugün İstanbul'da hava 17 derece.", false)]
    public void Islem_iddiasi_taninir(string answer, bool claims) => Assert.Equal(claims, ToolRunner.ClaimsAction(answer));
}

public class ReminderTests
{
    [Fact]
    public async Task Hatirlatici_kurulur_listelenir_iptal_edilir()
    {
        var h = new ToolHarness();
        var tools = h.Tools;
        var set = await tools.Single(t => t.Definition.Name == "hatirlatici_kur")
            .ExecuteAsync(Json("""{"metin":"Annemi ara","zaman":"2026-10-27T09:00","tekrar":"yok","onem":"onemsiz"}"""));
        Assert.True(set.Ok);
        Assert.Contains("27 Ekim", set.ForModel);

        var list = await tools.Single(t => t.Definition.Name == "hatirlaticilari_listele").ExecuteAsync(Json("{}"));
        Assert.Contains("#1", list.ForModel);
        Assert.Contains("Annemi ara", list.ForModel);

        var cancel = await tools.Single(t => t.Definition.Name == "hatirlatici_iptal").ExecuteAsync(Json("""{"numara":"1"}"""));
        Assert.True(cancel.Ok);
        Assert.Empty(await h.Reminders.GetPendingAsync());
        Assert.False((await tools.Single(t => t.Definition.Name == "hatirlatici_iptal").ExecuteAsync(Json("""{"numara":1}"""))).Ok);
    }

    [Theory]
    [InlineData("2026-10-26T09:00")] // bugün ama geçmiş saat (şu an 12:00)
    [InlineData("2025-10-27T09:00")] // model yanlış yıl yazdı
    [InlineData("yarın sabah")]
    public async Task Gecmis_ya_da_anlasilmaz_zaman_reddedilir(string when)
    {
        var h = new ToolHarness();

        var result = await new SetReminderTool(h.Reminders).ExecuteAsync(Json($$"""{"metin":"x","zaman":"{{when}}"}"""));

        Assert.False(result.Ok);
        Assert.Empty(h.Store.Items);
    }

    [Theory]
    [InlineData("2026-10-27T09:00:00")]
    [InlineData("2026-10-27 09:00")]
    [InlineData("2026-10-27T09:00Z")]
    [InlineData("2026-10-27T09:00:00+03:00")]
    public void Zaman_bicim_toleransi(string when)
    {
        Assert.True(SetReminderTool.TryParseLocal(when, out var local));
        Assert.Equal(new DateTime(2026, 10, 27, 9, 0, 0), local);
    }

    [Fact]
    public async Task Zamani_gelen_caldirilir_kapaliyken_gecen_kacirildi_sayilir_tekrarli_ileri_alinir()
    {
        var h = new ToolHarness();
        await h.Reminders.AddAsync("Su iç", new DateTime(2026, 10, 26, 12, 0, 30), ReminderRecurrence.Yok);
        await h.Reminders.AddAsync("İlaç", new DateTime(2026, 10, 26, 8, 0, 0), ReminderRecurrence.HerGun);
        await h.Reminders.AddAsync("Toplantı", new DateTime(2026, 10, 27, 10, 0, 0), ReminderRecurrence.Yok);

        h.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(2, await h.Reminders.FireDueAsync());

        Assert.False(h.Notifier.Notified.Single(n => n.Text == "Su iç").Missed);
        Assert.True(h.Notifier.Notified.Single(n => n.Text == "İlaç").Missed);
        Assert.Equal(ReminderStatus.Calindi, h.Store.Items[0].Status);
        Assert.Equal(new DateTime(2026, 10, 27, 8, 0, 0), h.Store.Items[1].DueAtUtc); // her gün: yarın 08:00
        Assert.Equal(ReminderStatus.Bekliyor, h.Store.Items[1].Status);
        Assert.Equal(0, await h.Reminders.FireDueAsync()); // ikinci bakışta tekrar çalmaz
    }

    [Theory]
    [InlineData(ReminderRecurrence.HaftaIci, "2026-10-30T09:00", "2026-11-02T09:00")] // cuma → pazartesi
    [InlineData(ReminderRecurrence.HerHafta, "2026-10-26T09:00", "2026-11-02T09:00")]
    [InlineData(ReminderRecurrence.HerGun, "2026-10-26T09:00", "2026-10-27T09:00")]
    public void Tekrar_kurallari(ReminderRecurrence recurrence, string due, string expected)
    {
        var d = DateTime.Parse(due);
        Assert.Equal(DateTime.Parse(expected), ReminderSchedule.Next(d, recurrence, d));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;
}

public class OtherToolTests
{
    [Theory]
    [InlineData("(1250*12)/7", "2.142,857143")]
    [InlineData("2,5*4", "10")]
    [InlineData("250*0.18", "45")]
    [InlineData("5/2", "2,5")]
    public async Task Hesap_makinesi(string expression, string expected)
    {
        var result = await new CalculatorTool().ExecuteAsync(JsonDocument.Parse($$"""{"ifade":"{{expression}}"}""").RootElement);

        Assert.True(result.Ok, result.ForModel);
        Assert.EndsWith("= " + expected, result.ForModel);
    }

    [Theory]
    [InlineData("System.IO.File.Delete('x')")]
    [InlineData("1/0")]
    [InlineData("")]
    public async Task Hesap_makinesi_kod_ve_tanimsizi_reddeder(string expression)
    {
        var result = await new CalculatorTool().ExecuteAsync(JsonDocument.Parse($$"""{"ifade":"{{expression}}"}""").RootElement);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Hava_araci_varsayilan_sehri_kullanir_ve_turkce_yazar()
    {
        var h = new ToolHarness();

        var result = await new WeatherTool(h.Weather).ExecuteAsync(JsonDocument.Parse("{}").RootElement);

        Assert.Null(Assert.Single(h.Weather.Requests));
        Assert.Contains("İstanbul", result.ForModel);
        Assert.Contains("17°C", result.ForModel);
        Assert.Contains("parçalı bulutlu", result.ForModel);
        Assert.Contains("%20", result.ForModel);
    }

    [Theory]
    [InlineData(MessageIntent.Komut, "Spotify'ı aç", 9)] // bilgisayar araçları (Faz 12)
    [InlineData(MessageIntent.Komut, "Işıkları kıs", 0)] // grup yok ama komut: tüm araçlar
    [InlineData(MessageIntent.Kisisel, "Hatırlatmalarım neler?", 3)]
    [InlineData(MessageIntent.Komut, "Brifingi 8'e al", 3)]
    [InlineData(MessageIntent.Kisisel, "Salı 13-15 Akışkanlar dersim var", 3)]
    [InlineData(MessageIntent.Guncel, "Bugün neler var?", 1)]
    [InlineData(MessageIntent.Komut, "Varsayılan şehrimi Ankara yap", 2)]
    [InlineData(MessageIntent.Guncel, "İstanbul'da yarın yağmur yağacak mı?", 1)]
    [InlineData(MessageIntent.Sohbet, "Bugün hava çok güzel değil mi", 1)]
    [InlineData(MessageIntent.Guncel, "Dolar kaç TL?", -1)]
    [InlineData(MessageIntent.GenelBilgi, "Entropi nedir?", -1)]
    [InlineData(MessageIntent.GenelBilgi, "Oksijenin kısmi basıncı deniz seviyesinde kaç kPa? Kısaca hesapla.", -1)] // sayı yok: bilgi sorusu (tur 7)
    [InlineData(MessageIntent.Komut, "1250*12 hesapla", 1)]
    [InlineData(MessageIntent.Kisisel, "Sevgilime ne hediye alabilirim?", -1)]
    [InlineData(MessageIntent.GenelBilgi, "Kışın evi ısıttığımızda neden içerideki hava kuru gelir?", -1)] // ders sorusu, hava durumu değil
    [InlineData(MessageIntent.GenelBilgi, "Hava 300 K'den başlayarak 1:8 basınç oranında izantropik sıkıştırılıyor (k = 1,4). Çıkış sıcaklığı yaklaşık kaç K?", -1)]
    public void Arac_secimi(MessageIntent intent, string text, int expected)
    {
        var selected = ToolRouting.Select(intent, text);
        Assert.Equal(expected, selected?.Count ?? -1);
    }
}

public class TextToolCallParserTests
{
    private static readonly IReadOnlyList<ToolDefinition> Offered =
        new ToolHarness().Tools.Select(t => t.Definition).ToList();

    [Fact]
    public void Metin_olarak_yazilan_cagri_ayristirilir()
    {
        var calls = Ultron.Infrastructure.Ollama.TextToolCallParser.Parse(
            """hatirlatici_kur(metin="Annemi ara (mutlaka)", tekrar="hafta_ici", zaman="2026-10-28T08:00")""", Offered);

        var call = Assert.Single(calls);
        Assert.Equal("hatirlatici_kur", call.Name);
        Assert.Equal("Annemi ara (mutlaka)", call.Arguments.GetProperty("metin").GetString());
        Assert.Equal("2026-10-28T08:00", call.Arguments.GetProperty("zaman").GetString());
    }

    [Theory]
    [InlineData("hatirlaticilari_listele()", "hatirlaticilari_listele")]
    [InlineData("```tool_code\nhatirlatici_iptal(numara=1)\n```", "hatirlatici_iptal")]
    [InlineData("hesapla(ifade=\"(1250*12)/7\")", "hesapla")]
    public void Farkli_bicimler(string content, string name)
    {
        var call = Assert.Single(Ultron.Infrastructure.Ollama.TextToolCallParser.Parse(content, Offered));
        Assert.Equal(name, call.Name);
    }

    [Theory]
    [InlineData("Tamam, yarın 9'da hatırlatacağım.")]
    [InlineData("dosya_sil(yol=\"C:/\")")] // sunulmayan araç
    [InlineData("Hava güzel (bugün 17 derece).")]
    public void Normal_metin_cagri_sayilmaz(string content) =>
        Assert.Empty(Ultron.Infrastructure.Ollama.TextToolCallParser.Parse(content, Offered));

    [Fact]
    public void Sayi_parametresi_sayi_olur()
    {
        var call = Assert.Single(Ultron.Infrastructure.Ollama.TextToolCallParser.Parse("hatirlatici_iptal(numara=3)", Offered));
        Assert.Equal(3, call.Arguments.GetProperty("numara").GetInt64());
    }
}

public class ReminderPhrasingTests
{
    [Fact]
    public async Task Hatirlatma_samimi_cumleyle_soylenir()
    {
        var h = new ToolHarness();
        await h.Reminders.AddAsync("Su iç", new DateTime(2026, 10, 26, 12, 0, 30), ReminderRecurrence.Yok);
        h.Time.Advance(TimeSpan.FromMinutes(1));

        await h.Reminders.FireDueAsync();

        Assert.Equal("Patron, hatırlatmamı istemiştin, su içmen gerekiyor.", Assert.Single(h.Notifier.Notified).Spoken);
    }

    [Fact]
    public async Task Model_cevap_veremezse_hazir_cumle_kullanilir()
    {
        var h = new ToolHarness();
        h.Phraser.ReminderPhraseJson = null;
        await h.Reminders.AddAsync("Su iç", new DateTime(2026, 10, 26, 9, 0, 0), ReminderRecurrence.Yok);

        await h.Reminders.FireDueAsync();

        Assert.Equal("Sen yokken bir hatırlatman vardı: Su iç.", Assert.Single(h.Notifier.Notified).Spoken);
    }

    [Fact]
    public void Hazir_cumle_hitap_ayarliysa_onunla_baslar()
    {
        Assert.Equal("Ece, hatırlatmamı istemiştin: Su iç.", ReminderPhrasing.Fallback("Su iç", missed: false, "Ece"));
        Assert.Equal("İlacını içme vakti", Ultron.Core.Settings.UltronSettings.Addressed(null, "ilacını içme vakti")); // Türkçe büyük İ
        Assert.Contains("\"Ece\" diye hitap et", ReminderPhrasing.Prompt("Ece"));
        Assert.Contains("İsimle hitap etme", ReminderPhrasing.Prompt(""));
    }

    [Fact]
    public async Task Hitap_ayari_dogrulanir_ve_ana_talimata_girer()
    {
        var h = new ToolHarness();
        var key = Ultron.Core.Settings.UltronSettings.UserAddress;

        Assert.Equal("", await h.Settings.GetAsync(key)); // varsayılan: isimle hitap yok
        Assert.Null(await h.Settings.SetAsync(key, "\"Ece\""));
        Assert.Equal("Ece", await h.Settings.GetAsync(key));
        Assert.NotNull(await h.Settings.SetAsync(key, "<b>Ece</b>")); // harf dışı karakter reddedilir
        Assert.Null(await h.Settings.SetAsync(key, "yok"));
        Assert.Equal("", await h.Settings.GetAsync(key));

        Assert.Contains("\"Ece\" diye seslenmeni", SystemPrompt.Build(DateTime.Now, address: "Ece"));
        Assert.Contains("ismiyle hitap etmezsin", SystemPrompt.Build(DateTime.Now));
    }
}

public class ReminderPriorityTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Onem_soylenmezse_kurulmaz_sorulmasi_istenir()
    {
        var h = new ToolHarness();

        var result = await new SetReminderTool(h.Reminders).ExecuteAsync(Json("""{"metin":"Su iç","zaman":"2026-10-26T13:00"}"""));

        Assert.False(result.Ok);
        Assert.Contains("BİLGİ GEREKLİ", result.ForModel);
        Assert.Contains("çok önemli", result.ForModel);
        Assert.Empty(h.Store.Items);
    }

    [Fact]
    public async Task Secilen_onem_hatirlanir_ayni_is_icin_tekrar_sorulmaz()
    {
        var h = new ToolHarness();
        var tool = new SetReminderTool(h.Reminders);

        Assert.True((await tool.ExecuteAsync(Json("""{"metin":"Su iç","zaman":"2026-10-26T13:00","onem":"cok_onemli"}"""))).Ok);
        var second = await tool.ExecuteAsync(Json("""{"metin":"su İÇ","zaman":"2026-10-26T15:00"}"""));

        Assert.True(second.Ok);
        Assert.Contains("daha önce seçtiği", second.ForModel);
        Assert.All(h.Store.Items, r => Assert.Equal(ReminderPriority.CokOnemli, r.Priority));
    }

    [Fact]
    public async Task Farkli_is_icin_kayitli_tercihler_modele_gosterilir()
    {
        var h = new ToolHarness();
        await h.Reminders.RememberPreferenceAsync("Su iç", ReminderPriority.Onemli);

        var result = await new SetReminderTool(h.Reminders).ExecuteAsync(Json("""{"metin":"Su içmeyi unutma","zaman":"2026-10-26T13:00"}"""));

        Assert.False(result.Ok);
        Assert.Contains("'Su iç' → önemli", result.ForModel);
    }

    [Theory]
    [InlineData(ReminderPriority.CokOnemli, 10)]
    [InlineData(ReminderPriority.Onemli, 600)]
    public async Task Onemli_hatirlatma_okudum_denene_kadar_tekrar_eder(ReminderPriority priority, int seconds)
    {
        var h = new ToolHarness();
        var r = await h.Reminders.AddAsync("Su iç", new DateTime(2026, 10, 26, 12, 0, 0), ReminderRecurrence.Yok, priority);

        await h.Reminders.FireDueAsync();
        h.Time.Advance(TimeSpan.FromSeconds(seconds - 1));
        await h.Reminders.FireDueAsync();
        Assert.Single(h.Notifier.Notified); // henüz tekrar zamanı değil

        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.Reminders.FireDueAsync();
        Assert.Equal(2, h.Notifier.Notified.Count);
        Assert.True(h.Notifier.Notified[1].Repeat);
        Assert.Equal(h.Notifier.Notified[0].Spoken, h.Notifier.Notified[1].Spoken); // cümle yeniden üretilmez

        Assert.True(await h.Reminders.AcknowledgeAsync(r.Id));
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.Reminders.FireDueAsync();
        Assert.Equal(2, h.Notifier.Notified.Count);
        Assert.Equal(ReminderStatus.Calindi, r.Status);
    }

    [Fact]
    public async Task Onemsiz_bir_kez_gosterilir_tekrarli_onemli_okununca_sonraki_gune_gecer()
    {
        var h = new ToolHarness();
        var once = await h.Reminders.AddAsync("Çiçek sula", new DateTime(2026, 10, 26, 12, 0, 0), ReminderRecurrence.Yok);
        var daily = await h.Reminders.AddAsync("İlaç", new DateTime(2026, 10, 26, 12, 0, 0), ReminderRecurrence.HerGun, ReminderPriority.Onemli);

        await h.Reminders.FireDueAsync();
        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.Reminders.FireDueAsync();

        Assert.Single(h.Notifier.Notified, n => n.Text == "Çiçek sula");
        Assert.Equal(ReminderStatus.Calindi, once.Status);
        Assert.Equal(ReminderStatus.Caliyor, daily.Status);
        Assert.True(await h.Reminders.AcknowledgeAsync(daily.Id));
        Assert.Equal(ReminderStatus.Bekliyor, daily.Status);
        Assert.Equal(new DateTime(2026, 10, 27, 12, 0, 0), daily.DueAtUtc);
        Assert.False(await h.Reminders.AcknowledgeAsync(once.Id));
    }

    [Theory]
    [InlineData("çok önemli", true)]
    [InlineData("önemsiz", true)]
    [InlineData("tamam", false)]
    public void Onem_cevabi_arac_turuna_gider(string answer, bool tools) =>
        Assert.Equal(tools, ToolRouting.Select(MessageIntent.Sohbet, answer, "Bu hatırlatma ne kadar önemli? Önemsiz, önemli ya da çok önemli?") is not null);
}

public class ToolExpectationTests
{
    [Theory]
    [InlineData("Yarın 10'da ilacımı içmemi hatırlat, önemli", true)]
    [InlineData("20 dakika sonra çayı hatırlat", true)]
    [InlineData("Hatırlatmalarım neler?", false)]
    [InlineData("1 numaralı hatırlatıcıyı iptal et", false)]
    [InlineData("Bana bir şey hatırlat", false)]
    public void Acik_hatirlatma_istegi_taninir(string text, bool expected) => Assert.Equal(expected, ToolRouting.ExpectsToolCall(text));

    [Fact]
    public async Task Kullanici_onemden_bahsetmediyse_modelin_tahmini_yok_sayilir()
    {
        var h = new ToolHarness();
        ToolRunContext.UserText = "Bugün 23:30'da kitap okumamı hatırlat";
        try
        {
            var result = await new SetReminderTool(h.Reminders).ExecuteAsync(
                JsonDocument.Parse("""{"metin":"Kitap oku","zaman":"2026-10-26T23:30","onem":"onemli"}""").RootElement);

            Assert.False(result.Ok);
            Assert.Contains("BİLGİ GEREKLİ", result.ForModel);
        }
        finally
        {
            ToolRunContext.UserText = null;
        }
    }

    [Fact]
    public async Task Arac_calisip_cevap_bos_kalirsa_arac_sonucu_soylenir()
    {
        var chat = new ScriptedChatModel();
        chat.ToolTurns.Enqueue(new ToolTurn("", [new ToolCall("hesapla", JsonDocument.Parse("""{"ifade":"2+2"}""").RootElement)]));
        chat.ToolTurns.Enqueue(new ToolTurn("", [])); // devam turu: başka iş yok
        chat.ToolTurns.Enqueue(new ToolTurn("", [])); // özet turu da boş döndü

        var result = await new ToolHarness().Runner(chat).RunAsync([new(Role.User, "2+2 hesapla")]);

        Assert.Contains("= 4", result.Answer);
    }
}

public class ToolPlannerTests
{
    private static readonly IReadOnlyList<ToolDefinition> Offered = new ToolHarness().Tools.Select(t => t.Definition).ToList();

    [Fact]
    public void Karar_arac_cagrisina_cevrilir_anahtarlar_temizlenir()
    {
        var turn = Ultron.Infrastructure.Ollama.ToolPlanner.Parse(
            """{"arac":"hatirlatici_kur","parametreler":{"metin":"Su iç","zaman":"2026-10-28T09:00","önem?":"onemli"}}""", Offered);

        var call = Assert.Single(turn.Calls);
        Assert.Equal("onemli", call.Arguments.GetProperty("onem").GetString());
    }

    [Fact]
    public void Yok_karari_soru_olarak_doner()
    {
        var turn = Ultron.Infrastructure.Ollama.ToolPlanner.Parse("""{"arac":"yok","parametreler":{},"soru":"Saat kaçta olsun?"}""", Offered);

        Assert.Empty(turn.Calls);
        Assert.Equal("Saat kaçta olsun?", turn.Content);
    }

    [Fact]
    public void Birden_cok_islem_okunur_tekrarlanan_ve_bilinmeyen_atlanir()
    {
        var turn = Ultron.Infrastructure.Ollama.ToolPlanner.Parse("""
            {"islemler":[
              {"arac":"hatirlatici_kur","parametreler":{"metin":"İlaç","zaman":"2026-10-28T09:00"}},
              {"arac":"hatirlatici_kur","parametreler":{"metin":"Spor","zaman":"2026-10-28T18:00"}},
              {"arac":"hatirlatici_kur","parametreler":{"metin":"Spor","zaman":"2026-10-28T18:00"}},
              {"arac":"uydurma_arac","parametreler":{}},
              {"arac":"hava_durumu","parametreler":{}}],
             "soru":""}
            """, Offered);
        Assert.Equal(["hatirlatici_kur", "hatirlatici_kur", "hava_durumu"], turn.Calls.Select(c => c.Name));
    }

    [Fact]
    public void Islem_yoksa_soru_cevap_olur()
    {
        var turn = Ultron.Infrastructure.Ollama.ToolPlanner.Parse("""{"islemler":[],"soru":"Saat kaçta olsun?"}""", Offered);
        Assert.Empty(turn.Calls);
        Assert.Equal("Saat kaçta olsun?", turn.Content);
    }

    [Fact]
    public void Sema_sadece_sunulan_araclari_kabul_eder_ve_istem_parametreleri_anlatir()
    {
        var subset = Offered.Where(t => t.Name.StartsWith("ders")).ToList();
        var schema = Ultron.Infrastructure.Ollama.ToolPlanner.Schema(subset);
        var messages = Ultron.Infrastructure.Ollama.ToolPlanner.BuildMessages([new(Role.System, "S"), new(Role.User, "u")], subset);

        Assert.Contains("\"ders_ekle\"", schema);
        Assert.DoesNotContain("hatirlatici_kur", schema);
        Assert.Contains("\"islemler\"", schema);
        Assert.Contains("gun (zorunlu; pazartesi|sali", messages[0].Content);
        Assert.Contains("yer (isteğe bağlı", messages[0].Content);
    }
}

public class NeedInfoTests
{
    [Fact]
    public async Task Arac_bilgi_isterse_model_yaptim_dese_bile_soru_sorulur()
    {
        var chat = new ScriptedChatModel();
        chat.ToolTurns.Enqueue(new ToolTurn("", [new ToolCall("hatirlatici_kur",
            JsonDocument.Parse("""{"metin":"Su iç","zaman":"2026-10-27T09:00"}""").RootElement)]));
        chat.ToolTurns.Enqueue(new ToolTurn("Tamam, hatırlatıcıyı kurdum.", []));

        var result = await new ToolHarness().Runner(chat).RunAsync([new(Role.User, "Yarın 9'da su içmemi hatırlat")]);

        Assert.Equal(SetReminderTool.PriorityQuestion, result.Answer);
    }
}

public class CancelByTextTests
{
    [Fact]
    public async Task Numara_yerine_metinle_iptal_birden_cok_eslesmede_numara_istenir()
    {
        var h = new ToolHarness();
        await h.Reminders.AddAsync("Spor yap", h.Time.GetLocalNow().DateTime.AddHours(5), ReminderRecurrence.Yok, ReminderPriority.Onemli);
        await h.Reminders.AddAsync("İlacımı iç", h.Time.GetLocalNow().DateTime.AddHours(6), ReminderRecurrence.Yok, ReminderPriority.Onemli);
        await h.Reminders.AddAsync("İlaç kutusunu al", h.Time.GetLocalNow().DateTime.AddHours(7), ReminderRecurrence.Yok, ReminderPriority.Onemli);
        var tool = new CancelReminderTool(h.Reminders);

        // Model numara alanına metin yazdı ("spor hatırlatıcısı numarası").
        var ok = await tool.ExecuteAsync(JsonDocument.Parse("""{"numara":"spor hatırlatıcısı numarası"}""").RootElement);
        Assert.True(ok.Ok);
        Assert.DoesNotContain(await h.Reminders.GetPendingAsync(), r => r.Text == "Spor yap");

        var two = await tool.ExecuteAsync(JsonDocument.Parse("""{"metin":"ilaç"}""").RootElement);
        Assert.False(two.Ok);
        Assert.Contains("Birden fazla", two.ForModel);
    }
}

public class BuildRequestTests
{
    [Theory]
    [InlineData("Flutter ile not alma uygulaması yaz: giriş ekranı, notlar listesi", true)]
    [InlineData("Bana Next.js ile e-ticaret sitesi yapar mısın", true)]
    [InlineData("Telegram botu geliştir", true)]
    [InlineData("React bileşenim her yazı yazdığımda tüm sayfayı yeniden çiziyor", false)]
    [InlineData("React'te bir butona her basıldığında sayacı 1 artıran basit bir bileşen yaz.", false)] // küçük kod: sohbette yazılır
    [InlineData("Bana bir login sayfası yazar mısın", true)]
    [InlineData("Dün projemi yaptım, çok yoruldum", false)]
    [InlineData("Spotify uygulamasını aç", false)]
    [InlineData("Bu kodu açıklar mısın", false)]
    public void Yazilim_uretme_istegi(string text, bool expected) =>
        Assert.Equal(expected, Ultron.Core.Tools.ToolRouting.IsBuildRequest(Ultron.Core.Text.TurkishText.Normalize(text)));

    [Fact]
    public void Uretme_isteginde_sadece_kodlama_araclari_sunulur() =>
        Assert.Equal(["kodlama_baslat", "kodlama_durum", "kodlama_cevapla", "kodlama_iptal"],
            Ultron.Core.Tools.ToolRouting.Select(Ultron.Core.Routing.MessageIntent.Komut, "Flutter ile not alma uygulaması yaz")!);

    [Theory]
    [InlineData("Uygulamam lokalde hızlı ama production'da yavaş, nereden başlarım?", false)]
    [InlineData("Spotify uygulamasını aç", true)]
    [InlineData("notlar.txt dosyasında ne yazıyor?", true)]
    [InlineData("Spotify'ı izinli uygulamalara ekle", true)]
    public void Bilgi_sorusunda_uygulama_kelimesi_bilgisayar_aracini_acmaz(string text, bool computer) =>
        Assert.Equal(computer, Ultron.Core.Tools.ToolRouting.Select(Ultron.Core.Routing.MessageIntent.Sohbet, text)?.Contains("dosya_oku") == true);
}
