using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ultron.Infrastructure.Voice;

namespace Ultron.Tests.Voice;

public sealed class ChatterboxTextToSpeechTests
{
    private sealed class WavHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? LastBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var wav = Wav.Write(new Wav.Audio(Enumerable.Range(0, 2400).Select(i => MathF.Sin(i / 10f) * 0.5f).ToArray(), 24000));
            return new HttpResponseMessage(status) { Content = new ByteArrayContent(wav) };
        }
    }

    private static (ChatterboxTextToSpeech Tts, WavHandler Handler) Create(HttpStatusCode status)
    {
        // Piper yolları bilerek yok: Piper'a düşülürse FileNotFoundException görülür.
        var options = Options.Create(new VoiceOptions { ModelsDirectory = Path.Combine(Path.GetTempPath(), "ultron-yok-" + Guid.NewGuid()) });
        var handler = new WavHandler(status);
        var piper = new PiperTextToSpeech(options, NullLogger<PiperTextToSpeech>.Instance);
        return (new ChatterboxTextToSpeech(new HttpClient(handler), piper, options, NullLogger<ChatterboxTextToSpeech>.Instance), handler);
    }

    [Fact]
    public async Task Servis_cevap_verirse_wav_doner_ve_markdown_temizlenmis_metin_gider()
    {
        var (tts, handler) = Create(HttpStatusCode.OK);

        var wav = await tts.SynthesizeAsync("**Merhaba** [1]");

        Assert.Equal(24000, Wav.Read(wav).SampleRate);
        Assert.Contains("\"text\":\"Merhaba\"", handler.LastBody);
    }

    [Fact]
    public async Task Servis_hata_verirse_Pipera_duser()
    {
        var (tts, _) = Create(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => tts.SynthesizeAsync("Merhaba"));

        Assert.Contains("Piper", ex.Message);
    }
}
