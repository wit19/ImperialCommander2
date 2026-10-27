# M7 QA-Lauf — unabhängiger Adversarial-Review M5+M6 (2026-10-27)

Karte t_241fd382. Review-only: kein Produktionscode geändert, keine Fixes.
Review-Range App-Repo `cbc33c8..HEAD` (M5: 284a0b1, 9318965, 86ffd29; M6:
e291562, 1a428e8; dev-Marker 4596d7c/9500683 = Controller-QA, appVersion
dev 25→27 — nur diese Commits fassen DataStore.cs an, ✓ erlaubt).
Sim-Referenz tacai_build@feature/tactical-ai (2f154e3, 985f890).
Methode: alle Gates selbst gelaufen; jedes Finding mit Datei:Zeile +
statischem Reproduktionsweg; PASS-Listen nur mit Beleg-Output.

## Gates (dieser QA-Lauf, independently reproduced)

- `compile_check/build.sh`: `APP: rc=0 errors=0 files=253` / `TESTS: rc=0
  errors=0 files=9`, EXIT=0. C#8-Gate real erzwungen (`-langversion:8.0`,
  build.sh Zeilen 21/30/46 — Roslyn 3.5 Proxy wie Unity 2020.3).
- Harness: `PASS world_feed.json (126 checks)`, `PASS overlord_focus.json
  (18 checks)`, `== summary: ALL PASS | checks=36774 fails=0`, EXIT=0.
- Sim: `401 passed in 153.71s (0:02:33)`, EXIT=0.
- Live-Runner /tmp/setgate (Nachbau M6-Handoff): `WORLDGATE ALL PASS`,
  `== ORD ALL PASS ==` — siehe MINOR-3: liegt nur in /tmp, kein Repo-Commit.

## Findings-Matrix

