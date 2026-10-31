using System.IO;
using System.Windows.Media;
using Ultron.Desktop.Widget;
using Drawing = System.Drawing;

namespace Ultron.Desktop;

/// <summary>
/// Uygulama ikonu: kullanıcının seçtiği Ultron kaskı (<see cref="UltronEmblem"/>'in sabit çizimi). Pencere, tepsi ve
/// (ultron.ico ile) exe aynı görseli kullanır; dosya yerine vektörden çizildiği için her boyutta net.
/// </summary>
internal static class UltronIcon
{
    private static Drawing.Icon? _cached;

    /// <summary>Tepsi ikonu: 64 px PNG'den (GDI+ PNG gömülü .ico girdilerini bozuk okuyor; PNG → Bitmap → ikon güvenli).</summary>
    public static Drawing.Icon Create()
    {
        if (_cached is not null)
            return _cached;
        using var bitmap = new Drawing.Bitmap(new MemoryStream(UltronEmblem.RenderPng(64)));
        return _cached = Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    public static ImageSource CreateImageSource() => UltronEmblem.Render(256);
}
