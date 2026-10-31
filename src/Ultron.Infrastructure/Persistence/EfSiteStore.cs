using Microsoft.EntityFrameworkCore;
using Ultron.Core.Sites;

namespace Ultron.Infrastructure.Persistence;

internal sealed class EfSiteStore(IDbContextFactory<UltronDbContext> dbFactory) : ISiteStore
{
    public async Task<IReadOnlyList<KnownSite>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.KnownSites.AsNoTracking().OrderBy(s => s.Id).ToListAsync(cancellationToken);
    }

    public async Task<KnownSite> AddAsync(KnownSite site, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.KnownSites.Add(site);
        await db.SaveChangesAsync(cancellationToken);
        return site;
    }

    public async Task UpdateAsync(KnownSite site, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.KnownSites.Update(site);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var pageIds = await db.SitePages.Where(p => p.SiteId == id).Select(p => p.Id).ToListAsync(cancellationToken);
        await db.SiteChunks.Where(c => pageIds.Contains(c.PageId)).ExecuteDeleteAsync(cancellationToken);
        await db.SitePages.Where(p => p.SiteId == id).ExecuteDeleteAsync(cancellationToken);
        return await db.KnownSites.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<SitePage>> PagesAsync(long siteId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.SitePages.AsNoTracking().Where(p => p.SiteId == siteId).ToListAsync(cancellationToken);
    }

    public async Task SavePageAsync(SitePage page, IReadOnlyList<SiteChunk> chunks, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.SitePages.FirstOrDefaultAsync(p => p.SiteId == page.SiteId && p.Url == page.Url, cancellationToken);
        long pageId;
        if (existing is null)
        {
            var added = new SitePage { SiteId = page.SiteId, Url = page.Url, Title = page.Title, ContentHash = page.ContentHash, IsPdf = page.IsPdf, UpdatedAtUtc = page.UpdatedAtUtc };
            db.SitePages.Add(added);
            await db.SaveChangesAsync(cancellationToken);
            pageId = added.Id;
        }
        else
        {
            existing.Title = page.Title;
            existing.ContentHash = page.ContentHash;
            existing.IsPdf = page.IsPdf;
            existing.UpdatedAtUtc = page.UpdatedAtUtc;
            await db.SaveChangesAsync(cancellationToken);
            pageId = existing.Id;
            await db.SiteChunks.Where(c => c.PageId == pageId).ExecuteDeleteAsync(cancellationToken);
        }
        db.SiteChunks.AddRange(chunks.Select(c => new SiteChunk { PageId = pageId, Ordinal = c.Ordinal, Text = c.Text, Embedding = c.Embedding }));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task RemovePagesAsync(IReadOnlyCollection<long> pageIds, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.SiteChunks.Where(c => pageIds.Contains(c.PageId)).ExecuteDeleteAsync(cancellationToken);
        await db.SitePages.Where(p => pageIds.Contains(p.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(SiteChunk Chunk, SitePage Page)>> ChunksAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var active = db.KnownSites.Where(s => s.IsActive).Select(s => s.Id);
        var pages = await db.SitePages.AsNoTracking().Where(p => active.Contains(p.SiteId)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var chunks = await db.SiteChunks.AsNoTracking().Where(c => pages.Keys.Contains(c.PageId)).ToListAsync(cancellationToken);
        return chunks.Select(c => (c, pages[c.PageId])).ToList();
    }
}
