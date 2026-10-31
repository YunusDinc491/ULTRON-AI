namespace Ultron.Core.Documents;

/// <summary>Bilgi tabanına eklenmiş bir dosya (ders notu, PDF, Word, araştırma raporu).</summary>
public sealed class Document
{
    public long Id { get; init; }

    /// <summary>Görünen ad: "Termo II - Hafta 5.pdf".</summary>
    public required string FileName { get; set; }

    /// <summary>"Termo II", "Genel", "Araştırmalar"… Aramayı daraltmak için.</summary>
    public required string Collection { get; set; }

    public DocumentKind Kind { get; init; }

    /// <summary>Kopyanın diskteki yolu (kaynak kartından açmak için).</summary>
    public required string StoredPath { get; init; }

    /// <summary>Aynı dosya ikinci kez eklenmesin diye içeriğin SHA-256 özeti.</summary>
    public required string Sha256 { get; init; }

    /// <summary>PDF'te sayfa sayısı; Word/metinde 0 (sayfa bilgisi yok).</summary>
    public int PageCount { get; init; }

    public int ChunkCount { get; set; }

    public DateTime AddedAtUtc { get; init; } = DateTime.UtcNow;
}

public enum DocumentKind
{
    Pdf,
    Word,
    Text,

    /// <summary>Ultron'un araştırma modunda yazdığı rapor (Markdown).</summary>
    Research,

    /// <summary>PowerPoint sunumu (.pptx, Faz 17): her slayt bir "sayfa".</summary>
    Slides,
}

/// <summary>Belgenin aranabilir bir parçası (~900 karakter).</summary>
public sealed class DocumentChunk
{
    public long Id { get; init; }

    public long DocumentId { get; init; }

    /// <summary>1'den başlar; sayfa bilgisi olmayan belgelerde null.</summary>
    public int? Page { get; init; }

    /// <summary>Belge içindeki sıra (komşu parçaları bulmak için).</summary>
    public int Ordinal { get; init; }

    public required string Text { get; init; }

    public float[] Embedding { get; set; } = [];
}

/// <summary>Metin çıkarıcının ürettiği bir sayfa (ya da sayfasız belgede tek parça metin).</summary>
public sealed record DocumentPage(int? Page, string Text);

/// <summary>Aramada bulunan parça, belgesiyle birlikte.</summary>
public sealed record DocumentHit(Document Document, DocumentChunk Chunk, double Score);

/// <summary>Cevapta [n] numarasıyla anılan belge kaynağı.</summary>
public sealed record DocumentSource(int Number, long DocumentId, string FileName, string Collection, int? Page, string Text, string StoredPath)
{
    /// <summary>"Termo II.pdf · s. 12"</summary>
    public string Label => Page is { } p ? $"{FileName} · s. {p}" : FileName;
}
