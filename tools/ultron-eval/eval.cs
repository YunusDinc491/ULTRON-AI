// Ultron · Tekrarlanabilir değerlendirme: Ultron'un GERÇEK bileşenleriyle (sistem mesajı, araç planlayıcı, yönlendirici,
// hafıza çıkarıcı) dev/holdout setlerini koşturur; otomatik kontrollerle puanlar (model kendini notlamaz).
// Çalıştırma (proje kökünden, Ollama açıkken):
//   dotnet run tools/ultron-eval/eval.cs -- --set dev --etiket baseline
//   dotnet run tools/ultron-eval/eval.cs -- --set dev --etiket sicaklik-03 --ayar Ollama:Temperature=0.3
//   dotnet run tools/ultron-eval/eval.cs -- --set holdout --etiket baseline --baseline-yaz
// Çıktı: tests/ultron-eval/sonuclar/<tarih>-<set>-<etiket>.json + .md; --baseline-yaz ile tests/ultron-eval/baseline-<set>.json.
#:property PublishAot=false
#:property TargetFramework=net10.0-windows10.0.19041.0
#:project ../../src/Ultron.Infrastructure/Ultron.Infrastructure.csproj
#:package Microsoft.Extensions.Configuration.Json@10.0.0

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ultron.Core.Chat;
using Ultron.Core.Memories;
using Ultron.Core.Routing;
using Ultron.Core.Text;
using Ultron.Core.Tools;
using Ultron.Infrastructure;

Console.OutputEncoding = Encoding.UTF8;
var inv = CultureInfo.InvariantCulture;
string Arg(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
var set = Arg("--set", "dev");
var label = Arg("--etiket", "deneme");
var writeBaseline = args.Contains("--baseline-yaz");
var onlyCategory = Arg("--kategori", "");
var overrides = args.Select((a, i) => (a, i)).Where(x => x.a == "--ayar").Select(x => args[x.i + 1].Split('=', 2))
    .ToDictionary(p => p[0], p => (string?)p[1]);

var root = Directory.GetCurrentDirectory();
var evalDir = Path.Combine(root, "tests", "ultron-eval");
var cases = new List<JsonObject>();
foreach (var s in set == "all" ? new[] { "dev", "holdout" } : [set])
    cases.AddRange(JsonNode.Parse(File.ReadAllText(Path.Combine(evalDir, s + ".json")))!["vakalar"]!.AsArray().Select(n => n!.AsObject())
        .Where(c => onlyCategory.Length == 0 || c["kategori"]!.GetValue<string>() == onlyCategory));

// Geçici veritabanı: değerlendirme kullanıcının gerçek verisine dokunmaz.
var db = Path.Combine(Path.GetTempPath(), $"ultron-eval-{Guid.NewGuid():N}.db");
var config = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(root, "src", "Ultron.Host", "appsettings.json"), optional: true)
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Path"] = db })
    .AddInMemoryCollection(overrides).Build();
var services = new ServiceCollection().AddLogging(b => b.SetMinimumLevel(LogLevel.Warning)).AddUltronInfrastructure(config).BuildServiceProvider();
await services.InitializeDatabaseAsync();
var chat = services.GetRequiredService<IChatModel>();
var router = services.GetRequiredService<IntentRouter>();
var extractor = services.GetRequiredService<MemoryExtractor>();
var tools = services.GetServices<ITool>().Select(t => t.Definition).ToList();
var now = new DateTime(2026, 10, 27, 10, 0, 0); // sabit "şimdi": sonuçlar tarihten bağımsız tekrarlanabilir

var vramBefore = Vram();
var vramMax = vramBefore;
var total = Stopwatch.StartNew();
var results = new List<JsonObject>();
Console.WriteLine($"Değerlendirme: {set} · {cases.Count} vaka · etiket '{label}'{(overrides.Count > 0 ? " · " + string.Join(", ", overrides.Select(o => $"{o.Key}={o.Value}")) : "")}");

