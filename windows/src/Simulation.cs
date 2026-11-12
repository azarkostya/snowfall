namespace Snowfall;

internal sealed class Flake
{
    public float X, Y, PrevY, R, Vx, Vy, Phase, Freq, Sway, Depth;
    public byte A;
    public int Screen;
    /// <summary>Snow shaken off a moved window: falls once, never respawns.</summary>
    public bool Transient;
    public bool Dead;
}

internal sealed class Pile
{
    public IntPtr Hwnd;
    public Native.RECT Rect;
    public float[] H = Array.Empty<float>();
    public List<(int L, int R)> Exposed = new();
    public float Scale = 1f;
    public bool Seen;
}

internal sealed class ScreenArea
{
    public Rectangle Bounds;
    public float Scale;
    public int Target;
}

/// <summary>Snow particles and window-edge snow piles, in virtual-screen pixels.</summary>
internal sealed class Simulation
{
    private const int MaxTransient = 1500;

    private readonly Random _rng = new();
    private float _wind;

    public List<ScreenArea> Screens { get; } = new();
    public List<Flake> Flakes { get; } = new();
    public Dictionary<IntPtr, Pile> Piles { get; } = new();

    public Settings Settings { get; }

    public Simulation(Settings settings) => Settings = settings;

    public void SetScreens(IEnumerable<Screen> screens)
    {
        Screens.Clear();
        Flakes.Clear();
        foreach (var s in screens)
        {
            var b = s.Bounds;
            Screens.Add(new ScreenArea { Bounds = b, Scale = Native.ScaleForPoint(b.Left + b.Width / 2, b.Top + b.Height / 2) });
        }
        ApplyIntensity(initialFill: true);
    }

    public void ApplyIntensity(bool initialFill = false)
    {
        int perFullHd = Settings.Intensity switch
        {
            Intensity.Light => 70,
            Intensity.Blizzard => 380,
            _ => 160,
        };
        for (int i = 0; i < Screens.Count; i++)
        {
            var s = Screens[i];
            s.Target = (int)(perFullHd * (s.Bounds.Width * (double)s.Bounds.Height) / (1920.0 * 1080.0));
            if (initialFill)
                for (int k = 0; k < s.Target; k++) Flakes.Add(Spawn(i, anywhere: true));
        }
    }

    public void Step(float dt, float t)
    {
        float windBase = Settings.Intensity == Intensity.Blizzard ? 60f : 18f;
        _wind = windBase * (0.6f * MathF.Sin(t * 0.07f) + 0.4f * MathF.Sin(t * 0.23f + 1.3f));

        // Keep each screen at its target count (respawn handles the steady state).
        var counts = new int[Screens.Count];
        foreach (var f in Flakes) if (!f.Transient && !f.Dead) counts[f.Screen]++;
        // At most two new flakes per screen per step, so resuming snowfall ramps up instead of dropping a wall.
        if (Settings.Falling)
            for (int i = 0; i < Screens.Count; i++)
                for (int k = counts[i], added = 0; k < Screens[i].Target && added < 2; k++, added++)
                    Flakes.Add(Spawn(i, anywhere: false));

        bool piles = Settings.PilesEnabled;
        foreach (var f in Flakes)
        {
            if (f.Dead) continue;
            var area = Screens[f.Screen];
            var b = area.Bounds;

            // Snowfall switched off: flakes in the air fade out within about a second.
            if (!Settings.Falling && !f.Transient)
            {
                int a = f.A - (int)MathF.Ceiling(220f * dt);
                if (a < 70) { f.Dead = true; continue; }
                f.A = (byte)a;
            }

            f.PrevY = f.Y;
            if (f.Transient)
            {
                // Clumps knocked off a pile: gravity plus a decaying sideways kick.
                f.Vy = MathF.Min(f.Vy + 380f * area.Scale * dt, 420f * area.Scale);
                f.X += f.Vx * dt;
                f.Vx *= MathF.Max(0f, 1f - 2.5f * dt);
            }
            f.Y += f.Vy * dt;
            f.X += (_wind * (0.35f + 0.65f * f.Depth) * area.Scale + MathF.Sin(t * f.Freq + f.Phase) * f.Sway) * dt;

            // Knocked-off clumps fall through: otherwise they'd land straight back on the pile they came from.
            if (piles && !f.Transient && f.Depth > 0.35f && TryLand(f))
            {
                Recycle(f);
                continue;
            }

            if (f.Y - f.R > b.Bottom) { Recycle(f); continue; }
            if (!f.Transient)
            {
                if (f.X < b.Left - 4) f.X += b.Width + 8;
                else if (f.X > b.Right + 4) f.X -= b.Width + 8;
            }
        }

        // Drop surplus (intensity lowered) and finished transient flakes.
        for (int i = 0; i < Screens.Count; i++) counts[i] = 0;
        Flakes.RemoveAll(f =>
        {
            if (f.Dead) return true;
            if (f.Transient) return false;
            return ++counts[f.Screen] > Screens[f.Screen].Target;
        });
    }

