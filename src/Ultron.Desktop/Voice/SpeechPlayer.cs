using System.IO;
using System.Threading.Channels;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ultron.Desktop.Voice;

/// <summary>
/// Cümleleri sırayla seslendirir. Her cümle sıraya girer girmez Host'ta sese çevrilmeye başlar
/// (biri çalarken sonraki hazırlanır), çalma sırası ise hep aynı kalır. <see cref="Stop"/> sözü keser:
/// çalan ses susar, bekleyen cümleler atılır.
/// </summary>
public sealed class SpeechPlayer : IDisposable
{
    private readonly Func<string, CancellationToken, Task<byte[]>> _synthesize;
    private readonly object _gate = new();
    private Channel<(Task<byte[]> Audio, bool SaysName)> _queue = NewQueue();
    private static long _nameAudibleUntil;
    private static int _sayingName;
    private CancellationTokenSource _cts = new();
    private int _pending;

    public SpeechPlayer(Func<string, CancellationToken, Task<byte[]>> synthesize)
    {
        _synthesize = synthesize;
        _ = Task.Run(PlayLoopAsync);
    }

    /// <summary>Konuşmaya başlayınca true, sıra boşalınca false.</summary>
    public event Action<bool>? SpeakingChanged;

    /// <summary>Çalan sesin anlık seviyesi (0-1): küre sese göre titreşsin.</summary>
    public event Action<float>? LevelChanged;

    public bool IsSpeaking => Volatile.Read(ref _pending) > 0;

    /// <summary>
    /// Ultron şu an (ya da 1,5 sn öncesine kadar) kendi adını içeren bir cümle söylüyor mu: hoparlörden mikrofona gelen
    /// "Ultron" wake word sayılmasın. Adı geçmeyen cümlelerde wake word çalışır ("Ultron" deyince söz kesilir).
    /// </summary>
    public static bool SelfNameAudible => Volatile.Read(ref _sayingName) > 0 || Environment.TickCount64 < Interlocked.Read(ref _nameAudibleUntil);

    public void Enqueue(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return;
        lock (_gate)
        {
            if (Interlocked.Increment(ref _pending) == 1)
                SpeakingChanged?.Invoke(true);
            _queue.Writer.TryWrite((_synthesize(sentence, _cts.Token), WakeWordRules.IsWakeText(sentence)));
        }
    }

    /// <summary>Sözü keser (barge-in): çalan ses ve sıradaki cümleler iptal.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            _queue.Writer.TryComplete();
            _queue = NewQueue();
            if (Interlocked.Exchange(ref _pending, 0) > 0)
                SpeakingChanged?.Invoke(false);
        }
    }

    private async Task PlayLoopAsync()
    {
        while (true)
        {
            Channel<(Task<byte[]> Audio, bool SaysName)> queue;
            CancellationToken token;
            lock (_gate)
            {
                queue = _queue;
                token = _cts.Token;
            }

            // Kuyruk Stop() ile tamamlanırsa döngü yeni kuyruğa geçer.
            await foreach (var (synth, saysName) in queue.Reader.ReadAllAsync())
            {
                try
                {
                    var wav = await synth;
                    if (wav.Length > 0 && !token.IsCancellationRequested)
                    {
                        if (saysName)
                            Interlocked.Increment(ref _sayingName);
                        try
                        {
                            await PlayAsync(wav, token);
                        }
                        finally
                        {
                            if (saysName)
                            {
                                Interlocked.Exchange(ref _nameAudibleUntil, Environment.TickCount64 + 1500);
                                Interlocked.Decrement(ref _sayingName);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    DesktopLog.Write("Seslendirme hatası: " + ex.Message);
                }
                finally
                {
                    if (!token.IsCancellationRequested && Interlocked.Decrement(ref _pending) == 0)
                        SpeakingChanged?.Invoke(false);
                }
            }
        }
    }

    private async Task PlayAsync(byte[] wav, CancellationToken token)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        var meter = new MeteringSampleProvider(reader.ToSampleProvider(), reader.WaveFormat.SampleRate / 25);
        meter.StreamVolume += (_, e) => LevelChanged?.Invoke(e.MaxSampleValues.Length > 0 ? e.MaxSampleValues[0] : 0);

        using var output = new WaveOutEvent { DesiredLatency = 120 };
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, _) => stopped.TrySetResult();
        output.Init(meter);
        output.Play();

        using (token.Register(output.Stop))
            await stopped.Task;
        LevelChanged?.Invoke(0);
    }

    private static Channel<(Task<byte[]> Audio, bool SaysName)> NewQueue() =>
        Channel.CreateUnbounded<(Task<byte[]> Audio, bool SaysName)>(new UnboundedChannelOptions { SingleReader = true });

    public void Dispose()
    {
        Stop();
        _queue.Writer.TryComplete();
    }
}
