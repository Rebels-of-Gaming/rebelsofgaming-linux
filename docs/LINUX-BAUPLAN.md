# Linux-Bauplan – Rebels of Gaming für Linux

Stand, Stufen, Annahmen und offene Fragen der Linux-App. Funktionsumfang wie die Windows-App
(`external/rog-windows/docs/WINDOWS-PFLICHTENHEFT.md`); hier stehen nur Linux-Besonderheiten und der Fortschritt.

## Stand

**Stufe 5 (Medien, 30.10.2026) – erledigt** (Uploads mit Drag & Drop, Dateiauswahl über das Portal und Strg+V,
Kurzlinks, Galerie mit Großansicht und Einreichen, Downloads mit Prüfsumme, Profil bearbeiten).
**Stufe 4 (Kommunikation) – erledigt** (Direktnachrichten schreiben mit Zitat, live, Anhängen und
eigenem Fenster, Forum lesen + schreiben, Zusagen, Offline-Warteschlange, verschlüsselter Plattencache, Tastenkürzel).
**Stufe 3 (Echtzeit + Desktop-Integration) – erledigt** (Reverb, Glocke + Zähler,
Desktop-Benachrichtigungen mit Klick, alle `rog://`-Ziele, Server-Reiter Minecraft/TeamSpeak/Discord-Voice, Tray,
Schließen in den Infobereich, Autostart, Portale in Flatpak). **Stufe 2 (Community lesen) – erledigt**, davor Stufe 1
(Konto + Start) und der Wechsel auf **Avalonia 12** (Owner-Entscheidung F3). Version 0.1.0, nicht veröffentlicht.

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
| **Echtzeit** (Reverb über den Kern: `live`, `servers`, eigener Kanal `private-App.Models.User.{id}`; Zähler per API beim Start und nach Neuverbindung) | steht; gegen die Produktion geprüft („Echtzeit: verbunden“ unter Einstellungen → Über die App) |
| **Glocke** (Postfach, „Nur ungelesene“, „Alle gelesen“, Eintrag öffnet das Ziel in der App bzw. die genaue Website-Seite; neue Einträge kommen live dazu; Zähler an Glocke und Nachrichten in der Seitenleiste) | steht |
| **Desktop-Benachrichtigungen** (neue Benachrichtigungen und neue Direktnachrichten, solange die App läuft und das Fenster nicht im Vordergrund ist; Aktion „Öffnen“ → Fenster nach vorn + Ziel; Kategorie `im.received` für DMs; Einstellung „Desktop-Benachrichtigungen zeigen“) | steht; Protokoll-Test mit `ActionInvoked` gegen privaten D-Bus |
| **`rog://` vollständig** (alle Ziele der Windows-App: Bereiche mit eigener Seite öffnen nativ, Votes/Verwaltung/Bans/Bestenlisten/Karte und Einträge in Forum/Wiki/Tools/Matches öffnen genau diese Website-Seite; Links während des Logins warten bis nach der Anmeldung; zweite Instanz gibt Links weiter) | steht |
| **Server-Reiter** wie Windows: Gameserver · **Minecraft** (Java/Bedrock/Konsole mit Kopieren, Ressourcenpaket herunterladen, Weltkarte/Bestenlisten/Konto verknüpfen → Website) · **TeamSpeak** (Kanäle mit Personen, Trenner als Überschriften, stumm/abwesend als Symbol + Screenreader-Text, „Leere Kanäle zeigen“, Verbinden über `ts3server://` mit RoG-Name, ohne TeamSpeak Adresse kopieren) · **Discord-Voice** (nur angemeldet, Gast: Hinweis + Anmelden) | steht; Voice alle 30 s und bei `.live.updated`, nur solange der Reiter offen ist; `rog://servers/minecraft|teamspeak|discord`; gegen die Produktion geprüft (TeamSpeak leer → Leerzustand) |
| **Tray** (eigenes StatusNotifierItem + dbusmenu über D-Bus, Anmeldung mit eindeutigem Busnamen; Menü Öffnen/Benachrichtigungen/Nachrichten/Server/Beenden, Tooltip mit Ungelesen-Zahl, Linksklick öffnet) | steht; Protokoll-Tests gegen privaten Bus, **echt geprüft mit xfce4-panel 4.20 unter Xvfb** (Symbol im Panel, Menü, Aktivieren, Beenden; `artifacts/screenshots/x11-tray/`) |
| **Schließen in den Infobereich** (nur wenn ein Panel das Symbol zeigt und „Beim Schließen in den Infobereich“ an ist; einmaliger Hinweis; Abmelden/SIGTERM/„Beenden“ schließen wirklich) | steht |
| **Autostart** („Beim Anmelden am Desktop starten“: XDG-Autostart-Datei bzw. Background-Portal in Flatpak, Start mit `--background` ohne Fenster im Infobereich, wartet bis 15 s auf das Panel, ohne Panel mit Fenster) | steht; Portal gegen privaten Bus getestet, Hintergrund-Start unter Xvfb + xfce4-panel geprüft |
| **Notification-Portal in Flatpak** (statt `--talk-name=org.freedesktop.Notifications`; Klick/„Öffnen“ über `ActionInvoked`) | steht; gegen privaten Bus getestet |
| **Direktnachrichten schreiben** (Enter sendet, Umschalt+Enter neue Zeile, Zitat-Antwort über Knopf/Kontextmenü, Text kopieren, Zeichen-Rest ab 90 %, neue/gelöschte Nachrichten live über `private-conversations.{id}`, Anhänge: Bilder mit Vorschau, Dateien über kurzlebigen Link im Browser; ohne Netz in die Warteschlange) | steht |
| **Chat als eigenes Fenster** (je Unterhaltung eins, „immer im Vordergrund“, Strg+W) und „Nachricht senden“ im Profil | steht |
| **Forum** (Bereiche mit Unterbereichen, Themen mit angeheftet/geschlossen/Umfrage und ungelesen, Thema mit nativ gezeichneten Beiträgen, blockierte Personen eingeklappt mit „Trotzdem anzeigen“, Reaktionen (Chips + Auswahl), Umfrage mit Balken und Abstimmen, Antwort mit BBCode-Leiste, Vorschau über die API, Strg+Enter, Zitat, „Neues Thema“, live über `private-forum.threads.{id}`; `rog://forum/{bereich|thema}`) | steht; gegen die Produktion geprüft (Bereiche, leerer Bereich) |
| **Zusagen** im Kalender (Dabei/Vielleicht/Absagen, gewählte Antwort mit Farbe + Haken, Liste lädt nach) | steht |
| **Offline-Warteschlange** (Kern-`Outbox`: DMs nur im Speicher, Forenantworten und Zusagen verschlüsselt in `$XDG_DATA_HOME/rebelsofgaming/outbox-<umgebung>.dat`; senden nach Anmeldung, bei Neuverbindung und jede Minute; endgültige Ablehnung als Desktop-Benachrichtigung) | steht |
| **Verschlüsselter Plattencache** (Kern-`PersistentEtagCache` mit `KeyringPayloadProtector`: AES-256-GCM, Schlüssel nur im Schlüsselbund; nie Unterhaltungen/Konto/Geräte; Dateien > 30 Tage werden beim Start gelöscht, höchstens 400 Einträge; Abmelden löscht alles) | steht; ohne Schlüsselbund nur im Speicher |
| **Tastenkürzel** (Alt+←/→ Verlauf, Strg+N, Strg+R, Strg+W, Strg+Q, F1 → Liste in den Einstellungen) | steht |
| **Uploads** (Bilder/kurze Videos per Drag & Drop, Dateiauswahl – Avalonia nutzt das FileChooser-Portal – oder Strg+V: kopierte Dateien bzw. Bild als PNG; vorab signierte Adresse ohne Token, Fortschritt, Sichtbarkeit, Direktlink kopieren; eigene Uploads mit Vorschaubild (nur öffentliche), Link/BBCode/Markdown kopieren) | steht |
| **Kurzlinks** (Reiter auf der Upload-Seite: anlegen mit Wunsch-Kürzel und Titel, Klicks, gesperrt/inaktiv, kopieren) | steht |
| **Galerie** (Alben mit Titelbild, Bilder/Clips, Großansicht mit Pfeiltasten und Escape, Clips öffnen im Browser, Einreichen mit Bildunterschrift + Rechte-Bestätigung) | steht; gegen die Produktion geprüft (noch keine Alben) |
| **Downloads** (Version, Spiel, Größe, Zähler, Änderungen; Laden mit Fortschritt in den Downloads-Ordner nach xdg-user-dirs, SHA-256-Prüfung, „Im Ordner zeigen“ über `org.freedesktop.FileManager1`, sonst `xdg-open`) | steht |
| **Profil bearbeiten** (Konto: Motto, Über mich, Stadt/Land, Einwilligungen Karte/Online-Status/Hall of Fame, Avatar/Banner mit Größengrenze der Plattform) | steht |
| Deep Links Stufe 2 | `rog://squads/…`, `rog://u/…`, `rog://events/…`, `rog://servers/…`, `rog://info/…`, `rog://messages/…`, `rog://search?q=…`, `rog://open?url=…` öffnen den Eintrag |
| Tastatur + Fokus | Tab/Pfeile, Strg+1…9, Strg+, (Einstellungen), Strg+K (Suche), Strg+F (Mitgliedersuche), F5, Escape (Menü, Detail jeder Liste-Detail-Seite); sichtbarer Fokusrahmen |
| Tests | 237 xUnit-Tests inkl. 63 Headless-Screenshots (Stufe 5); 218 xUnit-Tests inkl. 52 Headless-Screenshots (Stufe 4); 196 xUnit-Tests inkl. 47 Headless-Screenshots und 16 D-Bus-Tests (Schlüsselbund, Benachrichtigungen + Portal, Single-Instance, Tray, Background-Portal); bis Stufe 2: 170 xUnit-Tests inkl. 41 Headless-Screenshots (`artifacts/screenshots/`) und 8 D-Bus-Tests gegen einen privaten Bus; X11-Lauf unter Xvfb gegen die Produktion (Gastmodus) mit Bildschirmfotos (`artifacts/screenshots/x11-production/`) |
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
  Neu (Stufe 3): `client=rog-<plattform>` statt fest `rog-windows` in `RealtimeConfig.BuildSocketUri` (der Origin
  ist seit Website v0.53.1 nicht mehr nötig, F6 erledigt). **Hinweis an die Windows-App (kein Kern-Thema):** In
  `VoiceViewModel.Add` beendet `channel.Spacer` die Rekursion – Unterkanäle unter Trennern (so liefert die Produktion
  alle Squad-Kanäle) erscheinen dort nie; Linux zeigt Trenner als Überschrift (L29).
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
3. **Echtzeit + Desktop-Integration** *(erledigt)* – Reverb (Kern) im Betrieb, Glocke + Zähler,
   Desktop-Benachrichtigungen mit Klick, **Deep Links `rog://`** für alle Bereiche (Weitergabe per Single-Instance seit
   Stufe 1), Server-Reiter Minecraft/TeamSpeak/Discord-Voice (Live über `.live.updated`), Tray über
   StatusNotifierItem (eigene Umsetzung), Schließen in den Infobereich, Autostart (XDG bzw. Background-Portal),
   Notification-Portal in Flatpak. *Bleibt offen (Avalonia):* Fokus unter Wayland mit xdg-activation-Token – Avalonia
   12.1 wertet ihn nicht aus, das Fenster meldet sich dann nur in der Taskleiste. *Verschoben:* Minecraft-Beitrittsdaten
   bearbeiten und Ressourcenpaket hochladen (`minecraft.manage`) → Stufe 9 (Verwaltung).
