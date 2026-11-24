# Verboten – Design (Entwurf v0.1)

Untertitel im Workshop: "Verboten – Base Claims & Anti-Griefing". Eigenentwicklung (Clean Room): Konzept und Erkenntnisse aus der
Analyse von "Protected Areas" werden genutzt, dessen Code wird **nicht** übernommen.

> **Abschnitt 0 ist verbindlich.** Wo ältere Abschnitte davon abweichen, gilt Abschnitt 0.

## 0. Beschlüsse (ab 2026-11-17; neuere Abschnitte 0.15–0.17 am Ende gehen älteren vor)

Stand der Tests (2026-11-24): als Host im Spiel geprüft: Claim anlegen, Befehle, Fenster, Funken, Fackeln.
Noch nie mit einem zweiten Spieler gelaufen: der Schutz selbst, Betreten/Rauswurf, PIN, Spenden, Strafe, Felder.

### 0.1 Form, Größe, Abstände
> **Überholt für Claims mit Autolathe und `Growing = true`:** Form, Größe und Wachstum regeln jetzt 0.16/0.17
> (Felder von 10 x 10 m, Ansprüche, Konto). Hier gilt nur noch: Kompass, Abstände, Landezonen – und alles für
> Flaggen-Claims und `Growing = false`.

- **Himmelsrichtungen wie der Kompass im Spiel:** die HUD-Anzeige ist (Blickrichtung + 180) % 360, also
  Norden = -Z, Osten = -X (im Spielcode `PlayerStateWindow` gefunden). Alle Seiten-Namen und Richtungs-Hinweise folgen dem.
- Claim = Rechteck um die Autolathe (bzw. Flagge), unbegrenzt nach oben/unten.
- Vier Seiten (Nord/Ost/Süd/West), jede **5–45 m** von der Autolathe; **Start: 5 m je Seite** (10 × 10 m, Autolathe mittig)
  plus **Gratis-Meter** (`StartFreeMeters`, Standard 40 = z. B. 30 × 30 m bei gleichmäßiger Verteilung).
  Der Besitzer bestimmt selbst, in welche Richtungen er sie verteilt.
- Breite (W+O) und Tiefe (N+S) jeweils **höchstens 50 m**.
- **Abstand zwischen Claims ≥ 10 m** (Rand zu Rand). Prüfung beim Erstellen und bei jeder Größenänderung.
- **Landezonen:** kein Claim-Rand näher als **Spawn-Radius + 20 m** an einem Startpunkt (Spawn-Radius aus den
  Spieldaten, Standard 30 m → 50 m). Jeder Startpunkt (auch Round-Robin) eigene Zone. Einstellung
  `SpawnProtectionDistance` entfällt.

### 0.2 Vergrößern / Verkleinern
> **Überholt** durch 0.15 (nur noch Blickrichtung) und 0.16 (feldweise mit `/claim grow`, Konto statt Gratis-/verdienter
> Meter, Ansprüche). Admins verkleinern feldweise mit `/claim shrink`.

- Immer eine **ganze Seite**, meterweise: `/claim grow <north|east|south|west|all> <m>`.
- **Besitzer können nur vergrößern**, nicht verkleinern.
- **Admins:** kostenlos, vergrößern und verkleinern (`/claim shrink …`), innerhalb derselben Grenzen.
- **Besitzer:** zuerst **Gratis-Meter vom Start**, dann **verdiente Wachstumsmeter** (1 m pro 5 h, in denen Besitzer oder ein Freund in der Welt
  online ist), der Rest wird mit **Stahl aus der Claim-Autolathe** bezahlt (aus deren internem Materialspeicher),
  **Preis pro m² steigt mit der Claim-Fläche**. Genaue Zahlen: Einstellungen, nach Test festlegen.
- Flagge als Anker: nur Admins können die Größe ändern (kein Speicher zum Bezahlen).
- `/claim info` zeigt Seitenlängen, verdiente Meter und den Preis des nächsten Meters.

### 0.3 Fremde im und am Claim
- **Annäherungszone 10 m** um jeden Claim: einmalige Nachricht an Fremde (max. 1× pro Stunde je Spieler und Claim);
  Fremde können dort **nichts fallen lassen, werfen oder bauen**.
- **Grenze sichtbar (2026-11-17):** bei **jedem** Betreten der Zone 10 s Funken an den Grenzen in der Nähe – die Spieler
  sehen es, statt es nur zu lesen. Überfliegen und Untergraben bleiben verboten (keine Höhengrenze, so entschieden).
- **Bauen in Zonen:** an einer Stelle darf nur bauen, wer in **jedem** Claim Rechte hat, dessen 10-m-Zone die Stelle
  erreicht. Streifen, die zwei Zonen erreichen, sind Niemandsland (keiner kann den Nachbarn einmauern).
- **Fremder im Claim → nach 3 s vom Server getrennt** (Countdown-Nachricht).
- **3× getrennt in 30 min → 30 min Beitrittssperre** (über die eigene Beitrittsprüfung, nicht die Bannliste des Spiels).
- **Getrennte / offline gegangene Fremde** werden mit ihrem Charakter **vor die Tür gesetzt** (nächster sicherer Punkt
  außerhalb aller Claims). Grundlage: offline steuert der Server den Charakter. → Keine Frist beim Einloggen nötig.
- **10 s Frist** nur, wenn man Rechte verliert, während man drin steht, oder wenn um einen herum geclaimt wird.
- Bewusstlose und Tote werden nicht getrennt.
- **PvP:** Angriffe von Fremden auf Personen im Claim werden blockiert; Explosionen von Gegenständen (Granaten,
  Dynamit …) verletzen im Claim niemanden. Unfälle eigener Geräte bleiben normal.
- **Leichen:** Inventar von Toten im Claim ist geschützt wie alles im Claim (nur Besitzer/Freunde; Gäste nicht).
- Netze nach außen und Feuer durch Fackeln: kein Handlungsbedarf (so entschieden).

### 0.4 Befehle und Verwaltung
- `/claim delete` für Besitzer (nur eigener Claim) und Admins. Danach erzeugt eine neue Autolathe außerhalb
  von Claims wieder einen Claim.
- `/claim owner <Spieler>`: Besitz übergeben, alter Besitzer wird Freund, Limit des Neuen wird geprüft.
- **`/claim create` entfällt**, ebenso Anker-Einstellung `None` (Claims nur über Autolathe/Flagge).
- **Verfall:** `ClaimExpiryDays` (Standard 7). Gezählt wird nur Zeit, in der der Server läuft; zurückgesetzt,
  sobald Besitzer oder ein Freund online ist. Danach wird der Claim **verlassen** (nicht gelöscht), siehe 0.7.
- **Autolathe umstellen:** steht beim Abbau des Ankers eine weitere Autolathe im Claim, wird sie neuer Anker; der
  Claim bleibt genau gleich (gespeichert als festes Rechteck in der Welt). Erst ohne Autolathe im Claim endet er.
  Texte: Claim-Fenster erklärt es; beim Bau einer zweiten Autolathe im eigenen Claim: "Your claim stays the same.
  Or claim another one."; beim Wechsel: "Your claim now belongs to the autolathe at …".
