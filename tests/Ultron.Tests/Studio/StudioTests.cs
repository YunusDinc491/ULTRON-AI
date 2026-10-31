using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Core.Chat;
using Ultron.Core.Coding;
using Ultron.Core.Settings;
using Ultron.Core.Studio;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Studio;

public sealed class StudioCodeTests
{
    [Fact]
    public void Kod_blogu_ve_aciklama_ayrilir_kapanmamis_blok_da_okunur()
    {
        var (code, explanation) = StudioCode.Extract("Al bakalım:\n```jsx\nexport default function A(){return <b/>}\n```\nMobilde hamburger var.");
        Assert.Equal("export default function A(){return <b/>}", code);
        Assert.Equal("Al bakalım:\n\nMobilde hamburger var.", explanation);

        var (partial, _) = StudioCode.Extract("```jsx\nexport default function A(){\n  return <div>");
        Assert.StartsWith("export default function A()", partial);

        Assert.Null(StudioCode.Extract("Kod yok, sadece metin.").Code);
    }

    [Fact]
    public void React_disi_ice_aktarma_ve_eksik_disa_aktarma_sorun_sayilir()
    {
        Assert.Empty(StudioCode.Problems("import React, { useState } from 'react';\nexport default function A(){}"));
        var problems = StudioCode.Problems("import { Menu } from 'lucide-react';\nfunction A(){}");
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("lucide-react"));
        Assert.Equal("Navbar", StudioCode.ComponentName("export default function Navbar() {}"));
    }

    /// <summary>Ultron'la gelen bütün tarifler okunabilmeli ve önizlemede çalışabilir olmalı (React dışı içe aktarma yok).</summary>
    [Fact]
    public void Hazir_tariflerin_hepsi_okunur_ve_kurallara_uyar()
    {
        var folder = Path.Combine(RepoRoot(), "src", "Ultron.Host", "notlar", "bilesenler");
        var files = Directory.GetFiles(folder, "*.md");
        Assert.True(files.Length >= 40, $"tarif sayısı {files.Length}");
        foreach (var file in files)
        {
            var recipe = StudioCode.ParseRecipe(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
            Assert.True(recipe is not null, "okunamadı: " + file);
            Assert.True(recipe.Tags.Length > 0 && recipe.Description.Length > 0, "etiket/açıklama eksik: " + file);
            Assert.True(StudioCode.Problems(recipe.Code).Count == 0, file + ": " + string.Join("; ", StudioCode.Problems(recipe.Code)));
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Ultron.Host")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("depo kökü bulunamadı");
    }
}

public sealed class StudioServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ultron-studio-test-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryStudioStore _store = new();
    private readonly ScriptedChatModel _chat = new();
    private readonly WritingAgent _agent = new();
    private readonly StudioService _service;

    private const string Answer = "```jsx\nimport React from 'react';\nexport default function Navbar() { return <nav aria-label=\"Ana menü\">Menü</nav>; }\n```\nYapışkan navbar hazır.";

    public StudioServiceTests()
    {
        var recipes = Path.Combine(_root, "tarifler");
        Directory.CreateDirectory(recipes);
        File.WriteAllText(Path.Combine(recipes, "01-navbar.md"), "---\ntitle: Navbar\ntags: navbar, menü\ndescription: Üst menü\n---\n```jsx\nimport React from 'react';\nexport default function N(){return <nav/>}\n```\n");
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 28, 14, 0, 0, TimeSpan.Zero));
        _chat.AnswerChunks = [Answer[..30], Answer[30..]];
        _service = new StudioService(_store, _chat, new FakeEmbeddingModel([]), _agent, new LogStudioNotifier(),
            new UltronSettings(new InMemorySettingsStore(), time),
            Options.Create(new StudioOptions { RecipeFolder = recipes, WorkFolder = Path.Combine(_root, "is"), KnownMinScore = 0.5 }),
            time, NullLogger<StudioService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Istek_tarifle_yazilir_surum_olur_degisiklikte_mevcut_kod_verilir()
    {
        var project = await _service.CreateProjectAsync("Yeni bileşen");
        var events = await Collect(_service.GenerateAsync(project.Id, "Yapışkan navbar yap"));

        var started = Assert.IsType<StudioStarted>(events[0]);
        Assert.Equal(["Navbar"], started.Recipes);
        Assert.True(started.KnownPattern);
        Assert.Equal(2, events.OfType<StudioChunk>().Count());
        var done = Assert.IsType<StudioCompleted>(events[^1]);
        Assert.Equal(1, done.Version.Number);
        Assert.Contains("export default function Navbar", done.Version.Code);
        Assert.Equal("Yapışkan navbar hazır.", done.Explanation);
        Assert.Contains("Tarif defterinden örnek", _chat.StreamRequests[0][^1].Content);
        Assert.Equal("Yapışkan navbar yap", (await _store.GetProjectAsync(project.Id))!.Name); // ilk istek ad olur

        await Collect(_service.GenerateAsync(project.Id, "Butonu sağa al"));
        var second = _chat.StreamRequests[1];
        Assert.Contains("Mevcut bileşen (sürüm 1)", second[^1].Content);
        Assert.Contains("Değişiklik isteği: Butonu sağa al", second[^1].Content);
        Assert.Contains(second, m => m.Role == Role.User && m.Content == "Yapışkan navbar yap"); // geçmiş kodsuz
        Assert.Equal(2, (await _store.GetVersionsAsync(project.Id)).Count);
        Assert.Equal(4, (await _store.GetMessagesAsync(project.Id)).Count);
    }

    [Fact]
    public async Task Proje_silinince_surum_ve_mesajlari_gider_tarif_kalir()
    {
        var project = await _service.CreateProjectAsync("x");
        var version = Assert.IsType<StudioCompleted>((await Collect(_service.GenerateAsync(project.Id, "navbar")))[^1]).Version;
        await _service.RateAsync(version.Id, 1, null);

        Assert.True(await _service.DeleteProjectAsync(project.Id));

        Assert.Empty(await _service.ProjectsAsync());
        Assert.Empty(await _store.GetVersionsAsync(project.Id));
        Assert.Empty(await _store.GetMessagesAsync(project.Id));
        Assert.Single(await _service.LearnedRecipesAsync()); // favori tarif defterde kalır
    }

    [Fact]
    public async Task Kod_cikmazsa_surum_olusmaz()
    {
        var project = await _service.CreateProjectAsync("x");
        _chat.AnswerChunks = ["Bunu yapamam."];
        var events = await Collect(_service.GenerateAsync(project.Id, "navbar"));
        Assert.IsType<StudioFailed>(events[^1]);
        Assert.Empty(await _store.GetVersionsAsync(project.Id));
    }

    [Fact]
    public async Task Begeni_favori_tarif_begenmeme_notu_zevk_kurali_olur_ve_sonraki_isteklere_girer()
    {
        var project = await _service.CreateProjectAsync("x");
        var version = Assert.IsType<StudioCompleted>((await Collect(_service.GenerateAsync(project.Id, "navbar")))[^1]).Version;

        Assert.Contains("favori", await _service.RateAsync(version.Id, 1, null));
        Assert.Contains((await _service.LearnedRecipesAsync()), r => r.Kind == RecipeKind.Favori);

        Assert.Contains("kural", await _service.RateAsync(version.Id, -1, "Köşeler daha yuvarlak olsun"));
        Assert.Single(await _service.RulesAsync());
        await Collect(_service.GenerateAsync(project.Id, "Biraz daha koyu"));
        Assert.Contains("- Köşeler daha yuvarlak olsun", _chat.StreamRequests[^1][0].Content);

        Assert.True(await _service.RemoveRuleAsync((await _service.RulesAsync())[0].Id));
        Assert.Empty(await _service.RulesAsync());
    }

    [Fact]
    public async Task Claude_yaptiginda_yeni_surum_ve_tarif_olur()
    {
        var project = await _service.CreateProjectAsync("Kanban panosu");
        Assert.Contains("başladı", await _service.StartClaudeAsync(project.Id, "Sürükle bırak kanban panosu"));
        for (var i = 0; i < 100 && _service.ClaudeBusy; i++)
            await Task.Delay(20);

        var versions = await _store.GetVersionsAsync(project.Id);
        var version = Assert.Single(versions);
        Assert.Equal(StudioSource.Claude, version.Source);
        Assert.Contains(await _service.LearnedRecipesAsync(), r => r.Kind == RecipeKind.Claude);
        Assert.Equal(["Read", "Write", "Edit", "Glob", "Grep"], _agent.Requests.Single().AllowedTools);
        Assert.False(_agent.Requests.Single().ReadOnly);
    }

    [Fact]
    public async Task Gece_denetimi_duzeltilmis_kodu_tarife_kurallari_profile_ekler()
    {
        var project = await _service.CreateProjectAsync("x");
        var version = Assert.IsType<StudioCompleted>((await Collect(_service.GenerateAsync(project.Id, "navbar")))[^1]).Version;
        _agent.Reply = $$"""
            {"bilesenler":[{"id":{{version.Id}},"karar":"duzeltilmeli","baslik":"Navbar düzeltilmiş","aciklama":"a","duzeltilmis_kod":"import React from 'react';\nexport default function N(){ return <nav aria-label=\"Ana\">Düzgün menü bağlantıları</nav>; }"}],
             "kurallar":["Butonlarda rounded-full kullan"],"rapor":"r"}
            """;

        var report = await _service.ReviewAsync();

        Assert.Contains("1 bileşen incelendi, 1 düzeltilmiş tarif ve 1 yeni zevk kuralı", report);
        Assert.Contains(await _service.LearnedRecipesAsync(), r => r.Kind == RecipeKind.Denetim && r.Title == "Navbar düzeltilmiş");
        Assert.Equal("Gece denetimi", Assert.Single(await _service.RulesAsync()).Source);
        Assert.Empty(await _store.GetUnreviewedAsync(10));
        Assert.True(_agent.Requests.Single().ReadOnly);
    }

    private static async Task<List<StudioEvent>> Collect(IAsyncEnumerable<StudioEvent> events)
    {
        var list = new List<StudioEvent>();
        await foreach (var e in events)
            list.Add(e);
        return list;
    }

    /// <summary>Claude Code yerine: bileşen işinde Bilesen.jsx yazar, denetimde Reply'ı döner.</summary>
    private sealed class WritingAgent : ICodeAgent
    {
        public string Reply { get; set; } = "Kanban panosunu yazdım.";
        public List<CodeRunRequest> Requests { get; } = [];

        public Task<CodeAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new CodeAgentStatus(@"C:\claude.exe", true, "hazır"));

        public async Task<CodeRunResult> RunAsync(CodeRunRequest request, Action<string> progress, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (!request.ReadOnly)
                await File.WriteAllTextAsync(Path.Combine(request.Folder, "Bilesen.jsx"), "import React from 'react';\nexport default function Kanban(){ return <div>Yapılacak</div>; }", cancellationToken);
            return new CodeRunResult(true, Reply, "oturum", 0.1);
        }
    }
}

