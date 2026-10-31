using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Ultron.Core.Memories;
using Ultron.Core.Text;

namespace Ultron.Infrastructure.Persistence;

internal sealed class EfCategoryStore(IDbContextFactory<UltronDbContext> dbFactory) : ICategoryStore
{
    public async Task<IReadOnlyList<MemoryCategory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
    }

    public async Task<MemoryCategory> AddAsync(string name, string description, string drawer, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Categories.ToListAsync(cancellationToken);

        var duplicate = existing.FirstOrDefault(c => SameName(c.Name, name));
        if (duplicate is not null)
            return duplicate;

        var category = new MemoryCategory
        {
            Key = UniqueKey(name, existing.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)),
            Name = name.Trim(),
            Description = description.Trim(),
            Drawer = MemoryDrawers.Find(drawer)?.Key ?? MemoryDrawers.Patron,
            SortOrder = existing.Count == 0 ? 1 : existing.Max(c => c.SortOrder) + 1,
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task UpdateAsync(string key, string name, string description, bool isCore, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.FindAsync([key], cancellationToken)
            ?? throw new InvalidOperationException($"Kategori bulunamadı: {key}");

        // Anahtar değişmez: kayıtlar ona bağlı. Sadece görünen ad, açıklama ve çekirdek bayrağı değişir.
        category.Name = name.Trim();
        category.Description = description.Trim();
        category.IsCore = isCore;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteIfEmptyAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.FindAsync([key], cancellationToken);
        if (category is null || category.IsBuiltIn || await db.Memories.AnyAsync(m => m.Category == key && m.IsActive, cancellationToken))
            return false;

        await db.Memories.Where(m => m.Category == key).ExecuteDeleteAsync(cancellationToken);
        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<CategorySuggestion>> GetPendingSuggestionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CategorySuggestions.AsNoTracking()
            .Where(s => s.Status == SuggestionStatus.Pending)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddSuggestionAsync(string name, string description, string? example, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var categoryNames = await db.Categories.Select(c => c.Name).ToListAsync(cancellationToken);
        // Reddedilenler de sayılır: kullanıcı "hayır" dediği öneriyi tekrar tekrar görmesin.
        var suggested = await db.CategorySuggestions.Select(s => s.Name).ToListAsync(cancellationToken);
        if (categoryNames.Concat(suggested).Any(n => SameName(n, name)))
            return;

        db.CategorySuggestions.Add(new CategorySuggestion { Name = name.Trim(), Description = description.Trim(), Example = example });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MemoryCategory?> ResolveSuggestionAsync(long id, bool accept, CancellationToken cancellationToken = default)
    {
        CategorySuggestion suggestion;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            suggestion = await db.CategorySuggestions.FindAsync([id], cancellationToken)
                ?? throw new InvalidOperationException($"Öneri bulunamadı: {id}");
            suggestion.Status = accept ? SuggestionStatus.Accepted : SuggestionStatus.Rejected;
            await db.SaveChangesAsync(cancellationToken);
        }

        return accept ? await AddAsync(suggestion.Name, suggestion.Description, MemoryDrawers.Patron, cancellationToken) : null;
    }

    private static bool SameName(string a, string b) =>
        TurkishText.Normalize(a) == TurkishText.Normalize(b);

    /// <summary>"Araba & Motor" → "ArabaMotor"; çakışırsa "ArabaMotor2".</summary>
    internal static string UniqueKey(string name, IReadOnlySet<string> taken)
    {
        var words = TurkishText.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var key = string.Concat(words.Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
        if (key.Length == 0)
            key = "Kategori";
        if (key.Length > 40)
            key = key[..40];

        var candidate = key;
        for (var i = 2; taken.Contains(candidate); i++)
            candidate = key + i;
        return candidate;
    }
}
