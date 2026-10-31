using System.Text.Json;
using Ultron.Core.Tools;

namespace Ultron.Core.Library;

/// <summary>"Kütüphane ne durumda", "kütüphanede hangi kaynaklar var".</summary>
public sealed class LibraryStatusTool(LibraryService library) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "kutuphane_durumu",
        "Yazılım Kütüphanesi'nin durumunu gösterir: kaynaklar (MDN, React, Flutter…), sayfa sayısı, son güncelleme, şu an güncelleniyorsa ilerleme.",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default) =>
        ToolResult.Success($"Yazılım Kütüphanesi: {await library.DescribeAsync(cancellationToken)}\nKaynaklar: " +
            string.Join(", ", library.Sources.Select(s => s.Name)) + ". Haftada bir (pazar 03:30) kendiliğinden güncellenir.");
}

/// <summary>"Kütüphaneyi şimdi güncelle": arka planda başlar (uzun sürebilir), cevap beklemez.</summary>
public sealed class LibraryUpdateTool(LibraryService library) : ITool
{
    public ToolDefinition Definition { get; } = new(
        "kutuphane_guncelle",
        "Yazılım Kütüphanesi'ni şimdi günceller (kaynakların yeni sürümlerini indirir, sadece değişen sayfaları işler). Arka planda çalışır.",
        """{"type":"object","properties":{}}""");

    public PermissionLevel Level => PermissionLevel.Serbest;

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (library.IsSyncing)
            return Task.FromResult(ToolResult.Success("Kütüphane zaten güncelleniyor: " + library.Progress));
        _ = Task.Run(() => library.SyncAllAsync(CancellationToken.None), CancellationToken.None);
        return Task.FromResult(ToolResult.Success("Kütüphane güncellemesi arka planda başladı; ilerlemeyi /durum'da görebilirsin. İlk indirme uzun sürer (birkaç saat)."));
    }
}
