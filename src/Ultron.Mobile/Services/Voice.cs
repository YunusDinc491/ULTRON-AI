using System.Text;

namespace Ultron.Mobile.Services;

/// <summary>
/// Telefon mikrofonundan 16 kHz mono 16-bit kayıt (Ultron'un sesli soru biçimi). "Bas-konuş": basınca başlar, susunca (1,5 sn
/// sessizlik) ya da tekrar basınca biter; en fazla 45 sn. Ses yazıya bilgisayardaki Ultron'da (Groq) çevrilir.
/// </summary>
public sealed class VoiceRecorder
{
    private const int Rate = 16000;
    private CancellationTokenSource? _stop;

    public bool Recording { get; private set; }

    /// <summary>0-1 ses seviyesi (küre animasyonu için).</summary>
    public event Action<float>? Level;

    public void Stop() => _stop?.Cancel();

    /// <returns>WAV baytları; izin yoksa ya da hiç ses yoksa null.</returns>
    public async Task<byte[]?> RecordAsync(CancellationToken cancellationToken = default)
    {
        if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
            return null;
#if ANDROID
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _stop.Token;
        var minBuffer = Android.Media.AudioRecord.GetMinBufferSize(Rate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit);
        using var record = new Android.Media.AudioRecord(Android.Media.AudioSource.VoiceRecognition, Rate, Android.Media.ChannelIn.Mono,
            Android.Media.Encoding.Pcm16bit, Math.Max(minBuffer, Rate));
        var pcm = new MemoryStream();
        var buffer = new byte[Rate / 10 * 2]; // 100 ms
        Recording = true;
        record.StartRecording();
        try
        {
            int heardMs = 0, silentMs = 0, totalMs = 0;
            var noise = 0f;
            await Task.Run(() =>
            {
                while (!token.IsCancellationRequested && totalMs < 45_000)
                {
                    var read = record.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        continue;
                    pcm.Write(buffer, 0, read);
                    totalMs += 100;
                    var rms = Rms(buffer, read);
                    Level?.Invoke(Math.Min(1f, rms * 8));
                    if (totalMs <= 300) { noise = Math.Max(noise, rms); continue; } // ilk 300 ms: ortam
                    var loud = rms > Math.Max(0.015f, Math.Min(noise, 0.02f) * 3);
                    if (loud) { heardMs += 100; silentMs = 0; }
                    else if (heardMs >= 200) silentMs += 100;
                    if (heardMs >= 200 && silentMs >= 1500)
                        break; // konuştu ve sustu
                    if (heardMs == 0 && totalMs >= 8000)
                        break; // hiç konuşmadı
                }
            }, CancellationToken.None);
            return heardMs >= 200 ? Wav(pcm.ToArray()) : null;
        }
        finally
        {
            record.Stop();
            Recording = false;
        }
#else
        await Task.CompletedTask;
        return null;
#endif
    }

    private static float Rms(byte[] buffer, int count)
    {
        double sum = 0;
        for (var i = 0; i + 1 < count; i += 2)
        {
            var s = BitConverter.ToInt16(buffer, i) / 32768f;
            sum += s * s;
        }
        return (float)Math.Sqrt(sum / Math.Max(1, count / 2));
    }

    private static byte[] Wav(byte[] pcm)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length); w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }
}

/// <summary>Ultron'un sesini (bilgisayardan gelen WAV) çalar; cümleler sırayla.</summary>
public sealed class VoicePlayer
{
    private readonly SemaphoreSlim _gate = new(1, 1);
#if ANDROID
    private Android.Media.MediaPlayer? _current;
    private TaskCompletionSource? _done;
#endif

    public bool Speaking { get; private set; }

    public event Action<bool>? SpeakingChanged;

    public async Task PlayAsync(byte[] wav, CancellationToken cancellationToken = default)
    {
        if (wav.Length < 100)
            return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
#if ANDROID
            var file = Path.Combine(FileSystem.CacheDirectory, $"ses-{Guid.NewGuid():N}.wav");
            await File.WriteAllBytesAsync(file, wav, cancellationToken);
            var done = _done = new TaskCompletionSource();
            _current = new Android.Media.MediaPlayer();
            _current.SetDataSource(file);
            _current.Completion += (_, _) => done.TrySetResult();
            _current.Prepare();
            Speaking = true;
            SpeakingChanged?.Invoke(true);
            _current.Start();
            using (cancellationToken.Register(() => done.TrySetResult()))
                await done.Task;
            _current.Release();
            _current = null;
            File.Delete(file);
#endif
        }
        finally
        {
            Speaking = false;
            SpeakingChanged?.Invoke(false);
            _gate.Release();
        }
    }

    public void Stop()
    {
#if ANDROID
        try { _current?.Stop(); } catch (Java.Lang.IllegalStateException) { }
        _done?.TrySetResult(); // durdurulan ses beklemede kalmasın
#endif
    }
}
