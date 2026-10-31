using System.Runtime.CompilerServices;
using Ultron.Core.Chat;
using Ultron.Core.Embeddings;
using Ultron.Core.Memories;
using Ultron.Core.Text;

namespace Ultron.Tests.Fakes;

/// <summary>CompleteJsonAsync çağrılarına sırayla önceden verilmiş JSON'ları döndürür; gelen istekleri kaydeder.</summary>
internal sealed class FakeChatModel(params string[] jsonResponses) : IChatModel
{
    private readonly Queue<string> _responses = new(jsonResponses);

    public List<IReadOnlyList<ChatMessage>> JsonRequests { get; } = [];

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return "tamam";
    }

    public Task<string> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, string jsonSchema, CancellationToken cancellationToken = default)
    {
        JsonRequests.Add(messages);
        return Task.FromResult(_responses.Dequeue());
    }

    public Task<Ultron.Core.Tools.ToolTurn> CompleteWithToolsAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<Ultron.Core.Tools.ToolDefinition> tools, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Ultron.Core.Tools.ToolTurn("tamam", []));
}

/// <summary>Metne göre önceden belirlenmiş vektör döndürür; tanımsız metne sıfır olmayan sabit bir vektör.</summary>
internal sealed class FakeEmbeddingModel(Dictionary<string, float[]> vectors) : IEmbeddingModel
{
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => vectors.TryGetValue(t, out var v) ? v : [0f, 0f, 1f]).ToList());
}

/// <summary>Yerleşik kategoriyle (Sağlık) başlayan bellek içi kategori deposu.</summary>
internal sealed class InMemoryCategoryStore : ICategoryStore
{
    public List<MemoryCategory> All { get; } = MemoryCategories.BuiltIn
        .Select(c => new MemoryCategory { Key = c.Key, Name = c.Name, Description = c.Description, Drawer = c.Drawer, IsCore = c.IsCore, IsBuiltIn = true, SortOrder = c.SortOrder })
        .ToList();

    public List<CategorySuggestion> Suggestions { get; } = [];

    public Task<IReadOnlyList<MemoryCategory>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MemoryCategory>>(All.ToList());

    public Task<MemoryCategory> AddAsync(string name, string description, string drawer, CancellationToken cancellationToken = default)
    {
        // Gerçek depo gibi: aynı adda kategori varsa yenisi açılmaz.
        if (All.FirstOrDefault(x => TurkishText.Normalize(x.Name) == TurkishText.Normalize(name)) is { } existing)
            return Task.FromResult(existing);
        var c = new MemoryCategory { Key = name.Replace(" ", ""), Name = name, Description = description, Drawer = drawer, SortOrder = All.Count + 1 };
        All.Add(c);
        return Task.FromResult(c);
    }

    public Task UpdateAsync(string key, string name, string description, bool isCore, CancellationToken cancellationToken = default)
    {
        var c = All.Single(x => x.Key == key);
        c.Name = name;
        c.Description = description;
        c.IsCore = isCore;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteIfEmptyAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(All.RemoveAll(x => x.Key == key && !x.IsBuiltIn) > 0);

    public Task<IReadOnlyList<CategorySuggestion>> GetPendingSuggestionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CategorySuggestion>>(Suggestions.Where(s => s.Status == SuggestionStatus.Pending).ToList());

    public Task AddSuggestionAsync(string name, string description, string? example, CancellationToken cancellationToken = default)
    {
        if (!Suggestions.Any(s => s.Name == name) && !All.Any(c => c.Name == name))
            Suggestions.Add(new CategorySuggestion { Id = Suggestions.Count + 1, Name = name, Description = description, Example = example });
        return Task.CompletedTask;
    }

    public async Task<MemoryCategory?> ResolveSuggestionAsync(long id, bool accept, CancellationToken cancellationToken = default)
    {
        var s = Suggestions.Single(x => x.Id == id);
        s.Status = accept ? SuggestionStatus.Accepted : SuggestionStatus.Rejected;
        return accept ? await AddAsync(s.Name, s.Description, MemoryDrawers.Patron, cancellationToken) : null;
    }
}

internal sealed class InMemoryMemoryStore : IMemoryStore
{
    private long _nextId = 1;

    public List<Memory> All { get; } = [];

    public Task<IReadOnlyList<Memory>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Memory>>(All.Where(m => m.IsActive).ToList());

