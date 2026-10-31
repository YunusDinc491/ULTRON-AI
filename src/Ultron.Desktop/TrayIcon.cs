using WinForms = System.Windows.Forms;

namespace Ultron.Desktop;

/// <summary>
/// Sistem tepsisindeki Ultron ikonu. WPF'te hazır tepsi ikonu olmadığı için Windows Forms'unkini kullanıyoruz.
/// Sol tık: aç/gizle. Sağ tık: menü.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _wakeWord;
    private bool _syncing;

    public TrayIcon(MainWindow window, Action exit, Action restart)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Ultron'u aç  (Ctrl+Alt+U)", null, (_, _) => window.ShowAndFocus());
        menu.Items.Add("Kenar paneli / Büyük pencere", null, (_, _) =>
        {
            window.TogglePanelMode();
            window.ShowAndFocus();
        });
        menu.Items.Add("Tam ekran  (F11)", null, (_, _) =>
        {
            window.ShowAndFocus();
            window.ToggleFullScreen();
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        var autoStart = new WinForms.ToolStripMenuItem("Windows açılışında başlat") { Checked = DesktopSettings.AutoStart, CheckOnClick = true };
        autoStart.CheckedChanged += (_, _) => DesktopSettings.AutoStart = autoStart.Checked;
        menu.Items.Add(autoStart);
        // "Ultron" deyince dinle (Faz 14): ayar Host'ta; işaret Host'tan gelen değere göre ayarlanır.
        _wakeWord = new WinForms.ToolStripMenuItem($"\"{Voice.WakeWordRules.Word}\" deyince dinle") { CheckOnClick = true };
        _wakeWord.CheckedChanged += (_, _) =>
        {
            if (!_syncing)
                WakeWordToggled?.Invoke(_wakeWord.Checked);
        };
        menu.Items.Add(_wakeWord);
        menu.Items.Add($"\"{Voice.WakeWordRules.Word}\" ses örneklerimi kaydet…", null, (_, _) => WakeWordSetupRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Yeniden başlat (arka plan servisiyle)", null, (_, _) => restart());
        menu.Items.Add("Çıkış", null, (_, _) => exit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = UltronIcon.Create(),
            Text = "Ultron (Ctrl+Alt+U)",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.BalloonTipClicked += (_, _) => MessageClicked?.Invoke();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                window.ToggleVisibility();
        };
    }

    public void ShowMessage(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.Info);

    /// <summary>Tepsideki "Ultron deyince dinle" işaretlendi/kaldırıldı.</summary>
    public event Action<bool>? WakeWordToggled;

    /// <summary>Tepsideki "ses örneklerimi kaydet".</summary>
    public event Action? WakeWordSetupRequested;

    public void SetWakeWordChecked(bool on)
    {
        _syncing = true;
        _wakeWord.Checked = on;
        _syncing = false;
    }

    /// <summary>Menüde ve ikon ipucunda durum: "dinliyor", "kapalı", "mikrofon açılamadı…".</summary>
    public void SetWakeWordStatus(string status)
    {
        _wakeWord.Text = $"\"{Voice.WakeWordRules.Word}\" deyince dinle ({status})";
        _icon.Text = status == "dinliyor" ? "Ultron · 🎙 dinliyor (Ctrl+Alt+U)" : "Ultron (Ctrl+Alt+U)";
    }

    /// <summary>Windows bildirimine tıklandı (hatırlatmada "okudum" sayılır).</summary>
    public event Action? MessageClicked;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
