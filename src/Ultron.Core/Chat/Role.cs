namespace Ultron.Core.Chat;

/// <summary>Bir mesajın kimden geldiği.</summary>
public enum Role
{
    /// <summary>Modele verilen talimatlar (kişilik, kurallar, tarih).</summary>
    System,
    User,
    Assistant,
    /// <summary>Bir aracın (hatırlatıcı, hava durumu…) döndürdüğü sonuç.</summary>
    Tool,
}
