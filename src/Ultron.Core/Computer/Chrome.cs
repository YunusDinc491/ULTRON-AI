using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ultron.Core.Tools;

namespace Ultron.Core.Computer;

/// <summary>
/// kullanıcının kendi Chrome'una kurulan Ultron eklentisiyle bağlantı (Faz 22). Chrome 2025'ten beri ana profilde dışarıdan uzaktan
/// kumandayı kapattığı için tek sağlam yol bu: eklenti bilgisayardaki Ultron'a bağlanır (127.0.0.1, eşleştirme koduyla), komutları
/// izinli sitelerde uygular. Benzetme: televizyonun kendi kumandası, sadece izin verilen kanallar için.
/// </summary>
public interface IChromeBridge
{
    bool Connected { get; }

    /// <summary>Eklentiye komut gönderir, cevabını bekler. Bağlı değilse ya da cevap gelmezse <see cref="InvalidOperationException"/>.</summary>
    Task<JsonNode?> SendAsync(string command, JsonObject arguments, CancellationToken cancellationToken = default);

    /// <summary>Eklentinin ayarlarına bir kez girilen eşleştirme kodu.</summary>
    Task<string> PairingCodeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// "YouTube'da Tarkan Şımarık aç", "videoyu 10 dakika ileri sar", "10. dakikaya git", "durdur", "sonraki", "ne çalıyor?".
/// Okuma/oynatma serbest (tasarım kararı); yorum/mesaj gönderme bu araçta yok.
/// </summary>
public sealed class YouTubeTool(IChromeBridge chrome) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "youtube",
        "Kullanıcının kendi Chrome'unda YouTube'u kontrol eder: ac (sorgu: şarkı/video adı ya da adres; ilk sonucu açar), durdur, devam, sar (saniye + mod: ileri/geri/git), sonraki, ses (deger 0-100), ne_caliyor.",
        """
        {"type":"object","properties":{
          "islem":{"type":"string","enum":["ac","durdur","devam","sar","sonraki","ses","ne_caliyor"]},
          "sorgu":{"type":"string","description":"ac için: şarkı/video adı ya da YouTube adresi"},
          "saniye":{"type":"integer","description":"sar için: 10 dakika = 600"},
          "mod":{"type":"string","enum":["ileri","geri","git"],"description":"ileri/geri: şu andan itibaren; git: videonun o anına"},
          "deger":{"type":"integer","description":"ses için 0-100"}
        },"required":["islem"]}
        """);

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!chrome.Connected)
            return ToolResult.Fail(ChromeStatusTool.NotConnected);
        var action = ToolArgs.String(arguments, "islem");
        try
        {
            if (action == "ac")
            {
                if (ToolArgs.String(arguments, "sorgu") is not { } query)
                    return ToolResult.Fail("Ne açılacağını söylemedin (şarkı ya da video adı).");
                var opened = await chrome.SendAsync("yt.play", new JsonObject { ["query"] = query }, cancellationToken);
                return ToolResult.Success($"YouTube'da açıldı: {opened?["title"]} ({opened?["url"]})");
            }
            var args = new JsonObject { ["action"] = action };
            if (action == "sar")
            {
                if (ToolArgs.Long(arguments, "saniye") is not { } seconds)
                    return ToolResult.Fail("Kaç saniye/dakika sarılacağı gerekli.");
                args["seconds"] = seconds;
                args["mode"] = ToolArgs.String(arguments, "mod") ?? "ileri";
            }
            if (action == "ses")
                args["volume"] = Math.Clamp(ToolArgs.Long(arguments, "deger") ?? 50, 0, 100);
            var state = await chrome.SendAsync("yt.control", args, cancellationToken);
            return ToolResult.Success(Describe(action, state));
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail(ex.Message);
        }
    }

    internal static string Describe(string? action, JsonNode? state)
    {
        var title = state?["title"]?.GetValue<string>() ?? "video";
        var at = Clock(state?["time"]?.GetValue<double>());
        var total = Clock(state?["duration"]?.GetValue<double>());
        var paused = state?["paused"]?.GetValue<bool>() == true;
        return action switch
        {
            "durdur" => $"Durduruldu: {title} ({at}/{total}).",
            "devam" => $"Devam ediyor: {title} ({at}/{total}).",
            "sar" => $"Sarıldı: {title} şu an {at}/{total}.",
            "sonraki" => $"Sonraki videoya geçildi: {title}.",
            "ses" => $"YouTube sesi %{state?["volume"]}.",
            _ => $"{(paused ? "Duraklatılmış" : "Çalıyor")}: {title} ({at}/{total}).",
        };
    }

    internal static string Clock(double? seconds)
    {
        if (seconds is not { } s || double.IsNaN(s) || double.IsInfinity(s))
            return "?";
        var t = TimeSpan.FromSeconds(Math.Max(0, s));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}

