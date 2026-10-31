#:property PublishAot=false
#:project ../../src/Ultron.Core/Ultron.Core.csproj

// Bileşen galerisi (Faz 16): tarif defterini ya da ölçüm sonucunu tarayıcıda görülebilir sayfalara çevirir.
// Her bileşen iki çerçevede: geniş (masaüstü) ve 375 px (telefon); telefon çerçevesi Ultron'un önizlemesindeki kontrolü
// (hata, taşma, erişilebilirlik) çalıştırır, sonuç rozet olarak görünür. Kütüphaneler masaüstü uygulamasının kopyası.
// Kullanım: dotnet run tools/ultron-is/galeri.cs -- --tarifler src/Ultron.Host/notlar/bilesenler --cikti <klasör>
//           dotnet run tools/ultron-is/galeri.cs -- --sonuc tests/ultron-is/sonuclar/<x>.json --cikti <klasör>
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ultron.Core.Studio;

Console.OutputEncoding = Encoding.UTF8;
string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
var output = Path.GetFullPath(Arg("--cikti") ?? Path.Combine("tests", "ultron-is", "galeri"));
var items = new List<(string Id, string Title, string Meta, string Code)>();

if (Arg("--tarifler") is { } folder)
{
    foreach (var file in Directory.EnumerateFiles(folder, "*.md").Order())
    {
        var slug = Path.GetFileNameWithoutExtension(file);
        if (StudioCode.ParseRecipe(slug, File.ReadAllText(file)) is { } r)
            items.Add((slug, r.Title, r.Tags, r.Code));
        else
            Console.WriteLine("Okunamadı: " + file);
    }
}
else if (Arg("--sonuc") is { } result)
{
    foreach (var node in JsonNode.Parse(File.ReadAllText(result))!["sonuclar"]!.AsArray())
    {
        var code = node!["kod"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(code))
            continue;
        items.Add((node["id"]!.GetValue<string>(), node["istek"]!.GetValue<string>(),
            $"{node["sure_sn"]} sn · {node["kaynak"]} · tarif: {node["tarifler"]}", code));
    }
}
else
{
    Console.WriteLine("--tarifler <klasör> ya da --sonuc <json> gerekli");
    return;
}

var root = Directory.GetCurrentDirectory();
Directory.CreateDirectory(Path.Combine(output, "lib"));
Directory.CreateDirectory(Path.Combine(output, "b"));
var preview = Path.Combine(root, "src", "Ultron.Desktop", "wwwroot");
string[] libs = ["lib/preview/react.production.min.js", "lib/preview/react-dom.production.min.js", "lib/preview/babel.min.js", "lib/preview/tailwind.js", "js/preview-harness.js"];
foreach (var lib in libs)
    File.Copy(Path.Combine(preview, lib), Path.Combine(output, "lib", Path.GetFileName(lib)), overwrite: true);

var stamp = DateTime.Now.Ticks; // tarayıcı önbelleği eski kütüphaneyi kullanmasın
foreach (var item in items)
{
    var page = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
    foreach (var lib in libs.Take(4))
        page.Append($"<script src=\"../lib/{Path.GetFileName(lib)}?v={stamp}\"></script>");
    page.Append("<style>html,body{margin:0;font-family:ui-sans-serif,system-ui,'Segoe UI',sans-serif;background:#fff}</style></head><body><div id=\"root\"></div>");
    page.Append($"<script src=\"../lib/preview-harness.js?v={stamp}\"></script><script>");
    page.Append("window.postMessage({__ultron:true,type:'render',seq:1,final:true,check:true,code:")
        .Append(JsonSerializer.Serialize(item.Code).Replace("</", "<\\/")).Append("},'*');</script></body></html>");
    File.WriteAllText(Path.Combine(output, "b", item.Id + ".html"), page.ToString());
}

var index = new StringBuilder("""
    <!doctype html><html lang="tr"><head><meta charset="utf-8"><title>Ultron bileşen galerisi</title><style>
    body{margin:0;background:#262624;color:#f5f4ed;font:14px ui-sans-serif,system-ui,'Segoe UI',sans-serif}
    header{padding:20px 24px;border-bottom:1px solid #3d3d3a} h1{font:400 24px Georgia,serif;margin:0}
    .sum{color:#a6a39b;margin-top:6px} .item{padding:18px 24px;border-bottom:1px solid #3d3d3a}
    .head{display:flex;gap:10px;align-items:baseline;flex-wrap:wrap} .id{color:#e8906f;font-family:ui-monospace,monospace}
    .meta{color:#a6a39b;font-size:12px} .badge{font-size:12px;padding:2px 8px;border-radius:999px;background:#30302e}
    .ok{color:#7fbf8e} .bad{color:#e0b15a} .row{display:flex;gap:14px;margin-top:10px;align-items:flex-start}
    iframe{border:0;background:#fff;border-radius:10px} .wide{flex:1;height:440px;min-width:0} .narrow{width:375px;height:440px;flex:none}
    ul{margin:6px 0 0;color:#e0b15a;font-size:12px}
    </style></head><body><header><h1>Ultron bileşen galerisi</h1><div class="sum" id="sum">Kontroller çalışıyor…</div></header>
    """);
foreach (var item in items)
{
    index.Append($"<section class=\"item\" data-id=\"{item.Id}\"><div class=\"head\"><span class=\"id\">{WebUtility.HtmlEncode(item.Id)}</span><b>{WebUtility.HtmlEncode(item.Title)}</b>")
        .Append($"<span class=\"badge\" id=\"c-{item.Id}\">kontrol…</span></div><div class=\"meta\">{WebUtility.HtmlEncode(item.Meta)}</div>")
        .Append($"<div class=\"row\"><iframe class=\"wide\" title=\"{WebUtility.HtmlEncode(item.Title)} geniş\" src=\"b/{item.Id}.html?v={stamp}\"></iframe>")
        .Append($"<iframe class=\"narrow\" data-check=\"{item.Id}\" title=\"{WebUtility.HtmlEncode(item.Title)} telefon\" src=\"b/{item.Id}.html?v={stamp}\"></iframe></div><ul id=\"p-{item.Id}\"></ul></section>");
}
index.Append("""
    <script>
    const results = {};
    window.addEventListener('message', e => {
      const m = e.data; if (!m || !m.__ultron) return;
      if (m.type === 'compile-error') m.problems = ['Derleme hatası: ' + m.message];
      else if (m.type !== 'checked') return;
      const frame = [...document.querySelectorAll('iframe[data-check]')].find(f => f.contentWindow === e.source); if (!frame) return;
      const id = frame.dataset.check; results[id] = m.problems;
      const badge = document.getElementById('c-' + id);
      badge.textContent = m.problems.length ? '⚠ ' + m.problems.length + ' sorun' : '✓ kontrol temiz';
      badge.className = 'badge ' + (m.problems.length ? 'bad' : 'ok');
      document.getElementById('p-' + id).innerHTML = m.problems.map(p => '<li>' + p.replace(/</g, '&lt;') + '</li>').join('');
      const done = Object.keys(results).length, total = document.querySelectorAll('iframe[data-check]').length;
      const clean = Object.values(results).filter(p => p.length === 0).length;
      document.getElementById('sum').textContent = `${total} bileşen · kontrol edilen ${done} · temiz ${clean}`;
      window.__galeri = { total, done, clean, results };
    });
    </script></body></html>
    """);
File.WriteAllText(Path.Combine(output, "index.html"), index.ToString());
Console.WriteLine($"{items.Count} bileşen → {Path.Combine(output, "index.html")}");
