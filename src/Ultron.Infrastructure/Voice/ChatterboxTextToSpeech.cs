using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ultron.Core.Voice;

namespace Ultron.Infrastructure.Voice;

/// <summary>
/// Chatterbox Multilingual (yerel Python servisi, ekran kartında) ile tonlamalı Türkçe seslendirme.
/// Servis kapalıysa, henüz yükleniyorsa ya da hata verirse Piper'a düşer: Ultron hiçbir zaman sessiz kalmaz.
/// </summary>
internal sealed class ChatterboxTextToSpeech(
    HttpClient http,
    PiperTextToSpeech piper,
    IOptions<VoiceOptions> options,
    ILogger<ChatterboxTextToSpeech> logger) : ITextToSpeech
{
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        if (!string.Equals(o.Engine, "Chatterbox", StringComparison.OrdinalIgnoreCase))
            return await piper.SynthesizeAsync(text, cancellationToken);

        var spoken = SpeechText.ForSpeech(text);
        if (spoken.Length == 0)
            return [];

        try
        {
            var sw = Stopwatch.StartNew();
            var c = o.Chatterbox;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(c.TimeoutSeconds));
            using var response = await http.PostAsync(new Uri(new Uri(c.Url), "/tts"), Body(new
            {
                text = spoken,
                exaggeration = c.Exaggeration,
                cfg_weight = c.CfgWeight,
                temperature = c.Temperature,
            }), timeout.Token);
            response.EnsureSuccessStatusCode();

            var audio = VoiceEffects.ApplyUltron(Wav.Read(await response.Content.ReadAsByteArrayAsync(timeout.Token)), c.Effect);
            logger.LogInformation("TTS (Chatterbox) {Chars} karakter → {Seconds:F1} sn ses, {Ms} ms",
                spoken.Length, audio.Samples.Length / (double)audio.SampleRate, sw.ElapsedMilliseconds);
            return Wav.Write(audio);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Chatterbox kullanılamadı ({Message}); Piper ile okunuyor.", ex.Message);
            return await piper.SynthesizeAsync(text, cancellationToken);
        }
    }

    /// <summary>
    /// JSON gövde, uzunluğu belli (Content-Length) olarak. PostAsJsonAsync gövdeyi parça parça (chunked) gönderiyor; Python'un basit
    /// sunucusu uzunluk başlığına bakıp metni boş okuyor ve 400 dönüyordu → Ultron fark edilmeden hep Piper'la konuşuyordu (2026-10-27).
    /// </summary>
    internal static StringContent Body(object value) =>
        new(System.Text.Json.JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");
}
