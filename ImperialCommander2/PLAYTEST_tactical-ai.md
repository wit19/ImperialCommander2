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

1. Kampagne wie immer konfigurieren (Helden, Ausrüstung, Fähigkeiten — alles wie gehabt).
   Die KI braucht **keine** Änderungen an der Kampagnenverwaltung.
2. Im Spiel erscheint das schwebende Panel **TACTICAL AI**. Die Buttons schalten
   (jeder klickt sich zyklisch durch seine Werte):
   - *TACTICAL AI* — Master: **off** (Default = App verhält sich exakt wie vorher) / **on**.
   - *AI MODE* — `legacy` (Vanilla-Zufall) → `reference` (Inferenz-Aktivierung +
     taktische Zielauswahl) → `overlord` (zusätzlich harte Doktrinen: Rückzugs-/Fokusregeln).
   - *SKILL* — S1…S5 (Kompetenzkurve der Truppen).
   - *DEPLOYMENT* — `secret` / `open` (verrät die Aufstellung dem Blick aufs Brett).
   - *EXPLAIN* — `none` → `post_hoc` → `full` (Erklärtexte unter den Entscheidungen).
   Der globale Schalter ist pro Spiel speicherfest; pro Mission kann `aiMode` in den
   Missionsdaten zusätzlich übersteuern (Konzept §4.2.7, für uns im Playtest egal).
3. Normal spielen. Die KI-Entscheidungen laufen über die bestehenden App-Dialoge
   (Aktivierungsfenster, Zielauswahl-Popup) — nichts Neues zu lernen.
4. Fürs Protokoll: **EXPLAIN** mindestens auf `post_hoc` — dann erscheinen die „Why"-Texte
   (z. B. `Focus: gedrucktes Vorzugziel …`, `ThreatScore … = Waffe 7 + Offensiv-Tools 3.6 …`)
   im Aktivierungsablauf. Screenshots davon sind das wichtigste Playtest-Material.
5. Alle Entscheidungen werden zusätzlich in die **Unity-Entwickler-Konsole** geloggt
   (`TacticalAIBridge::…`, deterministisch, kein Zufall). Beim Build über den Editor:
   Konsole offen lassen und am Ende als Textdatei sichern (Rechtsklick → Save).
   Für Android-Builds: Logcat-Export (`adb logcat > playtest.log`) beim Mitspielen.

## 3. Worauf wir beim Spielen achten

- **Aktivierungsreihenfolge:** Wirkt sie „vernünftig" (Gefährliches/Gesichtetes früher),
  ohne vorhersehbar-mechanisch zu sein?
- **Zielauswahl:** Schießt die KI nie bewusst auf einen bereits verwundeten Helden,
  wenn ein frischer同等 gefährlich ist? (Fairness-Axiom)
- **Rückzug/Positionierung:** Verwundete Truppen sollten sich sichtbar zurückziehen
  („Ansonsten"-Ast), gesunde vorrücken. Kein „Body-Blocking" von Türen.
- **Kompetenz ohne Gemeinheit:** Die KI darf den Helden ruhig schlagen — aber nur mit
  Informationen vom Brett, nicht durch „Gedächtnis-Tricks". Wenn sich eine Entscheidung
  „unfair/berserkerhaft" anfühlt → Screenshot + Rundennummer notieren.
- **Vanilla-Regression:** 1–2 Runden im Modus *Vanilla* gegenspielen; die App muss sich
  exakt wie vor dem Feature verhalten.

## 4. Windows-Version bauen (Compile-Anleitung)

Die App ist ein Unity-Projekt (Unity **2020.3.48f1**, URP). Windows-Build:

1. **Unity Hub** installieren: <https://unity.com/download> (kostenlos, Personal-Lizenz).
2. Im Hub → *Instanzen* → *Installieren* → **Unity 2020.3.48f1** (exakt diese Version,
   sie steht in `ProjectSettings/ProjectVersion.txt`).
   Beim Haken-Dialog zusätzlich anhaken:
   - **Windows Build Support (Mono)** und **(IL2CPP)** + **Windows SDK & Compiler**.
3. Repository holen: `git clone https://github.com/wit19/ImperialCommander2`
   und im Hub das Verzeichnis `ImperialCommander2/ImperialCommander2` als Projekt öffnen
   (Branch `feature/tactical-ai` checkouten).
4. Package-Restore abwarten (Öffnen dauert beim ersten Mal mehrere Minuten — Addressables
   und URP werden aus dem Lockfile `Packages/packages-lock.json` geladen).
5. `File → Build Settings` → Platform **Windows, Mac, Linux → PC, Mac & Linux Standalone**,
   Target Platform **Windows**, Architecture **x86_64**.
   Szenen: die in Build Settings eingetragenen Szenen bleiben unverändert.
   (Vor dem Build: `Assets → Addressables → Build → New Build → Default Build Script`,
   sonst fehlen die Adressable-Assets im Player.)
6. **Build** → Ordner wählen → `Imperial Commander.exe` starten. Test unter
   `Edit → Project Settings → Player`: IL2CPP nur testen, wenn IL2CPP-Support
   (`AotTypeEnforcer`, Android) mit angefasst wurde — Mono ist der Standard.

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

## 5. Bekannte Grenzen (bewusst so entschieden)

- Die KI sieht keine Sichtlinien — Angriffe „durch Wände" verhindert sie über
  Wahrscheinlichkeitszonen (Sichtlinien-Bereich v3.9 §3.5), nicht über echtes LoS.
- Heldenschaden muss wie gehabt manuell in die Figuren-Tracker eingetragen werden;
  die KI nutzt die Deltas (Schadensherd → Angreifer-Vermutung) und Heldenschaden
  als Threat-Gewicht (HeroThreatProfile).
- Der in-App-Fokus nutzt aktuell **gedruckte Vorzugziele + Aufstellungsreihenfolge**;
  die volle ThreatScore-/Doktrinen-Rechnung (HeroThreatProfile über Weltmodell) greift
  erst mit dem Hex-Adapter (M5). Der ThreatScore in `reference`/`overlord` ist daher
  im Playtest noch gleichauf — die EXPLAIN-Texte zeigen, was die KI wirklich nutzt.
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
