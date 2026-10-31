namespace Ultron.Core.Studio;

/// <summary>
/// İş arayüzü (Faz 16): Ultron'un React + Tailwind bileşen ürettiği çalışma odası. Benzetme: çırak (Ultron) tarif defterine
/// bakarak dener, bilmediğini ustaya (Claude Code) sorar ve ustanın yaptığını deftere yazar; müşteri (kullanıcı) beğenir ya da
/// "şunu sevmedim" der, bu da zevk profiline kural olur.
/// </summary>
public sealed class StudioProject
{
    public long Id { get; init; }

    public required string Name { get; set; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Önizlemede gösterilen sürüm (son üretilen ya da kullanıcının geri döndüğü).</summary>
    public long? CurrentVersionId { get; set; }
}

/// <summary>Bileşeni kim yazdı.</summary>
public enum StudioSource
{
    Ultron,

    /// <summary>Otomatik kontrolden sonra Ultron'un kendi düzeltmesi.</summary>
    Duzeltme,

    /// <summary>Kullanıcının onayıyla Claude Code yazdı.</summary>
    Claude,
}

/// <summary>Bileşenin bir sürümü: her istek ("butonu sağa al") yeni sürüm, geri dönülebilir.</summary>
public sealed class StudioVersion
{
    public long Id { get; init; }

    public long ProjectId { get; init; }

    public int Number { get; init; }

    /// <summary>Bu sürümü doğuran istek.</summary>
    public required string Request { get; init; }

    public required string Code { get; init; }

    public string? Explanation { get; init; }

    public StudioSource Source { get; init; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Önizlemedeki otomatik kontrol (hata, telefonda taşma, erişilebilirlik); null = henüz kontrol edilmedi.</summary>
    public bool? ChecksPassed { get; set; }

    public string? CheckReport { get; set; }

    /// <summary>Kullanıcı: 1 👍, -1 👎, 0 yok.</summary>
    public int Rating { get; set; }

    public string? Feedback { get; set; }

    /// <summary>Gece denetiminde Claude'un incelediği an.</summary>
    public DateTime? ReviewedAtUtc { get; set; }
}

/// <summary>İş sohbetindeki bir mesaj (ana sohbetten ayrı: Ultron'un "son konuşma"sını bozmasın).</summary>
public sealed class StudioMessage
{
    public long Id { get; init; }

    public long ProjectId { get; init; }

    public bool FromUser { get; init; }

    public required string Content { get; init; }

    /// <summary>Asistan mesajı bir sürüm ürettiyse.</summary>
    public long? VersionId { get; init; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Tarifin nereden geldiği.</summary>
public enum RecipeKind
{
    /// <summary>Ultron'la gelen, Claude'un yazdığı bileşen (notlar/bilesenler).</summary>
    Hazir,

    /// <summary>Claude Code'un kullanıcının isteğiyle yaptığı: bir dahakine Ultron kendisi yapar.</summary>
    Claude,

    /// <summary>Kullanıcının 👍 verdiği bileşen.</summary>
    Favori,

    /// <summary>Kullanıcının verdiği referans (kod, tarif ya da görselin anlatımı).</summary>
    Referans,

    /// <summary>Gece denetiminde Claude'un düzelttiği bileşen.</summary>
    Denetim,
}

/// <summary>Tarif defterindeki bir bileşen.</summary>
public sealed class StudioRecipe
{
    public long Id { get; init; }

    /// <summary>Kalıcı ad (hazır tariflerde dosya adı): güncellemede aynı kayıt değişir.</summary>
    public required string Slug { get; init; }

    public required string Title { get; set; }

    public string Tags { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>React + Tailwind kodu (referans anlatımında boş olabilir).</summary>
    public string Code { get; set; } = "";

    public RecipeKind Kind { get; init; }

    public string ContentHash { get; set; } = "";

    public float[] Embedding { get; set; } = [];

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Zevk profilindeki bir kural ("köşeler daha yuvarlak olsun"): her üretimde uygulanır.</summary>
public sealed class DesignRule
{
    public long Id { get; init; }

    public required string Text { get; init; }

    /// <summary>"kullanıcı" (👎 notu ya da elle) veya "Gece denetimi".</summary>
    public required string Source { get; init; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed record StudioStats(int Versions, int ByUltron, int ByClaude, int ThumbsUp, int ThumbsDown, int Recipes, int LearnedRecipes, int Rules);

public interface IStudioStore
{
    Task<StudioProject> AddProjectAsync(StudioProject project, CancellationToken cancellationToken = default);

    Task UpdateProjectAsync(StudioProject project, CancellationToken cancellationToken = default);

    Task<StudioProject?> GetProjectAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Projeyi sohbeti ve sürümleriyle siler (tarif defterine geçmiş olanlar kalır).</summary>
    Task<bool> DeleteProjectAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioProject>> GetProjectsAsync(CancellationToken cancellationToken = default);

    Task<StudioVersion> AddVersionAsync(StudioVersion version, CancellationToken cancellationToken = default);

    Task UpdateVersionAsync(StudioVersion version, CancellationToken cancellationToken = default);

    Task<StudioVersion?> GetVersionAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioVersion>> GetVersionsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Gece denetimi: henüz incelenmemiş, Ultron'un (Claude değil) yazdığı sürümler.</summary>
    Task<IReadOnlyList<StudioVersion>> GetUnreviewedAsync(int max, CancellationToken cancellationToken = default);

    Task<StudioMessage> AddMessageAsync(StudioMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioMessage>> GetMessagesAsync(long projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioRecipe>> GetRecipesAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<StudioRecipe> AddRecipeAsync(StudioRecipe recipe, CancellationToken cancellationToken = default);

    Task UpdateRecipeAsync(StudioRecipe recipe, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DesignRule>> GetRulesAsync(CancellationToken cancellationToken = default);

    Task<DesignRule> AddRuleAsync(DesignRule rule, CancellationToken cancellationToken = default);

    Task<bool> DeactivateRuleAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>İş arayüzüne haber: Claude Code'un ilerlemesi ve gece denetiminin sonucu.</summary>
public interface IStudioNotifier
{
    /// <param name="version">İş bittiyse yeni sürüm; aksi halde null (ilerleme ya da hata).</param>
    Task ClaudeProgressAsync(long projectId, string status, bool finished, StudioVersion? version, CancellationToken cancellationToken = default);

    Task ReviewedAsync(int reviewed, int recipes, int rules, CancellationToken cancellationToken = default);
}

public sealed class LogStudioNotifier : IStudioNotifier
{
    public Task ClaudeProgressAsync(long projectId, string status, bool finished, StudioVersion? version, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReviewedAsync(int reviewed, int recipes, int rules, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Üretim akışının olayları (hub üzerinden İş arayüzüne akar).</summary>
public abstract record StudioEvent;

/// <param name="Recipes">Bağlama giren tarifler.</param>
/// <param name="KnownPattern">Defterde bu isteğe yeterince benzeyen tarif var mı (yoksa "Claude'a yaptırayım mı?" önerilir).</param>
public sealed record StudioStarted(IReadOnlyList<string> Recipes, bool KnownPattern) : StudioEvent;

public sealed record StudioChunk(string Text) : StudioEvent;

public sealed record StudioCompleted(StudioVersion Version, string Explanation) : StudioEvent;

public sealed record StudioFailed(string Message) : StudioEvent;
