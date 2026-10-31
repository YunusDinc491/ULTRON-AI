using Microsoft.EntityFrameworkCore;
using Ultron.Core.Library;
using Ultron.Core.Text;

namespace Ultron.Infrastructure.Persistence;

/// <summary>
/// Yazılım Kütüphanesi deposu. Parça metinleri normalleştirilmiş olarak LibrarySearch (SQLite FTS5) tablosunda da tutulur;
/// FTS satırının rowid'si = LibraryChunk.Id (belgelerdeki DocumentSearch ile aynı düzen).
/// </summary>
internal sealed class EfLibraryStore(IDbContextFactory<UltronDbContext> dbFactory) : ILibraryStore
{
    public async Task<IReadOnlyDictionary<string, LibraryPage>> GetPagesAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.LibraryPages.AsNoTracking().Where(p => p.SourceId == sourceId).ToDictionaryAsync(p => p.Path, StringComparer.Ordinal, cancellationToken);
    }

    public async Task<LibraryPage> SavePageAsync(LibraryPage page, IReadOnlyList<LibraryChunk> chunks, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (page.Id != 0)
        {
            await db.Database.ExecuteSqlAsync($"DELETE FROM LibrarySearch WHERE rowid IN (SELECT Id FROM LibraryChunks WHERE PageId = {page.Id})", cancellationToken);
            await db.LibraryChunks.Where(c => c.PageId == page.Id).ExecuteDeleteAsync(cancellationToken);
            db.LibraryPages.Update(page);
        }
        else
        {
            db.LibraryPages.Add(page);
        }
        await db.SaveChangesAsync(cancellationToken);

        var rows = chunks.Select(c => new LibraryChunk { PageId = page.Id, Ordinal = c.Ordinal, Text = c.Text, Embedding = c.Embedding }).ToList();
        db.LibraryChunks.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var row in rows)
            await db.Database.ExecuteSqlAsync($"INSERT INTO LibrarySearch(rowid, Text) VALUES ({row.Id}, {TurkishText.Normalize(row.Text)})", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }

    public async Task TouchPagesAsync(IReadOnlyCollection<long> pageIds, string version, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (pageIds.Count == 0)
            return;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        foreach (var batch in pageIds.Chunk(500))
            await db.LibraryPages.Where(p => batch.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Version, version).SetProperty(p => p.UpdatedAtUtc, nowUtc).SetProperty(p => p.IsStale, false), cancellationToken);
    }

    public async Task MarkStaleAsync(IReadOnlyCollection<long> pageIds, CancellationToken cancellationToken = default)
    {
        if (pageIds.Count == 0)
            return;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        foreach (var batch in pageIds.Chunk(500))
            await db.LibraryPages.Where(p => batch.Contains(p.Id)).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsStale, true), cancellationToken);
    }

    public async Task<IReadOnlyList<(long ChunkId, float[] Embedding)>> GetEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var staleIds = db.LibraryPages.Where(p => p.IsStale).Select(p => p.Id);
        return (await db.LibraryChunks.AsNoTracking()
                .Where(c => !staleIds.Contains(c.PageId))
                .Select(c => new { c.Id, c.Embedding })
                .ToListAsync(cancellationToken))
            .Select(c => (c.Id, c.Embedding))
            .ToList();
    }

    public async Task<IReadOnlyList<(LibraryChunk Chunk, LibraryPage Page)>> GetChunksAsync(IReadOnlyCollection<long> chunkIds, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var chunks = await db.LibraryChunks.AsNoTracking().Where(c => chunkIds.Contains(c.Id)).ToListAsync(cancellationToken);
        var pageIds = chunks.Select(c => c.PageId).Distinct().ToList();
        var pages = await db.LibraryPages.AsNoTracking().Where(p => pageIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        return chunks.Select(c => (c, pages[c.PageId])).ToList();
    }

    public async Task<IReadOnlyList<long>> SearchKeywordsAsync(IReadOnlyList<string> terms, int limit, CancellationToken cancellationToken = default)
    {
        if (terms.Count == 0 || limit <= 0)
            return [];
        var match = string.Join(" OR ", terms.Select(t => $"\"{t.Replace("\"", "")}\"*"));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database
            .SqlQuery<long>($"SELECT rowid AS Value FROM LibrarySearch WHERE LibrarySearch MATCH {match} ORDER BY rank LIMIT {limit}")
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<long>> SearchTitlesAsync(string name, int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var pattern = name.Replace("%", "").Replace("_", "\\_") + "%";
        return await db.LibraryPages
            .Where(p => !p.IsStale && EF.Functions.Like(p.Title, pattern, "\\"))
            .OrderBy(p => p.Title.Length) // "Array.prototype.map()" önce, "Array.prototype.mapLimit…" sonra
            .Take(limit)
            .Join(db.LibraryChunks.Where(c => c.Ordinal < 2), p => p.Id, c => c.PageId, (p, c) => c.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<LibraryStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var sources = await db.LibraryPages.Where(p => !p.IsStale).Select(p => p.SourceId).Distinct().CountAsync(cancellationToken);
        var pages = await db.LibraryPages.CountAsync(p => !p.IsStale, cancellationToken);
        var chunks = await db.LibraryChunks.CountAsync(cancellationToken);
        return new LibraryStats(sources, pages, chunks, null);
    }
}