4. **Kommunikation** *(erledigt)* – DMs schreiben (Zitat, live, Anhänge ansehen, Pop-out-Fenster), Forum lesen +
   schreiben (BBCode, Vorschau, Reaktionen, Umfragen, neues Thema), Zusagen, Offline-Warteschlange und verschlüsselter
   Plattencache (Schlüssel im Schlüsselbund), Tastenkürzel. *Wie Windows nicht enthalten:* Anhänge **senden** in DMs
   (die Windows-App kann es auch nicht), Beiträge bearbeiten/melden (Melden kommt mit der Moderation, Stufe 9), Bilder
   im Forum hochladen (Uploader, Stufe 5).
5. **Medien** *(erledigt)* – Uploads (Drag & Drop, Strg+V, Datei-Dialog über das FileChooser-Portal), Galerie (mit
   Einreichen), Downloads (Prüfsumme, Downloads-Ordner), Kurzlinks, Profil bearbeiten. *Nicht enthalten (auch in der
   Windows-App nur teilweise):* Likes/Kommentare an Galerie-Alben (Social-Karte, Stufe 6), Melden von Bildern
   (Moderation, Stufe 9), globales Screenshot-Kürzel (Pflichtenheft „optional“, unter Wayland nur über das
   Screenshot-Portal – F8).
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
  cdn.rogclan.de mit sha256 – F1, Rechte: Netz, Wayland + X11-Rückfall, IPC, DRI, Secret Service,
  StatusNotifierWatcher, `xdg-download`; Benachrichtigungen, Autostart, Dateiauswahl, Farbschema und Links über Portale
  ohne Zusatzrecht)
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

