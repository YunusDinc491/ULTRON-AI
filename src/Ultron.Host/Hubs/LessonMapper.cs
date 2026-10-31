using Microsoft.AspNetCore.SignalR;
using Ultron.Contracts;
using Ultron.Core.Lessons;

namespace Ultron.Host.Hubs;

public static class LessonMapper
{
    public static LessonSessionDto ToDto(LessonSession s) =>
        new(s.Id, s.Name, s.Course, s.Instructions, s.ExamName, s.ExamAtUtc?.ToLocalTime(), s.UpdatedAtUtc.ToLocalTime());

    public static LessonMessageDto ToDto(LessonMessage m) =>
        new(m.Id, m.FromUser, m.Content, m.Sources?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [], m.Author, m.Unsure, m.CreatedAtUtc.ToLocalTime());

    public static LessonNoteDto ToDto(LessonNote n) => new(n.Id, n.Text, n.Source, n.CreatedAtUtc.ToLocalTime());

    public static LessonMaterialDto ToDto(LessonMaterial m) =>
        new(m.Id, m.Kind, m.Title, m.Kind == MaterialKinds.Quiz ? "" : m.Content, m.UpdatedAtUtc.ToLocalTime(),
            m.Kind == MaterialKinds.Quiz ? LessonService.ReadQuiz(m).Select(q => new QuizQuestionDto(q.Type, q.Question, q.Choices, q.Unit, q.Topic)).ToList() : null);

    public static WeakTopicDto ToDto(WeakTopic t) => new(t.Topic, t.Misses, t.Hits, t.IsWeak);

    public static QuizGradeDto ToDto(QuizGrade g) =>
        new(g.Correct, g.Total, g.Results.Select(r => new QuizResultDto(r.Index, r.Correct, r.Feedback)).ToList(), g.WeakTopics);

    public static LessonEventDto ToDto(LessonEvent e) => e switch
    {
        LessonStarted s => new LessonEventDto("started", Sources: s.Sources, Solving: s.Solving),
        LessonChunk c => new LessonEventDto("chunk", c.Text),
        LessonCompleted c => new LessonEventDto("completed", Message: ToDto(c.Message)),
        LessonFailed f => new LessonEventDto("failed", f.Message),
        _ => new LessonEventDto("unknown"),
    };
}

/// <summary>Claude Code'un ders sorusu işi: masaüstündeki Ders arayüzüne.</summary>
public sealed class HubLessonNotifier(IHubContext<ChatHub> hub) : ILessonNotifier
{
    public Task ClaudeProgressAsync(long sessionId, string status, bool finished, LessonMessage? message, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync(ChatHubContract.LessonUpdate, new LessonUpdateDto(sessionId, status, finished, message is null ? null : LessonMapper.ToDto(message)), cancellationToken);
}
