# M11-QA — Review-Befunde Kette M11c2 / M11b / M11d / M11e

Reviewer-Run: 2026-10-28, Task t_281157c8 (Board tacai)
Arbeitsbasis: **abgetrennte Worktree** `/home/open/work/ia-ai/qa_m11/wt` (detached @ `637ebbb`,
letzter Hop der Kette). Arbeitsbaum der Kette (`feature/tactical-ai` @ `cf3b834`) wurde nicht
berührt; keine Produktlogik geändert, nichts committet (dieses Dokument liegt unversioniert
unter `docs/`). Commits im Prüfumfang: `1cbe998` (M11c2), `e449cdd` (M11b), `b780866` (M11d),
`637ebbb` (M11e); Controller-dev-Bumps (`5bd2ffa`, `9402e32`, `8c48d6f`, `d0de982`) nur appVersion.

Ergebnis: **2 Befunde** (1× F2, 1× F4) — siehe Befund-Tabelle am Ende. Alle anderen
Matrix-Punkte ✓ mit Beleg.

---

## A) Vorgaben-Treue

### A1 S-Gating (alle neuen Effekte nur ab S4; S1–S3 bitgleich) — ✓
- **M11c2:** Gate `CardKeywords.ActiveFor` → `SkillReadsCards` liest nur `S4`/`S5`,
  null-Profil = niemals (CardKeywords.cs:79, 119–137). ActionBuilder.cs:205ff: beide
  Bewegungs-/Angriffs-Branchen nur bei `noMoveAttack=true` anders; Referenzpfad ohne
  Profil bitgleich. Beweis im Harness: `keyword_rules.json` Cases `ewebs_s1_unchanged`,
  `ewebs_s2_unchanged`, `ewebs_s3_unchanged`, `ewebs_reference_unchanged`,
  `pinched_s3_unchanged` gegen `ewebs_s4/s5_never_move_and_attack`, `pinched_s4_stands_and_shoots`
  — in **meinem** Harness-Lauf alle grün. Bestehende Goldens (focus/risk/action_plan …)
  wurden in KEINEM der vier Hops neu gegossen und laufen unverändert durch →
  S1–S3-Bitgleichheit mechanisch belegt, nicht nur behauptet.
- **M11e:** `VillainOptions.ActiveFor` nutzt dieselbe `SkillReadsCards`-Plumbing
  (VillainOptions.cs:399–404); `WithVillainOption` (TacticalAIBridge.cs:522–527) early-return
  ohne Profil/unter S4. Harness `live_vader_s3_gate`, `pos_vader_far_s3_unchanged` grün.
- **M11d:** `StrainMarkers=null` hart im Bridge-Builder → live null Einfluss (unten B10).
- **M11b-Hinweis (kein Befund):** Threat v2 gilt in der Overlord-Fokus-Schicht insgesamt —
  auch für den deprecated-overlord-Ladder-Label; die S1–S3-Referenzpfade
  (focus.json/risk.json, Basis `reference`/`proxy`) sind davon unberührt (Fixture
  `deny_proxy_fallback` pinnt den Fallback; focus.json nie geändert). Entspricht dem
  Karten-Auftrag „Proxy-Basen entfällt".

### A2 Fairness / kuratierte Assets öffentlich & textfrei — **✗ (Befund M11-QA-01)**
- Kein Zugriff auf Heldenpositionen/verdeckte Werte: Threat v2, StrainPressure,
  VillainOptions rein druckbasiert, Distanz nur Parameter, `los=null → visible=false`
  (VillainOptions.cs:383); utility bei unbekannter Balance versteckt
  (`live_blaise_balnull` grün). ✓
- Kuratierte Options-Tabelle nur öffentliches Kartenmaterial (gedruckte {A}-Texte der
  Sprachdateien). ✓
