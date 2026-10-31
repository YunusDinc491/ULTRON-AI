using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Ultron.Contracts;

namespace Ultron.Mobile.Services;

/// <summary>Ekrandaki bir mesaj (soru ya da Ultron'un cevabı).</summary>
public sealed class ChatLine
{
    public required bool FromUser { get; init; }

    public string Text { get; set; } = "";

    public bool Streaming { get; set; }

    public string? Status { get; set; }
}

/// <summary>
/// Telefondaki sohbet: bilgisayardaki Ultron'un konuşmasına (kullanıcının bölümü) yazar, cevabı akış halinde alır. Sesli soruda cevabın
/// ilk iki cümlesi Ultron'un sesiyle (bilgisayardan, ElevenLabs) okunur. Paket seçici (1.1/Medium/PRO) bilgisayardakiyle aynı ayar.
/// </summary>
public sealed class ChatSession(UltronConnection connection, VoicePlayer player)
{
    private Guid? _conversation;

    public List<ChatLine> Lines { get; } = [];

    public bool Busy { get; private set; }

    public event Action? Changed;

    public async Task<string> TierAsync()
    {
        await connection.ConnectAsync();
        return await connection.Hub.InvokeAsync<string>(ChatHubContract.GetAnswerTier);
    }

    public async Task SetTierAsync(string tier)
    {
        await connection.ConnectAsync();
        await connection.Hub.InvokeAsync(ChatHubContract.SetAnswerTier, tier);
    }

    /// <summary>Kullanıcının istediği hitap (bilgisayardaki ayar); boşsa selamda isim yok.</summary>
    public async Task<string> AddressAsync()
    {
        await connection.ConnectAsync();
        return await connection.Hub.InvokeAsync<string>(ChatHubContract.GetUserAddress);
    }

    /// <summary>İlk açılışta bilgisayardaki güncel konuşmayı getirir (masaüstünde yazılanlar da görünür).</summary>
    public async Task LoadAsync()
    {
        if (_conversation is not null)
            return;
        await connection.ConnectAsync();
        var conversation = await connection.Hub.InvokeAsync<ConversationDto>(ChatHubContract.GetCurrentConversation);
        _conversation = conversation.Id;
        Lines.Clear();
        Lines.AddRange(conversation.Messages
            .Where(m => m.Role.Equals("User", StringComparison.OrdinalIgnoreCase) || m.Role.Equals("Assistant", StringComparison.OrdinalIgnoreCase))
            .Select(m => new ChatLine { FromUser = m.Role.Equals("User", StringComparison.OrdinalIgnoreCase), Text = m.Content }));
        Changed?.Invoke();
    }

    public async Task NewConversationAsync()
    {
        await connection.ConnectAsync();
        _conversation = (await connection.Hub.InvokeAsync<ConversationDto>(ChatHubContract.StartNewConversation)).Id;
        Lines.Clear();
        Changed?.Invoke();
    }

    /// <summary>Sesli soru: kaydı Ultron yazıya çevirir (Groq), sonra normal mesaj gibi gider.</summary>
    public async Task<string> TranscribeAsync(byte[] wav)
    {
        await connection.ConnectAsync();
        return (await connection.Hub.InvokeAsync<string>(ChatHubContract.Transcribe, wav)).Trim();
    }

    public async Task SendAsync(string text, bool speak)
    {
        if (string.IsNullOrWhiteSpace(text) || Busy)
            return;
        Busy = true;
        try
        {
            await connection.ConnectAsync();
            _conversation ??= (await connection.Hub.InvokeAsync<ConversationDto>(ChatHubContract.GetCurrentConversation)).Id;
            Lines.Add(new ChatLine { FromUser = true, Text = text.Trim() });
            var reply = new ChatLine { FromUser = false, Streaming = true, Status = "Düşünüyor…" };
            Lines.Add(reply);
            Changed?.Invoke();
            await foreach (var e in connection.Hub.StreamAsync<ChatEventDto>(ChatHubContract.SendMessage, _conversation.Value, text.Trim()))
            {
                switch (e.Type)
                {
                    case ChatEventTypes.Chunk:
                        reply.Text += e.Text;
                        reply.Status = null;
                        break;
                    case ChatEventTypes.Progress:
                        reply.Status = e.Text;
                        break;
                    case ChatEventTypes.Completed:
                        reply.Text = e.Text ?? reply.Text;
                        reply.Streaming = false;
                        reply.Status = null;
                        break;
                }
                Changed?.Invoke();
            }
            reply.Streaming = false;
            Changed?.Invoke();
            if (speak && FirstSentences(reply.Text) is { Length: > 0 } spoken)
                await player.PlayAsync(await connection.Hub.InvokeAsync<byte[]>(ChatHubContract.Speak, spoken));
        }
        catch (Exception ex) when (ex is HubException or InvalidOperationException or HttpRequestException)
        {
            Lines.Add(new ChatLine { FromUser = false, Text = "Ultron'a ulaşılamadı: bilgisayar açık mı, Tailscale bağlı mı?" });
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Sesli cevap kısa: ilk iki cümle (masaüstündeki gibi); kod blokları ve listeler okunmaz.</summary>
    internal static string FirstSentences(string text)
    {
        var plain = text.Split("```")[0].Split("\n- ")[0].Replace("**", "").Replace("#", "").Trim();
        var sentences = System.Text.RegularExpressions.Regex.Split(plain, @"(?<=[.!?])\s+").Where(s => s.Length > 1).Take(2);
        return string.Join(' ', sentences).Trim();
    }
}
