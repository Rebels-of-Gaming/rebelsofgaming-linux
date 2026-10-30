# Linux-Bauplan – Rebels of Gaming für Linux

Stand, Stufen, Annahmen und offene Fragen der Linux-App. Funktionsumfang wie die Windows-App
(`external/rog-windows/docs/WINDOWS-PFLICHTENHEFT.md`); hier stehen nur Linux-Besonderheiten und der Fortschritt.

## Stand

**Stufe 2 (Community lesen, 30.10.2026) – erledigt.** Stufe 1 (Konto + Start) und der Wechsel auf **Avalonia 12**
(Owner-Entscheidung F3) davor. Version 0.1.0, nicht veröffentlicht.

| Bereich | Stand |
|---|---|
| Repo, Lösung, CPM, `global.json`, Doku | steht |
| Avalonia **12.1.4** (+ `Avalonia.Wayland`), Tmds.DBus.Protocol 0.94.1, xUnit v3 | steht (siehe „Avalonia 12“) |
| Kern `RoG.Core` als Submodul | baut **unverändert** unter Linux (siehe „Geteilter Kern“) |
| Shell wie NavigationView „Auto“: ≥ 840 px aufgeklappt, 600–840 px Symbole, < 600 px Kopfleiste + Menü als Überlagerung | steht; Seitenleiste kompakt (36 px je Eintrag), passt bei 1280×800 ohne Scrollen (Screenshot-Test prüft das) |
| **Startseite** (Begrüßung, Gast-Hinweis, offene Abstimmungen, Onboarding, Live-Ticker, neueste News, nächste Termine) | steht; gegen die Produktion geprüft (Live-Ticker, News) |
| **Einstellungen** (Sprache DE/EN/FR/System, Design RoG-Dunkel/System/Hell, Desktop-Benachrichtigungen + Test, Über: Version, Plattform, Umgebung, Paket, Anzeige, Links, Copyright, Lizenzen) | steht |
| **Single-Instance** (D-Bus `org.freedesktop.Application`, Rückfall Unix-Socket), `rog://`-Links aus Startargument und zweiter Instanz | steht; unter Xvfb + echtem Session-Bus geprüft |
| **Fensterzustand** (Größe, Lage, maximiert) in `settings.json`, auch bei SIGTERM | steht; unter Xvfb geprüft |
| Login (PKCE + Loopback, System-Browser), Gastmodus, Abmelden | steht; echter Login gegen Staging/Produktion vom Owner noch zu testen |
| News (Liste, Mehr laden, Artikel nativ, Kommentare lesen; Artikel per Link öffnen) | steht |
| Nachrichten (Unterhaltungen, Suche, Verlauf lesen, gelesen markieren) | steht (nur lesen) |
| Konto (Name, Rang, Token-Speicher, Umgebung, Version, Abmelden) | steht |
| Token im Schlüsselbund (Secret Service), Rückfall „nur diese Sitzung“ | steht; Protokoll-Test gegen privaten D-Bus mit Test-Dienst (Anlegen, Ersetzen, Lesen, Entsperren per Prompt, Ablehnen, Löschen) |
| Desktop-Benachrichtigungen (org.freedesktop.Notifications) | steht; Protokoll-Test + „Test-Benachrichtigung“ in den Einstellungen; Glocke/DMs ab Stufe 3 |
| `xdg-open` (Browser, Spiele-Links), XDG-Ordner, Gerätename (PC/Steam Deck) | steht |
| **Squads** (Liste mit Logo + Rekrutierung, Umschalter Spiele; Squad-Seite mit Banner, Reitern Vorstellung/Line-up/Server und **Intern** nur für Member des Squads bzw. `squads.manage` mit Scope `squads:read`) | steht; gegen die Produktion geprüft (Gastmodus, ohne Intern) |
| **Mitglieder + Profile** (Suche verzögert, „Nur online“, Team-Ansicht, Avatar mit Status-Abzeichen; Profil mit Banner, Rang in Rangfarbe, Status + Aktivität, Motto, Über mich, Ort/Dabei seit/zuletzt gesehen, Squads, Rebel-Coins, Abzeichen, Spiele-Accounts) | steht; gegen die Produktion geprüft |
| **Status-Punkt** (`Controls/AvatarView`): grün online · blau Voice · rot RoG-Server · gold im Spiel · grau offline – zusätzlich Form (voller Punkt, Headset, Server, Controller, leerer Ring), Tooltip/Screenreader-Text | steht |
| **Server** (Karten mit Status als Form + Text, Spieler/Slots, Map, Passwort-Schloss, Spielerliste, **Verbinden** über `xdg-open` mit Prüfung per `xdg-mime`, sonst Adresse kopieren + Hinweis; „Adresse kopieren“; still jede Minute neu, `ApplyStatus` für Echtzeit) | steht; gegen die Produktion geprüft |
| **Kalender** (Liste 60 Tage nach Tag gruppiert, Monatsansicht 6×7 ab Montag mit Typfarben-Punkten, Tagesliste; Termin mit Zeit, Ort, Zahlen, eigener Antwort **nur anzeigen**, Teilnehmern, Server mit Verbinden, „In Kalender übernehmen“ = .ics) | steht |
| **Info** (Regeln, Seiten, Rechtstexte gruppiert; Regeln mit §-Abschnitten, wichtige markiert; „bitte bestätigen“ → Website) | steht |
| **Suche** (Strg+K, Lupe in Seitenleiste/Kopfleiste; Gruppen je Bereich; Treffer öffnen den Bereich in der App, sonst Browser) | steht |
| Deep Links Stufe 2 | `rog://squads/…`, `rog://u/…`, `rog://events/…`, `rog://servers/…`, `rog://info/…`, `rog://messages/…`, `rog://search?q=…`, `rog://open?url=…` öffnen den Eintrag |
| Tastatur + Fokus | Tab/Pfeile, Strg+1…9, Strg+, (Einstellungen), Strg+K (Suche), Strg+F (Mitgliedersuche), F5, Escape (Menü, Detail jeder Liste-Detail-Seite); sichtbarer Fokusrahmen |
| Tests | 132 xUnit-Tests inkl. 38 Headless-Screenshots (`artifacts/screenshots/`) und 7 D-Bus-Tests gegen einen privaten Bus; X11-Lauf unter Xvfb gegen die Produktion (Gastmodus) mit Bildschirmfotos (`artifacts/screenshots/x11-production/`) |
| CI | `.github/workflows/ci.yml` auf `[self-hosted, Linux, rog]` – **grün** (Lauf 41032873055: 94/94 Tests, auch die D-Bus-Tests) |
| Flatpak/AppStream | Entwürfe unter `packaging/` (Binärpaket-Manifest, nicht eingereicht), `packaging/scripts/make-tarball.sh` |

