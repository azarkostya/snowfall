# Snowfall ❄

Snow on your desktop for **Windows** and **macOS**. Flakes fall over everything, pile up on the top edges of windows (and the Windows taskbar), slide off when you move a window and burst apart when you click a pile. Christmas lights twinkle along the taskbar (Windows) or under the menu bar (macOS), with a snowman and a tree.

Снег на рабочем столе для **Windows** и **macOS**: падает поверх всех окон, копится сугробами на окнах, осыпается, если окно сдвинуть, и рассыпается от клика. Гирлянда на панели задач или под строкой меню, снеговик и ёлка.

## Download

Grab the latest build from [Releases](https://github.com/azarkostya/snowfall/releases):

- **Windows 10/11:** `Snowfall-Windows.zip` — one self-contained `Snowfall.exe`, nothing to install.
- **macOS 13+ (Apple Silicon and Intel):** `Snowfall-macOS.zip` — `Snowfall.app`. The app is ad-hoc signed, not notarized, so the first time macOS will refuse to open it. Either right-click → Open, or run:

  ```bash
  xattr -dr com.apple.quarantine /Applications/Snowfall.app
  ```

## Features

| | Windows | macOS |
|---|---|---|
| Falling snow on every monitor, click-through | ✓ | ✓ |
| Piles on window tops; shaken off when a window moves or closes | ✓ | ✓ |
| Click a pile to crumble it (the click still reaches the app below) | ✓ | ✓ |
| Piles on the taskbar | ✓ | — |
| Christmas lights: twinkle / chase / steady | taskbar | under the menu bar |
| Snowman (waves now and then) and a tree | taskbar corner | bottom-left corner |
| Two switches: **Winter** (everything) and **Snowfall** (falling snow only; piles and lights stay) | ✓ | ✓ |
| Intensity: light / normal / blizzard | ✓ | ✓ |
| Start with the system | registry Run key | Login Items |
| Desktop shortcuts and command line (`--toggle`, `--snow`, …) | ✓ | — |
| Pauses on a monitor with a full-screen app (game, video) | ✓ | — |

Everything is controlled from the tray icon (Windows) or the menu bar snowflake (macOS).

## Windows

Command line (talks to the running instance; `--toggle`, `--on`, `--snow`, `--snow-on` start it if needed):

```
Snowfall.exe --toggle | --on | --off | --snow | --snow-on | --snow-off | --shake
```

The tray menu can put two shortcuts on the desktop: **Зима вкл-выкл** (`--toggle`) and **Снегопад вкл-выкл** (`--snow`).

Build from source (needs the .NET 8 SDK):

```powershell
dotnet publish windows/src/Snowfall.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

Or `windows/tools/install.ps1`, which builds, installs to `%USERPROFILE%\Snowfall`, creates the desktop shortcuts and starts it.

### Integration API (Windows)

Other apps can control Snowfall and give it surfaces to pile snow on.

**Commands** — named pipe `\\.\pipe\snowfall`, byte mode. Write one line, read one line of JSON:

```
status | toggle | on | off | snow | snow-on | snow-off | shake
```

```json
{"running":true,"enabled":true,"falling":true,"piles":true,"intensity":"Normal"}
```

**Surfaces** — drop JSON files into `%LOCALAPPDATA%\Snowfall\surfaces\`:

```json
{"pid": 1234, "surfaces": [{"left": 100, "top": 48, "right": 900}]}
```

Physical virtual-screen pixels (same space as `GetWindowRect`); `top` is the edge's y. Treated as lying under all windows. Ignored once process `pid` exits. This is how a desktop-wallpaper calendar can have snow on its panels.

Debug: `Snowfall.exe --debug` writes tracked windows, pile heights and filtered-out windows to `%TEMP%\snowfall-debug.txt` every 2 s.

## macOS

Build from source (Xcode command line tools, macOS 13+):

```bash
macos/build-app.sh
open macos/build/Snowfall.app
```

Window positions come from `CGWindowListCopyWindowInfo` (bounds only, so no Screen Recording permission is needed). Clicks on piles are observed with a global mouse monitor and never consumed.

## How it works

- One transparent, click-through, always-on-top window per display. Windows: per-pixel-alpha layered window drawn into a DIB and presented with `UpdateLayeredWindow`. macOS: borderless `NSWindow` at overlay level drawn with Core Graphics.
- Every ~70–100 ms the app lists visible windows front to back and computes which parts of each top edge are not covered by windows above it.
- Flakes have a depth: far ones are small, slow and faint, near ones big and bright; only near ones land. Piles are per-pixel height maps with an angle of repose and rounded ends.
- Maximized windows don't collect snow (their top edge is the top of the screen).

## License

[MIT](LICENSE)
