using Ultron.Contracts;
using Ultron.Core.Chat;
using Ultron.Core.Documents;
using Ultron.Core.Memories;
using Ultron.Core.Web;

namespace Ultron.Host.Hubs;

/// <summary>Core'daki olayları tel üzerindeki sözleşme biçimine (Ultron.Contracts) çevirir.</summary>
public static class ChatEventMapper
{
    public static ChatEventDto ToDto(ChatEvent e) => e switch
    {
        IntentDetected d => new(ChatEventTypes.Intent, Intent: d.Intent.ToString(), Detailed: d.Detailed),
        MemoriesRecalled { Recall: var r } => new(ChatEventTypes.Memories,
            CoreMemories: r.Core.Select(ToDto).ToList(),
            RelevantMemories: r.Relevant.Select(ToDto).ToList()),
        WebPrepared { Context: var w } => new(ChatEventTypes.Web, FromCache: w.FromCache, Query: w.Plan.Query),
        WebUnavailable u => new(ChatEventTypes.WebUnavailable, Text: u.Reason),
        AnswerChunk c => new(ChatEventTypes.Chunk, Text: c.Text),
        ToolsUsed t => new(ChatEventTypes.Tools, Tools: t.Uses.Select(u => new ToolUseDto(u.Name, u.Ok)).ToList()),
        SourcesListed s => new(ChatEventTypes.Sources, Sources: s.Sources.Select(ToDto).ToList()),
        DocumentsSearched d => new(ChatEventTypes.Documents, Query: d.Context.Scope, Text: d.Context.Sources.Count.ToString()),
        LibrarySearched l => new(ChatEventTypes.Library, Text: l.Count.ToString()),
        ImageAttached i => new(ChatEventTypes.Image, FromScreen: i.FromScreen),
        ResearchProgress p => new(ChatEventTypes.Research, Text: p.Step),
        TierProgress t => new(ChatEventTypes.Progress, Text: t.Step, Query: t.Tier),
        ResearchSaved r => new(ChatEventTypes.ResearchSaved, Text: r.Document?.FileName),
        DocumentSourcesListed s => new(ChatEventTypes.Sources, Sources: s.Sources.Select(ToDto).ToList()),
        AnswerCompleted a => new(ChatEventTypes.Completed, Text: a.Text, MessageId: a.MessageId),
        FollowUpsSuggested f => new(ChatEventTypes.FollowUps,
            FollowUps: f.Questions
                .Select(q => new FollowUpDto(q.Question, q.Options.Select(o => new FollowUpOptionDto(o.Label, o.Answer)).ToList()))
                .ToList()),
        _ => throw new ArgumentOutOfRangeException(nameof(e), e.GetType().Name, "Bilinmeyen olay"),
    };

    private static MemoryDto ToDto(Memory m) => new(m.Id, m.Category.ToString(), m.Content);

    private static SourceDto ToDto(WebSource s) => new(s.Number, s.Title, s.Url);

    // Url: PDF görüntüleyicide sayfayı açan dosya adresi ("file:///C:/.../Hafta 5.pdf#page=12").
    private static SourceDto ToDto(DocumentSource s) =>
        new(s.Number, s.Label, new Uri(s.StoredPath).AbsoluteUri + (s.Page is { } p ? $"#page={p}" : ""), s.StoredPath, s.Page);
}
