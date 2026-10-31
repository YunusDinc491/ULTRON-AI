using Ultron.Core.Lessons;

namespace Ultron.Tests.Lessons;

public sealed class MathExpressionTests
{
    [Theory]
    [InlineData("300*8^((1,4-1)/1,4)", 543.4, 0.1)] // izantropik sıkıştırma (gece sınavında model 674 K demişti)
    [InlineData("1 - 300/600", 0.5, 1e-9)]
    [InlineData("0,622*2,3/(101,325-2,3)", 0.01445, 1e-4)]
    [InlineData("sqrt(1,4*287*293)", 343.1, 0.1)]
    [InlineData("2^3^2", 512, 1e-9)]
    [InlineData("-3^2", -9, 1e-9)]
    [InlineData("2^-1", 0.5, 1e-9)]
    [InlineData("2pi", 6.2832, 1e-3)]
    [InlineData("ln(e)", 1, 1e-9)]
    [InlineData("sin(30)", 0.5, 1e-9)]
    [InlineData("1,2×0,8", 0.96, 1e-9)]
    public void Hesaplar(string expression, double expected, double tolerance)
    {
        Assert.True(MathExpression.TryEvaluate(expression, out var value), expression);
        Assert.InRange(value, expected - tolerance, expected + tolerance);
    }

    [Theory]
    [InlineData("2+")]
    [InlineData("foo(3)")]
    [InlineData("1/0")]
    [InlineData("(2+3")]
    public void Gecersiz_ifade_reddedilir(string expression) => Assert.False(MathExpression.TryEvaluate(expression, out _));

    [Fact]
    public void Onceki_adimin_sonucu_degisken_olarak_kullanilir()
    {
        var vars = new Dictionary<string, double> { ["T_2"] = 543.4, ["T1"] = 300 };
        Assert.True(MathExpression.TryEvaluate("1,005*(T_2-T1)", vars, out var w));
        Assert.Equal(244.6, w, 1);
        Assert.False(MathExpression.TryEvaluate("T3*2", vars, out _));
    }

    [Fact]
    public void Turkce_bicimlenir() => Assert.Equal("543,4", MathExpression.Format(543.4129));
}
