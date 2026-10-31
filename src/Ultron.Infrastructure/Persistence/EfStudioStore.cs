using Microsoft.EntityFrameworkCore;
using Ultron.Core.Studio;

namespace Ultron.Infrastructure.Persistence;

internal sealed class EfStudioStore(IDbContextFactory<UltronDbContext> dbFactory) : IStudioStore
{
    public async Task<StudioProject> AddProjectAsync(StudioProject project, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.StudioProjects.Add(project);
        await db.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task UpdateProjectAsync(StudioProject project, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.StudioProjects.Update(project);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StudioProject?> GetProjectAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.StudioProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<bool> DeleteProjectAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.StudioMessages.Where(m => m.ProjectId == id).ExecuteDeleteAsync(cancellationToken);
        await db.StudioVersions.Where(v => v.ProjectId == id).ExecuteDeleteAsync(cancellationToken);
        return await db.StudioProjects.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<StudioProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.StudioProjects.AsNoTracking().OrderByDescending(p => p.UpdatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<StudioVersion> AddVersionAsync(StudioVersion version, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.StudioVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);
        return version;
    }

    public async Task UpdateVersionAsync(StudioVersion version, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.StudioVersions.Update(version);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<StudioVersion?> GetVersionAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.StudioVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<StudioVersion>> GetVersionsAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.StudioVersions.AsNoTracking().Where(v => v.ProjectId == projectId).OrderBy(v => v.Number).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StudioVersion>> GetUnreviewedAsync(int max, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.StudioVersions.AsNoTracking()
            .Where(v => v.ReviewedAtUtc == null && v.Source != StudioSource.Claude)
            .OrderByDescending(v => v.Id).Take(max).ToListAsync(cancellationToken);
    }

    public async Task<StudioMessage> AddMessageAsync(StudioMessage message, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.StudioMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task<IReadOnlyList<StudioMessage>> GetMessagesAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.StudioMessages.AsNoTracking().Where(m => m.ProjectId == projectId).OrderBy(m => m.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StudioRecipe>> GetRecipesAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.StudioRecipes.AsNoTracking();
        if (activeOnly)
            query = query.Where(r => r.IsActive);
        return await query.OrderBy(r => r.Id).ToListAsync(cancellationToken);
    }

    public async Task<StudioRecipe> AddRecipeAsync(StudioRecipe recipe, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.StudioRecipes.Add(recipe);
        await db.SaveChangesAsync(cancellationToken);
        return recipe;
    }

    public async Task UpdateRecipeAsync(StudioRecipe recipe, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.StudioRecipes.Update(recipe);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DesignRule>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.DesignRules.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Id).ToListAsync(cancellationToken);
    }

    public async Task<DesignRule> AddRuleAsync(DesignRule rule, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.DesignRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<bool> DeactivateRuleAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.DesignRules.Where(r => r.Id == id && r.IsActive).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false), cancellationToken) > 0;
    }
}