foreach (var c in cases)
{
    var id = c["id"]!.GetValue<string>();
    var kind = c["tur"]!.GetValue<string>();
    var input = c["girdi"]!.GetValue<string>();
    var check = c["kontrol"]?.AsObject() ?? [];
    var repeat = c["tekrar"]?.GetValue<int>() ?? 1;
    var errors = new List<string>();
    var answers = new List<string>();
    var sw = Stopwatch.StartNew();
    try
    {
        for (var r = 0; r < repeat; r++)
        {
            var (answer, errs) = kind switch
            {
                "sohbet" => await ChatCase(input, check),
                "arac" => await ToolCase(input, c),
                "router" => await RouterCase(input, c),
                "cikarma" => await ExtractCase(input, c, check),
                _ => ("", new List<string> { "bilinmeyen tür " + kind }),
            };
            answers.Add(answer);
            errors.AddRange(errs.Select(e => repeat > 1 ? $"[{r + 1}] {e}" : e));
        }
    }
    catch (Exception ex)
    {
        errors.Add("istisna: " + ex.Message);
    }
    vramMax = Math.Max(vramMax, Vram());
    var passed = errors.Count == 0;
    Console.WriteLine($"{(passed ? "✓" : "✕")} {id,-10} {sw.ElapsedMilliseconds,6} ms  {(passed ? "" : string.Join("; ", errors))}");
    results.Add(new JsonObject
    {
        ["id"] = id, ["kategori"] = c["kategori"]!.GetValue<string>(), ["gecti"] = passed, ["sure_ms"] = sw.ElapsedMilliseconds,
        ["hatalar"] = new JsonArray([.. errors.Select(e => (JsonNode)e)]),
        ["cevap"] = Short(string.Join(" ‖ ", answers)),
    });
}
total.Stop();

var byCategory = results.GroupBy(r => r["kategori"]!.GetValue<string>()).OrderBy(g => g.Key)
    .ToDictionary(g => g.Key, g => (Pass: g.Count(r => r["gecti"]!.GetValue<bool>()), Total: g.Count()));
var passCount = results.Count(r => r["gecti"]!.GetValue<bool>());
var report = new JsonObject
{
    ["etiket"] = label, ["set"] = set, ["tarih"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", inv), ["git"] = Git(),
    ["model"] = config["Ollama:ChatModel"] ?? "gemma4:e4b-it-qat",
    ["ayarlar"] = new JsonObject(overrides.Select(o => KeyValuePair.Create(o.Key, (JsonNode?)o.Value))),
    ["toplam_sure_sn"] = Math.Round(total.Elapsed.TotalSeconds, 1),
    ["ortalama_vaka_ms"] = results.Count == 0 ? 0 : (long)results.Average(r => r["sure_ms"]!.GetValue<long>()),
    ["vram_mb"] = new JsonObject { ["once"] = vramBefore, ["en_yuksek"] = vramMax },
    ["puan"] = new JsonObject { ["gecen"] = passCount, ["toplam"] = results.Count, ["oran"] = Math.Round(100.0 * passCount / Math.Max(1, results.Count), 1) },
    ["kategoriler"] = new JsonObject(byCategory.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)new JsonObject
        { ["gecen"] = kv.Value.Pass, ["toplam"] = kv.Value.Total, ["oran"] = Math.Round(100.0 * kv.Value.Pass / kv.Value.Total, 1) }))),
    ["vakalar"] = new JsonArray([.. results]),
};

var outDir = Path.Combine(evalDir, "sonuclar");
Directory.CreateDirectory(outDir);
var stem = Path.Combine(outDir, $"{DateTime.Now:yyyyMMdd-HHmm}-{set}-{label}");
var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
File.WriteAllText(stem + ".json", report.ToJsonString(options));

var baselinePath = Path.Combine(evalDir, $"baseline-{set}.json");
var baseline = File.Exists(baselinePath) ? JsonNode.Parse(File.ReadAllText(baselinePath)) : null;
File.WriteAllText(stem + ".md", Markdown(report, baseline));
if (writeBaseline)
    File.WriteAllText(baselinePath, report.ToJsonString(options));
Console.WriteLine($"\nPuan: {passCount}/{results.Count} (%{report["puan"]!["oran"]}) · {report["toplam_sure_sn"]} sn · VRAM en yüksek {vramMax} MB");
foreach (var (k, v) in byCategory)
    Console.WriteLine($"  {k,-16} {v.Pass}/{v.Total}");
