using Microsoft.EntityFrameworkCore;
using Ultron.Core.Memories;
using Ultron.Core.Text;

namespace Ultron.Infrastructure.Persistence;

/// <summary>
/// Kayıtlar Memories tablosunda; anahtar kelime araması için normalleştirilmiş metin ayrıca
/// MemorySearch (SQLite FTS5) tablosunda tutulur. FTS satırının rowid'si = Memory.Id.
/// Pasif kayıtlar FTS'den silinir, yani arama sadece aktif kayıtları bulur.
/// </summary>
internal sealed class EfMemoryStore(IDbContextFactory<UltronDbContext> dbFactory) : IMemoryStore
{
    public async Task<IReadOnlyList<Memory>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Memories.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Memory>> SearchKeywordsAsync(IReadOnlyList<string> prefixes, int limit, CancellationToken cancellationToken = default)
    {
        if (prefixes.Count == 0 || limit <= 0)
            return [];

        // FTS5 sorgu dili: "kedi"* OR "pamu"* → önekle başlayan kelime içeren satırlar.
        // Önekler normalleştirilmiş (sadece harf/rakam) ama yine de tırnak içine alıyoruz.
        var match = string.Join(" OR ", prefixes.Select(p => $"\"{p.Replace("\"", "")}\"*"));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.Database
            .SqlQuery<long>($"SELECT rowid AS Value FROM MemorySearch WHERE MemorySearch MATCH {match} ORDER BY rank LIMIT {limit}")
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
            return [];

        var found = await db.Memories.AsNoTracking()
            .Where(m => ids.Contains(m.Id) && m.IsActive)
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        return ids.Where(found.ContainsKey).Select(id => found[id]).ToList(); // FTS sıralamasını koru
    }

    public async Task<Memory> AddAsync(Memory memory, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Memories.Add(memory);
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO MemorySearch(rowid, Text) VALUES ({memory.Id}, {TurkishText.Normalize(memory.Content)})",
            cancellationToken);
        return memory;
    }

    public async Task UpdateAsync(long id, string content, string category, float[] embedding, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var memory = await db.Memories.FindAsync([id], cancellationToken)
            ?? throw new InvalidOperationException($"Hafıza kaydı bulunamadı: {id}");

        memory.Content = content;
        memory.Category = category;
        memory.Embedding = embedding;
        memory.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE MemorySearch SET Text = {TurkishText.Normalize(content)} WHERE rowid = {id}",
            cancellationToken);
    }

    public async Task DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Memories
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.IsActive, false)
                .SetProperty(m => m.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
        await db.Database.ExecuteSqlAsync($"DELETE FROM MemorySearch WHERE rowid = {id}", cancellationToken);
    }

    public async Task ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var memory = await db.Memories.FindAsync([id], cancellationToken)
            ?? throw new InvalidOperationException($"Hafıza kaydı bulunamadı: {id}");
        if (memory.IsActive)
            return;

        memory.IsActive = true;
        memory.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO MemorySearch(rowid, Text) VALUES ({id}, {TurkishText.Normalize(memory.Content)})",
            cancellationToken);
    }

    public async Task<IReadOnlyList<Memory>> GetRecentlyDeactivatedAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Memories.AsNoTracking()
            .Where(m => !m.IsActive)
            .OrderByDescending(m => m.UpdatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// FTS tablosunu aktif kayıtlardan baştan kurar. Açılışta sayılar tutmuyorsa çağrılır
    /// (ör. migration'dan önce eklenmiş kayıtlar ya da yarıda kalmış bir yazma).
    /// </summary>
    internal static async Task RebuildSearchIndexIfNeededAsync(UltronDbContext db, CancellationToken cancellationToken)
    {
        var activeCount = await db.Memories.CountAsync(m => m.IsActive, cancellationToken);
        var indexedCount = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM MemorySearch").SingleAsync(cancellationToken);
        if (activeCount == indexedCount)
            return;

        await db.Database.ExecuteSqlAsync($"DELETE FROM MemorySearch", cancellationToken);
        var active = await db.Memories.AsNoTracking().Where(m => m.IsActive).Select(m => new { m.Id, m.Content }).ToListAsync(cancellationToken);
        foreach (var m in active)
            await db.Database.ExecuteSqlAsync($"INSERT INTO MemorySearch(rowid, Text) VALUES ({m.Id}, {TurkishText.Normalize(m.Content)})", cancellationToken);
    }
}
