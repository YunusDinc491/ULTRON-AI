using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Ultron.Core.Voice;

namespace Ultron.Infrastructure.Voice;

/// <summary>
/// Bulut ses anahtarları (Faz 23): sohbete yazılmaz, kullanıcı Not Defteri'yle dosyaya koyar. 1. satır anahtar, 2. satır isteğe bağlı
/// ayar (ElevenLabs'te ses kimliği). Dosya her istekte okunur: anahtar konunca yeniden başlatma gerekmez.
/// </summary>
internal static class CloudKeys
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Ultron");

    public static (string Key, string? Extra)? Read(string fileName)
    {
        try
        {
            var path = Path.Combine(Folder, fileName);
            if (!File.Exists(path))
                return null;
            var lines = File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            return lines.Length > 0 && lines[0].Length > 10 ? (lines[0], lines.ElementAtOrDefault(1)) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

/// <summary>
/// Groq ses tanıma (Faz 23, tasarım kararı): Whisper large-v3'ün TAM modeli Groq'un donanımında; kısa komut genelde 1 sn altında
/// (bilgisayarda ekran kartı paylaşıldığı için 4-15 sn sürüyordu) ve turbo'dan daha iyi Türkçe. Anahtar yoksa, hata ya da kota
/// olursa yerel Whisper. "Ultron mu dendi?" kontrolü (TranscribeQuick) HER ZAMAN yerelde: ortam sesi buluta gitmez.
/// </summary>
internal sealed class GroqSpeechToText(HttpClient http, WhisperSpeechToText local, ILogger<GroqSpeechToText> logger) : ISpeechToText
{
    public const string KeyFile = "groq-anahtar.txt";

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellationToken = default)
    {
        if (CloudKeys.Read(KeyFile) is not { } key)
            return await local.TranscribeAsync(wav, cancellationToken);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var audio = WhisperSpeechToText.Normalize(Wav.Resample(Wav.Read(wav), 16000));
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Wav.Write(audio));
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(file, "file", "ses.wav");
            form.Add(new StringContent("whisper-large-v3"), "model");
            form.Add(new StringContent("tr"), "language");
            form.Add(new StringContent("json"), "response_format");
            form.Add(new StringContent("0"), "temperature");
            form.Add(new StringContent(WhisperSpeechToText.Prompt), "prompt");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions") { Content = form };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Key);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Groq ses tanıma {Status}; yerel Whisper kullanılıyor", (int)response.StatusCode);
                return await local.TranscribeAsync(wav, cancellationToken);
            }
            var text = (JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token))?["text"]?.GetValue<string>() ?? "").Trim();
            if (WhisperSpeechToText.IsPromptEcho(text))
                text = "";
            logger.LogInformation("STT (Groq) {Seconds:F1} sn ses → {Ms} ms: {Text}", audio.Samples.Length / 16000.0, sw.ElapsedMilliseconds, text);
            return SpeechText.IsLikelyHallucination(text) ? "" : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Groq'a ulaşılamadı ({Error}); yerel Whisper kullanılıyor", ex.GetType().Name);
            return await local.TranscribeAsync(wav, cancellationToken);
        }
    }

    public Task<string> TranscribeQuickAsync(byte[] wav, CancellationToken cancellationToken = default) => local.TranscribeQuickAsync(wav, cancellationToken);

    /// <summary>
    /// Yerel Whisper her durumda ön yüklenir: "Ultron mı dendi?" doğrulaması yerelde (2026-10-30: Groq varken yüklenmiyordu, ilk
    /// doğrulama soğuk modelle 5 sn'yi aşıp iptal ediliyor, kullanıcının "Ultron"ları duyulmuyordu).
    /// </summary>
    public Task WarmUpAsync(CancellationToken cancellationToken = default) => local.WarmUpAsync(cancellationToken);
}

/// <summary>
/// ElevenLabs sesi (Faz 23): doğal Türkçe (Flash v2.5, ~100 ms'de başlar). Kısa cümleler ("Açıyorum.", "Tamam.") bir kez üretilip
/// diskte saklanır, sonra anında çalar ve kotayı yemez. Anahtar yoksa, kota bittiyse (o ay) ya da hata olursa bugünkü ses
/// (Chatterbox, o da yoksa Piper).
/// </summary>
internal sealed class ElevenLabsTextToSpeech(HttpClient http, ChatterboxTextToSpeech local, TimeProvider time, ILogger<ElevenLabsTextToSpeech> logger) : ITextToSpeech
{
    public const string KeyFile = "elevenlabs-anahtar.txt";

    /// <summary>Varsayılan ses (ElevenLabs'in hazır, çok dilli erkek sesi); 2. satırda başka ses kimliği verilebilir.</summary>
    public const string DefaultVoice = "pNInz6obpgDQGcFmaJgB";

    private const int CacheMaxChars = 40;
    private static readonly string CacheFolder = Path.Combine(CloudKeys.Folder, "ses-onbellek");
    private string? _exhaustedMonth;

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
    {
        var spoken = SpeechText.ForSpeech(text);
        if (spoken.Length == 0)
            return [];
        var month = time.GetLocalNow().ToString("yyyy-MM");
        if (CloudKeys.Read(KeyFile) is not { } key || _exhaustedMonth == month)
            return await local.SynthesizeAsync(text, cancellationToken);
        var voice = string.IsNullOrWhiteSpace(key.Extra) ? DefaultVoice : key.Extra;

        var cache = spoken.Length <= CacheMaxChars ? Path.Combine(CacheFolder, $"{voice}-{Hash(spoken)}.wav") : null;
        if (cache is not null && File.Exists(cache))
            return await File.ReadAllBytesAsync(cache, cancellationToken);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.elevenlabs.io/v1/text-to-speech/{voice}?output_format=pcm_16000")
            {
                Content = JsonContent(new JsonObject { ["text"] = spoken, ["model_id"] = "eleven_flash_v2_5", ["language_code"] = "tr" }),
            };
            request.Headers.Add("xi-api-key", key.Key);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                if (body.Contains("quota", StringComparison.OrdinalIgnoreCase))
                {
                    _exhaustedMonth = month;
                    logger.LogWarning("ElevenLabs kotası doldu; bu ay Ultron'un yerel sesi kullanılacak");
                }
                else
                    logger.LogWarning("ElevenLabs {Status}: {Body}", (int)response.StatusCode, body.Length > 200 ? body[..200] : body);
                return await local.SynthesizeAsync(text, cancellationToken);
            }
            var pcm = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            var wav = Wav.Write(new Wav.Audio(Pcm16ToFloat(pcm), 16000));
            logger.LogInformation("TTS (ElevenLabs) {Chars} karakter → {Ms} ms", spoken.Length, sw.ElapsedMilliseconds);
            if (cache is not null)
            {
                Directory.CreateDirectory(CacheFolder);
                await File.WriteAllBytesAsync(cache, wav, cancellationToken);
            }
            return wav;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("ElevenLabs'e ulaşılamadı ({Error}); yerel ses", ex.GetType().Name);
            return await local.SynthesizeAsync(text, cancellationToken);
        }
    }

    private static StringContent JsonContent(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim().ToLowerInvariant())))[..16];

    internal static float[] Pcm16ToFloat(byte[] pcm)
    {
        var samples = new float[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
        return samples;
    }
}
