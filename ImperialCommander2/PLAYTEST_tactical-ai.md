# Playtest-Paket: Faire Imperiale KI (feature/tactical-ai)

Stand: FINAL-QA 26.10.2026 — Assembly-CSharp (249 Dateien inkl. KI) kompiliert fehlerfrei
(geprüft mit der Mono-Version von Unity 2020.3.48, inkl. aller Paket-Referenzen).
Branch: `feature/tactical-ai` (Fork wit19/ImperialCommander2,_commits bis `e39c0ae`_).

## 1. Was ist drin?

Die App bekommt einen optional, **pro Saga-Kampagne einschaltbaren** Taktik-Modus
(`SetupOptions.aiMode`, Default: leer = Vanilla). Die alte Zufalls-Aktivierungsreihenfolge
bleibt vollständig erhalten. Die KI ist **fair konstruiert**: Sie benutzt ausschließlich
Informationen, die auch ein am Brett sitzender imperialer Spieler aus dem Sichtfeld des
Hauptquartiers hat — Brettgeometrie, Reichweiten-Tabellen der Karten, gemerkte Letztsicht,
manuell eingetragener Heldenschaden (HP-Tracker), keine Sichtlinien, kein Würfel-Zugriff.

Module: Aktivierungs-Inferenz aus dem Order-Deck (M2a), Skill-Kurven S1–S5 (M2b),
Heldendrohprofil aus gedruckten Kartenwerten (M3), Fokus-/Zieldoktrinen,
RiskPosture „Ansonsten" (v3.9 §3.5.1), Doktrinen-Axiome (v3.9.1 §4.5).

## 2. Playtest-Setup

> **Version prüfen:** Für diese Testphase ist die Versionsnummer fest auf
> **`v.2.2.16 dev N`** gesetzt (die Nummer ändert sich nicht; nur das
> `dev`-Zählwerk steigt mit jedem Auslieferungsbuild). Titelbildschirm und
> Start-Log müssen dasselbe `dev N` zeigen wie die Release-Ansage — sonst
> läuft eine veraltete App.

1. Kampagne wie immer konfigurieren (Helden, Ausrüstung, Fähigkeiten — alles wie gehabt).
   Die KI braucht **keine** Änderungen an der Kampagnenverwaltung.