## Owner-Entscheidungen (30.10.2026)

- **F1 – Flathub mit Binärpaketen, Quelltext bleibt privat.** Die CI baut je Architektur ein self-contained Paket
  (`packaging/scripts/make-tarball.sh` → `rebelsofgaming-<version>-linux-<x86_64|aarch64>.tar.gz` mit
  `rebelsofgaming/` und `share/`) und legt es **öffentlich** unter `https://cdn.rogclan.de/apps/linux/<version>/` ab
  (Bucket `rog-public` der Website, ausgeliefert über cdn.rogclan.de). Das Flathub-Manifest bindet die Pakete als
  `type: archive` mit `sha256` ein (`only-arches` je Architektur) und kopiert sie nur nach `/app`. Geplante Pipeline
  (noch **nicht** umgesetzt): Tag `vX.Y.Z` auf `main` → CI baut x86_64 + aarch64 → Upload nach `rog-public` (Zugang
  als GitHub-Secret, nur Schreiben in `apps/linux/`) → URL + Prüfsumme per PR ins Flathub-Repo der App →
  Flathub-Build. Alte Versionen bleiben liegen (Flathub-Builds müssen reproduzierbar bleiben).
- **F2 – App-ID `com.rebelsofgaming.RebelsOfGaming` (endgültig).** Gleich in `.desktop`, AppStream, Symbolname,
  Flatpak-`id`, D-Bus-Name und -Pfad der Single-Instance (`/com/rebelsofgaming/RebelsOfGaming`), Benachrichtigungen
  (`app_icon`, `desktop-entry`), Schlüsselbund-Attribut `application`, X11-WM-Klasse und Wayland-App-ID.
