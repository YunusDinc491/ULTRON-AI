using System.Text.Json;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Ultron.Core.Computer;
using Ultron.Core.Routing;
using Ultron.Core.Tools;
using Ultron.Infrastructure.Computer;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Computer;

/// <summary>Faz 21: pencereler, ses/medya/güç, dosya düzenleme (plan, geri al, PDF), Ultron tarayıcısının izin kuralları, yönlendirme.</summary>
public sealed class Faz21Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ultron-faz21-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 20, 0, 0, TimeSpan.Zero));

    public Faz21Tests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    // ---- Pencereler ----

    private static readonly WindowInfo[] Windows =
    [
        new(1, "Termodinamik - Hafta 5.pdf - Microsoft Edge", "msedge", false),
        new(2, "Ultron - Visual Studio Code", "Code", false),
        new(3, "Spotify Premium", "Spotify", true),
        new(4, "Belge1 - Word", "WINWORD", false),
        new(5, "Belge2 - Word", "WINWORD", false),
    ];

    [Theory]
    [InlineData("vs code", 2)]
    [InlineData("spotify'ı", 3)]
    [InlineData("termodinamik pdf'i", 1)]
    [InlineData("edge penceresini", 1)]
    public void Pencere_adla_ya_da_basliktaki_kelimeyle_bulunur(string query, long handle)
    {
        Assert.Equal(handle, Assert.Single(WindowMatch.Find(Windows, query)).Handle);
    }

    [Fact]
    public void Ayni_uygulamanin_onundeki_pencere_secilir_bilinmeyen_bos_doner()
    {
        Assert.Equal(4, Assert.Single(WindowMatch.Find(Windows, "word")).Handle);
        Assert.Empty(WindowMatch.Find(Windows, "photoshop"));
    }

    [Fact]
    public async Task Pencere_kapatma_onayli_digerleri_bildirerek()
    {
        var tool = new WindowTool(new FakeDesktop());

        Assert.Equal(PermissionLevel.Onayli, await tool.LevelForAsync(Args("""{"islem":"kapat","pencere":"word"}""")));
        Assert.Equal(PermissionLevel.Bildirerek, await tool.LevelForAsync(Args("""{"islem":"one_getir","pencere":"word"}""")));
        var result = await tool.ExecuteAsync(Args("""{"islem":"one_getir","pencere":"vs code"}"""));
        Assert.True(result.Ok);
    }

    // ---- Ses, medya, güç ----

    [Fact]
    public async Task Ses_adimla_degisir_ve_sinirda_kalir()
    {
        var desktop = new FakeDesktop { Volume = 95 };
        var tool = new VolumeTool(desktop);

        await tool.ExecuteAsync(Args("""{"islem":"ac"}"""));
        Assert.Equal(100, desktop.Volume);
        await tool.ExecuteAsync(Args("""{"islem":"ayarla","deger":35}"""));
        Assert.Equal(35, desktop.Volume);
        await tool.ExecuteAsync(Args("""{"islem":"kapat"}"""));
        Assert.True(desktop.Muted);
    }

    [Theory]
    [InlineData("kilitle", PermissionLevel.Bildirerek)]
    [InlineData("uyku", PermissionLevel.Onayli)]
    [InlineData("kapat", PermissionLevel.Onayli)]
    public async Task Guc_islemlerinden_sadece_kilitleme_onaysiz(string action, PermissionLevel expected)
    {
        Assert.Equal(expected, await new PowerTool(new FakeDesktop()).LevelForAsync(Args($$"""{"islem":"{{action}}"}""")));
    }

    private sealed class FakeDesktop : IDesktop
    {
        public int Volume { get; set; } = 50;
        public bool Muted { get; set; }
        public List<string> Calls { get; } = [];

        public IReadOnlyList<WindowInfo> ListWindows() => Windows;
        public bool Focus(long handle) { Calls.Add($"focus {handle}"); return true; }
        public bool Minimize(long handle) => true;
        public bool Maximize(long handle) => true;
        public bool Close(long handle) => true;
        public bool Snap(long handle, bool left) => true;
        public int? GetVolume() => Volume;
        public void SetVolume(int percent) => Volume = percent;
        public void SetMute(bool mute) => Muted = mute;
        public bool? IsMuted() => Muted;
        public void Press(MediaKey key) => Calls.Add(key.ToString());
        public void Power(PowerAction action) => Calls.Add(action.ToString());
    }

    // ---- Dosya düzenleme ----

    private FileOrganizer Organizer() => new(_time, Path.Combine(_root, "log.json")) { AllowedRoots = [Path.Combine(_root, "indirilenler")] };

    private string File(string name, int ageMinutes = 60)
    {
        var path = Path.Combine(_root, "indirilenler", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, name);
        System.IO.File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime.AddMinutes(-ageMinutes));
        return path;
    }

    [Fact]
    public void Plan_ture_gore_alt_klasor_inmekte_olana_dokunmaz_geri_al_eski_yerine_koyar()
    {
        var folder = Path.Combine(_root, "indirilenler");
        File("odev.pdf");
        File("tatil.jpg");
        File("kurulum.exe");
        File("yeni.pdf", ageMinutes: 1);      // az önce indi
        File("film.mp4.crdownload");          // hâlâ iniyor
        var organizer = Organizer();

        var plan = organizer.Plan(folder, "tur");
        Assert.Equal(3, plan.Count);
        Assert.Contains("Belgeler (1)", FileOrganizer.Summary(folder, plan));

        organizer.Apply("Düzenle", plan);
        Assert.True(System.IO.File.Exists(Path.Combine(folder, "Belgeler", "odev.pdf")));
        Assert.True(System.IO.File.Exists(Path.Combine(folder, "Resimler", "tatil.jpg")));
        Assert.True(System.IO.File.Exists(Path.Combine(folder, "yeni.pdf")));

        var (batch, restored, skipped) = organizer.UndoLast();
        Assert.NotNull(batch);
        Assert.Equal((3, 0), (restored, skipped));
        Assert.True(System.IO.File.Exists(Path.Combine(folder, "odev.pdf")));
        Assert.Null(organizer.UndoLast().Batch); // geri alınacak başka iş yok
    }

    [Fact]
    public void Ayni_adli_dosya_ezilmez_ve_bulma_tur_ile_calisir()
    {
        var folder = Path.Combine(_root, "indirilenler");
        File("rapor.pdf");
        Directory.CreateDirectory(Path.Combine(folder, "Belgeler"));
        System.IO.File.WriteAllText(Path.Combine(folder, "Belgeler", "rapor.pdf"), "eski");
        var organizer = Organizer();

        var batch = organizer.Apply("Düzenle", organizer.Plan(folder, "tur"));

        Assert.EndsWith("rapor (2).pdf", batch.Moves[0].To);
        Assert.Equal(2, organizer.Find(null, "pdf", 7, null).Count);
        Assert.Empty(organizer.Find(null, "video", null, null));
    }

    [Theory]
    [InlineData("1-3,5", 10, new[] { 1, 2, 3, 5 })]
    [InlineData("8-", 10, new[] { 8, 9, 10 })]
    [InlineData("2, 40", 5, new[] { 2 })]
    public void Sayfa_araligi_okunur(string spec, int count, int[] expected)
    {
        Assert.Equal(expected, FileOrganizer.ParsePages(spec, count));
    }

    [Fact]
    public async Task Izinli_klasor_disina_tasima_onayli_sistem_klasoru_yasak()
    {
        var tool = new MoveFileTool(Organizer());
        var source = File("not.txt");

        Assert.Equal(PermissionLevel.Bildirerek, await tool.LevelForAsync(Args(JsonSerializer.Serialize(new { kaynak = source, hedef = "yeni-not.txt" }))));
        Assert.Equal(PermissionLevel.Onayli, await tool.LevelForAsync(Args(JsonSerializer.Serialize(new { kaynak = source, hedef = Path.Combine(_root, "baska") }))));
        Assert.Equal(PermissionLevel.Yasak, await tool.LevelForAsync(Args(JsonSerializer.Serialize(new { kaynak = source, hedef = Environment.GetFolderPath(Environment.SpecialFolder.Windows) }))));
    }

    [Fact]
    public void PDF_birlestirilir_ve_bolunur()
    {
        string Make(string name, int pages)
        {
            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            for (var i = 1; i <= pages; i++)
                builder.AddPage(PageSize.A4).AddText($"{name} sayfa {i}", 12, new PdfPoint(25, 700), font);
            var path = Path.Combine(_root, name + ".pdf");
            System.IO.File.WriteAllBytes(path, builder.Build());
            return path;
        }
        var pdf = new PdfTools();
        var a = Make("a", 2);
        var b = Make("b", 3);

        Assert.Equal(5, pdf.Merge([a, b], Path.Combine(_root, "ab.pdf")));
        Assert.Equal(2, pdf.Split(b, [1, 3], Path.Combine(_root, "b-parca.pdf")));
    }

    // ---- Ultron tarayıcısı ----

    private sealed class FakeBrowser : IUltronBrowser
    {
        public BrowserPage? Current { get; set; } = new("https://ornek.com/form", "Form", "Başvuru formu",
        [
            new(1, "alan:text", "Ad Soyad", false, false, false),
            new(2, "alan:password", "Şifre", true, false, false),
            new(3, "düğme", "Gönder", false, true, false),
            new(4, "düğme", "Satın Al", false, false, true),
            new(5, "bağlantı", "Duyurular", false, false, false),
        ]);
        public Task<BrowserPage> OpenAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult(Current!);
        public Task<BrowserPage> SnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current!);
        public Task<BrowserPage> ClickAsync(int number, CancellationToken cancellationToken = default) => Task.FromResult(Current!);
        public Task<BrowserPage> TypeAsync(int number, string text, CancellationToken cancellationToken = default) => Task.FromResult(Current!);
        public Task<BrowserPage> BackAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current!);
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("""{"islem":"ac","adres":"ornek.edu.tr"}""", PermissionLevel.Serbest)]
    [InlineData("""{"islem":"ac","adres":"http://127.0.0.1:5199/hub"}""", PermissionLevel.Yasak)]
    [InlineData("""{"islem":"ac","adres":"http://192.168.1.1"}""", PermissionLevel.Yasak)]
    [InlineData("""{"islem":"tikla","numara":5}""", PermissionLevel.Serbest)]
    [InlineData("""{"islem":"yaz","numara":1,"metin":"Merhaba"}""", PermissionLevel.Bildirerek)]
    [InlineData("""{"islem":"yaz","numara":2,"metin":"x"}""", PermissionLevel.Yasak)]
    [InlineData("""{"islem":"tikla","numara":3}""", PermissionLevel.Onayli)]
    [InlineData("""{"islem":"tikla","numara":4}""", PermissionLevel.Yasak)]
    public async Task Tarayici_izin_kurallari(string json, PermissionLevel expected)
    {
        Assert.Equal(expected, await new BrowserTool(new FakeBrowser()).LevelForAsync(Args(json)));
    }

    [Fact]
    public void Hassas_alan_ve_satin_alma_taninir()
    {
        Assert.Equal((true, false), BrowserTool.Classify("alan:text", "Kart Numarası", "text cardNumber cc-number"));
        Assert.Equal((false, true), BrowserTool.Classify("düğme", "Siparişi Tamamla", ""));
        Assert.Equal((false, false), BrowserTool.Classify("bağlantı", "Akademik Takvim", ""));
        Assert.Contains("[3] düğme \"Gönder\" (gönderir — onay gerekir)", BrowserTool.Summary(new FakeBrowser().Current!));
    }

    // ---- Yönlendirme ----

    [Theory]
    [InlineData("Chrome'u öne getir", "pencere")]
    [InlineData("sesi %30 yap", "ses")]
    [InlineData("şarkıyı geç, sonraki parçaya", "medya")]
    [InlineData("bilgisayarı kilitle", "guc")]
    [InlineData("indirilenler klasörünü türlerine göre düzenle", "klasor_duzenle")]
    [InlineData("geçen hafta indirdiğim pdf'leri bul", "dosya_bul")]
    [InlineData("tarayıcıda okul sitesine gir duyurulara bak", "tarayici")]
    public void Istek_dogru_araci_acar(string text, string tool)
    {
        Assert.Contains(tool, ToolRouting.Select(MessageIntent.Komut, text) ?? []);
    }

    [Theory]
    [InlineData("PDF formatı nasıl çalışır?")]
    [InlineData("dosya sistemi nedir")]
    public void Bilgi_sorusu_dosya_araclarini_acmaz(string text)
    {
        var tools = ToolRouting.Select(MessageIntent.GenelBilgi, text) ?? [];
        Assert.DoesNotContain("dosya_bul", tools);
        Assert.DoesNotContain("klasor_duzenle", tools);
    }
}
