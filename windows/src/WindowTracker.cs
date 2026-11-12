using static Snowfall.Native;

namespace Snowfall;

/// <summary>A top-level window (or an external surface) whose top edge can hold snow.</summary>
internal sealed class TrackedWindow
{
    public IntPtr Hwnd;
    public RECT Rect;
    public bool IsTaskbar;
    public bool IsSurface;
    /// <summary>Parts of the top edge not covered by windows higher in Z-order, as [left, right) in screen px.</summary>
    public List<(int L, int R)> Exposed = new();
}

/// <summary>Enumerates visible top-level windows in Z-order and computes exposed top edges.</summary>
internal sealed class WindowTracker
{
    private static readonly HashSet<string> TaskbarClasses = new() { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
    private static readonly HashSet<string> IgnoredClasses = new()
    {
        "Progman", "WorkerW", "#32768", "tooltips_class32", "Windows.UI.Core.CoreWindow",
        "ForegroundStaging", "MultitaskingViewFrame", "XamlExplorerHostIslandWindow",
    };

    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly List<(IntPtr Hwnd, RECT Rect, bool Taskbar, bool Topmost)> _raw = new();

    /// <summary>DWM bounds of maximized windows overshoot the monitor by a pixel or so; don't let that hide an edge.</summary>
    private const int OverlapTolerance = 3;

    public List<TrackedWindow> Windows { get; } = new();
    public IEnumerable<TrackedWindow> Taskbars => Windows.Where(w => w.IsTaskbar);

    /// <summary>Titled windows skipped by the filters, with the reason (only filled when debugging).</summary>
    public List<string>? Rejected { get; set; }

    public void Refresh(IReadOnlyList<ExternalSurface> surfaces)
    {
        _raw.Clear();
        Rejected?.Clear();
        EnumWindows(Collect, IntPtr.Zero);

        Windows.Clear();
        for (int i = 0; i < _raw.Count; i++)
        {
            var (hwnd, rect, taskbar, _) = _raw[i];
            var tw = new TrackedWindow { Hwnd = hwnd, Rect = rect, IsTaskbar = taskbar };

            // No pile on windows touching the top of their monitor (maximized etc.): every flake would land there.
            var screen = Screen.FromRectangle(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
            if (rect.Top > screen.Bounds.Top + 4 || taskbar)
                tw.Exposed = ExposedEdge(rect.Left, rect.Right, rect.Top, i, onlyTopmost: taskbar);
            Windows.Add(tw);
        }

        // External surfaces (CalWall panels) live in the wallpaper layer: below every window.
        foreach (var s in surfaces)
        {
            var rect = new RECT { Left = s.Left, Top = s.Top, Right = s.Right, Bottom = s.Top + 1 };
            Windows.Add(new TrackedWindow
            {
                Hwnd = s.Id,
                Rect = rect,
                IsSurface = true,
                Exposed = ExposedEdge(s.Left, s.Right, s.Top, _raw.Count, onlyTopmost: false),
            });
        }
    }

    /// <summary>
    /// [left, right) at height y minus every window among the first <paramref name="above"/> that spans y.
    /// The taskbar is drawn above normal windows whatever EnumWindows says, so only topmost ones can cover it.
    /// </summary>
    private List<(int L, int R)> ExposedEdge(int left, int right, int y, int above, bool onlyTopmost)
    {
        var segs = new List<(int L, int R)> { (left, right) };
        for (int j = 0; j < above && segs.Count > 0; j++)
        {
            var (_, r, isTaskbar, topmost) = _raw[j];
            if (onlyTopmost && (!topmost || isTaskbar)) continue;
            if (r.Top < y - OverlapTolerance && r.Bottom > y + OverlapTolerance)
                Subtract(segs, r.Left + OverlapTolerance, r.Right - OverlapTolerance);
        }
        segs.RemoveAll(s => s.R - s.L < 6);
        return segs;
    }

    /// <summary>A window that covers a whole monitor (game, video, presentation), or null.</summary>
    public static Screen? FullscreenScreen()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return null;
        var cls = ClassOf(fg);
        if (cls is "Progman" or "WorkerW" || TaskbarClasses.Contains(cls)) return null;
        GetWindowRect(fg, out var r);
        var s = Screen.FromHandle(fg);
        var b = s.Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom ? s : null;
    }

    private bool Collect(IntPtr hwnd, IntPtr _)
    {
        if (!IsWindowVisible(hwnd)) return true;
        if (IsIconic(hwnd)) { Reject(hwnd, "minimized"); return true; }
        if (IsCloaked(hwnd)) { Reject(hwnd, "cloaked"); return true; }
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownPid) return true;

        var cls = ClassOf(hwnd);
        if (IgnoredClasses.Contains(cls)) return true;
        bool taskbar = TaskbarClasses.Contains(cls);
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);

        if (!taskbar)
        {
            if (GetWindowTextLength(hwnd) == 0) return true;
            if ((ex & WS_EX_TOOLWINDOW) != 0) { Reject(hwnd, "toolwindow"); return true; }
            if ((ex & WS_EX_TRANSPARENT) != 0) { Reject(hwnd, "transparent"); return true; }
        }

        var rect = FrameBounds(hwnd);
        if (rect.Width < 120 || rect.Height < 40) { Reject(hwnd, $"small {rect.Width}x{rect.Height}"); return true; }

        _raw.Add((hwnd, rect, taskbar, (ex & WS_EX_TOPMOST) != 0));
        return true;
    }

    private void Reject(IntPtr hwnd, string reason)
    {
        if (Rejected == null || GetWindowTextLength(hwnd) == 0) return;
        Rejected.Add($"{hwnd:X} [{ClassOf(hwnd)}] {reason}");
    }

    private static void Subtract(List<(int L, int R)> segs, int l, int r)
    {
        for (int k = segs.Count - 1; k >= 0; k--)
        {
            var (sl, sr) = segs[k];
            if (r <= sl || l >= sr) continue;
            segs.RemoveAt(k);
            if (sl < l) segs.Add((sl, l));
            if (r < sr) segs.Add((r, sr));
        }
    }
}