2. Die KI-Optionen sitzen im normalen **Optionen-Fenster → Reiter „UI"** ganz unten
   (unterhalb der bestehenden Optionen, mitscrollbar; Playtest-Feedback 27.10:
   vorher schwamm das Panel über dem Dialog). Die Buttons schalten
   (jeder klickt sich zyklisch durch seine Werte):
   - *TACTICAL AI* — Master: **off** (Default = App verhält sich exakt wie vorher) / **on**
     (= alles Taktische an; die Missionen folgen automatisch dem AI MODE, außer man
     stellt einzelne Missionen bewusst um, siehe 3.).
   - *AI MODE* — `legacy` (Vanilla-Zufall) → `reference` (Inferenz-Aktivierung +
     taktische Zielauswahl) → `overlord` (zusätzlich harte Doktrinen: Rückzugs-/Fokusregeln).
     Das ist der **global Default** —missionsweit wirksam, solange keine Mission
     per Badge übersteuert.
   - *SKILL* — S1…S5 (Kompetenzkurve der Truppen).
   - *DEPLOYMENT* — `secret` / `open` (verrät die Aufstellung dem Blick aufs Brett).
   - *EXPLAIN* — `none` → `post_hoc` → `full` (Erklärtexte unter den Entscheidungen;
     **für sichtbare „Warum jetzt"-Zeilen auf den Anweisungskarten ist `full` nötig**).
3. **Missions-Badges (seit dev 2 mit AUTO):** Sowohl jede Missionskarte der
   Kampagnenliste (oben links) als auch das EINSATZKARTE-Setup-Panel (unter dem
   Schwierigkeits-Button) tragen einen teal-farbenen Badge, der nur bei Master ON
   sichtbar ist. Klick-Zyklus: **AI:AUTO → AI:REF → AI:ORD → AI:OFF → AI:AUTO**.
   * `AI:AUTO` (Default): die Mission folgt dem globalen *AI MODE*. Master AN +
     `reference` genügt also — **keine Mission braucht eine manuelle Umstellung**,
     um die taktische KI zu aktivieren.
   * `AI:REF` / `AI:ORD`: bewusste Override pro Mission, unabhängig vom Globalwert.
   * `AI:OFF`: diese Mission läuft immer in Legacy (z. B. für Vergleichsrunden).
   Alte Kampagnen-Speicherstände mit dem 3-Zustands-Default „legacy" werden beim
   Laden automatisch auf AUTO migriert (dev 2). Klick-Erfolg im Log:
   `SagaSetup::AI badge -> reference`. Der globale Schalter ist pro Spiel
   speicherfest (Konzept §4.2.7); ein laufende Mission wechselt den Modus nicht
   mehr (er wird beim Missionsstart einmal eingelesen).
4. Normal spielen. Die KI-Entscheidungen laufen über die bestehenden App-Dialoge
   (Aktivierungsfenster, Zielauswahl-Popup) — nichts Neues zu lernen.
   **So liest du eine Anweisung (Schema nach Thomas' Playtest-Runde 6):**
   Jede Aktion ist eine Wenn-Dann-Entscheidung, nummeriert wie die zwei Aktionen
   einer Aktivierung. Beispiel Angriffs-Karte:
   **Regel-Festhalt: pro Aktivierung darf eine Imperiale Figur genau EINEN Angriff
   ausführen — außer ihre eigene Imperielle-Karte druckt ausdrücklich mehrere
   Angriffe. Beide Aktionsblöcke tragen deshalb dieselbe Wenn-Dann-Prüfung; der
   zweite Block prüft zusätzlich, ob schon geschossen wurde.** Beispiel
   Angriffs-Karte (2 Angriffsblöcke):
   ```
   Fokusziel: Held X
   Aktion 1: Noch nicht angegriffen? Dann: X, wenn Reichweite und freie
             Sichtlinie. Keine Sichtlinie / zu weit: 4 Felder auf X zubewegen.
             Sichtlinie durch eine ANDERE Heldenfigur blockiert: diese angreifen.
   Aktion 2: Prüfung – schon angegriffen? Dann KEIN zweiter Schuss (außer deine
             Karte erlaubt es ausdrücklich) — dieser Block ist Bewegung: X noch in
             Reichweite? Deckung halten. Sonst bis 4 Felder von X entfernen und im
             Auge behalten. Noch nicht angegriffen? Dann gilt dieser Block wie
             Aktion 1.
   ```
   Fokusziel: Held X
   Aktion 1: Dein einziger Angriff dieser Aktivierung: X, wenn Reichweite und
             freie Sichtlinie. Keine Sichtlinie / zu weit: 4 Felder auf X zubewegen.
             Sichtlinie durch eine ANDERE Heldenfigur blockiert: diese angreifen.
   Aktion 2 (zweiter Angriffsblock => Bewegung, denn pro Aktivierung darf nur
   EINMAL angegriffen werden): X noch in Reichweite? Deckung halten.
             Sonst bis 4 Felder von X entfernen und ihn im Auge behalten.
   ```
   ```
   1. Das **Fokus-Ziel X** wird **positionsunabhängig** von der App berechnet —
      ein Köder, den man neben einen Imperialen stellt, zieht es nicht ab.
      Reihenfolge: **verwundet zuerst** (euer Hand-Schadenszähler am Brett ist
      gemeinsame Information — wer verwundet ist, wird erledigt), dann die
      **größte Feuerkraft** (Angriffswürfel, dann Lebenspunkte), bei Gleichstand
      der erste lebende Held von links in eurer Heldenreihe. Kein Zufall, kein
      Wurf. (Feiner noch: exakte Ausrüstungs-Bedrohung aus euren Campaign-Items
      — M5-Ausbau, Technik steht schon.) Die Regel „gedrucktes Vorzugziel" gibt
      es NICHT mehr — echte Imperiellen-Karten drucken kein Vorzugsziel.
   2. **Der Blocker-Fall ist eingebaut:** verbaut eine andere Heldenfigur die
      Sichtlinie auf X, ist sie das Ziel — nicht X, und auch kein unnützes
      Further-Running an ihr vorbei.
   3. **Bewegungskarten (Hit&Run, Thomas' Runde 7):** Steht schon ein Held in
      eurer eigenen Angriffreichweite: **stehen bleiben, Feuer vorbereiten**
      (Sonst könnte ein einziger schneller Meuchler unser Fernkampf-Korps per
      Spazierenfahren durch die halbe Karte pinnten). Kein Held in Reichweite:
      war der Angriff schon dran → **max. Speed Felder vom Ziel entfernen und
      es im Auge behalten**; noch kein Angriff → dem Ziel nähere. Reine
      Bewegungskarten ohne je erreichbares Ziel: nächste Heldenfigur.
      Aktionspunkte gibt es in IC2 nicht — „übrig" heißt: der Bewegungsblock
      der Karte wurde noch nicht als Angriff verplant.
   4. Warum ein Mensch so spielen würde: Zielfeuer auf Verwundete und die
      gefährlichsten Helden, Zugriff notfalls ueber Bewegung, Blocker abschießen —
      Standardtaktik. Standort-Köder ändern nichts an der Rangliste, sie ändern
      nur, wer gerade zufällig schießbar ist.
   **Doktrin-Kopfzeile und die sechs Vorschriften (dev 11):** Jede Anweisung beginnt
   mit `Doktrin: <Name> — nach dieser Vorschrift spielst du diese Aktivierung`.
   Der Kern kennt sechs Vorschriften; aktuell spielt IMMER alle Gruppen
   „Vorstoß" (Rollen-Vergabe je Mission ist der nächste Ausbildungsschritt M4),
   aber die Wenn-Dann-Texte aller sechs sind bereits einsatzfertig:
   - **Vorstoß** — das Schema oben (Angriff/Fokus, Zugriff über Bewegung, Blocker
     abschießen, Hit&Run mit Standfeuer-Guard).
   - **Wachschutz** — Bewegung endet immer in der Wachzone (Missionsziel + Umkreis);
     Angriff nur auf Helden IN der Zone; sonst kein Schuss, nie herauslocken lassen.
   - **Blockierer** — Bewegung auf das Feld zwischen Missionsziel und nächstem
     Helden (seine Route muss über euer Feld); Angriff: nächster Schussbarer.
   - **Jäger** — wie Vorstoß; das Vorrücken auf das Fokus-Ziel IST das Abkämmen.
   - **Support** — Distanz halten: nach Angriff zurück, vor Angriff nur vorrücken,
     wenn kein Held in Nahkampfreichweite endet; Feuer aus der Deckung.
   - **Hinterhalt** — Bewegung nur auf unsehbare Felder, erster Schuss aus dem
     Versteck auf den ersten Helden in Reichweite; danach nicht weiter enttarnen.

   Das Anweisungs-Popup (explain=full) zeigt genau dieses Schema, z. B.:
   `Aktion 1 – Angriff auf Drokkatta, wenn Reichweite und freie Sichtlinie. Keine Sichtlinie / zu weit: 4 Felder auf Drokkatta zubewegen. Sichtlinie durch eine ANDERE Heldenfigur blockiert: diese blockierende Heldenfigur angreifen` ·
   `Warum jetzt: Stärke-Effizienz 6.50 (Rang 1) |
   Fokus-Ziel: Drokkatta — er ist verwundet – zuerst erledigen (Wunden-Tracker am Brett); größte Feuerkraft unter den übrigen Helden (Angriffswürfel, dann Lebenspunkte); kein Zufall`
5. Fürs Protokoll: **EXPLAIN** auf `full` — nur dann erscheinen die Why-Texte
   (z. B. `Fokus-Ziel: …`, `Stärke-Effizienz …`)
   direkt auf den Anweisungs-Popups.
   letzten Runde zu Rundenbeginn. Screenshots davon sind das wichtigste Playtest-Material.
6. Alle Entscheidungen werden zusätzlich in die **Unity-Entwickler-Konsole** geloggt
   (`TacticalAIBridge::…`, deterministisch, kein Zufall). Beim Build über den Editor:
   Konsole offen lassen und am Ende als Textdatei sichern (Rechtsklick → Save).
   Für Android-Builds: Logcat-Export (`adb logcat > playtest.log`) beim Mitspielen.

## 3. Worauf wir beim Spielen achten

- **Aktivierungsreihenfolge:** Wirkt sie „vernünftig" (Gefährliches/Gesichtetes früher),
  ohne vorhersehbar-mechanisch zu sein?
- **Zielauswahl:** Schießt die KI nie bewusst auf einen bereits verwundeten Helden,
  wenn ein frischer gleichwertig gefährlich ist? (Fairness-Axiom)
- **Rückzug/Positionierung:** Verwundete Truppen sollten sich sichtbar zurückziehen
  („Ansonsten"-Ast), gesunde vorrücken. Kein „Body-Blocking" von Türen.
- **Kompetenz ohne Gemeinheit:** Die KI darf den Helden ruhig schlagen — aber nur mit
  Informationen vom Brett, nicht durch „Gedächtnis-Tricks". Wenn sich eine Entscheidung
  „unfair/berserkerhaft" anfühlt → Screenshot + Rundennummer notieren.
- **Vanilla-Regression:** 1–2 Runden im Modus *Vanilla* gegenspielen; die App muss sich
  exakt wie vor dem Feature verhalten.

## 4. Windows-Version bauen (Compile-Anleitung)

Die App ist ein Unity-Projekt (Unity **2020.3.48f1**, URP; der Hub erzwingt
mittlerweile den Sicherheits-Patch **2020.3.49f1** — der ist innerhalb derselben
LTS-Serie unproblematisch, einfach annehmen). Windows-Build, verifiziert auf
einem Windows-Laptop am 27.10.2026:

1. **Unity Hub** installieren: <https://unity.com/download> (kostenlos, Personal-Lizenz).
2. Im Hub → *Instanzen* → *Installieren* → **Unity 2020.3.48f1** (bzw. neuester
   2020.3.4x-Patch, steht in `ProjectSettings/ProjectVersion.txt`).
   Beim Modul-Dialog anhaken: **Windows Build Support (Mono)** und **(IL2CPP)**.
   *Hinweis:* Ein Modul „Windows SDK & Compiler" existiert im 2020.3-Hub
   **nicht** (2017er-Name) — der Mono-Build braucht kein SDK und kein Visual
   Studio. Nur IL2CPP-Anwendungen würden ein Visual Studio 2019 mit
   C++-Arbeitsauslastung verlangen. Language Packs/VS Community abwählbar.
3. Repository holen: `git clone https://github.com/wit19/ImperialCommander2`,
   `git checkout feature/tactical-ai`, und im Hub das Verzeichnis
   `ImperialCommander2/ImperialCommander2` (die Ebene mit `Assets/`) öffnen.
4. Package-Restore/First Import abwarten (5–15 min — Addressables und URP
   werden aus `Packages/packages-lock.json` geladen). Beim Erstimport sind
   folgende Meldungen **harmlos** (Originalspiel-Altlasten/Import-Reihenfolge):
   vereinzelte „Broken text PPtr"- und verwaiste `.meta`-Meldungen,
   „FSBTool … truncated" bei drei MP3-Sounds, TMP-Shader-Fehler
   „Distance Field SSD / TMPro_Properties.cginc" (verschwindet nach
   Editor-Neustart, betrifft nur eine Text-Schatten-Sondervariante).
   **Rote `error CS…`-Zeilen** sind es dagegen nicht — die bitte melden.
5. **Addressables einmalig bauen** (sonst fehlen Karten-/Figuren-Assets im
   Player): Menü **Window → Asset Management → Addressables → Groups**
   (Adresse für Addressables 1.19.19; nicht unter *Panels*!), dort
   **Build → New Build → Default Build Script**, 2–10 min bis `Build succeeded`.
   Falls „Asset Management" im Window-Menü fehlt: der Editor blendet die
   Menüpunkte aus, solange Skriptfehler vorliegen — erst Fehler beheben
   (`git pull`), Editor neu starten, dann erscheint der Punkt.
6. `File → Build Settings` → Platform **PC, Mac & Linux Standalone**
   (ggf. *Switch Platform*), Target Platform **Windows**, Architecture **x86_64**.
   Szenenliste unverändert lassen. **Build** → Ordner wählen →
   `Imperial Commander 2.exe` starten. Mono ist der Standard; IL2CPP hier nicht testen.

### Kann man die Windows-Version auf Linux cross-compilieren?

Prinzipiell ja — der Linux-Unity-Editor kann mit installiertem *Windows Build Support*-
Modul Windows-Player bauen. In der Praxis hier **nicht genutzt**, weil: (a) dafür trotzdem
der komplette Unity-Editor + Build-Support-Modul + Windows-SDK installiert sein müssen
(~10 GB, Lizenz-Login nötig), (b) der Addressables-Content-Build (AssetBundle-Inhalte,
Maps/JSON) dann auf diesem Rechner ebenso durchlaufen muss und (c) wir das Ergebnis ohne
Windows-Testinstallation nicht verifizieren können. Der einfache Weg bleibt daher der
Build auf einem Windows-PC (oder einer Windows-VM mit Ordner-Freigabe).
Was auf diesem Rechner **ohne** Unity bereits verifiziert ist: der komplette C#-Code
kompiliert fehlerfrei gegen die Unity-2020.3-Mono-Referenzen (Harness:
`compile_check/build.sh`, rc=0, 249 Dateien) — Build-Fehler durch unser KI-Feature sind
damit ausgeschlossen.

## 4b. dev 15 — MedPacks, Modus-Auswahl, Optik

- **MedPacks im Tactical-Modus (REF/ORD) abgeschaltet.** Nach den Referenz-
  Regeln bleibt ein verwundeter Held bis Missionsende verwundet — der
  MedPac-Knopf im Spiel-Screen, das Zähler-Popup und MedPac-Belohnungen aus
  Kisten entfallen in taktischen Missionen komplett (Legacy-Missionen unverändert).
  Im Log prüfbar: `TACTICAL mode - MedPac UI off` bzw. `MedPac reward SKIPPED`.
- **LEGACY ist im globalen Modus-Auswahl nicht mehr wählbar** (war identisch
  mit „Tactical AI aus"). Der Modus-Zyklus ist jetzt REFERENCE ⇄ OVERLORD; ein
  gespeicherter LEGACY-Wert wird beim Start automatisch auf REFERENCE migriert.
  Das missionsweise „Aus" am Badge pro Mission bleibt wie gehabt möglich.
- **Einstellungen-Optik:** Die Tactical-AI-Zeilen nutzen jetzt dieselben
  Bausteine wie die Rundenlimit-Gruppe (gleiche Schrift, gleiche Panel-
  Grafik): Überschrift „Taktische KI", Panel mit Schalter-Zeile, Panel mit
  MODUS/STUFE/AUFSTELLUNG/ERKLÄRUNG.


- **OVERLORD zeigt jetzt sichtbar**, wenn es wie REFERENCE plant: der
  Brett-Adapter (Weltmodell, M5) existiert noch nicht, deshalb fällt
  Overlord-Planung automatisch auf den Referenz-Regelsatz zurück. Das war
  bisher nur im Log sichtbar und hat im Playtest zu Verwirrung geführt
  (27.10). Jetzt kommt einmal pro Mission ein Hinweistext im Bildschirm:
  „Taktische KI – Overlord: Brett-Adapter noch nicht verfügbar. Planung
  erfolgt nach den Referenz-Regeln." Wenn der M5-Adapter registriert ist,
  erscheint der Hinweis nicht mehr und Overlord plant wirklich anders.


## 5. Bekannte Grenzen (bewusst so entschieden)

- Die KI sieht keine Sichtlinien — Angriffe „durch Wände" verhindert sie über
  Wahrscheinlichkeitszonen (Sichtlinien-Bereich v3.9 §3.5), nicht über echtes LoS.
- Heldenschaden muss wie gehabt manuell in die Figuren-Tracker eingetragen werden;
  die KI nutzt die Deltas (Schadensherd → Angreifer-Vermutung) und Heldenschaden
  als Threat-Gewicht (HeroThreatProfile).
- Der in-App-Fokus nutzt aktuell **verwundet zuerst → größte Feuerkraft → Aufstellung
  von links** (bei jeder Aktivierung neu berechnet, positionsunabhängig); die volle
  ThreatScore-/Doktrinen-Rechnung über Weltmodell (HeroThreatProfile aus euren
  Campaign-Items) greift erst mit dem Hex-Adapter (M5). Die EXPLAIN-Texte zeigen
  durchgehend, was die KI wirklich nutzt.
- **Nicht dieselbe Gegner-Karte zweimal als eigene Gruppe aufstellen** (z. B. zwei
  eigenständige Sturmtruppen-Gruppen mit identischer Karte): Planner-Status ist
  pro Karten-ID geführt, die zweite Instanz zieht eine zweite Ordnerkarte (bekannt,
  M5-Instanz-Keying). Gruppengröße 2+3 derselben Karte ist wie immer okay.
- Master-Schalter **nicht mitten in einer laufenden Mission umschalten** — geplant
  wird ohnehin nur rundenweise, aber ein Umschalten ist kein sauberer Moduswechsel.
- Die angezeigte Ordnerkarte zeigt abstrakte Blöcke (`move`/`attack`) statt des
  gedruckten Instruktionstextes (Platzhalter-UX, vor dem PR gemappt).
- Side-Missions ohne Order-Deck fallen auf die Zufallsreihenfolge zurück (dokumentiert).
- Custom-Missionen mit Embedded-Karten (eigene Karten-IDs): diese Gruppen laufen
  automatisch auf dem Legacy-Pfad (kein Order-Deck = kein Plan), der Rest der Mission
  bleibt taktisch.