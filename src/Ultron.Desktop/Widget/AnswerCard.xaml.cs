using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace Ultron.Desktop.Widget;

/// <summary>Simgenin yanında açılan cevap kartı. İş bitince 25 sn (uzun cevapta daha fazla) sonra kendiliğinden kapanır.</summary>
public partial class AnswerCard : Window
{
    private readonly DispatcherTimer _autoHide = new();

    public AnswerCard()
    {
        InitializeComponent();
        _autoHide.Tick += (_, _) =>
        {
            _autoHide.Stop();
            Hide();
        };
    }

    /// <summary>Karta tıklandı (arayüz açılsın).</summary>
    public event Action? Opened;

    public void ShowStatus(string status, string? question = null)
    {
        _autoHide.Stop();
        Status.Text = status;
        if (question is not null)
        {
            Question.Text = "“" + question + "”";
            Question.Visibility = Visibility.Visible;
        }
        else if (status.StartsWith("DİNLİYOR", StringComparison.Ordinal))
        {
            Question.Visibility = Visibility.Collapsed;
            Answer.Text = "";
        }

        if (!IsVisible)
            Show();
    }

    public void SetAnswer(string markdown) => Answer.Text = Plain(markdown);

    /// <summary>İş bitti: kart bir süre açık kalıp kaybolur (okuma süresi cevap uzunluğuyla artar).</summary>
    public void Finish(string status)
    {
        Status.Text = status;
        _autoHide.Interval = TimeSpan.FromSeconds(Math.Clamp(15 + Answer.Text.Length / 25, 20, 90));
        _autoHide.Start();
    }

    /// <summary>Kartı simgenin yanına koyar: simge ekranın sağ yarısındaysa soluna, değilse sağına.</summary>
    public void PlaceNear(Rect icon)
    {
        var area = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        Left = icon.Left + icon.Width / 2 > area.Width / 2 ? icon.Left - width + 6 : icon.Right - 6;
        Top = Math.Clamp(icon.Top, area.Top, Math.Max(area.Top, area.Bottom - Math.Max(ActualHeight, 160)));
    }

    private static readonly Dictionary<string, string> Greek = new()
    {
        ["eta"] = "η", ["Delta"] = "Δ", ["delta"] = "δ", ["alpha"] = "α", ["beta"] = "β", ["rho"] = "ρ", ["mu"] = "μ",
        ["sigma"] = "σ", ["theta"] = "θ", ["pi"] = "π", ["lambda"] = "λ", ["cdot"] = "·", ["times"] = "×", ["frac"] = "",
    };

    /// <summary>Kartta düz metin: kalın/başlık işaretleri atılır, madde işaretleri • olur.</summary>
    internal static string Plain(string markdown)
    {
        // Kart formül çizmiyor: "$\eta = 1 - T_L$" → "η = 1 - T_L".
        var text = Regex.Replace(markdown, @"\${1,2}([^$\n]*?[\\_^=][^$\n]*?)\${1,2}", m =>
            Regex.Replace(m.Groups[1].Value, @"\\([a-zA-Z]+)", c => Greek.GetValueOrDefault(c.Groups[1].Value, c.Groups[1].Value)).Replace("{", "").Replace("}", ""));
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
        text = Regex.Replace(text, @"(?m)^#{1,6}\s*", "");
        text = Regex.Replace(text, @"(?m)^\s*[-*+]\s+", "• ");
        text = Regex.Replace(text, @"`([^`]*)`", "$1");
        return text.Trim();
    }

    private void OnCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Hide();
        Opened?.Invoke();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _autoHide.Stop();
        Hide();
    }
}
