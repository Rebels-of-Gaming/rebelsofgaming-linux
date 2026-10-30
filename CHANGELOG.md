# Changelog – Rebels of Gaming für Linux

Versionen nach [SemVer](https://semver.org/lang/de/), Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/).
Jedes veröffentlichte Paket (Flathub, später ggf. Steam) bekommt einen Git-Tag `vX.Y.Z`. Änderungen bis zur nächsten
Version sammelt `## [Unveröffentlicht]`.

## [Unveröffentlicht]

Stufe 1 – Konto + Start, Wechsel auf Avalonia 12 (Version 0.1.0, nicht veröffentlicht).

### Hinzugefügt

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