Console.WriteLine("Rapor: " + stem + ".md" + (writeBaseline ? " · baseline yazıldı" : ""));

await services.DisposeAsync();
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(db) + "*"))
    File.Delete(f);

// ---------------- vaka türleri (Ultron'un orkestratörüyle aynı adımlar) ----------------

async Task<(string, List<string>)> ChatCase(string input, JsonObject check)
{
    var notes = AnswerStyle.NotesFor(input).ToList();
    var messages = new List<ChatMessage> { new(Role.System, SystemPrompt.Build(now, null, notes)), new(Role.User, input) };
    var sb = new StringBuilder();
    await foreach (var part in chat.StreamAsync(messages))
        sb.Append(part);
    var answer = sb.ToString().Trim();
    return (answer, Check(answer, check));
}

async Task<(string, List<string>)> ToolCase(string input, JsonObject c)
{
    var expected = c["beklenen_arac"]!.GetValue<string>();
    var intent = await router.ClassifyAsync(input);
    var toolSet = ToolRouting.Select(intent, input);
    if (toolSet is null)
        return ($"(araç yolu yok · {intent})", expected == "yok" ? [] : [$"araç yoluna girmedi (niyet {intent}), beklenen {expected}"]);
    var offered = toolSet.Count == 0 ? tools : tools.Where(t => toolSet.Contains(t.Name)).ToList();
    var messages = new List<ChatMessage> { new(Role.System, ToolRouting.SystemPrompt(now, tools: toolSet)), new(Role.User, input) };
    var turn = await chat.CompleteWithToolsAsync(messages, offered);
    var call = turn.Calls.FirstOrDefault();
    var shown = call is null ? $"yok: {turn.Content}" : $"{call.Name}({call.Arguments.GetRawText()})";
    if (expected == "yok")
        return (shown, call is null ? [] : [$"araç seçilmemeliydi, seçilen {call.Name}"]);
    if (call is null)
        return (shown, [$"araç seçilmedi, beklenen {expected}"]);
    var errs = new List<string>();
    if (call.Name != expected)
        errs.Add($"araç {call.Name}, beklenen {expected}");
    foreach (var (key, value) in c["parametreler"]?.AsObject() ?? [])
    {
        var got = ToolArgs.String(call.Arguments, key);
        if (got is null || Norm(got) != Norm(value!.ToString()))
            errs.Add($"{key}={got ?? "∅"}, beklenen {value}");
    }
    return (shown, errs);
}

async Task<(string, List<string>)> RouterCase(string input, JsonObject c)
{
    var intent = (await router.ClassifyAsync(input)).ToString();
    var expected = c["beklenen_niyet"]!.GetValue<string>();
    return (intent, intent == expected ? [] : [$"niyet {intent}, beklenen {expected}"]);
}

async Task<(string, List<string>)> ExtractCase(string input, JsonObject c, JsonObject check)
{
    var result = await extractor.ExtractAsync(input);
    var shown = string.Join(" | ", result.Facts.Select(f => $"[{f.Drawer} › {f.New?.Name ?? f.Category}] {f.Content}"));
    if (check["bos"]?.GetValue<bool>() == true)
        return (shown, result.Facts.Count == 0 ? [] : ["bilgi çıkarılmamalıydı"]);
    var errs = new List<string>();
    if (result.Facts.Count == 0)
        return (shown, ["hiç bilgi çıkarılmadı"]);
    // Çekmeceler (2026-10-28): klasör adlarını Ultron açtığı için sadece çekmece (ve yerleşik Sağlık) kontrol edilir.
    if (c["beklenen_cekmece"]?.GetValue<string>() is { } drawer && result.Facts.All(f => f.Drawer != drawer))
        errs.Add($"çekmece {string.Join(",", result.Facts.Select(f => f.Drawer))}, beklenen {drawer}");
    if (c["beklenen_kategori"]?.GetValue<string>() is { } cat && result.Facts.All(f => f.Category != cat))
        errs.Add($"kategori {string.Join(",", result.Facts.Select(f => f.Category))}, beklenen {cat}");
    errs.AddRange(Check(string.Join(" ", result.Facts.Select(f => f.Content)), check));
    return (shown, errs);
}

// ---------------- otomatik kontroller ----------------

