using Ultron.Core.Documents;
using Ultron.Core.Text;

namespace Ultron.Tests.Fakes;

/// <summary>Bellek içi belge deposu; anahtar kelime araması FTS5'in basit taklidi (önekle başlayan kelime).</summary>
internal sealed class InMemoryDocumentStore : IDocumentStore
{
    private long _nextDocumentId = 1;
    private long _nextChunkId = 1;

    public List<Document> Documents { get; } = [];

    public List<DocumentChunk> Chunks { get; } = [];

    public Task<IReadOnlyList<Document>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Document>>(Documents.ToList());

    public Task<Document?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.FirstOrDefault(d => d.Id == id));

    public Task<Document?> FindBySha256Async(string sha256, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.FirstOrDefault(d => d.Sha256 == sha256));

    public Task<Document> AddAsync(Document document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var stored = new Document
        {
            Id = _nextDocumentId++, FileName = document.FileName, Collection = document.Collection, Kind = document.Kind,
            StoredPath = document.StoredPath, Sha256 = document.Sha256, PageCount = document.PageCount, ChunkCount = chunks.Count,
        };
        Documents.Add(stored);
        Chunks.AddRange(chunks.Select(c => new DocumentChunk { Id = _nextChunkId++, DocumentId = stored.Id, Page = c.Page, Ordinal = c.Ordinal, Text = c.Text, Embedding = c.Embedding }));
        return Task.FromResult(stored);
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        Documents.RemoveAll(d => d.Id == id);
        Chunks.RemoveAll(c => c.DocumentId == id);
        return Task.CompletedTask;
    }

    public Task SetCollectionAsync(long id, string collection, CancellationToken cancellationToken = default)
    {
        Documents.Single(d => d.Id == id).Collection = collection;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(IReadOnlyCollection<long>? documentIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DocumentChunk>>(Chunks.Where(c => documentIds is null || documentIds.Contains(c.DocumentId)).ToList());

    public Task<IReadOnlyList<DocumentChunk>> SearchKeywordsAsync(IReadOnlyList<string> prefixes, IReadOnlyCollection<long>? documentIds, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DocumentChunk>>(Chunks
            .Where(c => documentIds is null || documentIds.Contains(c.DocumentId))
            .Where(c => TurkishText.Normalize(c.Text).Split(' ').Any(w => prefixes.Any(w.StartsWith)))
            .Take(limit)
            .ToList());
}

/// <summary>Dosya içeriğini UTF-8 metin sayar; ".pdf" adında "\f" ile ayrılmış sayfalar.</summary>
internal sealed class FakeTextExtractor : IDocumentTextExtractor
{
    public DocumentKind? KindOf(string fileName) => Path.GetExtension(fileName) switch
    {
        ".pdf" => DocumentKind.Pdf,
        ".txt" or ".md" => DocumentKind.Text,
        _ => null,
    };

    public IReadOnlyList<DocumentPage> Extract(DocumentKind kind, byte[] content)
    {
        var text = System.Text.Encoding.UTF8.GetString(content);
        return kind == DocumentKind.Pdf
            ? text.Split('\f').Select((t, i) => new DocumentPage(i + 1, t)).ToList()
            : [new DocumentPage(null, text)];
    }
}