- **F3 – Avalonia 12 jetzt.** Umgesetzt (siehe „Avalonia 12“); Flatpak mit `--socket=wayland` +
  `--socket=fallback-x11` (+ `--share=ipc` für den X11-Rückfall) statt erzwungenem X11.

## Avalonia 12

Wechsel von 11.3.22 auf **12.1.4** (neueste stabile 12.x, Versionen fest in `Directory.Packages.props`).
Grundlage: offizielle „Breaking changes in Avalonia 12“ und Release-Notes 12.1.

| Bruch / Änderung | Umsetzung |
|---|---|
| `Avalonia.FreeDesktop` 12 bringt **Tmds.DBus.Protocol 0.94.1** (vorher 0.21.3): `Connection` → `DBusConnection`, `Address.Session` → `DBusAddress.Session`, Ausnahmen `DBusErrorReplyException`/`DBusConnectFailedException`/`DBusConnectionClosedException` (Basis `DBusExceptionBase`), `AddMatchAsync` mit `Notification<T>` statt `(ex, value, …)` | Secret Service und Benachrichtigungen angepasst; gleiche Version wie Avalonia gepinnt (eine DLL im Paket) |
| `Avalonia.Headless.XUnit` 12 setzt **xUnit v3** voraus | `xunit` → `xunit.v3` 3.2.2, Testprojekt ist `OutputType=Exe`, Analyzer-Regel xUnit1051 (CancellationToken je Aufruf) im Testprojekt aus |
| `TextBox.Watermark` veraltet | `PlaceholderText` |
| `Bitmap.Save(string, int?)` veraltet | Überladung mit `PngBitmapEncoderOptions` |
| Kompilierte Bindungen jetzt Standard | war schon an (`AvaloniaUseCompiledBindingsByDefault`) – keine Änderung |
| Wayland-Backend (12.1, „experimentell“), nicht über `UsePlatformDetect` | Paket `Avalonia.Wayland`; `UsePlatformDetect().UseWaylandWithFallback()`, wenn `WAYLAND_DISPLAY` gesetzt ist – außer im Spielmodus (Gamescope → X11/XWayland). Erzwingen: `--x11`/`--wayland` bzw. `ROG_DISPLAY=x11|wayland`. App-ID über `WaylandPlatformOptions.AppId` |
| Fenster-/Fokus-/Clipboard-/TopLevel-Brüche (`FocusChangedEventArgs`, `IDataObject`, `TopLevel`-Casts, `SystemDecorations`) | im Code nicht benutzt – nichts zu tun |

**Geprüft:** Build ohne Warnungen, alle Tests grün; Secret Service, Benachrichtigungen und Single-Instance gegen
echtes D-Bus-Protokoll (privater Bus); X11-Lauf unter Xvfb gegen die Produktion (Gastmodus), Rückfall Wayland → X11
(nicht erreichbarer Compositor); Tastaturfokus (Fokusrahmen, Strg+1…9, Escape) unverändert. Datei-Dialoge benutzt die
App noch nicht (kommen mit Stufe 5 über `IStorageProvider` → FileChooser-Portal). **Nicht geprüft:** echter
Wayland-Compositor (auf dem Entwicklungsrechner keiner vorhanden) – bitte einmal unter GNOME/KDE-Wayland starten
(Einstellungen → Über die App zeigt „Anzeige: Wayland“).

## Geteilter Kern

