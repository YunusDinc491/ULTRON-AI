using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Core.Documents;
using Ultron.Core.Library;
using Ultron.Core.Memories;
using Ultron.Core.Settings;
using Ultron.Infrastructure.Embeddings;
using Ultron.Infrastructure.Ollama;
using Ultron.Infrastructure.Persistence;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Embeddings;

/// <summary>Model değişince eski vektörler yeni boyutta yeniden üretilir, kütüphane yeniden işlenmek üzere işaretlenir.</summary>
public sealed class EmbeddingMigrationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<UltronDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<UltronDbContext>().UseSqlite(_connection).Options;
        await using var db = new UltronDbContext(_options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Eski_vektorler_yeni_modelle_uretilir_kutuphane_yeniden_islenir()
    {
        await using (var db = new UltronDbContext(_options))
        {
            db.Memories.Add(new Memory { Category = "OzelHayat", Content = "Kedisinin adı Pamuk", Embedding = [1f, 0f] });
            var document = new Document { FileName = "a.pdf", Collection = "Genel", Kind = DocumentKind.Pdf, StoredPath = "a.pdf", Sha256 = "AA" };
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            db.DocumentChunks.Add(new DocumentChunk { DocumentId = document.Id, Ordinal = 0, Text = "Entropi düzensizliğin ölçüsüdür.", Embedding = [1f, 0f] });
            db.LibraryPages.Add(new LibraryPage { SourceId = "mdn", Path = "a.md", Title = "A", Url = "u", Version = "v", ContentHash = "H", UpdatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var time = TimeProvider.System;
        var settingsStore = new InMemorySettingsStore();
        var settings = new UltronSettings(settingsStore, time);
        await settings.SetInternalAsync(LibraryService.LastSyncKey, DateTime.UtcNow.ToString("O"));
        var embedder = new FakeEmbeddingModel([]); // her metne 3 boyutlu vektör
        var factory = new Factory(_options);
        var library = new LibraryService(new EfLibraryStore(factory), null!, embedder, settings, time, NullLogger<LibraryService>.Instance);
        var documents = new DocumentService(new InMemoryDocumentStore(), null!, embedder, Options.Create(new DocumentOptions()), NullLogger<DocumentService>.Instance);
        var migration = new EmbeddingMigration(factory, embedder, Options.Create(new OllamaOptions()), settings, documents, library, time, NullLogger<EmbeddingMigration>.Instance);

        await migration.MigrateAsync("qwen3-embedding:0.6b", 3, CancellationToken.None);

        await using (var db = new UltronDbContext(_options))
        {
            Assert.Equal(3, (await db.Memories.SingleAsync()).Embedding.Length);
            Assert.Equal(3, (await db.DocumentChunks.SingleAsync()).Embedding.Length);
            Assert.Equal("", (await db.LibraryPages.SingleAsync()).ContentHash);
        }
        Assert.Equal("qwen3-embedding:0.6b", await settings.GetInternalAsync(EmbeddingMigration.ModelKey));
        Assert.Null((await library.StatsAsync()).LastSyncUtc); // zamanlayıcı ilk fırsatta yeniden işleyecek
    }

    private sealed class Factory(DbContextOptions<UltronDbContext> options) : IDbContextFactory<UltronDbContext>
    {
        public UltronDbContext CreateDbContext() => new(options);
    }
}