**Stand Stufe 5 (30.10.2026):** alle Läufe grün. Einmal (Lauf 41052719016, erster Versuch) brach der Testprozess auf
dem Runner nach 233 von 237 Tests mit Exit-Code 139 (nativer Absturz, vermutlich Skia beim Headless-Rendern ohne
`libfontconfig1`) ab; die Wiederholung war grün, lokal (Debug und Release, mehrfach) nicht nachstellbar. Sollte es
wieder auftreten: `libfontconfig1` + `fonts-dejavu-core` auf dem Runner installieren (Owner-Aufgabe, siehe Punkt 3).

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

## Datenschutz (Stand Stufe 5)

- **Neu in Stufe 5:** Uploads gehen direkt an den Speicher der Plattform (vorab signierte Adresse aus
  `POST /v1/uploads/presign`, ohne Token), danach `POST /v1/uploads/{id}/complete`; Galerie-Einreichungen ebenso plus
  `POST /v1/gallery/albums/{album}/items` (Rechte-Bestätigung, Bildunterschrift). Downloads kommen über einen
  kurzlebigen signierten Link (`POST /v1/download-versions/{id}/link`) und landen im Downloads-Ordner (bleiben dort,
  gehören dem Benutzer). Profiländerungen `PATCH /v1/me/profile`, Avatar/Banner `POST /v1/me/avatar|banner` (der Server
  entfernt EXIF/GPS). Neue Einstellungen in `settings.json` (Kern-Felder): Sichtbarkeit neuer Uploads, „Link danach
  kopieren“. Keine weiteren lokalen Daten; Vorschaubilder nur im Speicher und nur von öffentlichen Uploads.
