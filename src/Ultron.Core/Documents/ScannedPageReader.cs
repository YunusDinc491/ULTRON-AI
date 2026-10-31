using Microsoft.Extensions.Logging;

namespace Ultron.Core.Documents;

/// <summary>PDF sayfasının görüntüsü (PNG).</summary>
public sealed record PageImage(int Page, byte[] Png);

/// <summary>PDF sayfalarını görüntüye çevirir.</summary>
public interface IPdfPageRenderer
{
    Task<IReadOnlyList<PageImage>> RenderAsync(byte[] pdf, IReadOnlyCollection<int> pages, CancellationToken cancellationToken = default);
}

/// <summary>Görüntüdeki yazıyı tanır (hızlı, sadece yazı).</summary>
public interface IOcrEngine
{
    bool Available { get; }

    Task<string> RecognizeAsync(byte[] png, CancellationToken cancellationToken = default);
}

/// <summary>Görüntüyü görsel destekli dil modeliyle okur: yazı + formül + tablo + şemanın ne gösterdiği (yavaş).</summary>
public interface IVisionReader
{
    Task<string> ReadPageAsync(byte[] png, CancellationToken cancellationToken = default);
}

/// <summary>
/// Yazı katmanı olmayan (resim/taranmış) PDF sayfalarını okur. İki aşamalı sınav okuma gibi: önce optik okuyucu
/// (Windows OCR, sayfa başına &lt;1 sn), yazı az çıkarsa ya da sayfa formül/şema ağırlıklıysa öğretmen (gemma4 görsel okuma,
/// sayfa başına birkaç sn). tasarım kararı (2026-10-27).
/// </summary>
public sealed class ScannedPageReader(IPdfPageRenderer renderer, IOcrEngine ocr, IVisionReader vision, ILogger<ScannedPageReader> logger)
{
    /// <summary>OCR bundan az harf bulursa sayfa görsel okumaya gider (slaytta tek başlık + şema gibi).</summary>
    public const int MinOcrLetters = 150;

    /// <summary>OCR metninde bu kadar matematik işareti varsa formül ağırlıklı sayılır (OCR formülleri bozuyor).</summary>
    public const int FormulaSymbols = 6;

    /// <summary>Yazısız sayfaları okuyup metinlerini doldurur; diğer sayfalara dokunmaz.</summary>
    public async Task<ScanResult> FillAsync(byte[] pdf, IReadOnlyList<DocumentPage> pages, CancellationToken cancellationToken = default)
    {
        var blank = DocumentService.BlankPages(pages);
        if (blank.Count == 0)
            return new ScanResult(pages, 0, 0, []);

        var images = await renderer.RenderAsync(pdf, blank, cancellationToken);
        var texts = new Dictionary<int, string>();
        var ocrPages = 0;
        var visionPages = 0;
        var failed = new List<int>();
        foreach (var image in images)
        {
            var text = ocr.Available ? await SafeAsync(() => ocr.RecognizeAsync(image.Png, cancellationToken), image.Page, "OCR") : "";
            if (NeedsVision(text))
            {
                var read = await SafeAsync(() => vision.ReadPageAsync(image.Png, cancellationToken), image.Page, "görsel okuma");
                if (read.Count(char.IsLetter) > text.Count(char.IsLetter) / 2)
                {
                    text = read;
                    visionPages++;
                }
                else if (text.Length > 0)
                {
                    ocrPages++;
                }
            }
            else
            {
                ocrPages++;
            }

            if (text.Count(char.IsLetter) < 20)
                failed.Add(image.Page);
            else
                texts[image.Page] = text;
        }

        logger.LogInformation("Taranmış sayfalar: {Blank} sayfa → OCR {Ocr}, görsel okuma {Vision}, okunamadı {Failed}", blank.Count, ocrPages, visionPages, failed.Count);
        var filled = pages.Select(p => p.Page is { } n && texts.TryGetValue(n, out var t) ? p with { Text = t } : p).ToList();
        return new ScanResult(filled, ocrPages, visionPages, failed);
    }

    /// <summary>Az yazı (başlık + şema) ya da formül ağırlıklı metin → görsel okuma.</summary>
    internal static bool NeedsVision(string ocrText) =>
        ocrText.Count(char.IsLetter) < MinOcrLetters || ocrText.Count(c => "=+−^√∑∫Δ∂∞≤≥×÷/".Contains(c)) >= FormulaSymbols;

    private async Task<string> SafeAsync(Func<Task<string>> read, int page, string what)
    {
        try
        {
            return (await read()).Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "s. {Page} {What} başarısız", page, what);
            return "";
        }
    }
}

/// <param name="Pages">Doldurulmuş sayfalar.</param>
/// <param name="OcrPages">Windows OCR ile okunan sayfa sayısı.</param>
/// <param name="VisionPages">Görsel okumayla (gemma4) okunan sayfa sayısı.</param>
/// <param name="Failed">Hiçbir yolla okunamayan sayfalar.</param>
public sealed record ScanResult(IReadOnlyList<DocumentPage> Pages, int OcrPages, int VisionPages, IReadOnlyList<int> Failed);