/// <summary>"Chrome'da şu siteyi aç", "açık sekmeyi özetle", "bu sayfada sınav tarihi var mı?" (okuma serbest).</summary>
public sealed class ChromeTabTool(IChromeBridge chrome) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "chrome_sekme",
        "Kullanıcının kendi Chrome'unda: ac (adres; yeni sekmede açar), oku (şu an açık sekmenin başlığı, adresi ve metni), liste (açık sekmeler).",
        """{"type":"object","properties":{"islem":{"type":"string","enum":["ac","oku","liste"]},"adres":{"type":"string"}},"required":["islem"]}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public Task<PermissionLevel> LevelForAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolArgs.String(arguments, "islem") != "ac" ? PermissionLevel.Serbest
            : BrowserTool.Url(arguments) is { } u && BrowserTool.IsPublic(u) ? PermissionLevel.Bildirerek : PermissionLevel.Yasak);

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!chrome.Connected)
            return ToolResult.Fail(ChromeStatusTool.NotConnected);
        try
        {
            switch (ToolArgs.String(arguments, "islem"))
            {
                case "ac" when BrowserTool.Url(arguments) is { } url:
                    var opened = await chrome.SendAsync("tabs.open", new JsonObject { ["url"] = url.AbsoluteUri }, cancellationToken);
                    return ToolResult.Success($"Chrome'da açıldı: {opened?["url"] ?? url.AbsoluteUri}");
                case "oku":
                    var page = await chrome.SendAsync("tabs.read", new JsonObject(), cancellationToken);
                    var text = page?["text"]?.GetValue<string>() ?? "";
                    return ToolResult.Success($"Açık sekme: {page?["title"]} — {page?["url"]}\nMetin:\n{(text.Length > 6000 ? text[..6000] + "…" : text)}");
                case "liste":
                    var tabs = (await chrome.SendAsync("tabs.list", new JsonObject(), cancellationToken))?.AsArray() ?? [];
                    var sb = new StringBuilder($"Chrome'da {tabs.Count} sekme:\n");
                    foreach (var t in tabs)
                        sb.AppendLine($"- {t?["title"]} — {t?["url"]}{(t?["active"]?.GetValue<bool>() == true ? " (açık olan)" : "")}");
                    return ToolResult.Success(sb.ToString().TrimEnd());
                default:
                    return ToolResult.Fail("İşlem anlaşılamadı (ac için adres gerekli).");
            }
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail(ex.Message);
        }
    }
}

/// <summary>"Chrome eklentisi bağlı mı?", "eklenti kodu ne?"</summary>
public sealed class ChromeStatusTool(IChromeBridge chrome) : ITool
{
    public const string NotConnected = "Chrome eklentisi bağlı değil: Chrome açık mı ve Ultron eklentisi kurulu mu? Kurulum: chrome://extensions → Geliştirici modu → Paketlenmemiş öğe yükle → Ultron'un eklenti klasörü; sonra eklentinin ayarlarına eşleştirme kodunu gir (\"eklenti kodu ne\" diye sorabilirsin).";

    public ToolDefinition Definition { get; } = new(
        "chrome_eklentisi",
        "Ultron Chrome eklentisinin bağlı olup olmadığını ve eklentiye girilecek eşleştirme kodunu söyler.",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        ToolResult.Success((chrome.Connected ? "Chrome eklentisi bağlı." : "Chrome eklentisi bağlı değil.") +
            $" Eşleştirme kodu: {await chrome.PairingCodeAsync(cancellationToken)} (eklentinin ayarlarına bir kez girilir).");
}