- **Entscheidung:** Git-Submodul `external/rog-windows` (Repo `Rebels-of-Gaming/rebelsofgaming-windows`, Branch
  `develop`, fester Commit) und `ProjectReference` auf `external/rog-windows/src/RoG.Core/RoG.Core.csproj`. Kein Kopieren,
  kein NuGet-Paket (noch): ein Commit-Pin ist nachvollziehbar und ohne Paket-Feed nutzbar.
- **Ergebnis:** `RoG.Core` (net10.0, nur AngleSharp) baut unter Linux ohne Änderung. MSBuild nimmt für den Kern die
  `Directory.Build.props`/`Directory.Packages.props` des Submoduls (Versionen wie im Windows-Repo), für die Linux-Projekte
  die eigenen. Die WinUI-App des Windows-Repos wird nicht gebaut.
- **Mitbenutzt aus dem Submodul:** Übersetzungen (`src/RoG.App/Strings/*/Resources.resw`, eingebettet und von
  `ReswLocalizer` gelesen), Barlow Condensed + OFL, RoG-Abzeichen.
- **Kein Blocker, aber Windows-Annahmen im Kern** (im Linux-Repo umgangen, nichts im Windows-Repo geändert):
  1. `RoGHttpHandler` setzt den User-Agent `RoG-Windows/…`, wenn keiner da ist → die Linux-App setzt
     `RoG-Linux/<Version>` als Standard-Kopf des `HttpClient` (geht vor, Test prüft das).
  2. `AccountService.RegisterDeviceAsync` meldet fest `platform: windows` → `LinuxDeviceRegistration` mit `linux`.
  3. `DeviceName` kennt nur `ForWindows` → `LinuxDeviceName` („Linux-App – PC/Steam Deck von …“).
  4. `PersistentEtagCache`/`Outbox`/Wiki-Offline erwarten einen `IPayloadProtector` (Windows: DPAPI) → unter Linux
     vorerst nur Speicher-Cache; Plattencache folgt mit eigenem Schlüssel aus dem Schlüsselbund (Stufe 4).
- **Wünsche an den Kern** (im Windows-Repo umsetzen, wenn es passt): Produktname im User-Agent aus `IApiContext`,
  Plattform als Parameter von `RegisterDeviceAsync`, `DeviceName.For(prefix, …)`. Dann entfallen 1–3. Neu (Stufe 1):
  Feld `Theme` in `AppSettings` (dann entfällt `linux.json`), `LanguageResolver`-Text „Systemsprache“ neutral.
  Neu (Stufe 2): **`SquadInternalService`** für `GET /v1/squads/{squad}/intern` (Forum, Squad-Gruppe, Termine, Downloads,
  Alben, `missing_scopes`) – liegt bis dahin in `src/RoG.Linux.App/Services/SquadInternalService.cs`; die Windows-App
  zeigt „Intern“ noch nicht. `IClipboardService.SetText` asynchron (Wayland/X11-Zwischenablage ist asynchron).
- **Kern anheben:** `git -C external/rog-windows fetch && git -C external/rog-windows checkout <commit>`, bauen,
  testen, Commit `chore: bump core to <hash>`.

## Stufen

Reihenfolge wie im Windows-Pflichtenheft (Abschnitt 10), Linux-Besonderheiten ergänzt. Jede Stufe lauffähig,
getestet, committet – weiter erst nach Wahkes „weiter“.

0. **Fundament / Spike** *(erledigt)* – Repo, Kern als Submodul, Avalonia-Shell, RoG-Theme, Login, News, Nachrichten
   (lesen), Konto/Abmelden, Secret Service, Benachrichtigungs-Dienst, Tests mit Screenshots, CI, Packaging-Entwürfe.
