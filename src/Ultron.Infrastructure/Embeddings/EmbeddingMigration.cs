using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Documents;
using Ultron.Core.Embeddings;
using Ultron.Core.Library;
using Ultron.Core.Settings;
using Ultron.Infrastructure.Ollama;
using Ultron.Infrastructure.Persistence;

namespace Ultron.Infrastructure.Embeddings;

/// <summary>
/// Embedding modeli değişince (2026-10-28: embeddinggemma → qwen3-embedding) kayıtlı vektörleri yenisiyle üretir.
/// Kütüphanedeki katalog kartlarını yeni sisteme göre baştan yazmak gibi: hafıza kayıtları ve belge parçaları burada,
/// arka planda yeniden kodlanır; kütüphane büyük olduğu için kendi güncelleme yolundan baştan işlenir (sayfa özetleri
/// sıfırlanır, zamanlayıcı ilk fırsatta hepsini yeniden kodlar). Geçiş sürerken eski boyuttaki vektörler aramada atlanır.
/// Hangi modelle kodlandığı ayarlarda tutulur; yarıda kalırsa bir sonraki açılışta kaldığı yerden sürer.
/// </summary>
internal sealed class EmbeddingMigration(
    IDbContextFactory<UltronDbContext> dbFactory,
    IEmbeddingModel embedder,
    IOptions<OllamaOptions> options,
    UltronSettings settings,
    DocumentService documents,
    LibraryService library,
    TimeProvider time,
    ILogger<EmbeddingMigration> logger) : BackgroundService
{
    public const string ModelKey = "embedding_modeli";
    private const int BatchSize = 32;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var model = options.Value.EmbeddingModel;
        if (await settings.GetInternalAsync(ModelKey, stoppingToken) == model)
            return;

        // Ollama açılışta henüz kalkmamış olabilir (servis başlatıcı açıyor): deneme vektörü gelene kadar bekle.
        int dimensions;
        while (true)
        {
            try
            {
                dimensions = (await embedder.EmbedAsync(["deneme"], EmbeddingPurpose.Document, stoppingToken))[0].Length;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogInformation("Embedding geçişi Ollama'yı bekliyor: {Message}", ex.Message);
                await Task.Delay(RetryDelay, time, stoppingToken);
            }
        }

        try
        {
            await MigrateAsync(model, dimensions, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Embedding geçişi yarıda kaldı; bir sonraki açılışta sürecek");
        }
    }

    internal async Task MigrateAsync(string model, int dimensions, CancellationToken cancellationToken)
    {
        logger.LogInformation("Embedding geçişi başladı → {Model} ({Dimensions} boyut)", model, dimensions);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // Hafıza (onlarca kayıt) ve belge parçaları (birkaç bin) belleğe sığar; belge araması da zaten hepsini belleğe alıyor.
        var memories = (await db.Memories.ToListAsync(cancellationToken)).Where(m => m.Embedding.Length != dimensions).ToList();
        await ReembedAsync(db, memories, m => m.Content, (m, v) => m.Embedding = v, cancellationToken);
        var chunks = (await db.DocumentChunks.ToListAsync(cancellationToken)).Where(c => c.Embedding.Length != dimensions).ToList();
        await ReembedAsync(db, chunks, c => c.Text, (c, v) => c.Embedding = v, cancellationToken);
        documents.ResetCache();

        // Kütüphane on binlerce parça: kendi güncelleme yolu baştan işlesin (değişmedi sanılmasın diye özetler silinir).
        var pages = await db.LibraryPages.ExecuteUpdateAsync(s => s.SetProperty(p => p.ContentHash, ""), cancellationToken);
        if (pages > 0)
            await settings.SetInternalAsync(LibraryService.LastSyncKey, "", cancellationToken); // zamanlayıcı ilk fırsatta yeniden işler
        library.ResetIndex();

        await settings.SetInternalAsync(ModelKey, model, cancellationToken);
        logger.LogInformation("Embedding geçişi bitti: {Memories} hafıza kaydı, {Chunks} belge parçası yeniden kodlandı; {Pages} kütüphane sayfası yeniden işlenecek",
            memories.Count, chunks.Count, pages);
    }

    /// <summary>Küçük gruplar hâlinde yeniden kodlar, her grubu hemen kaydeder (yarıda kalırsa yapılan kaybolmaz).</summary>
    private async Task ReembedAsync<T>(UltronDbContext db, IReadOnlyList<T> rows, Func<T, string> text, Action<T, float[]> set, CancellationToken cancellationToken)
    {
        foreach (var batch in rows.Chunk(BatchSize))
        {
            var vectors = await embedder.EmbedAsync(batch.Select(text).ToList(), EmbeddingPurpose.Document, cancellationToken);
            for (var i = 0; i < batch.Length; i++)
                set(batch[i], vectors[i]);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
