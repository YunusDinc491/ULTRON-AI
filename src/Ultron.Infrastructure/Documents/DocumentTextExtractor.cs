using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Ultron.Core.Documents;
using Wordprocessing = DocumentFormat.OpenXml.Wordprocessing;

namespace Ultron.Infrastructure.Documents;

/// <summary>PDF (PdfPig, sayfa sayfa), Word .docx (OpenXML, paragraf sırasıyla) ve düz metinden yazı çıkarır.</summary>
internal sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    public DocumentKind? KindOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => DocumentKind.Pdf,
        ".docx" => DocumentKind.Word,
        ".pptx" => DocumentKind.Slides,
        ".txt" or ".md" or ".markdown" or ".csv" => DocumentKind.Text,
        _ => null,
    };

    public IReadOnlyList<DocumentPage> Extract(DocumentKind kind, byte[] content) => kind switch
    {
        DocumentKind.Pdf => Pdf(content),
        DocumentKind.Word => Word(content),
        DocumentKind.Slides => Slides(content),
        _ => [new DocumentPage(null, Decode(content))],
    };

    private static List<DocumentPage> Pdf(byte[] content)
    {
        using var pdf = PdfDocument.Open(content);
        var pages = new List<DocumentPage>(pdf.NumberOfPages);
        foreach (var page in pdf.GetPages())
        {
            // İçerik sırasına göre okuma: page.Text kelimeler arasındaki boşlukları bazen yutuyor.
            var text = ContentOrderTextExtractor.GetText(page);
            pages.Add(new DocumentPage(page.Number, text));
        }

        return pages;
    }

    private static List<DocumentPage> Word(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
            return [];

        // Word'de sayfa bilgisi dosyada yok (sayfalara bölme ekranda yapılır): tek parça metin, paragraflar boş satırla.
        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Wordprocessing.Paragraph>())
        {
            var text = paragraph.InnerText.Trim();
            if (text.Length > 0)
                sb.Append(text).Append("\n\n");
        }

        return [new DocumentPage(null, sb.ToString())];
    }

    /// <summary>Ders slaytları (Faz 17): slayt sırasıyla, her slaytın yazıları (başlık, maddeler, tablo, notlar) ayrı sayfa.</summary>
    private static List<DocumentPage> Slides(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var deck = PresentationDocument.Open(stream, isEditable: false);
        var presentation = deck.PresentationPart;
        var ids = presentation?.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ToList() ?? [];
        var pages = new List<DocumentPage>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i].RelationshipId?.Value is not { } rel || presentation!.GetPartById(rel) is not SlidePart slide)
                continue;
            var sb = new StringBuilder();
            foreach (var paragraph in slide.Slide?.Descendants<DocumentFormat.OpenXml.Drawing.Paragraph>() ?? [])
            {
                var text = string.Concat(paragraph.Descendants<DocumentFormat.OpenXml.Drawing.Text>().Select(t => t.Text)).Trim();
                if (text.Length > 0)
                    sb.AppendLine(text);
            }
            if (slide.NotesSlidePart?.NotesSlide is { } notes)
            {
                var noteText = string.Join(" ", notes.Descendants<DocumentFormat.OpenXml.Drawing.Text>().Select(t => t.Text)).Trim();
                if (noteText.Length > 0)
                    sb.AppendLine("Konuşmacı notu: " + noteText);
            }
            pages.Add(new DocumentPage(i + 1, sb.ToString()));
        }
        return pages;
    }

    /// <summary>UTF-8 (BOM'lu ya da BOM'suz); geçersizse Türkçe Windows kod sayfası (eski not dosyaları).</summary>
    private static string Decode(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(content).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1254).GetString(content);
        }
    }
}
