using Ultron.Core.Tools;

namespace Ultron.Core.Chat;

/// <summary>
/// Modele gönderilen sohbet geçmişindeki tek bir mesaj. Araç döngüsünde asistan mesajı araç çağrılarını
/// (<see cref="ToolCalls"/>), araç mesajı da hangi aracın sonucu olduğunu (<see cref="ToolName"/>) taşır.
/// <see cref="Images"/>: kullanıcı mesajına eklenmiş görseller (PNG/JPEG baytları; gemma4 görseli okur, Faz 11).
/// </summary>
public sealed record ChatMessage(Role Role, string Content, IReadOnlyList<ToolCall>? ToolCalls = null, string? ToolName = null, IReadOnlyList<byte[]>? Images = null);