- **Bezahltes Wachsen bestätigen:** erster Befehl nennt den Preis, Wiederholung innerhalb 30 s zahlt.
- **"Während du weg warst":** beim Einloggen kurze Zusammenfassung der Vorfälle im eigenen Claim (letzte Vorfälle je Claim).
- **Ruinen ohne Claim:** fremde Bauteile blockieren das Claimen nur, wenn ihr Erbauer einen aktiven Claim hat oder in den
  letzten 7 Tagen (Server-Laufzeit) online war. Dafür merkt sich der Server bei allen Spielern "zuletzt online".
- **Verzögerte Aktionen:** Absender wird beim Zurückstellen gemerkt und beim Ausführen verwendet; geht das nicht:
  Chat "Sorry, Verboten is not (yet) that fast. Please try again."
- **Freunde/Gäste hinzufügen per Name (2026-11-17):** der genaue Name reicht, wenn der Spieler schon einmal auf dem
  Server war (Verboten merkt sich alle) – er muss nicht online sein. Nie da gewesen → SteamID64. Gleicher Name bei
  mehreren Spielern → SteamID64 nötig (kein Rechte-Klau per kopiertem Namen). Die Bestätigung nennt, was die Rolle darf.
- **Bauen in der Landezone:** Einstellung `BuildingInLandingZone` (Standard `true`, wie im Spiel: auf frischen Welten
  bauen die ersten Spieler am Lander). `false`: dort bauen und abbauen nur Admins; Benutzen und Kisten leeren geht weiter.

### 0.5 Neue Spieler informieren (überarbeitet 2026-11-17)
Grundgedanke: erklären, wenn es gebraucht wird. Kaum jemand fängt mit der Autolathe an – fast immer kommt zuerst ein
Eisenrahmen. Deshalb hängt die Erklärung am **ersten Bauteil**, nicht am Autolathe-Bausatz.
1. **Beim Beitreten** (jedes Mal, nur für Spieler ohne Claim und ohne Rolle in einem): zwei Chatzeilen – Basen sind
   geschützt, Fremde fliegen nach 3 s; `/claim check`, `/claim show`, `/claim help`; ggf. verlassene Basen (`/claim find`).
2. **Fenster beim ersten Bauteil** (einmal pro Spieler und Welt, nur ohne eigenen Claim und außerhalb von Claims):
   Größe, wo es nicht geht, **Ergebnis für genau diese Stelle** (✔ / ✘ mit Richtung und Entfernung), Rollen, Verfall,
   Befehle. Bei ✘ zusätzlich Funken an der störenden Grenze.
3. **Bau-Hinweis:** baut ein Spieler ohne Claim später an einer Stelle, wo kein Claim geht (Landezone, Nachbar zu
   nah) → Chatzeile mit Richtung + Entfernung und Funken, höchstens 1× pro Minute. Nur Abstände, keine Suche über
   alle Bauteile (billig).
4. **Beim Claimen** (neu oder übernommen): 30 s Funken an der neuen Grenze – der Besitzer sieht sofort, was geschützt
   ist, und entscheidet selbst, ob er wächst. Das Fenster erklärt Rollen und Verfall.
5. **`/claim check`:** prüft den eigenen Standort wie einen Autolathe-Bau, mit Grund und **Richtung + Entfernung**.
6. **`/claim show`:** Funken an Claim-Grenzen **und** am Rand der Landezone in der Nähe (10 s).
7. **Rollen und Verfall** stehen außerdem in `/claim help`; die Verfallsdauer auch in `/claim info`.
Entfallen: Begrüßungsfenster beim ersten Beitreten (kam mitten im Landestress), Bausatz-Hinweis.

### 0.7 Verlassene Claims
- Verfallene Claims bleiben als **verlassen** gespeichert (Form, Größe, gewachsene Meter, Anker), **ohne Schutz**.
- Betritt jemand einen verlassenen Claim: einmalige Nachricht
  - Anker steht noch: "This base was abandoned by X. Type /claim take to make it yours." (vorhandene Autolathe wird Anker)
  - kein Anker mehr: "… Build an autolathe here to make it yours."
- Übernahme: Claim inkl. gewachsener Meter (soweit noch frei, s. u.), Freundesliste geleert, Claim-Limit des Neuen geprüft.
- **Verlassene Claims weichen aktiven Spielern:** aktive Nachbarn dürfen in sie hineinwachsen bzw. daneben eine
  Autolathe bauen; der verlassene Claim wird auf der betroffenen Seite zurückgeschnitten (10 m Abstand bleibt).
  Haben aktive Spieler darin gebaut, werden bei der Übernahme die betroffenen Seiten verkleinert, bis deren Bauteile
  draußen liegen. Fällt eine Seite dabei unter 5 m, verfällt der verlassene Claim endgültig.
- Alter Besitzer kommt zurück: noch nicht übernommen → automatisch wieder seiner und geschützt; schon übernommen →
  Fenster "Your base was taken over by Y because you were away for more than 7 days."
- Die Chatzeilen beim Beitreten nennen die Anzahl verlassener Basen; `/claim find` zeigt Richtung und Entfernung zur nächsten.
- **Tracker:** an jeder verlassenen Basis stellt der Server 3 m vor die Grenze (Seite Richtung Landezone) einen
  festen Beacon "Abandoned: <Name>" – erscheint auf jedem Tablet-Tracker, auch ohne Strom. Geschützt (nur Admins
  können ihn ändern/entfernen, Explosionen wirken nicht), wird bei Übernahme/Rückkehr/Auflösung entfernt, jede
  Server-Minute überprüft und bei Bedarf neu gesetzt.

### 0.8 Firewall-Lücke und Schummel-Schutz
- **Pflicht:** Die Firewall prüft künftig alle referenzierbaren Ziele, nicht nur Dinge: **Räume/Atmosphären,
  Rohr- und Kabelnetze** (Lücke: "Gas hinzufügen" kann sonst jeden Raum/jedes Rohrnetz in Claims fluten).
- **Recherche-Ergebnis (2026-11-17), drei Gruppen.** Host und bestätigte Admins sind überall ausgenommen.
  - **A – schickt das Spiel nie** (nur manipulierte Clients): `SyncGameMode` (Spielmodus des Servers umstellen!),
    `SyncCustomGrowSpeed` (Pflanzenwachstum des Servers), `ThingSpawnMessage` (beliebiges Ding an beliebiger Stelle).
    → verwerfen, **sofort kicken** (keine Sperre), Log + Nachricht an Admins.
  - **B – Schummel-Funktionen des normalen Spiels**, für **jeden** über die F3-Konsole erreichbar (Befehle `addgas`
    und `thing`) bzw. aus dem Kreativ-Menü: `AddGasCommand`, `SpawnDynamicThingMaxStackMessage`,
    `SpawnSpawnDataMessage`, `CreateStructureMessage` mit `AuthoringMode` (Bauen ohne Bausatz mit dem Authoring-Tool).
    → im **Überlebensmodus** verwerfen + Nachricht "Cheats are disabled on this server", **kein Kick**
    (Entscheidung 2026-11-17); im **Kreativmodus** erlaubt (Claims schützt weiterhin die Firewall).
  - **C – schickt das Spiel auch im normalen Ablauf:** `ConstructionCreationMessage` (Bauteil ersetzen bei Kabeln,
    Rohren, Bauteilen) → nur Firewall (Claims + Bauregel in Zonen), **nie kicken**.
  - `SpawnIncidentRequestToServer` tut auf dem Server nichts → ignorieren.

