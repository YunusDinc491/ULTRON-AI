using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ultron.Core.Memories;
using Ultron.Infrastructure.Persistence;

namespace Ultron.Tests.Persistence;

public sealed class EfMemoryStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private EfMemoryStore _store = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UltronDbContext>().UseSqlite(_connection).Options;
        await using (var db = new UltronDbContext(options))
            await db.Database.MigrateAsync();
        _store = new EfMemoryStore(new Factory(options));
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Vektor_BLOB_olarak_kaybolmadan_saklanir()
    {
        float[] vector = [0.25f, -1.5f, 3.75f];
        await _store.AddAsync(new Memory { Category = "Okul", Content = "Termodinamik II alıyor", Embedding = vector });

        var loaded = Assert.Single(await _store.GetActiveAsync());

        Assert.Equal(vector, loaded.Embedding);
        Assert.Equal("Okul", loaded.Category);
    }

    [Fact]
    public async Task Guncelleme_ve_pasiflestirme_calisir()
    {
        var a = await _store.AddAsync(new Memory { Category = "OzelHayat", Content = "İstanbul'da yaşıyor", Embedding = [1f] });
        var b = await _store.AddAsync(new Memory { Category = "OzelHayat", Content = "Kedisi var", Embedding = [1f] });

        await _store.UpdateAsync(a.Id, "Ankara'da yaşıyor", "OzelHayat", [2f]);
        await _store.DeactivateAsync(b.Id);

        var active = Assert.Single(await _store.GetActiveAsync());
        Assert.Equal("Ankara'da yaşıyor", active.Content);
        Assert.Equal([2f], active.Embedding);
    }

    [Fact]
    public async Task Anahtar_kelime_aramasi_ek_ve_buyuk_harf_farkina_ragmen_bulur()
    {
        await _store.AddAsync(new Memory { Category = "OzelHayat", Content = "Kedisinin adı Pamuk", Embedding = [1f] });
        await _store.AddAsync(new Memory { Category = "Kariyer", Content = "ASELSAN'a staj başvurusu yaptı", Embedding = [1f] });

        var pamuk = await _store.SearchKeywordsAsync(KeywordQuery.Prefixes("PAMUK'un yaşı kaç?"), limit: 3);
        var staj = await _store.SearchKeywordsAsync(KeywordQuery.Prefixes("Aselsan başvurum ne oldu?"), limit: 3);

        Assert.Equal("Kedisinin adı Pamuk", Assert.Single(pamuk).Content);
        Assert.Equal("ASELSAN'a staj başvurusu yaptı", Assert.Single(staj).Content);
    }

    [Fact]
    public async Task Pasif_ve_guncellenen_kayitlar_aramaya_dogru_yansir()
    {
        var a = await _store.AddAsync(new Memory { Category = "OzelHayat", Content = "İstanbul'da yaşıyor", Embedding = [1f] });
        var b = await _store.AddAsync(new Memory { Category = "OzelHayat", Content = "Kedisinin adı Pamuk", Embedding = [1f] });

        await _store.UpdateAsync(a.Id, "Ankara'da yaşıyor", "OzelHayat", [1f]);
        await _store.DeactivateAsync(b.Id);

        Assert.Empty(await _store.SearchKeywordsAsync(["ista"], 3));
        Assert.Single(await _store.SearchKeywordsAsync(["anka"], 3));
        Assert.Empty(await _store.SearchKeywordsAsync(["pamu"], 3));
    }

    private sealed class Factory(DbContextOptions<UltronDbContext> options) : IDbContextFactory<UltronDbContext>
    {
        public UltronDbContext CreateDbContext() => new(options);
    }
}
