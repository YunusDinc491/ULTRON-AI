namespace Ultron.Core.Library;

/// <summary>
/// Kaynak listesi (Faz 15, tasarım kararı: çekirdek web + mobil). Hepsi resmi dokümantasyonun açık deposu; 2026-10-28'de
/// GitHub'da depo, dal ve klasör doğrulandı. Android'in resmi dokümantasyonu (developer.android.com) açık depoda olmadığı için
/// yok; Android tarafını Kotlin dokümantasyonu karşılar.
/// Liste ilk açılışta %USERPROFILE%\Ultron\kutuphane\kutuphane-kaynaklari.json olarak yazılır; oradan düzenlenebilir.
/// Sonradan eklenen varsayılan kaynaklar dosyaya kendiliğinden eklenir; bir kaynağı kapatmak için dosyada "paths": [] yapılır.
/// </summary>
public static class LibrarySources
{
    public static IReadOnlyList<LibrarySource> Default { get; } =
    [
        new("mdn", "MDN (HTML, CSS, JavaScript, HTTP, Web API)", "web", "mdn/content", "main",
            ["files/en-us/web/html", "files/en-us/web/css", "files/en-us/web/javascript", "files/en-us/web/http", "files/en-us/web/api"],
            [".md"], "CC-BY-SA-2.5", "https://developer.mozilla.org/en-US/docs/{path}", "files/en-us"),
        new("typescript", "TypeScript el kitabı", "web", "microsoft/TypeScript-Website", "v2",
            ["packages/documentation/copy/en"], [".md"], "CC-BY-4.0"),
        new("react", "React (react.dev)", "web", "reactjs/react.dev", "main",
            ["src/content/learn", "src/content/reference"], [".md"], "CC-BY-4.0", "https://react.dev/{path}", "src/content"),
        new("nextjs", "Next.js", "web", "vercel/next.js", "canary",
            ["docs"], [".mdx", ".md"], "MIT"),
        new("nodejs", "Node.js API", "web", "nodejs/node", "main",
            ["doc/api"], [".md"], "MIT", "https://nodejs.org/api/{path}.html", "doc/api"),
        new("git", "Git", "web", "git/git", "master",
            ["Documentation"], [".adoc", ".txt"], "GPL-2.0", "https://git-scm.com/docs/{path}", "Documentation"),
        new("react-native", "React Native", "mobil", "react/react-native-website", "main",
            ["docs"], [".md"], "CC-BY-4.0", "https://reactnative.dev/docs/{path}", "docs"),
        new("flutter", "Flutter", "mobil", "flutter/website", "main",
            ["sites/docs/src/content"], [".md"], "CC-BY-4.0", "https://docs.flutter.dev/{path}", "sites/docs/src/content"),
        new("dart", "Dart", "mobil", "dart-lang/site-www", "main",
            ["src/content"], [".md"], "CC-BY-4.0", "https://dart.dev/{path}", "src/content"),
        new("kotlin", "Kotlin (Android dahil dil dokümantasyonu)", "mobil", "JetBrains/kotlin-web-site", "master",
            ["docs/topics"], [".md"], "Apache-2.0", "https://kotlinlang.org/docs/{path}.html", "docs/topics"),
        new("swift", "Swift (The Swift Programming Language)", "mobil", "swiftlang/swift-book", "main",
            ["TSPL.docc"], [".md"], "Apache-2.0"),
        // Faz 16: İş arayüzünde bileşen fikri için (Tailwind bileşen örnekleri, HTML). MIT.
        new("flowbite", "Flowbite (Tailwind bileşenleri)", "web", "themesberg/flowbite", "main",
            ["content/components"], [".md"], "MIT", "https://flowbite.com/docs/components/{path}/", "content/components"),
        // .NET (2026-10-28: backend tarafı; Microsoft Learn'ün açık depoları, DocFX Markdown).
        new("dotnet", ".NET ve C#", "backend", "dotnet/docs", "main",
            ["docs/csharp", "docs/core", "docs/fundamentals", "docs/standard"], [".md"], "CC-BY-4.0",
            "https://learn.microsoft.com/en-us/dotnet/{path}", "docs"),
        new("aspnetcore", "ASP.NET Core", "backend", "dotnet/AspNetCore.Docs", "main",
            ["aspnetcore/fundamentals", "aspnetcore/web-api", "aspnetcore/mvc", "aspnetcore/razor-pages", "aspnetcore/blazor",
             "aspnetcore/security", "aspnetcore/data", "aspnetcore/signalr", "aspnetcore/grpc", "aspnetcore/performance",
             "aspnetcore/test", "aspnetcore/host-and-deploy", "aspnetcore/diagnostics", "aspnetcore/tutorials"],
            [".md"], "CC-BY-4.0", "https://learn.microsoft.com/en-us/aspnet/core/{path}", "aspnetcore"),
        new("efcore", "Entity Framework Core", "backend", "dotnet/EntityFramework.Docs", "main",
            ["entity-framework/core"], [".md"], "CC-BY-4.0", "https://learn.microsoft.com/en-us/ef/core/{path}", "entity-framework/core"),
        // kullanıcının dersleri (gece sınavı 2026-10-28: termodinamik/akışkanlar sorularında %54). Ultron'la gelen, Claude'un yazdığı
        // Türkçe ders notları; Host klasöründeki notlar/muhendislik altında, düzenlenebilir.
        new("muhendislik", "Mühendislik ders notları (Termodinamik, Akışkanlar, Isı transferi)", "ders", LocalPrefix + "notlar/muhendislik", "",
            ["."], [".md"], "Ultron notları"),
    ];

    /// <summary>Yerel kaynak öneki: depo yerine Ultron'un kendi klasöründeki notlar.</summary>
    public const string LocalPrefix = "yerel:";

    /// <summary>Sayfanın web adresi: şablon varsa resmi site, yoksa GitHub'daki dosya.</summary>
    public static string UrlFor(LibrarySource source, string repoPath)
    {
        if (source.PageUrl is null)
            return $"https://github.com/{source.Repo}/blob/{source.Branch}/{repoPath}";
        var path = repoPath;
        if (source.UrlRoot is { Length: > 0 } root && path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            path = path[(root.Length + 1)..];
        path = System.IO.Path.ChangeExtension(path, null)!.Replace('\\', '/');
        if (path.EndsWith("/index", StringComparison.OrdinalIgnoreCase))
            path = path[..^"/index".Length];
        return source.PageUrl.Replace("{path}", path);
    }
}