### 0.9 Roboter
- Roboter handeln ohne Nachrichten direkt auf dem Server (graben Erz, legen es in Roboter-Eingänge).
- Fremde Roboter: im Claim **nicht graben**, und wie getrennte Spieler **vor die Tür setzen** (Server steuert sie).
- **Recherche-Ergebnis (2026-11-17):** Einziger selbstständiger Roboter ist der Mining-Roboter (`RobotMining`,
  "AIMeE"); das Spiel führt eine Liste aller (`RobotMining.AllRobots`). Ein **Besitzer wird vom Spiel nicht
  gespeichert** (bei hergestellten Dingen bleibt `OwnerClientId` 0). Roboterarme sind fest verbaute Bauteile, Rover
  werden von Spielern gefahren (→ normale Regeln), der Händler-Roboter gehört dem Spiel.
- **Besitz-Regel (Verboten merkt sich das selbst, gespeichert mit dem Spielstand):**
  - Besitzer = wer den Roboter **zuletzt getragen oder gezogen** hat (Prüfung 1×/s, nur solange es mindestens einen Roboter gibt).
  - Taucht ein Roboter ohne Besitzer zum ersten Mal **innerhalb** eines Claims auf (z. B. frisch hergestellt),
    gehört er dem Claim-Besitzer.
  - Sonst unbekannt = fremd.
- **Graben verhindern:** `Vein.TryMineServer` – liegt die Stelle in einem Claim und gräbt ein Roboter (erkannt über
  das Ziel des Roboters), braucht sein Besitzer dort das Recht Minen.
- Ergänzung: Explosionen von Gegenständen graben in Claims auch **kein Gelände** um (sonst Erz-Diebstahl per Dynamit).

### 0.10 Ausbau abschaltbar (Beschluss und Bau 2026-11-17, im Spiel ungetestet)
Für Server, die einfach nur ihre (bestehenden) Basen schützen wollen: `[Growing] Enabled` (Standard `true`).
Bei `false` gilt:
- **Der Claim passt sich der Basis an:** Beim Bau der Autolathe deckt er alles ab, was Besitzer (und Freunde) im
  Umkreis von 45 m gebaut haben, **plus 5 m Rand** – höchstens 50 × 50 m. Das Spiel speichert bei jedem Bauteil,
  wer es gebaut hat (`OwnerClientId`, auch in alten Spielständen), deshalb klappt das auch bei bestehenden Basen.
- **Wächst von selbst mit:** Baut der Besitzer oder ein Freund näher als 5 m am Rand (oder bis 5 m davor), rückt
  die Seite nach, bis wieder 5 m Rand da sind. Gleiche Grenzen wie immer: 45 m ab Anker, 50 × 50 m, 10 m Abstand zu
  anderen Claims, nicht in die Landezone, nie über Bauteile anderer aktiver Spieler. Verlassene Claims weichen.
- Gewachsen wird meterweise und reihum, damit ein zu großer Bau gleichmäßig abgedeckt wird.
- **Gemeinsame Basen:** Bauteile anderer aktiver Spieler bleiben draußen; das Erstellen-Fenster und `/claim check`
  nennen sie. Wird so ein Spieler mit `/claim add` als Freund eingetragen, deckt der Claim auch seinen Teil ab.
- Keine Gratis-/Spielzeit-Meter, kein Stahl, `/claim grow` nur für Admins (Admins können weiter vergrößern und
  verkleinern). Die übrigen `[Growing]`-Einstellungen werden ignoriert.
- Kosten: Prüfung bei jedem Bau ist ein Rechteck-Vergleich; die teure Suche nach fremden Bauteilen läuft nur, wenn
  der Claim wirklich wächst.

