namespace Ultron.Core.Pro;

/// <summary>Ultron Pro'nun bağlı hesabı: hazır mı (kurulu ve giriş yapılmış), hangisi, değilse ne yapmalı.</summary>
public sealed record ProAgentStatus(bool Ready, string Provider, string Message);

public sealed record ProAgentResult(bool Ok, string Text, bool LimitReached = false, string? Error = null);

/// <summary>
/// Ultron Pro'nun araştırmacısı (2026-10-31, kullanıcı: "Pro için Claude, Codex ya da Gemini hesabına bağlaması gereksin").
/// Soruyu internette araştırıp tek bir metin cevap döner; sadece okur, dosya değiştirmez. Hangi hesabın kullanılacağı ayardan
/// (pro_hesap: otomatik, claude, codex, gemini); otomatikte kurulu ve girişli ilk araç.
/// </summary>
public interface IProResearcher
{
    Task<ProAgentStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <param name="folder">Çalışma klasörü (araç burada çalışır, cevap buraya da yazılır).</param>
    Task<ProAgentResult> RunAsync(string folder, string prompt, Action<string> progress, CancellationToken cancellationToken = default);
}
