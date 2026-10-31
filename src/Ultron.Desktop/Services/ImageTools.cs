using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Ultron.Desktop.Services;

/// <summary>
/// Görselli sorular (Faz 11): görseli modele uygun boya küçültür ve "ekrana bak" için ekran görüntüsü alır.
/// Görüntüler sadece bu bilgisayarda (Host → Ollama) işlenir, diske kaydedilmez.
/// </summary>
public static class ImageTools
{
    /// <summary>Uzun kenar en fazla bu kadar piksel: gemma4 ayrıntıyı görür, istek küçük kalır (~200-400 KB).</summary>
    public const int MaxSide = 1600;

    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"];

    public static bool IsImage(string fileName) =>
        ImageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <summary>Herhangi bir görsel → uzun kenarı en fazla <see cref="MaxSide"/> olan JPEG.</summary>
    public static byte[] Prepare(byte[] image)
    {
        var frame = BitmapDecoder.Create(new MemoryStream(image), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        BitmapSource source = frame;
        var scale = Math.Min(1.0, (double)MaxSide / Math.Max(frame.PixelWidth, frame.PixelHeight));
        if (scale < 1)
            source = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        // Saydam PNG'ler JPEG'de siyah zemin olmasın: beyaz zemine düzleştir.
        var flat = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
        encoder.Frames.Add(BitmapFrame.Create(flat));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>Farenin bulunduğu ekranın görüntüsü (fiziksel piksellerle), küçültülmüş JPEG.</summary>
    public static byte[] CaptureScreen()
    {
        var screen = WinForms.Screen.FromPoint(WinForms.Cursor.Position).Bounds;
        using var bitmap = new Drawing.Bitmap(screen.Width, screen.Height);
        using (var g = Drawing.Graphics.FromImage(bitmap))
            g.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size);
        using var png = new MemoryStream();
        bitmap.Save(png, Drawing.Imaging.ImageFormat.Png);
        return Prepare(png.ToArray());
    }

    /// <summary>Arayüzde küçük önizleme için data: adresi.</summary>
    public static string DataUrl(byte[] jpeg) => "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);

    /// <summary>
    /// "Ekrana bak", "ekranımda ne var", "ekrandaki hatayı açıkla"… Sadece kullanıcı açıkça isterse ekran görüntüsü alınır.
    /// </summary>
    public static bool IsScreenRequest(string text)
    {
        var t = " " + Normalize(text) + " ";
        string[] cues = [" ekrana bak", " ekranima bak", " ekranimda ne", " ekranda ne", " ekrandaki", " ekranimdaki", " ekrani oku", " ekranimi oku", " ekrana goz at", " ekranimi incele", " ekrani incele", " su an ekranda"];
        return cues.Any(c => t.Contains(c, StringComparison.Ordinal));
    }

    private static string Normalize(string text) =>
        new string(text.ToLower(new System.Globalization.CultureInfo("tr-TR")).Select(c => c switch
        {
            'ı' => 'i', 'ş' => 's', 'ç' => 'c', 'ğ' => 'g', 'ö' => 'o', 'ü' => 'u',
            _ => char.IsLetterOrDigit(c) ? c : ' ',
        }).ToArray());
}

/// <summary>Gönderilmeyi bekleyen görsel (yazı kutusunun üstünde önizleme).</summary>
public sealed record PendingImage(byte[] Jpeg, string Name)
{
    public string Preview { get; } = ImageTools.DataUrl(Jpeg);
}
