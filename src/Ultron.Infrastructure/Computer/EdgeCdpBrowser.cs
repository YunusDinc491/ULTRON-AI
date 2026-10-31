using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Ultron.Core.Computer;
using Ultron.Infrastructure.Web;

namespace Ultron.Infrastructure.Computer;

/// <summary>
/// Ultron tarayıcısı (Faz 21): bilgisayardaki Edge, ayrı bir profille (%USERPROFILE%\Ultron\tarayici — kullanıcının hesapları, çerezleri,
/// şifreleri burada yok) GÖRÜNÜR açılır ve Edge'in kendi uzaktan kumanda kanalıyla (DevTools Protocol, sadece 127.0.0.1) yönetilir.
/// Sayfaya küçük bir betik konup tıklanabilir/doldurulabilir öğeler numaralanır; tıklama ve yazma o numaralarla yapılır.
/// Benzetme: kullanıcının arabası değil, Ultron'a ait ayrı bir araba; kullanıcı camdan ne yaptığını izleyebiliyor.
/// </summary>
internal sealed class EdgeCdpBrowser(ILogger<EdgeCdpBrowser> logger) : IUltronBrowser, IAsyncDisposable
{
    private const int Port = 9333;
    private static readonly string Profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron", "tarayici");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http = new() { BaseAddress = new Uri($"http://127.0.0.1:{Port}/"), Timeout = TimeSpan.FromSeconds(5) };
    private ClientWebSocket? _socket;
    private int _nextId;

    public BrowserPage? Current { get; private set; }

    public Task<BrowserPage> OpenAsync(string url, CancellationToken cancellationToken = default) => WithPage(async ct =>
    {
        await SendAsync("Page.navigate", new JsonObject { ["url"] = url }, ct);
        await WaitLoadedAsync(ct);
    }, cancellationToken);

    public Task<BrowserPage> SnapshotAsync(CancellationToken cancellationToken = default) => WithPage(_ => Task.CompletedTask, cancellationToken);

    public Task<BrowserPage> ClickAsync(int number, CancellationToken cancellationToken = default) => WithPage(async ct =>
    {
        var ok = await EvalAsync($$"""(() => { const e = document.querySelector('[data-ultron-id="{{number}}"]'); if (!e) return false; e.scrollIntoView({block:'center'}); e.click(); return true; })()""", ct);
        if (ok?.GetValue<bool>() != true)
            throw new InvalidOperationException($"[{number}] numaralı öğe sayfada yok (sayfa değişmiş olabilir; önce 'oku').");
        await Task.Delay(1200, ct);
        await WaitLoadedAsync(ct);
    }, cancellationToken);

    public Task<BrowserPage> TypeAsync(int number, string text, CancellationToken cancellationToken = default) => WithPage(async ct =>
    {
        var value = JsonSerializer.Serialize(text);
        var ok = await EvalAsync($$"""
            (() => { const e = document.querySelector('[data-ultron-id="{{number}}"]'); if (!e) return false;
              e.focus();
              const proto = e.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : e.tagName === 'SELECT' ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
              const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
              if (setter) setter.call(e, {{value}}); else e.value = {{value}};
              e.dispatchEvent(new Event('input', {bubbles:true})); e.dispatchEvent(new Event('change', {bubbles:true}));
              return true; })()
            """, ct);
        if (ok?.GetValue<bool>() != true)
            throw new InvalidOperationException($"[{number}] numaralı alan sayfada yok (önce 'oku').");
    }, cancellationToken);

