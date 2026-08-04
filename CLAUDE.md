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
  CliWrap for ffmpeg/mp3gain, TagLibSharp for ID3 tags, xUnit for tests.
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
- **Track filename schema**: `Artist - Album - NN Title.mp3`. The **track number anchors the split** —
  do *not* count separators from either end. Both album and title may contain " - " themselves;
  compilations are named `Label - Album - NN TrackArtist - Title` (e.g. `Cryo Chamber - Tomb of
  Primordials - 01 Dahlia's Tear - Crystal Scars…`). `TrackFileName.TryParse` splits on " - " and
  scans left to right (from index 1) for the first segment matching `^\d{1,3}\b`: segment 0 = artist,
  everything before the hit = album, the hit and everything after = `NumberedTitle`.
  Taking the number after the *last* separator was a bug — such albums parsed to **zero tracks** and
  were silently dropped by `LoadReviewQueue`, invisible in the UI even after a restart.
  This schema is what the **renaming** (`AlbumNormalizer`) works on; it is **not** a precondition for
  reviewing — see the next point.
- **Track numbers are derived, not demanded** (`TrackNumbering.Assign`, since 2026-08-04). Only three
  things need a number at all — sort order, the decision keys in `.review.json` and the
  `[OHNE TRACK 2 und 3]` suffix — so an album folder is numbered by a cascade: filename
  (`TrackNumberParser`: Bandcamp schema, else a leading `^\d{1,3}(?!\d)[\s._-]` — the lookahead keeps
  `2001 - …` from yielding 200) → **ID3 track frame** → **position in the folder** (ordinal by
  filename). Numbers that were found keep their claim, gaps included; collisions and unnumbered files
  fill the lowest free numbers, so the result is unique and deterministic for a given file set.
  Before this, `TrackItem.FromFile` returned null for anything off-schema and `LoadReviewQueue`
  dropped the album without a word: a hand-dropped scene release (`01_massacre_divino_-_agarez.mp3`,
  no " - " anywhere) was correctly re-encoded and normalised but never appeared in the list.
  `LoadReviewQueue` now only skips folders with **no MP3 at all**.
  The ID3 step **must not touch cloud placeholders** (`CloudFiles.IsPlaceholder` guard): the list is
  rescanned on every refresh, and app-triggered on-demand downloads are what get the app blocked.
  Positional numbers are only stable while the file set is — adding a file later shifts the decision
  keys of everything after it. Keying decisions by filename would fix that but needs a sidecar
  migration.
- **Drag & drop onto the album list** takes ZIPs *and* album folders, and **both run the full
  pipeline**: archive → re-encode → normalise → review (`AcquisitionWorkflow.ProcessZipAsync` /
  `ProcessFolderAsync`). A dropped folder is therefore first moved into the *archive* as the
  untouched master, not into the review dir. Folders **already inside the archive** stay put and
  only get their review copy rebuilt; folders inside the *review* dir are refused (a failed
  re-encode would otherwise leave them half-archived). `FolderImporter` prefers `Directory.Move`
  and only copies across volumes; it never deletes the source itself but reports it back so
  `MainViewModel` can ask first (default "no").
  Until 2026-08-02 folder drops were only relocated into the review dir and skipped both steps —
  that is where the 320k, un-normalised albums in the review queue came from.
- **Skipping a pointless re-encode**: before a folder drop is processed, `Mp3StreamProbe` checks
  whether the album *already* is CBR at the target bitrate; only then does the user get asked
  whether to skip the encode (normalisation runs either way — `ProcessAlbumAsync(reencode: false)`
  copies instead of calling ffmpeg, then still runs mp3gain). The check walks **every MPEG frame
  header** and compares the bitrate field, because an *average* bitrate cannot tell CBR from VBR:
  LAME ABR at 192 and V2 both average around the target and are not CBR. Verified against real
  ffmpeg output (CBR 192/320 → yes, `-q:a 2` and `-b:a 192k -abr 1` → no). The Xing/Info/VBRI
  frame is excluded from the comparison, or every tagged CBR file would look variable.
- **Nextcloud placeholders break the pipeline.** Files in a synced folder can be dehydrated
  (`Offline | RecallOnDataAccess`) — in practice it is always `cover.jpg`, because nothing ever
  opens it while the MP3s get played. `File.Copy` and ffmpeg then die with *"Der Cloudvorgang war
  nicht erfolgreich"*. `CloudFiles` hydrates by **setting `FILE_ATTRIBUTE_PINNED` (0x80000) and
  waiting** for the flags to clear, then restoring the previous pin state. Do *not* "just read the
  first byte" to force hydration: that is an app-triggered automatic download, and after a few of
  them **Windows blocks the app** (Einstellungen → Datenschutz und Sicherheit → Automatische
  Dateidownloads), after which every further read fails instantly. A retry loop is the fastest way
  into that block. Measured on real covers, a pinned fetch took **18–51 s** — budget minutes, not
  a retry with a 500 ms backoff.
