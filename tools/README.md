# Externe Werkzeuge

Diese beiden Open-Source-Programme werden zur Laufzeit gebraucht und aus Lizenz-/Größen­gründen
**nicht** mit eingecheckt. Lege die Windows-Binaries hier ab – der Build kopiert sie automatisch
nach `bin\...\tools\`, und die App findet sie dort (alternativ über den PATH oder einen in den
Einstellungen gesetzten Pfad).

## ffmpeg.exe
- Zweck: Recode 320 → 192 kbit/s.
- Bezug: https://www.gyan.dev/ffmpeg/builds/ (z. B. „ffmpeg-release-essentials.zip"), aus dem
  Archiv nur `bin\ffmpeg.exe` hierher kopieren.
- Lizenz: LGPL/GPL.

## mp3gain.exe
- Zweck: Track-Normalisierung auf 86 dB (ReplayGain-Referenz 89 dB − 3).
- Bezug: https://sourceforge.net/projects/mp3gain/ (MP3Gain 1.6.x, Kommandozeilen-Version).
- Lizenz: LGPL.

Ergebnis:

```
tools/
  ffmpeg.exe
  mp3gain.exe
```

## Playwright-Chromium

Kommt **nicht** in diesen Ordner: der Browser wird von Playwright selbst verwaltet und landet im
Cache unter `%USERPROFILE%\AppData\Local\ms-playwright`. Einmalig nach dem ersten Build
installieren (lädt ~130 MB):

```bash
dotnet build
pwsh src/DarkAmbientRadio.App/bin/Debug/net9.0-windows/playwright.ps1 install chromium
```

Ohne `pwsh` (PowerShell Core) tut es auch Windows PowerShell – dann ohne das `pwsh`-Präfix:

```powershell
src/DarkAmbientRadio.App/bin/Debug/net9.0-windows/playwright.ps1 install chromium
```

Das Skript `playwright.ps1` entsteht erst beim Build im Ausgabeverzeichnis; für einen
Release-Build entsprechend `bin\Release\net9.0-windows\` verwenden.

- Zweck: Bandcamp-Code-Einlösung und Download (headed, persistente Session).
- Die Anmeldedaten/Session liegen separat in `%APPDATA%\DarkAmbientRadio\browser`.
