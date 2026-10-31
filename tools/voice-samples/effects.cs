// Ultron · Faz 6 · Efekt örnekleri: hazır bir TTS kaydına farklı hız/robot ayarları uygular; dinleyip seçmek için.
// Çalıştırma (proje kökünden): dotnet run tools/voice-samples/effects.cs -- <girdi.wav> <çıktı klasörü>
#:property PublishAot=false
#:property TargetFramework=net10.0-windows10.0.19041.0
#:project ../../src/Ultron.Infrastructure/Ultron.Infrastructure.csproj

using Ultron.Infrastructure.Voice;

var input = Wav.Read(await File.ReadAllBytesAsync(args[0]));
Directory.CreateDirectory(args[1]);
var name = Path.GetFileNameWithoutExtension(args[0]);

(string Label, UltronVoiceEffect Effect)[] variants =
[
    ("0-efektsiz", new() { Enabled = false }),
    ("1-hafif (hiz 1.15, robot 0.2)", new() { PitchSemitones = 0, Speed = 1.15, RobotMix = 0.2, MetallicMix = 0.18, EchoMix = 0.08 }),
    ("2-orta (hiz 1.2, robot 0.3) VARSAYILAN", new() { PitchSemitones = 0, Speed = 1.2, RobotMix = 0.3, MetallicMix = 0.25, EchoMix = 0.08 }),
    ("3-guclu (hiz 1.3, robot 0.5, kalin)", new() { PitchSemitones = -1, Speed = 1.3, RobotMix = 0.5, MetallicMix = 0.35, EchoMix = 0.1 }),
];

foreach (var (label, effect) in variants)
{
    var audio = VoiceEffects.ApplyUltron(input, effect);
    var path = Path.Combine(args[1], $"{label} - {name}.wav");
    await File.WriteAllBytesAsync(path, Wav.Write(audio));
    Console.WriteLine($"{label}: {input.Samples.Length / (double)input.SampleRate:F1} sn → {audio.Samples.Length / (double)audio.SampleRate:F1} sn");
}
