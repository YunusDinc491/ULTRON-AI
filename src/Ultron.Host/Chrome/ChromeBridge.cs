using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Ultron.Core.Computer;
using Ultron.Core.Settings;

namespace Ultron.Host.Chrome;

/// <summary>
/// Ultron Chrome eklentisinin bağlandığı nokta (Faz 22): ws://127.0.0.1:5199/eklenti. Güvenlik: Host sadece 127.0.0.1'i dinler;
/// bağlantı chrome-extension:// kaynağından gelmeli ve ilk mesajda eşleştirme kodu doğru olmalı (bilgisayardaki başka bir program
/// kendini eklenti diye tanıtamasın). Tek eklenti bağlı olur; yenisi gelince eskisi kapanır.
/// Protokol: eklenti → {type:"hello", code}, Ultron → {id, cmd, args}, eklenti → {id, ok, data | error}; 20 sn'de bir ping.
/// </summary>
public sealed class ChromeBridge(UltronSettings settings, ILogger<ChromeBridge> logger) : IChromeBridge
{
    public const string Path = "/eklenti";
    private const string CodeKey = "chrome_eklenti_kodu";

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly SemaphoreSlim _send = new(1, 1);
    private WebSocket? _socket;
    private int _nextId;

    public bool Connected => _socket is { State: WebSocketState.Open };

    public async Task<string> PairingCodeAsync(CancellationToken cancellationToken = default)
    {
        if (await settings.GetInternalAsync(CodeKey, cancellationToken) is { Length: >= 6 } code)
            return code;
        code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)); // 8 karakter, ör. 3FA91C0B
        await settings.SetInternalAsync(CodeKey, code, cancellationToken);
        return code;
    }

    public async Task<JsonNode?> SendAsync(string command, JsonObject arguments, CancellationToken cancellationToken = default)
    {
        var socket = _socket;
        if (socket is not { State: WebSocketState.Open })
            throw new InvalidOperationException(ChromeStatusTool.NotConnected);
        var id = Interlocked.Increment(ref _nextId);
        var waiter = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = waiter;
        try
        {
            await WriteAsync(socket, new JsonObject { ["id"] = id, ["cmd"] = command, ["args"] = arguments }, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                return await waiter.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("Chrome eklentisi 30 sn içinde cevap vermedi.");
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Eklentinin WebSocket bağlantısını kabul eder ve kapanana kadar mesajları dağıtır.</summary>
    public async Task AcceptAsync(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!context.WebSockets.IsWebSocketRequest || !origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var ct = context.RequestAborted;
        using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        helloTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var hello = await ReadAsync(socket, helloTimeout.Token);
        if (hello?["type"]?.GetValue<string>() != "hello" || !CodeMatches(hello["code"]?.GetValue<string>(), await PairingCodeAsync(ct)))
        {
            logger.LogWarning("Chrome eklentisi reddedildi (eşleştirme kodu yanlış ya da yok): {Origin}", origin);
            await WriteAsync(socket, new JsonObject { ["type"] = "rejected", ["reason"] = "Eşleştirme kodu yanlış. Ultron'a \"eklenti kodu ne\" diye sor." }, ct);
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "kod", ct);
            return;
        }

        var previous = Interlocked.Exchange(ref _socket, socket);
        if (previous is { State: WebSocketState.Open })
            _ = previous.CloseAsync(WebSocketCloseStatus.NormalClosure, "yeni bağlantı", CancellationToken.None);
        logger.LogInformation("Chrome eklentisi bağlandı ({Origin}, sürüm {Version})", origin, hello["version"]);
        await WriteAsync(socket, new JsonObject { ["type"] = "welcome" }, ct);
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var message = await ReadAsync(socket, ct);
                if (message is null)
                    break;
                if (message["type"]?.GetValue<string>() == "ping")
                {
                    await WriteAsync(socket, new JsonObject { ["type"] = "pong" }, ct);
                    continue;
                }
                if (message["id"]?.GetValue<int>() is { } id && _pending.TryGetValue(id, out var waiter))
                {
                    if (message["ok"]?.GetValue<bool>() == true)
                        waiter.TrySetResult(message["data"]?.DeepClone());
                    else
                        waiter.TrySetException(new InvalidOperationException("Chrome: " + (message["error"]?.GetValue<string>() ?? "işlem yapılamadı")));
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // eklenti kapandı / Chrome kapatıldı
        }
        finally
        {
            Interlocked.CompareExchange(ref _socket, null, socket);
            foreach (var waiter in _pending.Values)
                waiter.TrySetException(new InvalidOperationException("Chrome eklentisinin bağlantısı koptu."));
            logger.LogInformation("Chrome eklentisi ayrıldı");
        }
    }

    public static bool CodeMatches(string? given, string expected) =>
        given is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given.Trim().ToUpperInvariant()), Encoding.UTF8.GetBytes(expected));

    private async Task WriteAsync(WebSocket socket, JsonObject message, CancellationToken cancellationToken)
    {
        await _send.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _send.Release();
        }
    }

    private static async Task<JsonNode?> ReadAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            ms.Write(buffer, 0, result.Count);
            if (ms.Length > 4_000_000)
                return null; // sayfa metni bile bu kadar olmaz
        }
        while (!result.EndOfMessage);
        try
        {
            return JsonNode.Parse(ms.ToArray());
        }
        catch (System.Text.Json.JsonException)
        {
            return new JsonObject();
        }
    }
}
