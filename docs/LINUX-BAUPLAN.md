# Linux-Bauplan – Rebels of Gaming für Linux

Stand, Stufen, Annahmen und offene Fragen der Linux-App. Funktionsumfang wie die Windows-App
(`external/rog-windows/docs/WINDOWS-PFLICHTENHEFT.md`); hier stehen nur Linux-Besonderheiten und der Fortschritt.

## Stand

**Stufe 0 (Spike, 30.10.2026) – erledigt.** Version 0.1.0, nicht veröffentlicht.

| Bereich | Stand |
|---|---|
| Repo, Lösung, CPM, `global.json`, Doku | steht |
| Kern `RoG.Core` als Submodul | baut **unverändert** unter Linux (siehe „Geteilter Kern“) |
| Shell (Seitenleiste wie Windows, aufgeklappt/Symbole) | steht; alle Bereiche der Windows-App als Einträge, nicht umgesetzte mit Platzhalter + „Im Browser öffnen“ |
| Login (PKCE + Loopback, System-Browser), Gastmodus, Abmelden | steht; echter Login gegen Staging/Produktion vom Owner noch zu testen |
| News (Liste, Mehr laden, Artikel nativ, Kommentare lesen) | steht; gegen die Live-API geprüft (Liste, Artikel mit 26 Blöcken, Kommentare) |
| Nachrichten (Unterhaltungen, Suche, Verlauf lesen, gelesen markieren) | steht (nur lesen) |
| Konto (Name, Rang, Token-Speicher, Umgebung, Version, Abmelden) | steht |
| Token im Schlüsselbund (Secret Service), Rückfall „nur diese Sitzung“ | steht; D-Bus-Protokoll gegen einen Test-Dienst geprüft (Anlegen, Ersetzen, Lesen, Entsperren per Prompt, Löschen) |
| Desktop-Benachrichtigungen (org.freedesktop.Notifications) | Dienst steht und ist geprüft, wird ab Stufe 3 benutzt |
| `xdg-open` (Browser, Spiele-Links), XDG-Ordner, Gerätename (PC/Steam Deck) | steht |
| Tastatur + Fokus | Tab/Pfeile, Strg+1…9, F5, Escape; sichtbarer Fokusrahmen |
| Tests | 55 xUnit-Tests inkl. 11 Headless-Screenshots (`artifacts/screenshots/`) |
| CI | `.github/workflows/ci.yml` auf `[self-hosted, Linux, rog]`; braucht Secret `ROG_WINDOWS_DEPLOY_KEY` (siehe „CI“) |
| Flatpak/AppStream | Entwürfe unter `packaging/` (nicht eingereicht) |

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
  Plattform als Parameter von `RegisterDeviceAsync`, `DeviceName.For(prefix, …)`. Dann entfallen 1–3.
- **Kern anheben:** `git -C external/rog-windows fetch && git -C external/rog-windows checkout <commit>`, bauen,
  testen, Commit `chore: bump core to <hash>`.

## Stufen

Reihenfolge wie im Windows-Pflichtenheft (Abschnitt 10), Linux-Besonderheiten ergänzt. Jede Stufe lauffähig,
getestet, committet – weiter erst nach Wahkes „weiter“.

0. **Fundament / Spike** *(erledigt)* – Repo, Kern als Submodul, Avalonia-Shell, RoG-Theme, Login, News, Nachrichten
   (lesen), Konto/Abmelden, Secret Service, Benachrichtigungs-Dienst, Tests mit Screenshots, CI, Packaging-Entwürfe.
1. **Konto + Start** – Startseite mit echten Daten (Live-Ticker, Squads, News), Einstellungen-Seite (Sprache,
   Umgebung, Benachrichtigungen), Versionsanzeige/Über, Single-Instance (D-Bus-Name bzw. Unix-Socket), Fensterzustand
   merken, schmale Ansicht (< 600 px: Menü-Knopf statt Symbolleiste), Toast/InfoBar-Baustein, Avatar + Status-Punkt.
2. **Community lesen** – Squads, Spiele, Mitglieder, Profile, Server mit „Verbinden“ (`steam://`, `et://` über
   xdg-open, sonst Adresse kopieren), TeamSpeak/Discord-Voice, Matches/Hall of Fame, Kalender, Regeln, Seiten, Suche.
3. **Echtzeit + Desktop-Integration** – Reverb (Kern) im Betrieb, Glocke + Zähler, Desktop-Benachrichtigungen
   (Notification-Portal in Flatpak), Tray über StatusNotifierItem (KDE/GNOME-Erweiterung, optional), Autostart
   (Background-Portal), **Deep Links `rog://`** (.desktop `x-scheme-handler/rog` + Single-Instance-Weitergabe).
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

- App-ID (Vorschlag, Annahme L3): **`com.rebelsofgaming.RebelsOfGaming`**. Flathub verlangt den Nachweis, dass die
  Domain uns gehört (`https://rebelsofgaming.com/.well-known/org.flathub.VerifiedApps.txt`). Einmal veröffentlicht nicht
  mehr änderbar → **Owner bestätigt vor der Einreichung**.
- Manifest-Entwurf `packaging/flatpak/com.rebelsofgaming.RebelsOfGaming.yml` (freedesktop 25.08 + .NET-10-Erweiterung,
  self-contained Publish, Rechte: Netz, X11, DRI, Secret Service, Notifications)
- `.desktop` (inkl. `x-scheme-handler/rog`) und AppStream-Metadaten unter `packaging/linux/` (Entwürfe, validiert:
  `desktop-file-validate` ok, XML wohlgeformt; `appstreamcli validate` vor Einreichung)
