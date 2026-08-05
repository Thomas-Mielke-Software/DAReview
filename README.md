# Dark Ambient Radio – Album Review Workflow

Windows-Anwendung (C# / .NET 9 / WPF) für den Album-Review-Ablauf von **Dark Ambient Radio**:
Bandcamp-Review-Code einlösen → herunterladen → entpacken → auf 192 kbit/s umkodieren und
normalisieren → im Player durchhören und pro Track freigeben/ablehnen → freigegebene Tracks für die
Ausstrahlung bereitstellen.

## Aufbau

| Projekt | Zweck |
|---|---|
| `src/DarkAmbientRadio.Core` | Kernlogik: Config, Bandcamp-Automation (Playwright), Entpacken, Audio (ffmpeg/mp3gain), Bibliothek, Review-State, Airplay |
| `src/DarkAmbientRadio.App`  | WPF-Oberfläche (MVVM): Akquise-Auslöser, Albenliste, Player, Track-Review, Einstellungen |
| `tests/DarkAmbientRadio.Core.Tests` | xUnit-Tests der reinen Logik |
| `tools/` | Ablage für `ffmpeg.exe` und `mp3gain.exe` (siehe `tools/README.md`) |

## Einrichtung

1. **.NET 9 SDK** (installiert).
2. **Externe Tools** nach `tools/` legen: `ffmpeg.exe`, `mp3gain.exe` – siehe [tools/README.md](tools/README.md).
3. **Playwright-Browser** einmalig installieren (lädt Chromium herunter, ~130 MB):

   ```bash
   dotnet build
   pwsh src/DarkAmbientRadio.App/bin/Debug/net9.0-windows/playwright.ps1 install chromium
   ```

   Ist `pwsh` (PowerShell Core) nicht installiert, funktioniert auch Windows PowerShell – einfach
   ohne `pwsh`-Präfix aufrufen:

   ```powershell
   src/DarkAmbientRadio.App/bin/Debug/net9.0-windows/playwright.ps1 install chromium
   ```

4. Bauen & starten:

   ```bash
   dotnet run --project src/DarkAmbientRadio.App
   ```

## Konfiguration

Beim ersten Start werden Defaults verwendet; über **Einstellungen** anpassbar und gespeichert unter
`%APPDATA%\DarkAmbientRadio\config.json`. Alle Verzeichnisse leiten sich vom Cloud-Basisverzeichnis
(`D:\Nextcloud`) ab:

| Zweck | Default |
|---|---|
| Archiv (320, unberührt) | `D:\Nextcloud\Multimedia\Music\Styles\Dark Ambient` |
| Review (192k, Queue) | `D:\Nextcloud\Dark Ambient Review` |
| Airplay (freigegeben) | `D:\Nextcloud\Dark Ambient 192kbps` |
| Download (ZIP-Zwischenablage) | `%USERPROFILE%\Downloads` |

## Bedienung

- **Code einlösen** – liest den Code aus der Zwischenablage (Form `12ab-3cd4`), öffnet ein
  Browserfenster und durchläuft die Cryo-Chamber-„yum"-Seite. Ist eine Anmeldung nötig
  (reCAPTCHA / Login-Link per E-Mail), erscheint **» Weiter (Login erledigt)** – erst manuell im
  Browser einloggen, dann klicken. Danach läuft alles automatisch bis das Album im Review liegt –
  es wird anschließend ausgewählt und die Wiedergabe startet. Dasselbe gilt für bereits
  heruntergeladene ZIPs, die per Drag & Drop auf die Albenliste gezogen werden.
- **Albenliste** – zeigt jedes Review-Album mit Hördurchgang in Prozent (100 % = einmal komplett
  durchgehört, 200 % = zweimal). Klick startet die automatische Wiedergabe ab Track 1;
  Doppelklick auf einen Track spielt gezielt diesen.
- **Drag & Drop auf die Albenliste** – akzeptiert heruntergeladene `.zip`-Dateien *und* fertige
  Album-Ordner. ZIPs durchlaufen den vollen Ablauf (entpacken → Archiv → 192k → Review);
  Ordner werden **unverändert** in den Review-Ordner *verschoben* (nicht umkodiert). Nur wenn
  Quelle und Ziel auf verschiedenen Laufwerken liegen, wird kopiert – dann fragt die App nach,
  ob der Ursprungsordner gelöscht werden soll (Voreinstellung: nein).
- **Aa Artist / Aa Album** – normalisiert die Schreibweise des Artists bzw. des Album-Titels
  (`ETERNAL VOID` → `Eternal Void`), und zwar in einem Rutsch im Ordnernamen, in allen
  Track-Dateinamen und in den ID3-Tags. Bewusst mixed-case geschriebene Namen (`DiN`, `McCoy`)
  und römische Ziffern bleiben unangetastet.
- **Approve/Reject** – Entscheidung pro Track. Sind **alle** Tracks entschieden, wird **Airplay**
  aktiv: die freigegebenen Tracks werden nach `Dark Ambient 192kbps` kopiert; bei Ablehnungen
  erhält der Ordner den kürzeren der beiden Zusätze `[OHNE TRACK …]` / `[NUR TRACK …]`.

Der Review-Fortschritt liegt als verstecktes `.review.json` im jeweiligen Albumordner und wandert
so mit der Nextcloud-Synchronisation.

## Tests

```bash
dotnet test
```

## Noch offen / bewusst später

- **Live-Selektoren der Bandcamp-Seite** in `BandcampRedeemer` sind ein erster, defensiver Entwurf
  und müssen bei einem echten Durchlauf verifiziert/nachgezogen werden.
- **ezstream/Playlist-Skript** auf dem Debian-Upstream (neue Tracks erkennen) – separater Schritt.
- **Thunderbird-Code-Quelle** – Interface `IReviewCodeSource` ist vorbereitet; aktiv ist die
  Zwischenablage.
