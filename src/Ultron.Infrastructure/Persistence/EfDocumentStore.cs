using Microsoft.EntityFrameworkCore;
using Ultron.Core.Documents;
using Ultron.Core.Text;

namespace Ultron.Infrastructure.Persistence;

/// <summary>
/// Belgeler Documents, parçaları DocumentChunks tablosunda; anahtar kelime araması için parça metni
/// normalleştirilmiş olarak DocumentSearch (SQLite FTS5) tablosunda da tutulur. FTS satırının rowid'si = DocumentChunk.Id.
/// </summary>
internal sealed class EfDocumentStore(IDbContextFactory<UltronDbContext> dbFactory) : IDocumentStore
{
    public async Task<IReadOnlyList<Document>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents.AsNoTracking().OrderByDescending(d => d.AddedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<Document?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<Document?> FindBySha256Async(string sha256, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Sha256 == sha256, cancellationToken);
    }

    public async Task<Document> AddAsync(Document document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        var rows = chunks.Select(c => new DocumentChunk { DocumentId = document.Id, Page = c.Page, Ordinal = c.Ordinal, Text = c.Text, Embedding = c.Embedding }).ToList();
        db.DocumentChunks.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var row in rows)
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO DocumentSearch(rowid, Text) VALUES ({row.Id}, {TurkishText.Normalize(row.Text)})",
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return document;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM DocumentSearch WHERE rowid IN (SELECT Id FROM DocumentChunks WHERE DocumentId = {id})",
            cancellationToken);
        await db.DocumentChunks.Where(c => c.DocumentId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Documents.Where(d => d.Id == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetCollectionAsync(long id, string collection, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Documents.Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.Collection, collection), cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(IReadOnlyCollection<long>? documentIds, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.DocumentChunks.AsNoTracking();
        if (documentIds is not null)
            query = query.Where(c => documentIds.Contains(c.DocumentId));
        return await query.OrderBy(c => c.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentChunk>> SearchKeywordsAsync(IReadOnlyList<string> prefixes, IReadOnlyCollection<long>? documentIds, int limit, CancellationToken cancellationToken = default)
    {
        if (prefixes.Count == 0 || limit <= 0)
            return [];

        // Hafızadaki gibi: "entr"* OR "tani"*. Daraltma varsa FTS'ten fazlasını alıp belgeye göre süzeriz.
        var match = string.Join(" OR ", prefixes.Select(p => $"\"{p.Replace("\"", "")}\"*"));
        var take = documentIds is null ? limit : limit * 20;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.Database
            .SqlQuery<long>($"SELECT rowid AS Value FROM DocumentSearch WHERE DocumentSearch MATCH {match} ORDER BY rank LIMIT {take}")
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
            return [];

        var found = await db.DocumentChunks.AsNoTracking()
            .Where(c => ids.Contains(c.Id) && (documentIds == null || documentIds.Contains(c.DocumentId)))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        return ids.Where(found.ContainsKey).Select(id => found[id]).Take(limit).ToList(); // FTS sıralamasını koru
    }
}
