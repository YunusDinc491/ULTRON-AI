using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ultron.Core.Backup;
using Ultron.Core.Memories;
using Ultron.Core.Settings;
using Ultron.Infrastructure.Backup;
using Ultron.Infrastructure.Persistence;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Tools;

/// <summary>Gerçek zip + SQLite yedeği, geçici klasörlerde (kullanıcının verisine dokunmaz).</summary>
public sealed class BackupTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ultron-yedek-test-" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_root, "ultron.db");
    private string DataRoot => Path.Combine(_root, "veri");
    private string Docs => Path.Combine(DataRoot, "belgeler");
    private DbContextOptions<UltronDbContext> _options = null!;
    private ZipBackupArchiver _archiver = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Docs);
        _options = new DbContextOptionsBuilder<UltronDbContext>().UseSqlite($"Data Source={DbPath}").Options;
        await using (var db = new UltronDbContext(_options))
            await db.Database.MigrateAsync();
        _archiver = new ZipBackupArchiver(DbPath, Docs, new BackupOptions { DataRoot = DataRoot }, new Factory(_options), NullLogger<ZipBackupArchiver>.Instance);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private EfMemoryStore Memories => new(new Factory(_options));

    [Fact]
    public async Task Yedek_alinir_ve_geri_yuklenince_veri_ve_dosyalar_doner()
    {
        await Memories.AddAsync(new Memory { Category = "Okul", Content = "Termodinamik II alıyor", Embedding = [1f] });
        await File.WriteAllTextAsync(Path.Combine(Docs, "not.txt"), "ilk hâli");
        Directory.CreateDirectory(Path.Combine(DataRoot, "projeler", "site", "node_modules"));
        await File.WriteAllTextAsync(Path.Combine(DataRoot, "projeler", "site", "ULTRON-NOTLAR.md"), "koyu tema");
        await File.WriteAllTextAsync(Path.Combine(DataRoot, "projeler", "site", "node_modules", "dev.js"), "x");

        var backup = await _archiver.CreateAsync(_archiver.Folder, new DateTime(2026, 10, 27, 3, 0, 5), null);
        using (var zip = ZipFile.OpenRead(backup.Path))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("ultron.db", names);
            Assert.Contains("dosyalar/belgeler/not.txt", names);
            Assert.Contains("dosyalar/projeler/site/ULTRON-NOTLAR.md", names);
            Assert.DoesNotContain(names, n => n.Contains("node_modules"));
        }
        Assert.Equal(backup.Path, Assert.Single(_archiver.List(_archiver.Folder)).Path);

        // Sonradan değişiklik: geri yükleme bunları eski hâline döndürmeli.
        await Memories.AddAsync(new Memory { Category = "Okul", Content = "Sonradan eklenen", Embedding = [1f] });
        await File.WriteAllTextAsync(Path.Combine(Docs, "not.txt"), "bozuldu");

        await _archiver.RestoreAsync(backup);

        Assert.Equal("Termodinamik II alıyor", Assert.Single(await Memories.GetActiveAsync()).Content);
        Assert.Equal("ilk hâli", await File.ReadAllTextAsync(Path.Combine(Docs, "not.txt")));
    }

    [Fact]
    public async Task Ultron_yedegi_olmayan_zip_reddedilir_ve_veri_degismez()
    {
        await Memories.AddAsync(new Memory { Category = "Okul", Content = "Duruyor", Embedding = [1f] });
        Directory.CreateDirectory(_archiver.Folder);
        var fake = Path.Combine(_archiver.Folder, "ultron-20261026-030000.zip");
        using (var zip = ZipFile.Open(fake, ZipArchiveMode.Create))
            zip.CreateEntry("ultron.db");
        await Assert.ThrowsAsync<InvalidDataException>(() => _archiver.RestoreAsync(new BackupFile(fake, new DateTime(2026, 10, 26, 3, 0, 0), 1)));
        Assert.Single(await Memories.GetActiveAsync());
    }

    [Theory]
    [InlineData("belgeler/a/b.pdf", true)]
    [InlineData("belgeler/../../windows/x.dll", false)]
    [InlineData("bilinmeyen/x.txt", false)]
    [InlineData("desktop-settings.json", true)]
    public void Geri_yukleme_yolu_bilinen_klasorlerin_disina_cikmaz(string entry, bool allowed) =>
        Assert.Equal(allowed, _archiver.TargetFor(entry) is not null);

    [Fact]
    public async Task Servis_gunluk_alir_eskileri_siler_ve_geri_yuklemeden_once_yedekler()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 27, 2, 0, 0, TimeSpan.Zero));
        var settings = new UltronSettings(new InMemorySettingsStore(), time);
        var service = new BackupService(settings, _archiver, time, NullLogger<BackupService>.Instance);

        Assert.NotNull(await service.TryRunScheduledAsync()); // hiç yedek yok → hemen
        Assert.Null(await service.TryRunScheduledAsync());    // dünkü 03:00 diliminde zaten var
        time.Advance(TimeSpan.FromHours(1.5));                 // 03:30
        Assert.NotNull(await service.TryRunScheduledAsync());
        Assert.Single(service.List()); // aynı günün 02:00 yedeği silindi: her günden en yenisi kalır

        var yesterday = service.Find("en_son")!;
        var safety = await service.RestoreAsync(yesterday);
        Assert.Equal(BackupService.BeforeRestoreLabel, safety.Label);
        Assert.Null(service.Find("en_son")!.Label); // güvenlik yedeği "en son" sayılmaz
        Assert.StartsWith("son: ", await service.DescribeAsync());
    }

    [Fact]
    public void Saklama_son_7_gun_ve_son_4_hafta()
    {
        var now = new DateTime(2026, 10, 27, 3, 0, 0); // salı
        var all = Enumerable.Range(0, 60).Select(i => now.AddDays(-i))
            .Select(d => new BackupFile($"ultron-{d:yyyyMMdd}-030000.zip", d, 1)).ToList();
        var deleted = BackupService.ToDelete(all, now).Select(b => b.CreatedLocal).ToHashSet();
        var kept = all.Select(b => b.CreatedLocal).Where(d => !deleted.Contains(d)).OrderDescending().ToList();

        Assert.Equal(Enumerable.Range(0, 7).Select(i => now.AddDays(-i)), kept.Take(7)); // son 7 gün
        Assert.Equal(9, kept.Count); // + önceki 3 haftanın en yenisi (pazar), biri zaten son 7 günde: 7 + 2
        Assert.All(kept, d => Assert.True(d > now.AddDays(-28)));
    }

    [Fact]
    public void Yedek_secimi_tarihle()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        Directory.CreateDirectory(_archiver.Folder);
        foreach (var name in new[] { "ultron-20261027-030000.zip", "ultron-20261026-030000.zip", "ultron-20261025-030000.zip", "ultron-20261027-110000-geri-yukleme-oncesi.zip" })
            File.WriteAllText(Path.Combine(_archiver.Folder, name), "");
        var service = new BackupService(new UltronSettings(new InMemorySettingsStore(), time), _archiver, time, NullLogger<BackupService>.Instance);

        Assert.Equal(new DateTime(2026, 10, 27, 3, 0, 0), service.Find("en son")!.CreatedLocal);
        Assert.Equal(new DateTime(2026, 10, 26, 3, 0, 0), service.Find("dün")!.CreatedLocal);
        Assert.Equal(new DateTime(2026, 10, 25, 3, 0, 0), service.Find("25.10")!.CreatedLocal);
        Assert.Null(service.Find("20.10"));
        Assert.NotNull(service.Find("ultron-20261027-110000-geri-yukleme-oncesi.zip")!.Label);
    }

    [Theory]
    [InlineData("yok", "yok")]
    [InlineData("E:\\UltronYedek\\", "E:\\UltronYedek")]
    [InlineData("\"D:\\Yedek\"", "D:\\Yedek")]
    [InlineData("yedek klasörüm", null)]
    [InlineData("C:\\Windows\\Temp", null)]
    public void Ikinci_yer_bicimi(string value, string? expected) => Assert.Equal(expected, UltronSettings.ParseBackupFolder(value));

    private sealed class Factory(DbContextOptions<UltronDbContext> options) : IDbContextFactory<UltronDbContext>
    {
        public UltronDbContext CreateDbContext() => new(options);
    }
}
