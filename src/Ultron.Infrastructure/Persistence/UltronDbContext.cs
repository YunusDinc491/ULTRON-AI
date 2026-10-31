using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Ultron.Core.Conversations;
using Ultron.Core.Memories;
using Ultron.Core.Calendar;
using Ultron.Core.Coding;
using Ultron.Core.Documents;
using Ultron.Core.Reminders;
using Ultron.Core.Settings;
using Ultron.Core.Tools;
using Ultron.Core.Web;

namespace Ultron.Infrastructure.Persistence;

public sealed class UltronDbContext(DbContextOptions<UltronDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Memory> Memories => Set<Memory>();

    public DbSet<MemoryCategory> Categories => Set<MemoryCategory>();

    public DbSet<CategorySuggestion> CategorySuggestions => Set<CategorySuggestion>();

    public DbSet<WebCacheEntry> WebCache => Set<WebCacheEntry>();

    public DbSet<OutgoingQuery> OutgoingQueries => Set<OutgoingQuery>();

    public DbSet<Reminder> Reminders => Set<Reminder>();

    public DbSet<ReminderPreference> ReminderPreferences => Set<ReminderPreference>();

    public DbSet<ToolAuditEntry> ToolAudit => Set<ToolAuditEntry>();

    public DbSet<UserSetting> UserSettings => Set<UserSetting>();

    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    public DbSet<CodingJob> CodingJobs => Set<CodingJob>();

    public DbSet<Ultron.Core.Learning.AnswerReview> AnswerReviews => Set<Ultron.Core.Learning.AnswerReview>();

    public DbSet<Ultron.Core.Studio.StudioProject> StudioProjects => Set<Ultron.Core.Studio.StudioProject>();

    public DbSet<Ultron.Core.Studio.StudioVersion> StudioVersions => Set<Ultron.Core.Studio.StudioVersion>();

    public DbSet<Ultron.Core.Studio.StudioMessage> StudioMessages => Set<Ultron.Core.Studio.StudioMessage>();

    public DbSet<Ultron.Core.Studio.StudioRecipe> StudioRecipes => Set<Ultron.Core.Studio.StudioRecipe>();

    public DbSet<Ultron.Core.Studio.DesignRule> DesignRules => Set<Ultron.Core.Studio.DesignRule>();


    public DbSet<Ultron.Core.Lessons.LessonSession> LessonSessions => Set<Ultron.Core.Lessons.LessonSession>();

    public DbSet<Ultron.Core.Lessons.LessonMessage> LessonMessages => Set<Ultron.Core.Lessons.LessonMessage>();

    public DbSet<Ultron.Core.Lessons.LessonNote> LessonNotes => Set<Ultron.Core.Lessons.LessonNote>();

    public DbSet<Ultron.Core.Lessons.LessonMaterial> LessonMaterials => Set<Ultron.Core.Lessons.LessonMaterial>();

    public DbSet<Ultron.Core.Lessons.WeakTopic> WeakTopics => Set<Ultron.Core.Lessons.WeakTopic>();

    public DbSet<Ultron.Core.Growth.NightlyReport> NightlyReports => Set<Ultron.Core.Growth.NightlyReport>();

    public DbSet<Ultron.Core.Growth.QuizResult> QuizResults => Set<Ultron.Core.Growth.QuizResult>();

    public DbSet<Ultron.Core.Pro.ProAnswer> ProAnswers => Set<Ultron.Core.Pro.ProAnswer>();

    public DbSet<Ultron.Core.Sites.KnownSite> KnownSites => Set<Ultron.Core.Sites.KnownSite>();

    public DbSet<Ultron.Core.Sites.SitePage> SitePages => Set<Ultron.Core.Sites.SitePage>();

    public DbSet<Ultron.Core.Sites.SiteChunk> SiteChunks => Set<Ultron.Core.Sites.SiteChunk>();

    public DbSet<Ultron.Core.Library.LibraryPage> LibraryPages => Set<Ultron.Core.Library.LibraryPage>();

    public DbSet<Ultron.Core.Library.LibraryChunk> LibraryChunks => Set<Ultron.Core.Library.LibraryChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ULTRON PRO (Faz 19): Claude'un araştırıp yazdığı, Ultron'un hafızasına giren cevaplar.
        modelBuilder.Entity<Ultron.Core.Pro.ProAnswer>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Question).IsRequired().HasMaxLength(600);
            e.Property(a => a.Answer).IsRequired();
            e.Property(a => a.Lifetime).IsRequired().HasMaxLength(10);
            e.Property(a => a.Origin).IsRequired().HasMaxLength(10).HasDefaultValue("pro");
            e.Property(a => a.Embedding)
                .HasConversion(v => VectorBlob.ToBytes(v), b => VectorBlob.FromBytes(b))
                .Metadata.SetValueComparer(new ValueComparer<float[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.GetHashCode())),
                    v => v.ToArray()));
        });

        // Gelişim panosu (Faz 18): gece raporlarının geçmişi ve quiz puanları.
        modelBuilder.Entity<Ultron.Core.Growth.NightlyReport>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Area).IsRequired().HasMaxLength(20);
            e.Property(r => r.Summary).IsRequired();
        });
        modelBuilder.Entity<Ultron.Core.Growth.QuizResult>(e =>
        {
            e.HasKey(q => q.Id);
            e.HasIndex(q => q.AtUtc);
        });

        // Ders arayüzü (Faz 17): oturumlar, ders sohbeti, oturuma özel notlar, çalışma malzemeleri, zayıf konular.
        modelBuilder.Entity<Ultron.Core.Lessons.LessonSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).IsRequired().HasMaxLength(120);
            e.Property(s => s.Course).HasMaxLength(120);
            e.Property(s => s.ExamName).HasMaxLength(120);
            e.Ignore(s => s.Collection);
        });
        modelBuilder.Entity<Ultron.Core.Lessons.LessonMessage>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Content).IsRequired();
            e.Property(m => m.Author).IsRequired().HasMaxLength(10);
            e.HasIndex(m => new { m.SessionId, m.Id });
            e.HasIndex(m => m.ReviewedAtUtc);
        });
        modelBuilder.Entity<Ultron.Core.Lessons.LessonNote>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Text).IsRequired().HasMaxLength(1000);
            e.Property(n => n.Source).IsRequired().HasMaxLength(40);
            e.HasIndex(n => n.SessionId);
        });
        modelBuilder.Entity<Ultron.Core.Lessons.LessonMaterial>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Kind).IsRequired().HasMaxLength(10);
            e.Property(m => m.Title).IsRequired().HasMaxLength(200);
            e.Property(m => m.Content).IsRequired();
            e.HasIndex(m => m.SessionId);
        });
        modelBuilder.Entity<Ultron.Core.Lessons.WeakTopic>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Topic).IsRequired().HasMaxLength(200);
            e.Ignore(t => t.IsWeak);
            e.HasIndex(t => t.SessionId);
        });

        // İş arayüzü (Faz 16): projeler, sürümler, iş sohbeti, tarif defteri, zevk kuralları.
        modelBuilder.Entity<Ultron.Core.Studio.StudioProject>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(120);
        });
        modelBuilder.Entity<Ultron.Core.Studio.StudioVersion>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Request).IsRequired();
            e.Property(v => v.Code).IsRequired();
            e.Property(v => v.Source).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(v => new { v.ProjectId, v.Number });
            e.HasIndex(v => v.ReviewedAtUtc);
        });
        modelBuilder.Entity<Ultron.Core.Studio.StudioMessage>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Content).IsRequired();
            e.HasIndex(m => new { m.ProjectId, m.Id });
        });
        modelBuilder.Entity<Ultron.Core.Studio.StudioRecipe>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Slug).IsRequired().HasMaxLength(120);
            e.HasIndex(r => r.Slug);
            e.Property(r => r.Title).IsRequired().HasMaxLength(200);
            e.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.ContentHash).HasMaxLength(64);
            e.Property(r => r.Embedding)
                .HasConversion(v => VectorBlob.ToBytes(v), b => VectorBlob.FromBytes(b))
                .Metadata.SetValueComparer(new ValueComparer<float[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.GetHashCode())),
                    v => v.ToArray()));
        });
        modelBuilder.Entity<Ultron.Core.Studio.DesignRule>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Text).IsRequired().HasMaxLength(400);
            e.Property(r => r.Source).IsRequired().HasMaxLength(40);
        });

        modelBuilder.Entity<Ultron.Core.Learning.AnswerReview>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Question).IsRequired();
            e.Property(r => r.Answer).IsRequired();
            e.Property(r => r.Verdict).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Decision).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Embedding)
                .HasConversion(v => VectorBlob.ToBytes(v), b => VectorBlob.FromBytes(b))
                .Metadata.SetValueComparer(new ValueComparer<float[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.GetHashCode())),
                    v => v.ToArray()));
            e.Ignore(r => r.IsPendingSuggestion);
            e.HasIndex(r => r.AuditedAtUtc);
            e.HasIndex(r => r.Decision);
        });

        modelBuilder.Entity<CodingJob>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.Title).IsRequired().HasMaxLength(100);
            e.Property(j => j.Folder).IsRequired().HasMaxLength(500);
            e.Property(j => j.Request).IsRequired();
            e.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(j => j.SessionId).HasMaxLength(80);
            e.Property(j => j.Progress).HasMaxLength(300);
            e.Ignore(j => j.IsActive);
        });

        modelBuilder.Entity<Document>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.FileName).IsRequired().HasMaxLength(260);
            e.Property(d => d.Collection).IsRequired().HasMaxLength(60);
            e.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.StoredPath).IsRequired().HasMaxLength(500);
            e.Property(d => d.Sha256).IsRequired().HasMaxLength(64);
            e.HasIndex(d => d.Sha256);
            e.HasIndex(d => d.Collection);
        });

        // Yazılım Kütüphanesi (Faz 15): kişisel belgelerden ayrı tablolar; kelime araması LibrarySearch (FTS5) tablosunda.
        // Bildiğim siteler (Faz 20): takip edilen siteler, sayfaları ve vektörlü parçaları.
        modelBuilder.Entity<Ultron.Core.Sites.KnownSite>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.Property(s => s.Url).IsRequired().HasMaxLength(500);
            e.Property(s => s.Purpose).HasMaxLength(300);
        });
        modelBuilder.Entity<Ultron.Core.Sites.SitePage>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Url).IsRequired().HasMaxLength(1000);
            e.Property(p => p.Title).IsRequired().HasMaxLength(300);
            e.Property(p => p.ContentHash).IsRequired().HasMaxLength(64);
            e.HasOne<Ultron.Core.Sites.KnownSite>().WithMany().HasForeignKey(p => p.SiteId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.SiteId, p.Url }).IsUnique();
        });
        modelBuilder.Entity<Ultron.Core.Sites.SiteChunk>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Text).IsRequired();
            e.Property(c => c.Embedding)
                .HasConversion(v => VectorBlob.ToBytes(v), b => VectorBlob.FromBytes(b))
                .Metadata.SetValueComparer(new ValueComparer<float[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.GetHashCode())),
                    v => v.ToArray()));
            e.HasOne<Ultron.Core.Sites.SitePage>().WithMany().HasForeignKey(c => c.PageId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.PageId);
        });

        modelBuilder.Entity<Ultron.Core.Library.LibraryPage>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.SourceId).HasMaxLength(40).IsRequired();
            e.Property(p => p.Path).IsRequired();
            e.HasIndex(p => new { p.SourceId, p.Path }).IsUnique();
        });
        modelBuilder.Entity<Ultron.Core.Library.LibraryChunk>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Text).IsRequired();
            e.Property(c => c.Embedding)
                .HasConversion(v => VectorBlob.ToBytes(v), b => VectorBlob.FromBytes(b))
                .Metadata.SetValueComparer(new ValueComparer<float[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.GetHashCode())),
                    v => v.ToArray()));
            e.HasOne<Ultron.Core.Library.LibraryPage>().WithMany().HasForeignKey(c => c.PageId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.PageId);
        });

        modelBuilder.Entity<DocumentChunk>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Text).IsRequired();
            e.Property(c => c.Embedding)
                .HasConversion(v => VectorBlob.ToBytes(v), b => VectorBlob.FromBytes(b))
                .Metadata.SetValueComparer(new ValueComparer<float[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.GetHashCode())),
                    v => v.ToArray()));
            e.HasOne<Document>().WithMany().HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.DocumentId, c.Ordinal });
        });

        modelBuilder.Entity<UserSetting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(60);
            e.Property(s => s.Value).HasMaxLength(200);
        });

        modelBuilder.Entity<CalendarEvent>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Title).IsRequired().HasMaxLength(200);
            e.Property(c => c.Location).HasMaxLength(100);
            e.Property(c => c.Instructor).HasMaxLength(100);
            e.Property(c => c.Day).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(c => c.Day);
        });

        modelBuilder.Entity<Reminder>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Text).IsRequired().HasMaxLength(500);
            e.Property(r => r.Recurrence).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            // Eski kayıtlar için migration'da varsayılan "Onemsiz" (elle ayarlandı).
            e.Property(r => r.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.SpokenText).HasMaxLength(500);
            e.HasIndex(r => new { r.Status, r.DueAtUtc });
        });

        modelBuilder.Entity<ReminderPreference>(e =>
        {
            e.HasKey(p => p.Key);
            e.Property(p => p.Key).HasMaxLength(200);
            e.Property(p => p.Text).HasMaxLength(500);
            e.Property(p => p.Priority).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<ToolAuditEntry>(e =>
        {
            e.ToTable("ToolAudit");
            e.HasKey(a => a.Id);
            e.Property(a => a.Tool).HasMaxLength(60);
            e.Property(a => a.Arguments).HasMaxLength(2000);
            e.Property(a => a.Result).HasMaxLength(1000);
            e.Property(a => a.Level).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Approval).HasMaxLength(40);
            e.HasIndex(a => a.AtUtc);
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Title).HasMaxLength(200);
            e.HasIndex(c => c.StartedAtUtc);
            e.HasMany(c => c.Messages)
                .WithOne()
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.HasKey(m => m.Id);
            // Enum'u sayı yerine metin olarak sakla: DB'ye bakınca "User", "Assistant" okunur.
            e.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.Content).IsRequired();
            e.HasIndex(m => new { m.ConversationId, m.Id });
        });

        modelBuilder.Entity<Memory>(e =>
        {
            e.HasKey(m => m.Id);
            // Kategori anahtarı metin (eskiden enum adıydı; değerler aynı kaldığı için eski kayıtlar bozulmadı).
            e.Property(m => m.Category).HasMaxLength(60);
            e.Property(m => m.Content).IsRequired().HasMaxLength(1000);
            // float[] → BLOB: 768 boyutlu vektör = 3 KB. Karşılaştırıcı, EF'in dizideki değişikliği fark etmesi için.
            e.Property(m => m.Embedding)
                .HasConversion(v => VectorBlob.ToBytes(v), b => VectorBlob.FromBytes(b))
                .Metadata.SetValueComparer(new ValueComparer<float[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.GetHashCode())),
                    v => v.ToArray()));
            e.HasIndex(m => m.IsActive);
        });

        modelBuilder.Entity<MemoryCategory>(e =>
        {
            e.ToTable("Categories");
            e.HasKey(c => c.Key);
            e.Property(c => c.Key).HasMaxLength(60);
            e.Property(c => c.Name).HasMaxLength(60);
            e.Property(c => c.Description).HasMaxLength(300);
            e.Property(c => c.Drawer).HasMaxLength(20).HasDefaultValue(MemoryDrawers.Patron);
            // Yerleşik kategoriler ilk kurulumda (migration ile) eklenir.
            e.HasData(MemoryCategories.BuiltIn);
        });

        modelBuilder.Entity<CategorySuggestion>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(60);
            e.Property(s => s.Description).HasMaxLength(300);
            e.Property(s => s.Example).HasMaxLength(500);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<WebCacheEntry>(e =>
        {
            e.ToTable("WebCache");
            e.HasKey(w => w.Id);
            e.Property(w => w.CacheKey).HasMaxLength(200);
            e.Property(w => w.Query).HasMaxLength(500);
            e.Property(w => w.Freshness).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(w => w.CacheKey);
        });

        modelBuilder.Entity<OutgoingQuery>(e =>
        {
            e.HasKey(q => q.Id);
            e.Property(q => q.Query).HasMaxLength(500);
            e.HasIndex(q => q.CreatedAtUtc);
        });
    }
}

internal static class VectorBlob
{
    public static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
}