- Offline-Build: NuGet-Quellen mit `flatpak-dotnet-generator.py` erzeugen
- Screenshots für AppStream müssen öffentlich erreichbar sein (z. B. `cdn.rogclan.de/apps/linux/…`)
- Einreichung nur auf Auftrag (PR an `flathub/flathub`)

## CI

`.github/workflows/ci.yml`: Build + Tests auf den eigenen Linux-Runnern (`[self-hosted, Linux, rog]`, wie das
Windows-Repo), .NET über `actions/setup-dotnet` ins Benutzerverzeichnis.

**Der Owner muss einrichten (sonst bricht der Lauf mit klarer Meldung ab):**

1. **Deploy-Key für das private Submodul**
   ```bash
   ssh-keygen -t ed25519 -N '' -C 'rebelsofgaming-linux CI' -f rog-windows-deploy
   ```
   - Öffentlichen Schlüssel (`rog-windows-deploy.pub`) in **rebelsofgaming-windows → Settings → Deploy keys** eintragen,
     **nur lesen** (kein Schreibzugriff).
   - Privaten Schlüssel (`rog-windows-deploy`) in **rebelsofgaming-linux → Settings → Secrets and variables → Actions**
     als Secret **`ROG_WINDOWS_DEPLOY_KEY`** speichern. Danach beide Dateien lokal löschen.
2. **Runner freigeben:** Die Runner-Gruppe der Organisation muss das neue Repo `rebelsofgaming-linux` zulassen (falls
   die Runner nicht für alle Repos freigegeben sind). Konnte ich nicht prüfen (kein `admin:org`-Recht).
3. Runner-Pakete: `libfontconfig1` (+ eine Schrift, z. B. `fonts-dejavu-core`) für die Headless-Screenshots – der
   Workflow warnt, wenn sie fehlen.

## Datenschutz (Stand Stufe 0)

- **Lokal:** `settings.json` (Sprache, Umgebung, Gastmodus, zuletzt geöffneter Bereich – keine personenbezogenen Daten)
  unter `$XDG_CONFIG_HOME/rebelsofgaming/`; Geräte-Token im Schlüsselbund des Systems (Attribute: App-ID, Art,
  Umgebung; Bezeichnung „Rebels of Gaming (&lt;Anzeigename&gt;)“). Ohne Schlüsselbund nur im Speicher.
  Antworten und Bilder nur im Speicher. Abmelden widerruft den Token (`POST /v1/auth/logout`) und löscht ihn lokal.
- **Übermittlung:** nur an die eigenen Server (API, CDN der Plattform). Gerätemeldung `POST /v1/devices` mit
  `platform: linux` und App-Version (nur mit Scope `devices:manage`), Gerätename beim Login (enthält den
  Linux-Benutzernamen bzw. „Steam Deck“ – wie unter Windows).
- **Desktop-Benachrichtigungen** gehen an den lokalen Benachrichtigungsdienst (nicht an Dritte).
- Website-seitig: keine Änderung nötig (`platform: linux` und Loopback-Login gibt es schon, Guide 35).

## Verzeichnisstruktur

```
external/rog-windows/       Submodul (Kern, Texte, Schrift, Logo)
src/RoG.Linux.Platform/     Secrets/ Desktop/ Localization/ Strings/<de|en|fr>/Linux.resw
src/RoG.Linux.App/          Views/ ViewModels/ Controls/ Services/ Themes/ Localization/
tests/RoG.Linux.Tests/      Platform/ App/ Screenshots/ Support/
packaging/                  flatpak/ linux/
docs/                       LINUX-BAUPLAN.md
```

## Annahmen

- **L1 – Avalonia 11.3 statt 12:** Vorgabe war „neueste stabile 11.x“ → 11.3.22. Avalonia 12.1.4 ist inzwischen stabil;
  ein Wechsel lohnt sich vor allem wegen nativer Wayland-Unterstützung (11 läuft unter Wayland nur über XWayland,
  daher `--socket=x11` im Flatpak). Vorschlag: Wechsel als eigener Schritt in Stufe 1 nach Owner-OK.
- **L2 – Texte aus der Windows-App:** Die `.resw` des Submoduls sind die Quelle (über 2 300 Schlüssel, DE/EN/FR);
  Linux-eigene Texte und Abweichungen (Schlüsselbund statt Anmeldeinformationsverwaltung, „für Linux“) in
  `Linux.resw`. So bleiben Übersetzungen an einer Stelle gepflegt.
- **L3 – App-ID** `com.rebelsofgaming.RebelsOfGaming` (siehe Flathub), Ordnername `rebelsofgaming`, Programmname
  `rebelsofgaming`, WM-Klasse = App-ID.
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

## Offene Fragen (an Wahke)

- **F1 – Quellen für Flathub:** Flathub baut nur aus öffentlich abrufbaren Quellen. Beide Repos sind privat. Optionen:
  (a) Linux-Repo und Kern öffentlich machen (Quelltext sichtbar, Lizenz wählen), (b) fertige Binärpakete aus der CI
  öffentlich ablegen (z. B. `cdn.rogclan.de`) und im Manifest als Archiv mit Prüfsumme einbinden (für proprietäre
  Apps erlaubt), (c) nur eigenes Flatpak-Repo statt Flathub. Empfehlung: (b).
- **F2 – App-ID** `com.rebelsofgaming.RebelsOfGaming` bestätigen (nachträglich nicht änderbar).
- **F3 – Avalonia 12** jetzt (Wayland nativ) oder erst nach weiteren Stufen?
- **F4 – Runner-Zugriff** und Deploy-Key einrichten (siehe „CI“).