| # | Severity | Fundstelle | Kurzform |
|---|----------|-----------|----------|
| F1 | MAJOR | WorldEventsUnity.cs:199,206-216 × MissionLogger.cs:20-84 | DrainLogger vergleicht `it.message` mit Gruppennamen, LogItem.message ist aber lokalisiertes Rich-Text — Death/Removal/ActivationStart im App-Betrieb tot + HashSprache |
| F2 | BLOCKER (Atmosphäre: M3-Ära, hier gemäß Prüfplan-Punkt 3; nicht von M5/M6 verursacht) | TacticalAIBridge.cs:390,409; Rufe SagaController.cs:742-743, EnemyActivationPopup.cs:113-114 | Mission-Badge OFF (Token "off") + global ON: TryPeekNextActivation/TryBuildActivationPlan prüfen nur Enabled, kein ActiveNow() → Aktivierungsreihenfolge + Popup-Plan laufen trotz Legacy-Mission |
| F3 | MINOR | /tmp/setgate/*, in keinem Repo referenziert | Live-Gates (Leak fail-loud, gap/resync, ORD-Fairness) nicht versioniert |
| F4 | MINOR (Spezifikations-Annahme) | WorldEventVector.cs:52-70 vs. DeploymentCard.cs:55, HPTracker.cs:43-48 vs. Konzept §3.5 Z.597 | Rad-Richtung: Konzept dekretiert Restvitalität, App-Konvention (Frisch={0,0,0}, Doppelklick→0, Name) spricht für Wundenzahl — Unity-Livetest muss die Annahme bestätigen |
| F5 | MINOR | WorldEventsUnity.cs:166-170 (MissionStart roundLimit) vs. Utils.cs:316-324, SagaController.cs:809, MissionEventHandler.cs:185/201 | Posture "stall" aus authored roundLimit trotz Spieler-Clock OFF (roundLimitToggle 0/2: Limit-Event feuert nur bei Setting==1) und ohne Event-RoundLimit-Änderungen zur Laufzeit |

## Detail-Befunde

### F1 (MAJOR) — DrainLogger matcht nie reale Namen

`MissionLogger.LogEvent` speichert in `LogItem.message` nicht den Roh-Namen,
sondern das formatierte `eventText` mit Label + Color-Tags
(MissionLogger.cs:78-83; z. B. GroupDefeated:
`<b><color=red>{groupDefeatedLabel}:</color></b> <color=orange>{NAME}</color>\n\n`).
Der Adapter vergleicht den String direkt:

- `WorldEventsUnity.cs:199` `var name = it.message.Trim();`
- `:206` `_heroDefeated.Contains(name) || IsHeroName(name)` — immer false,
  `:209` `Append("Death", …, IdForName(name))` — IdForName findet keine
  deployedEnemies-Karte für Rich-Text ⇒ Phantom-Gruppe mit bunt
  kategorisiertem Label-String als GroupId in Feed, View und HistoryHash.
- `:213` Removal, `:216` ActivationStart analog.

Reproduzierbar ohne Unity: eventText-Komposition in MissionLogger.cs:40-83
gegen IdForName/IsHeroName (reine Strings, Gleichheit unmöglich).
Folgen im App-Betrieb: (a) View.Alive sinkt nie (kein gematchter Death-Treffer)
⇒ EnsureThreatMirror (Bridge:772-777) zählt tote Gruppen als lebend,
Ledger-Einkommen zu hoch ⇒ RiskPosturen/Erklärung biased (public-Daten-Qualität,
kein Leak); (b) HistoryHash/Canon enthalten lokalisierte Labels ⇒ derselbe
logische History-Hash ist sprachabhängig — Determinismus-Promotion
("bitidentisch") gilt nur pro Sprache; (c) HeroDefeated via Logger nie
(über PollHeroes-Fallback abgedeckt, view.Seitigkeit ok).
Kein committedes Gate kann das fangen: world_feed.json scriptet Events
direkt, die Adapter-Extraktion hat keinen Tester. Fix-Richtung: LogItem um
Roh-Feld erweitern oder Tags strippen + Name extrahieren; dazu
Adapter-Extraktions-Test in den Harness/späteres NUnit spiegeln.

### F2 (BLOCKER, vorbestehend M3, HEAD-betreffend) — Mission-OFF-Hook-Loch

Dokumentierte Semantik (Bridge:143-145, SagaSetup.cs:481-483): "the mode
frozen into setupOptions at mission start decides", "an explicit
per-mission 'legacy' must survive (FINAL-QA audit)". ActiveNow() (Bridge:147)
ist genau dafür da und wird von OnNewRound (Bridge:241) und
TryChooseDeploymentPoint (Bridge:594) benutzt. Aber:

- `TacticalAIBridge.cs:390` `if (!TacticalAISettings.Enabled || candidates…)` —
  TryPeekNextActivation: kein Modus-Gate.
- `TacticalAIBridge.cs:409` `if (!TacticalAISettings.Enabled || cd == null)` —
  TryBuildActivationPlan: kein Modus-Gate.
- Rufer gate-n nur Enabled: SagaController.cs:742-743
  (`OnActivateImperial` — Planner-Reihenfolge ersetzt den 70%-Zufallszug),
  EnemyActivationPopup.cs:113-114 (`ShowSaga` — Instruktionenskarte +
  deterministisches Ziel ersetzt Legacy-Blöcke).

Reproduktion (statisch, Ende-zu-Ende): globales Enabled=true;
Mission-Badge auf OFF (NextMissionToken erzeugt "off",
EffectiveMode("off")=="legacy", TacticalAISettings.cs:166);
SagaSetup friert setupOptions.aiMode="off" ein (SagaSetup.cs:331-332) ⇒
ActiveNow()==false, aber beide Hooks planen und liefern ⇒ Aktivierungs-
reihenfolge und Popup laufen mit AI, obwohl das Spiel auf OFF steht.
EnsureFeed baut zudem in OFF-Missionen das Adapter-GO (Kommentar
Bridge:179-180 "legacy play never creates the component" gilt nur bei
global off). Stille Vertragsverletzung am vom Nutzer gesetzten Fluchtschalter —
Klasse der FINAL-QA-Hook-Gate-Blocker. Fix: `|| !ActiveNow()` in beide Hooks
(je eine Zeile).

### F3 (MINOR) — Live-Gates nicht versioniert

WORLDGATE (seq-gap→Resync, Leak fail-loud) und ORD (Permutation,
Determinismus 2×, Isolation Canon/Rank, Ledger, Risk-Press) liegen nur in
/tmp/setgate und sind in keinem Repo referenziert. Die Gewährleistungen der
Explizit-NUnit-Suiten (Klassen sind `[Explicit]`) hängen an genau diesen
Scripts. Fix-Richtung: unter tools/TacticalAiGate/ committen, Gate-Aufruf in
die QA-Doku-Notiz.

### F4 (MINOR) — Rad-Richtungs-Annahme bestätigen

Konzept §3.5 (Z.597) definiert `woundTrackerValue` als Restvitalität je Figur;
Fixture `wounded_vitality` pinnt genau das (wheel 5/5 = lebt, 13/15 Vitalität)
und die C#-Projektion summiert Räder als Rest-HP (WorldEventVector.cs:52-70).
Die App-Konvention liest gegenteilig: Neuwert {0,0,0} (DeploymentCard.cs:55,
bei Rest-HP-Lesart wäre jede frisch gesetzte Gruppe "tod"), Doppelklick-Reset
auf 0 (HPTracker.cs:43-48), Label `groupWoundNumber`,
Konzept §3.3.3-2b selbst sagt "Schadens-Deltas". Wenn Menschen die Räder als
Wundenzahl füllen, dreht die Vitalitäts-Eingabe für S4/S5-Risiko/Fokus
um (nur bei beobachteten Gruppen; unbeobachtete Gruppen sind per Konstruktion
=1, Fixture/Pins bleiben grün). Kein Sim-Oracle für diese Frage (kein
Welt-Feed in der Sim). Fordering: im ersten Unity-Playtest die Rad-Richtung
an einem Brett verifizieren; ggf. `health - wheel` Komplement + Fixture-Repin.
Bis dahin dokumentierte Spezifikations-Annahme, kein Implementierungsfehler
gegen das Dokument.

### F5 (MINOR) — Clock-Posture gegen Spieler-Einstellung

MissionStart payload roundLimit kommt aus gameVars/mp (WorldEventsUnity.cs:166-170);
Posture-Ableitung: roundLimit>=0 ⇒ "stall" (WorldEventVector.cs:261). Der
Rundenlimit-Toter feuert aber nur bei `roundLimitToggle==1`
(Utils.IsRoundLimitReachedWithSetting(1) in SagaController.cs:809; Settings
0=off/2=dangerous, SettingsPanel.cs:92-94), und Missions-Events können
gameVars.roundLimit auf -1/`setLimitTo` ändern (MissionEventHandler.cs:185/201).
26 echte Missionsziele mit roundLimit 4-12 (Scan über SagaMissions/*/*.json)
ergeben "stall"-Posture mit Doktrin/Erklärung, obwohl die Uhr abgeschaltet
ist. Keine versteckte Info, Entscheidungsfeld — Adapter könnte PlayerPrefs
`roundLimitToggle` lesen (öffentliche Einstellung) und Limit-Änderungen als
ObjectiveChange-artiges Event nachzögen.