- **Hydrate before moving, not after.** `ProcessFolderAsync` calls `CloudFiles.HydrateFolder` on
  the dropped folder *before* `FolderImporter` moves it into the archive: once a placeholder has
  moved to a new path inside the sync root, the client must replay that move on the server before
  it can serve the content at all.
- A drop of several albums isolates failures per item — one bad album used to abort the whole
  batch, which is why two albums silently never got processed.
- **Normalisation buttons ("Aa Artist" / "Aa Album")** fix *capitalisation only*, across folder name,
  track filenames **and** ID3 tags (`AlbumNormalizer`). `TitleCaseNormalizer` deliberately skips
  already-mixed-case words ("McCoy", "DiN") and roman numerals, and keeps minor words ("of", "the")
  lowercase unless first/last. Case-only renames need a **temp-name detour** — Windows treats
  `x.mp3`/`X.mp3` as the same path and `File.Move` would fail. The op is idempotent, so the caller
  retries on `IOException` (the player may still hold the current track open).
- **Directory defaults** (all under `CloudBase` = `D:\Nextcloud`, individually overridable):
  Archive = `…\Multimedia\Music\Styles\Dark Ambient` (untouched MP3 320 master — note the deep path),
  Review = `…\Dark Ambient Review` (192k queue), Airplay = `…\Dark Ambient 192kbps`.

## Bandcamp flow (verified against the live page)
After redeeming, the code page navigates straight to `bandcamp.com/download?...` — there is **no
"Code redeemed!" text** (waiting for it was a bug). The format picker is a native `<select>`; MP3 320
has option value **`mp3-320`** (label includes size). The download control is a plain `<a>` whose
accessible name is **"Download &lt;album title&gt;"** — *not* exactly "Download" (matching it exactly
was a bug: the wait never completed). Match `^\s*Download\b`, which also excludes "Need download
help?". Selecting a format updates the link href async — wait ~1.5 s before clicking.
Login is manual (reCAPTCHA / email magic link); persistent context means it's usually needed only once.
When the ZIP isn't cached server-side the page shows **"Preparing…"** instead and only swaps in the
download link once built — `WaitForDownloadReadyAsync` polls for it (10 min budget, 15 s progress
heartbeat) before clicking. Clicking during "Preparing…" does nothing.
Closing the browser window cancels the run (`IBrowserContext.Close` → linked CTS), so the
manual-login gate can't hang on a dead browser.

## Player quirks
- **Nextcloud prefetch**: selecting an album fires a parallel (4×) fire-and-forget read of the first
  byte of every track (`MainViewModel.PrefetchTracks`, `ReadExactlyAsync`) so the whole album
  hydrates in one go instead of stalling at each track change. Opens with `FileShare.ReadWrite` —
  the player may already hold the current file.
- Playback is driven from **code-behind** (`MediaElement` in `MainWindow`), bridged to the MVVM
  `MainViewModel` via `PlayFileRequested` / `StopRequested` events. Position slider, play/pause and
  listen counting live in `MainWindow.xaml.cs`.
- **Standby kills the MediaElement**: resuming from sleep leaves it dead or already failed.
  `SystemEvents.PowerModeChanged` (Resume) → wait 2 s for the audio device → reload the same
  `_currentSource`, seek back via `_resumePosition` in `MediaOpened`, resume playing. Hence
  `OnMediaFailed` must *not* clear `_currentSource` — the recovery needs it. Unsubscribe in
  `OnClosing`: `SystemEvents` is static and would leak the window.
- **Window placement** is persisted into `AppConfig.Window` on closing (`RestoreBounds`, not
  `Left`/`Top`, so a maximised window can be un-maximised next start) and only restored when
  `WindowPlacement.IsOnScreen` still matches the current virtual desktop.
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
lettering (Eccentric font) **glowing cyan** on a dark rounded tile with a cyan border — 7 frames,
16–256 px, PNG-compressed. Regenerating it needs `Eccentric.ttf` (not in the repo; the user keeps it
in Nextcloud) and a GDI+ renderer: **Regular** outlines widened by a stroke, *not* `FontStyle.Bold`
— GDI+' synthetic bold closes the counters and destroys the letterforms. The glow is two blurs
(wide saturated `#12B8E8` + tight `#99EEFF`) behind a near-white core; frames ≤32 px need a
noticeably weaker halo or the lettering turns to mush. The website favicon set
(`favicon.ico` 16/32/48 + PNGs + webmanifest) comes out of the same renderer so site and app match.
