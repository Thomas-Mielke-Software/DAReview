# CLAUDE.md — DAReview

Guidance for future Claude sessions working in this repo.

## What this is
A Windows desktop app that automates the album-review workflow for the web radio
**"Dark Ambient Radio"**: redeem a Bandcamp review code → download → unzip → re-encode to
192 kbit/s + normalise → review in a player (per-track approve/reject, listen counter) →
publish approved tracks to the airplay folder. Broadcast is done downstream by ezstream →
Shoutcast on a Debian server (out of scope here).

## Stack & layout
- **C# / .NET 9, WPF (MVVM, CommunityToolkit.Mvvm).** Playwright (.NET) for browser automation,
  CliWrap for ffmpeg/mp3gain, xUnit for tests.
- `src/DarkAmbientRadio.Core` — all logic (config, Bandcamp, files, audio, library, review, airplay).
- `src/DarkAmbientRadio.App` — WPF UI (acquisition trigger, album list, player, settings).
- `tests/DarkAmbientRadio.Core.Tests` — xUnit; the pure logic is covered here.
- **Solution is `DAReview.sln`; projects/namespaces are still `DarkAmbientRadio.*`** (only the
  solution was renamed). Don't "fix" this unless asked.

## Build / test / run
```bash
dotnet build DAReview.sln
dotnet test tests/DarkAmbientRadio.Core.Tests/DarkAmbientRadio.Core.Tests.csproj
dotnet run --project src/DarkAmbientRadio.App
```
The running app locks `DarkAmbientRadio.Core.dll` — stop `DarkAmbientRadio.App.exe` before rebuilding.
In the WPF App project, `System.IO` and `System.Linq` are **not** in the implicit usings — add them explicitly.

## External dependencies (not in repo)
- **ffmpeg** + **mp3gain** — resolved via `AppConfig` paths → bundled `tools/*.exe` → PATH
  (`ToolLocator`). On the dev machine: ffmpeg via winget (Gyan.FFmpeg), mp3gain 1.4.6 at
  `C:\Program Files (x86)\MP3Gain\mp3gain.exe`. mp3gain uses `/`-style flags; only its GUI needs
  MSCOMCTL.OCX, the CLI is standalone.
- **Playwright Chromium**: `pwsh <appOutput>/playwright.ps1 install chromium` (works with
  Windows PowerShell too if no `pwsh`).
- **Config** lives at `%APPDATA%\DarkAmbientRadio\config.json` (not in repo). The persistent
  Bandcamp login/session is in `%APPDATA%\DarkAmbientRadio\browser`.

## Never commit
`config.json` and the `browser` profile hold session state and live in `%APPDATA%`, not the repo —
keep it that way. `bin/`, `obj/`, `.vs/`, `tools/*.exe` are gitignored. When staging, verify none of
these (or credentials) sneak in.

## Non-obvious conventions (easy to get wrong)
- **Normalisation "86 dB" = mp3gain track gain** at ReplayGain reference 89 dB minus 3 (`/d -3`),
  NOT LUFS/loudnorm.
- **Airplay folder suffix uses square brackets**: `[OHNE TRACK 2, 3 und 5]` / `[NUR TRACK 1 UND 4]`,
  whichever string is **shorter**; rejected track files are omitted. See `TrackListFormatter`.
- **Listen counter is a percentage**: +100%/trackcount per fully-played track (200% = twice through).
  Persisted as `CompletedTrackPlays` in a hidden **`.review.json`** sidecar per album folder (travels
  with Nextcloud sync). Decisions per track number live there too.
- **Track filename schema**: `Artist - Album - NN Title.mp3`; `NN` after the last " - " is the number.
- **Directory defaults** (all under `CloudBase` = `D:\Nextcloud`, individually overridable):
  Archive = `…\Multimedia\Music\Styles\Dark Ambient` (untouched MP3 320 master — note the deep path),
  Review = `…\Dark Ambient Review` (192k queue), Airplay = `…\Dark Ambient 192kbps`.

## Bandcamp flow (verified against the live page)
After redeeming, the code page navigates straight to `bandcamp.com/download?...` — there is **no
"Code redeemed!" text** (waiting for it was a bug). The format picker is a native `<select>`; MP3 320
has option value **`mp3-320`** (label includes size). "Download" is a plain `<a>` whose accessible name
is exactly "Download". Selecting a format updates the link href async — wait ~1.5 s before clicking.
Login is manual (reCAPTCHA / email magic link); persistent context means it's usually needed only once.

## Player quirks
- Playback is driven from **code-behind** (`MediaElement` in `MainWindow`), bridged to the MVVM
  `MainViewModel` via `PlayFileRequested` / `StopRequested` events. Position slider, play/pause and
  listen counting live in `MainWindow.xaml.cs`.
- **Cold-start auto-play**: the hidden `MediaElement` (Height=0) is slow to become play-ready at launch
  and can silently drop the first `Play()` (position sticks at 0:00). There is no "ready" event, so
  `MainWindow.xaml.cs` uses a **start watchdog**: after every `Play()`, a 1 s `DispatcherTimer` checks
  whether `Player.Position` advances; if not, it forces a full source reload (`Source = null` → re-assign
  → `Play()`), bounded at 10 attempts. Auto-play therefore starts right after first render (no fixed
  delay). The robust alternative is `System.Windows.Media.MediaPlayer` (same engine, no visual
  binding) — offered but the user declined. Don't swap to NAudio.

## Theme
UI palette matches darkambientradio.de: background `#111111`/`#000000`, text white, primary accent
cyan `#99EEFF`, secondary accent lime `#93C900`, font Verdana. App icon (`appicon.ico`) is the "DAR"
lettering (Eccentric font) in gold on a dark tile.
