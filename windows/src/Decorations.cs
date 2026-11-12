using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Snowfall;

/// <summary>Christmas lights along the taskbar, a snowman and a tree standing on it. All vector, no assets.</summary>
internal sealed class Decorations : IDisposable
{
    private static readonly Color[] Palette =
    {
        Color.FromArgb(255, 70, 70),   // red
        Color.FromArgb(255, 200, 60),  // gold
        Color.FromArgb(70, 225, 110),  // green
        Color.FromArgb(80, 160, 255),  // blue
        Color.FromArgb(255, 100, 210), // pink
    };
    private const int GlowLevels = 5;

    private readonly Dictionary<(int Color, int Level, int Size), Bitmap> _glow = new();

    private readonly Pen _wire = new(Color.FromArgb(220, 30, 48, 36), 1.3f);
    private readonly SolidBrush _cap = new(Color.FromArgb(255, 40, 62, 42));
    private readonly SolidBrush _snow = new(Color.FromArgb(252, 250, 252, 255));
    private readonly Pen _snowEdge = new(Color.FromArgb(170, 150, 168, 198), 1f);
    private readonly SolidBrush _coal = new(Color.FromArgb(255, 30, 30, 36));
    private readonly SolidBrush _carrot = new(Color.FromArgb(255, 255, 140, 30));
    private readonly SolidBrush _scarf = new(Color.FromArgb(255, 210, 40, 50));
    private readonly Pen _branch = new(Color.FromArgb(255, 110, 70, 40), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    private readonly SolidBrush _needles = new(Color.FromArgb(255, 30, 122, 66));
    private readonly Pen _needlesEdge = new(Color.FromArgb(255, 18, 82, 44), 1f);
    private readonly SolidBrush _trunk = new(Color.FromArgb(255, 105, 66, 38));
    private readonly SolidBrush _star = new(Color.FromArgb(255, 255, 214, 70));
    private readonly SolidBrush[] _bulb = new SolidBrush[Palette.Length];
    private readonly SolidBrush _highlight = new(Color.White);

    public Decorations()
    {
        for (int i = 0; i < _bulb.Length; i++) _bulb[i] = new SolidBrush(Palette[i]);
    }

    public void Draw(Graphics g, Settings s, IEnumerable<TrackedWindow> taskbars, Rectangle bounds, float t)
    {
        foreach (var tb in taskbars)
        {
            var r = tb.Rect;
            if (r.Width < r.Height) continue; // vertical taskbar: not supported
            if (r.Right <= bounds.Left || r.Left >= bounds.Right || r.Top < bounds.Top || r.Top > bounds.Bottom) continue;

            if (tb.Exposed.Count == 0) continue; // taskbar fully covered by a window

            // Draw only over the visible parts of the taskbar: no lights over a window that covers it.
            using var clip = new Region(RectangleF.Empty);
            foreach (var (l, rr) in tb.Exposed)
                clip.Union(new RectangleF(l - bounds.Left, r.Top - bounds.Top - 80, rr - l, r.Height + 80));
            var oldClip = g.Clip;
            g.Clip = clip;
            try { DrawBar(g, s, tb, bounds, t); }
            finally { g.Clip = oldClip; oldClip.Dispose(); }
        }
    }

    private void DrawBar(Graphics g, Settings s, TrackedWindow tb, Rectangle bounds, float t)
    {
        var r = tb.Rect;
        {
            float scale = Native.ScaleForPoint(r.Left + r.Width / 2, r.Top + r.Height / 2);
            float left = r.Left - bounds.Left, right = r.Right - bounds.Left, top = r.Top - bounds.Top;
            float bottom = r.Bottom - bounds.Top;

            // Snowman and tree stand inside the taskbar's empty left corner instead of over other windows.
            // Both are ~66 design units tall; shrink them to fit the bar.
            float k = MathF.Min(scale, (r.Height - 6 * scale) / 66f);
            float floor = bottom - 2 * scale;
            if (s.Snowman) DrawSnowman(g, left + 30 * k, floor, k, t);
            if (s.Tree) DrawTree(g, left + (s.Snowman ? 78 : 30) * k, floor, k, t);
            if (s.Garland) DrawGarland(g, left, right, top, scale, t, s.GarlandMode);
        }
    }

    // ---------------------------------------------------------------- garland

    private void DrawGarland(Graphics g, float left, float right, float top, float s, float t, GarlandMode mode)
    {
        float span = 120 * s, sag = 4 * s, y0 = top + 1.2f * s;
        float WireY(float x)
        {
            float u = ((x - left) % span) / span;
            return y0 + sag * 4 * u * (1 - u);
        }

        var pts = new List<PointF>();
        for (float x = left; x <= right; x += 6) pts.Add(new PointF(x, WireY(x)));
        if (pts.Count > 1)
        {
            _wire.Width = 1.3f * s;
            g.DrawLines(_wire, pts.ToArray());
        }

        float spacing = 24 * s;
        int i = 0;
        for (float x = left + spacing / 2; x < right; x += spacing, i++)
        {
            int c = i % Palette.Length;
            float b = Brightness(i, t, mode);
            float wy = WireY(x);
            float bw = 4.6f * s, bh = 6.6f * s;
            float cx = x, cy = wy + 1.6f * s + bh / 2;

            // Small glow: neighbours 24 px apart must not merge into a coloured haze over the taskbar.
            DrawGlow(g, c, b, cx, cy, 13 * s);
            g.FillRectangle(_cap, cx - 1.5f * s, wy - 0.4f * s, 3f * s, 2.4f * s);
            _bulb[c].Color = Lerp(Dim(Palette[c]), Palette[c], b);
            g.FillEllipse(_bulb[c], cx - bw / 2, wy + 1.6f * s, bw, bh);
            _highlight.Color = Color.FromArgb((int)(60 + 150 * b), 255, 255, 255);
            g.FillEllipse(_highlight, cx - bw / 2 + 0.9f * s, wy + 2.6f * s, 1.4f * s, 2f * s);
        }
    }

    private static float Brightness(int i, float t, GarlandMode mode)
    {
        uint h = (uint)i * 2654435761u;
        float phase = (h & 0xFFFF) / 65535f * MathF.Tau;
        float speed = 0.7f + ((h >> 16) & 0xFF) / 255f * 0.9f;
        switch (mode)
        {
            case GarlandMode.Chase:
            {
                float pos = t * 6f - i;
                float k = pos - 3f * MathF.Floor(pos / 3f); // 0..3
                return k < 1f ? 1f - 0.6f * k : 0.25f;
            }
            case GarlandMode.Steady:
                return 0.85f + 0.15f * MathF.Sin(t * 0.8f + i * 0.35f);
            default:
            {
                float w = 0.5f + 0.5f * MathF.Sin(t * 1.8f * speed + phase);
                return 0.25f + 0.75f * w * w;
            }
        }
    }

    private void DrawGlow(Graphics g, int color, float brightness, float cx, float cy, float size)
    {
        int level = Math.Clamp((int)(brightness * GlowLevels), 0, GlowLevels - 1);
        int px = Math.Max(4, (int)size);
        if (!_glow.TryGetValue((color, level, px), out var bmp))
        {
            bmp = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using var gg = Graphics.FromImage(bmp);
            using var path = new GraphicsPath();
            path.AddEllipse(0, 0, px, px);
            var c = Palette[color];
            using var brush = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb((int)(25 + 110 * (level + 1) / (float)GlowLevels), c),
                SurroundColors = new[] { Color.FromArgb(0, c) },
            };
            gg.FillEllipse(brush, 0, 0, px, px);
            _glow[(color, level, px)] = bmp;
        }
        g.DrawImage(bmp, cx - px / 2f, cy - px / 2f, px, px);
    }