- **Neu in Stufe 4:** **Plattencache** `$XDG_CACHE_HOME/rebelsofgaming/http/<umgebung>/*.dat` (verschlüsselte
  API-Antworten wie News, Forum, Profile, Termine; nie Unterhaltungen, Nachrichten, Konto, Geräte, Exporte),
  **Warteschlange** `$XDG_DATA_HOME/rebelsofgaming/outbox-<umgebung>.dat` (verschlüsselt: Forenantworten, Zusagen –
  DMs nur im Speicher), **Schlüsselbund-Eintrag** „Cache-Schlüssel“. Speicherdauer: Cache 30 Tage/400 Einträge,
  Warteschlange bis zum Senden; Abmelden löscht Cache und Warteschlange. Neue Übermittlungen nur an die eigene API:
  Nachrichten senden, Unterhaltung starten (`POST /v1/conversations`), Anhänge abrufen (kurzlebige Links, Vorschau nur
  im Speicher), Forum lesen/schreiben/reagieren/abstimmen/Vorschau, Zusagen (`PUT /v1/events/{id}/rsvp`). Echtzeit
  zusätzlich auf `private-conversations.{id}` und `private-forum.threads.{id}`.
- **Neu in Stufe 3 (Abschluss):** Server-Reiter laden `/v1/minecraft/join`, `/v1/minecraft/resource-pack`,
  `/v1/teamspeak/viewer` (öffentlich) und angemeldet `/v1/discord/voice` (Namen + Avatare der Personen im Voice, nur im
  Speicher, Avatare nur vom CDN der Plattform). „TeamSpeak verbinden“ übergibt Adresse und den eigenen RoG-Namen als
  Spitznamen an das lokale TeamSpeak (`ts3server://` über `xdg-open`). „Herunterladen“ des Ressourcenpakets öffnet die
  Adresse der Plattform im Browser. **Tray:** Titel, Tooltip (Zahl ungelesener Einträge) und Menü gehen nur an das
  lokale Panel (Session-Bus). **Autostart:** neue lokale Datei `~/.config/autostart/com.rebelsofgaming.RebelsOfGaming.desktop`
  (nur Programmpfad + `--background`, keine personenbezogenen Daten) bzw. in Flatpak Eintrag über das Background-Portal;
  `linux.json` merkt sich zusätzlich „Autostart an/aus“. Abschalten entfernt den Eintrag. **Notification-Portal:**
  Titel/Text der Benachrichtigung gehen an den lokalen Portal-Dienst (wie vorher an den Benachrichtigungsdienst).
  Nichts davon verlässt den Rechner außer den genannten API-Aufrufen an die eigene Plattform.