    public Task<BrowserPage> BackAsync(CancellationToken cancellationToken = default) => WithPage(async ct =>
    {
        await EvalAsync("history.back(); true", ct);
        await Task.Delay(1000, ct);
        await WaitLoadedAsync(ct);
    }, cancellationToken);

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (await ConnectedAsync(cancellationToken))
                await SendAsync("Browser.close", new JsonObject(), cancellationToken);
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Tarayıcı kapatılırken");
        }
        finally
        {
            _socket?.Dispose();
            _socket = null;
            Current = null;
            _gate.Release();
        }
    }

    private async Task<BrowserPage> WithPage(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureAsync(cancellationToken);
            await action(cancellationToken);
            Current = await ReadPageAsync(cancellationToken);
            return Current;
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or TimeoutException)
        {
            _socket?.Dispose();
            _socket = null;
            throw new InvalidOperationException("Ultron tarayıcısına ulaşılamadı (pencere kapatılmış olabilir); tekrar dene.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- Başlatma ve bağlantı ----

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (await ConnectedAsync(cancellationToken))
            return;
        if (!await PortOpenAsync(cancellationToken))
        {
            var edge = EdgeHeadlessBrowser.FindEdge() ?? throw new InvalidOperationException("Bu bilgisayarda Microsoft Edge bulunamadı.");
            Directory.CreateDirectory(Profile);
            var start = new ProcessStartInfo(edge) { UseShellExecute = false };
            foreach (var arg in new[] { $"--user-data-dir={Profile}", $"--remote-debugging-port={Port}", "--remote-debugging-address=127.0.0.1",
                         "--no-first-run", "--no-default-browser-check", "--disable-sync", "--new-window", "about:blank" })
                start.ArgumentList.Add(arg);
            Process.Start(start)?.Dispose();
            logger.LogInformation("Ultron tarayıcısı açıldı (ayrı profil: {Profile})", Profile);
            for (var i = 0; i < 40 && !await PortOpenAsync(cancellationToken); i++)
                await Task.Delay(250, cancellationToken);
        }
        var targets = await _http.GetFromJsonAsync<JsonArray>("json", cancellationToken) ?? [];
        var page = targets.FirstOrDefault(t => t?["type"]?.GetValue<string>() == "page")
            ?? await _http.GetFromJsonAsync<JsonObject>("json/new?about:blank", cancellationToken);
        var ws = page?["webSocketDebuggerUrl"]?.GetValue<string>() ?? throw new InvalidOperationException("Tarayıcı sekmesine bağlanılamadı.");
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(ws), cancellationToken);
        await SendAsync("Page.enable", new JsonObject(), cancellationToken);
        await SendAsync("Page.bringToFront", new JsonObject(), cancellationToken);
    }

    private async Task<bool> ConnectedAsync(CancellationToken cancellationToken)
    {
        if (_socket is not { State: WebSocketState.Open })
            return false;
        try
        {
            await EvalAsync("1", cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is WebSocketException or TimeoutException or InvalidOperationException)
        {
            _socket.Dispose();
            _socket = null;
            return false;
        }
    }

    private async Task<bool> PortOpenAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var r = await _http.GetAsync("json/version", cancellationToken);
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task WaitLoadedAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 60; i++)
        {
            if ((await EvalAsync("document.readyState", cancellationToken))?.GetValue<string>() == "complete")
            {
                await Task.Delay(500, cancellationToken); // JavaScript'le dolan içerik için kısa pay
                return;
            }
            await Task.Delay(250, cancellationToken);
        }
    }

    // ---- Sayfayı okuma: öğeler numaralanır ----

    private const string SnapshotScript = """
        (() => {
          const visible = e => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none'; };
          document.querySelectorAll('[data-ultron-id]').forEach(e => e.removeAttribute('data-ultron-id'));
          const sel = 'a[href], button, input:not([type=hidden]), textarea, select, [role=button], [role=link], [onclick]';
          const els = []; let n = 0;
          for (const e of document.querySelectorAll(sel)) {
            if (!visible(e) || els.length >= 120) continue;
            n++; e.setAttribute('data-ultron-id', n);
            const tag = e.tagName.toLowerCase(); const type = (e.getAttribute('type') || '').toLowerCase();
            const label = (e.getAttribute('aria-label') || e.innerText || e.value || e.getAttribute('placeholder') || e.getAttribute('title') || e.getAttribute('name') || e.getAttribute('href') || '').trim().replace(/\s+/g, ' ').slice(0, 80);
            const kind = tag === 'a' ? 'bağlantı' : tag === 'button' || e.getAttribute('role') === 'button' ? 'düğme' : tag === 'select' ? 'seçim' : tag === 'textarea' ? 'metin alanı' : (type === 'submit' ? 'düğme' : 'alan:' + (type || 'text'));
            const inForm = !!e.closest('form');
            const submit = (tag === 'button' && inForm && (type === '' || type === 'submit')) || (tag === 'input' && (type === 'submit' || type === 'image'));
            const hints = [type, e.getAttribute('name'), e.id, e.getAttribute('autocomplete'), e.getAttribute('placeholder')].filter(Boolean).join(' ');
            els.push({ n, kind, label, submit, hints });
          }
          const text = (document.body?.innerText || '').replace(/\n{3,}/g, '\n\n').slice(0, 8000);
          return JSON.stringify({ url: location.href, title: document.title, text, els });
        })()
        """;

    private async Task<BrowserPage> ReadPageAsync(CancellationToken cancellationToken)
    {
        var json = (await EvalAsync(SnapshotScript, cancellationToken))?.GetValue<string>() ?? "{}";
        var node = JsonNode.Parse(json)!;
        var elements = (node["els"]?.AsArray() ?? []).Select(e =>
        {
            var kind = e!["kind"]!.GetValue<string>();
            var label = e["label"]!.GetValue<string>();
            var (sensitive, purchase) = BrowserTool.Classify(kind, label, e["hints"]?.GetValue<string>() ?? "");
            return new BrowserElement(e["n"]!.GetValue<int>(), kind, label, sensitive, e["submit"]!.GetValue<bool>(), purchase);
        }).ToList();
        return new BrowserPage(node["url"]?.GetValue<string>() ?? "", node["title"]?.GetValue<string>() ?? "", node["text"]?.GetValue<string>() ?? "", elements);
    }

    // ---- DevTools Protocol ----

    private async Task<JsonNode?> EvalAsync(string expression, CancellationToken cancellationToken)
    {
        var result = await SendAsync("Runtime.evaluate", new JsonObject { ["expression"] = expression, ["returnByValue"] = true, ["awaitPromise"] = true }, cancellationToken);
        return result?["result"]?["value"];
    }

    private async Task<JsonNode?> SendAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("Tarayıcıya bağlı değil.");
        var id = Interlocked.Increment(ref _nextId);
        var message = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString();
        await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            try
            {
                do
                {
                    r = await socket.ReceiveAsync(buffer, timeout.Token);
                    ms.Write(buffer, 0, r.Count);
                }
                while (!r.EndOfMessage);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Tarayıcı cevap vermedi ({method}).");
            }
            if (r.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("Tarayıcı bağlantıyı kapattı.");
            var node = JsonNode.Parse(ms.ToArray());
            if (node?["id"]?.GetValue<int>() != id)
                continue; // olay mesajları (Page.loadEventFired vb.) atlanır
            if (node["error"] is { } error)
                throw new InvalidOperationException($"Tarayıcı hatası ({method}): {error["message"]}");
            return node["result"];
        }
    }

    public async ValueTask DisposeAsync()
    {
        _socket?.Dispose();
        _http.Dispose();
        await Task.CompletedTask;
    }
}
