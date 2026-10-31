using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ultron.Core.Memories;
using Ultron.Infrastructure.Persistence;

namespace Ultron.Tests.Persistence;

public sealed class EfCategoryStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private EfCategoryStore _store = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UltronDbContext>().UseSqlite(_connection).Options;
        await using (var db = new UltronDbContext(options))
            await db.Database.MigrateAsync();
        _store = new EfCategoryStore(new Factory(options));
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Migration_sadece_Saglik_ile_baslar_PATRON_cekmecesinde_ve_cekirdek()
    {
        var c = Assert.Single(await _store.GetAllAsync());

        Assert.Equal(MemoryCategories.Saglik, c.Key);
        Assert.Equal(MemoryDrawers.Patron, c.Drawer);
        Assert.True(c.IsCore);
    }

    [Fact]
    public async Task Kategori_cekmecesiyle_eklenir_anahtar_uretilir_ayni_ad_tekrar_eklenmez()
    {
        var added = await _store.AddAsync("Araba & Motor", "Araç bakımı", MemoryDrawers.Patron);
        var again = await _store.AddAsync("araba & MOTOR", "farklı açıklama", MemoryDrawers.Is);
        var frontend = await _store.AddAsync("Frontend", "React", MemoryDrawers.Is);
        var unknown = await _store.AddAsync("Garip", "", "Uydurma");

        Assert.Equal("ArabaMotor", added.Key);
        Assert.Equal(added.Key, again.Key);
        Assert.Equal(MemoryDrawers.Is, (await _store.GetAllAsync()).Single(c => c.Key == frontend.Key).Drawer);
        Assert.Equal(MemoryDrawers.Patron, unknown.Drawer); // bilinmeyen çekmece → PATRON
        Assert.Equal(4, (await _store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Oneri_kabul_edilince_kategori_olur_reddedilen_tekrar_onerilmez()
    {
        await _store.AddSuggestionAsync("Araç", "Araba", "Arabası Egea");
        await _store.AddSuggestionAsync("Seyahat", "Geziler", null);
        await _store.AddSuggestionAsync("Sağlık", "zaten var", null); // var olan kategoriyle aynı ad: eklenmez

        var pending = await _store.GetPendingSuggestionsAsync();
        Assert.Equal(["Araç", "Seyahat"], pending.Select(s => s.Name));

        var accepted = await _store.ResolveSuggestionAsync(pending[0].Id, accept: true);
        await _store.ResolveSuggestionAsync(pending[1].Id, accept: false);
        await _store.AddSuggestionAsync("Seyahat", "Geziler", null); // reddedildi: tekrar önerilmez

        Assert.Equal("Arac", accepted!.Key);
        Assert.Empty(await _store.GetPendingSuggestionsAsync());
    }

    [Fact]
    public async Task Ad_aciklama_ve_cekirdek_guncellenir_anahtar_degismez()
    {
        var added = await _store.AddAsync("İlişki", "Sevgilisi", MemoryDrawers.Patron);
        await _store.UpdateAsync(added.Key, "Sevgilim", "Sevgilimle ilgili her şey", isCore: true);

        var c = (await _store.GetAllAsync()).Single(x => x.Key == added.Key);
        Assert.Equal("Sevgilim", c.Name);
        Assert.True(c.IsCore);
    }

    [Fact]
    public async Task Bos_klasor_silinir_dolu_ve_yerlesik_silinmez_pasif_kayitlar_da_gider()
    {
        var bos = await _store.AddAsync("Sunum", "", MemoryDrawers.Patron);
        var dolu = await _store.AddAsync("Proje", "", MemoryDrawers.Is);
        var options = new DbContextOptionsBuilder<UltronDbContext>().UseSqlite(_connection).Options;
        await using (var db = new UltronDbContext(options))
        {
            db.Memories.Add(new Memory { Category = bos.Key, Content = "Sınavdan kalan", Embedding = [1f], IsActive = false });
            db.Memories.Add(new Memory { Category = dolu.Key, Content = "Gerçek kayıt", Embedding = [1f] });
            await db.SaveChangesAsync();
        }

        Assert.True(await _store.DeleteIfEmptyAsync(bos.Key));
        Assert.False(await _store.DeleteIfEmptyAsync(dolu.Key));
        Assert.False(await _store.DeleteIfEmptyAsync(MemoryCategories.Saglik));
        Assert.Equivalent(new[] { MemoryCategories.Saglik, dolu.Key }, (await _store.GetAllAsync()).Select(c => c.Key));
        await using (var db = new UltronDbContext(options))
            Assert.Equal(["Gerçek kayıt"], await db.Memories.Select(m => m.Content).ToArrayAsync());
    }

    private sealed class Factory(DbContextOptions<UltronDbContext> options) : IDbContextFactory<UltronDbContext>
    {
        public UltronDbContext CreateDbContext() => new(options);
    }
}
