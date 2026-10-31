using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.JSInterop;

namespace Ultron.Desktop;

/// <summary>
/// Blazor arayüzü (WebView içinde) ile WPF penceresi arasındaki köprü. Tuşlar WebView'a gittiği için
/// F11 gibi pencere komutları JavaScript'ten buraya gelir; pencerenin durumu da buradan arayüze bildirilir.
/// Başlık çubuğu artık HUD'un üst çubuğu (kullanıcı: "üst kısım üste dayalı olsun"): sürükleme ve pencere düğmeleri buradan.
/// </summary>
public static class WindowBridge
{
    private const int WmNcLButtonDown = 0xA1, HtCaption = 2;

    private static MainWindow? _window;

    public static event Action<bool>? FullScreenChanged;

    internal static void Attach(MainWindow window)
    {
        _window = window;
        window.FullScreenChanged += fullScreen => FullScreenChanged?.Invoke(fullScreen);
    }

    /// <summary>JavaScript'ten (F11) ve arayüzden çağrılır. Pencere işi WPF iş parçacığında yapılmalı.</summary>
    [JSInvokable]
    public static void ToggleFullScreen() => _window?.Dispatcher.InvokeAsync(() => _window.ToggleFullScreen());

    /// <summary>Kenar paneli ⇄ büyük pencere.</summary>
    [JSInvokable]
    public static void TogglePanelMode() => _window?.Dispatcher.InvokeAsync(() => _window.TogglePanelMode());

    /// <summary>Kurulum sihirbazı (Faz 26) "Ultron" ses örneği kayıt penceresini ister; App açar (tepsideki menüyle aynı pencere).</summary>
    public static event Action? WakeWordSetupRequested;

    public static void ShowWakeWordSetup() => WakeWordSetupRequested?.Invoke();

    /// <summary>İş arayüzü için büyük pencere (kenar panelindeyse).</summary>
    public static void EnsureLarge() => _window?.Dispatcher.InvokeAsync(() => _window.EnsureLarge());

    /// <summary>Gizle / kapat: pencere gizlenir, masaüstündeki Ultron simgesi geri gelir.</summary>
    [JSInvokable]
    public static void HideWindow() => _window?.Dispatcher.InvokeAsync(() =>
    {
        DesktopLog.Write("Gizle düğmesi (üst çubuk)");
        _window.Hide();
    });

    /// <summary>
    /// Üst çubuktan sürükleme: fare hâlâ basılıyken Windows'a "başlık çubuğuna basıldı" denir, taşımayı Windows yapar.
    /// Tam ekranda ve kenar panelinde taşınmaz (yeri sabit).
    /// </summary>
    [JSInvokable]
    public static void BeginDrag() => _window?.Dispatcher.InvokeAsync(() =>
    {
        if (!_window.CanDrag)
            return;
        var handle = new WindowInteropHelper(_window).Handle;
        ReleaseCapture();
        SendMessage(handle, WmNcLButtonDown, HtCaption, 0);
    });

    /// <summary>
    /// "Ekrana bak": Ultron penceresi bir an gizlenir (kendisi görüntüye girmesin), ekran görüntüsü alınır, pencere geri gelir.
    /// </summary>
    /// <summary>Ekran görüntüsü alınırken true.</summary>
    public static bool Capturing { get; private set; }

    public static async Task<byte[]> CaptureScreenAsync()
    {
        var window = _window;
        var wasVisible = window?.IsVisible == true;
        Capturing = true; // masaüstü simgesi bu arada görünmesin (görüntüye girmesin)
        if (wasVisible)
        {
            window!.Hide();
            await Task.Delay(250); // pencerenin ekrandan silinmesi için
        }

        try
        {
            return await Task.Run(Services.ImageTools.CaptureScreen);
        }
        finally
        {
            if (wasVisible)
                window!.ShowAndFocus();
            Capturing = false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
