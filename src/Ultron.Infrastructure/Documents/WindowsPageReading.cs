using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Ultron.Core.Documents;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;

namespace Ultron.Infrastructure.Documents;

/// <summary>PDF sayfalarını Windows'un kendi PDF çizicisiyle (Windows.Data.Pdf, Edge'in kullandığı) PNG'ye çevirir.</summary>
internal sealed class WindowsPdfRenderer : IPdfPageRenderer
{
    /// <summary>Genişlik: Windows OCR en fazla 2600 px kabul eder; 1600 px slayt yazısı için yeterince net.</summary>
    public const uint Width = 1600;

    public async Task<IReadOnlyList<PageImage>> RenderAsync(byte[] pdf, IReadOnlyCollection<int> pages, CancellationToken cancellationToken = default)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(pdf.AsBuffer()).AsTask(cancellationToken);
        input.Seek(0);
        var document = await WinPdf.PdfDocument.LoadFromStreamAsync(input).AsTask(cancellationToken);

        var images = new List<PageImage>(pages.Count);
        foreach (var number in pages.Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (number < 1 || number > document.PageCount)
                continue;
            using var page = document.GetPage((uint)number - 1);
            using var output = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(output, new WinPdf.PdfPageRenderOptions
            {
                DestinationWidth = Width,
                BitmapEncoderId = BitmapEncoder.PngEncoderId,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            }).AsTask(cancellationToken);

            var bytes = new byte[output.Size];
            output.Seek(0);
            await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None).AsTask(cancellationToken);
            images.Add(new PageImage(number, bytes));
        }

        return images;
    }
}

/// <summary>
/// Windows'un yerleşik yazı tanıması (Windows.Media.Ocr). Türkçe tanıyıcı İngilizce slaytları da okuyor (Latin harfler);
/// Türkçe dil paketi yoksa İngilizce, o da yoksa kullanıcı dilleri.
/// </summary>
internal sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly OcrEngine? _engine;

    public WindowsOcrEngine(ILogger<WindowsOcrEngine> logger)
    {
        try
        {
            _engine = OcrEngine.TryCreateFromLanguage(new Language("tr"))
                ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                ?? OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TypeLoadException or PlatformNotSupportedException)
        {
            logger.LogWarning(ex, "Windows yazı tanıma başlatılamadı");
        }

        if (_engine is null)
            logger.LogWarning("Windows yazı tanıma dili bulunamadı; resimli sayfalar sadece görsel okumayla okunacak");
    }

    public bool Available => _engine is not null;

    public async Task<string> RecognizeAsync(byte[] png, CancellationToken cancellationToken = default)
    {
        if (_engine is null)
            return "";
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer()).AsTask(cancellationToken);
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken);
        var result = await _engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
        return string.Join("\n", result.Lines.Select(l => l.Text));
    }
}


