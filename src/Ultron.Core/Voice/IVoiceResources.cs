namespace Ultron.Core.Voice;

/// <summary>
/// Ses modellerinin ekran kartındaki yeri (Faz 16, tasarım kararı): İş modunda Whisper ve Chatterbox boşaltılır, model
/// (gemma4) bileşen yazarken ekran kartı ona kalır; ana arayüze dönünce ses geri yüklenir. Boşaltılmışken konuşma Piper'la
/// (işlemcide) okunur, yazıya çevirme yapılmaz.
/// </summary>
public interface IVoiceResources
{
    bool Released { get; }

    Task ReleaseAsync(CancellationToken cancellationToken = default);

    Task RestoreAsync(CancellationToken cancellationToken = default);
}

/// <summary>Ses kurulu değilken (testler, değerlendirme): hiçbir şey yapmaz.</summary>
public sealed class NoVoiceResources : IVoiceResources
{
    public bool Released { get; private set; }

    public Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        Released = true;
        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        Released = false;
        return Task.CompletedTask;
    }
}
