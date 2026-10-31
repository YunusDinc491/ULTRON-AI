using Ultron.Core.Chat;
using Ultron.Core.Conversations;

namespace Ultron.Tests.Chat;

public class ChatContextTests
{
    [Fact]
    public void System_mesaji_basta_ve_sadece_son_N_mesaj_gonderilir()
    {
        var history = Enumerable.Range(1, 10)
            .Select(i => new Message { Role = i % 2 == 1 ? Role.User : Role.Assistant, Content = $"m{i}" })
            .ToList();

        var messages = ChatContext.Build("talimat", history, maxMessages: 4);

        Assert.Equal(5, messages.Count);
        Assert.Equal(new ChatMessage(Role.System, "talimat"), messages[0]);
        Assert.Equal(["m7", "m8", "m9", "m10"], messages.Skip(1).Select(m => m.Content));
    }

    [Fact]
    public void Gecmisteki_system_mesajlari_tekrar_gonderilmez()
    {
        Message[] history = [new() { Role = Role.System, Content = "eski talimat" }, new() { Role = Role.User, Content = "selam" }];

        var messages = ChatContext.Build("yeni talimat", history, maxMessages: 20);

        Assert.Equal(["yeni talimat", "selam"], messages.Select(m => m.Content));
    }
}
