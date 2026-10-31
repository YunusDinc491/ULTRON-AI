using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ultron.Desktop.Widget;

public enum EmblemState
{
    Idle,
    Listening,
    Thinking,
    Speaking,
}

/// <summary>
/// Hareketli Ultron simgesi. Her karede (CompositionTarget.Rendering) halkalar döner, simge Y ekseninde salınır,
/// gözler nefes alır. Dinlerken ve konuşurken parıltı ses seviyesine göre büyür; düşünürken halkalar hızlanır.
/// </summary>
public partial class UltronEmblem : UserControl
{
    private readonly DateTime _start = DateTime.UtcNow;
    private double _outer, _inner, _last, _level;
    private bool _animating;

    public UltronEmblem()
    {
        InitializeComponent();
        Loaded += (_, _) => Animate(true);
        Unloaded += (_, _) => Animate(false);
    }

    public EmblemState State { get; set; }

    /// <summary>Anlık ses seviyesi (0-1): mikrofon ya da Ultron'un sesi.</summary>
    public void SetLevel(float level) => _level = Math.Max(_level * 0.6, Math.Clamp(level * 3, 0, 1));

    /// <summary>false: sabit çizim (ikon dosyası üretirken).</summary>
    public bool Animated { get; init; } = true;

    private void Animate(bool on)
    {
        if (!Animated || on == _animating)
            return;
        _animating = on;
        if (on)
            CompositionTarget.Rendering += OnFrame;
        else
            CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var t = (DateTime.UtcNow - _start).TotalSeconds;
        var dt = Math.Clamp(t - _last, 0, 0.1);
        _last = t;
        _level *= 0.9;

        // Halkalar: beklerken yavaş, dinlerken orta, düşünürken hızlı.
        var speed = State switch { EmblemState.Thinking => 4.0, EmblemState.Listening => 2.2, EmblemState.Speaking => 1.6, _ => 1.0 };
        _outer = (_outer + dt * 15 * speed) % 360;
        _inner = (_inner - dt * 24 * speed) % 360;
        OuterRing.Angle = _outer;
        InnerRing.Angle = _inner;

        // 3B salınım: Y ekseninde ±16° dönüyormuş gibi (kosinüs daralma) + hafif eğim; kask zıt yöne kayar (derinlik).
        var yaw = Math.Sin(t * 0.7) * 16 * Math.PI / 180;
        Yaw.ScaleX = 0.94 + 0.06 * Math.Cos(yaw);
        Tilt.AngleY = Math.Sin(yaw) * 5;
        HeadShift.X = Math.Sin(yaw) * 22;
        HeadShift.Y = Math.Sin(t * 1.1) * 4;

        // Gözler: nefes + ses seviyesi; dinlerken camgöbeğine kayan değil, daha parlak turuncu.
        var breath = 0.5 + 0.5 * Math.Sin(t * 2.0);
        var active = State is EmblemState.Listening or EmblemState.Speaking;
        EyeGlow.BlurRadius = 20 + breath * 16 + (active ? _level * 60 : 0) + (State == EmblemState.Thinking ? 18 : 0);
        EyeGlow.Opacity = 0.75 + breath * 0.2;
        FrameGlow.BlurRadius = 18 + breath * 14 + (active ? _level * 40 : 0);
        Mouth.Height = State == EmblemState.Speaking ? 34 + _level * 30 : 34;
    }

    /// <summary>Sabit simgeyi verilen boyutta PNG olarak çizer (pencere, tepsi ve exe ikonu için).</summary>
    public static BitmapSource Render(int size)
    {
        var emblem = new UltronEmblem { Animated = false, Width = size, Height = size };
        emblem.Measure(new Size(size, size));
        emblem.Arrange(new Rect(0, 0, size, size));
        emblem.UpdateLayout();
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(emblem);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] RenderPng(int size)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(size)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Çok boyutlu .ico (PNG gömülü): 16-256 px.</summary>
    public static byte[] RenderIco()
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var images = sizes.Select(RenderPng).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images)
            writer.Write(image);
        return stream.ToArray();
    }
}