- **Lokal:** `settings.json` (Sprache, Umgebung, Gastmodus, zuletzt geöffneter Bereich, Benachrichtigungen an/aus,
  Fenstergröße/-lage – keine personenbezogenen Daten) und `linux.json` (Design) unter
  `$XDG_CONFIG_HOME/rebelsofgaming/` (Flatpak: `~/.var/app/com.rebelsofgaming.RebelsOfGaming/config/…`); Geräte-Token im Schlüsselbund des Systems (Attribute: App-ID, Art,
  Umgebung; Bezeichnung „Rebels of Gaming (&lt;Anzeigename&gt;)“). Ohne Schlüsselbund nur im Speicher.
  Bilder nur im Speicher, Antworten seit Stufe 4 verschlüsselt auf der Platte (siehe unten). Abmelden widerruft den Token (`POST /v1/auth/logout`) und löscht ihn lokal.
- **Übermittlung:** nur an die eigenen Server (API, CDN der Plattform). Gerätemeldung `POST /v1/devices` mit
  `platform: linux` und App-Version (nur mit Scope `devices:manage`), Gerätename beim Login (enthält den
  Linux-Benutzernamen bzw. „Steam Deck“ – wie unter Windows).
- **Desktop-Benachrichtigungen** gehen an den lokalen Benachrichtigungsdienst (nicht an Dritte).
- **Single-Instance:** D-Bus-Name `com.rebelsofgaming.RebelsOfGaming` auf dem lokalen Session-Bus bzw. Socket
  `$XDG_RUNTIME_DIR/rebelsofgaming/instance.sock` (Ordner nur für den Benutzer, wird beim Beenden entfernt).
  Übertragen werden nur `rog://`-Links zwischen zwei Prozessen desselben Benutzers.
- **Neu in Stufe 1, keine Übermittlung an Dritte:** Startseite lädt `/v1/live`, `/v1/events`, `/v1/news` und
  (angemeldet) `/v1/votes`, `/v1/me/onboarding`; Einstellungen lesen die Plattform-Version (`GET /v1`).
- **Neu in Stufe 3, keine Übermittlung an Dritte:** WebSocket zu Reverb der Plattform (`ws.`-Domain aus `GET /v1`,
  Origin = Website der Umgebung); private Kanäle werden mit dem Geräte-Token über `POST /v1/broadcasting/auth`
  autorisiert. Zähler über `/v1/notifications/unread-count` und `/v1/messages/unread-count`, Glocke über
  `/v1/notifications` (+ gelesen markieren). Desktop-Benachrichtigungen (Titel/Text der Benachrichtigung bzw. Name und
  Vorschau der Nachricht) gehen nur an den lokalen Benachrichtigungsdienst; die Zuordnung „Benachrichtigung → Ziel“ liegt
  nur im Speicher und verschwindet beim Beenden. Nichts davon wird gespeichert.
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
- **L6 – Plattencache** (seit Stufe 4): eigener `IPayloadProtector` statt DPAPI – siehe L30.
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
- **L22 – Reverb mit Origin:** Die Produktion lehnte bis Website v0.53.0 WebSocket-Verbindungen ohne erlaubten
  `Origin` ab (`4009`). Die Linux-App sendet als Origin die Website ihrer Umgebung (`WebBaseUrl` aus
  `environments.json`, keine feste Domain) – seit v0.53.1 geht es auch ohne, der Origin bleibt (F6 erledigt).