1. **Konto + Start** *(erledigt)* – Avalonia 12, Startseite (Live-Ticker, Onboarding, News, Termine), Einstellungen
   (Sprache, Design, Benachrichtigungen, Über mit Umgebung), Single-Instance (D-Bus, Rückfall Unix-Socket) mit
   `rog://`-Weitergabe, Fensterzustand merken, schmale Ansicht (< 600 px: Kopfleiste + Menü-Überlagerung), kompakte
   Seitenleiste. *Offen aus der ursprünglichen Liste:* Toast-Baustein (kommt mit der ersten schreibenden Aktion,
   Stufe 4), Avatar + Status-Punkt (Stufe 2 mit Mitgliedern/Presence).
2. **Community lesen** *(erledigt)* – Squads (inkl. Spiele und „Intern“), Mitglieder, Profile mit Status-Punkt,
   Server mit „Verbinden“ (`steam://`, `et://`, `wolf://` über xdg-open, sonst Adresse kopieren), Kalender (Liste +
   Monat, Zusagen nur anzeigen), Regeln/Seiten/Rechtstexte, globale Suche, Deep Links in alle diese Bereiche.
   *Verschoben:* TeamSpeak-/Discord-Voice-Ansicht und Minecraft-Reiter der Server (Stufe 3, zusammen mit der Echtzeit
   über `.live.updated`), Matches/Hall of Fame (Stufe 6 mit Stats), Zusagen/Regeln bestätigen (Stufe 4 bzw. 6).
3. **Echtzeit + Desktop-Integration** – Reverb (Kern) im Betrieb, Glocke + Zähler, Desktop-Benachrichtigungen
   (Notification-Portal in Flatpak), Tray über StatusNotifierItem (KDE/GNOME-Erweiterung, optional), Autostart
   (Background-Portal), **Deep Links `rog://`** für alle Bereiche (Weitergabe per Single-Instance steht seit Stufe 1;
   Fokus unter Wayland mit xdg-activation-Token).
4. **Kommunikation** – DMs schreiben (Antworten, Anhänge, Pop-out-Fenster), Forum lesen + schreiben (BBCode), Zusagen,
   Offline-Warteschlange und verschlüsselter Plattencache (Schlüssel im Schlüsselbund), Tastenkürzel.
5. **Medien** – Uploads (Drag & Drop, Strg+V, Datei-Dialog über das FileChooser-Portal), Galerie, Downloads, Kurzlinks,
   Profil bearbeiten.
6. **Clan-Prozesse** – Bewerbung, Votes, Regeln bestätigen, Mitgliederkarte, Stats, Abwesenheit, Konto/Sicherheit.
7. **Server-Steuerung** – Pterodactyl (Power, Ressourcen, Konsole im eigenen Fenster, Configs, Dateien, Backups, RCON);
   App-Sperre (statt Windows Hello: Polkit/fprintd prüfen, sonst PIN).
8. **Wiki** – lesen, offline, bearbeiten, Versionen.
9. **Verwaltung + Moderation** – Bereiche aus der API wie in der Windows-App.
10. **Feinschliff, Steam Deck, Flathub** – siehe unten.

Später, außerhalb der Stufen: **Clan-„Tools“** in einem eingebetteten, abgeschotteten WebKitGTK-Fenster (einzige
Web-Ansicht der App, nur für die Tools-Seiten der Plattform).

### Stufe 10 – Steam Deck und Controller

- 1280×800 ist Pflicht-Auflösung (Screenshot-Tests); Schriftgrößen ≥ 14 px, Ziele ≥ 44 px
- Controller über SDL2/SDL3 (Gamepad-Eingaben → Fokus-Navigation, A = Aktivieren, B = Zurück, Schultertasten = Bereich
  wechseln); Avalonia hat keine eingebaute Gamepad-Unterstützung → eigener Eingabedienst (P/Invoke auf SDL)
- Bildschirmtastatur: im Spielmodus über Steam (Steam+X); Eingabefelder mit klaren Platzhaltern
- Spielmodus (Gamescope): Vollbild-Start, keine Fensterdekoration nötig; Erkennung über `SteamDeck=1`/`gamescope`
- Erkennung Steam Deck schon im Spike (`LinuxDeviceName.IsSteamDeck`) – für Gerätenamen genutzt

