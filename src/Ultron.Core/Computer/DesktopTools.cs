using System.Text;
using System.Text.Json;
using Ultron.Core.Text;
using Ultron.Core.Tools;

namespace Ultron.Core.Computer;

/// <summary>"Hangi pencereler açık?"</summary>
public sealed class ListWindowsTool(IDesktop desktop) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "pencereleri_listele",
        "Bilgisayarda açık olan pencereleri (başlık ve uygulama) listeler.",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var windows = desktop.ListWindows();
        if (windows.Count == 0)
            return Task.FromResult(ToolResult.Success("Açık pencere yok."));
        var sb = new StringBuilder($"Açık pencereler ({windows.Count}):\n");
        foreach (var w in windows.Take(25))
            sb.AppendLine("- " + WindowMatch.Describe(w));
        return Task.FromResult(ToolResult.Success(sb.ToString().TrimEnd()));
    }
}

/// <summary>"Chrome'u öne getir", "Spotify'ı küçült", "Word'ü kapat" (kapatma onaylı).</summary>
public sealed class WindowTool(IDesktop desktop) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "pencere",
        "Açık bir pencereyi öne getirir, küçültür, büyütür ya da kapatır (kapatma onay ister; uygulama kaydedilmemiş iş varsa kendisi sorar).",
        """
        {"type":"object","properties":{
          "islem":{"type":"string","enum":["one_getir","kucult","buyut","kapat"]},
          "pencere":{"type":"string","description":"Uygulama adı ya da başlıktaki kelime (ör. chrome, vs code, termodinamik)"}
        },"required":["islem","pencere"]}
        """);

    public PermissionLevel Level => PermissionLevel.Bildirerek;

    public Task<PermissionLevel> LevelForAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolArgs.String(arguments, "islem") == "kapat" ? PermissionLevel.Onayli : PermissionLevel.Bildirerek);

    public string Describe(JsonElement arguments) =>
        $"Pencereyi kapat: {(Target(arguments) is { } w ? WindowMatch.Describe(w) : ToolArgs.String(arguments, "pencere"))}";

    private WindowInfo? Target(JsonElement arguments) =>
        ToolArgs.String(arguments, "pencere") is { } name && WindowMatch.Find(desktop.ListWindows(), name) is [var only] ? only : null;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var name = ToolArgs.String(arguments, "pencere");
        var action = ToolArgs.String(arguments, "islem");
        if (name is null || action is null)
            return Task.FromResult(ToolResult.Fail("'islem' ve 'pencere' gerekli."));
        var matches = WindowMatch.Find(desktop.ListWindows(), name);
        if (matches.Count == 0)
            return Task.FromResult(ToolResult.Fail($"'{name}' adında açık pencere bulamadım."));
        if (matches.Count > 1)
            return Task.FromResult(ToolResult.NeedInfo("Birden çok pencere uyuyor.",
                "Hangisi? " + string.Join(" · ", matches.Select(WindowMatch.Describe))));
        var w = matches[0];
        var ok = action switch
        {
            "one_getir" => desktop.Focus(w.Handle),
            "kucult" => desktop.Minimize(w.Handle),
            "buyut" => desktop.Maximize(w.Handle),
            "kapat" => desktop.Close(w.Handle),
            _ => false,
        };
        var verb = action switch { "one_getir" => "öne getirildi", "kucult" => "küçültüldü", "buyut" => "büyütüldü", "kapat" => "kapatılması istendi", _ => "?" };
        return Task.FromResult(ok ? ToolResult.Success($"{WindowMatch.Describe(w)} {verb}.") : ToolResult.Fail($"{WindowMatch.Describe(w)} için işlem yapılamadı."));
    }
}

/// <summary>"VS Code solda, Chrome sağda olsun."</summary>
public sealed class ArrangeWindowsTool(IDesktop desktop) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "pencereleri_diz",
        "Pencereleri ekranın sol ve sağ yarısına yerleştirir (biri boş bırakılabilir).",
        """{"type":"object","properties":{"sol":{"type":"string","description":"Sola gidecek pencere"},"sag":{"type":"string","description":"Sağa gidecek pencere"}}}""");

    public PermissionLevel Level => PermissionLevel.Bildirerek;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var windows = desktop.ListWindows();
        var done = new List<string>();
        foreach (var (key, left) in new[] { ("sol", true), ("sag", false) })
        {
            if (ToolArgs.String(arguments, key) is not { } name)
                continue;
            if (WindowMatch.Find(windows, name) is not [var w, ..])
                return Task.FromResult(ToolResult.Fail($"'{name}' adında açık pencere bulamadım."));
            if (desktop.Snap(w.Handle, left))
                done.Add($"{WindowMatch.Describe(w)} {(left ? "sola" : "sağa")}");
        }
        return Task.FromResult(done.Count == 0 ? ToolResult.Fail("Yerleştirilecek pencere söylenmedi.") : ToolResult.Success("Yerleştirildi: " + string.Join(", ", done) + "."));
    }
}

