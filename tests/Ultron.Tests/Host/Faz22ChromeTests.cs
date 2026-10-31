using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ultron.Core.Chat;
using Ultron.Core.Computer;
using Ultron.Core.Embeddings;
using Ultron.Core.Routing;
using Ultron.Core.Tools;
using Ultron.Core.Voice;
using Ultron.Host.Chrome;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Host;

/// <summary>Faz 22: Ultron Chrome eklentisi — araçlar, eşleştirme, gerçek WebSocket protokolü (bellekte Host), yönlendirme.</summary>
public sealed class Faz22ChromeTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ultron-test-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Database:Path", _dbPath);
            b.UseSetting("Voice:StartChatterbox", "false");
            b.UseSetting("Telegram:Start", "false");
            b.UseSetting("Reminders:Scheduler", "false");
            b.UseSetting("Backup:Scheduler", "false");
            b.UseSetting("Services:AutoStart", "false");
            b.UseSetting("Library:Scheduler", "false");
            b.UseSetting("Sites:Scheduler", "false");
            b.UseSetting("Voice:WarmUpOnStart", "false"); // testte gerçek Whisper yüklenmesin
            b.UseSetting("Embeddings:Migration", "false");
            b.ConfigureTestServices(services =>
            {
                services.AddSingleton<IChatModel>(new ScriptedChatModel());
                services.AddSingleton<IEmbeddingModel>(new FakeEmbeddingModel([]));
            });
        });
        _ = _factory.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private async Task<WebSocket> ConnectAsync(string origin = "chrome-extension://abcdef")
    {
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = r => r.Headers["Origin"] = origin;
        return await client.ConnectAsync(new Uri("ws://localhost/eklenti"), CancellationToken.None);
    }

    private static Task Send(WebSocket ws, object message) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<JsonNode?> Receive(WebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        var r = await ws.ReceiveAsync(buffer, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        return r.MessageType == WebSocketMessageType.Close ? null : JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, r.Count));
    }

    [Fact]
    public async Task Dogru_kodla_baglanir_komut_gider_cevap_doner()
    {
        var bridge = _factory.Services.GetRequiredService<ChromeBridge>();
        var code = await bridge.PairingCodeAsync();
        using var ws = await ConnectAsync();

        await Send(ws, new { type = "hello", code = code.ToLowerInvariant(), version = "1.0.0" }); // büyük/küçük harf fark etmez
        Assert.Equal("welcome", (await Receive(ws))?["type"]?.GetValue<string>());
        Assert.True(bridge.Connected);

        var call = bridge.SendAsync("tabs.list", new JsonObject());
        var command = await Receive(ws);
        Assert.Equal("tabs.list", command?["cmd"]?.GetValue<string>());
        await Send(ws, new { id = command!["id"]!.GetValue<int>(), ok = true, data = new[] { new { title = "YouTube", url = "https://youtube.com", active = true } } });

        var result = await call;
        Assert.Equal("YouTube", result?[0]?["title"]?.GetValue<string>());
    }

    [Fact]
    public async Task Eklenti_hatasi_aciklamasiyla_doner()
    {
        var bridge = _factory.Services.GetRequiredService<ChromeBridge>();
        using var ws = await ConnectAsync();
        await Send(ws, new { type = "hello", code = await bridge.PairingCodeAsync() });
        await Receive(ws);

        var call = bridge.SendAsync("yt.control", new JsonObject { ["action"] = "durdur" });
        var command = await Receive(ws);
        await Send(ws, new { id = command!["id"]!.GetValue<int>(), ok = false, error = "Chrome'da açık bir YouTube videosu yok." });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => call);
        Assert.Contains("açık bir YouTube videosu yok", ex.Message);
    }

    [Fact]
    public async Task Yanlis_kod_reddedilir()
    {
        var bridge = _factory.Services.GetRequiredService<ChromeBridge>();
        using var ws = await ConnectAsync();

        await Send(ws, new { type = "hello", code = "YANLIS00" });

        Assert.Equal("rejected", (await Receive(ws))?["type"]?.GetValue<string>());
        Assert.False(bridge.Connected);
    }

    [Fact]
    public async Task Eklenti_olmayan_kaynak_kabul_edilmez()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(origin: "https://kotu-site.com"));
    }

    // ---- Araçlar (sahte köprü) ----

    private sealed class FakeBridge(bool connected = true) : IChromeBridge
    {
        public List<(string Cmd, JsonObject Args)> Sent { get; } = [];
        public bool Connected => connected;
        public Task<string> PairingCodeAsync(CancellationToken cancellationToken = default) => Task.FromResult("3FA91C0B");

        public Task<JsonNode?> SendAsync(string command, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            Sent.Add((command, arguments));
            JsonNode? reply = command == "yt.play"
                ? new JsonObject { ["title"] = "Tarkan - Şımarık", ["url"] = "https://www.youtube.com/watch?v=x" }
                : new JsonObject { ["title"] = "Tarkan - Şımarık", ["time"] = 725.0, ["duration"] = 245.0 * 4, ["paused"] = false, ["volume"] = 40 };
            return Task.FromResult<JsonNode?>(reply);
        }
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task YouTube_ac_ve_sar_dogru_komutlari_gonderir()
    {
        var bridge = new FakeBridge();
        var tool = new YouTubeTool(bridge);

        var opened = await tool.ExecuteAsync(Args("""{"islem":"ac","sorgu":"Tarkan Şımarık"}"""));
        var seek = await tool.ExecuteAsync(Args("""{"islem":"sar","saniye":600,"mod":"ileri"}"""));

        Assert.Contains("Tarkan - Şımarık", opened.ForModel);
        Assert.Equal(("yt.play", "Tarkan Şımarık"), (bridge.Sent[0].Cmd, bridge.Sent[0].Args["query"]!.GetValue<string>()));
        Assert.Equal(600, bridge.Sent[1].Args["seconds"]!.GetValue<long>());
        Assert.Equal("ileri", bridge.Sent[1].Args["mode"]!.GetValue<string>());
        Assert.Contains("12:05/16:20", seek.ForModel);
    }

    [Fact]
    public async Task Eklenti_bagli_degilse_kurulumu_anlatir()
    {
        var result = await new YouTubeTool(new FakeBridge(connected: false)).ExecuteAsync(Args("""{"islem":"durdur"}"""));

        Assert.False(result.Ok);
        Assert.Contains("chrome://extensions", result.ForModel);
    }

    [Fact]
    public async Task Sekme_acma_yerel_adrese_yasak_okuma_serbest()
    {
        var tool = new ChromeTabTool(new FakeBridge());

        Assert.Equal(PermissionLevel.Yasak, await tool.LevelForAsync(Args("""{"islem":"ac","adres":"http://127.0.0.1:5199"}""")));
        Assert.Equal(PermissionLevel.Bildirerek, await tool.LevelForAsync(Args("""{"islem":"ac","adres":"github.com"}""")));
        Assert.Equal(PermissionLevel.Serbest, await tool.LevelForAsync(Args("""{"islem":"oku"}""")));
    }

    [Fact]
    public void Kod_buyuk_kucuk_harf_ve_bosluk_tolere_eder()
    {
        Assert.True(ChromeBridge.CodeMatches(" 3fa91c0b ", "3FA91C0B"));
        Assert.False(ChromeBridge.CodeMatches("3FA91C0", "3FA91C0B"));
        Assert.False(ChromeBridge.CodeMatches(null, "3FA91C0B"));
    }

    [Theory]
    [InlineData("YouTube'da Tarkan Şımarık aç", "youtube")]
    [InlineData("videoyu 10 dakika ileri sar", "youtube")]
    [InlineData("videonun 12. dakikasına git", "youtube")]
    [InlineData("Chrome'da açık sayfayı özetle", "chrome_sekme")]
    [InlineData("chrome eklentisi kodu ne", "chrome_eklentisi")]
    public void Istek_chrome_araclarini_acar(string text, string tool)
    {
        Assert.Contains(tool, ToolRouting.Select(MessageIntent.Komut, text) ?? []);
    }

    [Fact]
    public void Zamanli_hatirlatma_youtube_acmaz()
    {
        Assert.DoesNotContain("youtube", ToolRouting.Select(MessageIntent.Komut, "10 dakikaya hatırlat ilacımı içeyim") ?? []);
    }
}
