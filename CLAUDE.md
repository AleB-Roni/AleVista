# AleVista – Projektkontext für Claude Code

Windows-Desktop-App für Monitorprofile: Bildschirme (z. B. einen TV neben drei Hauptmonitoren) per Profil, Tray-Menü oder Tastenkürzel an- und abschalten, ohne die Windows-Anzeigeeinstellungen zu öffnen.

## Kommunikation
- Mit dem Nutzer (Roni) **auf Deutsch** sprechen. UI-Texte, Meldungen und Kommentare im Code ebenfalls auf Deutsch.
- Roni entwickelt meist auf einem **Intel-MacBook** → dort kann nicht kompiliert/getestet werden. Gebaut wird über **GitHub Actions** (Windows-Runner).

## Stack
- C# / .NET 8 / WinForms (`net8.0-windows`), `Nullable` + `ImplicitUsings` aktiv
- Keine NuGet-Abhängigkeiten – bitte so lassen, wenn möglich
- Ausgabe: eine einzelne, self-contained `AleVista.exe` (win-x64)

## Struktur
```
src/
  Program.cs            Einstieg, Single-Instance (Mutex + EventWaitHandle), CLI (--profile "Name", --tray)
  Native.cs             P/Invoke: CCD-API (QueryDisplayConfig/SetDisplayConfig), Hotkeys, DWM
  DisplayManager.cs     DisplaySnapshot (Pfade+Modi), Layout (Lücken schließen, Hauptmonitor auf 0,0), Apply()
  Models.cs             Profile, KnownMonitor, AppSettings, AppData, Store (%APPDATA%\AleVista\data.json)
  AppController.cs      Logik zwischen API, Daten und UI; „Master“-Vorlage, Vorschau-Daten
  UI/Theme.cs           Dunkles Theme, Farben, DPI-Skalierung S(), Btn, Dark-Menü-Renderer
  UI/Controls.cs        LayoutPreview, ProfileCard, AddCard, MonitorRow (alles selbst gezeichnet)
  UI/Dialogs.cs         DarkForm-Basis, InputDialog, ConfirmForm (Countdown), IdentifyForm
  UI/ProfileEditorForm.cs  Profil anlegen/bearbeiten
  UI/MainForm.cs        Hauptfenster, Tray, Hotkeys Strg+Alt+1–9, Autostart (HKCU\...\Run)
.github/workflows/build.yml   Build + automatisches Release bei jedem Push nach main (v1.0.<run_number>)
upload-to-github.command      Doppelklick-Upload für macOS
upload-to-github.bat          Upload für Windows
```

## Wichtige Konzepte
- **Monitor-ID** = `monitorDevicePath` aus `DisplayConfigGetDeviceInfo` (stabil über Neustarts). Fallback: `target:<LUID>:<targetId>`.
- **Adapter-LUIDs ändern sich bei jedem Neustart** → `DisplaySnapshot.Adapters` speichert LUID → Adapter-Gerätepfad, `Apply()` mappt auf aktuelle LUIDs um.
- **Master/Vorlage**: Anordnung mit allen Monitoren an. Wird automatisch neu gelernt, wenn die aktive Konfiguration eine Obermenge der bisherigen ist. Profile ohne `Fixed` werden per `Master.Filter()` + `Layout.Normalize()` abgeleitet.
- **Fixed-Profile** („Aktuelle Anordnung speichern“): exakte Pfade/Modi als Base64 der blittable Structs (`MemoryMarshal`). Struct-Layouts in `Native.cs` daher nicht ändern (PATH_INFO = 72 Bytes, MODE_INFO = 64 Bytes).
- `Apply()` versucht zuerst `SDC_USE_SUPPLIED_DISPLAY_CONFIG`, Fallback `SDC_TOPOLOGY_SUPPLIED` ohne Modi.
- Nach dem Umschalten 15-s-Bestätigung (`ConfirmForm`), sonst Rücksetzen auf den vorherigen Snapshot.
- In RDP-Sitzungen verweigert Windows `SetDisplayConfig` (Fehler 5) – immer lokal testen.

## Bauen
- Lokal (Windows, .NET 8 SDK):
  `dotnet publish src/AleVista.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out`
- Auf dem Mac: nur `dotnet build src/AleVista.csproj` zur Compiler-Prüfung möglich (`EnableWindowsTargeting` ist gesetzt), nicht ausführbar.
- Push nach `main` → GitHub Actions baut und veröffentlicht ein Release mit `AleVista.exe`.

## Konventionen
- Neue Apps von Roni heißen „Ale…“ (Wortspiel). Dieses Projekt: **AleVista**.
- GitHub-Repo: `AleB-Roni/AleVista`
- Commits/Push: Roni nutzt lieber die Doppelklick-Datei `upload-to-github.command` als Git-Befehle.
- UI-Größen immer über `Theme.S(...)` skalieren; Farben nur aus `Theme`.
- Labels mit `UseMnemonic = false`, `TextRenderer` mit `NoPrefix` (Profilnamen können „&“ enthalten).
