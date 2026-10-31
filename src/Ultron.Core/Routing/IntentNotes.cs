namespace Ultron.Core.Routing;

/// <summary>Niyete göre modele verilen ek talimatlar ve modeli hiç çağırmadan verilen sabit cevaplar.</summary>
public static class IntentNotes
{
    /// <summary>
    /// Web araması gelene kadar (Faz 4 adım 3) güncel sorularda modelin kur, hava gibi değerleri
    /// eğitim verisinden "hatırlayıp" bugünün bilgisiymiş gibi söylemesini engeller. Canlı denemede işe yaradı.
    /// </summary>
    public const string NoWebAccess =
        "Bu soru güncel bilgi gerektiriyor (kur, fiyat, hava, haber gibi) ve şu an internete erişimin yok. " +
        "Tahmin yürütme veya eski bir değeri güncelmiş gibi söyleme; bu bilgiye şu an ulaşamadığını kısaca belirt.";

    /// <summary>
    /// Araçlar gelene kadar (Faz 7) komutlara verilen sabit cevap. Prompt'a "yaptım deme" notu eklemek
    /// canlı denemede 3/3 başarısız oldu (model yine "kurdum", "açtım" dedi); bu yüzden model hiç çağrılmaz.
    /// </summary>
    public const string NoToolsAnswer =
        "Bu işlemi henüz yapamıyorum: hatırlatma, alarm ve uygulama kontrolü gibi araçlar daha eklenmedi.";

    public static IReadOnlyList<string> For(MessageIntent intent, bool webAvailable) => intent switch
    {
        MessageIntent.Guncel when !webAvailable => [NoWebAccess],
        _ => [],
    };

    /// <summary>Modeli çağırmadan verilecek cevap; yoksa null (normal akış).</summary>
    public static string? DirectAnswer(MessageIntent intent, bool toolsAvailable) =>
        intent == MessageIntent.Komut && !toolsAvailable ? NoToolsAnswer : null;
}
