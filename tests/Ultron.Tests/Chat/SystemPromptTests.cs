using Ultron.Core.Chat;

namespace Ultron.Tests.Chat;

public class SystemPromptTests
{
    [Fact]
    public void Tarih_turkce_olarak_eklenir()
    {
        var prompt = SystemPrompt.Build(new DateTime(2026, 10, 26, 17, 45, 0));

        Assert.Contains("26 Ekim 2026 Pazartesi, 17:45", prompt);
    }
}