    private void Recycle(Flake f)
    {
        // Snowfall switched off: flakes already in the air finish their fall and are not replaced.
        if (f.Transient || !Settings.Falling) { f.Dead = true; return; }
        var fresh = Spawn(f.Screen, anywhere: false);
        f.X = fresh.X; f.Y = fresh.Y; f.PrevY = fresh.Y;
    }

    private bool TryLand(Flake f)
    {
        int x = (int)f.X;
        foreach (var p in Piles.Values)
        {
            var r = p.Rect;
            if (x < r.Left || x >= r.Right || p.Exposed.Count == 0) continue;
            int col = x - r.Left;
            float surface = r.Top - p.H[col];
            if (!(f.PrevY < surface && f.Y >= surface)) continue;

            bool onExposed = false;
            foreach (var (l, rr) in p.Exposed) if (x >= l && x < rr) { onExposed = true; break; }
            if (!onExposed) continue;

            if (!f.Transient && _rng.NextSingle() > 0.9f) return false; // some flakes fall past in front
            return Deposit(p, col, f.R);
        }
        return false;
    }

    private bool Deposit(Pile p, int col, float radius)
    {
        int len = p.H.Length;
        float maxH = 13f * p.Scale;
        float amount = radius * 0.75f;
        float sigma = MathF.Max(1.5f, radius * 1.3f);
        int w = (int)(sigma * 2.5f);
        bool any = false;
        for (int k = -w; k <= w; k++)
        {
            int i = col + k;
            if (i < 0 || i >= len) continue;
            // Taper towards the window corners so the pile rounds off instead of ending in a wall.
            int edge = Math.Min(i, len - 1 - i);
            float cap = MathF.Min(maxH, 1.5f + edge * 0.45f);
            if (p.H[i] >= cap) continue;
            p.H[i] = MathF.Min(cap, p.H[i] + amount * MathF.Exp(-k * k / (2f * sigma * sigma)));
            any = true;
        }
        return any;
    }

    /// <summary>Match piles to the current window list: follow, shake off moved windows, drop closed ones.</summary>
    public void SyncPiles(List<TrackedWindow> windows)
    {
        foreach (var p in Piles.Values) p.Seen = false;

        foreach (var w in windows)
        {
            if (Piles.TryGetValue(w.Hwnd, out var p))
            {
                var o = p.Rect;
                if (o.Left != w.Rect.Left || o.Top != w.Rect.Top || o.Right != w.Rect.Right)
                {
                    ShakeOff(p);
                    p.H = new float[w.Rect.Width];
                }
                p.Rect = w.Rect;
            }
            else
            {
                if (w.Exposed.Count == 0) continue;
                p = new Pile
                {
                    Hwnd = w.Hwnd,
                    Rect = w.Rect,
                    H = new float[w.Rect.Width],
                    Scale = Native.ScaleForPoint(w.Rect.Left + w.Rect.Width / 2, w.Rect.Top),
                };
                Piles[w.Hwnd] = p;
            }
            p.Exposed = w.Exposed;
            p.Seen = true;
            Relax(p);
        }

        foreach (var p in Piles.Values.Where(p => !p.Seen).ToList())
        {
            ShakeOff(p);
            Piles.Remove(p.Hwnd);
        }
    }

    public void ShakeOffAll()
    {
        foreach (var p in Piles.Values)
        {
            ShakeOff(p);
            Array.Clear(p.H);
        }
    }

    private void ShakeOff(Pile p) => Crumble(p, 0, p.H.Length, burst: 25f);

    /// <summary>
    /// If (x, y) hits a visible pile, knock the snow around that point off with a little burst.
    /// Called from the mouse hook on the UI thread.
    /// </summary>
    public bool CrumbleAt(int x, int y)
    {
        foreach (var p in Piles.Values)
        {
            var r = p.Rect;
            if (x < r.Left || x >= r.Right) continue;
            bool exposed = false;
            foreach (var (l, rr) in p.Exposed) if (x >= l && x < rr) { exposed = true; break; }
            if (!exposed) continue;

            int col = x - r.Left;
            float h = 0;
            for (int i = Math.Max(0, col - 6); i <= Math.Min(p.H.Length - 1, col + 6); i++) h = MathF.Max(h, p.H[i]);
            if (h < 1.5f) continue;

            float pad = 5f * p.Scale;
            if (y < r.Top - h - pad || y > r.Top + 3) continue;

            int radius = (int)(45f * p.Scale);
            Crumble(p, col - radius, col + radius, burst: 140f);
            return true;
        }
        return false;
    }