    // ---------------------------------------------------------------- snowman

    private void DrawSnowman(Graphics g, float x, float baseY, float s, float t)
    {
        var state = g.Save();
        g.TranslateTransform(x, baseY);
        g.ScaleTransform(s, s);

        // Arms behind the body. The right one waves for ~2 s every 9 s.
        float cycle = t % 9f;
        float wave = cycle < 2f ? 0.45f * MathF.Sin(cycle * 11f) * MathF.Sin(cycle / 2f * MathF.PI) : 0f;
        g.DrawLine(_branch, -8, -30, -20, -39);
        g.DrawLine(_branch, -17, -36.5f, -19, -42);
        float a = -0.6f + wave;
        float ex = 8 + 13 * MathF.Cos(a), ey = -30 + 13 * MathF.Sin(a);
        g.DrawLine(_branch, 8, -30, ex, ey);
        g.DrawLine(_branch, 8 + 9 * MathF.Cos(a), -30 + 9 * MathF.Sin(a), ex + 2, ey - 4);

        foreach (var (cy, r) in new[] { (-12f, 12f), (-29f, 9f), (-43f, 7f) })
        {
            g.FillEllipse(_snow, -r, cy - r, 2 * r, 2 * r);
            g.DrawEllipse(_snowEdge, -r, cy - r, 2 * r, 2 * r);
        }

        // Face.
        g.FillEllipse(_coal, -3.6f, -46.2f, 2.2f, 2.2f);
        g.FillEllipse(_coal, 1.4f, -46.2f, 2.2f, 2.2f);
        g.FillPolygon(_carrot, new[] { new PointF(0, -43.6f), new PointF(8, -42.6f), new PointF(0, -41.4f) });
        foreach (var (mx, my) in new[] { (-3f, -39.6f), (-1.5f, -38.9f), (0f, -38.7f), (1.5f, -38.9f), (3f, -39.6f) })
            g.FillEllipse(_coal, mx - 0.6f, my - 0.6f, 1.2f, 1.2f);

        // Scarf.
        g.FillRectangle(_scarf, -7.2f, -37.4f, 14.4f, 3.4f);
        g.FillPolygon(_scarf, new[] { new PointF(2.5f, -35f), new PointF(6.5f, -35f), new PointF(8f, -27f), new PointF(4f, -27.6f) });

        // Buttons.
        foreach (var by in new[] { -32f, -27.5f, -16f, -10f })
            g.FillEllipse(_coal, -1.2f, by - 1.2f, 2.4f, 2.4f);

        // Hat, outlined so it doesn't vanish against dark windows.
        g.FillRectangle(_coal, -6, -62, 12, 12);
        g.DrawRectangle(_snowEdge, -6, -62, 12, 12);
        g.FillRectangle(_coal, -9, -50.5f, 18, 2.6f);
        g.DrawRectangle(_snowEdge, -9, -50.5f, 18, 2.6f);
        g.FillRectangle(_scarf, -6, -52.8f, 12, 2);
        g.FillEllipse(_snow, -6.5f, -63.5f, 13, 3); // snow on the hat

        g.Restore(state);
    }

