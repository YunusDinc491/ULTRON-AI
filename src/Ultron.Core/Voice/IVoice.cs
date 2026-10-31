namespace Ultron.Core.Voice;

/// <summary>Konuşmayı yazıya çevirir (STT).</summary>
public interface ISpeechToText
{
    /// <param name="wav">WAV dosyası (PCM 16 bit). Örnekleme hızı/kanal ne olursa olsun 16 kHz mono'ya çevrilir.</param>
    Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kısa ses için hızlı çeviri ("Ultron" dendi mi kontrolü): bağlam ipucu YOK (ipucunda geçen "Ultron" her sesi Ultron sandırırdı),
    /// en hızlı tahmin. Desteklemeyen motor normal çeviriyi kullanır.
    /// </summary>
    Task<string> TranscribeQuickAsync(byte[] wav, CancellationToken cancellationToken = default) => TranscribeAsync(wav, cancellationToken);

    /// <summary>Modeli önceden belleğe alır: kullanıcı konuşurken yüklensin, ilk cevap beklemesin.</summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Yazıyı konuşmaya çevirir (TTS). Girdi markdown olabilir (<see cref="SpeechText.ForSpeech"/> ile temizlenir).
/// Çıktı WAV (PCM 16 bit mono); okunacak bir şey yoksa boş dizi.
/// </summary>
public interface ITextToSpeech
{
    Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default);
}
