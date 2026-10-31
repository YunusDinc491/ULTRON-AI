using System.Text.Json;

namespace Ultron.Core.Mobile;

/// <summary>Telefona sunulan en yeni APK'nın sürümü (Android versionCode), gösterilen adı ve boyutu.</summary>
public sealed record MobileRelease(int Surum, string Ad, long Boyut);

/// <summary>
/// Telefon güncellemesi (karar 2026-10-30: uygulama içinden). <c>installer\build-mobile.ps1</c> APK'yı derleyip
/// <c>%USERPROFILE%\Ultron\Ultron-mobil.apk</c> ve yanına <c>Ultron-mobil.json</c> ({"surum":2,"ad":"1.2"}) koyar; telefon
/// açılışta sürümü sorar, kendisininkinden büyükse "Güncelle" der. Mağaza gibi: raf burası, telefon gelip bakar.
/// </summary>
public sealed class MobileUpdate(string? folder = null)
{
    public const string ApkName = "Ultron-mobil.apk", InfoName = "Ultron-mobil.json";

    public static readonly string DefaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron");

    private readonly string _folder = folder ?? DefaultFolder;

    public string ApkPath => Path.Combine(_folder, ApkName);

    /// <summary>Sunulacak APK; APK ya da geçerli sürüm dosyası yoksa null (eski elle derlenmiş APK sunulmaz).</summary>
    public MobileRelease? Latest()
    {
        var info = Path.Combine(_folder, InfoName);
        if (!File.Exists(ApkPath) || !File.Exists(info))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(info));
            var root = doc.RootElement;
            if (!root.TryGetProperty("surum", out var code) || !code.TryGetInt32(out var surum) || surum < 1)
                return null;
            var ad = root.TryGetProperty("ad", out var name) && name.GetString() is { Length: > 0 } text ? text : surum.ToString();
            return new MobileRelease(surum, ad, new FileInfo(ApkPath).Length);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