## PASS-Liste (je ein Beleg)

1. **Fairness-Kanon** — Whitelist ist die ganze Fläche: `PayloadKeys`/
   `RequiredKeys` + fail-loud `Validate` bei Append UND Resync
   (WorldEvents.cs:60-107,145-159); unbekannte Event-Kind-Weiche existiert
   nicht (Validate wirft, Project-switch ignoriert nur registrierte Kinde).
   Move/TileChange = vocab slots ohne Producer (by design). Bridge-M6-Ebene
   isst nur Feed+Gedrucktes+Squad-HUD (BuildFocusCandidates:700-725,
   BuildRiskPositions:800-830, TrySelectTarget:630-671 — permutation only,
   gleiche lebende Heldenmenge, ohne Feed Referenz-Rang). Beleg: ORD-Gate
   `permutation_only_identical_legal_set` PASS + Harness
   `overlord_focus.json (18 checks)` PASS inkl. `.permutation`-Check pro Fall.
2. **Determinismus** — 2× Replay als Harness-Check pro Fixture-Fall
   (SectionsD/E), Hash canonisiert payload-Sortierung+invariant culture
   (WorldEvents.cs:166-222), grep `Random|DateTime|Guid` in allen neuen
   Dateien leer (SessionKey nutzt stabiles campaignGUID), Fixture-Fall
   `wounded_vitality_shuffled_keys` pinned Schlüsselreihenfolge-Unabhängigkeit.
3. **Legacy-Bit (Enabled=false)** — jede Hook-Stelle fällt bei Enabled=false
   in den Originalzweig; PullFeedView/EnsureFeed laufen nur innerhalb des
   Enabled-Gate (Bridge:188,216); Mission-OFF-Loch = F2, separater Befund.
4. **Adapter-Qualität** — `[DisallowMultipleComponent]` + FindObjectOfType +
   `_feedTried` idempotent (Bridge:183-197); Missions-Reset am Key-Wechsel
   campaignGUID|missionID (WorldEventsUnity.cs:377-384); Resync bei
   Logger-Schrumpfung und Wheel-Verlust, Re-Log aus reinem Public-State
   (:103-108, :183-187, :298-358); Rad-Cap 3 == HPTrackerContainer
   (3 Tracker, `woundTrackerValue`-Array-Länge 3, DeploymentCard.cs:55) —
   kein Cap-Fehler; Logger-Rundenstempel = Logging-Runde (MissionLogger.cs:80).
   Namens-Match-Loch = F1.
