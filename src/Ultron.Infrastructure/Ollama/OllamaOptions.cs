namespace Ultron.Infrastructure.Ollama;

/// <summary>appsettings.json içindeki "Ollama" bölümü.</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public Uri BaseUrl { get; set; } = new("http://127.0.0.1:11434");

    public string ChatModel { get; set; } = "gemma4:e4b-it-qat";

    /// <summary>Yazılım sorularının cevabını yazan model (Faz 15 c). Boş: sohbet modeli yazar.</summary>
    public string CodeModel { get; set; } = "";

    /// <summary>
    /// Model değişince kayıtlı vektörler açılışta yeniden üretilir (<see cref="Embeddings.EmbeddingMigration"/>),
    /// ama hafıza/belge eşikleri modele özgüdür: elle yeniden ölçülmeli.
    /// </summary>
    public string EmbeddingModel { get; set; } = "qwen3-embedding:0.6b";

    /// <summary>
    /// Qwen3-Embedding biçimi: soru "Instruct: … \nQuery: …", belge ön eksiz. Türkçe talimat ölçümde belirgin iyi
    /// (10 hafıza sorusunda doğru kayıt en üstte: Türkçe 9/10, İngilizce 5/10).
    /// </summary>
    public string EmbeddingQueryPrefix { get; set; } = "Instruct: Kullanıcının sorusuna cevap veren bilgiyi veya metin parçasını bul\nQuery: ";

    public string EmbeddingDocumentPrefix { get; set; } = "";

    /// <summary>num_ctx: modelin aynı anda aklında tutabildiği token sayısı. Faz 1'de 16384'ün VRAM'e sığdığı ölçüldü.</summary>
    public int ContextLength { get; set; } = 16384;

    /// <summary>num_predict: cevap uzunluğu sınırı. Küçük modellerin sonsuz döngüye girmesine karşı.</summary>
    public int MaxResponseTokens { get; set; } = 2048;

    public double Temperature { get; set; } = 0.6;

    /// <summary>
    /// JSON çağrılarında sınır: Faz 1'de şemalı çağrıların döngüye girdiği görüldü. 400 idi; takip soruları
    /// (3 soru × 4 seçenek × etiket+cümle) sığmayıp kesildiği için 900'e çıkarıldı. Döngüye karşı hâlâ koruyor.
    /// </summary>
    public int JsonMaxResponseTokens { get; set; } = 900;

    /// <summary>İç işlerde (çıkarma, karar) yaratıcılık değil tutarlılık istiyoruz.</summary>
    public double JsonTemperature { get; set; } = 0.1;
}
