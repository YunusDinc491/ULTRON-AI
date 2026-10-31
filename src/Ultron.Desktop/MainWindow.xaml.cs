using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace Ultron.Desktop;

/// <summary>
/// Üç görünüm: ekranın sağına yapışık dar "kenar paneli" (hızlı soru), ortada "büyük pencere" ve
/// kenarlıksız, başlık çubuksuz, görev çubuğunu da kaplayan "tam ekran" (F11).
/// Ctrl+Alt+U her yerden açar/gizler (Ctrl+Alt+Space Claude uygulamasında kullanıldığı için). Kapatma düğmesi uygulamayı kapatmaz, gizler.
/// </summary>
public partial class MainWindow : Window
{
    private const int HotkeyId = 0x5554; // "UT"
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModNoRepeat = 0x4000, VkU = 0x55;

    private const double PanelWidth = 440;

    private enum ViewMode { Panel, Window, FullScreen }

    private ViewMode _mode = ViewMode.Panel;
    private ViewMode _beforeFullScreen = ViewMode.Panel;
    private HwndSource? _hwndSource;

    public MainWindow(IServiceProvider services)
    {
        Resources.Add("services", services); // BlazorWebView bileşenleri DI'ı buradan alır
        InitializeComponent();
        Icon = UltronIcon.CreateImageSource();
        ApplyPanelMode();
        WindowBridge.Attach(this);

        SourceInitialized += (_, _) => RegisterHotkey();
        Closing += OnClosing;
        // Küçültme (görev çubuğu, Win+↓) görev çubuğuna inmez: pencere gizlenir, ekranda Ultron simgesi kalır (kullanıcının isteği).
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
                return;
            DesktopLog.Write("Küçültme → simgeye");
            WindowState = WindowState.Normal;
            Hide();
        };
    }

    public bool IsFullScreen => _mode == ViewMode.FullScreen;

    /// <summary>Sadece büyük pencere taşınır (kenar paneli ve tam ekranın yeri sabit).</summary>
    public bool CanDrag => _mode == ViewMode.Window;

    /// <summary>Tam ekrana girilince/çıkılınca (arayüz çıkış düğmesini göstersin diye).</summary>
    public event Action<bool>? FullScreenChanged;

    /// <summary>Tepsideki "Çıkış" seçilince true olur; aksi hâlde kapatma = gizleme.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Kısayol başka bir uygulamada kayıtlıysa.</summary>
    public event Action? HotkeyFailed;

    public void ToggleVisibility()
    {
        DesktopLog.Write($"Aç/gizle: görünür={IsVisible}, etkin={IsActive}");
        if (IsVisible && IsActive)
            Hide();
        else
            ShowAndFocus();
    }

    public void ShowAndFocus()
    {
        if (_mode == ViewMode.Panel)
            ApplyPanelMode(); // çözünürlük/görev çubuğu değişmiş olabilir
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        WebView.Focus(); // sayfadaki "focus" olayı yazı kutusuna odaklanır
    }

    /// <summary>İş arayüzü (Faz 16) yarım ekran önizleme ister: kenar panelindeyse büyük pencereye geç.</summary>
    public void EnsureLarge()
    {
        if (_mode == ViewMode.Panel)
            Apply(ViewMode.Window);
    }

    /// <summary>Kenar paneli ⇄ büyük pencere (tam ekrandaysa önce oradan çıkar).</summary>
    public void TogglePanelMode() =>
        Apply(_mode == ViewMode.Panel ? ViewMode.Window : ViewMode.Panel);

    public void ToggleFullScreen()
    {
        if (_mode == ViewMode.FullScreen)
        {
            Apply(_beforeFullScreen);
            return;
        }

        _beforeFullScreen = _mode;
        Apply(ViewMode.FullScreen);
    }

    private void Apply(ViewMode mode)
    {
        var wasFullScreen = _mode == ViewMode.FullScreen;
        _mode = mode;
        var chrome = WindowChrome.GetWindowChrome(this);

        if (mode == ViewMode.FullScreen)
        {
            // Başlık çubuğu, kenarlık ve boyutlandırma kenarı kalkar; pencere tüm ekranı (görev çubuğu dahil) kaplar.
            TitleRow.Height = new GridLength(0);
            RootBorder.BorderThickness = new Thickness(0);
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(0);
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }
        else
        {
            // WPF başlık satırı yok: başlık çubuğu HUD'un üst çubuğu (sürükleme ve düğmeler WindowBridge'ten).
            TitleRow.Height = new GridLength(0);
            RootBorder.BorderThickness = new Thickness(1);
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(6);
            ResizeMode = ResizeMode.CanResize;
            if (mode == ViewMode.Panel)
                ApplyPanelMode();
            else
                ApplyWindowMode();
        }

        DesktopLog.Write($"Görünüm: {mode}");
        if (wasFullScreen != (mode == ViewMode.FullScreen))
            FullScreenChanged?.Invoke(mode == ViewMode.FullScreen);
    }

    private void ApplyPanelMode()
    {
        var area = SystemParameters.WorkArea;
        Topmost = true;
        Width = PanelWidth;
        Height = area.Height;
        Left = area.Right - PanelWidth;
        Top = area.Top;
        ModeButton.Content = "⤢";
        ModeButton.ToolTip = "Büyük pencere";
    }

    private void ApplyWindowMode()
    {
        var area = SystemParameters.WorkArea;
        Topmost = false;
        Width = Math.Min(1100, area.Width - 80);
        Height = Math.Min(780, area.Height - 60);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
        ModeButton.Content = "⤡";
        ModeButton.ToolTip = "Kenar paneli";
    }

    private void OnModeClick(object sender, RoutedEventArgs e) => TogglePanelMode();

    private void OnFullScreenClick(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        DesktopLog.Write("Gizle düğmesi");
        Hide();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
            return;
        DesktopLog.Write("Kapatma isteği → gizlendi");
        e.Cancel = true;
        Hide();
    }

    private void RegisterHotkey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource.AddHook(WndProc);
        if (RegisterHotKey(handle, HotkeyId, ModControl | ModAlt | ModNoRepeat, VkU))
        {
            DesktopLog.Write("Kısayol kaydedildi: Ctrl+Alt+U");
        }
        else
        {
            DesktopLog.Write($"Kısayol kaydedilemedi (Win32 hata {Marshal.GetLastWin32Error()})");
            HotkeyFailed?.Invoke();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            DesktopLog.Write("Kısayol basıldı");
            ToggleVisibility();
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
        _hwndSource?.RemoveHook(WndProc);
        base.OnClosed(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
