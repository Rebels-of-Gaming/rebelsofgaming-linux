# CLAUDE.md – Rebels of Gaming für Linux (Avalonia, Flathub)

Dauerhafte Arbeitsregeln für dieses Repository. Gilt für jede Sitzung.

## Projekt

Native Linux-App für **Rebels of Gaming (RoG)**, Multigaming-Clan seit 2020. Owner: Wahke.
Die App ist ein vollwertiger Client der Laravel-Plattform – gleicher Funktionsumfang wie die Windows-App – plus das, was
Linux-Desktop und Steam Deck besser können (Schlüsselbund, Desktop-Benachrichtigungen, Controller, Spielmodus).

- Owner-Entscheidung (30.10.2026): eigenes privates Repo, „es soll super sein“, **keine WebView für die Oberfläche**,
  Verteilung später über **Flathub** (erreicht auch SteamOS/Steam Deck), Steam vielleicht später.
- Fortschritt, Annahmen, offene Fragen: `docs/LINUX-BAUPLAN.md`
- Funktionsumfang/Bau-Reihenfolge: wie `external/rog-windows/docs/WINDOWS-PFLICHTENHEFT.md`, Linux-Abweichungen im Bauplan

## Andere Repositories (nur lesen!)

| Repo | Pfad | Wofür |
|---|---|---|
| Windows-App | `/home/wahke/projects/rog-windows/` bzw. Submodul `external/rog-windows` | **geteilter Kern `src/RoG.Core`**, Übersetzungen, Schrift, Logo, Vorbild für Seiten und Texte |
| Website | `/home/wahke/projects/rebelsofgaming/` | einzige Wahrheit über die API: `docs/api/` (v. a. `35-apps.md`), `docs/DESIGN.md` |
| Mobile-App | `/home/wahke/projects/rog-app/` | Vorbild für Funktionsumfang und Texte |

**Regel: In den anderen Repos wird nichts geändert, nichts committet, kein `git checkout`/`stash`/`reset`.** Auch im
Submodul `external/rog-windows` nichts ändern – nur den gepinnten Commit bewusst anheben (`git -C external/rog-windows
fetch && git -C external/rog-windows checkout <commit>`, danach bauen, testen, eigener Commit „chore: bump core“).
Braucht der Kern eine Änderung für Linux, kommt sie als Notiz in `docs/LINUX-BAUPLAN.md` („Wünsche an den Kern“) und
wird im Windows-Repo umgesetzt – bis dahin im Linux-Repo umgehen (eigene Implementierung der Schnittstelle).

## Stack

- **.NET 10**, C# latest, Nullable an, **Warnungen = Fehler**
- **Avalonia UI 12.1** (Fluent, RoG-Dunkel Standard, Hell/System optional) + `Avalonia.Wayland` (nativ, Rückfall X11)
  + **CommunityToolkit.Mvvm**; kompilierte Bindungen (`x:DataType`)
- Kern **RoG.Core** aus dem Submodul (ProjectReference, unverändert): API-Client aus der OpenAPI-Spec, RoGHttpHandler,
  AppLoginFlow (PKCE + Loopback), Reverb-Client, HTML/BBCode-Parser, Einstellungen, `StringKeys`
- D-Bus über **Tmds.DBus.Protocol** (gleiche Version wie Avalonia.FreeDesktop): Secret Service, Notifications
- Tests: **xUnit v3** + **Avalonia.Headless** (Skia-Rendering für Screenshots); D-Bus-Tests gegen einen privaten
  `dbus-daemon` (`Support/PrivateBus.cs`, Test-Dienste für Secret Service und Notifications)
- Paketversionen zentral in `Directory.Packages.props` (fest, Updates bewusst per Commit)

## Architektur

```
external/rog-windows/      # Submodul (fester Commit): src/RoG.Core + Strings/*.resw + Fonts + Assets
src/RoG.Linux.Platform/    # ohne UI: Secrets/ (Secret Service, Rückfall), Desktop/ (xdg-open, Notifications),
                           # Localization/ (ReswLocalizer + Strings/<sprache>/Linux.resw), LinuxPaths, Gerätename
src/RoG.Linux.App/         # Avalonia: Views/ ViewModels/ Controls/ Services/ (AppServices = Composition Root) Themes/
tests/RoG.Linux.Tests/     # Platform/ App/ Screenshots/ Support/ (FakeApi, FakeImages, TestServices)
packaging/                 # flatpak/ (Manifest-Entwurf), linux/ (.desktop, metainfo)
```