internal sealed class InMemoryStudioStore : IStudioStore
{
    private readonly List<StudioProject> _projects = [];
    private readonly List<StudioVersion> _versions = [];
    private readonly List<StudioMessage> _messages = [];
    private readonly List<StudioRecipe> _recipes = [];
    private readonly List<DesignRule> _rules = [];
    private long _id;

    private long Next() => Interlocked.Increment(ref _id);

    public Task<StudioProject> AddProjectAsync(StudioProject project, CancellationToken cancellationToken = default)
    {
        var p = new StudioProject { Id = Next(), Name = project.Name, CreatedAtUtc = project.CreatedAtUtc, UpdatedAtUtc = project.UpdatedAtUtc };
        _projects.Add(p);
        return Task.FromResult(p);
    }

    public Task UpdateProjectAsync(StudioProject project, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> DeleteProjectAsync(long id, CancellationToken cancellationToken = default)
    {
        _messages.RemoveAll(m => m.ProjectId == id);
        _versions.RemoveAll(v => v.ProjectId == id);
        return Task.FromResult(_projects.RemoveAll(p => p.Id == id) > 0);
    }

    public Task<StudioProject?> GetProjectAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_projects.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<StudioProject>> GetProjectsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StudioProject>>(_projects.ToList());

    public Task<StudioVersion> AddVersionAsync(StudioVersion v, CancellationToken cancellationToken = default)
    {
        var copy = new StudioVersion { Id = Next(), ProjectId = v.ProjectId, Number = v.Number, Request = v.Request, Code = v.Code, Explanation = v.Explanation, Source = v.Source, CreatedAtUtc = v.CreatedAtUtc };
        _versions.Add(copy);
        return Task.FromResult(copy);
    }

    public Task UpdateVersionAsync(StudioVersion version, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<StudioVersion?> GetVersionAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_versions.FirstOrDefault(v => v.Id == id));

    public Task<IReadOnlyList<StudioVersion>> GetVersionsAsync(long projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StudioVersion>>(_versions.Where(v => v.ProjectId == projectId).OrderBy(v => v.Number).ToList());

    public Task<IReadOnlyList<StudioVersion>> GetUnreviewedAsync(int max, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StudioVersion>>(_versions.Where(v => v.ReviewedAtUtc is null && v.Source != StudioSource.Claude).Take(max).ToList());

    public Task<StudioMessage> AddMessageAsync(StudioMessage m, CancellationToken cancellationToken = default)
    {
        var copy = new StudioMessage { Id = Next(), ProjectId = m.ProjectId, FromUser = m.FromUser, Content = m.Content, VersionId = m.VersionId };
        _messages.Add(copy);
        return Task.FromResult(copy);
    }

    public Task<IReadOnlyList<StudioMessage>> GetMessagesAsync(long projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StudioMessage>>(_messages.Where(m => m.ProjectId == projectId).ToList());

    public Task<IReadOnlyList<StudioRecipe>> GetRecipesAsync(bool activeOnly = true, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StudioRecipe>>(_recipes.Where(r => !activeOnly || r.IsActive).ToList());

    public Task<StudioRecipe> AddRecipeAsync(StudioRecipe r, CancellationToken cancellationToken = default)
    {
        var copy = new StudioRecipe { Id = Next(), Slug = r.Slug, Title = r.Title, Tags = r.Tags, Description = r.Description, Code = r.Code, Kind = r.Kind, ContentHash = r.ContentHash, Embedding = r.Embedding };
        _recipes.Add(copy);
        return Task.FromResult(copy);
    }

    public Task UpdateRecipeAsync(StudioRecipe recipe, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<DesignRule>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DesignRule>>(_rules.Where(r => r.IsActive).ToList());

    public Task<DesignRule> AddRuleAsync(DesignRule rule, CancellationToken cancellationToken = default)
    {
        var copy = new DesignRule { Id = Next(), Text = rule.Text, Source = rule.Source, CreatedAtUtc = rule.CreatedAtUtc };
        _rules.Add(copy);
        return Task.FromResult(copy);
    }

    public Task<bool> DeactivateRuleAsync(long id, CancellationToken cancellationToken = default)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id && r.IsActive);
        if (rule is not null)
            rule.IsActive = false;
        return Task.FromResult(rule is not null);
    }
}
