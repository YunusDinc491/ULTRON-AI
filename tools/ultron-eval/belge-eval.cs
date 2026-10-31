// Ultron · Belge sorgulama değerlendirmesi (orkestratör düzeyi): örnek ders notunu (tests/ultron-eval/belge/ornek-notlar.json → PDF)
// geçici veritabanına ekler, tests/ultron-eval/belge/vakalar.json'daki soruları GERÇEK akıştan (yönlendirici + belge araması +
// model + kaynak düzeltici) geçirir; doğru sayfa, içerik, dürüstlük, talimat enjeksiyonu ve yanlış yönlendirme otomatik kontrol edilir.
// Çalıştırma (proje kökünden, Ollama açıkken): dotnet run tools/ultron-eval/belge-eval.cs -- --etiket <ad> [--tekrar 2]
// Çıktı: tests/ultron-eval/sonuclar/<tarih>-belge-<etiket>.json + .md
#:property PublishAot=false
#:property TargetFramework=net10.0-windows10.0.19041.0
#:project ../../src/Ultron.Infrastructure/Ultron.Infrastructure.csproj
#:package Microsoft.Extensions.Configuration.Json@10.0.0

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Ultron.Core.Chat;
using Ultron.Core.Conversations;
using Ultron.Core.Documents;
using Ultron.Core.Text;
using Ultron.Infrastructure;

Console.OutputEncoding = Encoding.UTF8;
var inv = CultureInfo.InvariantCulture;
string Arg(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
var label = Arg("--etiket", "deneme");
var repeat = int.Parse(Arg("--tekrar", "1"), inv);

var root = Directory.GetCurrentDirectory();
var evalDir = Path.Combine(root, "tests", "ultron-eval");
var notes = JsonNode.Parse(File.ReadAllText(Path.Combine(evalDir, "belge", "ornek-notlar.json")))!;
var cases = JsonNode.Parse(File.ReadAllText(Path.Combine(evalDir, "belge", "vakalar.json")))!["vakalar"]!.AsArray().Select(n => n!.AsObject()).ToList();

// Geçici veritabanı ve belge klasörü: kullanıcının gerçek verisine dokunulmaz.
var temp = Path.Combine(Path.GetTempPath(), $"ultron-belge-eval-{Guid.NewGuid():N}");
Directory.CreateDirectory(temp);
var config = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(root, "src", "Ultron.Host", "appsettings.json"), optional: true)
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Database:Path"] = Path.Combine(temp, "eval.db"),
        ["Documents:Folder"] = Path.Combine(temp, "belgeler"),
        ["Chat:FollowUps"] = "false",
    })
    .Build();
var services = new ServiceCollection().AddLogging(b => b.SetMinimumLevel(LogLevel.Warning)).AddUltronInfrastructure(config).BuildServiceProvider();
await services.InitializeDatabaseAsync();
var orchestrator = services.GetRequiredService<ChatOrchestrator>();
var conversations = services.GetRequiredService<IConversationStore>();
var documents = services.GetRequiredService<DocumentService>();

var added = await documents.AddFileAsync(notes["dosya"]!.GetValue<string>(), BuildPdf(notes), notes["koleksiyon"]!.GetValue<string>());
Console.WriteLine($"Belge: {added.Message}");
if (added.Status != DocumentAddStatus.Added)
    return 1;

var total = Stopwatch.StartNew();
var results = new List<JsonObject>();
Console.WriteLine($"Belge değerlendirmesi · {cases.Count} vaka × {repeat} · etiket '{label}'");
foreach (var c in cases)
{
    var id = c["id"]!.GetValue<string>();
    var check = c["kontrol"]!.AsObject();
    var errors = new List<string>();
    var answers = new List<string>();
    var sw = Stopwatch.StartNew();
    for (var r = 0; r < repeat; r++)
    {
        try
        {
            var conversation = await conversations.StartAsync(); // her vaka temiz konuşma: önceki cevaplar karışmasın
            var events = new List<ChatEvent>();
            await foreach (var e in orchestrator.SendAsync(conversation.Id, c["girdi"]!.GetValue<string>()))
                events.Add(e);
            var answer = events.OfType<AnswerCompleted>().LastOrDefault()?.Text ?? "";
            answers.Add(answer);
            errors.AddRange(Check(check, events, answer).Select(e => repeat > 1 ? $"[{r + 1}] {e}" : e));
        }
        catch (Exception ex)
        {
            errors.Add("istisna: " + ex.Message);
        }
    }

    var passed = errors.Count == 0;
    Console.WriteLine($"{(passed ? "✓" : "✕")} {id,-12} {sw.ElapsedMilliseconds,6} ms  {(passed ? "" : string.Join("; ", errors))}");
    results.Add(new JsonObject
    {
        ["id"] = id, ["kategori"] = c["kategori"]!.GetValue<string>(), ["gecti"] = passed, ["sure_ms"] = sw.ElapsedMilliseconds,
        ["hatalar"] = new JsonArray([.. errors.Select(e => (JsonNode)e)]),
        ["cevap"] = string.Join(" ‖ ", answers.Select(a => a.Length > 300 ? a[..300] + "…" : a)),
    });
}
total.Stop();

