using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Ultron.Core.Chat;
using Ultron.Core.Coding;
using Ultron.Core.Computer;
using Ultron.Core.Embeddings;
using Ultron.Core.Memories;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Host;

/// <summary>Gerçek Host + resmi MCP istemcisi: Claude Code'un göreceği uç nokta.</summary>
public sealed class McpTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ultron-mcp-{Guid.NewGuid():N}.db");
    private readonly string _project = Path.Combine(Path.GetTempPath(), $"ultron-mcp-proje-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_project);
        File.WriteAllText(Path.Combine(_project, "PLAN.md"), "# Kitap listesi\nKoyu tema.");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:Path", _dbPath);
            b.UseSetting("Voice:StartChatterbox", "false");
            b.UseSetting("Telegram:Start", "false");
            b.UseSetting("Reminders:Scheduler", "false");
            b.UseSetting("Backup:Scheduler", "false");
            b.UseSetting("Services:AutoStart", "false");
            b.UseSetting("Library:Scheduler", "false");
            b.UseSetting("Embeddings:Migration", "false");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IChatModel>(new ScriptedChatModel());
                s.AddSingleton<IEmbeddingModel>(new FakeEmbeddingModel([]));
            });
        });
        _ = _factory.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
            File.Delete(f);
        Directory.Delete(_project, recursive: true);
    }

    private Task<McpClient> ConnectAsync(string? token)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_factory.Server.BaseAddress, "mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = token is null ? null : new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
        }, _factory.CreateClient());
        return McpClient.CreateAsync(transport);
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    [Fact]
    public async Task Anahtarla_proje_bilgisi_notlar_ve_sadece_paylasilan_hafiza_gorulur()
    {
        var services = _factory.Services;
        var job = await services.GetRequiredService<ICodingJobStore>().AddAsync(new CodingJob { Title = "kitap", Folder = _project, Request = "Kitap listesi sitesi" });
        var token = services.GetRequiredService<CodingAccess>().Issue(job.Id);
        var memoryStore = services.GetRequiredService<IMemoryStore>();
        await memoryStore.AddAsync(new Memory { Category = "Okul", Content = "Makine mühendisliği okuyor", Embedding = [1f] });
        await memoryStore.AddAsync(new Memory { Category = MemoryCategories.Saglik, Content = "Fıstık alerjisi var", Embedding = [1f] });
        await services.GetRequiredService<ComputerPermissions>().ShareMemoryAsync("Okul");

        await using var client = await ConnectAsync(token);
        var tools = (await client.ListToolsAsync()).Select(t => t.Name).Order().ToList();
        var info = Text(await client.CallToolAsync("proje_bilgisi"));
        await client.CallToolAsync("proje_notu_ekle", new Dictionary<string, object?> { ["not"] = "Veri localStorage'da", ["tur"] = "karar" });
        var prefs = Text(await client.CallToolAsync("kullanici_tercihleri"));

        Assert.Equal(["kullanici_tercihleri", "proje_bilgisi", "proje_notu_ekle"], tools);
        Assert.Contains("Kitap listesi sitesi", info);
        Assert.Contains("Koyu tema", info);
        Assert.Contains("Veri localStorage'da", await File.ReadAllTextAsync(ProjectNotes.Path(_project)));
        Assert.Contains("Makine mühendisliği okuyor", prefs);
        Assert.DoesNotContain("alerji", prefs); // paylaşılmayan kategori (Sağlık) asla dönmez
    }

    [Fact]
    public async Task Anahtarsiz_ya_da_iptal_edilmis_anahtarla_baglanilamaz()
    {
        using var http = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("mcp", new StringContent("{}"))).StatusCode);

        var access = _factory.Services.GetRequiredService<CodingAccess>();
        var token = access.Issue(1);
        access.Revoke(token);
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(token));
    }
}
