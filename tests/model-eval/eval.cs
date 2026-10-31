// Ultron · Faz 1 · Model değerlendirme betiği
// .NET 10 "file-based app": proje dosyası gerekmez.
// Çalıştırma (bu klasörden):  dotnet run eval.cs
// Sadece bazı modeller:        dotnet run eval.cs -- qwen3-vl:8b gemma4:e4b-it-qat
// Sadece embedding testi:      dotnet run eval.cs -- --sadece-embedding
// Sadece bir kategori:         dotnet run eval.cs -- --kategori gorsel
// Sonuçlar: sonuclar/<tarih-saat>/rapor.md ve ham.json
#:property PublishAot=false

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var tr = new CultureInfo("tr-TR");
CultureInfo.CurrentCulture = tr;
Console.OutputEncoding = Encoding.UTF8;

var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://127.0.0.1:11434";
var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

var dir = FindDir();
var cfg = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "sorular.json")))!.AsObject();

var argList = args.ToList();
var onlyEmbedding = argList.Remove("--sadece-embedding");
string? onlyKind = null; // --kategori gorsel → sadece o kategori, embedding testi atlanır
var kindIdx = argList.IndexOf("--kategori");
if (kindIdx >= 0) { onlyKind = argList[kindIdx + 1]; argList.RemoveRange(kindIdx, 2); }
var models = argList.Count > 0 ? argList : cfg["modeller"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
if (onlyEmbedding) models.Clear();

var categories = cfg["kategoriler"]!.AsArray();
var routerClasses = cfg["router_siniflari"]!.AsArray();
var prompts = cfg["sistem_promptlari"]!.AsObject();
var tools = cfg["araclar"]!.AsArray();
var tests = cfg["testler"]!.AsArray().Where(t => onlyKind is null || t!["kategori"]!.GetValue<string>() == onlyKind).ToList();
var ctxTrials = cfg["ctx_denemeleri"]!.AsArray().Select(x => x!.GetValue<int>()).ToList();
var testCtx = cfg["test_ctx"]!.GetValue<int>();
var now = DateTime.Now.ToString("dd MMMM yyyy dddd, HH:mm", tr);

string Prompt(string key) => prompts[key]!.GetValue<string>()
    .Replace("{tarih}", now)
    .Replace("{kategoriler}", string.Join(", ", categories.Select(c => c!.GetValue<string>())));

// Ollama'nın "structured output" özelliği: format alanına JSON Schema verilince model bu şemaya uymak zorunda kalır.
var extractionSchema = JsonNode.Parse($$"""
{ "type": "object",
  "properties": { "bilgiler": { "type": "array", "items": { "type": "object",
      "properties": { "kategori": { "type": "string", "enum": {{categories.ToJsonString()}} },
                      "bilgi": { "type": "string" } },
      "required": ["kategori", "bilgi"] } } },
  "required": ["bilgiler"] }
""")!;
var routerSchema = JsonNode.Parse($$"""
{ "type": "object", "properties": { "sinif": { "type": "string", "enum": {{routerClasses.ToJsonString()}} } }, "required": ["sinif"] }
""")!;

var outDir = Path.Combine(dir, "sonuclar", DateTime.Now.ToString("yyyyMMdd-HHmm"));
Directory.CreateDirectory(outDir);

var results = new List<TestResult>();
var modelSummaries = new List<ModelSummary>();

foreach (var model in models)
{
    Console.WriteLine($"\n=== {model} ===");
    await UnloadAllAsync();

    // 1) Isınma: ilk istek modeli diskten VRAM'e yükler, yükleme süresini ölçeriz.
    var warm = await ChatAsync(model, Msgs(null, "Merhaba"), numCtx: testCtx);
    Console.WriteLine($"  yükleme: {warm.LoadNs / 1e9:F1} sn");

    // 2) Bağlam uzunluğu (num_ctx) arttıkça VRAM nasıl değişiyor?
    var vram = new List<VramRow>();
    foreach (var ctx in ctxTrials)
    {
        await ChatAsync(model, Msgs(null, "Tek kelimeyle cevap ver: tamam"), numCtx: ctx);
        var (size, sizeVram) = await PsAsync(model);
        vram.Add(new VramRow(ctx, size, sizeVram));
        Console.WriteLine($"  num_ctx={ctx}: toplam {Gb(size)} GB, GPU'da {Gb(sizeVram)} GB (%{Pct(sizeVram, size)})");
    }

    // 3) Testler (sabit bağlam uzunluğunda)
    foreach (var t in tests.Select(x => x!.AsObject()))
    {
        Console.Write($"  … {t["id"]} ");
        var r = await RunTestAsync(model, t);
        Console.Write("\r");
        results.Add(r);
        var mark = r.Passed switch { true => "✅", false => "❌", null => "📝" };
        Console.WriteLine($"  {mark} {r.Id} ({r.TokPerSec:F1} tok/s) {r.Note}");
    }

    modelSummaries.Add(new ModelSummary(model, warm.LoadNs / 1e9, vram));
}
await UnloadAllAsync();

// 4) Embedding testi: hafıza kayıtları arasından doğru olanı bulabiliyor mu?
var embedRows = new List<EmbedRow>();
var embed = cfg["embedding"]!.AsObject();
var records = embed["kayitlar"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
var queries = embed["sorgular"]!.AsArray().Select(x => x!.AsObject()).ToList();
foreach (var em in onlyKind is null ? cfg["embedding_modelleri"]!.AsArray().Select(x => x!.GetValue<string>()) : Enumerable.Empty<string>())
{
    Console.WriteLine($"\n=== embedding: {em} ===");
    foreach (var cpu in new[] { false, true })
    {
        await UnloadAllAsync();
        await EmbedAsync(em, ["ısınma"], cpu); // yükleme süresini ölçüme katmamak için
        var sw = Stopwatch.StartNew();
        var recVecs = await EmbedAsync(em, records, cpu);
        var correct = 0;
        foreach (var q in queries)
        {
            var qv = (await EmbedAsync(em, [q["soru"]!.GetValue<string>()], cpu))[0];
            var best = Enumerable.Range(0, recVecs.Count).MaxBy(i => Cosine(qv, recVecs[i]));
            if (best == q["dogru"]!.GetValue<int>()) correct++;
        }
        sw.Stop();
        var (size, sizeVram) = await PsAsync(em);
        var row = new EmbedRow(em, cpu ? "CPU" : "GPU", correct, queries.Count, sw.Elapsed.TotalMilliseconds, recVecs[0].Length, size, sizeVram);
        embedRows.Add(row);
        Console.WriteLine($"  {row.Device}: doğru {correct}/{queries.Count}, süre {row.Ms:F0} ms, boyut {row.Dim}");
    }
}
await UnloadAllAsync();

WriteReport();
Console.WriteLine($"\nRapor: {Path.Combine(outDir, "rapor.md")}");

// ---------------------------------------------------------------------------------

async Task<TestResult> RunTestAsync(string model, JsonObject t)
{
    var id = t["id"]!.GetValue<string>();
    var kind = t["kategori"]!.GetValue<string>();
    var msg = t["mesaj"]!.GetValue<string>();
    try
    {
        switch (kind)
        {
            case "sohbet":
            {
                var r = await ChatAsync(model, Msgs(Prompt("sohbet"), msg), numCtx: testCtx);
                return new(model, id, kind, msg, r.Content, null, "elle puanlanacak", r.TokPerSec, r.TotalSec);
            }
            case "cikarma":
            {
                var r = await ChatAsync(model, Msgs(Prompt("cikarma"), msg), format: extractionSchema, numCtx: testCtx);
                if (r.DoneReason == "length")
                    return new(model, id, kind, msg, r.Content, false, "uzunluk sınırına takıldı (JSON döngüsü)", r.TokPerSec, r.TotalSec);
                var got = JsonNode.Parse(r.Content)!["bilgiler"]!.AsArray()
                    .Select(b => b!["kategori"]!.GetValue<string>()).ToHashSet();
                bool ok;
                if (t["bos_olmali"]?.GetValue<bool>() == true) ok = got.Count == 0;
                else
                {
                    var required = t["gerekli"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet();
                    var allowed = t["izinli"]!.AsArray().Select(x => x!.GetValue<string>()).Concat(required).ToHashSet();
                    ok = got.Count > 0 && required.IsSubsetOf(got) && got.IsSubsetOf(allowed);
                }
                return new(model, id, kind, msg, r.Content, ok, $"kategoriler: [{string.Join(", ", got)}]", r.TokPerSec, r.TotalSec);
            }
            case "router":
            {
                var r = await ChatAsync(model, Msgs(Prompt("router"), msg), format: routerSchema, numCtx: testCtx);
                if (r.DoneReason == "length")
                    return new(model, id, kind, msg, r.Content, false, "uzunluk sınırına takıldı (JSON döngüsü)", r.TokPerSec, r.TotalSec);
                var cls = JsonNode.Parse(r.Content)!["sinif"]!.GetValue<string>();
                var expected = t["beklenen"]!.GetValue<string>();
                return new(model, id, kind, msg, r.Content, cls == expected, $"{cls} (beklenen {expected})", r.TokPerSec, r.TotalSec);
            }
            case "arac":
            {
                var r = await ChatAsync(model, Msgs(Prompt("arac"), msg), tools: tools, numCtx: testCtx);
                var expected = t["beklenen_arac"]?.GetValue<string>();
                var call = r.ToolCalls?.FirstOrDefault()?["function"];
                var name = call?["name"]?.GetValue<string>();
                var argsNode = call?["arguments"];
                bool ok;
                if (expected is null) ok = call is null;
                else
                {
                    var needed = t["gerekli_argumanlar"]!.AsArray().Select(x => x!.GetValue<string>());
                    ok = name == expected && needed.All(a => !string.IsNullOrWhiteSpace(argsNode?[a]?.ToString()));
                }
                var answer = call is null ? r.Content : $"{name}({argsNode?.ToJsonString()})";
                return new(model, id, kind, msg, answer, ok, $"araç: {name ?? "yok"} (beklenen {expected ?? "yok"})", r.TokPerSec, r.TotalSec);
            }
            case "gorsel":
            {
                var path = Path.Combine(dir, t["dosya"]!.GetValue<string>());
                if (!File.Exists(path))
                    return new(model, id, kind, msg, "", null, $"atlandı: {t["dosya"]} yok", 0, 0);
                var image = Convert.ToBase64String(File.ReadAllBytes(path));
                var r = await ChatAsync(model, Msgs(null, msg, image), numCtx: testCtx);
                var keywords = t["anahtar_kelimeler"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
                bool? ok = keywords.Count == 0 ? null
                    : keywords.All(k => r.Content.Contains(k, StringComparison.CurrentCultureIgnoreCase));
                return new(model, id, kind, msg, r.Content, ok, keywords.Count == 0 ? "elle puanlanacak" : "anahtar kelime kontrolü", r.TokPerSec, r.TotalSec);
            }
            default:
                return new(model, id, kind, msg, "", false, $"bilinmeyen kategori: {kind}", 0, 0);
        }
    }
    catch (Exception e)
    {
        return new(model, id, kind, msg, "", false, "HATA: " + e.Message.Split('\n')[0], 0, 0);
    }
}

JsonArray Msgs(string? system, string user, string? imageBase64 = null)
{
    var arr = new JsonArray();
    if (system is not null) arr.Add(new JsonObject { ["role"] = "system", ["content"] = system });
    var u = new JsonObject { ["role"] = "user", ["content"] = user };
    if (imageBase64 is not null) u["images"] = new JsonArray(imageBase64);
    arr.Add(u);
    return arr;
}

async Task<ChatResult> ChatAsync(string model, JsonArray messages, JsonNode? format = null, JsonArray? tools = null, int numCtx = 8192)
{
    // num_predict: cevap uzunluğu sınırı. JSON modunda küçük modeller bazen durmadan boşluk üretip döngüye girer.
    var maxTokens = format is not null ? 400 : 2048;
    var body = new JsonObject
    {
        ["model"] = model,
        ["messages"] = messages.DeepClone(),
        ["stream"] = false,
        ["think"] = false, // düşünme modunu kapat: daha hızlı, JSON'u bozmaz
        ["options"] = new JsonObject { ["num_ctx"] = numCtx, ["temperature"] = 0.2, ["num_predict"] = maxTokens },
    };
    if (format is not null) body["format"] = format.DeepClone();
    if (tools is not null) body["tools"] = tools.DeepClone();

    JsonNode r;
    try { r = await PostAsync("/api/chat", body); }
    catch (HttpRequestException e) when (e.Message.Contains("think", StringComparison.OrdinalIgnoreCase))
    {
        body.Remove("think"); // düşünme modunu desteklemeyen model
        r = await PostAsync("/api/chat", body);
    }

    var m = r["message"]!;
    return new ChatResult(
        m["content"]?.GetValue<string>() ?? "",
        m["tool_calls"]?.AsArray(),
        r["eval_count"]?.GetValue<long>() ?? 0,
        r["eval_duration"]?.GetValue<long>() ?? 0,
        r["load_duration"]?.GetValue<long>() ?? 0,
        r["total_duration"]?.GetValue<long>() ?? 0,
        r["done_reason"]?.GetValue<string>() ?? "");
}

async Task<List<float[]>> EmbedAsync(string model, List<string> input, bool cpu)
{
    var body = new JsonObject { ["model"] = model, ["input"] = new JsonArray(input.Select(s => (JsonNode)s!).ToArray()) };
    if (cpu) body["options"] = new JsonObject { ["num_gpu"] = 0 }; // 0 katman GPU'ya = tamamen CPU
    var r = await PostAsync("/api/embed", body);
    return r["embeddings"]!.AsArray().Select(e => e!.AsArray().Select(x => x!.GetValue<float>()).ToArray()).ToList();
}

async Task<(long size, long sizeVram)> PsAsync(string model)
{
    var r = JsonNode.Parse(await http.GetStringAsync(ollamaUrl + "/api/ps"))!;
    var m = r["models"]!.AsArray().FirstOrDefault(x => x!["name"]!.GetValue<string>() == model
        || x!["model"]!.GetValue<string>() == model);
    return m is null ? (0, 0) : (m["size"]!.GetValue<long>(), m["size_vram"]!.GetValue<long>());
}

async Task UnloadAllAsync()
{
    var r = JsonNode.Parse(await http.GetStringAsync(ollamaUrl + "/api/ps"))!;
    foreach (var m in r["models"]!.AsArray())
    {
        var name = m!["name"]!.GetValue<string>();
        await PostAsync("/api/generate", new JsonObject { ["model"] = name, ["keep_alive"] = 0 });
    }
}

async Task<JsonNode> PostAsync(string path, JsonNode body)
{
    using var res = await http.PostAsync(ollamaUrl + path,
        new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
    var text = await res.Content.ReadAsStringAsync();
    if (!res.IsSuccessStatusCode) throw new HttpRequestException($"{(int)res.StatusCode}: {text}");
    return JsonNode.Parse(text)!;
}

// Kosinüs benzerliği: iki vektör aynı yöne bakıyorsa 1, alakasızsa 0'a yakın.
static double Cosine(float[] a, float[] b)
{
    double dot = 0, na = 0, nb = 0;
    for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
    return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
}

static string Gb(long bytes) => (bytes / 1024.0 / 1024 / 1024).ToString("F2");
static string Pct(long part, long whole) => whole == 0 ? "-" : (100.0 * part / whole).ToString("F0");

static string FindDir()
{
    var cwd = Directory.GetCurrentDirectory();
    foreach (var d in new[] { cwd, Path.Combine(cwd, "tests", "model-eval") })
        if (File.Exists(Path.Combine(d, "sorular.json"))) return d;
    throw new FileNotFoundException("sorular.json bulunamadı. Komutu tests/model-eval klasöründen çalıştır.");
}

void WriteReport()
{
    var sb = new StringBuilder();
    sb.AppendLine($"# Model değerlendirme raporu · {now}\n");
    sb.AppendLine($"Test bağlam uzunluğu (num_ctx): {testCtx}\n");

    if (modelSummaries.Count > 0)
    {
        var kinds = new[] { "cikarma", "router", "arac", "gorsel" };
        sb.AppendLine("## Özet\n");
        sb.AppendLine("| Model | Yükleme (sn) | " + string.Join(" | ", kinds) + " | Ort. tok/s | Ort. cevap (sn) |");
        sb.AppendLine("|---|---|" + string.Concat(kinds.Select(_ => "---|")) + "---|---|");
        foreach (var s in modelSummaries)
        {
            var mine = results.Where(r => r.Model == s.Model).ToList();
            var cells = kinds.Select(k =>
            {
                var scored = mine.Where(r => r.Kind == k && r.Passed is not null).ToList();
                return scored.Count == 0 ? "-" : $"{scored.Count(r => r.Passed == true)}/{scored.Count}";
            });
            var timed = mine.Where(r => r.TokPerSec > 0).ToList();
            sb.AppendLine($"| {s.Model} | {s.LoadSec:F1} | {string.Join(" | ", cells)} | " +
                $"{(timed.Count == 0 ? 0 : timed.Average(r => r.TokPerSec)):F1} | {(timed.Count == 0 ? 0 : timed.Average(r => r.Sec)):F1} |");
        }

        sb.AppendLine("\n## VRAM ve bağlam uzunluğu\n");
        sb.AppendLine("| Model | num_ctx | Toplam (GB) | GPU'da (GB) | GPU % |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var s in modelSummaries)
            foreach (var v in s.Vram)
                sb.AppendLine($"| {s.Model} | {v.Ctx} | {Gb(v.Size)} | {Gb(v.SizeVram)} | {Pct(v.SizeVram, v.Size)} |");
    }

    if (embedRows.Count > 0)
    {
        sb.AppendLine("\n## Embedding\n");
        sb.AppendLine("| Model | Cihaz | Doğru (top-1) | Toplam süre (ms) | Vektör boyutu | GPU'da (GB) |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var e in embedRows)
            sb.AppendLine($"| {e.Model} | {e.Device} | {e.Correct}/{e.Total} | {e.Ms:F0} | {e.Dim} | {Gb(e.SizeVram)} |");
    }

    foreach (var g in results.GroupBy(r => r.Model))
    {
        sb.AppendLine($"\n## Ayrıntılar · {g.Key}\n");
        foreach (var r in g)
        {
            var mark = r.Passed switch { true => "✅", false => "❌", null => "📝" };
            sb.AppendLine($"### {mark} {r.Id} · {r.Kind} · {r.Note}");
            sb.AppendLine($"**Soru:** {r.Prompt}\n");
            sb.AppendLine("```");
            sb.AppendLine(r.Answer.Trim());
            sb.AppendLine("```");
            sb.AppendLine($"_{r.TokPerSec:F1} tok/s · {r.Sec:F1} sn_\n");
        }
    }

    File.WriteAllText(Path.Combine(outDir, "rapor.md"), sb.ToString());
    File.WriteAllText(Path.Combine(outDir, "ham.json"), JsonSerializer.Serialize(
        new { results, modelSummaries, embedRows }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}

record ChatResult(string Content, JsonArray? ToolCalls, long EvalCount, long EvalNs, long LoadNs, long TotalNs, string DoneReason)
{
    public double TokPerSec => EvalNs == 0 ? 0 : EvalCount / (EvalNs / 1e9);
    public double TotalSec => TotalNs / 1e9;
}
record TestResult(string Model, string Id, string Kind, string Prompt, string Answer, bool? Passed, string Note, double TokPerSec, double Sec);
record VramRow(int Ctx, long Size, long SizeVram);
record ModelSummary(string Model, double LoadSec, List<VramRow> Vram);
record EmbedRow(string Model, string Device, int Correct, int Total, double Ms, int Dim, long Size, long SizeVram);
