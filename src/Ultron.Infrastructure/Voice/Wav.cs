namespace Ultron.Infrastructure.Voice;

/// <summary>Basit WAV (PCM 16 bit) okuma/yazma. Ses, sayı dizisi olarak işlenir: her örnek -1..1 arası bir float.</summary>
public static class Wav
{
    public sealed record Audio(float[] Samples, int SampleRate);

    /// <summary>PCM 16 bit WAV okur; çok kanallıysa kanalların ortalamasını alır (mono).</summary>
    public static Audio Read(byte[] wav)
    {
        using var reader = new BinaryReader(new MemoryStream(wav));
        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("WAV değil (RIFF yok).");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("WAV değil (WAVE yok).");

        int channels = 1, sampleRate = 16000, bits = 16;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                var format = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                reader.BaseStream.Seek(size - 16, SeekOrigin.Current);
                if (format != 1 || bits != 16)
                    throw new InvalidDataException($"Sadece PCM 16 bit destekleniyor (format {format}, {bits} bit).");
            }
            else if (id == "data")
            {
                var frames = Math.Min(size, (int)(reader.BaseStream.Length - reader.BaseStream.Position)) / 2 / channels;
                var samples = new float[frames];
                for (var i = 0; i < frames; i++)
                {
                    float sum = 0;
                    for (var c = 0; c < channels; c++)
                        sum += reader.ReadInt16() / 32768f;
                    samples[i] = sum / channels;
                }
                return new Audio(samples, sampleRate);
            }
            else
            {
                reader.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }

        throw new InvalidDataException("WAV'da ses verisi (data) yok.");
    }

    public static byte[] Write(Audio audio)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataBytes = audio.Samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);                 // PCM
        w.Write((short)1);                 // mono
        w.Write(audio.SampleRate);
        w.Write(audio.SampleRate * 2);     // byte/sn
        w.Write((short)2);                 // blok hizalama
        w.Write((short)16);                // bit
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in audio.Samples)
            w.Write((short)Math.Clamp(s * 32767f, -32768f, 32767f));
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Doğrusal aradeğerleme ile örnekleme hızı değiştirir (Whisper 16 kHz ister).</summary>
    public static Audio Resample(Audio audio, int targetRate)
    {
        if (audio.SampleRate == targetRate)
            return audio;
        var ratio = (double)audio.SampleRate / targetRate;
        var output = new float[(int)(audio.Samples.Length / ratio)];
        for (var i = 0; i < output.Length; i++)
        {
            var pos = i * ratio;
            var i0 = (int)pos;
            var i1 = Math.Min(i0 + 1, audio.Samples.Length - 1);
            var t = (float)(pos - i0);
            output[i] = audio.Samples[i0] * (1 - t) + audio.Samples[i1] * t;
        }
        return new Audio(output, targetRate);
    }
}
