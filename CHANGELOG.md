# Changelog – Rebels of Gaming für Linux

Versionen nach [SemVer](https://semver.org/lang/de/), Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/).
Jedes veröffentlichte Paket (Flathub, später ggf. Steam) bekommt einen Git-Tag `vX.Y.Z`. Änderungen bis zur nächsten
Version sammelt `## [Unveröffentlicht]`.

## [Unveröffentlicht]

Stufe 10 – Feinschliff und Steam Deck, Stufe 9 – Verwaltung + Moderation, Stufe 8 – Wiki, Stufe 7 – Server-Steuerung, Stufe 6 – Clan-Prozesse, Stufe 5 – Medien, Stufe 4 – Kommunikation, Stufe 3 – Echtzeit + Desktop-Integration, Stufe 2 – Community lesen; davor Stufe 1 – Konto + Start und Wechsel auf Avalonia 12 (Version 0.1.0, nicht veröffentlicht).

### Hinzugefügt (Stufe 10)

- **Controller**: mit Gamepad oder Steam Deck bedienen (Steuerkreuz, A/B, Schultertasten wechseln den Bereich); Belegung unter Einstellungen → Controller
- **Spielmodus** auf dem Steam Deck: Vollbild, größere Knöpfe und Schrift, Hinweis auf die Bildschirmtastatur
- **Barrierefreiheit**: besser lesbare rote Schrift (Kontrast AA), deutlich sichtbarer Fokusrahmen, Escape schließt jedes Fenster, „Bewegung reduzieren“ des Desktops wird beachtet
- **Schneller Start** (rund 0,8 s bis zum Fenster)
- Paket: AppStream-Beschreibung DE/EN/FR, Starter mit Schnellzugriffen, Flatpak-Manifest für Flathub vorbereitet (nicht eingereicht)

### Hinzugefügt (Stufe 9)

- **Verwaltung** (mit Rechten): Übersicht mit Systemstatus und Audit-Log, Mitglieder und Rollen, Bewerbungen entscheiden, Meldungen, Sperren, Einsprüche, Wortfilter, Mod-Log, Minecraft-Gruppen, Rebel-Coins, Minecraft-Netzwerk, Galerie-Freigaben, Upload-Meldungen und Website-Bereiche im Editor
- **Editoren** in eigenen Fenstern: Squad verwalten, News schreiben (mit Titelbild, Planen und Vorschau), Matches eintragen
- **Melden** an Forenbeiträgen, Profilen, Galerie-Bildern, Wiki-Seiten, Direktnachrichten und News-Kommentaren
- **Minecraft-Weltkarte** mit Warps und Live-Spielern; Beitrittsdaten und Ressourcenpaket im Minecraft-Reiter bearbeiten (mit Recht)

### Hinzugefügt (Stufe 8)

- **Wiki** in der App: Bereiche, Seitenbaum, Suche, Kategorien, Inhaltsverzeichnis, Beobachten
- Seiten **bearbeiten** mit Vorschau; hat jemand anderes gleichzeitig gespeichert, zeigt die App die Unterschiede
- **Versionen** vergleichen, wiederherstellen, freigeben oder ablehnen (mit Recht); neue Seite anlegen; Bild einfügen
- **Offline lesen**: gelesene Seiten bleiben verschlüsselt verfügbar, ganze Bereiche lassen sich vorab speichern
- Links in News, Forum und Wiki öffnen Seiten der Plattform direkt in der App

### Hinzugefügt (Stufe 7)

- **Server steuern** (mit Recht): eigenes Fenster je Server mit Start/Neustart/Stopp/Kill, Live-Ressourcen und Live-Konsole (Befehle mit ↑/↓, Mitscrollen, Kopieren, Neuverbinden)
- **Configs** mit Prüfen, Unterschieden vor dem Speichern, Versionen und Zurückspielen; **Dateien** ansehen, kopieren, umbenennen, löschen; **Backups** anlegen, sperren, wiederherstellen, löschen
- **RCON, Sperren, Whitelist, Spiel-Bans mit Plugin-Schlüssel und Vier-Augen-Freigaben**
- **Gameserver einrichten und verwalten** (Node, Spiel-Vorlage, Limits, Ablauf, Ports)
- **Ban-Liste** in der App mit Einspruch
- **App-Sperre**: mit PIN oder Fingerabdruck (fprintd) entsperren, beim Start und nach Abwesenheit

### Behoben (Stufe 7)

- Beim Schließen des Fensters konnte die App hängen bleiben, wenn gerade eine Einstellung gespeichert wurde

### Hinzugefügt (Stufe 6)

- **Abstimmungen** in der App: Bewerber, Antworten, Trial-Aufgaben, Beteiligung mit Quorum; Dafür, Dagegen oder Enthaltung
- **Bewerben** direkt auf der Squad-Seite mit allen Fragen des Squads, Regelwerken und Probezeit
- **Regeln und Rechtstexte bestätigen**: Knopf unter dem Text; was noch bestätigt werden muss, zeigt ein Hinweis über jeder Seite
- **Matches**: Ergebnisse mit Maps, Line-up und Bericht, Hall of Fame, Statistiken mit Ranglisten und Wochen-Awards, Turniere
- **Minecraft-Bestenlisten** (auch aus dem Minecraft-Reiter der Server)
- **Freunde**: Anfragen, hinzufügen, entfernen, Nachricht schreiben, live aktualisiert; „Als Freund hinzufügen“ im Profil
- **Mitgliederkarte** in eigenem Fenster (nur Stadt-Genauigkeit und nur, wer zugestimmt hat; Kacheln nur über die Plattform)
- **Konto**: Meine Bewerbungen (Verlauf, Trial-Aufgaben, Nachrichten, Zurückziehen), Abwesenheit melden, Minecraft-Konten und Rebel-Coins, Geräte abmelden, Datenexport herunterladen, Konto löschen
- **Einstellungen → Benachrichtigungen**: je Ereignis Glocke, E-Mail und Discord, Sammel-Mail und Ruhezeit

### Behoben (Stufe 6)

- Start ohne Absturz, wenn die Adresse des Session-Bus kaputt oder `XDG_RUNTIME_DIR` sehr lang ist

### Hinzugefügt (Stufe 5)

- **Bildschirmfoto hochladen** (Upload-Seite): der Desktop fragt über das Screenshot-Portal, welcher Bereich geteilt wird, danach geht das Bild wie jede andere Datei hoch

- **Uploads**: Bilder und kurze Videos per Drag & Drop, „Dateien wählen“ oder Strg+V (kopierte Dateien oder ein Screenshot) hochladen, mit Fortschritt und Sichtbarkeit; der Link wird danach kopiert; eigene Uploads mit Vorschau und Knöpfen für Link, BBCode und Markdown
- **Kurzlinks** (Reiter auf der Upload-Seite): anlegen, Klicks sehen, kopieren
- **Galerie**: Alben, Bilder und Clips, Großansicht mit Pfeiltasten, Einreichen mit Bildunterschrift und Bestätigung der Rechte
- **Downloads**: herunterladen in den Downloads-Ordner mit Prüfsumme und „Im Ordner zeigen“
- **Profil bearbeiten** (Konto): Motto, Über mich, Stadt/Land, Mitgliederkarte, Online-Status, Hall of Fame, Avatar und Banner

### Hinzugefügt (Stufe 4)

- **Direktnachrichten schreiben**: Enter sendet, Umschalt+Enter neue Zeile; auf eine Nachricht antworten (Zitat), Text kopieren; neue und gelöschte Nachrichten erscheinen live; Bild-Anhänge mit Vorschau, andere Dateien öffnen sich im Browser
- **Chat in eigenem Fenster** (je Unterhaltung eins, optional immer im Vordergrund) und „Nachricht senden“ im Profil
- **Forum**: Bereiche und Unterbereiche, Themen (angeheftet, geschlossen, Umfrage, ungelesen), Beiträge mit Formatierung, Reaktionen, Umfragen mit Abstimmen, Antworten mit BBCode-Leiste und Vorschau (Strg+Enter sendet), Zitieren, neues Thema; Beiträge blockierter Personen eingeklappt mit „Trotzdem anzeigen“; neue Beiträge kommen live
- **Zusagen** im Kalender: Dabei, Vielleicht, Absagen
- **Offline**: ohne Netz geschriebene Nachrichten, Forenantworten und Zusagen warten und gehen automatisch raus, sobald wieder Netz da ist; zuletzt gesehene Inhalte (News, Forum, Profile …) sind nach dem Start sofort da – verschlüsselt, der Schlüssel liegt im Schlüsselbund
- **Tastenkürzel**: Alt+←/→, Strg+N, Strg+R, Strg+W, Strg+Q, F1 (Liste unter Einstellungen)

### Hinzugefügt (Stufe 3)

- Server-Seite mit Reitern wie in der Windows-App: **Minecraft** („So kommst du auf den Server“ für Java, Bedrock und Konsole mit Kopieren, Konsolen-Hinweis, Ressourcenpaket zum Herunterladen, Weltkarte, Bestenlisten, Konto verknüpfen), **TeamSpeak** (wer ist in welchem Kanal, Trenner als Überschriften, stumm/abwesend als Symbol, „Leere Kanäle zeigen“, „TeamSpeak verbinden“ mit deinem RoG-Namen – ohne TeamSpeak wird die Adresse kopiert) und **Discord-Voice** (angemeldet; im Gastmodus Hinweis mit „Anmelden“); Voice aktualisiert sich alle 30 Sekunden und live, F5 lädt den offenen Reiter neu; `rog://servers/minecraft`, `…/teamspeak`, `…/discord`
- **Tray-Symbol** (StatusNotifierItem: KDE, XFCE, Cinnamon, GNOME mit AppIndicator-Erweiterung, Steam Deck im Desktopmodus): Linksklick öffnet, Menü mit Öffnen, Benachrichtigungen, Nachrichten, Server und Beenden, Tooltip mit der Zahl ungelesener Einträge
- **Beim Schließen in den Infobereich** (Einstellung, Standard an): Benachrichtigungen kommen weiter; nur, wenn ein Panel das Symbol wirklich zeigt – sonst Hinweis in den Einstellungen und das Fenster beendet die App
- **Beim Anmelden am Desktop starten**: startet ohne Fenster im Infobereich (`--background`); in Flatpak über das Background-Portal (das System fragt einmal nach)
- In Flatpak: Desktop-Benachrichtigungen über das Notification-Portal (Klick und „Öffnen“ wie bisher)

- Echtzeit über Reverb (Kern): Live-Ticker der Startseite, Serverstatus und eigene Ereignisse kommen sofort; nach einer Neuverbindung gleicht die App die Zähler ab; „Echtzeit: verbunden“ unter Einstellungen → Über die App
- Glocke: Benachrichtigungen mit Symbol, „Nur ungelesene“, „Alle gelesen“, „Mehr laden“; ein Eintrag öffnet das Ziel in der App (Termin, Squad, News …) oder genau die Seite der Website; neue Einträge kommen live dazu
- Zähler an Glocke und Nachrichten in der Seitenleiste (aufgeklappt als Zahl, sonst als Punkt)
- Desktop-Benachrichtigungen für neue Benachrichtigungen und neue Direktnachrichten, solange die App im Hintergrund läuft – Klick auf „Öffnen“ holt das Fenster nach vorn und öffnet das Ziel
- `rog://` für alle Ziele der Windows-App: Votes, Verwaltung, Bans, Bestenlisten, Karte sowie Forum-Themen, Wiki-Seiten, Tools und Matches öffnen die passende Website-Seite; Links, die während des Logins ankommen, öffnen sich nach der Anmeldung
- Tests: Glocke, Zähler, Desktop-Benachrichtigungen mit Klick (auch gegen einen privaten D-Bus), alle `rog://`-Bereiche

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

### Geändert (Stufe 2 und 3)

- Flatpak-Entwurf: `--filesystem=xdg-download` für Downloads (Stufe 5)
- Flatpak-Entwurf: `--talk-name=org.kde.StatusNotifierWatcher` statt `--talk-name=org.freedesktop.Notifications` (Benachrichtigungen und Autostart über Portale)

- CI: `actions/checkout@v7` und `actions/setup-dotnet@v6` (Node 24)
- Hinweis zu Desktop-Benachrichtigungen in den Einstellungen beschreibt jetzt, was gemeldet wird

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