### Stufe 10 – Flathub

- App-ID **`com.rebelsofgaming.RebelsOfGaming`** (Owner-Entscheidung F2). Flathub verlangt den Nachweis, dass die
  Domain uns gehört (`https://rebelsofgaming.com/.well-known/org.flathub.VerifiedApps.txt`).
- Manifest-Entwurf `packaging/flatpak/com.rebelsofgaming.RebelsOfGaming.yml` (freedesktop 25.08, Binärpakete von
  cdn.rogclan.de mit sha256 – F1, Rechte: Netz, Wayland + X11-Rückfall, IPC, DRI, Secret Service, Notifications)
- `.desktop` (inkl. `x-scheme-handler/rog`) und AppStream-Metadaten unter `packaging/linux/` (Entwürfe, validiert:
  `desktop-file-validate` ok, XML wohlgeformt; `appstreamcli validate` vor Einreichung)
- Kein Offline-NuGet-Restore nötig: Flathub bekommt fertige Pakete (F1)
- Screenshots für AppStream müssen öffentlich erreichbar sein (z. B. `cdn.rogclan.de/apps/linux/…`)
- Einreichung nur auf Auftrag (PR an `flathub/flathub`)

## CI

`.github/workflows/ci.yml`: Build + Tests auf den eigenen Linux-Runnern (`[self-hosted, Linux, rog]`, wie das
Windows-Repo), .NET über `actions/setup-dotnet` ins Benutzerverzeichnis.

**Stand 30.10.2026:** Secret `ROG_WINDOWS_DEPLOY_KEY` und Runner-Freigabe stehen – CI läuft grün (Build + 94 Tests in
rund 7 Minuten inkl. Wartezeit). Hinweis im Lauf: `libfontconfig1` fehlt auf dem Runner (Screenshots rendern
trotzdem, der Workflow warnt nur – Owner-Aufgabe). Actions auf Node 24 angehoben: `actions/checkout@v7`,
`actions/setup-dotnet@v6` (Runner 2.338 unterstützt Node 24).

**Einrichtung (zum Nachlesen):**

1. **Deploy-Key für das private Submodul** *(erledigt)*
   ```bash
   ssh-keygen -t ed25519 -N '' -C 'rebelsofgaming-linux CI' -f rog-windows-deploy
   ```
   - Öffentlichen Schlüssel (`rog-windows-deploy.pub`) in **rebelsofgaming-windows → Settings → Deploy keys** eintragen,
     **nur lesen** (kein Schreibzugriff).
   - Privaten Schlüssel (`rog-windows-deploy`) in **rebelsofgaming-linux → Settings → Secrets and variables → Actions**
     als Secret **`ROG_WINDOWS_DEPLOY_KEY`** speichern. Danach beide Dateien lokal löschen.
2. **Runner freigeben** *(erledigt)*: Die Runner-Gruppe der Organisation muss das neue Repo `rebelsofgaming-linux` zulassen (falls
   die Runner nicht für alle Repos freigegeben sind). Konnte ich nicht prüfen (kein `admin:org`-Recht).
3. Runner-Pakete: `libfontconfig1` (+ eine Schrift, z. B. `fonts-dejavu-core`) für die Headless-Screenshots – der
   Workflow warnt, wenn sie fehlen.

## Datenschutz (Stand Stufe 2)

- **Lokal:** `settings.json` (Sprache, Umgebung, Gastmodus, zuletzt geöffneter Bereich, Benachrichtigungen an/aus,
  Fenstergröße/-lage – keine personenbezogenen Daten) und `linux.json` (Design) unter
  `$XDG_CONFIG_HOME/rebelsofgaming/` (Flatpak: `~/.var/app/com.rebelsofgaming.RebelsOfGaming/config/…`); Geräte-Token im Schlüsselbund des Systems (Attribute: App-ID, Art,
  Umgebung; Bezeichnung „Rebels of Gaming (&lt;Anzeigename&gt;)“). Ohne Schlüsselbund nur im Speicher.
  Antworten und Bilder nur im Speicher. Abmelden widerruft den Token (`POST /v1/auth/logout`) und löscht ihn lokal.