    /// <summary>FTS5'in basit taklidi: normalleştirilmiş metinde önekle başlayan kelime var mı.</summary>
    public Task<IReadOnlyList<Memory>> SearchKeywordsAsync(IReadOnlyList<string> prefixes, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Memory>>(All
            .Where(m => m.IsActive && TurkishText.Normalize(m.Content).Split(' ').Any(w => prefixes.Any(w.StartsWith)))
            .Take(limit)
            .ToList());

    public Task<Memory> AddAsync(Memory memory, CancellationToken cancellationToken = default)
    {
        var stored = new Memory
        {
            Id = _nextId++,
            Category = memory.Category,
            Content = memory.Content,
            Embedding = memory.Embedding,
            SourceMessageId = memory.SourceMessageId,
        };
        All.Add(stored);
        return Task.FromResult(stored);
    }

    public Task UpdateAsync(long id, string content, string category, float[] embedding, CancellationToken cancellationToken = default)
    {
        var m = All.Single(x => x.Id == id);
        m.Content = content;
        m.Category = category;
        m.Embedding = embedding;
        return Task.CompletedTask;
    }

    public Task DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        All.Single(x => x.Id == id).IsActive = false;
        return Task.CompletedTask;
    }

    public Task ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        All.Single(x => x.Id == id).IsActive = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Memory>> GetRecentlyDeactivatedAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Memory>>(All.Where(m => !m.IsActive).Reverse().Take(limit).ToList());
}

/// <summary>Whisper/Piper yerine: gelen sesin boyutunu yazar, metni bayt olarak geri verir.</summary>
internal sealed class FakeVoice : Ultron.Core.Voice.ISpeechToText, Ultron.Core.Voice.ITextToSpeech
{
    public bool WarmedUp { get; private set; }

    public Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellationToken = default) =>
        Task.FromResult($"{wav.Length} bayt");

    public Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        WarmedUp = true;
        return Task.CompletedTask;
    }

    public Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(System.Text.Encoding.UTF8.GetBytes(text));
}

/// <summary>Bellek içi kütüphane: kelime araması "metin terimi içeriyor mu", vektörler parçanın kendi vektörü.</summary>
internal sealed class InMemoryLibraryStore : Ultron.Core.Library.ILibraryStore
{
    public List<(Ultron.Core.Library.LibraryChunk Chunk, Ultron.Core.Library.LibraryPage Page)> Rows { get; } = [];

    public void Add(string title, string url, string text, float[] embedding)
    {
        var page = new Ultron.Core.Library.LibraryPage { Id = Rows.Count + 1, SourceId = "react", Path = title, Title = title, Url = url, Version = "v", ContentHash = "h" };
        Rows.Add((new Ultron.Core.Library.LibraryChunk { Id = Rows.Count + 1, PageId = page.Id, Text = text, Embedding = embedding }, page));
    }

    public Task<IReadOnlyDictionary<string, Ultron.Core.Library.LibraryPage>> GetPagesAsync(string sourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, Ultron.Core.Library.LibraryPage>>(Rows.Select(r => r.Page).ToDictionary(p => p.Path));

    public Task<Ultron.Core.Library.LibraryPage> SavePageAsync(Ultron.Core.Library.LibraryPage page, IReadOnlyList<Ultron.Core.Library.LibraryChunk> chunks, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task TouchPagesAsync(IReadOnlyCollection<long> pageIds, string version, DateTime nowUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task MarkStaleAsync(IReadOnlyCollection<long> pageIds, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<(long ChunkId, float[] Embedding)>> GetEmbeddingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<(long, float[])>>(Rows.Select(r => (r.Chunk.Id, r.Chunk.Embedding)).ToList());

    public Task<IReadOnlyList<(Ultron.Core.Library.LibraryChunk Chunk, Ultron.Core.Library.LibraryPage Page)>> GetChunksAsync(IReadOnlyCollection<long> chunkIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<(Ultron.Core.Library.LibraryChunk, Ultron.Core.Library.LibraryPage)>>(Rows.Where(r => chunkIds.Contains(r.Chunk.Id)).ToList());

    public Task<IReadOnlyList<long>> SearchKeywordsAsync(IReadOnlyList<string> terms, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<long>>(Rows.Where(r => terms.Any(t => r.Chunk.Text.Contains(t, StringComparison.OrdinalIgnoreCase))).Select(r => r.Chunk.Id).Take(limit).ToList());

    public Task<Ultron.Core.Library.LibraryStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new Ultron.Core.Library.LibraryStats(1, Rows.Count, Rows.Count, null));

    public Task<IReadOnlyList<long>> SearchTitlesAsync(string name, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<long>>(Rows.Where(r => r.Page.Title.StartsWith(name, StringComparison.OrdinalIgnoreCase)).Select(r => r.Chunk.Id).Take(limit).ToList());
}
