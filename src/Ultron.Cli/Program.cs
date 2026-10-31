// Geçici terminal istemcisi: Faz 5'te masaüstü uygulaması gelince emekli olacak.
// Sohbet mantığı Core'daki ChatOrchestrator'da; burası sadece olayları ekrana yazar.
// Komutlar: /yeni (yeni konuşma), /hafiza (hafızadaki bilgiler), /sorgular (internete giden son sorgular),
// /ara <sorgu> (web aramasını ve sayfa okumayı dener).

using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ultron.Core.Chat;
using Ultron.Core.Conversations;
using Ultron.Core.Documents;
using Ultron.Core.Research;
using Ultron.Core.Memories;
using Ultron.Core.Web;
using Ultron.Infrastructure;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// ContentRoot = exe'nin klasörü: appsettings.json, komut nereden çalıştırılırsa çalıştırılsın bulunur.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Services.AddUltronInfrastructure(builder.Configuration);
using var host = builder.Build();
await host.Services.InitializeDatabaseAsync();
await host.StartAsync(); // arka plan işçilerini (bilgi çıkarma) başlatır

var orchestrator = host.Services.GetRequiredService<ChatOrchestrator>();
var store = host.Services.GetRequiredService<IConversationStore>();
var memoryStore = host.Services.GetRequiredService<IMemoryStore>();
var memoryQueue = host.Services.GetRequiredService<IMemoryQueue>();
var showDiagnostics = builder.Configuration.GetValue("Chat:ShowDiagnostics", false);

var conversation = await store.GetLatestAsync() ?? await store.StartAsync();
var history = conversation.Messages;

Console.WriteLine(history.Count == 0
    ? "Ultron hazır. Komutlar: /yeni, /hafiza, /sorgular, /ara, /belge <yol> | <koleksiyon>. Çıkmak için boş satırda Enter."
    : $"Ultron hazır. Son konuşmaya devam ediliyor ({history.Count} mesaj). Komutlar: /yeni, /hafiza, /sorgular, /ara, /belge <yol> | <koleksiyon>. Çıkmak için boş satırda Enter.");
foreach (var m in history.TakeLast(4))
    Console.WriteLine($"  {(m.Role == Role.User ? "Sen" : "Ultron")}: {m.Content}");