- **Übermittlung:** nur an die eigenen Server (API, CDN der Plattform). Gerätemeldung `POST /v1/devices` mit
  `platform: linux` und App-Version (nur mit Scope `devices:manage`), Gerätename beim Login (enthält den
  Linux-Benutzernamen bzw. „Steam Deck“ – wie unter Windows).
- **Desktop-Benachrichtigungen** gehen an den lokalen Benachrichtigungsdienst (nicht an Dritte).
- **Single-Instance:** D-Bus-Name `com.rebelsofgaming.RebelsOfGaming` auf dem lokalen Session-Bus bzw. Socket
  `$XDG_RUNTIME_DIR/rebelsofgaming/instance.sock` (Ordner nur für den Benutzer, wird beim Beenden entfernt).
  Übertragen werden nur `rog://`-Links zwischen zwei Prozessen desselben Benutzers.
- **Neu in Stufe 1, keine Übermittlung an Dritte:** Startseite lädt `/v1/live`, `/v1/events`, `/v1/news` und
  (angemeldet) `/v1/votes`, `/v1/me/onboarding`; Einstellungen lesen die Plattform-Version (`GET /v1`).
- **Neu in Stufe 2, keine Übermittlung an Dritte:** Squads, Mitglieder/Profile, Team, Spiele, Server, Termine
  (+ Teilnehmer nur angemeldet), Regeln/Seiten/Rechtstexte und Suche kommen nur von der eigenen API (`/v1/squads…`,
  `/v1/members…`, `/v1/team`, `/v1/games`, `/v1/servers`, `/v1/events…`, `/v1/rules…`, `/v1/pages`, `/v1/legal`,
  `/v1/search`). Avatare, Banner und Logos nur von der Plattform bzw. ihrem CDN, nur im Speicher. Nichts davon wird
  gespeichert. „Verbinden“ übergibt nur die Server-Adresse an das lokale Spiel/Steam (`xdg-open`) bzw. die
  Zwischenablage; `xdg-mime` wird nur nach dem Schema gefragt. „In Kalender übernehmen“ öffnet die `.ics`-Adresse der
  Plattform im Browser bzw. in der Kalender-App des Systems.
- Website-seitig: keine Änderung nötig (`platform: linux` und Loopback-Login gibt es schon, Guide 35).

## Verzeichnisstruktur

```
external/rog-windows/       Submodul (Kern, Texte, Schrift, Logo)
src/RoG.Linux.Platform/     Secrets/ Desktop/ Localization/ Strings/<de|en|fr>/Linux.resw
src/RoG.Linux.App/          Views/ ViewModels/ Controls/ Services/ Themes/ Localization/
tests/RoG.Linux.Tests/      Platform/ App/ Screenshots/ Support/
packaging/                  flatpak/ linux/ scripts/ (make-tarball.sh)
docs/                       LINUX-BAUPLAN.md
```

## Annahmen

- **L1 – Avalonia 12.1.4** (erledigt, F3). Der Wayland-Backend ist in 12.1 noch „experimentell“ → Rückfall auf X11
  automatisch, Abschalten per `--x11`/`ROG_DISPLAY=x11`; im Spielmodus des Steam Deck immer X11 (Gamescope).
- **L2 – Texte aus der Windows-App:** Die `.resw` des Submoduls sind die Quelle (über 2 300 Schlüssel, DE/EN/FR);
  Linux-eigene Texte und Abweichungen (Schlüsselbund statt Anmeldeinformationsverwaltung, „für Linux“) in
  `Linux.resw`. So bleiben Übersetzungen an einer Stelle gepflegt.
- **L3 – App-ID** `com.rebelsofgaming.RebelsOfGaming` (endgültig, F2), Ordnername `rebelsofgaming`, Programmname
  `rebelsofgaming`, WM-Klasse und Wayland-App-ID = App-ID.
