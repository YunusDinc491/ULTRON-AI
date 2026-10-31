using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Ultron.Core.Mobile;

namespace Ultron.Host.Mobile;

/// <summary>
/// Telefondan erişim (Faz 25, tasarım kararı: Tailscale). Ultron sadece 127.0.0.1'i dinliyordu; Tailscale kuruluysa onun özel ağ
/// adresi (100.64.0.0/10) de dinlenir — ev ağına ya da internete açılmaz. Bu bilgisayardan gelmeyen her istek için: sadece
/// eşleştirme ucu açık, diğer her şey (sohbet hub'ı, güncelleme) cihaz anahtarı ister; MCP, Chrome eklentisi ve geri kalan uçlar
/// dışarıya tamamen kapalı.
/// </summary>
public static class MobileAccess
{
    public const string TokenHeader = "X-Ultron-Token";

    /// <summary>Tailscale'in bu bilgisayara verdiği adres (100.64.0.0/10); Tailscale yoksa null.</summary>
    public static IPAddress? TailscaleAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(IsTailscale);

    public static bool IsTailscale(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var b = ip.GetAddressBytes();
        return b[0] == 100 && b[1] is >= 64 and <= 127;
    }

    /// <summary>Uzak isteklerin kapısı. Yerel (127.0.0.1) istekler eskisi gibi serbest.</summary>
    public static async Task GuardAsync(HttpContext context, RequestDelegate next, MobileDevices devices)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || IPAddress.IsLoopback(remote))
        {
            await next(context);
            return;
        }
        var path = context.Request.Path;
        if (path.StartsWithSegments("/mobil/eslestir") || path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }
        var allowed = path.StartsWithSegments("/hub") || path.StartsWithSegments("/mobil");
        // SignalR WebSocket başlık gönderemez: anahtar access_token sorgu değişkeniyle de gelebilir.
        var token = context.Request.Headers[TokenHeader].FirstOrDefault() ?? context.Request.Query["access_token"].FirstOrDefault();
        if (!allowed || devices.Authenticate(token) is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await next(context);
    }

    /// <summary>Eşleştirme ve güncelleme uçları.</summary>
    public static void MapMobile(this WebApplication app)
    {
        app.MapPost("/mobil/eslestir", (PairRequest request, MobileDevices devices, ILogger<MobileDevices> logger) =>
        {
            if (devices.Pair(request.Code ?? "", request.Name ?? "") is not { } paired)
                return Results.Unauthorized();
            logger.LogInformation("Telefon eşleşti: {Name} ({Id}) → bölüm {Profile}", paired.Device.Name, paired.Device.Id, paired.Device.Profile);
            return Results.Ok(new { token = paired.Token, id = paired.Device.Id, name = paired.Device.Name, profile = paired.Device.Profile });
        });

        // Telefon güncellemesi: /mobil altında olduğu için uzaktan cihaz anahtarı ister (GuardAsync).
        app.MapGet("/mobil/guncelleme", (MobileUpdate update) =>
            update.Latest() is { } release ? Results.Ok(release) : Results.NoContent());

        app.MapGet("/mobil/guncelleme/apk", (MobileUpdate update) =>
            update.Latest() is null ? Results.NotFound() : Results.File(update.ApkPath, "application/vnd.android.package-archive", MobileUpdate.ApkName));
    }

    public sealed record PairRequest(string? Code, string? Name);
}
