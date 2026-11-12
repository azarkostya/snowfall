using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;
using Microsoft.Win32;

namespace Snowfall;

/// <summary>Tray icon, frame loop, overlays, command pipe and the pile-click hook.</summary>
internal sealed class SnowApp : ApplicationContext
{
    private const double WindowRefreshSec = 0.066;
    private const double SurfaceRefreshSec = 1.0;
    private const double TopmostRefreshSec = 1.0;
    public const string ShortcutName = "Зима вкл-выкл.lnk";
    public const string SnowShortcutName = "Снегопад вкл-выкл.lnk";

    public static bool DebugDump;

    private readonly Settings _settings = Settings.Load();
    private readonly Simulation _sim;
    private readonly WindowTracker _tracker = new();
    private readonly SurfaceStore _surfaces = new();
    private readonly Decorations _deco = new();
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Overlay> _overlays = new();
    private readonly Icon _icon;
    private readonly Control _ui = new();
    private readonly CommandServer _server;
    private readonly MouseHook _hook;

    private double _last, _lastWindows, _lastSurfaces, _lastTopmost, _lastDump;
    private Screen? _fullscreen;

    private ToolStripMenuItem _enabledItem = null!, _fallingItem = null!, _pilesItem = null!, _autostartItem = null!;
    private ToolStripMenuItem _garlandItem = null!, _snowmanItem = null!, _treeItem = null!;
    private readonly Dictionary<Intensity, ToolStripMenuItem> _intensityItems = new();
    private readonly Dictionary<GarlandMode, ToolStripMenuItem> _modeItems = new();

