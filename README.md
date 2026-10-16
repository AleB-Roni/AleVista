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
