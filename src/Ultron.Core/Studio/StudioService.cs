using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Chat;
using Ultron.Core.Coding;
using Ultron.Core.Conversations;
using Ultron.Core.Embeddings;
using Ultron.Core.Settings;

namespace Ultron.Core.Studio;

/// <summary>appsettings "Studio" bölümü.</summary>
public sealed class StudioOptions
{
    public const string SectionName = "Studio";

    /// <summary>Ultron'la gelen tarifler (Claude'un yazdığı React + Tailwind bileşenleri).</summary>
    public string RecipeFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "notlar", "bilesenler");

    /// <summary>Claude Code'un çalıştığı proje klasörleri.</summary>
    public string WorkFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "is");

    /// <summary>
    /// İstek–tarif benzerliği bundan düşükse defterde benzer tarif yok sayılır ("Claude'a yaptırayım mı?" önerilir) ve bağlama
    /// tarif konmaz (ilgisiz örnek küçük modeli yanıltır). Ölçüm (2026-10-28, 45 tarif, 15 istek): bilinen türler 0,57-0,86
    /// ("koyu temalı üst menü" 0,568), defterde olmayanlar (kanban, takvim, carousel…) en fazla 0,560.
    /// </summary>
    public double KnownMinScore { get; set; } = 0.565;

    /// <summary>Bağlama giren tarif sayısı.</summary>
    public int RecipesPerRequest { get; set; } = 2;

    /// <summary>Gece denetiminde en fazla kaç proje incelenir.</summary>
    public int MaxReviewPerNight { get; set; } = 15;

    /// <summary>Gece denetiminden bir gecede en fazla kaç yeni zevk kuralı gelir (profil şişmesin).</summary>
    public int MaxNewRulesPerNight { get; set; } = 3;
}

