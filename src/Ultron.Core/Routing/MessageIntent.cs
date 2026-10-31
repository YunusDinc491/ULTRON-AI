namespace Ultron.Core.Routing;

/// <summary>Kullanıcı mesajının türü: cevabın hangi kaynaktan geleceğini belirler.</summary>
public enum MessageIntent
{
    /// <summary>Kullanıcının kendi hayatı hakkında soru → hafıza.</summary>
    Kisisel,

    /// <summary>Zamanla değişmeyen bilgi (tanım, açıklama) → modelin kendi bilgisi.</summary>
    GenelBilgi,

    /// <summary>Zamanla değişen bilgi (kur, hava, haber) → önbellek / web.</summary>
    Guncel,

    /// <summary>Yüklenen dosyalar hakkında soru → belge tabanı (Faz 10).</summary>
    Belge,

    /// <summary>Bir işlem isteği (hatırlatma, uygulama açma) → araçlar (Faz 7).</summary>
    Komut,

    /// <summary>Selamlaşma, hal hatır → doğrudan cevap.</summary>
    Sohbet,

    /// <summary>Programlama, framework, API, hata mesajı sorusu → Yazılım Kütüphanesi (Faz 15 b).</summary>
    Yazilim,
}
