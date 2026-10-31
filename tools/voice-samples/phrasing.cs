// Ultron · Hatırlatma cümlesi denemesi: gerçek modelle birkaç hatırlatmanın nasıl söyleneceğini yazdırır.
// Çalıştırma (proje kökünden, Ollama açıkken): dotnet run tools/voice-samples/phrasing.cs
#:property PublishAot=false
#:property TargetFramework=net10.0-windows10.0.19041.0
#:project ../../src/Ultron.Infrastructure/Ultron.Infrastructure.csproj

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ultron.Core.Reminders;
using Ultron.Infrastructure;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var services = new ServiceCollection().AddLogging().AddUltronInfrastructure(new ConfigurationBuilder().Build()).BuildServiceProvider();
var phrasing = services.GetRequiredService<ReminderPhrasing>();

foreach (var (text, missed) in new[] { ("Su iç", false), ("Annemi ara", false), ("İlacımı iç", false), ("Termodinamik ödevini teslim et", false), ("Su iç", true) })
    Console.WriteLine($"{text}{(missed ? " (kaçırıldı)" : "")} → {await phrasing.PhraseAsync(text, missed)}");