    /// <summary>Turn the pile between columns [from, to) into falling clumps and clear it.</summary>
    private void Crumble(Pile p, int from, int to, float burst)
    {
        from = Math.Max(0, from);
        to = Math.Min(p.H.Length, to);
        if (to <= from) return;

        int screen = ScreenIndexOf(p.Rect.Left + (from + to) / 2, p.Rect.Top);
        int transient = Flakes.Count(f => f.Transient);
        int step = Math.Max(2, (to - from) / 160);
        float mid = (from + to) / 2f;
        for (int i = from; i < to && transient < MaxTransient; i += step)
        {
            float h = p.H[i];
            if (h < 0.8f) continue;
            int n = 1 + (int)(h / (4f * p.Scale));
            // Kick outwards from the click point; a plain shake-off just drops.
            float dir = MathF.Sign(i - mid);
            for (int k = 0; k < n; k++, transient++)
            {
                Flakes.Add(new Flake
                {
                    X = p.Rect.Left + i + _rng.NextSingle() * step,
                    Y = p.Rect.Top - _rng.NextSingle() * h,
                    R = (0.9f + 1.8f * _rng.NextSingle()) * p.Scale,
                    Vx = (dir * 0.6f + (_rng.NextSingle() - 0.5f)) * burst * p.Scale,
                    Vy = (-burst * 0.6f * _rng.NextSingle() + 20f) * p.Scale,
                    Depth = 0.6f + 0.4f * _rng.NextSingle(),
                    A = (byte)(200 + 55 * _rng.NextSingle()),
                    Phase = _rng.NextSingle() * MathF.Tau,
                    Freq = 0.8f + _rng.NextSingle(),
                    Sway = (2f + 6f * _rng.NextSingle()) * p.Scale,
                    Screen = screen,
                    Transient = true,
                });
            }
        }
        Array.Clear(p.H, from, to - from);
        // Soften the cut so the remaining pile slopes into the gap.
        Relax(p);
    }

    /// <summary>Angle of repose: let steep steps slide down to the neighbours.</summary>
    private static void Relax(Pile p)
    {
        var h = p.H;
        float limit = 1.1f * p.Scale;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i + 1 < h.Length; i++)
            {
                float d = h[i] - h[i + 1];
                if (MathF.Abs(d) <= limit) continue;
                float move = (d - MathF.Sign(d) * limit) * 0.25f;
                h[i] -= move;
                h[i + 1] += move;
            }
    }

    private int ScreenIndexOf(int x, int y)
    {
        for (int i = 0; i < Screens.Count; i++)
            if (Screens[i].Bounds.Contains(x, y)) return i;
        int best = 0; long bestD = long.MaxValue;
        for (int i = 0; i < Screens.Count; i++)
        {
            var b = Screens[i].Bounds;
            long dx = Math.Max(0, Math.Max(b.Left - x, x - b.Right));
            long dy = Math.Max(0, Math.Max(b.Top - y, y - b.Bottom));
            if (dx * dx + dy * dy < bestD) { bestD = dx * dx + dy * dy; best = i; }
        }
        return best;
    }

    private Flake Spawn(int screen, bool anywhere)
    {
        var s = Screens[screen];
        var b = s.Bounds;
        // Depth 0 = far (small, slow, faint), 1 = near (big, fast, bright).
        float d = MathF.Pow(_rng.NextSingle(), 1.6f);
        float scale = s.Scale;
        float r = (0.7f + 2.6f * d) * scale;
        float y = anywhere ? b.Top + _rng.NextSingle() * b.Height : b.Top - r - _rng.NextSingle() * 40f * scale;
        return new Flake
        {
            X = b.Left + _rng.NextSingle() * b.Width,
            Y = y,
            PrevY = y,
            R = r,
            Vy = (22f + 48f * d + 8f * _rng.NextSingle()) * scale,
            Depth = d,
            A = (byte)(110 + 145 * d),
            Phase = _rng.NextSingle() * MathF.Tau,
            Freq = 0.4f + 1.2f * _rng.NextSingle(),
            Sway = (6f + 22f * _rng.NextSingle()) * scale,
            Screen = screen,
        };
    }
}
