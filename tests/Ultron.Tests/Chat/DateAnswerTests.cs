using Ultron.Core.Chat;

namespace Ultron.Tests.Chat;

public class DateAnswerTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 5, 30, 0); // çarşamba

    [Theory]
    [InlineData("Bugünden 10 gün sonra haftanın hangi günü olur?", "7 Kasım 2026 Cumartesi.")]
    [InlineData("3 hafta sonra ayın kaçı?", "18 Kasım 2026 Çarşamba.")]
    [InlineData("Yarın hangi gün?", "29 Ekim 2026 Perşembe.")]
    [InlineData("2 ay önce hangi tarihti? Hangi tarih yani", "28 Ağustos 2026 Cuma.")]
    public void Takvim_hesabi_kodla_yapilir(string question, string expected) => Assert.Equal(expected, DateAnswer.TryAnswer(question, Now));

    [Theory]
    [InlineData("10 gün sonra sınavım var, nasıl çalışayım?")]
    [InlineData("Bugün hava nasıl?")]
    [InlineData("Dolar bugün kaç TL?")]
    public void Takvim_sorusu_olmayanlara_dokunmaz(string question) => Assert.Null(DateAnswer.TryAnswer(question, Now));
}
