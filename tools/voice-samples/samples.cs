// Ultron · Faz 6 · Ses örnekleri ve STT ölçümü
// Aynı cümleyi Piper (dfki) ve Windows "Microsoft Tolga" ile, düz ve "Ultron efekti"yle seslendirir;
// sonra her kaydı Whisper'a geri dinletip süre ve doğruluğu ölçer. Çıktı: ses-ornekleri/ (git'e girmez).
// Çalıştırma (proje kökünden): dotnet run tools/voice-samples/samples.cs
#:property TargetFramework=net10.0-windows10.0.19041.0
#:property PublishAot=false
#:project ../../src/Ultron.Infrastructure/Ultron.Infrastructure.csproj

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ultron.Core.Voice;
using Ultron.Infrastructure;
using Ultron.Infrastructure.Voice;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

Console.OutputEncoding = Encoding.UTF8;

const string Sentence =
    "Günaydın. Bugün okul günü; öğleden sonra yağmur bekleniyor, şemsiye almanız iyi olur. Termodinamik iki dersiniz saat on dörtte.";

var outDir = Path.Combine(Directory.GetCurrentDirectory(), "ses-ornekleri");
Directory.CreateDirectory(outDir);

ServiceProvider Build(bool effect) => new ServiceCollection()
    .AddLogging()
    .AddUltronInfrastructure(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Voice:Effect:Enabled"] = effect.ToString(),
            ["Voice:PiperLengthScale"] = effect ? "0.92" : "1.0",
            ["Database:Path"] = Path.Combine(Path.GetTempPath(), "ultron-voice-samples.db"),
        })
        .Build())
    .BuildServiceProvider();

using var plain = Build(effect: false);
using var ultron = Build(effect: true);
var effectSettings = new UltronVoiceEffect();

// 1-2) Piper
foreach (var (name, sp) in new[] { ("1-piper-duz", plain), ("2-piper-ultron", ultron) })
{
    var sw = Stopwatch.StartNew();
    var wav = await sp.GetRequiredService<ITextToSpeech>().SynthesizeAsync(Sentence);
    await File.WriteAllBytesAsync(Path.Combine(outDir, name + ".wav"), wav);
    Console.WriteLine($"{name}: {sw.ElapsedMilliseconds} ms");
}

// 3-4) Windows OneCore "Microsoft Tolga"
var tolga = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName.Contains("Tolga"));
if (tolga is null)
{
    Console.WriteLine("Microsoft Tolga bulunamadı.");
}
else
{
    using var synth = new SpeechSynthesizer { Voice = tolga };
    var sw = Stopwatch.StartNew();
    using var stream = await synth.SynthesizeTextToStreamAsync(Sentence);
    var bytes = new byte[stream.Size];
    using (var reader = new DataReader(stream.GetInputStreamAt(0)))
    {
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
    }
    Console.WriteLine($"3-tolga-duz: {sw.ElapsedMilliseconds} ms");
    await File.WriteAllBytesAsync(Path.Combine(outDir, "3-tolga-duz.wav"), bytes);
    var withEffect = VoiceEffects.ApplyUltron(Wav.Read(bytes), effectSettings);
    await File.WriteAllBytesAsync(Path.Combine(outDir, "4-tolga-ultron.wav"), Wav.Write(withEffect));
}

// STT ölçümü: her kaydı Whisper'a geri dinlet.
var stt = plain.GetRequiredService<ISpeechToText>();
await stt.TranscribeAsync(Wav.Write(new Wav.Audio(new float[16000], 16000))); // modeli yükle (ısınma)
Console.WriteLine($"\n--- Whisper (large-v3-turbo q5, {Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary}) ---");
foreach (var file in Directory.GetFiles(outDir, "*.wav").Order())
{
    var wav = await File.ReadAllBytesAsync(file);
    var seconds = Wav.Read(wav) is var a ? a.Samples.Length / (double)a.SampleRate : 0;
    var sw = Stopwatch.StartNew();
    var text = await stt.TranscribeAsync(wav);
    Console.WriteLine($"{Path.GetFileName(file)} ({seconds:F1} sn ses) → {sw.ElapsedMilliseconds} ms\n   \"{text}\"");
}

Console.WriteLine($"\nÖrnekler: {outDir}");