5. **Test-Qualität** — Expect-Provenanz maschinell bestätigt: Harness-Lauf
   mit `--record` gegen die committeten Fixtures ⇒ 20/20 Fälle
   expect==record, keine von-Hand-frisierten Erwartungsblöcke; Fixture-Historie
   kennt nur 86ffd29/1a428e8 (keine Expect-only-Händedinger); jede
   `[Explicit]`-Zeile hat Gewährleistung in Harness oder Live-Runner
   (Lücke: Live-Runner nicht versioniert = F3).
6. **C#8/Konventionen** — Build-Gate `-langversion:8.0` rc=0; keine C#9-Konstrukte
   über Scan; Vektoren nie manuell editiert (Punkt 5); DataStore.appVersion nur
   dev-Marker-Commits; Settings-UI/SagaSetupOptions im Range unangetastet;
   ui.json: genau 8 neue Keys `ord*` innerhalb `uiTacticalAI` × 11 Sprachen,
   keine Hardcodes (BuildOverlordWhy nur TF(L.…)).
7. **M6-Spezifika** — ThreatEconomy nur auf FocusCandidate-öffentlichen Feldern
   (Struktur hat keinen Zugriff auf Gruppen-/Hidden-State, OverlordFocus.cs);
   Wortstamm-Präfix-Scan über ALLE authored Missionsobjekte
   (SagaMissions/*/*.json, SagaTutorials, MissionData) +
   priorityTargetType-Werte (Rebel/Hero/Ally/Other/Trait): keine
   Gleich-Klasse-Fehltreffer ('smuggler'→eliminate ✓, 'stealth' kommt in den
   authored Daten nicht vor, "stronghold" matcht 'hold' strukturell nicht —
   Präfix am Wortanfang); Erklärtexte vollständig in uiTacticalAI ×11.
8. **Sim↔C#-Drift** — `export_csharp_vectors.py` Re-export: 23/23 Dateien
   byte-identisch zu den committeten Fixtures; 10 FINAL-QA-Fixes im C#-Core
   mit Pin-Kommentaren gespiegelt (Belief #8, Doctrine/HeroProfiles/RiskPosture/
   Victory #2/#3/#5/#6); Pin #10 Gruppen-Vitalität: Sim-Formel (sum vitalität/
   Σ health·n) == C#-Formel bei voller Rad-Abdeckung, Fixture-Pin 13/15.

## Gesamt-Empfehlung

**Nacharbeit nötig — vor Release additionally REVIEW-BLOCKED über F2.**

1. F2 (BLOCKER, vorbestehend seit M3-Hook-Integration): ActiveNow()-Gate in
   TryPeekNextActivation + TryBuildActivationPlan nachrüsten; die zwei Rufe
   damit korrekt in den Legacy-Zweig. Nicht M5/M6-Hausmacht, aber HEAD
   bricht den per-Mission-OFF-Vertrag still.
2. F1 (MAJOR, M5): DrainLogger auf strukturierte Roh-Namen umstellen
   (LogItem-Feld oder Strip+Extrakt), Adapter-Extraktion testbar spiegeln
   (Harness oder künftiges NUnit). Bis dahin ist der Feed-Kernkanal
   Tod/Entfernen/Aktivierung im App-Betrieb faktisch tot und der Feed-Hash
   sprachabhängig.
3. F3 (MINOR, Pflicht vor nächstem QA): setgate-Gates committen.
4. F4/F5 (MINOR): F4 im ersten Unity-Livetest verifizieren (eine Brett-
   prüfung), F5 roundLimitToggle mitlesen — jeweils dokumentierte Klein-
   korrekturen, kein Freigabe-Hindernis für den M6-Core selbst.

M6-Auslieferungsumfang (OverlordFocus, Feed-Gate, ui.json×11, Fixtures/Pins,
Provenanz) ist handwerklich sauber und gate-grün; die Befunde treffen die
Bridge-/Adapter-Schicht (F1/F2) und die Prozess-Absicherung (F3). Bekannte,
ehrlich gepinnte Ausnahmen bleiben wie vom Vorarbeiter dokumentiert
(siege-Inversion M2b.2, Tile-Graph offen, Unity-Livetest ausstehend).
