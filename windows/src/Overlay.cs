using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static Snowfall.Native;

namespace Snowfall;

/// <summary>
/// Click-through, always-on-top, per-pixel-alpha window covering one monitor.
/// Draws into a DIB section and presents it with UpdateLayeredWindow.
/// </summary>
internal sealed class Overlay : Form
{
    private static readonly SolidBrush[] FlakeBrushes =
        Enumerable.Range(0, 16).Select(i => new SolidBrush(Color.FromArgb(70 + i * 12, 255, 255, 255))).ToArray();
    private static readonly SolidBrush FlakeShadow = new(Color.FromArgb(45, 70, 90, 120));
    private static readonly SolidBrush PileFill = new(Color.FromArgb(248, 250, 252, 255));
    private static readonly Pen PileEdge = new(Color.FromArgb(120, 150, 172, 205), 1.2f);
    private static readonly Pen PileShade = new(Color.FromArgb(55, 120, 145, 185), 2.5f);

    private readonly Rectangle _bounds;
    private IntPtr _memDc, _dib, _oldBmp;
    private Bitmap? _bitmap;
    private Graphics? _g;
    private bool _cleared;
    private readonly List<PointF> _pts = new();

    public Rectangle ScreenBounds => _bounds;

    public Overlay(Rectangle bounds)
    {
        _bounds = bounds;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = bounds;
        Text = "Snowfall";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST) { m.Result = (IntPtr)HTTRANSPARENT; return; }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        CreateSurface();
    }

    private void CreateSurface()
    {
        int w = _bounds.Width, h = _bounds.Height;
        var screenDc = GetDC(IntPtr.Zero);
        var bmi = new BITMAPINFOHEADER
        {
            biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0,
        };
        _dib = CreateDIBSection(screenDc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        _memDc = CreateCompatibleDC(screenDc);
        _oldBmp = SelectObject(_memDc, _dib);
        ReleaseDC(IntPtr.Zero, screenDc);

        _bitmap = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
        _g = Graphics.FromImage(_bitmap);
        _g.SmoothingMode = SmoothingMode.AntiAlias;
        _g.CompositingQuality = CompositingQuality.HighSpeed;
        _g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
    }

    public void BringToTop() =>
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Present an empty frame once (used when snow is paused on this monitor).</summary>
    public void Clear()
    {
        if (_g == null || _cleared) return;
        _g.Clear(Color.Transparent);
        Present();
        _cleared = true;
    }

    public void Render(Simulation sim, Decorations deco, IEnumerable<TrackedWindow> taskbars, float t)
    {
        if (_g == null) return;
        _cleared = false;
        var g = _g;
        g.Clear(Color.Transparent);
        int ox = _bounds.Left, oy = _bounds.Top;

        if (sim.Settings.PilesEnabled)
            foreach (var p in sim.Piles.Values)
                DrawPile(g, p, ox, oy);

        deco.Draw(g, sim.Settings, taskbars, _bounds, t);

        foreach (var f in sim.Flakes)
        {
            if (f.Dead) continue;
            float x = f.X - ox, y = f.Y - oy, r = f.R;
            if (x < -r || y < -r || x > _bounds.Width + r || y > _bounds.Height + r) continue;
            if (r > 1.5f)
                g.FillEllipse(FlakeShadow, x - r - 0.8f, y - r - 0.3f, 2 * r + 1.6f, 2 * r + 1.6f);
            var brush = FlakeBrushes[Math.Clamp((f.A - 70) / 12, 0, FlakeBrushes.Length - 1)];
            if (r < 1.1f) g.FillRectangle(brush, x - r, y - r, 2 * r, 2 * r);
            else g.FillEllipse(brush, x - r, y - r, 2 * r, 2 * r);
        }

        Present();
    }

    private void DrawPile(Graphics g, Pile p, int ox, int oy)
    {
        var rect = p.Rect;
        if (rect.Right < _bounds.Left || rect.Left > _bounds.Right ||
            rect.Top < _bounds.Top || rect.Top - 20 > _bounds.Bottom) return;

        foreach (var (l, r) in p.Exposed)
        {
            int a = Math.Max(l, _bounds.Left), b = Math.Min(r, _bounds.Right);
            if (b - a < 2) continue;

            float peak = 0;
            for (int x = a; x < b; x++) peak = MathF.Max(peak, p.H[x - rect.Left]);
            if (peak < 0.6f) continue;

            // Sink the base slightly into the frame so no gap shows above the window's 1px border.
            float baseY = rect.Top - oy + 2f;
            _pts.Clear();
            _pts.Add(new PointF(a - ox, baseY));
            for (int x = a; x < b; x += 2)
                _pts.Add(new PointF(x - ox, baseY - p.H[x - rect.Left]));
            _pts.Add(new PointF(b - 1 - ox, baseY - p.H[b - 1 - rect.Left]));
            _pts.Add(new PointF(b - 1 - ox, baseY));

            var poly = _pts.ToArray();
            g.FillPolygon(PileFill, poly);
            var top = poly[1..^1];
            if (top.Length >= 2)
            {
                // Soft blue underside line, then a crisp outline so the snow reads on light windows too.
                g.DrawLine(PileShade, a - ox, baseY - 0.5f, b - 1 - ox, baseY - 0.5f);
                g.DrawLines(PileEdge, top);
            }
        }
    }

    private void Present()
    {
        var dst = new POINT(_bounds.Left, _bounds.Top);
        var size = new SIZE(_bounds.Width, _bounds.Height);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        var screenDc = GetDC(IntPtr.Zero);
        UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, ULW_ALPHA);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _g?.Dispose();
            _bitmap?.Dispose();
        }
        if (_memDc != IntPtr.Zero)
        {
            SelectObject(_memDc, _oldBmp);
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
        base.Dispose(disposing);
    }
}
