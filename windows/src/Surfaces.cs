using System.Diagnostics;
using System.Text.Json;

namespace Snowfall;

/// <summary>A horizontal edge published by another app (e.g. a CalWall panel) where snow can pile.</summary>
internal readonly record struct ExternalSurface(IntPtr Id, int Left, int Top, int Right);

/// <summary>
/// Reads <c>%LOCALAPPDATA%\Snowfall\surfaces\*.json</c>:
/// <c>{"pid": 123, "surfaces": [{"left": 0, "top": 0, "right": 0}]}</c> in physical virtual-screen pixels.
/// Files whose writer process is gone are ignored.
/// </summary>
internal sealed class SurfaceStore
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snowfall", "surfaces");

    private readonly Dictionary<string, (DateTime Stamp, int Pid, List<(int L, int T, int R)> Rects)> _cache = new();

    public List<ExternalSurface> Current { get; } = new();

    public void Poll()
    {
        Current.Clear();
        string[] files;
        try { files = Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.json") : Array.Empty<string>(); }
        catch { return; }

        foreach (var key in _cache.Keys.Except(files).ToList()) _cache.Remove(key);

        for (int f = 0; f < files.Length; f++)
        {
            var path = files[f];
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                if (!_cache.TryGetValue(path, out var entry) || entry.Stamp != stamp)
                {
                    entry = (stamp, 0, new List<(int, int, int)>());
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    var root = doc.RootElement;
                    entry.Pid = root.TryGetProperty("pid", out var pid) ? pid.GetInt32() : 0;
                    if (root.TryGetProperty("surfaces", out var arr))
                        foreach (var s in arr.EnumerateArray())
                            entry.Rects.Add((s.GetProperty("left").GetInt32(), s.GetProperty("top").GetInt32(),
                                s.GetProperty("right").GetInt32()));
                    _cache[path] = entry;
                }

                if (!IsAlive(entry.Pid)) continue;
                for (int i = 0; i < entry.Rects.Count; i++)
                {
                    var (l, t, r) = entry.Rects[i];
                    if (r - l < 10) continue;
                    // Stable synthetic id far outside the HWND range.
                    long id = (1L << 48) + Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(path) % 100000) * 100L + i;
                    Current.Add(new ExternalSurface(new IntPtr(id), l, t, r));
                }
            }
            catch
            {
                // Half-written or malformed file: try again next poll.
            }
        }
    }

    private static bool IsAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
