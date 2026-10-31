namespace Ultron.Core.Chat;

/// <summary>appsettings.json içindeki "Chat" bölümü.</summary>
public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    /// <summary>Modele gönderilen son mesaj sayısı (num_ctx taşmasın diye).</summary>
    public int HistoryWindow { get; set; } = 20;

    /// <summary>
    /// Cevaptan sonra seçenekli takip soruları otomatik üretilsin mi (her mesajda bir ek model çağrısı). (2026-10-27):
    /// her sorudan sonra kart çıkmasın; sorunun altındaki + ile istenince gelsin (hub: SuggestFollowUps).
    /// </summary>
    public bool FollowUps { get; set; }
}