- **ABER:** „textfreie Zahlen-Assets wirklich textfrei" ist NICHT erfüllt — siehe
  Befund-Tabelle M11-QA-01. Die Asset-eigene Konvention
  (`conventions/faehigkeiten`: „reine Keyword-Slugs + Zahlen") und der Curator-Header
  („NO prose, NO wiki text") werden durch 12 Zeilen mit bis zu 247 Zeichen langen,
  slugifizierten Regelntexten gebrochen; Lizenzpolicy-Punkt 5 (LICENSE_DATA.md) erklärt
  Hero-Text-Wiki-Transkripte ausdrücklich zum offenen Punkt, der nicht in den Build darf.

### A3 Sprachregel — ✓
- Skriptprüfung (eigener Lauf): `setCardSkills`, `ordThreat`, `ordStrainPressure`,
  `ordOptionPrinted` in **11/11** `ui.json` vorhanden, Nicht-De-Dateien **identisch EN**,
  De übersetzt (z.B. ordOptionPrinted De: „Gedruckte Sonder-Aktion der Karte: {0} - {1}").
- Popup-Text kommt aus Sprachdatei + `ui.json`, keine hartcodierten UI-Strings
  (TacticalAIBridge.cs:539–545 `L.ordOptionPrinted`, `cd.abilities` der aktiven Sprache).
- Notiz (nicht vorgabenrelevant): interne `plan.Why`-Strings bleiben EN — bestehende
  Konvention aller Core-Why-Texte, kein UI-Feld.

### A4 C#8 / Newtonsoft — ✓
- Gate-Beweis im eigenen Lauf: `compile_check/build.sh` mit `-langversion:8.0`
  (Roslyn 3.5 = C#8-Max): APP rc=0 (258 files), TESTS rc=0. Syntaxscan ohne record-/
  target-typed-new-Treffer in den neuen Dateien.
- `tactical_hero_cards.json` ist strict-JSON (mit strengem Parser geladen, kein trailing
  comma); Asset-Load über bestehenden Newtonsoft-`Json`-Seam (TacticalAIAssets.cs:83–104).

### A5 Determinismus — ✓
- Kein neues `Random`/`Guid`/`DateTime.Now`/`TickCount` in den vier Hops (Scan über alle
  .cs-Diffs; Treffer nur in Alt-Code DataStore.cs/M5 und UILanguage-Feldnamen).
- Options-/Keyword-Wahl rein score-basiert (VillainOptions.Evaluate: value → class-rank →
  Ordinal-key; CardKeywords statische Tabelle).
- Harness **dreimal** in meiner Worktree gefahren: `ALL PASS checks=36972 fails=0` ×3,
  Ausgaben identisch (Diff nur Compile-Warnung/rc-Zeile des Erstlaufs) → Ordnungs-/
  ORD-Karte deterministisch.

### A6 Fixture-Disziplin — ✓ (Stichprobe bestanden)
- Hop-Matrix der Expectation-Dateien (git show --stat): M11c2 nur `keyword_rules.json`
  (neu); M11b nur `overlord_focus.json` (13 Cases, jede Differenz im Commit-Body
  einzeln begründet — nachgelesen); M11d `overlord_focus.json` **nur Additionen**
  (mechanisch verifiziert: Diff-Deletionen = exakt 1 Zeile, die schließende
  Root-Klammer) + `strain_budget.json` (neu); M11e nur `villain_options.json` (neu),
  bestehende Fixtures byte-identisch → Canon-Shift null.
- `strain_druck`-Key nur auf feuerverdächtigen Zeilen (OverlordFocus.AsDict:117–130),
  Pre-M11d-Canon byte-identisch — per Diff bestätigt.
- **Unabhängige Gegenrechnung** (Stichprobe „gegen die alte Version durchrechnen"):
  eigenständiger Python-Port der Trefferformel (Dice.cs-Faces 1:1 gegen die Tabellen in
  `curate_hero_cards.py` verglichen — identisch) rechnet 4 Canon-Erwartungen aus
  `m11b_mixed_roster_common_basis` exakt nach: Fenn 7/6 ✓, Gaarkhan verw. 0 ✓,
  Biv 7/6 ✓, MHD-19+item328 (BBR) 8/3 ✓ — Brucharithmetik, 4/4 match. (Script:
  `qa_m11/threat_spot.py`.)

## B) Bug-Jagd

### B7 Keyword-Tabelle — ✓
- Skript gegen `En-abilities`: Assault = {DG007, DG008, DG046, DG071} ==
  MultiAttack-Tabelle; Tripod = {DG007, DG008} == NoMoveAndAttack-Tabelle;
  FocusFire = {DG025} == Tabelle. **Keine toten IDs** (alle Tabellen-IDs in En-Datei
  vorhanden), kein toter Treffer (jede En-{Assault,Tripod,Focus Fire}-Karte in Tabelle).
- DG047 (Riot Trooper) korrekt NICHT aufgenommen (Crowd Control/Schild, Controller-Korrektur
  bestätigt).
- Sprachwechsel: kein Harness-Sprachwechsel-Fixtur vorhanden → statischer Beleg:
  CardKeywords pinned ausschließlich DG-IDs (sprachenunabhängig); VillainOptions-
  AbilityIndex-Joins gegen En- UND De-Arrays validiert: 13/13 — Array-Längen gleich,
  Slot trägt in beiden Sprachen ein `{A}`. DE-Dateien können das Verhalten nicht ändern.

### B8 MultiAttack / NoMoveAndAttack — ✓
- MultiAttack wird im Planer nirgends erzwungen (grep: keine Referenz außer
  Dokumentation + VillainOptions-`NotUsed`-Dokumentation); Angriffe bleiben per
  ORD-Schema bei 1 (OrderDeck.ValidateCard; Harness-Pins `attack_actions` ∈ {0,1},
  Case `assault_s4_single_attack_schema_limit`). >2 Angriffe nicht darstellbar.
- Kombifall E-Web (DG007/DG008 tragen Beides): `noMoveAttack` aus
  NoMoveAndAttack-Hälfte deaktiviert Beiden-Branch (ActionBuilder.cs:205–231,
  TryEngagedRetreat nur `!noMoveAttack`); MultiAttack kann nicht entdrosseln, da es
  im Regelpfad nicht vorkommt. Keine Schleifen neu (Struktur Early-Return; Harness
  Case `ewebs_s4_never_move_and_attack` grün).

### B9 Threat v2 — ✓
- Division/sqrt: Der Bedrohungs-Pfad enthält **keine** Laufzeit-Division und kein sqrt —
  reine exakte Rationale über feste Würfel-EW-Tabellen (Dice.ExpectedDamage:315–345).
  Leere Pools: Attributpool nur bei `Count>0` (HeroCards.cs:297), leerer
  Basispool → EV 0, `best` bleibt ≥0; Defender null → blocks 0. Verwundete Heldin mit
  leeren Attributswürfeln → Pool-Loops überspringen, kein Crash/NaN. Case
  `m11b_mixed_roster_common_basis` (Gaarkhan verw. = 0) grün.
- Unbekannte Held-ID: `CardFor`→null→kuratiertes M2a.4-Fallback (Bridge-Kommentar
  :761–764; Fixture `deny_proxy_fallback` pinnt es), degrade-null statt Exception.
- Item-Pool-Leerfall: `table==null`/`Item(id)==null` → continue (HeroCards.cs:286–288),
  42 Items kuratiert, alle `HatAngriffswirkung`-Geprüft.
- Proxy-Alt-Pfad tot: `RealCurated`/`CommonGroundChip` im gesamten Repo **referenzlos
  entfernt** (grep rc=1, Scripts+tools). Restliche „proxy"-Treffer sind der
  dokumentierte Referenz-/Fallback-Pfad (bewusst S1–S3-Verhalten, kein toter Code).

### B10 Strain-Fenster — ✓
- Aufschlag konstant `Druck=8` (StrainPressure.cs:47) — kapt **per Konstruktion**,
  skaliert nicht mit Marker-Überlauf (Harness `m11d_strain_below_buffer_and_capped`).
- Sitztpunkt: `OverlordFocus.Rank` nach Threat/ObjectiveActor-+1, **vor** der
  Gleichstandskette (Diff `e449cdd..b780866`: Block zwischen actor-+1 und StableSort;
  Compare-Keys unverändert) — keine Kaskade; der einzige M10b-Relikt-Term (+1 actor)
  bleibt unverändert davor. Marker>0-Guard hält Puffer-lose Helden raus.
- Ready-to-wire live-neutral: `StrainMarkers = null` hart im Bridge-Kandidaten
  (TacticalAIBridge.cs M11d-Block), `Pressure(null,…)` → null → Econ unangetastet;
  `strain_druck`-Key und WHY-Zelle feuern nur bei >0/==Druck. `Buffer` nie vom Caller
  (OverlordFocus.cs:205–213 Kommentar + Code: aus Card berechnet).

### B11 Optionen/Popup — ✓
- Nur wenn Score schlägt: striktes Gate `option > attackValue`, Gleichstand →
  Angriff (VillainOptions.cs:205–208, 385–391; Cases `live_kayn_s4_tie`,
  `pos_jabba_far_execution`, `live_jabba_bal0` grün).
- Overlord-Ignore: Optionsblock ergänzt ausschließlich die Popup-Bausteine
  (`plan.Blocks` nach `RenderBlocks` in `BuildActivationPlan`, Bridge:502–516);
  ORD-Bausteine/Plan-Inhalt unverändert, ohne Treffer frühere Rückgabe —
  identisch altes Verhalten. Kein Akkumulationspfad (Rebuild = frisches
  RenderBlocks, Append genau einmal).
- Duplikate: `Evaluate`/`PickLive` liefern genau eine Option pro Aufruf; `Table()` nach
  (card, abilityIndex) dedupliert.
- TMP-Escaping: Anzeige über `Saga.Utils.ReplaceGlyphs` (Utils.cs:136ff) — derselbe,
  im Spiel existierende Glyphenpfad, den Kartenzoom/EnemyActivationPopup seit je für
  exakt diese Textfelder nutzen; unbekannte `{…}`-Sequenzen bleiben Literal-Text
  (TMP parst spitige Tags), kein neues Markup-Surface.
- DG081 ehrlich: Bully value 0 + Execution Order 2 verlieren gegen Attack-EV 10/3 —
  pinnt `live_jabba_bal0`; Nicht-Nutzung dokumentiert (`NotUsed`, 4 Drucke mit Grund).

## C) Gates (in meiner Worktree selbst gefahren, Logs: qa_m11/logs/)
- `build.sh` (REPO auf Worktree gezeigt): **UI rc=0 / TMP rc=0 / APP rc=0 errors=0
  files=258 / TESTS rc=0 / URP rc=0** — build.log.
- Harness (`dotnet run … -- ImperialCommander2/Assets/Tests/Fixtures/tacai`):
  **ALL PASS checks=36972 fails=0**, dreimal, Ausgabe identisch. Zahl gleich dem
  letzten Worker-Kommentar (M11e 37 villain_options auf 36935 + M11f-Bestätigung 36972).
- Live-Gate in `tools/TacticalAiGate` (cwd-Pflicht eingehalten): **ALL PASS** — gate.log.

---

## Befund-Tabelle

| ID | Schwere | Datei:Zeile | Replikation | Vorgaben-Verstoß? |
|----|---------|-------------|-------------|-------------------|
| M11-QA-01 | **F2** | `ImperialCommander2/Assets/Resources/Languages/En/tactical_hero_cards.json` (u.a. `heroes[CT-1701]/faehigkeiten[1]/bezeichnung`, 12 Zeilen >60 Zeichen, max 247 Zeichen, 7 Helden: CT-1701, Loku Kanoloa, Onar Koma, Saska Teft, Shyla Varad, Tress Hacnua, Vinto Hreeda); Herkunft: `wiki_data/tools/curate_hero_cards.py:187-190` (`label = ab.text.split("\n")[0]` → `slug(label)`) | Skript: Slugs >60ch im Build-Asset extrahieren → enthalten vollständig slugifizierten Regeln-Volltext (z.B. `barrage_2_s_use_after_you_resolve_an_attack_with_a_p_weapon_to_…`); Gegenprobe: `wiki_data/heros.json` `"text": "Barrage 2 {{S}}: Use after you resolve an attack …"` ist exakt die slugifizierte Quelle | **Ja.** M11b-Karte Scope 1 verlangt ausdrücklich „nur Zahlen + reine Keyword-Slugs wie 'saber_strike' — KEINE Prosa-Texte, damit das Asset textfrei/lizenzneutral bleibt"; Asset-Konvention und Curator-Header behaupten „reine Keyword-Slugs + Zahlen"/„NO prose, NO wiki text" — faktisch falsch. Lizenzpolicy `wiki_data/LICENSE_DATA.md` Punkt 5 erklärt Hero-Kartentext-Wiki-Transkripte zum offenen, **nicht buildfähigen** Punkt; die slugs transportieren genau diesen Text Wort-für-Wort (nur underscores) in den Build (Resources). Verstoß auch gegen die dev-35-Behauptung „einzige Strings >60 Zeichen sind Policy-/Legende-Zeilen" (gemessen: 12 Faehigkeiten-Zeilen). Kein Runtime-/Fairness-Effekt (Slugs werden nirgends angezeigt); Rework-Richtung dem Controller überlassen (z.B. Slug aus Keyword-Namen statt Text-First-Line; die Prefix-Merge-Logik in StrainPressure vereinfacht sich dadurch). |
| M11-QA-02 | F4 | `ImperialCommander2/Assets/Scripts/TacticalAI/Core/HeroCards.cs:278-300,308-314` | `EliminateEv` sammelt `genauigkeit/schaden`-Statik und `durchschlag` **aller** possedierten Items in EINEN Keyword-/Pierce-Topf und wendet ihn auf JEDEN Pool inkl. Basiswaffe an. Datenbeispiel: item311 (`RR` + genauigkeit 6) würde +6 Acc auch dem Basiswaffen-Pool geben, obwohl der Bonus auf der Waffenkarte des anderen Drucks steht. | Grenzfall, nicht blockierend. „Echte Kartenwerte"-Faithfulness: per-Waffen-Boni werden globalisiert; Richtung ist dokumentierter Safety-Bias („Empire may never under-estimate") → Bedrohung kann nur zu hoch, nie zu niedrig. Aktuell **kein** Canon-/Live-Effekt nachweisbar (Fixtures nutzen nur item328 = reine Würfel; Live-Lastouts mit static-Boni treffen nur die Best-Pool-Obergrenze). Hinweis zur Nachjustierung, falls Faithfulness 1:1 gefordert wird. |

Nicht-Befunde (geprüft, für die Akten): (a) interne Why-Strings EN = Bestandskonvention;
(b) Threat v2 gilt auch im deprecated-overlord-Label = Kartenentwurf („Proxy-Basen
entfällt"), S1-S3-Referenz unberührt; (c) Popup-Reichweiten-Überschuss-Glied
(PickLive ohne Distanz) = dokumentierter, im Code benannter Kompromiss,
Printed-Bedingung textlich dabei.

## Verfahren
- Alle Gates/Scripts gegen `637ebbb` in eigener Worktree; Kanban-DB-Zitate gegen
  Originaltexte geprüft; Behauptungen des Controllers/Workers wurden nur dort
  übernommen, wo ich Gegenbeleg (Datei:Zeile/Skript/Gate-Ausgabe) habe.

---

# Delta-Recheck nach M11g (Abschluss, 2026-10-28)

Basis: eigene, frische abgetrennte Worktree `qa_m11/wt2` @ `43c863e` (= HEAD,
enthält M11f `8de1ee3`/`cf3b834` und M11g `74c9c23`/`43c863e`); Arbeitsbaum der
Kette unberührt, keine Produktlogik geändert, nichts von mir committet.
Logs: `qa_m11/logs2/`. Auftragsgrundlage: Controller-Anweisung nach
M11-QA-01-Entscheidung (Kartenvolltexte bleiben Build-Inhalt; Thomas, 2026-10-28):
Delta-Recheck der drei Texte + Bestätigung Asset unverändert.

## Zu M11-QA-01 — erledigt durch M11g, Delta-Beweise

1. **Asset inhaltlich unverändert (programmatisch bewiesen).**
   `git diff 637ebbb..HEAD -- .../En/tactical_hero_cards.json` zeigt genau zwei
   geänderte Zeilen: `source` und `conventions.faehigkeiten` (Doku-Kopf).
   Skriptgegenprobe (Datenblock ohne diese beiden Header-Strings,
   sortiert normalisiert, sha256): `316cf495a8a1fdfa` == `316cf495a8a1fdfa`
   (alt/neu) → **DATA IDENTICAL: True**. Die 12 Volltext-Zeilen (>60 Zeichen,
   max 247 Zeichen, 7 Helden) sind zeichengleich erhalten (`identical: True`);
   heroes 21 / items 42 wie zuvor.
2. **Die drei Texte jetzt wahr und ehrlich ( gegen gemessene Realität geprüft):**
   - `LICENSE_DATA.md` Punkt 5: trennt (a) Roh-Wiki-Dumps (out-of-build,
     unverändert) von (b) kuratierten Brettspiel-Kartentexten; Wiki als
     Transkriptions-Medium benannt; „keine Sauberkeit behauptet",
     Originallizenz des Wikis ausdrücklich als nicht einwandfrei geklärt
     markiert; Controller-Entscheidung mit Datum dokumentiert. ✓
   - Asset-Header `source` + `conventions.faehigkeiten`: beschreiben jetzt den
     echten Zustand („Volltext nur bei auf der Karte aufgedruckten
     Fähigkeiten"); der alte, durch meinen Befund widerlegte Claim
     („reine Keyword-Slugs + Zahlen") ist ersetzt. ✓
   - `curate_hero_cards.py`-Header: „NO prose, NO wiki text" berichtigt auf
     „slug carries the full printed text too"; **Skriptlogik nachweislich
     unberührt** — `git diff 637ebbb..HEAD` über das Skript: 0 Nicht-Kommentar-
     Zeilen geändert; `py_compile` OK. ✓
3. **Präzedenz-Behauptung verifiziert** (Doku stützt sich auf allies/villains —
   nicht ungeprüft übernommen): `Assets/Resources/Languages/En/DeploymentGroups/`
   `allies.json` trägt 28, `villains.json` 31 Volltext-Zeilen >80 Zeichen
   (gedruckte Regeln, z.B. „While a friendly figure within 3 spaces is
   defending, it may reroll 1 defense die…"). Die App shipped also bereits
   Kartenvolltexte — Präzedenz ist real. ✓
4. **Restliche Doku-Widersprüche?** Punkt 2 der Policy beschreibt dasselbe
   Modell (1:1-Transkripte bei Selbstaufdruck, Wiki = Transportmedium) —
   konsistent mit Punkt 5 und Asset-Header. Kein Restbefund.

## Gates in wt2 @ HEAD selbst gefahren (nicht Autor-Ausgaben übernommen)

- `build.sh` (REPO auf wt2): **BUILD_RC=0** — UI/TMP/INSYS/RESMGR/ADDRS/MATH/
  SRPC/URP rc=0 errors=0; **APP rc=0 errors=0 files=258**; TESTS rc=0 (build.log).
- Harness **3×**: `ALL PASS | checks=36972 fails=0` in allen drei Läufen,
  zahlgleich zu meinem Erstlauf @637ebbb und zum letzten Worker-Kommentar
  (M11f/M11g: 36972). Determinismus: Diff Läufe 1/2 = nur Compile-Warning-Zeile
  des Erstlaufs + rc-Marker; Diff 2/3 = nur rc-Marker → Ergebnis identisch.
- Live-Gate in `tools/TacticalAiGate` (cwd-Pflicht): **ALL PASS**, GATE_RC=0.
- M11f (`8de1ee3`) lag außerhalb meines Prüfumfangs (Präfixliste der Kette) und
  wurde vom Controller unabhängig reproduziert (`cf3b834`); meine Gates @HEAD
  belegen zusätzlich: kein Regressionseffekt auf Build/Harness/Gate der Kette.

## Restzustand der Befunde

- **M11-QA-01: erledigt.** Spec-Korrektur durch Controller/Thomas (M11g);
  Befund gegen die alte Spec war korrekt, Asset bleibt wie gemessen, Doku
  nun wahr. Keine Nacharbeit mehr.
- **M11-QA-02 (F4, HeroCards.cs `EliminateEv`): bleibt als Beobachtung
  stehen** — keine Controller-Entscheidung dazu getroffen, dokumentierter
  Safety-Bias, aktuell kein Canon-Effekt. Dem Controller zur Kenntnis;
  keine Befund-Eskalation durch die QA.

## Ergebnis

**REVIEW-CLEAN (delta)** — Matrix-Punkte A1–A6, B7–B11, C-Gates: ✓ wie im
Erstbefund; M11-QA-01 durch M11g erledigt (Delta bewiesen: Datenblock
zeichengleich, drei Texte wahr, Präzedenz real); Gates bei HEAD ALL PASS,
Harness 36972 zahlgleich und deterministisch. QA abgeschlossen.
