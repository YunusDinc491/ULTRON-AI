using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ultron.Core.Chat;
using Ultron.Infrastructure.Persistence;

namespace Ultron.Tests.Persistence;

/// <summary>Gerçek SQLite motoru, ama diske değil belleğe yazar: hızlı ve her test temiz başlar.</summary>
public sealed class EfConversationStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private EfConversationStore _store = null!;

    public async Task InitializeAsync()
    {
        // Bellek içi SQLite bağlantı kapanınca silinir; test boyunca açık tutuyoruz.
        await _connection.OpenAsync();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<UltronDbContext>().UseSqlite(_connection).Options);
        await using (var db = factory.CreateDbContext())
            await db.Database.MigrateAsync();
        _store = new EfConversationStore(factory);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Hic_konusma_yoksa_null_doner() => Assert.Null(await _store.GetLatestAsync());

    [Fact]
    public async Task Mesajlar_kaydedilir_ve_sirayla_geri_okunur()
    {
        var conversation = await _store.StartAsync();
        await _store.AddMessageAsync(conversation.Id, Role.User, "Kedimin adı Pamuk.");
        await _store.AddMessageAsync(conversation.Id, Role.Assistant, "Anlaşıldı.");

        var loaded = await _store.GetLatestAsync();

        Assert.NotNull(loaded);
        Assert.Equal(conversation.Id, loaded.Id);
        Assert.Equal([Role.User, Role.Assistant], loaded.Messages.Select(m => m.Role));
        Assert.Equal("Kedimin adı Pamuk.", loaded.Messages[0].Content);
    }

    [Fact]
    public async Task En_son_baslatilan_konusma_doner()
    {
        var first = await _store.StartAsync();
        await _store.AddMessageAsync(first.Id, Role.User, "eski");
        var second = await _store.StartAsync();

        var latest = await _store.GetLatestAsync();

        Assert.Equal(second.Id, latest!.Id);
        Assert.Empty(latest.Messages);
    }

    private sealed class TestDbFactory(DbContextOptions<UltronDbContext> options) : IDbContextFactory<UltronDbContext>
    {
        public UltronDbContext CreateDbContext() => new(options);
    }
}
