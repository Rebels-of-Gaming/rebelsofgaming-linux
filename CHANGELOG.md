# Changelog – Rebels of Gaming für Linux

Versionen nach [SemVer](https://semver.org/lang/de/), Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/).
Jedes veröffentlichte Paket (Flathub, später ggf. Steam) bekommt einen Git-Tag `vX.Y.Z`. Änderungen bis zur nächsten
Version sammelt `## [Unveröffentlicht]`.

## [Unveröffentlicht]

Stufe 2 – Community lesen; davor Stufe 1 – Konto + Start und Wechsel auf Avalonia 12 (Version 0.1.0, nicht veröffentlicht).

### Hinzugefügt (Stufe 2)

- Squads: Liste mit Logo, Spiel, Clan-Tag und Rekrutierung (Chip), Umschalter „Spiele“ (Clan-Spiele mit Links); Squad-Seite mit Banner, Leitung, Reitern **Vorstellung** (Text, Trainingszeiten, Clan, Links mit Kopieren), **Line-up** (Avatare, Rollen in Rollenfarbe, Klick öffnet das Profil), **Server** (mit Verbinden) und **Intern** – nur für Member des Squads bzw. Verwalter: Squad-Gruppe, nächste Termine mit eigener Antwort, Squad-Forum, Downloads und Alben, Hinweis bei fehlenden Berechtigungen des Tokens
- Mitglieder: Suche (verzögert, Strg+F), „Nur online“, Team-Ansicht, „Mehr laden“; Avatar mit **Status-Abzeichen** (grün online, blau Voice, rot auf RoG-Server, gold im Spiel, grau offline – jeweils auch als Form/Symbol, mit Tooltip und Screenreader-Text)
- Profile: Banner, Avatar mit Status, Rang in Rangfarbe, Status + Aktivität, Motto, Über mich, Ort, „Dabei seit“, „Zuletzt gesehen“, Squads, Rebel-Coins, Abzeichen, Spiele-Accounts, „Im Browser öffnen“
- Server: Karten mit Status (Punkt/Ring/Symbol + Text), Spieler und Auslastung, Map, Passwort-Schloss, Spielerliste; **Verbinden** startet Steam (`steam://`), ET: Legacy (`et://`) bzw. RtCW (`wolf://`) über `xdg-open` – ist kein Programm registriert (`xdg-mime`), wird die Adresse kopiert und erklärt, wie man sie im Spiel einträgt; Minecraft: Adresse kopieren mit Anleitung; „Adresse kopieren“; aktualisiert sich jede Minute
- Kalender: Liste der nächsten 60 Tage nach Tag gruppiert, **Monatsansicht** (6 Wochen ab Montag, Punkte in Typfarbe, heute rot umrandet, „Heute“), Termin mit Zeit, Ort, Zahlen, eigener Antwort (nur anzeigen), Teilnehmern, Server mit Verbinden, Beschreibung, „Im Browser öffnen“, „In Kalender übernehmen“ (.ics)
- Info: Regeln, Seiten und Rechtstexte nach Gruppen; Regeln mit §-Abschnitten, wichtige markiert, Hinweis „bitte bestätigen“ (vorerst auf der Website)
- Suche (Strg+K, Lupe in Seitenleiste und Kopfleiste): Treffer nach Bereich, öffnen den Bereich in der App (Squads, Profile, News, Termine …) oder sonst den Browser
- `rog://`-Links für Squads, Profile (`rog://u/…`), Termine, Server, Info, Unterhaltungen, Suche und `rog://open?url=…`
- Startseite: Termine und Onboarding-Schritte öffnen jetzt die passenden Seiten der App statt der Website
- Escape schließt das Detail auf allen Liste-Detail-Seiten; Farben aus der API (Ränge, Rollen, Abzeichen) bleiben im hellen Design lesbar
- Tests: ViewModel-Tests für alle neuen Seiten, Deep Links und `xdg-mime`; Screenshots in 1280×800, 1440×900, schmal (480 px) und hell

### Geändert (Stufe 2)

- CI: `actions/checkout@v7` und `actions/setup-dotnet@v6` (Node 24)

### Hinzugefügt (Stufe 1)

- Startseite wie in der Windows-App: Begrüßung, Gast-Hinweis mit „Anmelden“, offene Abstimmungen, „Dein Start bei den Rebels“, Live-Ticker (`/v1/live`) als Kacheln, neueste News (öffnen den Artikel in der App) und nächste Termine
- Einstellungen: Sprache (DE/EN/FR/System, wirkt nach Neustart), Design (RoG-Dunkel als Standard, „Wie das System“, Hell – sofort), Desktop-Benachrichtigungen mit Test-Knopf, „Über die App“ mit Version, Plattform-Version, Umgebung, Paket (Flatpak/direkt), Anzeige (Wayland/X11), Links, Copyright und Lizenzen
- Natives Wayland (Avalonia 12.1) mit automatischem Rückfall auf X11; `--x11`/`--wayland` bzw. `ROG_DISPLAY`; im Spielmodus des Steam Deck X11
- Nur eine laufende App: ein zweiter Start holt das Fenster nach vorn und gibt `rog://`-Links weiter (D-Bus `org.freedesktop.Application`, ohne Session-Bus Unix-Socket); `rog://news/…`, `rog://settings` usw. öffnen den Bereich
- Fenstergröße, -lage und „maximiert“ werden gemerkt (auch beim Beenden per SIGTERM) und passend zum Bildschirm wiederhergestellt
- Schmale Ansicht unter 600 px: Kopfleiste mit Menü-Knopf, Seitenleiste als Überlagerung (Escape oder Klick daneben schließt); 600–840 px nur Symbole
- Strg+, öffnet die Einstellungen
- Tests: D-Bus-Tests gegen einen privaten Bus (Schlüsselbund inkl. Entsperr-Prompt, Benachrichtigungen, Single-Instance), Screenshots von Start, Einstellungen, schmaler Ansicht, Menü und hellem Design
- `packaging/scripts/make-tarball.sh`: Binärpaket (self-contained) für Flathub

### Geändert

- Avalonia 11.3.22 → **12.1.4**, Tmds.DBus.Protocol 0.21.3 → 0.94.1, xUnit 2 → **xUnit v3**
- Seitenleiste kompakter (36 px je Eintrag, neuer Eintrag „Einstellungen“) – passt bei 1280×800 ohne Scrollen; App startet auf der Startseite
- Flatpak-Entwurf: Binärpakete von cdn.rogclan.de mit Prüfsumme statt Quellbau, `--socket=wayland` + `--socket=fallback-x11` statt erzwungenem X11
- App-ID `com.rebelsofgaming.RebelsOfGaming` endgültig (überall gleich, auch D-Bus und Wayland-App-ID)

### Stufe 0 – Spike

- Lösung `RoG.Linux.sln`: Avalonia-App `RoG.Linux.App`, Linux-Dienste `RoG.Linux.Platform`, Tests `RoG.Linux.Tests`
- Geteilter Kern `RoG.Core` der Windows-App als Git-Submodul (`external/rog-windows`, fester Commit) – baut unverändert unter Linux
- RoG-Design für Avalonia (Dunkel, Farben und Barlow Condensed wie Website/Windows), Symbole als Vektorpfade
- Shell mit Seitenleiste wie die Windows-App (aufgeklappt ab 840 px, sonst Symbole), Platzhalter mit „Im Browser öffnen“ für noch fehlende Bereiche
- Login im System-Browser (Discord oder E-Mail) mit PKCE und Rücksprung an `http://127.0.0.1:<Port>/callback`, Gastmodus, Abmelden mit Widerruf
- Geräte-Token im Schlüsselbund (Secret Service über D-Bus), Rückfall „nur diese Sitzung“ mit Hinweis; Gerätename „Linux-App – PC/Steam Deck von …“, Gerätemeldung `platform: linux`
- News: Liste mit Titelbildern, „Mehr laden“, Artikel nativ gezeichnet (Überschriften, Listen, Zitate, Code, Links, Tabellen, Spoiler), Kommentare lesen
- Nachrichten: Unterhaltungen mit Suche und Ungelesen-Markierung, Verlauf lesen („Ältere laden“, Zitate, gelöschte Nachrichten, „aus Minecraft“)
- Konto: Name, Rang, Avatar, Ort des Tokens, Umgebung, Version
- Desktop-Benachrichtigungen (org.freedesktop.Notifications), Links über `xdg-open` (nur http/https/mailto bzw. bekannte Spiele-Links)
- Tastatur: Tab/Pfeile, Strg+1…9 für Bereiche, F5 aktualisiert, Escape schließt das Detail; sichtbarer Fokusrahmen
- Sprachen DE/EN/FR aus den Texten der Windows-App plus Linux-eigene Texte
- Tests (xUnit, Avalonia.Headless) inkl. Screenshots der Hauptseiten in 1280×800 (Steam Deck) und 1440×900
- CI auf den eigenen Linux-Runnern; Entwürfe für Flatpak-Manifest, `.desktop`-Datei (`rog://`-Links) und AppStream-Metadaten