- **L23 – Desktop-Benachrichtigungen nur im Hintergrund:** Hat das Fenster den Fokus, zeigt die App nichts zusätzlich
  (Zähler und Glocke aktualisieren sich trotzdem). Direktnachrichten erzeugen serverseitig keine Glocken-Einträge
  (`messages.received` nie in der Glocke) – die App holt bei steigendem `.messages.unread` die Unterhaltungen und meldet
  neue Nachrichten seit dem Start (je Nachricht einmal, höchstens drei auf einmal).
- **L24 – Ziele ohne eigene Seite:** Benachrichtigungen, Suche und Links öffnen Ziele, die die App noch nicht nativ hat
  (Forum-Thema, Wiki-Seite, Votes …), mit genau der Adresse im Browser statt nur des Platzhalters.
- **L25 – Tray selbst gebaut:** Avalonias `TrayIcon` besitzt unter Linux einen eigenen Busnamen
  (`org.kde.StatusNotifierItem-<pid>-<n>`), den Flatpak ohne `--own-name` nicht erlaubt, und sagt nicht, ob ein Panel
  das Symbol zeigt. Die eigene Umsetzung (`StatusNotifierTray`, ~600 Zeilen, Tmds.DBus.Protocol) meldet sich mit dem
  eindeutigen Busnamen an und liest `IsStatusNotifierHostRegistered` – nur dann geht „Schließen“ in den Infobereich.
  Symbol: `IconName` = App-ID (Icon-Theme, Flatpak exportiert es) plus `IconPixmap` aus dem RoG-Abzeichen (16–64 px).
- **L26 – Schließen in den Infobereich** ist wie unter Windows standardmäßig an (`CloseToTray` des Kerns), wirkt aber
  nur mit sichtbarem Tray. GNOME ohne AppIndicator-Erweiterung: Hinweis in den Einstellungen, Fenster schließt die App.
- **L27 – Autostart:** außerhalb von Flatpak die XDG-Autostart-Datei
  `~/.config/autostart/com.rebelsofgaming.RebelsOfGaming.desktop` (`Exec="<Programm>" --background`); in Flatpak das
  Background-Portal (fragt einmal nach, Eintrag außerhalb der Sandbox, Stand wird in `linux.json` gemerkt, weil er von
  innen nicht lesbar ist). Hintergrund-Start wartet bis zu 15 s auf das Panel (startet beim Anmelden oft später).
- **L28 – Portale in Flatpak:** Benachrichtigungen über das Notification-Portal (Text-IDs `rog-<n>`, Klick und
  „Öffnen“ über `ActionInvoked`), außerhalb von Flatpak weiter `org.freedesktop.Notifications` (mit Kategorie
  `im.received`; das Portal bekommt keine Kategorie, weil ältere Portale unbekannte Werte ablehnen).
- **L29 – TeamSpeak-Trenner** (`spacer`) sind Überschriften, ihre Unterkanäle bleiben sichtbar (die Website liefert
  Kanäle unter Trennern als Kinder, siehe Produktion). Die Windows-App blendet Trenner samt Unterkanälen aus – Hinweis
  an das Windows-Repo (siehe „Wünsche an den Kern“).
- **L30 – Cache-Schlüssel im Schlüsselbund:** eigener Eintrag „Rebels of Gaming (Cache-Schlüssel)“ (Attribute App-ID,
  `kind=cache-key`, Umgebung), 32 Zufallsbytes, AES-256-GCM je Datei mit eigener Nonce. Er wird beim Start gelesen
  (auch im Gastmodus – öffentliche Antworten werden ebenfalls zwischengespeichert). Ist der Schlüsselbund gesperrt,
  fragt das System wie beim Token nach; ohne Schlüsselbund bleibt alles im Speicher. **Speicherdauer:** Antworten
  höchstens 30 Tage nach dem letzten Schreiben und höchstens 400 Einträge, Warteschlange bis zum Senden, beides bis zum
  Abmelden. Die Windows-App kennt keine Altersgrenze – Linux ist hier strenger.
- **L31 – Neues Thema im rechten Bereich** statt Dialog (Avalonia hat keinen ContentDialog; so bleibt die Liste
  sichtbar und der Entwurf geht beim Wechsel nicht verloren, solange die Seite offen ist).