- **L4 – Token-Rückfall:** Ohne Secret Service wird der Token nicht auf die Platte geschrieben, sondern gilt nur bis
  zum Beenden (Hinweis auf Login- und Kontoseite). Ein verschlüsselter Datei-Rückfall wäre ohne Schlüsselbund nicht
  wirklich geschützt.
- **L5 – Secret Service direkt über D-Bus** (Tmds.DBus.Protocol, Sitzung `plain` über den lokalen Bus) statt libsecret
  per P/Invoke: keine native Abhängigkeit, funktioniert mit GNOME Keyring, KWallet (Secret-Service-Schnittstelle) und
  KeePassXC. Später zusätzlich das Secret-Portal für Flatpak prüfen.
- **L6 – Kein Plattencache im Spike:** DPAPI gibt es nicht; ein eigener `IPayloadProtector` (AES-GCM, Schlüssel im
  Schlüsselbund) kommt mit der Offline-Stufe.
- **L7 – Nachrichten nur lesen:** Senden, Echtzeit und Anhänge folgen in Stufe 4 (Logik steht im Kern bereit).
- **L8 – Systemschrift für Fließtext:** keine eingebettete Textschrift; Barlow Condensed nur für Anzeige-Texte.
- **L9 – Symbole** als Vektorpfade nach Material Design Icons (Apache 2.0) statt Symbolschrift.
- **L10 – Runner-Label** wie im Windows-Repo `[self-hosted, Linux, rog]`.
- **L11 – Design:** Standard bleibt RoG-Dunkel (wie Website/Windows). Zusätzlich „Wie das System“ (folgt dem
  Farbschema des Desktops über das Settings-Portal, auch live) und „Hell“ mit eigener heller Palette (Kontrast AA,
  Rot/Gold abgedunkelt). Farben per `DynamicResource`, Wechsel ohne Neustart. Sprache wirkt weiter erst nach Neustart
  (wie Windows).
- **L12 – Single-Instance über D-Bus** (`org.freedesktop.Application` mit `Activate`/`Open`, Name = App-ID) statt nur
  Unix-Socket: Standard unter Linux, in Flatpak ohne Zusatzrecht, später nutzbar für `DBusActivatable`. Rückfall
  Unix-Socket ohne Session-Bus. Fenster nach vorn: unter X11 sofort; Wayland erlaubt Fokuswechsel nur mit
  xdg-activation-Token (wird mitgeschickt, Avalonia wertet ihn noch nicht aus → ggf. nur Hinweis in der Taskleiste).
- **L13 – `linux.json`** für Linux-eigene Einstellungen (Design), weil `AppSettings` des Kerns unbekannte Felder
  verwirft und der Kern nicht geändert wird (Wunsch an den Kern: Feld `Theme`).
- **L14 – Seitenleiste kompakt (36 px je Eintrag)** statt 44 px, damit alle 15 + 3 Bereiche bei 1280×800 ohne
  Scrollen passen. Für Touch/Controller (Stufe 10) wird das mit dem Spielmodus noch einmal bewertet.
- **L15 – Startseite:** Abstimmungen öffnen vorerst die Website (Votes nativ in Stufe 6); Termine, News und
  Onboarding-Schritte (Regeln, Termine …) öffnen seit Stufe 2 die Seite in der App.
- **L16 – Fensterlage:** Größe in DIP, Position in Pixeln (nur X11 – Wayland erlaubt Apps keine Position). Gespeichert
  beim Schließen und bei SIGTERM; beim Start an den heutigen Bildschirm angepasst (`WindowPlacement.FitInto` des Kerns).

## Offene Fragen (an Wahke)

- **F5 – Wayland-Test:** Bitte die App einmal unter einer Wayland-Sitzung (GNOME oder KDE, gern auch Steam Deck im
  Desktopmodus) starten und unter Einstellungen → Über die App „Anzeige: Wayland“ prüfen; bei Problemen hilft
  `--x11` sofort.
