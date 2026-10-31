using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using Ultron.Infrastructure.Voice;

namespace Ultron.Infrastructure.Telegram;

/// <summary>
/// Telegram sesli mesajları OGG kabında Opus sesidir. Whisper WAV ister, Telegram'a da OGG/Opus gönderilir:
/// Concentus (saf C# Opus) ile ikisi arasında çeviri.
/// </summary>
internal static class OpusAudio
{
    private const int Rate = 48000; // Opus her zaman 48 kHz çözer

    public static Wav.Audio DecodeOgg(byte[] ogg)
    {
        var decoder = OpusCodecFactory.CreateDecoder(Rate, 1);
        var reader = new OpusOggReadStream(decoder, new MemoryStream(ogg));
        var samples = new List<float>();
        while (reader.HasNextPacket)
        {
            var packet = reader.DecodeNextPacket();
            if (packet is null)
                continue;
            foreach (var s in packet)
                samples.Add(s / 32768f);
        }
        return new Wav.Audio([.. samples], Rate);
    }

    public static byte[] EncodeOgg(Wav.Audio audio)
    {
        var input = Wav.Resample(audio, Rate);
        var encoder = OpusCodecFactory.CreateEncoder(Rate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = 32000;
        using var output = new MemoryStream();
        var writer = new OpusOggWriteStream(encoder, output);
        var pcm = input.Samples.Select(s => (short)Math.Clamp(s * 32767f, short.MinValue, short.MaxValue)).ToArray();
        writer.WriteSamples(pcm, 0, pcm.Length);
        writer.Finish();
        return output.ToArray();
    }
}