while (true)
{
    Console.Write("\nSen: ");
    // Yönlendirilmiş girdide başta görünmez BOM (﻿) gelebiliyor; DB'ye ve komut kontrolüne girmesin.
    var input = Console.ReadLine()?.Trim().TrimStart('﻿');
    if (string.IsNullOrWhiteSpace(input))
        break;

    switch (input)
    {
        case "/yeni":
            conversation = await store.StartAsync();
            Console.WriteLine("Yeni konuşma başlatıldı.");
            continue;

        case "/hafiza":
            await memoryQueue.WaitUntilIdleAsync(TimeSpan.FromSeconds(30)); // bekleyen çıkarmalar da listeye girsin
            var all = await memoryStore.GetActiveAsync();
            Console.WriteLine(all.Count == 0 ? "Hafıza boş." : $"Hafızada {all.Count} bilgi:");
            foreach (var group in all.GroupBy(m => m.Category))
                Console.WriteLine($"  {group.Key}: {string.Join(" | ", group.Select(m => $"#{m.Id} {m.Content}"))}");
            continue;

        case "/sorgular":
            // Gizlilik: internete hangi sorguların gittiği her zaman görülebilir.
            var outgoing = await host.Services.GetRequiredService<IWebCacheStore>().GetRecentOutgoingAsync(10);
            Console.WriteLine(outgoing.Count == 0 ? "İnternete henüz sorgu gitmedi." : "İnternete giden son sorgular:");
            foreach (var q in outgoing)
                Console.WriteLine($"  {q.CreatedAtUtc.ToLocalTime():dd.MM HH:mm}  {q.Query}");
            continue;
    }

    if (input.StartsWith("/belge ", StringComparison.Ordinal))
    {
        // "/belge C:\notlar\hafta5.pdf | Termo II": dosyayı bilgi tabanına ekler (koleksiyon isteğe bağlı).
        var parts = input[7..].Split('|', 2, StringSplitOptions.TrimEntries);
        var path = parts[0].Trim('"');
        var documents = host.Services.GetRequiredService<Ultron.Core.Documents.DocumentService>();
        Console.WriteLine(File.Exists(path)
            ? (await documents.AddFileAsync(path, await File.ReadAllBytesAsync(path), parts.Length > 1 ? parts[1] : null)).Message
            : $"Dosya bulunamadı: {path}");
        continue;
    }

    if (input.StartsWith("/ara ", StringComparison.Ordinal))
    {
        // Geliştirme komutu: web aramasını ve sayfa okumayı modelden bağımsız dener.
        var search = host.Services.GetRequiredService<IWebSearch>();
        var reader = host.Services.GetRequiredService<IWebPageReader>();
        var results = await search.SearchAsync(input[5..]);
        Console.WriteLine($"{results.Count} sonuç:");
        foreach (var r in results)
        {
            var text = await reader.ReadTextAsync(r.Url);
            Console.WriteLine($"  - {r.Title} | {r.Url}");
            Console.WriteLine(text is null
                ? "      (okunamadı)"
                : $"      {text.Length} karakter: {text[..Math.Min(160, text.Length)].ReplaceLineEndings(" ")}…");
        }
        continue;
    }

    var answerStarted = false;
    await foreach (var e in orchestrator.SendAsync(conversation.Id, input))
    {
        switch (e)
        {
            case IntentDetected d:
                Diagnostic($"(niyet: {d.Intent})");
                break;
            case MemoriesRecalled { Recall: var recall }:
                if (recall.Core.Count > 0)
                    Diagnostic($"(çekirdek: {string.Join(" | ", recall.Core.Select(m => m.Content))})");
                if (recall.Relevant.Count > 0)
                    Diagnostic($"(hafızadan: {string.Join(" | ", recall.Relevant.Select(m => m.Content))})");
                break;
            case WebPrepared { Context: var web }:
                Diagnostic(web.FromCache
                    ? $"(önbellekten: {web.Plan.CacheKey})"
                    : $"(web: \"{web.Plan.Query}\" → {web.Sources.Count} kaynak, önbellek anahtarı: {web.Plan.CacheKey})");
                break;
            case WebUnavailable u:
                Diagnostic($"(web kullanılamadı: {u.Reason})");
                break;
            case DocumentsSearched { Context: var docs }:
                Diagnostic($"(belgeler{(docs.Scope is null ? "" : $" · {docs.Scope}")}: {string.Join(" | ", docs.Sources.Select(s => s.Label))})");
                break;
            case DocumentSourcesListed s:
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                foreach (var source in s.Sources)
                    Console.Write($"  [{source.Number}] {source.Label}\n");
                Console.ResetColor();
                answerStarted = false;
                break;
            case ResearchProgress p:
                Diagnostic($"(araştırma: {p.Step})");
                break;
            case AnswerChunk c:
                if (!answerStarted)
                {
                    Console.Write("Ultron: ");
                    answerStarted = true;
                }
                Console.Write(c.Text);
                break;
            case SourcesListed s:
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                foreach (var source in s.Sources)
                    Console.Write($"  [{source.Number}] {source.Title} | {source.Url}\n");
                Console.ResetColor();
                answerStarted = false; // satır sonu zaten yazıldı
                break;
        }
    }
    if (answerStarted)
        Console.WriteLine();
}

Console.WriteLine("Hafıza güncelleniyor...");
await memoryQueue.WaitUntilIdleAsync(TimeSpan.FromSeconds(60));
await host.StopAsync();

void Diagnostic(string text)
{
    if (!showDiagnostics)
        return;
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  {text}");
    Console.ResetColor();
}