- **L32 – Forum-Links:** `rog://forum/{x}` – ist `x` ein Bereich, zeigt die App den Bereich, sonst gilt `x` als
  Themen-ID (wie `/forum/t/{id}/{slug}` der Website).
- **L33 – Tastenkürzel nach Pflichtenheft** (Strg+N, Alt+←/→, Strg+W, F1 – die Windows-App setzt sie noch nicht um),
  dazu Linux-üblich Strg+Q (beenden). Strg+N öffnet „Nachrichten“ (eine neue Unterhaltung beginnt man über „Nachricht
  senden“ im Profil), im Forum mit Schreibrecht „Neues Thema“.
- **L34 – Emojis:** Reaktionen im Forum kommen als Unicode-Emoji von der API. Avalonia nimmt dafür eine Farb-Emoji-Schrift
  des Systems (Noto Color Emoji ist auf den gängigen Desktops installiert); ohne sie erscheinen Ersatzzeichen (so auf dem
  Testrechner/CI). Eine eigene Emoji-Schrift mitzuliefern (~10 MB) wäre möglich – siehe F7.
- **L35 – Downloads direkt in den Downloads-Ordner** (wie Windows) statt Speichern-Dialog: Ordner aus
  `user-dirs.dirs` (`XDG_DOWNLOAD_DIR`), in Flatpak mit `--filesystem=xdg-download` (gängiges, von Flathub akzeptiertes
  Recht). Namen kommen vom Server und werden bereinigt (`SafeFileName` des Kerns), bei Kollision „Name (2).ext“; stimmt
  die SHA-256-Prüfsumme nicht, wird die Datei gelöscht.
- **L36 – Uploads über die Dateiauswahl des Systems** (Avalonia → FileChooser-Portal bzw. GTK-Dialog): in Flatpak kein
  Dateisystem-Recht nötig. Strg+V nimmt kopierte Dateien oder ein Bild (als PNG, Name `screenshot-<Zeit>.png`).
- **L37 – Neues in den Kern?** Nein: Uploads, Kurzlinks, Galerie, Downloads und Profil nutzen unverändert die Dienste des
  Kerns (`UploadService`, `ShortLinkService`, `MediaService`, `ProfileService`).
- **L16 – Fensterlage:** Größe in DIP, Position in Pixeln (nur X11 – Wayland erlaubt Apps keine Position). Gespeichert
  beim Schließen und bei SIGTERM; beim Start an den heutigen Bildschirm angepasst (`WindowPlacement.FitInto` des Kerns).

## Offene Fragen (an Wahke)

- **F8 – Screenshot-Kürzel?** Das Pflichtenheft nennt ein optionales globales Kürzel „Bildschirmausschnitt hochladen“.
  Unter Linux geht das sauber nur über das Screenshot-Portal (`org.freedesktop.portal.Screenshot`, der Desktop fragt
  nach) und ein globales Kürzel über das GlobalShortcuts-Portal (GNOME 48+/KDE). Soll die App das bekommen (Vorschlag:
  Knopf „Bildschirmfoto hochladen“ auf der Upload-Seite über das Portal, ohne globales Kürzel)?

- **F7 – Emoji-Schrift mitliefern?** Forum-Reaktionen sind Unicode-Emojis. Auf Desktops mit Noto Color Emoji (Standard bei
  GNOME/KDE/Steam Deck) sehen sie richtig aus, sonst erscheinen Ersatzzeichen. Soll das Paket eine eigene Emoji-Schrift
  (Noto Color Emoji, SIL OFL, rund 10 MB) mitbringen? Vorschlag: nein, erst wenn Mitglieder es melden.

- ~~F6 – Reverb und Apps~~ **erledigt** (Website v0.53.1, Commit 606f1c40): Reverb nimmt Verbindungen ohne und mit
  Origin an. Die Linux-App sendet weiter die Website ihrer Umgebung als Origin (L22).

- **F5 – Wayland-Test:** Bitte die App einmal unter einer Wayland-Sitzung (GNOME oder KDE, gern auch Steam Deck im
  Desktopmodus) starten und unter Einstellungen → Über die App „Anzeige: Wayland“ prüfen; bei Problemen hilft
  `--x11` sofort.