    public SnowApp(bool forceEnable)
    {
        Native.timeBeginPeriod(1);
        if (forceEnable && !_settings.Enabled)
        {
            _settings.Enabled = true;
            _settings.Save();
        }
        if (DebugDump) _tracker.Rejected = new List<string>();

        _ui.CreateControl(); // owns a handle on the UI thread for Invoke from the pipe
        _sim = new Simulation(_settings);
        _icon = MakeIcon();

        _tray = new NotifyIcon { Icon = _icon, Text = "Snowfall", Visible = true, ContextMenuStrip = BuildMenu() };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) SetEnabled(!_settings.Enabled); };

        BuildOverlays();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        _server = new CommandServer(cmd => (string)_ui.Invoke(() => HandleCommand(cmd)));
        _hook = new MouseHook((x, y) =>
        {
            if (_settings.Enabled && _settings.PilesEnabled) _sim.CrumbleAt(x, y);
        });

        _timer = new System.Windows.Forms.Timer { Interval = 15 };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private bool NeedsWindows => _settings.PilesEnabled || _settings.Garland || _settings.Snowman || _settings.Tree;

    private void OnTick(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _last, 0, 0.05);
        _last = now;
        if (!_settings.Enabled) return;

        if (now - _lastSurfaces > SurfaceRefreshSec)
        {
            _lastSurfaces = now;
            _surfaces.Poll();
        }
        if (now - _lastWindows > WindowRefreshSec)
        {
            _lastWindows = now;
            if (NeedsWindows)
            {
                _tracker.Refresh(_surfaces.Current);
                if (_settings.PilesEnabled) _sim.SyncPiles(_tracker.Windows);
            }
            _fullscreen = WindowTracker.FullscreenScreen();
            if (DebugDump && now - _lastDump > 2)
            {
                _lastDump = now;
                Dump();
            }
        }
        if (now - _lastTopmost > TopmostRefreshSec)
        {
            _lastTopmost = now;
            foreach (var o in _overlays) o.BringToTop();
        }

        _sim.Step(dt, (float)now);

        foreach (var o in _overlays)
        {
            if (_fullscreen != null && _fullscreen.Bounds == o.ScreenBounds) o.Clear();
            else o.Render(_sim, _deco, _tracker.Taskbars, (float)now);
        }
    }

    // ---------------------------------------------------------------- commands (pipe)

    private string HandleCommand(string cmd)
    {
        switch (cmd)
        {
            case "toggle": SetEnabled(!_settings.Enabled); break;
            case "on": SetEnabled(true); break;
            case "off": SetEnabled(false); break;
            case "shake": _sim.ShakeOffAll(); break;
            // Snowfall only: piles and decorations stay. Asking for snow while everything is off turns it all on.
            case "snow":
                if (!_settings.Enabled) { SetFalling(true); SetEnabled(true); }
                else SetFalling(!_settings.Falling);
                break;
            case "snow-on": SetFalling(true); SetEnabled(true); break;
            case "snow-off": SetFalling(false); break;
        }
        return JsonSerializer.Serialize(new
        {
            running = true,
            enabled = _settings.Enabled,
            falling = _settings.Falling,
            piles = _settings.PilesEnabled,
            intensity = _settings.Intensity.ToString(),
        });
    }

    // ---------------------------------------------------------------- overlays

    private void BuildOverlays()
    {
        foreach (var o in _overlays) o.Dispose();
        _overlays.Clear();

        var screens = Screen.AllScreens;
        _sim.SetScreens(screens);
        foreach (var s in screens)
        {
            var o = new Overlay(s.Bounds);
            o.Show();
            if (!_settings.Enabled) o.Clear();
            _overlays.Add(o);
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => _ui.BeginInvoke(BuildOverlays);

    // ---------------------------------------------------------------- tray menu

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _enabledItem = new ToolStripMenuItem("Зима включена (всё)", null, (_, _) => SetEnabled(!_settings.Enabled)) { Checked = _settings.Enabled };
        _fallingItem = new ToolStripMenuItem("Снегопад", null, (_, _) => SetFalling(!_settings.Falling)) { Checked = _settings.Falling };

        var intensity = new ToolStripMenuItem("Сила снега");
        foreach (var (level, title) in new[] { (Intensity.Light, "Лёгкий"), (Intensity.Normal, "Обычный"), (Intensity.Blizzard, "Метель") })
        {
            var item = new ToolStripMenuItem(title, null, (_, _) => SetIntensity(level)) { Checked = _settings.Intensity == level };
            _intensityItems[level] = item;
            intensity.DropDownItems.Add(item);
        }

        _pilesItem = new ToolStripMenuItem("Сугробы на окнах", null, (_, _) => TogglePiles()) { Checked = _settings.PilesEnabled };
        var shake = new ToolStripMenuItem("Стряхнуть сугробы", null, (_, _) => _sim.ShakeOffAll());

        var decor = new ToolStripMenuItem("Украшения");
        _garlandItem = new ToolStripMenuItem("Гирлянда на панели задач", null, (_, _) =>
        {
            _settings.Garland = !_settings.Garland;
            _garlandItem.Checked = _settings.Garland;
            _settings.Save();
        }) { Checked = _settings.Garland };
        var modes = new ToolStripMenuItem("Режим гирлянды");
        foreach (var (mode, title) in new[] { (GarlandMode.Twinkle, "Мерцание"), (GarlandMode.Chase, "Бегущие огни"), (GarlandMode.Steady, "Ровный свет") })
        {
            var item = new ToolStripMenuItem(title, null, (_, _) => SetGarlandMode(mode)) { Checked = _settings.GarlandMode == mode };
            _modeItems[mode] = item;
            modes.DropDownItems.Add(item);
        }
        _snowmanItem = new ToolStripMenuItem("Снеговик", null, (_, _) =>
        {
            _settings.Snowman = !_settings.Snowman;
            _snowmanItem.Checked = _settings.Snowman;
            _settings.Save();
        }) { Checked = _settings.Snowman };
        _treeItem = new ToolStripMenuItem("Ёлка", null, (_, _) =>
        {
            _settings.Tree = !_settings.Tree;
            _treeItem.Checked = _settings.Tree;
            _settings.Save();
        }) { Checked = _settings.Tree };
        decor.DropDownItems.AddRange(new ToolStripItem[] { _garlandItem, modes, new ToolStripSeparator(), _snowmanItem, _treeItem });

        var shortcut = new ToolStripMenuItem("Кнопки на рабочий стол", null, (_, _) => CreateDesktopShortcut());
        _autostartItem = new ToolStripMenuItem("Запускать вместе с Windows", null, (_, _) => ToggleAutostart())
        {
            Checked = Settings.Autostart,
        };
        var exit = new ToolStripMenuItem("Выход", null, (_, _) => ExitThread());

        menu.Items.AddRange(new ToolStripItem[]
        {
            _enabledItem, _fallingItem, intensity, _pilesItem, shake, decor, new ToolStripSeparator(),
            shortcut, _autostartItem, new ToolStripSeparator(), exit,
        });
        return menu;
    }

    private void SetEnabled(bool enabled)
    {
        if (_settings.Enabled == enabled) return;
        _settings.Enabled = enabled;
        _enabledItem.Checked = enabled;
        _tray.Text = enabled ? "Snowfall" : "Snowfall — снег выключен";
        _settings.Save();
        if (!enabled)
            foreach (var o in _overlays) o.Clear();
    }

    private void SetFalling(bool falling)
    {
        if (_settings.Falling == falling) return;
        _settings.Falling = falling;
        _fallingItem.Checked = falling;
        _settings.Save();
    }

    private void TogglePiles()
    {
        _settings.PilesEnabled = !_settings.PilesEnabled;
        _pilesItem.Checked = _settings.PilesEnabled;
        if (!_settings.PilesEnabled) _sim.Piles.Clear();
        _settings.Save();
    }

    private void SetIntensity(Intensity level)
    {
        _settings.Intensity = level;
        foreach (var (l, item) in _intensityItems) item.Checked = l == level;
        _sim.ApplyIntensity();
        _settings.Save();
    }

    private void SetGarlandMode(GarlandMode mode)
    {
        _settings.GarlandMode = mode;
        foreach (var (m, item) in _modeItems) item.Checked = m == mode;
        _settings.Save();
    }

    private void ToggleAutostart()
    {
        try
        {
            Settings.Autostart = !Settings.Autostart;
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(3000, "Snowfall", "Не удалось изменить автозапуск: " + ex.Message, ToolTipIcon.Warning);
        }
        _autostartItem.Checked = Settings.Autostart;
    }

    /// <summary>Desktop shortcut that runs <c>Snowfall.exe --toggle</c>: one double-click stops or starts the snow.</summary>
    private void CreateDesktopShortcut()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell недоступен");
            dynamic shell = Activator.CreateInstance(shellType)!;
            foreach (var (name, arg, about) in new[]
                     {
                         (ShortcutName, "--toggle", "Включить или выключить всё: снег, сугробы, украшения"),
                         (SnowShortcutName, "--snow", "Включить или выключить снегопад (сугробы остаются)"),
                     })
            {
                dynamic lnk = shell.CreateShortcut(Path.Combine(desktop, name));
                lnk.TargetPath = Environment.ProcessPath;
                lnk.Arguments = arg;
                lnk.IconLocation = Environment.ProcessPath + ",0";
                lnk.Description = about;
                lnk.Save();
            }
            _tray.ShowBalloonTip(2500, "Snowfall", "Ярлыки «Зима вкл-выкл» и «Снегопад вкл-выкл» на рабочем столе", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(3000, "Snowfall", "Не удалось создать ярлык: " + ex.Message, ToolTipIcon.Warning);
        }
    }

    // ---------------------------------------------------------------- debug

    /// <summary>Writes tracked windows and pile heights to %TEMP%\snowfall-debug.txt (run with --debug).</summary>
    private void Dump()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{DateTime.Now:HH:mm:ss} flakes={_sim.Flakes.Count} piles={_sim.Piles.Count} " +
                      $"surfaces={_surfaces.Current.Count} fullscreen={_fullscreen?.DeviceName}");
        foreach (var w in _tracker.Windows)
        {
            _sim.Piles.TryGetValue(w.Hwnd, out var p);
            float peak = p == null || p.H.Length == 0 ? 0 : p.H.Max();
            string cls = w.IsSurface ? "surface" : Native.ClassOf(w.Hwnd);
            sb.AppendLine($"  {w.Hwnd:X} [{cls}] L{w.Rect.Left} T{w.Rect.Top} R{w.Rect.Right} B{w.Rect.Bottom} " +
                          $"exposed={string.Join(",", w.Exposed.Select(s => $"{s.L}-{s.R}"))} peak={peak:0.0}");
        }
        if (_tracker.Rejected is { Count: > 0 } rejected)
        {
            sb.AppendLine("  rejected:");
            foreach (var r in rejected) sb.AppendLine("    " + r);
        }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "snowfall-debug.txt"), sb.ToString());
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _hook.Dispose();
        _server.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _tray.Visible = false;
        _tray.Dispose();
        foreach (var o in _overlays) o.Dispose();
        _deco.Dispose();
        _icon.Dispose();
        _ui.Dispose();
        Native.timeEndPeriod(1);
        base.ExitThreadCore();
    }

    /// <summary>Six-armed snowflake drawn at runtime, so the tray needs no image assets.</summary>
    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = new Pen(Color.FromArgb(255, 40, 90, 160), 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var core = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            foreach (var pen in new[] { outline, core })
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3;
                    float c = MathF.Cos((float)a), s = MathF.Sin((float)a);
                    g.DrawLine(pen, 16, 16, 16 + 13 * c, 16 + 13 * s);
                    float bx = 16 + 8 * c, by = 16 + 8 * s;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        double b = a + side * Math.PI / 4;
                        g.DrawLine(pen, bx, by, bx + 4.5f * MathF.Cos((float)b), by + 4.5f * MathF.Sin((float)b));
                    }
                }
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        Native.DestroyIcon(h);
        return icon;
    }
}