/// <summary>"Sesi %30 yap", "sesi kapat", "biraz kıs".</summary>
public sealed class VolumeTool(IDesktop desktop) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "ses",
        "Bilgisayarın ana ses düzeyini ayarlar: ayarla (0-100), ac (yükselt, +10), kis (alçalt, -10), kapat (sessize al), sesi_ac (sessizden çıkar), durum.",
        """{"type":"object","properties":{"islem":{"type":"string","enum":["ayarla","ac","kis","kapat","sesi_ac","durum"]},"deger":{"type":"integer","description":"ayarla için 0-100; ac/kis için adım (varsayılan 10)"}},"required":["islem"]}""");

    public PermissionLevel Level => PermissionLevel.Bildirerek;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var action = ToolArgs.String(arguments, "islem");
        var value = ToolArgs.Long(arguments, "deger");
        var current = desktop.GetVolume();
        switch (action)
        {
            case "durum":
                return Task.FromResult(ToolResult.Success($"Ses düzeyi %{current?.ToString() ?? "?"}{(desktop.IsMuted() == true ? " (sessizde)" : "")}."));
            case "kapat":
                desktop.SetMute(true);
                return Task.FromResult(ToolResult.Success("Ses kapatıldı (sessiz)."));
            case "sesi_ac":
                desktop.SetMute(false);
                return Task.FromResult(ToolResult.Success($"Ses açıldı (%{current})."));
            case "ayarla" when value is { } v:
                return Task.FromResult(Set((int)v, current));
            case "ac" or "kis" when current is { } c:
                var step = (int)(value ?? 10);
                return Task.FromResult(Set(action == "ac" ? c + step : c - step, current));
            default:
                return Task.FromResult(ToolResult.Fail("İşlem anlaşılamadı (ayarla için 'deger' gerekli)."));
        }
    }

    private ToolResult Set(int target, int? before)
    {
        target = Math.Clamp(target, 0, 100);
        desktop.SetMute(false);
        desktop.SetVolume(target);
        return ToolResult.Success($"Ses düzeyi %{before?.ToString() ?? "?"} → %{target}.");
    }
}

/// <summary>"Müziği durdur", "sonraki şarkı": klavyedeki medya tuşları (Spotify, YouTube, hangisi çalıyorsa).</summary>
public sealed class MediaTool(IDesktop desktop) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "medya",
        "Çalan müziği/videoyu kontrol eder (hangi uygulamada çalıyorsa): oynat_durdur, sonraki, onceki, durdur.",
        """{"type":"object","properties":{"islem":{"type":"string","enum":["oynat_durdur","sonraki","onceki","durdur"]}},"required":["islem"]}""");

    public PermissionLevel Level => PermissionLevel.Bildirerek;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        (MediaKey Key, string Text)? pick = ToolArgs.String(arguments, "islem") switch
        {
            "oynat_durdur" => (MediaKey.PlayPause, "Oynat/duraklat"),
            "sonraki" => (MediaKey.Next, "Sonraki parça"),
            "onceki" => (MediaKey.Previous, "Önceki parça"),
            "durdur" => (MediaKey.Stop, "Durdur"),
            _ => null,
        };
        if (pick is not { } p)
            return Task.FromResult(ToolResult.Fail("İşlem anlaşılamadı."));
        desktop.Press(p.Key);
        return Task.FromResult(ToolResult.Success($"{p.Text} tuşuna basıldı."));
    }
}

/// <summary>"Bilgisayarı kilitle" (serbest), "uyut / yeniden başlat / kapat" (onaylı).</summary>
public sealed class PowerTool(IDesktop desktop) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "guc",
        "Bilgisayarı kilitler, uyku moduna alır, yeniden başlatır ya da kapatır (kilitleme dışındakiler onay ister).",
        """{"type":"object","properties":{"islem":{"type":"string","enum":["kilitle","uyku","yeniden_baslat","kapat"]}},"required":["islem"]}""");

    public PermissionLevel Level => PermissionLevel.Onayli;

    public Task<PermissionLevel> LevelForAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolArgs.String(arguments, "islem") == "kilitle" ? PermissionLevel.Bildirerek : PermissionLevel.Onayli);

    public string Describe(JsonElement arguments) => ToolArgs.String(arguments, "islem") switch
    {
        "uyku" => "Bilgisayarı uyku moduna al",
        "yeniden_baslat" => "Bilgisayarı yeniden başlat (kaydedilmemiş işler kaybolabilir)",
        "kapat" => "Bilgisayarı kapat (kaydedilmemiş işler kaybolabilir; Ultron da kapanır)",
        _ => "Bilgisayarı kilitle",
    };

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        (PowerAction Action, string Text)? pick = ToolArgs.String(arguments, "islem") switch
        {
            "kilitle" => (PowerAction.Lock, "Bilgisayar kilitlendi."),
            "uyku" => (PowerAction.Sleep, "Bilgisayar uyku moduna alınıyor."),
            "yeniden_baslat" => (PowerAction.Restart, "Bilgisayar 15 saniye içinde yeniden başlatılıyor."),
            "kapat" => (PowerAction.Shutdown, "Bilgisayar 15 saniye içinde kapatılıyor."),
            _ => null,
        };
        if (pick is not { } p)
            return Task.FromResult(ToolResult.Fail("İşlem anlaşılamadı."));
        desktop.Power(p.Action);
        return Task.FromResult(ToolResult.Success(p.Text));
    }
}