- **Logik gehört in den Kern.** Was plattformneutral ist und noch fehlt, wird im Windows-Repo ergänzt (siehe oben);
  in diesem Repo nur Oberfläche, Linux-Verdrahtung und ViewModels.
- Views binden ViewModels (keine Logik im Code-Behind außer Fenster-/Tastatur-Verdrahtung).
- **Keine WebView** in der Oberfläche. HTML der API wird im Kern zu `RichDocument` geparst und nativ gezeichnet
  (`Controls/RichContentView`). Später laufen nur die Clan-„Tools“ in einem eingebetteten, abgeschotteten WebKitGTK.
- **Rechte nie über Rollennamen** – nur `permissions` aus `/v1/me` plus Token-Scopes.
- Externe Ausfälle (API, D-Bus, Bilder) dürfen keine Seite kaputt machen: Lade-/Leer-/Fehlerzustand, Hinweis, erneut versuchen.

## API-Konventionen (wie Website/Windows)

- Basis aus `environments.json` des Kerns (`production`, `staging`, `local`), Auswahl über Einstellungen bzw.
  `--environment=` / `ROG_ENVIRONMENT` (Entwicklung) – **nie Domains im Code**
- User-Agent `RoG-Linux/<Version>`, Bearer-Token, `Accept-Language` = App-Sprache, Idempotency/ETag/401/429 macht der Kern
- Login: System-Browser (`xdg-open`, in Flatpak über das Portal) + PKCE + Loopback `http://127.0.0.1:<Port>/callback`
- Gerät melden mit `platform: linux` (`LinuxDeviceRegistration`), Gerätename „Linux-App – PC/Steam Deck von …“

## Design

Gleiche Identität wie Website und Windows-App, nativ umgesetzt.

