using System.Text;

namespace Ultron.Core.Studio;

/// <summary>İş modunun talimatları: kurallar, tasarım sistemi (ölçü kapları) ve kullanıcının zevk kuralları.</summary>
public static class StudioPrompts
{
    /// <summary>
    /// Tasarım sistemi: bütün bileşenler aynı ölçülerle yazılsın (tarif defteri de buna göre). kullanıcının zevk kuralları bunun
    /// üstüne yazılır ve çelişirse onlar geçerli.
    /// </summary>
    public const string DesignSystem = """
        - Ana renk indigo (düğme bg-indigo-600 hover:bg-indigo-500, vurgu text-indigo-600); nötr tonlar slate (metin text-slate-900 / text-slate-600, kenarlık border-slate-200, zemin bg-white ya da bg-slate-50).
        - Köşeler: düğme ve girdi rounded-lg, kart ve modal rounded-2xl, rozet rounded-full.
        - Gölge: kartlarda shadow-sm, üzerine gelince hover:shadow-md; açılır menü ve modalda shadow-lg.
        - Boşluk 4'ün katları (p-2, p-4, p-6, gap-4); sayfa kenarı px-4 sm:px-6 lg:px-8, içerik max-w-7xl mx-auto.
        - Yazı: başlık font-semibold tracking-tight text-slate-900; gövde text-sm/text-base text-slate-600; düğme text-sm font-medium.
        - Hareket: transition duration-200; odak focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2.
        - Koyu tema istenirse dark: sınıfları (dark:bg-slate-900 dark:text-slate-100) eklenir; önizleme "koyu" düğmesiyle dener.
        """;

    public static string System(IReadOnlyList<DesignRule> rules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sen Ultron'un İş modundasın: kullanıcı için React + Tailwind CSS ile web bileşeni yazan titiz bir ön yüz ustasısın.");
        sb.AppendLine("Cevap biçimi: önce TEK bir ```jsx kod bloğu (bileşenin TAMAMI, eksiksiz), sonra en fazla 2 cümle Türkçe açıklama. Başka kod bloğu yazma.");
        sb.AppendLine("Kod kuralları:");
        sb.AppendLine("1. Sadece `import React, { useState, useEffect, useRef } from 'react';` (gerekenler). Başka kütüphane YOK; ikonlar satır içi <svg>.");
        sb.AppendLine("2. Tek dosya. Yardımcı bileşenler aynı dosyada; en sonda `export default function BilesenAdi() { … }` olsun.");
        sb.AppendLine("3. Stil sadece Tailwind v3 sınıfları; <style> ya da CSS dosyası yok.");
        sb.AppendLine("4. Mobil öncelikli ve duyarlı (sm: md: lg:); 375 px genişlikte yatay taşma olmasın; mobilde menüler hamburgerle açılsın.");
        sb.AppendLine("5. Erişilebilir: düğme ve bağlantılarda anlaşılır metin ya da aria-label; açılır menüde aria-expanded ve aria-controls; görselde alt; klavyeyle odak görünür.");
        sb.AppendLine("6. Örnek içerik Türkçe ve gerçekçi. Dış görsel adresi kullanma (önizleme çevrimdışı): görsel yerine renkli kutu ya da gradyan.");
        sb.AppendLine("7. Değişiklik isteğinde mevcut kodu temel al, sadece isteneni değiştir ama kodun TAMAMINI yeniden yaz.");
        sb.AppendLine("8. Tarif defterinden örnek verildiyse yapısını ve kalitesini koru, isteğe göre uyarla.");
        sb.AppendLine().AppendLine("Tasarım sistemi (aksi istenmedikçe):").AppendLine(DesignSystem);
        var active = rules.Where(r => r.IsActive).ToList();
        if (active.Count > 0)
        {
            sb.AppendLine().AppendLine("Kullanıcının zevk kuralları (HER ZAMAN uygula; tasarım sistemiyle çelişirse bunlar geçerli):");
            foreach (var r in active)
                sb.AppendLine("- " + r.Text);
        }
        return sb.ToString().TrimEnd();
    }

    public static string User(string request, IReadOnlyList<StudioRecipe> recipes, StudioVersion? current, string? reference)
    {
        var sb = new StringBuilder();
        foreach (var r in recipes)
        {
            sb.AppendLine($"Tarif defterinden örnek ({KindLabel(r.Kind)}): {r.Title} — {r.Description}");
            if (r.Code.Length > 0)
                sb.AppendLine("```jsx").AppendLine(r.Code).AppendLine("```");
            sb.AppendLine();
        }
        if (reference is { Length: > 0 })
            sb.AppendLine("Ek referans (Flowbite, HTML; sadece fikir için, React'e çevir):").AppendLine(reference).AppendLine();
        if (current is not null)
            sb.AppendLine($"Mevcut bileşen (sürüm {current.Number}):").AppendLine("```jsx").AppendLine(current.Code).AppendLine("```").AppendLine();
        sb.Append(current is null ? "İstek: " : "Değişiklik isteği: ").Append(request);
        return sb.ToString();
    }

    public static string FixRequest(string report) =>
        "Önizlemedeki otomatik kontrol şu sorunları buldu, düzelt (başka şeyi değiştirme):\n" + report;

    private static string KindLabel(RecipeKind kind) => kind switch
    {
        RecipeKind.Favori => "Kullanıcının beğendiği",
        RecipeKind.Claude => "Claude'un yaptığı",
        RecipeKind.Referans => "Kullanıcının referansı",
        RecipeKind.Denetim => "denetimde düzeltilmiş",
        _ => "hazır",
    };
}
