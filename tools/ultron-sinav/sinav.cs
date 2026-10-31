#:property PublishAot=false
#:package Microsoft.AspNetCore.SignalR.Client@10.0.14
#:project ../../src/Ultron.Contracts/Ultron.Contracts.csproj

// Ultron sınavı: soruları ÇALIŞAN Ultron'a (masaüstünün kullandığı hub üzerinden) tek tek sorar, her soru ayrı konuşmada.
// Cevaplar otomatik puanlanmaz: Claude okuyup değerlendirir (rapor Markdown + JSON). Sonda yeni boş konuşma açılır.
// Kullanım (proje kökünden, Ultron açıkken): dotnet run tools/ultron-sinav/sinav.cs -- tests/ultron-sinav/tur1.json [etiket] [--id s01,s02]
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR.Client;
using Ultron.Contracts;

Console.OutputEncoding = Encoding.UTF8;
var file = args[0];
var label = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : Path.GetFileNameWithoutExtension(file);
var only = args.SkipWhile(a => a != "--id").Skip(1).FirstOrDefault()?.Split(',').ToHashSet();
var questions = JsonNode.Parse(File.ReadAllText(file))!["sorular"]!.AsArray()
    .Select(q => q!.AsObject())
    .Where(q => only is null || only.Contains(q["id"]!.GetValue<string>()))
    .ToList();

await using var hub = new HubConnectionBuilder()
    .WithUrl("http://127.0.0.1:5199" + ChatHubContract.Path)
    .WithServerTimeout(TimeSpan.FromMinutes(10))
    .Build();
await hub.StartAsync();

var results = new JsonArray();
int checkedCount = 0, passedCount = 0;
var totalTime = Stopwatch.StartNew();
var md = new StringBuilder($"# Ultron sınavı · {label} · {DateTime.Now:dd.MM.yyyy HH:mm}\n\n");
foreach (var q in questions)
{
    var id = q["id"]!.GetValue<string>();
    var turns = q["mesajlar"]?.AsArray().Select(m => m!.GetValue<string>()).ToList() ?? [q["soru"]!.GetValue<string>()];
    var conversation = await hub.InvokeAsync<ConversationDto>(ChatHubContract.StartTestConversation);
    var sw = Stopwatch.StartNew();
    string answer = "", intent = "", library = "", sources = "", tools = "";
    foreach (var text in turns) // çok adımlı sorularda son cevap değerlendirilir, öncekiler bağlam
    {
        var sb = new StringBuilder();
        var src = new List<string>();
        library = tools = "";
        try
        {
            await foreach (var e in hub.StreamAsync<ChatEventDto>(ChatHubContract.SendMessage, conversation.Id, text))
            {
                switch (e.Type)
                {
                    case ChatEventTypes.Intent: intent = e.Intent ?? ""; break;
                    case ChatEventTypes.Library: library = e.Text ?? ""; break;
                    case ChatEventTypes.Chunk: sb.Append(e.Text); break;
                    case ChatEventTypes.Sources: src.AddRange(e.Sources!.Select(s => $"[{s.Number}] {s.Title} — {s.Url}")); break;
                    case ChatEventTypes.Tools: tools = string.Join(", ", e.Tools!.Select(t => t.Name + (t.Ok ? "" : "✕"))); break;
                    case ChatEventTypes.Completed: sb.Clear().Append(e.Text); break;
                }
            }
        }
        catch (Exception ex)
        {
            sb.Append($"\n[HATA: {ex.Message}]");
        }
        answer = sb.ToString().Trim();
        sources = string.Join("\n", src);
    }
    var seconds = sw.Elapsed.TotalSeconds;
    var missing = Missing(answer, q["kontrol"]?.AsObject());
    if (missing is not null)
    {
        checkedCount++;
        if (missing.Count == 0) passedCount++;
    }
    Console.WriteLine($"{id,-6} {seconds,5:F1} sn  {intent,-9} kütüphane:{(library.Length == 0 ? "-" : library)}  {(missing is null ? "" : missing.Count == 0 ? "✓ " : "✕ ")}{Short(answer)}");

    results.Add(new JsonObject
    {
        ["id"] = id, ["seviye"] = q["seviye"]?.GetValue<string>(), ["konu"] = q["konu"]?.GetValue<string>(),
        ["soru"] = string.Join(" ⟶ ", turns), ["beklenen"] = q["beklenen"]?.GetValue<string>(),
        ["niyet"] = intent, ["kutuphane"] = library, ["araclar"] = tools, ["kaynaklar"] = sources,
        ["sure_sn"] = Math.Round(seconds, 1), ["cevap"] = answer,
        ["otomatik"] = missing is null ? null : missing.Count == 0, ["eksik"] = missing is null ? null : string.Join(", ", missing),
    });
    md.AppendLine($"## {id} · {q["seviye"]} · {q["konu"]}");
    md.AppendLine($"**Soru:** {string.Join(" ⟶ ", turns)}");
    md.AppendLine($"**Beklenen:** {q["beklenen"]}");
    md.AppendLine($"**Niyet:** {intent} · **kütüphane:** {(library.Length == 0 ? "-" : library)} · **araç:** {(tools.Length == 0 ? "-" : tools)} · {seconds:F1} sn"
        + (missing is null ? "" : missing.Count == 0 ? " · **otomatik: ✓**" : $" · **otomatik: ✕** (eksik: {string.Join(", ", missing)})"));
    md.AppendLine();
    md.AppendLine(answer);
    if (sources.Length > 0)
        md.AppendLine("\n_Kaynaklar:_\n" + sources);
    md.AppendLine("\n---\n");
}

if (checkedCount > 0)
{
    var summary = $"Otomatik puan: {passedCount}/{checkedCount} · toplam {totalTime.Elapsed.TotalMinutes:F1} dk · soru başı ortalama {totalTime.Elapsed.TotalSeconds / questions.Count:F1} sn";
    Console.WriteLine(summary);
    md.Insert(md.ToString().IndexOf("\n\n", StringComparison.Ordinal) + 2, $"**{summary}**\n\n");
}

await hub.InvokeAsync<ConversationDto>(ChatHubContract.StartNewConversation); // kullanıcının ekranında sınav konuşması kalmasın

var outDir = Path.Combine("tests", "ultron-sinav", "sonuclar");
Directory.CreateDirectory(outDir);
var stamp = $"{DateTime.Now:yyyyMMdd-HHmm}-{label}";
File.WriteAllText(Path.Combine(outDir, stamp + ".json"), results.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
File.WriteAllText(Path.Combine(outDir, stamp + ".md"), md.ToString());
Console.WriteLine($"Rapor: {Path.Combine(outDir, stamp + ".md")}");

// "kontrol": { "icermeli_hepsi_kok": ["a|b", "c"] }: her öğeden en az bir seçenek cevapta geçmeli (büyük/küçük harf, ı/i fark etmez).
static List<string>? Missing(string answer, JsonObject? check)
{
    if (check?["icermeli_hepsi_kok"] is not JsonArray all)
        return null;
    var text = Fold(answer);
    return all.Select(v => v!.GetValue<string>()).Where(v => !v.Split('|').Any(alt => text.Contains(Fold(alt), StringComparison.Ordinal))).ToList();
}

static string Fold(string s) => s.ToLowerInvariant().Replace("\u0307", "").Replace('ı', 'i');

static string Short(string s) => (s.Length <= 90 ? s : s[..90] + "…").ReplaceLineEndings(" ");