- Tokens in `src/RoG.Linux.App/Themes/RoGTheme.axaml`: `bg #1C1F26 · bg2 #242832 · bg3 #2D3240 · line #3A4050 ·
  ink #F1EFEA · muted #A8ACB6 · red #E0324A · action-red #D62C43 · gold #D4AE3C · green #3DDC84` (+ blue #4F8CFF Voice)
- 90 % dunkel / 8 % Off-White / 2 % Akzent. Rot = Aktion/Status, Gold = Rang/Auszeichnung. Standard RoG-Dunkel; helle Palette für „Hell“/„Wie das System“ – Farben in Views **nur per `DynamicResource`**.
- Display: **Barlow Condensed** 600/700 in Versalien (eingebettet aus dem Submodul, SIL OFL), Text: Systemschrift
- Symbole als Vektorpfade (`Themes/Icons.axaml`), keine Symbolschrift
- Status-Punkte: grün online · blau Voice · rot auf RoG-Server · gold im Spiel · grau offline
- Seiten nutzen nur Ressourcen/Klassen aus dem Theme (`display`, `title`, `section`, `kicker`, `body`, `muted`, `card`,
  `primary`, `secondary`, `icon`, `nav`, `cards`) – keine Einweg-Farben in Views
- Seitenleiste wie die Windows-App: ≥ 840 px aufgeklappt, 600–840 px nur Symbole, < 600 px Kopfleiste + Menü als
  Überlagerung; Liste-Detail ab 760 px nebeneinander,
  Lesebreite max. 720 px. **1280×800 (Steam Deck) muss immer gut aussehen** (Screenshot-Test).
- Jede Liste hat Lade-, Leer- und Fehlerzustand (`Controls/StateView`), jede Aktion ein Feedback
- Barrierefreiheit: `AutomationProperties.Name` an Symbol-Knöpfen und Listen, Tastatur vollständig (Tab, Pfeile,
  Strg+1…9, F5, Escape), sichtbarer Fokus (Off-White-Rahmen), Kontrast AA

## Sicherheit

- Geräte-Token nur im **Schlüsselbund** (Secret Service, ein Eintrag je Umgebung). Ohne Schlüsselbund nur im Speicher
  mit deutlichem Hinweis – **nie Klartext auf der Platte**, nie in Einstellungen, Logs, Caches
- Links aus Inhalten nur `http`/`https`/`mailto` öffnen; Spiele-Links nur bekannte Schemata; Prozesse ohne Shell
- Keine API-Keys in der App; Admin-Funktionen nur, wenn `/v1/me` + Scopes sie erlauben
- **Keine Secrets im Repo** (keine Schlüssel, keine `.env`, keine Zugangsdaten). CI-Secrets nur über GitHub Secrets.

## Datenschutz (wie Website, DSGVO, Betreiber Privatperson in Luxemburg)

- Kein Tracking, keine Analytics, keine fremden Schriften/Skripte; Bilder nur von den Servern/dem CDN der Plattform
- Lokal: `settings.json` (+ `linux.json` für das Design) unter `$XDG_CONFIG_HOME/rebelsofgaming/` (Flatpak: `~/.var/app/<App-ID>/config/…`),
  Token im Schlüsselbund, Bilder/Antworten nur im Speicher. Abmelden löscht Token und Caches.
- Jede neue lokale Speicherung oder Übermittlung an Dritte: im Bauplan unter „Datenschutz“ nennen

## Sprache

- UI-Texte nur über Ressourcen: `{l:T Schlüssel}` in AXAML bzw. `ILocalizer`. Quelle sind die `.resw` der Windows-App
  (DE/EN/FR); Linux-Abweichungen und neue Texte in `src/RoG.Linux.Platform/Strings/<sprache>/Linux.resw`
  (Präfix `Linux_`), **Deutsch zuerst, EN/FR immer mitpflegen** – `LocalizationTests` prüfen das
- Code, Bezeichner, Commits auf **Englisch**; Kommentare dürfen Deutsch sein
- **Berichte, Rückfragen und Zusammenfassungen an den Owner immer auf Deutsch.**

## Arbeitsweise und Git

- `develop` = laufende Entwicklung (Standard-Branch), `main` = Releases (Fast-Forward nur nach Freigabe des Owners).
  Kleine, thematische Commits, Nachricht Englisch (`feat(app): …`, `docs: …`), letzte Zeile die
  `Co-Authored-By:`-Zeile aus der Sitzungsanweisung. Nie Force-Push.
- **Vor jedem Commit:** `dotnet build RoG.Linux.sln` und `dotnet test RoG.Linux.sln` grün; bei UI-Änderungen die
  Screenshots in `artifacts/screenshots/` ansehen (1280×800!)
- Subagenten: kein `git checkout`/`stash`/`reset` – nur die Hauptsitzung committet; explizite Pfade stagen
- Ein Feature ist fertig, wenn: Seite in DE/EN/FR, Lade-/Leer-/Fehlerzustand, Tests (ViewModel + Screenshot),
  `docs/LINUX-BAUPLAN.md` und `CHANGELOG.md` aktualisiert
- **Flatpak/Flathub-Einreichungen, Steam, Store-Builds und Releases nur auf ausdrücklichen Auftrag des Owners.**
- Bei Unklarheit: sinnvollste Annahme, im Bauplan unter „Annahmen“ notieren, weiterbauen. Vorher fragen nur bei schwer
  Umkehrbarem (App-ID, Lizenz/Quellen öffentlich, Paketname, Kosten).

## Befehle

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
git submodule update --init                            # Kern holen (privat: SSH-/HTTPS-Zugang zum Windows-Repo nötig)
dotnet build RoG.Linux.sln && dotnet test RoG.Linux.sln
dotnet test RoG.Linux.sln --filter Category=Screenshots   # nur Screenshots → artifacts/screenshots/
dotnet run --project src/RoG.Linux.App -- --environment=staging
xvfb-run -a dotnet run --project src/RoG.Linux.App     # ohne Bildschirm
dotnet run --project src/RoG.Linux.App -- --x11        # X11 erzwingen (Standard unter Wayland: nativ Wayland)
packaging/scripts/make-tarball.sh x86_64               # Binärpaket für Flathub nach artifacts/dist/
flatpak-builder --user --install --force-clean build-dir packaging/flatpak/com.rebelsofgaming.RebelsOfGaming.yml   # nur lokal (url → path)
```
