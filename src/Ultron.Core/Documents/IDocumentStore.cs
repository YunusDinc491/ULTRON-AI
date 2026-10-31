namespace Ultron.Core.Documents;

/// <summary>Belgeler ve parçaları. Anahtar kelime araması için parçalar ayrıca FTS5 tablosunda tutulur.</summary>
public interface IDocumentStore
{
    Task<IReadOnlyList<Document>> ListAsync(CancellationToken cancellationToken = default);

    Task<Document?> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<Document?> FindBySha256Async(string sha256, CancellationToken cancellationToken = default);

    /// <summary>Belgeyi parçalarıyla birlikte tek seferde kaydeder (yarım belge kalmasın).</summary>
    Task<Document> AddAsync(Document document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task SetCollectionAsync(long id, string collection, CancellationToken cancellationToken = default);

    /// <summary>Anlamsal arama için parçalar (embedding'leriyle). <paramref name="documentIds"/> null ise hepsi.</summary>
    Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(IReadOnlyCollection<long>? documentIds, CancellationToken cancellationToken = default);

    /// <summary>FTS5 öneki araması; en alakalı önce.</summary>
    Task<IReadOnlyList<DocumentChunk>> SearchKeywordsAsync(IReadOnlyList<string> prefixes, IReadOnlyCollection<long>? documentIds, int limit, CancellationToken cancellationToken = default);
}

/// <summary>Dosyadan sayfa sayfa düz metin çıkarır (PDF, Word, metin).</summary>
public interface IDocumentTextExtractor
{
    /// <summary>Uzantıya göre tür; desteklenmiyorsa null.</summary>
    DocumentKind? KindOf(string fileName);

    IReadOnlyList<DocumentPage> Extract(DocumentKind kind, byte[] content);
}
