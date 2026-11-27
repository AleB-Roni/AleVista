# AleVista

Monitorprofile für Windows – Bildschirme (z. B. den Fernseher) per Klick, Tray-Menü oder Tastenkürzel an- und abschalten, ohne jedes Mal in die Anzeigeeinstellungen zu müssen.

## Funktionen

- **Profile** mit frei wählbaren Monitoren (an/aus), optionalem Hauptmonitor und Kürzel **Strg+Alt+1–9**
- **Vorlage mit allen Monitoren**: AleVista merkt sich die Anordnung, wenn alle Bildschirme an sind, und leitet daraus jedes Profil ab. Lücken durch abgeschaltete Monitore werden automatisch geschlossen.
- **Aktuelle Anordnung speichern**: speichert exakt Auflösung, Bildfrequenz und Position (für Spezialfälle)
- **Sicherheitsabfrage**: nach dem Umschalten 15 s „Beibehalten?“ – sonst wird automatisch zurückgesetzt
- **Tray-Symbol**: Linksklick → Profilmenü zum schnellen Umschalten
- **Identifizieren**: zeigt die Nummer groß auf jedem Bildschirm; Monitore lassen sich umbenennen (z. B. „Links“, „Mitte“, „TV“)
- **Desktop-Verknüpfung pro Profil** bzw. Kommandozeile: `AleVista.exe --profile "Ohne TV"` (auch für Stream Deck o. ä.)
- Autostart im Tray (`--tray`), Daten unter `%APPDATA%\AleVista\data.json`
- **Maus & Tastatur teilen** (wie „Mouse without Borders“): eine Maus und Tastatur für mehrere PCs, auf denen AleVista läuft – siehe unten

## Maus & Tastatur teilen

Reiter **„Maus & Tastatur teilen“**:

1. Auf dem ersten PC den Schalter einschalten – AleVista schlägt einen **Verbindungsschlüssel** vor (z. B. `K7QX-M2PA-…`). Übernehmen.
2. Auf dem zweiten PC ebenfalls einschalten und **denselben Schlüssel** eintragen.
3. Die PCs finden sich im lokalen Netzwerk automatisch (sonst: „PC per Name oder IP hinzufügen“).
4. In der **Anordnung** den anderen PC an die Bildschirmkante ziehen, an der er stehen soll. Die Anordnung wird auf alle PCs übertragen.

Dann einfach die Maus über die Kante schieben – Maus und Tastatur steuern jetzt den anderen PC.

| Kürzel | Funktion |
|---|---|
| Strg+Alt+F1 | zurück zu diesem PC |
| Strg+Alt+F2 … F4 | direkt zu PC 2 – 4 wechseln |
| Strg+Alt+F9 | Wechsel über Bildschirmkanten an/aus |

**Gemeinsame Profile über mehrere PCs:** Sobald ein anderer PC verbunden ist, zeigt der Reiter „Monitorprofile“ unter „Aktuelle Anordnung“ auch dessen Monitore (blau). Im Profil-Editor erscheinen alle Monitore beider PCs – pro PC wählst du, welche an sind, oder „nicht ändern“. Beim Aktivieren (Karte, Tray, Strg+Alt+1–9) werden alle beteiligten PCs gleichzeitig umgeschaltet; „Zurücksetzen“ in der Sicherheitsabfrage setzt auch die anderen PCs zurück. Ideal für Monitore, die an beiden PCs hängen und automatisch auf den Eingang umschalten, der ein Bild liefert.

Außerdem: **Zwischenablage teilen** (Text), **Ecken sperren**, Mauszeiger ausblenden, solange ein anderer PC gesteuert wird. Wird der ferngesteuerte PC direkt mit seiner eigenen Maus/Tastatur benutzt, gibt er die Steuerung sofort zurück.

Hinweise:
- Netzwerk: TCP-Port **24860** (Verbindung, AES-GCM-verschlüsselt) und UDP-Port **24861** (Suche). Beim ersten Einschalten fragt die Windows-Firewall – Zugriff im **privaten Netzwerk** erlauben.
- Fenster, die als Administrator laufen (z. B. Task-Manager), lassen sich nur bedienen, wenn AleVista dort ebenfalls als Administrator läuft (Windows-Schutz). Strg+Alt+Entf und der Sperrbildschirm lassen sich nicht fernsteuern.

## Erste Schritte

1. `AleVista.exe` starten.
2. **„Alle aktivieren & einlesen“** klicken – alle angeschlossenen Monitore (inkl. TV) gehen an.
3. Falls nötig, in **„Windows-Anzeige“** die Anordnung zurechtschieben – AleVista übernimmt sie automatisch.
4. **„+ Neues Profil“**: z. B. „Ohne TV“ (nur die drei Hauptmonitore) und „Mit TV“. Kürzel vergeben, fertig.

## Bauen

GitHub Actions baut bei jedem Push nach `main` eine eigenständige `AleVista.exe` (kein .NET nötig) → Reiter **Actions** → Artefakt *AleVista-win-x64*.
Ein Tag `v1.0.0` erzeugt zusätzlich ein Release mit der `.exe`.

Hochladen: `upload-to-github.command` (macOS) bzw. `upload-to-github.bat` (Windows) doppelklicken.

Lokal: `dotnet publish src/AleVista.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out`

## Technik

C# / .NET 8 / WinForms. Umschalten über die Windows-CCD-API (`QueryDisplayConfig` / `SetDisplayConfig`), Monitore werden über ihren Gerätepfad (EDID/Anschluss) wiedererkannt – auch nach Neustarts, wenn sich die Grafikkarten-LUIDs ändern.