/// <summary>
/// İş arayüzünün beyni (Faz 16): istek → tarif defteri + zevk profili → gemma4 bileşeni yazar (akarak, önizleme canlı) →
/// otomatik kontrol (masaüstünde) → gerekirse kendi düzeltmesi → bilmediğini kullanıcının onayıyla Claude Code'a yaptırır ve
/// deftere yazar. Geri bildirim (👍/👎 + not) zevk profiline, gece denetimi deftere ve profile işler.
/// </summary>
public sealed class StudioService(
    IStudioStore store,
    IChatModel chat,
    IEmbeddingModel embedder,
    ICodeAgent agent,
    IStudioNotifier notifier,
    UltronSettings settings,
    IOptions<StudioOptions> options,
    TimeProvider time,
    ILogger<StudioService> logger,
    Library.LibraryService? library = null)
{
    public const string LastReviewKey = "atolye_denetim_son";

    private readonly SemaphoreSlim _seedGate = new(1, 1);
    private readonly SemaphoreSlim _claudeGate = new(1, 1);
    private readonly SemaphoreSlim _reviewGate = new(1, 1);
    private bool _seeded;

    public bool ClaudeBusy => _claudeGate.CurrentCount == 0;

    // ---- Projeler ----

    public Task<IReadOnlyList<StudioProject>> ProjectsAsync(CancellationToken cancellationToken = default) => store.GetProjectsAsync(cancellationToken);

    public Task<bool> DeleteProjectAsync(long projectId, CancellationToken cancellationToken = default) => store.DeleteProjectAsync(projectId, cancellationToken);

    public Task<StudioProject> CreateProjectAsync(string name, CancellationToken cancellationToken = default) =>
        store.AddProjectAsync(new StudioProject { Name = string.IsNullOrWhiteSpace(name) ? "Yeni bileşen" : Clip(name.Trim(), 80) }, cancellationToken);

    public async Task<(StudioProject Project, IReadOnlyList<StudioMessage> Messages, IReadOnlyList<StudioVersion> Versions)?> OpenAsync(long projectId, CancellationToken cancellationToken = default)
    {
        if (await store.GetProjectAsync(projectId, cancellationToken) is not { } project)
            return null;
        return (project, await store.GetMessagesAsync(projectId, cancellationToken), await store.GetVersionsAsync(projectId, cancellationToken));
    }

    /// <summary>Eski bir sürüme dön: önizlemede o gösterilir, sonraki değişiklik onun üstüne yapılır.</summary>
    public async Task<bool> SelectVersionAsync(long projectId, long versionId, CancellationToken cancellationToken = default)
    {
        if (await store.GetProjectAsync(projectId, cancellationToken) is not { } project
            || await store.GetVersionAsync(versionId, cancellationToken) is not { } version || version.ProjectId != projectId)
            return false;
        project.CurrentVersionId = versionId;
        project.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await store.UpdateProjectAsync(project, cancellationToken);
        return true;
    }

    // ---- Üretim ----

    /// <summary>Yeni bileşen ya da değişiklik: model yazdıkça parçalar akar, sonunda sürüm kaydedilir.</summary>
    public async IAsyncEnumerable<StudioEvent> GenerateAsync(long projectId, string request, byte[]? image = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (await store.GetProjectAsync(projectId, cancellationToken) is not { } project)
        {
            yield return new StudioFailed("Proje bulunamadı.");
            yield break;
        }

        var current = project.CurrentVersionId is { } cv ? await store.GetVersionAsync(cv, cancellationToken) : null;
        var history = await store.GetMessagesAsync(projectId, cancellationToken);
        var firstRequest = history.FirstOrDefault(m => m.FromUser)?.Content;
        await store.AddMessageAsync(new StudioMessage { ProjectId = projectId, FromUser = true, Content = request.Length > 0 ? request : "(görsel)" }, cancellationToken);
        if (current is null && history.Count == 0 && project.Name == "Yeni bileşen" && request.Length > 0)
        {
            project.Name = await TitleAsync(request, cancellationToken); // ilk istekten konu başlığı (2026-10-31)
            await store.UpdateProjectAsync(project, cancellationToken);
        }

        var searchText = current is null ? request : $"{firstRequest} {request}";
        var (recipes, best) = await FindRecipesAsync(searchText, current is null ? options.Value.RecipesPerRequest : 1, cancellationToken);
        var known = current is not null || best >= options.Value.KnownMinScore;
        var reference = current is null ? await FlowbiteReferenceAsync(request, cancellationToken) : null;
        yield return new StudioStarted(recipes.Select(r => r.Title).ToList(), known);

        await foreach (var e in RunModelAsync(project, request, recipes, current, reference, image, history, StudioSource.Ultron, cancellationToken))
            yield return e;
    }

    /// <summary>Otomatik kontrol sorun bulduysa Ultron bir kez kendisi düzeltir (masaüstü çağırır).</summary>
    public async IAsyncEnumerable<StudioEvent> FixAsync(long projectId, long versionId, string report,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (await store.GetProjectAsync(projectId, cancellationToken) is not { } project
            || await store.GetVersionAsync(versionId, cancellationToken) is not { } version)
        {
            yield return new StudioFailed("Sürüm bulunamadı.");
            yield break;
        }
        var history = await store.GetMessagesAsync(projectId, cancellationToken);
        yield return new StudioStarted([], true);
        await foreach (var e in RunModelAsync(project, StudioPrompts.FixRequest(report), [], version, null, null, history, StudioSource.Duzeltme, cancellationToken))
            yield return e;
    }

    private async IAsyncEnumerable<StudioEvent> RunModelAsync(StudioProject project, string request, IReadOnlyList<StudioRecipe> recipes,
        StudioVersion? current, string? reference, byte[]? image, IReadOnlyList<StudioMessage> history, StudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rules = await store.GetRulesAsync(cancellationToken);
        var messages = new List<ChatMessage> { new(Role.System, StudioPrompts.System(rules)) };
        // Önceki konuşma kodsuz (kod "mevcut bileşen" olarak ayrıca veriliyor): bağlam kısa kalsın.
        foreach (var m in history.TakeLast(6))
            messages.Add(new ChatMessage(m.FromUser ? Role.User : Role.Assistant, Clip(m.Content, 600)));
        var userText = StudioPrompts.User(request.Length > 0 ? request : "Görseldeki arayüzü React + Tailwind bileşeni olarak yap.", recipes, current, reference);
        messages.Add(new ChatMessage(Role.User, userText, Images: image is null ? null : [image]));

        var answer = new StringBuilder();
        var stream = chat.StreamAsync(messages, cancellationToken).GetAsyncEnumerator(cancellationToken);
        string? error = null;
        while (true)
        {
            try
            {
                if (!await stream.MoveNextAsync())
                    break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "İş modu: model cevabı yarıda kaldı");
                error = "Model cevabı yarıda kaldı: " + ex.Message;
                break;
            }
            answer.Append(stream.Current);
            yield return new StudioChunk(stream.Current);
        }
        await stream.DisposeAsync();

        var (code, explanation) = StudioCode.Extract(answer.ToString());
        if (code is null || code.Length < 40)
        {
            var message = error ?? "Kod üretilemedi; isteği biraz daha açık yazar mısın?";
            await store.AddMessageAsync(new StudioMessage { ProjectId = project.Id, FromUser = false, Content = explanation.Length > 0 ? explanation : message }, cancellationToken);
            yield return new StudioFailed(message);
            yield break;
        }

        var version = await SaveVersionAsync(project, request, code, explanation, source, cancellationToken);
        await store.AddMessageAsync(new StudioMessage
        {
            ProjectId = project.Id, FromUser = false, VersionId = version.Id,
            Content = explanation.Length > 0 ? explanation : $"Sürüm {version.Number} hazır.",
        }, cancellationToken);
        yield return new StudioCompleted(version, explanation);
    }

    private async Task<StudioVersion> SaveVersionAsync(StudioProject project, string request, string code, string? explanation, StudioSource source, CancellationToken cancellationToken)
    {
        var versions = await store.GetVersionsAsync(project.Id, cancellationToken);
        var version = await store.AddVersionAsync(new StudioVersion
        {
            ProjectId = project.Id, Number = versions.Count == 0 ? 1 : versions.Max(v => v.Number) + 1,
            Request = Clip(request, 2000), Code = code, Explanation = explanation, Source = source,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        }, cancellationToken);
        project.CurrentVersionId = version.Id;
        project.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await store.UpdateProjectAsync(project, cancellationToken);
        return version;
    }

    /// <summary>Masaüstündeki önizleme kontrolünün sonucu (gece denetimi ve istatistik için).</summary>
    public async Task ReportCheckAsync(long versionId, bool passed, string report, CancellationToken cancellationToken = default)
    {
        if (await store.GetVersionAsync(versionId, cancellationToken) is not { } version)
            return;
        version.ChecksPassed = passed;
        version.CheckReport = Clip(report, 2000);
        await store.UpdateVersionAsync(version, cancellationToken);
    }

    // ---- Tarif defteri ----

    /// <summary>İsteğe en yakın tarifler ve en iyi benzerlik.</summary>
    public async Task<(IReadOnlyList<StudioRecipe> Recipes, double Best)> FindRecipesAsync(string request, int count, CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var recipes = (await store.GetRecipesAsync(true, cancellationToken)).Where(r => r.Embedding.Length > 0).ToList();
        if (recipes.Count == 0 || string.IsNullOrWhiteSpace(request))
            return ([], 0);
        var query = (await embedder.EmbedAsync([request], EmbeddingPurpose.Query, cancellationToken))[0];
        var scored = recipes
            .Where(r => r.Embedding.Length == query.Length)
            .Select(r => (Recipe: r, Score: VectorMath.Cosine(query, r.Embedding) + KindBoost(r.Kind)))
            .OrderByDescending(x => x.Score).ToList();
        if (scored.Count == 0)
            return ([], 0);
        var useful = scored.Where(x => x.Score >= options.Value.KnownMinScore && (x.Recipe.Code.Length > 0 || x.Recipe.Kind == RecipeKind.Referans));
        return (useful.Take(count).Select(x => x.Recipe).ToList(), scored[0].Score);
    }

    /// <summary>Kullanıcının beğendiği ve ustanın yaptığı tarifler, eşit benzerlikte hazır tariften önce gelsin.</summary>
    private static double KindBoost(RecipeKind kind) => kind switch
    {
        RecipeKind.Favori or RecipeKind.Referans => 0.04,
        RecipeKind.Claude or RecipeKind.Denetim => 0.02,
        _ => 0,
    };

    /// <summary>Ultron'la gelen tarifleri deftere alır (ilk kullanımda; dosya değiştiyse günceller).</summary>
    public async Task<int> EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        if (_seeded)
            return 0;
        await _seedGate.WaitAsync(cancellationToken);
        try
        {
            if (_seeded)
                return 0;
            var folder = options.Value.RecipeFolder;
            if (!Directory.Exists(folder))
            {
                logger.LogWarning("Tarif klasörü yok: {Folder}", folder);
                _seeded = true;
                return 0;
            }
            var existing = (await store.GetRecipesAsync(false, cancellationToken)).Where(r => r.Kind == RecipeKind.Hazir).ToDictionary(r => r.Slug);
            var changed = 0;
            foreach (var file in Directory.EnumerateFiles(folder, "*.md").Order())
            {
                var slug = Path.GetFileNameWithoutExtension(file);
                var parsed = StudioCode.ParseRecipe(slug, await File.ReadAllTextAsync(file, cancellationToken));
                if (parsed is null)
                {
                    logger.LogWarning("Tarif okunamadı: {File}", file);
                    continue;
                }
                if (existing.TryGetValue(slug, out var old) && old.ContentHash == parsed.ContentHash && old.Embedding.Length > 0)
                    continue;
                var vector = (await embedder.EmbedAsync([StudioCode.SearchText(parsed)], EmbeddingPurpose.Document, cancellationToken))[0];
                if (old is not null)
                {
                    old.Title = parsed.Title;
                    old.Tags = parsed.Tags;
                    old.Description = parsed.Description;
                    old.Code = parsed.Code;
                    old.ContentHash = parsed.ContentHash;
                    old.Embedding = vector;
                    await store.UpdateRecipeAsync(old, cancellationToken);
                }
                else
                {
                    parsed.Embedding = vector;
                    await store.AddRecipeAsync(parsed, cancellationToken);
                }
                changed++;
            }
            _seeded = true;
            if (changed > 0)
                logger.LogInformation("Tarif defteri: {Count} hazır tarif eklendi/güncellendi", changed);
            return changed;
        }
        finally
        {
            _seedGate.Release();
        }
    }

    /// <summary>Kullanıcının referansı: kod, tarif ya da görsel (görseli model anlatır, anlatım kaydedilir).</summary>
    public async Task<StudioRecipe> AddReferenceAsync(string title, string text, byte[]? image, CancellationToken cancellationToken = default)
    {
        var description = text.Trim();
        if (image is not null)
        {
            var seen = new StringBuilder();
            await foreach (var part in chat.StreamAsync([
                new ChatMessage(Role.System, "Bir arayüz tasarımcısısın. Görseldeki arayüz parçasını tasarım açısından Türkçe ve kısa madde madde anlat: düzen, renkler, köşeler, boşluklar, yazı, etkileşim ipuçları."),
                new ChatMessage(Role.User, description.Length > 0 ? description : "Bu görseli anlat.", Images: [image]),
            ], cancellationToken))
                seen.Append(part);
            description = (description + "\n\nGörsel referansın anlatımı:\n" + seen).Trim();
        }
        var (code, explanation) = StudioCode.Extract(description);
        return await AddLearnedRecipeAsync(RecipeKind.Referans, string.IsNullOrWhiteSpace(title) ? "Kullanıcının referansı" : title.Trim(),
            code is null ? description : explanation, code ?? "", cancellationToken);
    }

    private async Task<StudioRecipe> AddLearnedRecipeAsync(RecipeKind kind, string title, string description, string code, CancellationToken cancellationToken)
    {
        var recipe = new StudioRecipe
        {
            Slug = $"{kind.ToString().ToLowerInvariant()}-{time.GetUtcNow():yyyyMMddHHmmssfff}",
            Title = Clip(title, 120), Description = Clip(description, 3000), Code = code, Kind = kind,
            ContentHash = StudioCode.Hash(title + description + code), CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        recipe.Embedding = (await embedder.EmbedAsync([StudioCode.SearchText(recipe)], EmbeddingPurpose.Document, cancellationToken))[0];
        return await store.AddRecipeAsync(recipe, cancellationToken);
    }

    /// <summary>Öğrenilen (hazır olmayan) tarifler: zevk panelinde, "Geri al" ile kaldırılabilir.</summary>
    public async Task<IReadOnlyList<StudioRecipe>> LearnedRecipesAsync(CancellationToken cancellationToken = default) =>
        (await store.GetRecipesAsync(true, cancellationToken)).Where(r => r.Kind != RecipeKind.Hazir).OrderByDescending(r => r.Id).ToList();

    public async Task<bool> RemoveRecipeAsync(long id, CancellationToken cancellationToken = default)
    {
        var recipe = (await store.GetRecipesAsync(true, cancellationToken)).FirstOrDefault(r => r.Id == id);
        if (recipe is null || recipe.Kind == RecipeKind.Hazir)
            return false;
        recipe.IsActive = false;
        await store.UpdateRecipeAsync(recipe, cancellationToken);
        return true;
    }

    // ---- Zevk profili ve geri bildirim ----

    public Task<IReadOnlyList<DesignRule>> RulesAsync(CancellationToken cancellationToken = default) => store.GetRulesAsync(cancellationToken);

    public async Task<DesignRule?> AddRuleAsync(string text, string source = "kullanıcı", CancellationToken cancellationToken = default)
    {
        text = text.Trim();
        if (text.Length < 3)
            return null;
        var existing = await store.GetRulesAsync(cancellationToken);
        if (existing.FirstOrDefault(r => r.IsActive && Text.TurkishText.Normalize(r.Text) == Text.TurkishText.Normalize(text)) is { } same)
            return same;
        return await store.AddRuleAsync(new DesignRule { Text = Clip(text, 300), Source = source, CreatedAtUtc = time.GetUtcNow().UtcDateTime }, cancellationToken);
    }

    public Task<bool> RemoveRuleAsync(long id, CancellationToken cancellationToken = default) => store.DeactivateRuleAsync(id, cancellationToken);

    /// <summary>👍: bileşen favori tarif olur. 👎 + not: not zevk kuralı olur. Dönen metin kullanıcıya gösterilir.</summary>
    public async Task<string> RateAsync(long versionId, int rating, string? note, CancellationToken cancellationToken = default)
    {
        if (await store.GetVersionAsync(versionId, cancellationToken) is not { } version)
            return "Sürüm bulunamadı.";
        version.Rating = Math.Sign(rating);
        version.Feedback = string.IsNullOrWhiteSpace(note) ? null : Clip(note.Trim(), 500);
        await store.UpdateVersionAsync(version, cancellationToken);

        if (version.Rating > 0)
        {
            var project = await store.GetProjectAsync(version.ProjectId, cancellationToken);
            await AddLearnedRecipeAsync(RecipeKind.Favori, project?.Name ?? version.Request, version.Request, version.Code, cancellationToken);
            return "Beğendin: bu bileşen tarif defterine favori olarak eklendi; benzer isteklerde örnek alınacak.";
        }
        if (version.Rating < 0 && version.Feedback is { } feedback)
        {
            await AddRuleAsync(feedback, "kullanıcı", cancellationToken);
            return $"Not zevk profiline kural olarak eklendi: \"{feedback}\". Bundan sonraki bütün tasarımlarda uygulanacak.";
        }
        return version.Rating < 0 ? "Not aldım. Neyi beğenmediğini yazarsan kural olarak eklerim." : "Puan kaldırıldı.";
    }

    public async Task<StudioStats> StatsAsync(CancellationToken cancellationToken = default)
    {
        var projects = await store.GetProjectsAsync(cancellationToken);
        var versions = new List<StudioVersion>();
        foreach (var p in projects)
            versions.AddRange(await store.GetVersionsAsync(p.Id, cancellationToken));
        var recipes = await store.GetRecipesAsync(true, cancellationToken);
        var rules = await store.GetRulesAsync(cancellationToken);
        return new StudioStats(versions.Count, versions.Count(v => v.Source != StudioSource.Claude), versions.Count(v => v.Source == StudioSource.Claude),
            versions.Count(v => v.Rating > 0), versions.Count(v => v.Rating < 0), recipes.Count, recipes.Count(r => r.Kind != RecipeKind.Hazir), rules.Count(r => r.IsActive));
    }

    // ---- Usta: Claude Code ----

    /// <summary>
    /// kullanıcının "Claude'a yaptır" onayıyla: Claude Code bileşeni proje klasöründe yazar (sadece dosya okuma/yazma, komut yok);
    /// sonuç yeni sürüm olur ve tarif defterine geçer. Arka planda çalışır; ilerleme bildirimle gelir.
    /// </summary>
    public async Task<string> StartClaudeAsync(long projectId, string? request, CancellationToken cancellationToken = default)
    {
        if (await store.GetProjectAsync(projectId, cancellationToken) is not { } project)
            return "Proje bulunamadı.";
        var status = await agent.GetStatusAsync(cancellationToken);
        if (!status.LoggedIn)
            return status.Message;
        if (!await _claudeGate.WaitAsync(0, cancellationToken))
            return "Claude şu an başka bir bileşen üzerinde çalışıyor.";
        _ = Task.Run(async () =>
        {
            try
            {
                await RunClaudeAsync(project, request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Claude bileşen işi hata verdi");
                await notifier.ClaudeProgressAsync(project.Id, "Claude işi yarıda kaldı: " + ex.Message, true, null);
            }
            finally
            {
                _claudeGate.Release();
            }
        }, CancellationToken.None);
        return "Claude Code bileşen üzerinde çalışmaya başladı; bitince önizlemede görünecek.";
    }

    private async Task RunClaudeAsync(StudioProject project, string? request, CancellationToken cancellationToken)
    {
        var current = project.CurrentVersionId is { } cv ? await store.GetVersionAsync(cv, cancellationToken) : null;
        var messages = await store.GetMessagesAsync(project.Id, cancellationToken);
        var wish = string.IsNullOrWhiteSpace(request)
            ? string.Join("\n", messages.Where(m => m.FromUser).Select(m => "- " + m.Content).TakeLast(6))
            : request.Trim();
        var folder = Path.Combine(options.Value.WorkFolder, $"proje-{project.Id}");
        Directory.CreateDirectory(folder);
        var rules = await store.GetRulesAsync(cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder, "TASARIM.md"), StudioPrompts.System(rules), cancellationToken);
        if (current is not null)
            await File.WriteAllTextAsync(Path.Combine(folder, "mevcut.jsx"), current.Code, cancellationToken);
        var output = Path.Combine(folder, "Bilesen.jsx");
        if (File.Exists(output))
            File.Delete(output);

        var prompt = new StringBuilder()
            .AppendLine("Ultron'un İş arayüzü için bir React + Tailwind bileşeni yazacaksın. Kullanıcının isteği:")
            .AppendLine(wish)
            .AppendLine()
            .AppendLine("TASARIM.md'deki kurallara (özellikle kullanıcının zevk kurallarına) uy." + (current is null ? "" : " mevcut.jsx Ultron'un denemesi; daha iyisini yap."))
            .AppendLine("Sonucu bu klasörde Bilesen.jsx dosyasına yaz: tek dosya, sadece 'react' içe aktarımı, Tailwind v3 sınıfları, satır içi SVG ikonlar, en sonda export default function. Komut çalıştırma.")
            .AppendLine("Bitince tek cümlelik Türkçe özet yaz.")
            .ToString();
        await notifier.ClaudeProgressAsync(project.Id, "Claude Code yazıyor…", false, null, cancellationToken);
        var result = await agent.RunAsync(
            new CodeRunRequest(folder, prompt) { AllowedTools = ["Read", "Write", "Edit", "Glob", "Grep"] },
            step => _ = notifier.ClaudeProgressAsync(project.Id, step, false, null, CancellationToken.None), cancellationToken);

        if (!File.Exists(output))
        {
            await notifier.ClaudeProgressAsync(project.Id, result.Ok ? "Claude dosyayı yazmadı." : "Claude işi yarıda kaldı: " + Clip(result.Text, 200), true, null, cancellationToken);
            return;
        }
        var code = await File.ReadAllTextAsync(output, cancellationToken);
        var version = await SaveVersionAsync(project, "Claude Code: " + Clip(wish, 300), code, Clip(result.Text, 500), StudioSource.Claude, cancellationToken);
        await store.AddMessageAsync(new StudioMessage { ProjectId = project.Id, FromUser = false, VersionId = version.Id, Content = "Claude Code yaptı: " + Clip(result.Text, 500) }, cancellationToken);
        if (StudioCode.Problems(code).Count == 0)
            await AddLearnedRecipeAsync(RecipeKind.Claude, project.Name, wish, code, cancellationToken); // bir dahakine Ultron kendisi yapar
        await notifier.ClaudeProgressAsync(project.Id, "Claude Code bitirdi: sürüm " + version.Number, true, version, cancellationToken);
    }

    // ---- Gece denetimi ----

    /// <summary>Zamanlayıcı çağırır (04:00, gece denetimiyle birlikte): Ultron'un gün içinde yaptığı bileşenler.</summary>
    public async Task<string?> TryReviewAsync(CancellationToken cancellationToken = default)
    {
        var localNow = time.GetLocalNow().DateTime;
        if (DateTime.TryParse(await settings.GetInternalAsync(LastReviewKey, cancellationToken), null, System.Globalization.DateTimeStyles.RoundtripKind, out var last)
            && TimeZoneInfo.ConvertTimeFromUtc(last, time.LocalTimeZone) >= Learning.LearningService.LastSlot(localNow))
            return null;
        return await ReviewAsync(cancellationToken);
    }

    public async Task<string?> ReviewAsync(CancellationToken cancellationToken = default)
    {
        if (!await _reviewGate.WaitAsync(0, cancellationToken))
            return null;
        try
        {
            var pending = await store.GetUnreviewedAsync(200, cancellationToken);
            // Her projenin son hali incelenir; ara sürümler de "incelendi" sayılır.
            var latest = pending.GroupBy(v => v.ProjectId).Select(g => g.OrderByDescending(v => v.Number).First())
                .OrderByDescending(v => v.Id).Take(options.Value.MaxReviewPerNight).ToList();
            if (latest.Count == 0)
            {
                await MarkReviewedAsync(cancellationToken);
                return "İncelenecek yeni bileşen yok.";
            }
            var status = await agent.GetStatusAsync(cancellationToken);
            if (!status.LoggedIn)
                return "Bileşen denetimi yapılamadı: " + status.Message;

            var folder = Path.Combine(options.Value.WorkFolder, "denetim", time.GetLocalNow().ToString("yyyyMMdd-HHmm"));
            Directory.CreateDirectory(folder);
            var rules = await store.GetRulesAsync(cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, "TASARIM.md"), StudioPrompts.System(rules), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, "girdi.json"), ReviewInput(latest), cancellationToken);
            var result = await agent.RunAsync(new CodeRunRequest(folder, ReviewPrompt(latest.Count)) { AllowedTools = ["Read", "Glob", "Grep"], ReadOnly = true },
                step => logger.LogDebug("Bileşen denetimi: {Step}", step), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, "claude-cevabi.txt"), result.Text, cancellationToken);
            if (!result.Ok || ParseReview(result.Text) is not { } review)
                return "Bileşen denetimi bitti ama sonuç okunamadı (claude-cevabi.txt).";

            int recipes = 0, newRules = 0;
            foreach (var v in latest)
            {
                if (!review.Items.TryGetValue(v.Id, out var item))
                    continue;
                if (item.FixedCode is { Length: > 40 } fixedCode && StudioCode.Problems(fixedCode).Count == 0)
                {
                    await AddLearnedRecipeAsync(RecipeKind.Denetim, item.Title ?? v.Request, item.Description ?? v.Request, fixedCode, cancellationToken);
                    recipes++;
                }
            }
            foreach (var rule in review.Rules.Take(options.Value.MaxNewRulesPerNight))
                if (await AddRuleAsync(rule, "Gece denetimi", cancellationToken) is { Source: "Gece denetimi" } added && added.CreatedAtUtc >= time.GetUtcNow().UtcDateTime.AddMinutes(-5))
                    newRules++;
            foreach (var v in pending.Where(p => latest.Any(l => l.ProjectId == p.ProjectId)))
            {
                v.ReviewedAtUtc = time.GetUtcNow().UtcDateTime;
                await store.UpdateVersionAsync(v, cancellationToken);
            }
            await MarkReviewedAsync(cancellationToken);
            await notifier.ReviewedAsync(latest.Count, recipes, newRules, cancellationToken);
            var report = $"Bileşen denetimi: {latest.Count} bileşen incelendi, {recipes} düzeltilmiş tarif ve {newRules} yeni zevk kuralı öğrenildi.";
            await File.WriteAllTextAsync(Path.Combine(folder, "rapor.md"), report + (review.Report is { Length: > 0 } r ? "\n" + r : ""), cancellationToken);
            return report;
        }
        finally
        {
            _reviewGate.Release();
        }
    }

    private Task MarkReviewedAsync(CancellationToken cancellationToken) =>
        settings.SetInternalAsync(LastReviewKey, time.GetUtcNow().UtcDateTime.ToString("O"), cancellationToken);

    internal static string ReviewInput(IReadOnlyList<StudioVersion> versions) =>
        new JsonObject
        {
            ["bilesenler"] = new JsonArray(versions.Select(v => (JsonNode)new JsonObject
            {
                ["id"] = v.Id, ["istek"] = v.Request, ["kod"] = v.Code,
                ["kullanici_puani"] = v.Rating, ["kullanici_notu"] = v.Feedback, ["otomatik_kontrol"] = v.CheckReport,
            }).ToArray()),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    internal static string ReviewPrompt(int count) => $$"""
        Sen Ultron'un tasarım ustasısın. Ultron (küçük yerel model) bugün kullanıcı için {{count}} React + Tailwind bileşeni yaptı: girdi.json (istek, kod, kullanıcının puanı/notu, otomatik kontrol). TASARIM.md tasarım sistemi ve kullanıcının zevk kuralları.
        Her bileşeni kaliteye göre değerlendir: tasarım sistemine ve zevk kurallarına uyum, mobil uyum, erişilebilirlik, kod doğruluğu, isteğin karşılanması.
        - "iyi": olduğu gibi kalsın.
        - "duzeltilmeli": "duzeltilmis_kod" alanına bileşenin düzeltilmiş TAMAMINI yaz (sadece 'react' içe aktarımı, Tailwind v3, satır içi SVG, export default function). Bu kod Ultron'un tarif defterine girer.
        Ayrıca kullanıcının puan/notlarından ve tekrarlayan hatalardan genel zevk kuralı çıkarabiliyorsan "kurallar" listesine kısa Türkçe cümlelerle yaz (en fazla 3; zaten TASARIM.md'de olanı yazma).
        Dosya yazma, komut çalıştırma; sadece oku. Sonunda SADECE şu JSON'u yaz:
        {"bilesenler":[{"id":1,"karar":"iyi|duzeltilmeli","baslik":"kısa ad","aciklama":"ne işe yarar","duzeltilmis_kod":""}],"kurallar":[],"rapor":"en fazla 2 cümle"}
        """;

    internal sealed record ReviewItem(bool NeedsFix, string? Title, string? Description, string? FixedCode);

    internal sealed record ParsedReview(IReadOnlyDictionary<long, ReviewItem> Items, IReadOnlyList<string> Rules, string? Report);

    internal static ParsedReview? ParseReview(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        try
        {
            var root = JsonNode.Parse(text[start..(end + 1)]);
            if (root?["bilesenler"] is not JsonArray list)
                return null;
            var items = new Dictionary<long, ReviewItem>();
            foreach (var node in list)
            {
                if (node?["id"] is not JsonValue idValue || !(idValue.TryGetValue<long>(out var id) || long.TryParse(idValue.ToString(), out id)))
                    continue;
                var fix = (node["karar"]?.GetValue<string>() ?? "").Contains("duzelt", StringComparison.OrdinalIgnoreCase);
                items[id] = new ReviewItem(fix, node["baslik"]?.GetValue<string>(), node["aciklama"]?.GetValue<string>(), fix ? node["duzeltilmis_kod"]?.GetValue<string>() : null);
            }
            var rules = root["kurallar"] is JsonArray r ? r.Select(x => x?.GetValue<string>()).OfType<string>().Where(s => s.Trim().Length > 2).ToList() : [];
            return new ParsedReview(items, rules, root["rapor"]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // ---- Yardımcılar ----

    /// <summary>Flowbite (MIT) dokümanından isteğe en yakın örnek: sadece fikir için (HTML), varsa.</summary>
    private async Task<string?> FlowbiteReferenceAsync(string request, CancellationToken cancellationToken)
    {
        if (library is null || string.IsNullOrWhiteSpace(request))
            return null;
        try
        {
            var hits = await library.SearchAsync(["tailwind " + request], 12, cancellationToken);
            return hits.FirstOrDefault(h => h.Page.SourceId == "flowbite") is { } hit ? Clip(hit.Chunk.Text, 1500) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Flowbite referansı aranamadı");
            return null;
        }
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private const string TitlePrompt = """
        Kullanıcının bir web bileşeni isteğinden 2-5 kelimelik Türkçe bir oturum adı çıkar: NE yapıldığını anlatan isim tamlaması.
        Örnekler: "bu şekilde admin paneli yap" → "Admin paneli yapımı"; "fiyatlandırma tablosu istiyorum 3 paketli" → "Üç paketli fiyat tablosu";
        "login sayfası lazım koyu temalı" → "Koyu temalı giriş sayfası". Emir kipi, tırnak, nokta ve emoji kullanma.
        """;

    /// <summary>Oturum (proje) adı: istekten konu başlığı (kullanıcı, 2026-10-31: "ilk cümle değil, sorduğum şey"). Model yoksa isteğin başı.</summary>
    internal async Task<string> TitleAsync(string request, CancellationToken cancellationToken)
    {
        try
        {
            var json = await chat.CompleteJsonAsync(
                [new ChatMessage(Role.System, TitlePrompt), new ChatMessage(Role.User, Clip(request, 400))],
                """{"type":"object","properties":{"baslik":{"type":"string"}},"required":["baslik"]}""", 60, cancellationToken);
            if (CleanTitle(System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("baslik").GetString()) is { } title)
                return title;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Oturum adı üretilemedi, isteğin başı kullanılıyor");
        }
        return Clip(request, 60);
    }

    /// <summary>Tırnak/nokta atılır, ilk harf büyür; 3-60 karakter dışıysa null.</summary>
    internal static string? CleanTitle(string? title)
    {
        var t = (title ?? "").Trim().Trim('"', '\'', '“', '”', '.', ' ');
        if (t.Length is < 3 or > 60)
            return null;
        return char.ToUpper(t[0], new System.Globalization.CultureInfo("tr-TR")) + t[1..];
    }
}
