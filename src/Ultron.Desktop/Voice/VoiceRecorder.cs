using System.IO;
using NAudio.Wave;

namespace Ultron.Desktop.Voice;

/// <summary>
/// Varsayılan mikrofondan 16 kHz mono kayıt (Whisper'ın istediği biçim). <see cref="SilenceDetector"/>
/// konuşmanın bittiğini anlayınca kayıt kendiliğinden durur. Düğmeye tekrar basılırsa hemen biter.
/// </summary>
public sealed class VoiceRecorder
{
    private static readonly WaveFormat Format = new(16000, 16, 1);
    private const int BufferMs = 30;

    private static int _active;

    /// <summary>Herhangi bir yerde (arayüz, simge) kayıt sürüyor mu: wake word bu sırada tetiklenmesin.</summary>
    public static bool AnyRecording => Volatile.Read(ref _active) > 0;

    /// <summary>Anlık ses seviyesi (0-1): küre dinlerken sese göre titreşsin.</summary>
    public event Action<float>? LevelChanged;

    /// <summary>
    /// Kaydı başlatır ve bitince WAV döndürür. Hiç konuşulmadıysa ya da iptal edildiyse null.
    /// <paramref name="finishNow"/> iptal edilince (düğmeye tekrar basıldı) o ana kadarki kayıt kullanılır.
    /// </summary>
    /// <param name="noSpeechTimeoutMs">Hiç konuşulmazsa ne kadar beklensin (wake word sonrası ek soru: 8 sn).</param>
    public async Task<byte[]?> RecordAsync(CancellationToken finishNow, CancellationToken abort, int? noSpeechTimeoutMs = null)
    {
        if (WaveInEvent.DeviceCount == 0)
            throw new InvalidOperationException("Mikrofon bulunamadı.");

        var detector = noSpeechTimeoutMs is { } wait ? new SilenceDetector { NoSpeechTimeoutMs = wait } : new SilenceDetector();
        Interlocked.Increment(ref _active);
        using var active = new ActiveScope();
        var pcm = new MemoryStream();
        var done = new TaskCompletionSource<ListenResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Windows'ta seçili mikrofon (çağrı kelimesi dinleyicisiyle aynı cihaz; 2026-10-30).
        using var waveIn = new WaveInEvent { DeviceNumber = Math.Max(0, Microphone.Pick().Number), WaveFormat = Format, BufferMilliseconds = BufferMs, NumberOfBuffers = 4 };
        waveIn.DataAvailable += (_, e) =>
        {
            if (done.Task.IsCompleted)
                return;
            pcm.Write(e.Buffer, 0, e.BytesRecorded);
            var rms = Rms(e.Buffer, e.BytesRecorded);
            LevelChanged?.Invoke(rms);
            var result = detector.Feed(rms, e.BytesRecorded * 1000 / Format.AverageBytesPerSecond);
            if (result != ListenResult.Continue)
                done.TrySetResult(result);
        };
        waveIn.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
                done.TrySetException(e.Exception);
            else
                done.TrySetResult(detector.HeardSpeech ? ListenResult.SpeechEnded : ListenResult.NoSpeech);
        };

        using var finish = finishNow.Register(() => done.TrySetResult(ListenResult.SpeechEnded));
        using var cancel = abort.Register(() => done.TrySetCanceled(abort));

        waveIn.StartRecording();
        ListenResult outcome;
        try
        {
            outcome = await done.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            waveIn.StopRecording();
            LevelChanged?.Invoke(0);
        }

        // Düğmeyle erken bitirmede de en az biraz konuşma olmalı; yoksa Whisper sessizlikten cümle uydurur.
        if (outcome == ListenResult.NoSpeech || !detector.HeardSpeech)
            return null;
        return ToWav(pcm.ToArray());
    }

    private sealed class ActiveScope : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref _active);
    }

    private static float Rms(byte[] buffer, int bytes)
    {
        double sum = 0;
        var count = bytes / 2;
        for (var i = 0; i < count; i++)
        {
            var s = BitConverter.ToInt16(buffer, i * 2) / 32768.0;
            sum += s * s;
        }
        return count == 0 ? 0 : (float)Math.Sqrt(sum / count);
    }

    private static byte[] ToWav(byte[] pcm)
    {
        using var output = new MemoryStream();
        using (var writer = new WaveFileWriter(output, Format))
            writer.Write(pcm, 0, pcm.Length);
        return output.ToArray();
    }
}
