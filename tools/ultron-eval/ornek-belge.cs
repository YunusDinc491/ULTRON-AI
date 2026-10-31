// Ultron · Belge sorgulama denemeleri için ÖRNEK ders notu PDF'i üretir. İçerik tests/ultron-eval/belge/ornek-notlar.json'da
// (genel termodinamik bilgisi; kullanıcının gerçek notu değil). Değerlendirme aracı (belge-eval.cs) aynı içeriği kendisi üretir.
// Çalıştırma (proje kökünden): dotnet run tools/ultron-eval/ornek-belge.cs -- <çıktı.pdf>
// Türkçe harfler için Windows'un Arial yazı tipi gömülür; üretilen PDF repoya konmaz.
#:property PublishAot=false
#:package PdfPig@0.1.16

using System.Text.Json.Nodes;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

var source = JsonNode.Parse(File.ReadAllText(Path.Combine("tests", "ultron-eval", "belge", "ornek-notlar.json")))!;
var output = args.Length > 0 ? args[0] : source["dosya"]!.GetValue<string>();
var pages = source["sayfalar"]!.AsArray().Select(p => p!.AsArray().Select(l => l!.GetValue<string>()).ToArray()).ToArray();

var builder = new PdfDocumentBuilder();
var font = builder.AddTrueTypeFont(File.ReadAllBytes(@"C:\Windows\Fonts\arial.ttf"));
foreach (var lines in pages)
{
    var page = builder.AddPage(PageSize.A4);
    var y = 780.0;
    for (var i = 0; i < lines.Length; i++)
    {
        page.AddText(lines[i], i == 0 ? 15 : 11, new PdfPoint(50, y), font);
        y -= i == 0 ? 30 : 20;
    }
}

File.WriteAllBytes(output, builder.Build());
Console.WriteLine($"{output}: {pages.Length} sayfa");
