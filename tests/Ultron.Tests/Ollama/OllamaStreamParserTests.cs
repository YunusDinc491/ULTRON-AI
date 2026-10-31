using Ultron.Infrastructure.Ollama;

namespace Ultron.Tests.Ollama;

public class OllamaStreamParserTests
{
    [Fact]
    public void Icerik_satirindan_metin_parcasi_okunur()
    {
        var chunk = OllamaStreamParser.ParseLine("""{"message":{"role":"assistant","content":"Mer"},"done":false}""");

        Assert.NotNull(chunk);
        Assert.Equal("Mer", chunk.Message?.Content);
        Assert.False(chunk.Done);
    }

    [Fact]
    public void Son_satirda_done_ve_bitis_nedeni_okunur()
    {
        var chunk = OllamaStreamParser.ParseLine("""{"message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","eval_count":12}""");

        Assert.NotNull(chunk);
        Assert.True(chunk.Done);
        Assert.Equal("stop", chunk.DoneReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Bos_satir_atlanir(string line) => Assert.Null(OllamaStreamParser.ParseLine(line));

    [Fact]
    public void Hata_satiri_istisna_firlatir()
    {
        var ex = Assert.Throws<OllamaException>(() => OllamaStreamParser.ParseLine("""{"error":"model not found"}"""));

        Assert.Equal("model not found", ex.Message);
    }
}