### 0.11 Gesamtprüfung 2026-11-17: geschlossene Lücken
- **Spielstände:** die Begleitdatei wurde bei jedem *neuen* Spielstand (Auto-, Schnell-, Neu-, "Speichern
  unter") sofort wieder gelöscht – der Speicher-Hook läuft, bevor das Spiel die Datei schreibt, und das Aufräumen
  hielt sie für verwaist. Folge wäre gewesen: solche Stände laden ohne Claims. Behoben (aktuelle Datei bleibt).
- **"Nur über sich selbst"-Nachrichten** (`SelfOnly`): das Spiel glaubt die Ids darin. Ein manipulierter Client
  konnte jeden Spieler töten (`EntityStateMessage` setzt den Zustand beliebiger Wesen – auch in Claims), aus
  seinem Körper werfen (`RelinquishControlMessage`), für andere respawnen (`RespawnMessage`), Aussehen/Emotes/
  "tippt…" anderer steuern. Jetzt nur noch für den eigenen Charakter bzw. die eigene SteamID; sonst blockiert
  (kein Kick: falls das Spiel so etwas doch mal selbst schickt). Unbekannter Absender → wie im Spiel.
- **`SyncTimeMessage`** schickt nur der Server; von einem Client verschiebt sie die Server-Uhr → Gruppe A (Kick).
- **Namen mit Steuerzeichen** (manipulierter Client) hätten die Begleitdatei unlesbar gemacht (= alle Claims
  aus). Beim Schreiben entfernt, beim Lesen tolerant.
- **Befehls-Flut:** `/claim check`/`info` durchsuchen alle Bauteile; ein Client hätte den Server per Dauerfeuer
  ausbremsen können → höchstens 1 Befehl pro Sekunde und Spieler.
- **"Verboten" als Spielername** sah im Chat aus wie Server-Meldungen → wird zu "Verboten (player)".
- **Countdown-Trick:** kurz raus und wieder rein startete die 3 s neu. Jetzt läuft der Countdown weiter, wenn man
  innerhalb von 10 s zurückkommt.
- **Bauteile von Freunden** zählten beim Wachsen (`/claim grow`) und beim Übernehmen als fremd. Behoben.
- **Wachsen und Bauteile anderer:** ein Claim wächst nicht mehr bis auf 10 m an die Bauteile anderer aktiver
  Spieler heran (sie könnten dort sonst nichts mehr bauen); war etwas schon so nah, darf es nur nicht hinein.
  Erstellen bleibt wie bisher (nur nicht *über* fremde Bauteile) – sonst könnten Koop-Gruppen nicht claimen.
- **Rückkehr in einen verlassenen Claim:** was andere aktive Spieler in der offenen Zeit darin gebaut haben,
  bleibt draußen (Claim wird zurückgeschnitten), wie bei der Übernahme.
- **Funken beim Betreten der Zone** zeigen nur noch diesen Claim (weniger Netzlast).
- **Roboter-Besitzer** zerstörter Roboter werden nicht mehr ewig mitgespeichert.

Entschieden danach: "Claim direkt neben fremdem Lager" bleibt erlaubt (strenger wäre eine Hürde für Gruppen, die
zusammen anfangen; Admins können löschen). Flagge und Preisanstieg-Einstellung bleiben.

### 0.12 PIN gegen gefälschte Identität (Beschluss und Bau 2026-11-17, im Spiel ungetestet)
- Hintergrund: auf dedizierten Servern sind alle Spieler per IP verbunden, ihre SteamID ist nicht prüfbar.
- `/claim pin <code>`: freiwillig, frei wählbar, 1–10 Zeichen. Gespeichert wird nur ein Hash (pro Spieler verschieden).
- Kommt ein Spieler mit PIN per IP von einer Adresse, die Verboten von ihm nicht kennt (letzte 5, als Hash), ruhen
  seine Rechte in allen Claims, bis er `/claim pin <PIN>` eingibt: er darf in seiner Basis stehen (kein Rauswurf),
  aber nichts benutzen, bauen oder nehmen; Claim-Befehle außer `pin`/`help` warten; er baut keine Claims. Richtige
  PIN → Adresse wird gemerkt. 3 falsche → 10 min warten.
- Ändern `/claim pin <neu>`, entfernen `/claim pin off` – nur von bekannter Adresse bzw. nach der PIN.
  Vergessen: Admin `/claim pin off <Spieler>`. Steam-P2P-Spieler brauchen die PIN nie.
- Hinweis im Claim-Fenster nur für IP-Spieler ohne PIN. Ersetzt die frühere Adress-Warnung (`ImpersonationWatch`).
- Grenze: die Spielfigur samt Inventar bekommt ein Betrüger weiterhin (Lücke im Spiel selbst), die Basis nicht.

### 0.13 Abstecken mit Flaggen (Einstellung `Anchor = Flag`, Beschluss und Bau 2026-11-17, im Spiel ungetestet)
- **Eine Flagge an jede Ecke:** sobald ein Spieler 4 eigene Flaggen außerhalb von Claims im Umkreis von 50 m hat,
  wird das Rechteck um die äußersten zum Claim. 4 statt 3: Claims sind immer Rechtecke in Nord-Süd/Ost-West-
  Richtung; "eine Flagge pro Ecke" passt genau dazu (bei 3 erwartet man ein Dreieck). Eine Zahl im Code.
- Größe 10 × 10 bis 50 × 50 m (Flaggen in einer Reihe → auf 10 m verbreitert). Sonst dieselben Regeln: 10 m zu
  anderen Claims, nicht in der Landezone, nicht über fremde Bauteile. Chat zählt mit: "Flag 2 of 4".
- **Größer:** eine weitere Flagge (Besitzer oder Freund) neben dem Claim schiebt die Grenze bis zu ihr (Regeln wie beim
  Wachsen, 10 m Abstand zu fremden Bauteilen). **Kleiner:** Flagge abbauen → Grenze folgt den restlichen (nur
  kleiner, nie größer); unter 4 Flaggen endet der Claim. Beim Laden bleibt die gespeicherte Grenze.
- Flagge in einem verlassenen Claim → Übernahme. `/claim grow`/`shrink` gibt es für abgesteckte Claims nicht
  (auch nicht für Admins: die Flaggen bestimmen); die Ausbau-Einstellungen gelten nicht.
- Das Spiel speichert, wer welche Flagge gebaut hat → nichts geht verloren, wenn der Server zwischendurch neu startet.

### 0.14 Sprachen (2026-11-17)
- Alle Texte für Spieler stehen in `src/Lang/<code>.txt` (eingebaut in die DLL): `[schlüssel]`, darunter der Text,
  `{platzhalter}` füllt der Mod. Übersetzer brauchen kein C#. Fehlt ein Text, gilt Englisch, dann der Schlüssel.
- Einstellung `[General] Language` = `English` | `Deutsch` – eine Sprache pro Server (der Server kennt die
  Spielsprache der Clients nicht). Log und `claim status` bleiben Englisch (für Admins und Fehlerberichte).
- Deutsch: konsequent "Sie", Amtsdeutsch mit Augenzwinkern ("Betreten VERBOTEN!", "Privatgrundstück", "herrenlos", "Bauvoranfrage",
  "Besondere Vorkommnisse", "müssen leider draußen bleiben", "des Servers verwiesen"), Inhalt bleibt sachlich.
  Standardname "Basis von Bob" (keine Genitiv-Probleme), Bake "Herrenlos: …". Beitritt: "… werden nach 3 Sekunden
  erschossen – Verzeihung – des Servers verwiesen." Bau-Fenster: "Vertrauen ist gut, Verboten ist besser."
- Englisch im selben Ton: ein sehr korrektes deutsches Grundstücksamt, das Englisch schreibt ("Betreten VERBOTEN!",
  "trespassers will be shot – sorry – disconnected", "Digging: VERBOTEN.", "derelict", "Lost property office",
  "Planning permission granted", "Incident report", "Ordnung muss sein.", "Trust is good, Verboten is better.").
- Befehle bleiben englisch; auf Deutsch versteht er zusätzlich nord/ost/süd/west/alle, gast/freund, `pin aus`.
- Tests: beide Dateien haben dieselben Schlüssel und Platzhalter, und jeder Schlüssel aus dem Code existiert.
- **Im Spiel prüfen:** Der Chat wird vom Spiel mit ImGui gezeichnet (reiner Text, keine Formatierung). Ob seine
  Schrift "–", „ “ und ✔ ✘ zeigt, sieht man erst im Spiel; sonst durch einfache Zeichen bzw. Wörter ersetzen.

### 0.6 Einstellungen danach
`[General]` `Language` (English | Deutsch) ·
`[Claims]` `Anchor` (Autolathe | Flag), `MaxClaimsPerPlayer`, `ClaimExpiryDays`, `BuildingInLandingZone` ·
`[Growing]` `Enabled` (0.10), `StartFreeMeters` (Standard 40), `HoursPerEarnedMeter`, Preis-Einstellungen
(Zahlen offen) · `[Admins]` `AdminSteamIds`.

## 1. Ziele

- Anti-Griefing für öffentliche Server: Spieler beanspruchen Gebiete ("Claims"), darin dürfen nur
  berechtigte Spieler bauen, abbauen, interagieren, Container leeren, minen, Schaden anrichten.
- **Nur der Host/Server braucht den Mod.** Clients mit Mod bekommen Komfort (Cursor, Grenzen, Teleport).
- **Sicher gegen manipulierte Clients:** keine Entscheidung auf Basis von Daten, die der Client
  frei setzen kann.
- **Kein messbarer Performance-Einfluss**, auch bei vielen Claims und Spielern.
- Loader: BepInEx 5 + StationeersLaunchPad. Logik loader-unabhängig halten (spätere Doorstop-Variante möglich).

## 2. Sicherheitsmodell (Kern des Ganzen)

### Befund aus dem Spielcode
- Der Server ruft `NetworkBase.DeserializeReceivedData(_hostId, …)` mit seinem **eigenen** statischen
  Feld auf, nicht mit dem Absender. `Process(long hostId)` kennt den Absender also nicht.
- Der Absender steht nur als lokale Variable in der Empfangsschleife von `NetworkManager`
  (RakNet: `Packet.Guid`, Steam P2P: `P2Packet.SteamId`).
- Alle "Wer handelt?"-Felder in Nachrichten (`ChatMessage.HumanId`, `InteractionMessage.SourceId`,
  `MoveToSlotMessage`, Construct-`steamId`, `MineAsteroid`-Spieler-ID …) setzt der Client selbst.

### Weiterer Befund: das Spiel prüft die SteamID nicht
- `NetworkServer.VerifyConnection` legt den Spieler mit **`msg.ClientId` und `msg.OwnerConnectionId`
  aus der Beitritts-Nachricht des Clients** an. Eine Steam-Prüfung (`SteamTransport.Authenticate`) existiert
  im Code, wird aber **nie aufgerufen**.
- **Steam P2P** (Beitritt über Steam/Einladung): der Absender jedes Pakets ist die von Steam bestätigte
  SteamID → prüfbar.
- **IP-Verbindung (RakNet, Dedicated Server / Direkt-IP):** Absender ist eine RakNet-GUID; die SteamID ist
  nur eine Behauptung des Clients. Ohne Mod auf dem Client nicht prüfbar (es bräuchte ein Steam-Ticket).

### Umsetzung (M1, `src/Game/SenderContext.cs`, `RemoteChat.cs`)
1. **Absender pro Paket:** Transpiler auf `NetworkManager.ReceiveEvents` ersetzt
   `HandleGeneralTraffic(_hostId, size)` durch `HandleGeneralTrafficWithSender(_hostId, size, sender)`.
   Die Sender-Variable wird über ihre Verwendung in `PlayerConnected`/`PlayerDisconnected` gefunden; alle
   Verwendungen müssen übereinstimmen, sonst wird nichts geändert (fail closed). Logik in
   `Core/SenderPattern.cs`, getestet gegen das echte Spiel-IL (Mono.Cecil).
2. **Herkunft:** `Local` (Host selbst), `Network` (Absender bekannt), `Deferred` (vom Spiel verzögerte
   Nachricht, Absender verloren → wie unbekannt). Lokal zählt **nur** der eigene Charakter des Hosts.
3. **Verbindungen, die der Server selbst geöffnet hat** (`PlayerConnected`) werden mit Methode gemerkt.
4. **Beitritts-Prüfung** (`VerifyConnection`), Ablehnung mit Meldung + Verbindung schließen, wenn:
   Verbindung unbekannt; behauptete Verbindungs-ID ≠ echter Absender; behauptete Methode ≠ echte;
   ungültige SteamID; **P2P: SteamID ≠ von Steam gemeldete**; SteamID des Hosts; SteamID schon online.
5. **`/claim` von anderen Spielern** läuft als verifizierter Absender (nie als `HumanId` aus der Nachricht),
   wird nicht öffentlich gesendet, Antworten gehen privat an den Spieler. Unbekannter Absender → nichts passiert.
6. **Normaler Chat** wird auf den echten Absender korrigiert (niemand schreibt als anderer Spieler oder als "Verboten").
7. **Admins:** Host und Server-Konsole immer; Admin-SteamIDs aus der Config **nur mit von Steam
   bestätigter SteamID** (P2P). Über IP verbundene Admins sind normale Spieler (Entscheidung 2026-11-16).
   **Dedizierte Server** starten Steam gar nicht (nur IP-Verbindungen) → dort gibt es keine bestätigten SteamIDs.
   Admins nutzen dort `serverrun claim …` (Spiel-Befehl mit dem `ServerAuthSecret` aus der settings.xml, vom Spiel
   geprüft): Verboten führt den Befehl als dieser Spieler mit Admin-Rechten aus (mit seiner Position) und antwortet
   ihm privat im Chat (2026-11-17).
8. **IP-Spieler** (Entscheidung 2026-11-16): bekommen ihre Rechte über die angegebene SteamID wie im
   normalen Spiel. Schutz dagegen: freiwillige **PIN** (siehe 0.12; ersetzt seit 2026-11-17 die frühere
   Adress-Warnung). Gespeichert werden nur Hashes der letzten 5 Adressen pro Spieler und der PIN.
9. **Default deny:** unbekannter Akteur ist kein Admin und hat in fremden Claims keine Rechte.

## 3. Performance-Modell

- **ClaimIndex:** unveränderliches Snapshot-Objekt, bei jeder Änderung neu gebaut und atomar
  ausgetauscht. Lesen ohne Lock, ohne Dateizugriff.
- **Räumliches Raster:** Zelle (z. B. 16 m) → Liste der Claims. Abfrage = 1 Hash-Lookup + wenige
  AABB-Vergleiche. Früher Ausstieg, wenn es keine Claims gibt.
- **Server-Prüfungen** nur bei eingehenden Aktions-Nachrichten (wenige pro Sekunde und Spieler).
- **Client-Hover/Cursor:** Ergebnis pro Ziel-Objekt zwischenspeichern (bis Index-Version wechselt).
- **Betreten (Trespass):** 2–4 Hz, nur Spieler in Rasterzellen mit Claims.
- **Fremde Strukturen beim Erstellen:** nur Gitterzellen des neuen Claims abfragen, kein `FindObjectsOfType`.
- Kein Per-Frame-Code auf dem Dedicated Server außer einem billigen Timer.

## 4. Datenmodell & Speicherung

- `Claim`: Id (GUID), Name (optional, Standard "&lt;Besitzer&gt;'s base"), OwnerSteamId, Mittelpunkt,
  Seitenlänge, **unbegrenzt nach oben und unten** (Säule), AnchorId, Mitglieder (SteamId + Rolle;
  Anzeigename nur als Cache), Erstellzeit.
- **Feste Rollen, nichts zu konfigurieren:**

| Rolle | Darf |
|---|---|
| Besitzer | alles, inkl. Freunde/Gäste verwalten und umbenennen |
| Freund | alles außer verwalten (bauen, abbauen, benutzen, Container, minen) |
| Gast | betreten, Türen/Schalter benutzen – nicht bauen, keine Container |
| alle anderen | nichts (werden beim Betreten gewarnt, der Besitzer wird informiert) |

- Admins (Host + `AdminSteamIds`) dürfen überall alles und sehen alle Claims mit allen Beteiligten.
- **Speichern zusammen mit dem Spielstand** (Hook auf das Speichern des Spiels), nicht sofort bei
  jeder Änderung → kein Auseinanderlaufen beim Laden alter Stände. Versioniertes Format.

## 5. Schutz (M3, `src/Game/Protection.cs`, `Core/MessageRules.cs`)

**Prinzip: Firewall für alles, was andere Spieler senden.** Jede Aktion eines Spielers kommt als
Netzwerk-Nachricht beim Server an. Vor der Verarbeitung (Prefix auf `Process(long)` **aller** Nachrichten-
Typen des Spiels, aktuell ~110) sammelt Verboten, worauf sich die Nachricht bezieht:
- alle `long`-Felder → Dinge (`Thing.Find`); übersprungen: Personen/Tiere und Dinge, die jemand trägt,
- `Vector3`-Felder mit "Position" im Namen und alle `Grid3`-Felder → Orte; zwei Orte (z. B. Kabel A→B)
  → auch der Weg dazwischen,
und prüft sie gegen die Claims – mit dem **verifizierten Absender** als Akteur.

| Benötigtes Recht | Nachrichten |
|---|---|
| keins (übersprungen) | Verbindung, Chat, Emotes, Pings, Sounds, Effekte |
| Benutzen (Gast) | `InteractionMessage`, `RequestInteractionToServer`, `RequestUseItemToServer` |
| Container (Freund) | Slots verschieben/tauschen, Stapel, Sortieren, Fabrikator-Aufträge; Fallenlassen (nur das Ding zählt, nicht der Ort) |
| Minen (Freund), 2 m Puffer | `MineAsteroidMessage`, `AsteroidVoxel(Paint)Message` |
| **Bauen (Freund) = Standard für alles andere** | Bauen, Werkzeuge, Abbauen, Logik, Umbenennen, Farbe, … und **jeder neue Nachrichtentyp** aus künftigen Spiel-Updates |

- Felder "wer hat gebaut" (`CreatorSteamId`, `OwnerClientId`) werden auf den echten Absender gesetzt.
- Nicht geprüft: Host (Admin), Admins mit bestätigter SteamID.
- Unbekannter Absender oder vom Spiel verzögerte Nachricht → im Claim **blockiert** (fail closed).
- Fehler in der Prüfung, während Claims existieren → blockiert.
- Abgelehnt → kurze private Chat-Meldung an den Spieler (max. alle 3 s).
- Tests prüfen gegen das installierte Spiel, dass jeder Name in der Regel-Tabelle existiert, und listen die
  Typen, die den Standard (Bauen) verwenden.

Bekannte Grenzen: Aktionen, die nicht als Nachricht ankommen (Physik-Effekte), werden nicht geprüft; Explosionen siehe 5a. Die Spiel-eigenen Schummel-Nachrichten (z. B. Strukturen ohne Bausatz) bleiben außerhalb von Claims möglich.

## 5a. Explosionen und Unbefugte (M4, `Damage.cs`, `Trespass.cs`)

- **Explosionen** (Entscheidung 2026-11-17): Wer eine Explosion ausgelöst hat, verrät das Spiel nicht,
  wohl aber, *was* explodiert: `Explosion.Explode` wird direkt vom explodierenden Objekt aufgerufen
  (Klasse aus dem Aufruf-Stack, Coroutinen entpackt).
  - **Fest gebautes Gerät** (`Structure`: Ofen, Stirling-Motor, Schaltkreis-Gehäuse …) explodiert **im
    Claim** → Schaden in *diesem* Claim wie im normalen Spiel (Unfälle bleiben Teil des Spiels).
  - **Alles andere** (Granaten, Dynamit, Sprengladungen, tragbare Kanister, Anzüge, Tablets, Raketen,
    Geräte außerhalb des Claims, unbekannte Quelle) → richtet im Claim **keinen** Schaden an, egal von wem.
  - Personen, Tiere und Getragenes werden immer normal getroffen; außerhalb von Claims ändert sich nichts.
  - Rohr-/Tank-Bersten laufen nicht über Explosionen und bleiben normal.
  - Test prüft gegen das Spiel, welche Explosionsquellen Geräte (`Structure`) sind.
- **Unbefugte** (weder Besitzer, Freund noch Gast) in einem Claim: einmalige private Warnung beim Betreten,
  der Besitzer wird informiert (höchstens alle 60 s pro Spieler und Claim). **Kein Kick** (Entscheidung
  2026-11-16) – anfassen können sie ohnehin nichts. Prüfung 1× pro Sekunde; Host und bestätigte Admins ausgenommen.

## 6. Spielmechanik-Verbesserungen gegenüber dem Vorbild

- Claims ohne Höhengrenze (kein Bauen/Minen über oder unter fremden Basen).
- Fremde Explosionen beschädigen in Claims nichts; Unfälle eigener Geräte im Claim bleiben möglich.
- `/claim show`: Grenzanzeige ohne Mod beim Spieler – schwebende Funken (Spiel-Effekt) entlang der Grenzen
  aller Claims im Umkreis von 60 m, 10 s lang, auf Höhe des Spielers, nur für ihn sichtbar (`BorderDisplay.cs`).
  Farbe nicht änderbar (orange); grüne Fackeln wurden verworfen.
- Unbefugte werden nur gewarnt, nicht gekickt (anfassen können sie ohnehin nichts).
- Exakter Spielername bei `add` (kein Teilstring-Treffer), Auswahl bei Mehrdeutigkeit.
- Texte über Lokalisierungsdatei (EN/DE).

## 6a. Claim-Anker (Bedienung ohne Befehle)

Ziel: so einfach wie möglich. Ein Claim hängt an einem **vorhandenen Bauteil des Spiels** – kein
eigenes Objekt, damit Clients weiterhin **keinen** Mod brauchen.

- **Standard: Autolathe** (`Assets.Scripts.Objects.Electrical.Autolathe`). Baut jeder, ist das Herzstück
  der Basis, wird selten neu platziert.
- **Optional / Ersatz: Flagge** (`Assets.Scripts.Objects.Flag`).
- Config `ClaimAnchor = Autolathe | Flag | None` (None = nur `/claim create`).

| Spieler-Aktion | Wirkung (serverseitig) |
|---|---|
| Anker außerhalb aller Claims, Spieler hat noch nicht die Höchstzahl an Claims | Claim um den Anker, Besitzer = verifizierter Erbauer (M1), Größe aus Config |
| Anker innerhalb eines Claims (auch als Freund) / Limit erreicht | normale Nutzung, kein neuer Claim |
| Claim dort nicht erlaubt (Überlappung, Spawn-Abstand, fremde Strukturen, Limit) | **Anker wird trotzdem gebaut** (Autolathe darf nie blockiert werden), kein Claim, Grund wird gemeldet |
| Anker abgebaut / zerstört | Claim wird **sofort** entfernt (keine Schonfrist). Abbauen darf nur, wer `build` im Claim hat |
| Claim umbenennen | nur per `/claim name <Name>` (Standardname: "&lt;Spielername&gt;'s base") |
| Freund / Gast hinzufügen | `/claim add <Spieler>` bzw. `/claim add <Spieler> guest` |

Befehle (bewusst wenige):

| Wer | Befehl |
|---|---|
| Spieler | `/claim add <Spieler> [guest]`, `/claim remove <Spieler>`, `/claim name <Name>`, `/claim info` |
| Admin | zusätzlich `/claim list` (alle Claims mit Besitzer, Freunden, Gästen), `/claim info <Claim oder Spieler>`, `/claim delete [Claim oder Spieler]`, `/claim create [Name]` |

Einstellungen (bewusst wenige): `ClaimSize`, `MaxClaimsPerPlayer`, `Anchor`, `SpawnProtectionDistance`, `AdminSteamIds`.

Namen werden nur per `/claim name` geändert (kein Labeller, kein Eingabefenster).

Umsetzung:
- Claim speichert die ReferenceId des Ankers (`AnchorId`). Beim Laden: Anker fehlt → Claim wird entfernt und geloggt.
- Hooks: Server-Construct (Anker entsteht), Anker zerstört/abgebaut.
- `/claim create` bleibt als Alternative (z. B. für Admins).
- Abhängig von M1 (sicherer Erbauer); Umsetzung zusammen mit M3.

## 6b. Meldungen an Spieler

Chat-Meldungen sind nur kurz sichtbar. Für wichtige Erklärungen nutzt der Server die **Server-Ankündigung**
des Spiels (`AnnounceMessage`): ein Fenster mit OK-Knopf, das bleibt, bis der Spieler es schließt.
Funktioniert auch bei Clients ohne Mod (Host lokal: `AnnounceMessage.Display`).

Sparsam einsetzen, weil das Fenster das Spiel unterbricht:
- **Claim erstellt** (einmalig): was geschützt ist, Größe, wie man Freunde hinzufügt (`/claim add`),
  dass Abbauen der Autolathe den Claim löscht, Verweis auf `/claim help`.
- **Kein Claim möglich** (beim ersten Versuch): Grund und was zu tun ist.
- **Claim entfernt** (Anker abgebaut/zerstört): Hinweis an den Besitzer.
- Alles andere (verweigerte Aktionen usw.) bleibt kurz im Chat, gedrosselt.
- Kein Merken nötig: die Fenster kommen nur bei seltenen Ereignissen (Claim erstellt, abgelehnt, entfernt).

## 7. Meilensteine

| # | Inhalt | Ergebnis |
|---|---|---|
| M0 | Projektgerüst, Build, Plugin lädt, Logging, Config | DLL lädt im Spiel |
| M1 | SenderContext, Beitritts-Prüfung, /claim für alle, IP-Warnung ✔ (im Spiel ungetestet) | Sicherheitsfundament |
| M2 | ClaimIndex, Speicherung im Spielstand, Befehle, feste Rollen ✔ | Claims anlegen/verwalten |
| M3 | Firewall-Schutz (Bauen, Abbauen, Benutzen, Container, Minen), Autolathe-/Flaggen-Anker, Erklärungsfenster ✔ (im Spiel ungetestet) | spielbare Server-Version |
| M4 | Explosionsschutz (fremde Explosionen), Warnung für Unbefugte ✔ (im Spiel ungetestet) | Feature-Parität |
| M5 | Grenzanzeige per Funken (`/claim show`) ✔ (im Spiel ungetestet); weiterer Client-Komfort entfällt (nur Server-Mod) | Komfort |
| M6 | Tests Dedicated Server, Last-Test, Release | Workshop-Release |

## 8. Testplan (Kurzfassung)

- Host + 2. Client (mit und ohne Mod), Dedicated Server (RakNet) und Listen-Server (Steam P2P).
- Angriffs-Tests mit einem absichtlich manipulierten Test-Client: gefälschte HumanId/SourceId,
  gefälschte Sync-/Teleport-Nachrichten → müssen verworfen werden.
- Performance: 200 Claims, 10 Spieler, Profiler-Vergleich mit/ohne Mod.

## 9. Update-Sicherheit

- **Alle Eingriffe ins Spiel an einer Stelle** (`src/Game/Hooks.cs`): jeder Hook wird einzeln gesucht und
  installiert. Fehlt einer nach einem Spiel-Update, fällt nur diese Funktion aus; der Rest läuft weiter.
- **Selbsttest beim Start** im Log: "Verboten x.y: N of M hooks active", fehlende Hooks mit Grund.
  Spielversion wird mit der getesteten verglichen (`Hooks.TestedGameVersion`).
- **Warnung an den Host** beim Weltstart (Konsole + Fenster), wenn ein Hook fehlt oder die Claims-Datei
  nicht lesbar war. Admin-Befehl `/claim status` zeigt denselben Bericht.
- **Positionale Parameter** (`__0`, `__1`) in allen Patches: umbenannte Parameter im Spiel brechen nichts.
- **Spiel-API-Test** (`tests/.../GameApiTests.cs`): prüft gegen das installierte Spiel, ob alle Ziel-Methoden
  mit den erwarteten Parametern existieren. Nach jedem Spiel-Update `dotnet test` ausführen.
- **Speicherdaten nie überschreiben, die nicht gelesen werden konnten**: defekte Datei wird als
  `.broken-<Zeit>` gesichert.
- Begleitdateien liegen im Unterordner `verboten/` neben den Spielständen, damit die Autosave-Rotation des
  Spiels (zählt alle Dateien im Ordner) nicht vorzeitig Spielstände löscht.

### 0.15 Test-Runde 1 (2026-11-22): Änderungen
- **Fläche statt Meter:** Neue Claims 10 x 10 m plus `StartFreeArea` = 300 m² gratis; verdient werden
  `EarnedAreaPerHour` = 25 m² je Stunde, in der Besitzer oder Freund online sind (minütlich gutgeschrieben).
  Wachsen kostet Seitenlänge x Meter; erst Freifläche, dann erspielte Fläche, dann Stahl (Preis je m² steigt mit der Fläche).
- **Wachsen nur nach Blickrichtung:** `/claim grow <Meter>` schiebt die Seite hinaus, auf die der Spieler schaut
  (keine Himmelsrichtungen mehr); Admins verkleinern fremde Claims mit `/claim shrink <Meter>` ebenso.
  Admins zahlen am eigenen Claim wie alle; kostenlos nur an fremden Claims.
- **Grenzmarkierung:** Einstellung `[General] BorderMarkers` = Flares (Standard) | Sparks. Fackeln je 30 s:
  grau an den Ecken des eigenen Claims (gedämpftes Weiß), khaki bei fremden, 12 um die Landezone
  (4 gelbe an den Himmelsrichtungen, 8 rote dazwischen). Fackeln sind eingefroren (rollen nicht) und
  lassen sich nicht aufheben (`CanPickup = false`, die Sperre des Spiels); falls doch, verschwinden sie sofort. Jede Stelle hat höchstens eine Fackel; ruft ein anderer Spieler
  `/claim show` auf, wird sie in seiner Sicht ersetzt und brennt erneut 30 s. Annähern an fremde Claims: weiter Funken.
  Funken: gleichmäßig unter dem Teilchen-Limit des Spiels, 1,5 m über Boden, 4 Schwünge/s, 30 s.
- **Fenster:** ✔/✘ (fehlen in der Schrift) durch Wörter ersetzt; Hilfe auf `/claim help` und `/claim help2`
  verteilt, gleichmäßige Zeilenabstände (U+2028 innerhalb eines Abschnitts).
- Das Log nennt beim ersten Fackel-Einsatz die Lichtfarben der Paletten-Einträge (zur Farbwahl).

### 0.16 Felder, Ansprüche, Spenden, Konto, Strafe (2026-11-23)
- **Felder:** Mit `Growing = true` und Autolathe als Anker besteht ein Claim aus Feldern von 10 x 10 m auf einem
  Raster, dessen Feld (0/0) um die erste Autolathe liegt (`GridX/GridZ`, bleibt bei Umzug der Lathe). `/claim grow`
  (ohne Zahl) setzt in Blickrichtung (`Entity.EntityForward`, nicht der Körper) das erste freie Feld vor dem Feld, in dem
  man steht (oder dem nächsten Feld des Claims). Jede Form, solange alles zusammenhängt. Admins: `/claim shrink`
  nimmt das äußerste Feld in Blickrichtung weg (nicht das der Lathe, nichts darf zerfallen). Claims ohne Felder
  (Flaggen, `Growing = false`) bleiben Rechtecke; alte Rechteck-Claims werden beim ersten grow zu Feldern.
- **Ansprüche:** `[Claims] ClaimSlotsPerPlayer` = 3. Ein Claim braucht je angefangene 50 m in jede Richtung (Rechteck
  außen herum) einen Anspruch. Eigene Claims dürfen aneinandergrenzen (nicht überlappen), zu fremden 10 m.
- **Spenden:** `/claim donate <n>` (nur eingetragene Freunde, im Claim stehend; 0 = zurück). Austragen, Gast machen,
  Verfall → Spende zurück. Wer alle Ansprüche gespendet hat und selbst gründet, bekommt alle zurück.
- **Rechnung (`Claim.SettleSlots`):** Spende deckt zuerst per Strafe gekaufte Ansprüche, dann ersetzt sie eigene
  (werden frei); Fehlendes kommt aus freien eigenen, der Rest ist `MissingSlots`. Wachsen nur, wenn eigene + gespendete
  reichen (gekaufte zählen nicht).
- **Konto pro Spieler:** einmal `StartFreeArea` (300 m²), dazu `EarnedAreaPerHour` (25 m²) online, nur solange man
  einen Claim besitzt oder spendet. Ein Feld kostet 100 m²: Konto des Besitzers, dann der Spender, dann Stahl.
- **Strafe:** fehlt ein Anspruch und der Besitzer ist online: Fenster, 3 h Serverzeit. `/claim pay` = bebaute Fläche
  (2 x 2 m-Zellen mit Gebäuden) außerhalb von 50 x 50 m um die Lathe x Stahlpreis x 5 → bleibt, Anspruch gilt als
  gekauft. Sonst (oder sofort, wenn dort nichts steht) schrumpft er auf die Felder in 5 x 5 um die Lathe; verlorene
  Fläche innerhalb der eigenen Ansprüche kommt aufs Konto.
- **Anzeige:** Fackeln (1,5 m über Boden, auf dem Kopf) an den Außenecken; aneinandergrenzende Claims eines Besitzers
  gelten als eine Fläche (L = 6 Fackeln). Funken nur an der Außenkante.
- **Hilfe:** `/claim help [1–4]` (help2–4 gehen auch): 1 Grundstück/Regeln, 2 Größe mit Lageplan (TextMeshPro
  `<mark>` + `<mspace>`, Norden oben, Lathe gelb, • = Spieler), 3 Ansprüche/Spenden/Strafe/Verfall, 4 Befehle.

### 0.17 Prüfrunde zu 0.16 (2026-11-23): behobene Logikfehler
- **Blickrichtung:** `Entity.EntityForward` – beim Host `characterRotationY` (Kamera-Yaw), bei Mitspielern die vom
  Client gesendete Drehung. `transform.forward` des Körpers drehte sich beim Host nicht mit.
- **Raster:** Ein neuer Feld-Claim übernimmt das Raster des nächsten eigenen Feld-Claims (`AlignToOwnClaims`) –
  sonst lägen Felder um Bruchteile versetzt und eigene Claims könnten nie lückenlos aneinanderstoßen.
- **Herrenlose Claims weichen** jetzt den echten Feldern eines aktiven Claims, nicht dem Rechteck um ihn (ein L
  schnitt sonst auch neben seiner leeren Ecke weg). Zwischen Claims desselben Besitzers gilt Abstand 0
  (`ClaimGeometry.Apart` / `GapBetween`) – auch beim Zurückschneiden, Wiederbeleben und Übernehmen.
- **Strafe:** zählt nur, solange der Besitzer online ist (`PenaltyLeft` in Minuten). Preis und Schrumpfen nutzen
  denselben Rest (`PenaltyRest`: 5 x 5 Felder um die Lathe, nur was mit ihr zusammenhängt) – Gebäude in abgeschnittenen
  Feldern zählen mit. Ohne Gebäude draußen schrumpft der Claim sofort, auch bei `/claim pay` (kein Gratis-Behalten);
  ein Stahlpreis von 0 lässt ihn nicht mehr fälschlich schrumpfen.
- **Ansprüche neu verrechnet:** jede Minute für Claims mit Fehlbetrag (frei gewordene Ansprüche durch Löschen, Ende,
  zurückgenommene Spenden); zurückgenommene Spenden rechnen auch beim Spender neu. Wer alle Ansprüche gespendet hat und
  gründet, bekommt sie nur zurück, wenn er keinen eigenen Claim hat.
- **Wiederbeleben:** ein herrenloser Claim wird nur wieder geschützt, wenn ein Anspruch frei ist (Hinweis einmal beim
  Einloggen); gekaufte Ansprüche bleiben, Spenden waren mit dem Verfall beendet.
- **Admins** können fremde Claims nicht über deren Ansprüche hinaus wachsen lassen (keine Überraschungsstrafe);
  Flaggen-Claims lassen sich wie bisher gar nicht per Befehl ändern. `/claim grow` nur auf dem Claim oder in seiner
  10-m-Zone (das nächste Feld hängt vom Standort ab).
- **Alte Rechteck-Claims** werden nur aus Feldern gebildet, die ganz in ihnen liegen (nie größer → keine neuen
  Konflikte). Claims mit Feldern folgen nie der Basis (`Growing = false`).
- **Index:** der Feld-Zwischenspeicher wird beiseite gebaut und am Stück getauscht (Index wird von mehreren Stellen
  gelesen). Weit entfernte Claims werden über ihr Rechteck abgekürzt.
- **Anzeige:** höchstens 32 Fackeln pro Fläche; Lageplan mit geschützten Leerzeichen und deckenden Farben.
