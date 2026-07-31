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