List<string> Check(string answer, JsonObject check)
{
    var errs = new List<string>();
    var norm = Norm(answer);
    foreach (var (key, value) in check)
    {
        switch (key)
        {
            case "sayi":
                if (!Numbers(answer).Any(n => Math.Abs(n - value!.GetValue<double>()) < 1e-9))
                    errs.Add($"sayı {value} yok");
                break;
            case "sayi_yaklasik":
                var tol = check["tolerans"]?.GetValue<double>() ?? 0.01;
                if (!Numbers(answer).Any(n => Math.Abs(n - value!.GetValue<double>()) <= tol))
                    errs.Add($"≈{value} yok");
                break;
            case "tolerans":
            case "bos":
                break;
            case "icermeli_herhangi":
                if (!value!.AsArray().Any(v => norm.Contains(Norm(v!.GetValue<string>()), StringComparison.Ordinal)))
                    errs.Add("beklenen ifadelerden hiçbiri yok");
                break;
            case "icermeli_hepsi_kok":
                // "ışınım|radyasyon": eş anlamlılardan biri yeterli.
                foreach (var v in value!.AsArray().Select(v => v!.GetValue<string>())
                             .Where(v => !v.Split('|').Any(alt => norm.Contains(Norm(alt), StringComparison.Ordinal))))
                    errs.Add($"'{v}' yok");
                break;
            case "icermemeli":
                foreach (var v in value!.AsArray().Select(v => v!.GetValue<string>()).Where(v => Regex.IsMatch(norm, $@"\b{Regex.Escape(Norm(v))}\b")))
                    errs.Add($"'{v}' olmamalıydı");
                break;
            case "en_fazla_cumle":
                if (Sentences(answer) > value!.GetValue<int>())
                    errs.Add($"{Sentences(answer)} cümle > {value}");
                break;
            case "en_fazla_kelime":
                if (Words(answer) > value!.GetValue<int>())
                    errs.Add($"{Words(answer)} kelime > {value}");
                break;
            case "en_az_kelime":
                if (Words(answer) < value!.GetValue<int>())
                    errs.Add($"{Words(answer)} kelime < {value}");
                break;
            case "liste_maddesi":
                if (ListItems(answer) != value!.GetValue<int>())
                    errs.Add($"{ListItems(answer)} madde ≠ {value}");
                break;
            case "liste_maddesi_en_az":
                if (ListItems(answer) < value!.GetValue<int>())
                    errs.Add($"{ListItems(answer)} madde < {value}");
                break;
            case "markdownsuz":
                if (Regex.IsMatch(answer, @"(?m)^\s*([-*•]\s|\d+[.)]\s|#{1,6}\s|\|)|\*\*"))
                    errs.Add("markdown/liste kullanılmış");
                break;
            case "dil":
                var turkish = Regex.IsMatch(answer, "[çğıöşüÇĞİÖŞÜ]") || Regex.IsMatch(" " + norm + " ", " (ve|bir|bu|icin|ile|da|de) ");
                if (value!.GetValue<string>() == "en" && Regex.IsMatch(answer, "[çğışöüÇĞİŞÖÜ]"))
                    errs.Add("İngilizce istendi, Türkçe karakter var");
                if (value.GetValue<string>() == "tr" && !turkish)
                    errs.Add("Türkçe görünmüyor");
                break;
            default:
                errs.Add("bilinmeyen kontrol " + key);
                break;
        }
    }
    return errs;
}

static string Norm(string s) => TurkishText.Normalize(s);

static int Words(string s) => Regex.Matches(s, @"[\p{L}\p{N}]+").Count;

static int Sentences(string s) =>
    Regex.Split(Regex.Replace(s.Trim(), @"\d+[.,]\d+", "0"), @"(?<=[.!?…])\s+|\n+").Count(x => Regex.IsMatch(x, @"\p{L}"));

static int ListItems(string s) => Regex.Matches(s, @"(?m)^\s*([-*•+]\s|\d{1,2}[.)]\s)").Count;