var passCount = results.Count(r => r["gecti"]!.GetValue<bool>());
var byCategory = results.GroupBy(r => r["kategori"]!.GetValue<string>()).OrderBy(g => g.Key)
    .Select(g => $"{g.Key} {g.Count(r => r["gecti"]!.GetValue<bool>())}/{g.Count()}");
var summary = $"{passCount}/{results.Count} (%{Math.Round(100.0 * passCount / Math.Max(1, results.Count), 1).ToString(inv)}) · {Math.Round(total.Elapsed.TotalSeconds)} sn · {string.Join(", ", byCategory)}";
Console.WriteLine("Sonuç: " + summary);

var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm", inv);
var outDir = Path.Combine(evalDir, "sonuclar");
Directory.CreateDirectory(outDir);
var report = new JsonObject
{
    ["etiket"] = label, ["tarih"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", inv), ["git"] = Git(), ["tekrar"] = repeat,
    ["model"] = config["Ollama:ChatModel"], ["ozet"] = summary, ["vakalar"] = new JsonArray([.. results]),
};
File.WriteAllText(Path.Combine(outDir, $"{stamp}-belge-{label}.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
var md = new StringBuilder($"# Belge değerlendirmesi · {label}\n\n{summary}\n\n| Vaka | Sonuç | Hata |\n|---|---|---|\n");
foreach (var r in results)
    md.AppendLine($"| {r["id"]} | {(r["gecti"]!.GetValue<bool>() ? "✓" : "✕")} | {string.Join("; ", r["hatalar"]!.AsArray().Select(e => e!.GetValue<string>())).Replace("|", "/")} |");
File.WriteAllText(Path.Combine(outDir, $"{stamp}-belge-{label}.md"), md.ToString());

services.Dispose();
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
return 0;

static List<string> Check(JsonObject check, List<ChatEvent> events, string answer)
{
    var errors = new List<string>();
    var normalized = " " + TurkishText.Normalize(AnswerCleanup.PlainMath(answer)) + " "; // formül LaTeX gelebilir
    var cards = events.OfType<DocumentSourcesListed>().SelectMany(e => e.Sources).ToList();
    var searched = events.OfType<DocumentsSearched>().Any();

    if (check["sayfa"]?.GetValue<int>() is { } page)
    {
        var pages = cards.Select(s => s.Page).Distinct().ToList();
        if (!pages.Contains(page))
            errors.Add($"kartta s. {page} yok (kart: {(pages.Count == 0 ? "yok" : string.Join(",", pages.Select(p => p?.ToString() ?? "-")))})");
        else if (pages.Count > 1)
            errors.Add($"fazladan kart: s. {string.Join(",", pages.Where(p => p != page))}");
    }
    foreach (var phrase in check["icerir"]?.AsArray().Select(p => p!.GetValue<string>()) ?? [])
        if (!phrase.Split('|').Any(p => normalized.Contains(TurkishText.Normalize(p), StringComparison.Ordinal)))
            errors.Add($"'{phrase}' yok");
    foreach (var phrase in check["icermez"]?.AsArray().Select(p => p!.GetValue<string>()) ?? [])
        if (normalized.Contains(TurkishText.Normalize(phrase), StringComparison.Ordinal))
            errors.Add($"'{phrase}' var (olmamalı)");
    if (check["kart_yok"]?.GetValue<bool>() == true && cards.Count > 0)
        errors.Add($"kart olmamalı (s. {string.Join(",", cards.Select(s => s.Page))})");
    if (check["belge_yok"]?.GetValue<bool>() == true && searched)
        errors.Add("belge araması yapılmamalıydı");
    if (check["arac"]?.GetValue<string>() is { } tool
        && !events.OfType<ToolsUsed>().SelectMany(t => t.Uses).Any(u => u.Name == tool)
        && !normalized.Contains(" onem", StringComparison.Ordinal)) // hatırlatıcı önce önemi sorabilir: o da araç yolu
        errors.Add($"{tool} çalışmadı");
    return errors;
}

static byte[] BuildPdf(JsonNode notes)
{
    var builder = new PdfDocumentBuilder();
    var font = builder.AddTrueTypeFont(File.ReadAllBytes(@"C:\Windows\Fonts\arial.ttf"));
    foreach (var lines in notes["sayfalar"]!.AsArray().Select(p => p!.AsArray().Select(l => l!.GetValue<string>()).ToArray()))
    {
        var page = builder.AddPage(PageSize.A4);
        var y = 780.0;
        for (var i = 0; i < lines.Length; i++)
        {
            page.AddText(lines[i], i == 0 ? 15 : 11, new PdfPoint(50, y), font);
            y -= i == 0 ? 30 : 20;
        }
    }

    return builder.Build();
}

static string Git()
{
    try
    {
        using var p = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD") { RedirectStandardOutput = true, UseShellExecute = false })!;
        return p.StandardOutput.ReadToEnd().Trim();
    }
    catch (Exception) { return "?"; }
}
