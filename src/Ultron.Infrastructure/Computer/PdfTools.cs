using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;
using Ultron.Core.Computer;

namespace Ultron.Infrastructure.Computer;

/// <summary>PDF birleştirme/bölme (PdfPig; zaten belge okuma için kullanılıyor, ek paket yok). Asıl dosyalara dokunulmaz.</summary>
internal sealed class PdfTools : IPdfTools
{
    public int Merge(IReadOnlyList<string> inputs, string output)
    {
        var bytes = PdfMerger.Merge(inputs.Select(File.ReadAllBytes).ToList());
        File.WriteAllBytes(output, bytes);
        return PageCount(output);
    }

    public int Split(string input, IReadOnlyList<int> pages, string output)
    {
        var bytes = PdfMerger.Merge([File.ReadAllBytes(input)], [pages.ToList()]);
        File.WriteAllBytes(output, bytes);
        return PageCount(output);
    }

    public int PageCount(string input)
    {
        using var pdf = PdfDocument.Open(input);
        return pdf.NumberOfPages;
    }
}
