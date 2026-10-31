namespace Ultron.Host.Security;

/// <summary>
/// Tarayıcıdan gelen istekleri reddeder (güvenlik, 2026-10-31). Ultron 127.0.0.1'deki isteklere güvenir; ama bilgisayarda açık
/// herhangi bir web sitesi de 127.0.0.1:5199'a istek atabilir ve WebSocket bağlantıları tarayıcının CORS korumasına takılmaz
/// (cross-site WebSocket hijacking): kötü niyetli bir sayfa sohbet hub'ına bağlanıp Ultron'u yönetebilirdi. Tarayıcılar bu tür her
/// istekte Origin başlığı gönderir; Ultron'un kendi istemcileri (masaüstü ve telefon, .NET) göndermez. Tek istisna eşleştirme kodlu
/// Chrome eklentisi (kendi ucunda, chrome-extension:// kökeniyle). Benzetme: kapıdaki görevli, ev halkının anahtarla girdiğini,
/// sokaktan "ben de buradanım" diye seslenenin ise yabancı olduğunu bilir.
/// </summary>
public static class BrowserGuard
{
    public static Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (IsAllowed(context.Request.Path, context.Request.Headers.Origin.ToString()))
            return next(context);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    public static bool IsAllowed(PathString path, string origin) =>
        string.IsNullOrEmpty(origin)
        || (path.StartsWithSegments(Chrome.ChromeBridge.Path) && origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase));
}