    // ---------------------------------------------------------------- tree

    private static readonly (float X, float Y)[] Ornaments =
    {
        (-12, -13), (-3, -16), (8, -13), (15, -10.5f), (-9, -28), (3, -30), (10, -26.5f), (-5, -40), (5, -42), (0, -50),
    };

    private void DrawTree(Graphics g, float x, float baseY, float s, float t)
    {
        var state = g.Save();
        g.TranslateTransform(x, baseY);
        g.ScaleTransform(s, s);

        g.FillRectangle(_trunk, -3, -9, 6, 9);
        foreach (var tier in new[]
                 {
                     new[] { new PointF(0, -34), new PointF(-21, -8), new PointF(21, -8) },
                     new[] { new PointF(0, -46), new PointF(-16, -23), new PointF(16, -23) },
                     new[] { new PointF(0, -57), new PointF(-11, -35), new PointF(11, -35) },
                 })
        {
            g.FillPolygon(_needles, tier);
            g.DrawPolygon(_needlesEdge, tier);
        }
        foreach (var (dx, dy, w, h) in new[] { (-16f, -10f, 9f, 2.6f), (6f, -11f, 10f, 2.6f), (-12f, -25f, 8f, 2.4f), (4f, -25.5f, 9f, 2.4f), (-7f, -37f, 6f, 2.2f), (2f, -37.5f, 6f, 2.2f) })
            g.FillEllipse(_snow, dx, dy, w, h);

        g.Restore(state);

        // Lights and star in screen space so the glow sprites stay crisp.
        for (int i = 0; i < Ornaments.Length; i++)
        {
            int c = (i * 3) % Palette.Length;
            float b = Brightness(i + 7, t, GarlandMode.Twinkle);
            float ox = x + Ornaments[i].X * s, oy = baseY + Ornaments[i].Y * s;
            DrawGlow(g, c, b, ox, oy, 12 * s);
            _bulb[c].Color = Lerp(Dim(Palette[c]), Palette[c], b);
            g.FillEllipse(_bulb[c], ox - 1.9f * s, oy - 1.9f * s, 3.8f * s, 3.8f * s);
        }

        float sx = x, sy = baseY - 58 * s;
        DrawGlow(g, 1, 0.75f + 0.25f * MathF.Sin(t * 2.3f), sx, sy, 26 * s);
        g.FillPolygon(_star, Star(sx, sy, 5.5f * s, 2.3f * s));
    }

    private static PointF[] Star(float cx, float cy, float outer, float inner)
    {
        var pts = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            float r = i % 2 == 0 ? outer : inner;
            float a = -MathF.PI / 2 + i * MathF.PI / 5;
            pts[i] = new PointF(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
        }
        return pts;
    }

    // ---------------------------------------------------------------- helpers

    private static Color Dim(Color c) => Color.FromArgb(255, c.R * 2 / 5, c.G * 2 / 5, c.B * 2 / 5);

    private static Color Lerp(Color a, Color b, float k)
    {
        k = Math.Clamp(k, 0f, 1f);
        return Color.FromArgb(255,
            (int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
    }

    public void Dispose()
    {
        foreach (var bmp in _glow.Values) bmp.Dispose();
        _glow.Clear();
    }
}
