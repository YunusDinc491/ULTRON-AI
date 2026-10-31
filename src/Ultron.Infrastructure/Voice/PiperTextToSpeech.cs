using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Voice;

namespace Ultron.Infrastructure.Voice;

/// <summary>
/// Piper (yerel, hızlı, ONNX tabanlı TTS) ile Türkçe seslendirme. piper.exe'ye metni stdin'den verip
/// geçici WAV dosyası aldırıyoruz, sonra "Ultron havası" efektini uyguluyoruz.
/// </summary>
internal sealed class PiperTextToSpeech(IOptions<VoiceOptions> options, ILogger<PiperTextToSpeech> logger) : ITextToSpeech
{
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
    {
        text = SpeechText.ForSpeech(text);
        if (text.Length == 0)
            return [];

        var o = options.Value;
        var exe = o.Resolve(o.PiperExecutable);
        var voice = o.Resolve(o.PiperVoice);
        if (!File.Exists(exe) || !File.Exists(voice))
            throw new FileNotFoundException($"Piper ya da ses modeli bulunamadı: {exe} / {voice}");

        var output = Path.Combine(Path.GetTempPath(), $"ultron-tts-{Guid.NewGuid():N}.wav");
        try
        {
            var sw = Stopwatch.StartNew();
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!, // espeak-ng-data ve onnxruntime.dll yanında
                RedirectStandardInput = true,
                RedirectStandardOutput = true, // piper çıktı dosyasının yolunu yazar; konsola düşmesin
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
            };
            foreach (var arg in new[] { "--model", voice, "--output_file", output, "--length_scale", o.PiperLengthScale.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Piper başlatılamadı.");
            // Piper satır satır okur: tek satır = tek konuşma.
            await process.StandardInput.WriteLineAsync(text.ReplaceLineEndings(" "));
            process.StandardInput.Close();
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 || !File.Exists(output))
                throw new InvalidOperationException($"Piper hata verdi ({process.ExitCode}): {await errors}");

            var audio = VoiceEffects.ApplyUltron(Wav.Read(await File.ReadAllBytesAsync(output, cancellationToken)), o.Effect);
            logger.LogInformation("TTS {Chars} karakter → {Seconds:F1} sn ses, {Ms} ms", text.Length, audio.Samples.Length / (double)audio.SampleRate, sw.ElapsedMilliseconds);
            return Wav.Write(audio);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
