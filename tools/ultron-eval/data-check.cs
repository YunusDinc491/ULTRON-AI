// Ultron · Eğitim örneği kalite kontrolü: data/finetune/ornekler.jsonl için bozuk satır, eksik alan, tekrar,
// değerlendirme setiyle çakışma, boş/aşırı uzun cevap ve araç/hafıza JSON'unun geçerliliği.
// Çalıştırma (proje kökünden): dotnet run tools/ultron-eval/data-check.cs
#:property PublishAot=false
#:project ../../src/Ultron.Core/Ultron.Core.csproj

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ultron.Core.Text;

Console.OutputEncoding = Encoding.UTF8;
var root = Directory.GetCurrentDirectory();
var problems = new List<string>();
var seen = new Dictionary<string, string>();
var evalInputs = new[] { "dev", "holdout" }
    .SelectMany(s => JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tests", "ultron-eval", s + ".json")))!["vakalar"]!.AsArray())
    .Select(c => (Id: c!["id"]!.GetValue<string>(), Tokens: Tokens(c["girdi"]!.GetValue<string>()))).ToList();
var lines = File.ReadAllLines(Path.Combine(root, "data", "finetune", "ornekler.jsonl"));
var counts = new Dictionary<string, int>();
var languages = new Dictionary<string, int>();

for (var n = 0; n < lines.Length; n++)
{
    var line = lines[n].Trim();
    if (line.Length == 0)
        continue;
    JsonNode? row;
    try { row = JsonNode.Parse(line); }
    catch (JsonException ex) { problems.Add($"satır {n + 1}: bozuk JSON ({ex.Message})"); continue; }

    var id = row?["id"]?.GetValue<string>();
    var messages = row?["messages"]?.AsArray();
    if (id is null || messages is null || messages.Count < 2 || row!["dil"] is null || row["etiket"] is null)
    {
        problems.Add($"satır {n + 1}: alan eksik (id/dil/etiket/messages)");
        continue;
    }
    var label = row["etiket"]!.GetValue<string>();
    counts[label] = counts.GetValueOrDefault(label) + 1;
    languages[row["dil"]!.GetValue<string>()] = languages.GetValueOrDefault(row["dil"]!.GetValue<string>()) + 1;
    var user = messages.LastOrDefault(m => m!["role"]!.GetValue<string>() == "user")?["content"]?.GetValue<string>() ?? "";
    var answer = messages.Last()!["content"]?.GetValue<string>() ?? "";

    if (messages.Last()!["role"]!.GetValue<string>() != "assistant")
        problems.Add($"{id}: son mesaj asistan değil");
    if (answer.Trim().Length == 0 || answer.Length > 2500)
        problems.Add($"{id}: cevap boş ya da çok uzun ({answer.Length})");
    var key = TurkishText.Normalize(user);
    if (seen.TryGetValue(key, out var other))
        problems.Add($"{id}: {other} ile aynı soru (tekrar)");
    seen[key] = id;
    foreach (var e in evalInputs.Where(e => Jaccard(e.Tokens, Tokens(user)) >= 0.6))
        problems.Add($"{id}: değerlendirme vakası {e.Id} ile çok benzer (sızıntı riski)");
    if (label.StartsWith("arac_karari") || label.StartsWith("hafiza") || label == "hatirlatma_cumlesi")
    {
        try
        {
            var json = JsonNode.Parse(answer)!.AsObject();
            if (label.StartsWith("arac_karari") && json["arac"] is null)
                problems.Add($"{id}: araç JSON'unda 'arac' yok");
            if (label.StartsWith("hafiza") && json["bilgiler"] is not JsonArray)
                problems.Add($"{id}: hafıza JSON'unda 'bilgiler' dizisi yok");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            problems.Add($"{id}: cevap geçerli JSON değil");
        }
    }
}

Console.WriteLine($"Örnek: {seen.Count} · diller: {string.Join(", ", languages.Select(l => $"{l.Key}={l.Value}"))}");
Console.WriteLine("Etiketler: " + string.Join(", ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}")));
Console.WriteLine(problems.Count == 0 ? "Sorun yok ✓" : $"{problems.Count} sorun:\n  " + string.Join("\n  ", problems));
return problems.Count == 0 ? 0 : 1;

static HashSet<string> Tokens(string s) => TurkishText.Normalize(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).ToHashSet();

static double Jaccard(HashSet<string> a, HashSet<string> b) => a.Count + b.Count == 0 ? 0 : (double)a.Intersect(b).Count() / a.Union(b).Count();
