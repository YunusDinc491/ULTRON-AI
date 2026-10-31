#:property PublishAot=false
#:package Microsoft.AspNetCore.SignalR.Client@10.0.14
#:project ../../src/Ultron.Contracts/Ultron.Contracts.csproj
// Faz 19 ölçümü: aynı soruları Ultron 1.1 ve Ultron Medium ile sorar; PRO'yu (kullanıcının Claude limitinden harcar) sadece
// --pro verilirse ilk soruyla dener ve aynı soruyu 1.1'de tekrar sorup "PRO hafızasından" geldiğini doğrular.
// Rapor: tests/ultron-paket/rapor-<tarih>.md. Çalıştırma: dotnet run tools/ultron-paket/olcum.cs [--pro]   (Ultron açıkken)
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.SignalR.Client;
using Ultron.Contracts;

Console.OutputEncoding = Encoding.UTF8;
var withPro = args.Contains("--pro");
var root = Directory.GetCurrentDirectory();
await using var hub = new HubConnectionBuilder().WithUrl("http://127.0.0.1:5199/hub/chat").WithServerTimeout(TimeSpan.FromMinutes(10)).Build();
await hub.StartAsync();
var original = await hub.InvokeAsync<string>("GetAnswerTier");
var conversation = (await hub.InvokeAsync<ConversationDto>("StartTestConversation")).Id;

string[] questions =
[
    "Isı pompası nasıl çalışır ve kışın verimi neden düşer?",
    "Rust ile Go arasındaki temel farklar nelerdir, hangisi ne zaman seçilmeli?",
    "Türkiye'de elektrikli araç şarj istasyonu sayısı son durumda ne kadar?",
];
var report = new StringBuilder($"# Faz 19 paket ölçümü — {DateTime.Now:yyyy-MM-dd HH:mm}\n\n| Soru | Paket | Süre | Uzunluk | Kaynak | Yol |\n|---|---|---|---|---|---|\n");
var bodies = new StringBuilder();
try
{
    foreach (var q in questions)
        foreach (var tier in new[] { "1.1", "medium" })
            await AskAsync(q, tier);
    if (withPro)
    {
        await AskAsync(questions[0] + " (PRO)", "pro", questions[0]);
        await AskAsync(questions[0] + " (tekrar, 1.1)", "1.1", questions[0]);
    }
}
finally
{
    await hub.InvokeAsync("SetAnswerTier", original);
}

Directory.CreateDirectory(Path.Combine(root, "tests", "ultron-paket"));
var path = Path.Combine(root, "tests", "ultron-paket", $"rapor-{DateTime.Now:yyyyMMdd-HHmm}.md");
await File.WriteAllTextAsync(path, report + "\n## Cevaplar\n\n" + bodies);
Console.WriteLine("Rapor: " + path);

async Task AskAsync(string label, string tier, string? text = null)
{
    await hub.InvokeAsync("SetAnswerTier", tier);
    var sw = Stopwatch.StartNew();
    string answer = "";
    var sources = 0;
    var path = new List<string>();
    await foreach (var e in hub.StreamAsync<ChatEventDto>("SendMessage", conversation, text ?? label))
    {
        if (e.Type == ChatEventTypes.Completed)
            answer = e.Text ?? "";
        else if (e.Type == ChatEventTypes.Sources)
            sources += e.Sources?.Count ?? 0;
        else if (e.Type == ChatEventTypes.Progress && e.Query is { } t && !path.Contains(t))
            path.Add(t);
        else if (e.Type == ChatEventTypes.Web)
            path.Add("web");
    }
    var row = $"| {label} | {tier} | {sw.Elapsed.TotalSeconds:0} sn | {answer.Length} | {sources} | {string.Join(" → ", path)} |";
    Console.WriteLine(row);
    report.AppendLine(row);
    bodies.AppendLine($"### {label} · {tier}\n\n{answer}\n");
}
