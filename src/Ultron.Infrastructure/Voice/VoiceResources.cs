using Ultron.Core.Voice;

namespace Ultron.Infrastructure.Voice;

/// <summary>Whisper ve Chatterbox'ı birlikte boşaltır/yükler (İş modu, Faz 16).</summary>
internal sealed class VoiceResources(WhisperSpeechToText whisper, ChatterboxLauncher chatterbox) : IVoiceResources
{
    public bool Released { get; private set; }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (Released)
            return;
        Released = true;
        whisper.Release();
        await chatterbox.SuspendAsync();
    }

    public Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!Released)
            return Task.CompletedTask;
        Released = false;
        whisper.Restore();
        chatterbox.Resume();
        return Task.CompletedTask;
    }
}
