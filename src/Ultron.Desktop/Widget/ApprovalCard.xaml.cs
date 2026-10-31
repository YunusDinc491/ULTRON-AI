using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Ultron.Contracts;

namespace Ultron.Desktop.Widget;

/// <summary>Onay kartı: [Onayla] / [Reddet]; kalan süre geri sayılır. Yetki artırma istekleri kırmızı çerçeveyle ayrılır.</summary>
public partial class ApprovalCard : Window
{
    private readonly ApprovalRequestDto _request;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public ApprovalCard(ApprovalRequestDto request)
    {
        InitializeComponent();
        _request = request;
        Summary.Text = request.Summary;
        if (request.Summary.StartsWith("YETKİ ARTIRMA", StringComparison.Ordinal))
        {
            Title.Text = "YETKİ ARTIRMA ONAYI";
            Title.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            Frame.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D));
            Glow.Color = Color.FromRgb(0xFF, 0x30, 0x30);
            Note.Text = "Bu, Ultron'un yetkilerini kalıcı ya da süreli olarak artırır. Sen istemediysen REDDET.";
        }

        _tick.Tick += (_, _) => UpdateCountdown();
        UpdateCountdown();
        _tick.Start();
        Loaded += (_, _) => Place();
    }

    /// <summary>Kullanıcının kararı (true: onay).</summary>
    public event Action<long, bool>? Decided;

    public long Id => _request.Id;

    private void UpdateCountdown()
    {
        var left = _request.ExpiresAtUtc - DateTime.UtcNow;
        Countdown.Text = left > TimeSpan.Zero ? $"{(int)left.TotalSeconds} sn" : "süre doldu";
    }

    /// <summary>Ekranın alt ortası, görev çubuğunun hemen üstü.</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 24;
    }

    private void OnApprove(object sender, RoutedEventArgs e) => Decide(true);

    private void OnReject(object sender, RoutedEventArgs e) => Decide(false);

    private void Decide(bool approve)
    {
        _tick.Stop();
        Decided?.Invoke(_request.Id, approve);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        base.OnClosed(e);
    }
}
