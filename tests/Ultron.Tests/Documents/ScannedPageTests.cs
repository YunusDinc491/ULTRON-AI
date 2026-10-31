using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Ultron.Core.Documents;
using Ultron.Infrastructure.Documents;
using Ultron.Tests.Fakes;

namespace Ultron.Tests.Documents;

public sealed class ScannedPageReaderTests
{
    private static readonly string LongText = string.Join(" ", Enumerable.Repeat("Isı iletimi Fourier yasası ile anlatılır.", 8));

    [Fact]
    public async Task Ocr_yeterliyse_ocr_az_yazi_ya_da_formul_varsa_gorsel_okuma_kullanilir()
    {
        var ocr = new FakeOcr(new() { [2] = LongText, [3] = "Şekil 1", [4] = "q = -k A dT/dx = h (Ts - T∞) + σ ε (Ts^4 - Tsur^4)" + LongText });
        var vision = new FakeVision("Görsel okuma: " + LongText + " Şema: sıcak yüzeyden akışkana ısı geçer.");
        var reader = new ScannedPageReader(new FakeRenderer(), ocr, vision, NullLogger<ScannedPageReader>.Instance);
        DocumentPage[] pages = [new(1, "Yazı katmanı olan sayfa, zaten okunuyor."), new(2, ""), new(3, " "), new(4, "")];

        var result = await reader.FillAsync([1], pages);

        Assert.Equal("Yazı katmanı olan sayfa, zaten okunuyor.", result.Pages[0].Text); // yazılı sayfaya dokunulmaz
        Assert.Equal(LongText, result.Pages[1].Text);                               // OCR yeterli
        Assert.StartsWith("Görsel okuma", result.Pages[2].Text);                    // az yazı → görsel
        Assert.StartsWith("Görsel okuma", result.Pages[3].Text);                    // formül → görsel
        Assert.Equal((1, 2), (result.OcrPages, result.VisionPages));
        Assert.Equal([3, 4], vision.Asked); // sadece az yazılı ve formüllü sayfa görsele gitti
    }

    [Fact]
    public async Task Hicbir_yol_okuyamazsa_sayfa_okunamadi_olarak_kalir()
    {
        var reader = new ScannedPageReader(new FakeRenderer(), new FakeOcr([]), new FakeVision("", fails: true), NullLogger<ScannedPageReader>.Instance);

        var result = await reader.FillAsync([1], [new DocumentPage(1, "")]);

        Assert.Equal([1], result.Failed);
        Assert.Equal("", result.Pages[0].Text);
    }

    [Fact]
    public async Task Belge_eklenirken_resimli_sayfalar_okunur_ve_mesajda_bildirilir()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ultron-tarama-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var scanner = new ScannedPageReader(new FakeRenderer(), new FakeOcr(new() { [2] = LongText }), new FakeVision("Şema: ısı değiştirici, iki akışkan."), NullLogger<ScannedPageReader>.Instance);
            var service = new DocumentService(new InMemoryDocumentStore(), new FakeTextExtractor(), new FakeEmbeddingModel([]),
                Options.Create(new DocumentOptions { Folder = folder }), NullLogger<DocumentService>.Instance, scanner);

            var result = await service.AddFileAsync("week_1.pdf", Encoding.UTF8.GetBytes("Kapak sayfası, ders adı ve hafta bilgisi.\f \f "), null);

            Assert.Equal(DocumentAddStatus.Added, result.Status);
            Assert.Contains("Resimli 2 sayfa okundu (1 yazı tanıma, 1 görsel okuma)", result.Message);
            Assert.DoesNotContain("Dikkat", result.Message);
            Assert.Single(await service.SearchAsync("Fourier yasası", null), h => h.Chunk.Page == 2);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("Şekil 1", true)]
    [InlineData("q = -k A dT/dx, h = q / (Ts - T) ve Nu = h L / k, Re = V L / ν; Pr = ν / α", true)]
    public void Gorsel_okuma_karari(string ocr, bool expected) => Assert.Equal(expected, ScannedPageReader.NeedsVision(ocr));

    [Fact]
    public void Uzun_duz_yazi_gorsel_okumaya_gitmez() => Assert.False(ScannedPageReader.NeedsVision(LongText));

    private sealed class FakeRenderer : IPdfPageRenderer
    {
        public Task<IReadOnlyList<PageImage>> RenderAsync(byte[] pdf, IReadOnlyCollection<int> pages, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PageImage>>(pages.Order().Select(p => new PageImage(p, [(byte)p])).ToList());
    }

    private sealed class FakeOcr(Dictionary<int, string> byPage) : IOcrEngine
    {
        public bool Available => true;

        public Task<string> RecognizeAsync(byte[] png, CancellationToken cancellationToken = default) =>
            Task.FromResult(byPage.GetValueOrDefault(png[0], ""));
    }

    private sealed class FakeVision(string text, bool fails = false) : IVisionReader
    {
        public List<int> Asked { get; } = [];

        public Task<string> ReadPageAsync(byte[] png, CancellationToken cancellationToken = default)
        {
            Asked.Add(png[0]);
            return fails ? throw new HttpRequestException("model yok") : Task.FromResult(text);
        }
    }
}

/// <summary>Gerçek Windows bileşenleri: PDF sayfası görüntüye çevrilir, Windows OCR okur.</summary>
public sealed class WindowsOcrTests
{
    [Fact]
    public async Task Pdf_sayfasi_goruntuye_cevrilir_ve_windows_ocr_yaziyi_okur()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddTrueTypeFont(File.ReadAllBytes(@"C:\Windows\Fonts\arial.ttf"));
        builder.AddPage(PageSize.A4).AddText("Entropi düzensizliğin ölçüsüdür", 28, new PdfPoint(40, 700), font);
        builder.AddPage(PageSize.A4).AddText("Heat transfer by convection", 28, new PdfPoint(40, 700), font);

        var images = await new WindowsPdfRenderer().RenderAsync(builder.Build(), [2, 1]);
        var ocr = new WindowsOcrEngine(NullLogger<WindowsOcrEngine>.Instance);
        Assert.True(ocr.Available, "Windows OCR dili kurulu değil");
        var first = await ocr.RecognizeAsync(images[0].Png);
        var second = await ocr.RecognizeAsync(images[1].Png);

        Assert.Equal([1, 2], images.Select(i => i.Page));
        Assert.StartsWith("\x89PNG", Encoding.Latin1.GetString(images[0].Png, 0, 4));
        Assert.Contains("Entropi", first);
        Assert.Contains("convection", second);
    }
}
