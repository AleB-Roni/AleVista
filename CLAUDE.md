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
  Edid.cs               Physischer Bildschirm-Schlüssel aus der EDID (gleich auf allen PCs)
  Models.cs             Profile, KnownMonitor, AppSettings, AppData, Store (%APPDATA%\AleVista\data.json)
  AppController.cs      Logik zwischen API, Daten und UI; „Master“-Vorlage, Vorschau-Daten
  UI/Theme.cs           Dunkles Theme, Farben, DPI-Skalierung S(), Btn, Dark-Menü-Renderer
  UI/Controls.cs        LayoutPreview, ProfileCard, AddCard, MonitorRow (alles selbst gezeichnet)
  UI/Dialogs.cs         DarkForm-Basis, InputDialog, ConfirmForm (Countdown), IdentifyForm
  UI/ProfileEditorForm.cs  Profil anlegen/bearbeiten
  UI/MainForm.cs        Hauptfenster mit Reitern, Tray, Hotkeys Strg+Alt+1–9, Autostart (HKCU\...\Run)
  UI/SharePage.cs       Reiter „Maus & Tastatur teilen“, Schlüssel-Dialog
  UI/ShareControls.cs   Toggle, MachineCanvas (Anordnung der PCs, ziehbar), PeerRow, HotkeyCard, TabStrip
  Share/ShareService.cs Maus/Tastatur teilen: Verbindungen, Suche, Kantenwechsel, Weiterleiten/Nachspielen, Zwischenablage
  Share/ShareProtocol.cs Nachrichten, AES-GCM-Kanal (SecureChannel), PeerConnection (Lese-/Schreib-Thread)
  Share/ShareGeometry.cs MachineView/ShareWorld (Anordnung), Einrasten/Andocken
  Share/InputHook.cs    Low-Level-Hooks auf eigenem Thread (Per-Monitor-DPI), ClipboardWatcher
  Share/ShareNative.cs  P/Invoke: Hooks, SendInput, Cursor, EnumDisplayMonitors
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

### Maus & Tastatur teilen
- Alle PCs mit demselben Schlüssel (`AppData.Share.Key`) finden sich per UDP-Broadcast (Port 24861, mit Schlüssel-Fingerabdruck) und verbinden sich per TCP 24860. Sitzungsschlüssel = HMAC(PBKDF2(Schlüssel), Zufall beider Seiten), jede Nachricht AES-GCM.
- Koordinaten immer in **physischen Pixeln** (Hook-Thread läuft mit Per-Monitor-DPI v2, Bildschirme über `EnumDisplayMonitors`).
- „Welt“: jeder PC hat einen Offset (`SharePeer.X/Y`) relativ zu diesem PC. Beim Ziehen wird die Anordnung an alle verteilt (`Arrangement`), jeder rechnet sie in eigene Koordinaten um.
- Der **steuernde** PC rechnet alles: Cursor wird geparkt, Deltas aus dem LL-Hook → virtuelle Position im Ziel-PC → absolute Position wird geschickt. Ziel-PC spielt per `SendInput` nach. Eigene Hooks ignorieren injizierte Ereignisse (`LLMHF_INJECTED`).
- Threads: Einstellungen nur auf dem UI-Thread ändern; Hook/Netz lesen den Schnappschuss `ShareWorld`; Steuerzustand unter `_lock`. Keine `SendInput`-Aufrufe aus dem Hook-Callback (über `InputHook.Post`).
- Doppelte Verbindungen: es bleibt die, die der PC mit der kleineren `MachineId` aufgebaut hat.
- **Gemeinsame Profile**: PCs tauschen ihre Monitore als JSON aus (`Displays`, `RemoteDisplays` in `SharePeer.Displays` gespeichert). `Profile.Remote` = Monitorauswahl je anderem PC, `Profile.KeepLocal` = diesen PC nicht ändern. Aktivieren schickt `ApplyDisplays` (dort `AppController.ApplyMonitors`, ohne Bestätigungsdialog), Zurücksetzen `RevertDisplays`. Vorschau über alle PCs: `CombinedPreview` (Gruppe 0 = dieser PC grün, andere blau).
- Profil-Editor mit verbundenen PCs: „Bildschirme verteilen“ – je physischem Bildschirm eine `ScreenChoiceRow` (PC 1 | PC 2 | Aus). Welcher Bildschirm dort welcher hier ist: `AppController.MonitorLinks` – erst manuell (`SharePeer.Links`, Dialog `MonitorLinkForm`), dann automatisch Seriennummer (`Edid.PhysicalKey`) → Modellkürzel aus der Monitor-ID → Name, jeweils nur bei eindeutigem Treffer. `ScreenGroups` baut daraus die Zeilen. Der Editor arbeitet auf einer Kopie (`Profile.Copy/CopyFrom`) und wird nach geänderter Zuordnung per `DialogResult.Retry` neu aufgebaut. Ein PC ohne Bildschirm bleibt unverändert (`KeepLocal` bzw. keine `RemoteSelection`).

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
