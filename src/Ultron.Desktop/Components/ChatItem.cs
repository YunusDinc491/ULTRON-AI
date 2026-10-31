using Ultron.Contracts;

namespace Ultron.Desktop.Components;

public static class OrbState
{
    public const string Idle = "idle";
    public const string Thinking = "thinking";
    public const string Listening = "listening";
    public const string Speaking = "speaking";
    public const string Error = "error";
}

/// <summary>Ekrandaki tek bir mesaj. Ultron'un cevabı akarken Text büyür, Streaming true kalır.</summary>
public sealed class ChatItem(bool isUser, string text)
{
    public bool IsUser { get; } = isUser;

    public string Text { get; set; } = text;

    public bool Streaming { get; set; }

    /// <summary>Cevabın üstündeki küçük etiketler: "Güncel bilgi", "İnternette arandı: …", "Hafızadan 2 bilgi".</summary>
    public List<string> Chips { get; } = [];

    public IReadOnlyList<SourceDto> Sources { get; set; } = [];

    /// <summary>Soruya eklenen görselin küçük önizlemesi (data: adresi); görsel kaydedilmez.</summary>
    public string? ImagePreview { get; set; }

    /// <summary>Cevabın altındaki seçenekli sorular.</summary>
    public IReadOnlyList<FollowUpDto> FollowUps { get; set; } = [];

    /// <summary>Soru sırası → seçilen seçenek (her soruda tek seçim).</summary>
    public Dictionary<int, string> Selected { get; } = [];

    /// <summary>Gönderildi ya da geçildi: kart kapanır.</summary>
    public bool FollowUpsDone { get; set; }

    /// <summary>Sorunun altındaki + ile açılan "bu konuya ekle" paneli (takip soruları artık otomatik değil).</summary>
    public bool ExtraOpen { get; set; }

    /// <summary>+ basılınca öneriler Host'tan alınırken.</summary>
    public bool LoadingExtra { get; set; }

    /// <summary>Takip sorularında seçeneklerin yanında kullanıcının kendi yazdığı cevap.</summary>
    public string CustomAnswer { get; set; } = "";

    /// <summary>Hatırlatma kartıysa hatırlatıcının numarası (tekrarlar aynı kartı günceller).</summary>
    public long? ReminderId { get; set; }

    /// <summary>"Okudum" bekliyor (önemli / çok önemli): kartta OKUDUM düğmesi görünür.</summary>
    public bool AwaitingAcknowledge { get; set; }

    /// <summary>Kaç kez tekrar çaldı (kartta "3. kez" gibi gösterilir).</summary>
    public int Repeats { get; set; }
}