/// <summary>"300.000" → 300000, "12,57" → 12.57, "%25" → 25; yazıyla sayılar da ("Bir elması kaldı", "Sıfır Kelvin").</summary>
static IEnumerable<double> Numbers(string s)
{
    // Yazıyla sayılar: "on iki" → 12, "yüz altmış sekiz" → 168 (ardışık sayı kelimeleri toplanır).
    var units = new Dictionary<string, int> { ["sifir"] = 0, ["bir"] = 1, ["iki"] = 2, ["uc"] = 3, ["dort"] = 4, ["bes"] = 5, ["alti"] = 6, ["yedi"] = 7, ["sekiz"] = 8, ["dokuz"] = 9,
        ["on"] = 10, ["yirmi"] = 20, ["otuz"] = 30, ["kirk"] = 40, ["elli"] = 50, ["altmis"] = 60, ["yetmis"] = 70, ["seksen"] = 80, ["doksan"] = 90 };
    int? acc = null;
    foreach (var w in Norm(s).Split(' ').Append(""))
    {
        if (units.TryGetValue(w, out var u))
            acc = (acc ?? 0) + u;
        else if (w == "yuz")
            acc = (acc is null or 0 ? 1 : acc.Value) * 100;
        else if (acc is { } done)
        {
            yield return done;
            acc = null;
        }
    }
    foreach (Match m in Regex.Matches(s, @"-?\d[\d.,]*"))
    {
        var t = m.Value.TrimEnd('.', ',');
        if (Regex.IsMatch(t, @"^-?\d{1,3}(\.\d{3})+$"))
            t = t.Replace(".", "");
        t = t.Replace(',', '.');
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            yield return v;
    }
}

static string Short(string s) => s.Length > 400 ? s[..400] + "…" : s;

static long Vram()
{
    try
    {
        using var p = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=memory.used --format=csv,noheader,nounits") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        return long.Parse(p.StandardOutput.ReadToEnd().Trim().Split('\n')[0], CultureInfo.InvariantCulture);
    }
    catch
    {
        return -1;
    }
}

static string Git()
{
    try
    {
        using var p = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        return p.StandardOutput.ReadToEnd().Trim();
    }
    catch
    {
        return "?";
    }
}

static string Markdown(JsonObject r, JsonNode? baseline)
{
    var sb = new StringBuilder();
    sb.AppendLine($"# Ultron değerlendirmesi · {r["set"]} · {r["etiket"]}");
    sb.AppendLine($"{r["tarih"]} · git {r["git"]} · {r["model"]} · ayarlar: {(r["ayarlar"]!.AsObject().Count == 0 ? "varsayılan" : r["ayarlar"]!.ToJsonString())}\n");
    sb.AppendLine($"**Puan: {r["puan"]!["gecen"]}/{r["puan"]!["toplam"]} (%{r["puan"]!["oran"]})**{Delta(r["puan"]!["oran"], baseline?["puan"]?["oran"])} · süre {r["toplam_sure_sn"]} sn · ortalama vaka {r["ortalama_vaka_ms"]} ms · VRAM {r["vram_mb"]!["once"]} → en yüksek {r["vram_mb"]!["en_yuksek"]} MB\n");
    sb.AppendLine("| Kategori | Geçen | Oran | Baseline'a göre |\n|---|---|---|---|");
    foreach (var (k, v) in r["kategoriler"]!.AsObject())
        sb.AppendLine($"| {k} | {v!["gecen"]}/{v["toplam"]} | %{v["oran"]} | {Delta(v["oran"], baseline?["kategoriler"]?[k]?["oran"]).Trim()} |");
    sb.AppendLine("\n## Başarısız vakalar\n");
    foreach (var c in r["vakalar"]!.AsArray().Where(c => !c!["gecti"]!.GetValue<bool>()))
        sb.AppendLine($"- **{c!["id"]}**: {string.Join("; ", c["hatalar"]!.AsArray().Select(e => e!.ToString()))} — cevap: `{c["cevap"]!.ToString().Replace("\n", " ").Replace("`", "'")}`");
    return sb.ToString();
}

static string Delta(JsonNode? now, JsonNode? before)
{
    if (before is null || now is null)
        return "";
    var d = now.GetValue<double>() - before.GetValue<double>();
    return Math.Abs(d) < 0.05 ? " (=)" : $" ({(d > 0 ? "+" : "")}{d:0.#} puan)";
}
