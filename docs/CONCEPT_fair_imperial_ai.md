# Konzept v4.2: „Tactical AI“ — optionaler regelbasierter Imperialer-KI-Modus für Imperial Commander 2

Stand: 2026-10-27 (v4.2 — M5-Realisierung: passiver Welt-Feed umgesetzt, Seam = TacticalAIBridge statt CoreApi (§3.3.1b Realisierungs-Hinweis); v4.1 — Thomas-Entscheid Option A: EIN Schwierigkeits-Regler. Die Skill-Leiter
(Rekrut, Stormtrooper, Veteran, Kommandant, 5-Sterne-General) ist die einzige
Schwierigkeits-Stellschraube in den Settings; das Regelprofil wird DARAUS ABGELEITET:
S1-S3 = Reference (Buchausführung, wachsende Qualität bei diskretionären Regeln),
S4-S5 = Overlord (Score-Optimierung auf demselben Öffentlichen Stand). Der Modus-Regler
fällt aus den Settings (Modus bleibt Test-/Konsolen-Override `SetModeOverride` und
3-Zustands-Missionsbadge legacy/REF/ORD — beides Flächen, wo das Verfahren ausdrücklich
gefragt ist). Begründung: Skill steuerte in Reference fast nichts, Overlord S3 > Reference
S5 — zwei Regler an einer Schraube.) · v4.0 — Thomas-Entscheid: Hex-/WorldProvider-Registrierung verworfen, M5 wird
der **passive Welt-Feed** §3.3.1b; neue Kanon-Regel: kein Feature darf vom Menschen verlangen,
der App Positionen oder Zustände zu melden — Spiel, nicht Arbeit.) v3.11 — HeroThreatProfile
§3.3.1a: Loadout-WISSEN aus der App selbst (campaignItems/campaignSkills verifiziert),
Tier+Kosten-Proxies nach Thomas, kein Opt-In; v3.9 RiskPosture §3.5.1: Angriff/Deckung-Abwägung
per Fallback-Doktrin; v3.8b OrderPlanner+Inferenz; v3.7 Skill-Skala; v3.6 Theater/Doktrinen;
v3.2 Observability) · Repo: GlowPuff/ImperialCommander2 @ a7bb921 (main) · Autor: werth IT /
Thomas (Konzept), Hermes (Analyse)

**v1→v2:** Modus-Name **Tactical AI** (Thomas) · Aktionsbudget an die offiziellen *Rules Reference
Guide* angepasst (§1.2) · Regelprofile Reference/Overlord (§2) · Bedrohungsstufen §6 ·
Simulations-/QA-Harness §7 · Aktivierungsreihenfolge per Zielnähe §3.4 · EXE-Befund §1.7.

**v2→v3:** Echte JinM-App verifiziert & analysiert (§1.8) · **Größen-/Raumlegalitäts-Layer**
§3.8 (Massive/AT-AT-Regel, Mehrfelder-Figuren) · **Bedrohungs-Ökonomie-Doktrin** §3.9
(sparen statt sofort ausgeben) · verdeckter Elite-Würfel als weiterer Schummel-Befund §1.1
(Um8) · JinM-strukturelle Lehren in §2/§3.2.

---

## 0. Ziel & Abgrenzung

Ein optional aktivierbarer KI-Modus („Tactical AI“), in dem die imperiale Seite
**regelbasiert, deterministisch erklärbar und ohne verdeckte Boni** agiert. Die *Struktur* der
Automatisierung folgt dem Vorbild der Monsterlogik aus **Gloomhaven** (Rulebook S. 37–43: Monster
Acting Order, Focus, Monster Movement, Monster Attacks) und **Frosthaven**
(Verhaltensmuster-Feldführer); **was** die Einheiten tun dürfen, kommt ausschließlich aus den
Imperial-Assault-Regeln (§2). Die Legacy-Logik bleibt unverändert erhalten; der Modus wird über
die Missions-Setup-Optionen ausgewählt.

Leitprinzipien:

1. **Kein Schummel, kein Nebel:** Jede Aktion eines Imperials lässt sich vor ihrer Ausführung
   mit Missionsregeln + Kartentext begründen. Die App würfelt keine verdeckten Buffs.
2. **Automatisierung statt Intuition:** „Monster abilities are automated, following specific
   guidelines, so that very few decisions … need to be made by the party.“ (GH Rulebook S. 39)
   — Prioritäten sind fest kodiert, nicht zufällig.
3. **Schwierigkeit aus Spielstärke, nicht aus Material** (§6): die Skill-Skala S1–S5 dünnnt
   nur die Legalauswahl ein; Deploy-Kontingente bleiben ausschließlich Missionsdesign. Nie
   versteckte Statistik-Aufschläge.
4. **Der Mensch bleibt taktischer Entscheider, wo das Regelbuch einen Menschen hat**
   (Aufstellungswahl, Aktivierungsreihenfolge): die App schlägt vor mit Begründung und fragt —
   **aber nur, wo keine privaten Informationen leakens** (Prinzip 5).
5. **Observability-Filter (v3.2, neu):** Kein UI-Ausgabekanal (Frage, Log, Tooltip) darf
   **geheime Information** enthalten, bevor sie durch Spielzüge öffentlich wird. Geheim sind:
   Lage/Aktive-Liste der Aufstellungspunkte (bis eine Figur dort steht), Hand-/Reserve-Zusammensetzung,
   Spar-/Deploy-Absichten, Agenda-Pläne. Öffentlich sind: Threat-Pool-Stand, alles auf dem Brett,
   ausgeführte Instruktionen, Order-Deck-Ablage (bewusst offene Informationsquelle, §3.2).
   Im Coop-Modus sitzen beide Spieler am selben Bildschirm — jede Vorab-Preisgabe wäre ein
   Nachteil für die imperale Seite, selbst wenn sie regelkonform gewählt wäre.

---

## 1. Befund

### 1.1 Code-Evidenz IC2 (unverändert gültig, verifiziert gegen `a7bb921`)

| # | Benutzerbeobachtung | Code-Fundstelle | Befund |
|---|---|---|---|
| 1 | Fokusziel „random“ | `EnemyActivationPopup.FindRebel()` (Z. 360–385) | Zielwahl = **gleichverteilter Zufall** aus lebenden Helden, mit 65-%-Chance zuerst auf Trait-Präferenz (`preferredTargets`). Kein LOS, keine Distanz, keine Reichweitenprüfung. |
| 2 | Unfaire Boni | `ParseBonus()` (Z. 101–111), `bonuseffects.json` (90 Einträge), `instructions.json` | **65 % Bonus-Wurf pro Aktivierung** (z. B. `THE BIG GUN: Gain +1 {H}` DG014, `GRENADE LAUNCHER`), plus statische Aufschläge in Instruktionen (AT-DP „CHARGE GENERATORS: … gains +2 {H}“ DG046). Boni existieren auf keiner gedruckten Karte → **de-facto Schummeln**. |
| 3 | Mehrere Aktionen | `instructions.json` (91 Karten) | Siehe Regelfakt §1.2: 2 Aktionen sind regulär — erschummelt ist die **Unbegrenztheit**: Move-Distanzen ≠ Speed-Stat, Attacken über das Non-Hero-Limit, Keywords ohne Kartenblatt. |
| 4 | Positionierungsziel am Ende / widersprüchlich | `GetModifiedRepositioning()` (Z. 258–275) | „reposition X“ hinten im Block, per Regex umgeschrieben; kann ausgeführter Bewegung widersprechen. |
| 5 | Random-Aufstellung | `MapEntityManager.GetActiveDeploymentPoint()` Z. 466–468 | Mehrere aktive DPs → **gleichverteilter Zufall**. Kein Bezug zu Rebellen/Zielen/Deckung. Classic: Threat-Wheel (`RandomDeployment`). |
| 6 | Kein Missionsziel-Bezug | — | Instruktionen statisch pro Gruppe; Terminals/Türen/Objective werden taktisch nirgends modelliert. |
| 7 | Kein Aktivierungs-Modell | `hasActivated`-Flag | 1 Zufallsinstruktion pro Gruppe/Runde, unabhängig gewürfelt; kein Deck, keine Ablage, kein Regel-Aktivierungsmodell. |
| 8 | Verdeckte Elite-Aufstufung (neu, v3) | `DeploymentManager.DoDeployGroup()` Z. 122–152 | „Hard“-Difficulty: **15 % Chance, jede deployte Normal-Gruppe kostenlos auf Elite hochzuwürfeln**; „Easy“: Elite fällt mit 15 % auf Normal zurück, **ohne Threat-Rückerstattung**. Beides versteckt, ohne Kartenblatt-Grund → derselbe Schummel-Typus wie #2. |
| 9 | Threat wird sofort ausgegeben; Größen-Regeln nur halb vorhanden (neu, v3) | `MissionEventHandler` Z. 285ff., `DeploymentManager` „OPTIONAL DEPLOYMENT“; `DeploymentPointProps` | Deployment-Fenster geben die komplette Hand/Reserve sofort aus — kein Spar-/Tempo-Verhalten. Figuren-Größe (`FigureSize`, `incHuge` etc.) existiert **nur als DP-Filter in Saga-Missionen**; Innerraum-Verbot für Massive, Mehrfelder-Placement und Push-Regeln haben keine Validierung in Bewegung/Aufstellung (Legacy erlaubt AT-AT indoor). |

**Positiv:** Datenmodell sauber getrennt (`DeploymentCard` mit `speed/attacks/health/keywords`
aus `enemies.json`; `instructions.json` = Verhaltensdaten). Ein Planner kann auf regelkonforme
Unitwerte zugreifen — wir **validieren und neu kombinieren**, statt Daten zu pflegen.

### 1.2 Regelfakt aus dem offiziellen *Rules Reference Guide (Core Game)* — Korrektur zu v1

Die Annahme „imperiale Figuren hätten nur Bewegung + ein Angriff“ greift zu kurz. Wörtlich
(Reference Guide S. 4, ABSCHRIFT):

> **ACTIONS** — „During a figure's activation, it may perform two actions. The available actions
> are Attack, Move, Interact, Rest, and Special. … A figure can perform the same action multiple
> times during the same activation except as follows: — **A non-hero figure can use only one of
> its actions to perform an attack per activation.** — A figure can perform each special action
> only once per activation. … Movement points can be spent before or after performing an action.“

> **ACTIVATION** — „During each Activation Phase, each figure receives one activation. During a
> figure's activation, it performs up to two actions. … While activating a group of two or more
> figures, the player resolves two actions for each figure in that group … **in the order of his
> choice.** … During a campaign mission, Rebel and Imperial players **alternate** activating
> groups of figures. The Rebel players resolve the first activation each round.“

LotA-Buch bestätigt dasselbe Modell („that hero performs two actions …“). **Konsequenz:**
Tactical AI erzwingt das 2-Aktionen-Budget + Movement-Points = Speed-Stat + Non-Hero-Attack-Limit
(§2 Profil A). IC2s Verstoß gegen die Regeln ist nicht die Anzahl der Aktionen, sondern die
Unbegrenztheit und die Stat-Aufschläge.

### 1.3 Aktivierungsreihenfolge (Regelfakt für §3.4)

Innerhalb einer Gruppe: freie Wahl des imperialen Spielers → jeder deterministische Default ist
regelkonform; Thomas' Vorschlag **„Figuren mit Zielnähe zuerst“** wird Default. Zwischen den
Seiten: striktes Abwechseln, Rebellen zuerst (Regel, s. o.) — die Popup-Kadenz muss die
Gruppenwahl erst nach bestätigter Rebellen-Aktivierung nennen.

### 1.4 Aufstellung (Regelfakt für §3.6)

Reference Guide, DEPLOYMENT / CAMPAIGN DEPLOYMENT (Abschrift):

> „When the location for a figure's deployment is not specified, it is deployed **as close as
> possible to any active deployment point** shown in the mission's diagram, **chosen by the
> Imperial player**. … Green deployment points are active at the start … red, blue, and yellow …
> not until specified.“

→ DP-Wahl ist Spielerentscheidung, „as close as possible“ die einzige harte Grenze. Die Ask-UI
(§3.6) ist damit nicht nur erlaubt, sondern die regelgetreue Abbildung des imperialen Spielers.

### 1.5 Legends of the Alliance (Info, nicht Leitlinie — wie von Thomas vorgegeben)

LotA Rulebook: App wählt „a randomly selected Imperial group“ pro Aktivierung und diktiert die
Instruktion. IC2 imitiert genau dieses Modell und teilt damit LotAs Schwächen (Zufalls-Auswahl,
Diktion statt Entscheidung). Referenziert, nicht kopiert.

### 1.6 HotE / Jabba's Realm / Return to Hoth / FAQ

Bei den expansions-PDFs handelt es sich um Komponenten-/Content-Regelbücher (kein separater
Aktivierungstext), das FAQ (42 Aktivierungs-Treffer) wird in der Implementierung als
Feinschliff-Quelle konsultiert. Bestätigen: 2-Aktionen-Aktivierung, Interact-Aktion,
Strain-Bewegung **nur bei Helden**, Companion-Sonderregeln (blocken kein LOS). Keine neuen
Constraints gegen das Design.

### 1.7 „Reise durch Mittelerde“-EXE (Analyse abgeschlossen)

`Install-Reise durch Mittelerde-GooglePlayGames.exe` = **Google „ProdBrandedInstaller“**
(.NET-Mono-Assembly, Google-Play-Games-for-PC-Branding). nachweisbar: Installer-Gerüst +
Play-Store-Client-Bibliotheken; **kein Spielcode, keine eingebettete APK, keine Unity-/il2cpp-
Daten** — das Spiel wird zur Installationszeit über den Google-Account heruntergeladen.
Zusammen mit dem APK-Befund (Ad-Fake, s. v1 §1) war eine statische Analyse der JinM-Gegnerlogik
aus diesen beiden Artefakten nicht möglich. → **In v3 aber doch analysiert: s. §1.8.**

### 1.8 Die echte JinM-App — Verifikation & Analyse (v3, neu)

`~/Downloads/Journeys in Middle-earth/` enthält ein echtes Split-APK-Set:
**`com.fantasyflightgames.jime` v1.6.6** (Fantasy Flight Games, Unity IL2CPP, arm64) —
identifiziert über AndroidManifest + `libil2cpp.so` + `global-metadata.dat`. Die
Szenario-/Encounter-**Inhalte** werden erst beim Start geladen (95 MB Basis-APK), das
Ziel-/Bewegungs-Detail liegt in serialisierten PlayMaker-FSMs (inhaltlich nicht statisch
extrahierbar). **Die Architektur ist aber aus der IL2CPP-Metadaten-Tabelle vollständig ablesbar**
(`FFG.JIME`-Klassen: `EnemyLogic`, `EnemyActivationModel`, `EnemyBonusModel`,
`EncounterGroupModel`, `EncounterTraitsFilter`, `EnemySpawnPoint`, `CurrentEnemyReserveList`,
`ReservedEnemyIds`, `ThreatEvent`/`ActiveThreatEvents`/`AdventureThreat`, `UIThreatMeter`,
`GetRandomEnemyActivation`, `get_RemainingEnemyActivations`, `SpawnEnemyGroup`,
`DifficultyToggle`, Phasen `HeroPhase`/`ShadowPhase`). Konsequenzen für unser Konzept:

1. **Struktur-Bestätigung:** JinM = Reservoir-Modell (Begegnungs-Gruppen lagern in Reserve,
   Spawn über definierte Spawn-Points, Threat-Ereignisse lösen Nachschub aus, Aktivierungen pro
   Phase sind **kontingentiert**). Das ist genau die Schicht, die §3.9 nun formalisiert — JinM
   deployed auch nicht „sofort alles“, sondern phasen- und threat-getrieben.
2. **Bonus-Kanal, aber sichtbar:** JinM hat `EnemyBonusModel`/`ExtraEnemyBonuses` — Bonus-
   Mechanik existiert also auch dort, ist aber an **Threat-Stufen gekoppelt und für alle Spieler
   einsehbar** (Threat-Meter-UI). Für Tactical AI heißt das (§6): Boni bleiben Standard-AUS;
   der *einzige* erlaubte Bonus-Kanal ist eine **offene, pro Threat-Stufe veröffentlichte
   Tabelle**, die vor Missionsbeginn sichtbar ist — nie ein Wurf im Aktivierungspopup.
3. **Schwierigkeit als Toggle** (`DifficultyToggle`) statt versteckter Würfel: bestätigt §6
   gegenüber IC2s verdecktem Elite-Roll (§1.1 Um8).
4. **Focus/Targeting-Detail in JinM-Content** (AI-Karten: „Attack the closest …“ etc.,
   fsmbasiert): kein Widerspruch zu unserer GH-Focus-Prozedur — JinM nutzt dieselbe Logikklasse
   (kürzeste Distanz, LOS-Einschränkung, Sonderregeln je Karten-Eintrag). Kein Konzept-Wechsel
   nötig; die Verhaltensmuster-Tabellen (§3.5) bleiben die richtige Abstraktion.
5. **Belehrung, keine Kopie:** JinM würfelt die aktivierende Gruppe (`GetRandomEnemyActivation`)
   und teilt damit eine IC2-Schwäche; IA-Regel (alternieren, Wahl des imperialen Spielers) ist
   für uns Leitlinie (§1.3), JinM nur strukturelles Vorbild für Reserve/Threat/Spawn.

---

## 2. Regelprofile (Kern seit v2, in v3 unverändert)

Tactical AI besitzt ein **RuleProfile**-Objekt (pro Mission wählbar):

### Profil A — „Reference Rules“ (Default, offiziell)
Budget pro Figurenaktivierung:
- **Movement Points = Speed-Stat** des Kartenblatts (legale Mods nur vom Kartenblatt;
  Strain-Move nur Helden → für Imperiale nie).
- **2 Aktionen** aus {Attack, Move, Interact, Rest, Special}; Special nur, wenn auf der Karte
  (Kosten laut Icon-Zahl).
- **Attack-Limit:** Nicht-Helden: max. 1 Aktion mit Attacke pro Aktivierung — Ausnahme
  Kartenblatt (Beispiel Sentry Droid: „Multi-Fire: Perform 2 attacks …“ = EINE Special-Aktion —
  regulär, über `keywords` abgebildet).
- Figuren einer Gruppe aktivieren nacheinander (Einzelabwicklung), Reihenfolge: Default
  Zielnähe (§3.4).

### Profil B — „Order Cards“ (optional aktivierbar; eigene Fertigung — v3.1)

**Es existieren keine Overlord-Vorlagen** (Fan-Material liegt nicht bei und ist lizenzrechtlich
für einen MIT-Fork ohnehin ungeeignet). Profil B wird deshalb **selbst erzeugt** — sauber,
fairheitsbewiesen und simulativ kalibriert. Design:

1. **Fairness-Gesetz (strukturell):** Jede Order-Karte ist ein Aktivierungs-BLOCK, der gegen das
   Profil-A-Budget validiert ist (max. 1 Attack-Aktion bei Nicht-Helden, Move ≤ MP = Speed,
   Specials nur aus `keywords`). Profil B kann damit **mathematisch nie** über Reference-Power
   hinaus — Overlord-Feeling (Ziehen statt Plan) ohne Power-Fantasie. Der Profil-Name im UI:
   „Order Cards (Overlord-Stil)“.
2. **Deck-Generierung (datengetrieben):** Pro `DeploymentCard` (69 Gruppen in `enemies.json` +
   Villains-Datei) wird ein 8-Karten-Deck aus Bausteinen komponiert: `{Move+Attack}`
   (Vorstoß+Feuer), `{Attack}` (Feuerstellung), `{Move+Reposition}` (Stellung), `{Rest/Special}`
   (Kartenblatt-Specials wie Overwatch/Multi-Fire nur wo vorhanden), Shuffle-Icon auf 1–2 Karten,
   Elite/Villain-Decks mit mehr Spezial-Anteil. Ergebnis als `order_decks.json` (Data-Datei nach
   dem Muster von `instructions.json`, also ohne Code-Change pro Einheit wartbar).
3. **Kalibrierung (empirisch, §7):** Zielband **Ø-Karten-EV / Profil-A-EV ∈ [0.95, 1.05]** pro
   Archetyp bei gehaltener Spreizung (Zieh-Varianz ist das Overlord-„Gefühl“, der Mittelwert ist
   die Fairness). Spike verifiziert die Methode: naiver Mix = 0,59× A (zu schwach),
   6×{Move+Attack}+2×{Attack} = 0,96× A bei moderater Spreizung — die Search über Mix-Gewichte
   läuft der Simulator (§7.2) pro Archetyp, mit Hero-Modell-Rückkopplung (Positionsspiel der
   Rebellen, Cover, Overwatch) statt nur Roh-EV.
4. **Manuelle Veredelung:** Unique/Villains (Karten mit reichhaltigerem Text) bekommen
   von Hand mitgeschriebene Karten-Abschnitte; der Validator (§3.1) bleibt Endkontrolle.
5. **Integration:** Vollständig in der App wie jede andere Data-Datei: Deck-Zustand (Draw/Discard)
   wie `instructionOption` serialisiert; UI zeigt gezogene Karte als klassisches Aktivierungs-
   karten-Layout (Wiederverwendung `DynamicCardPrefab`); Legacy-/Profil-A-Pfade unberührt.
6. **Ehrliche Grenze:** „Ausbalanciert“ = kalibriert gegen unser Hero-Modell + Playtest-Protokoll
   (3 Missionen Handspiel mit Feedback pro Missions-Boot), nicht gegen einen externen Goldstandard.
   Beides zusammen ist das Bestmögliche und transparent dokumentiert.

### Profil C — „Legacy“ (bisheriges Verhalten, Default für bestehende Sessions)

Der **Validierer (§3.1)** prüft generierte Pläne gegen das aktive Profil — das Profil wechselt
nur die erlaubte Aktionendimension, nie die Redlichkeit.

---

## 3. Das Design: „Tactical AI“-Modus

### 3.1 Schicht A — Integritäts-Layer (Anti-Schummel-Garantie)

- **Single Source of Truth:** Werte/Fähigkeiten ausschließlich `enemies.json`
  (`DeploymentCard.attacks/speed/health/keywords`); der Planner addiert nichts.
- **Bonus-Effekte aus:** `bonuseffects.json` wird im neuen Modus nicht angesprochen.
- **ActionBudget-Validierer** (v2, gegen Regelprofil): `Anzahl Attack-Aktionen ≤ Profilregel`,
  `sum(Bewegung) ≤ MP = Speed`, `Specials ⊆ Karten-keywords`, keine Reposition ohne Kartenblatt.
  Ungültige Pläne → nächster legaler Plan, kein Reroll.

### 3.2 Schicht B — Order-Deck statt Aktivierungswürfel

**Order-Deck mit 8 Karten pro Deployment-Gruppe** (analog GH-Ability-Decks), generiert aus
Kartendaten:

```json
{
  "initiative": 24,
  "priorities": [ "move_if_out_of_range", "attack_focus" ],
  "special": "overwatch",
  "shuffle": false
}
```

v2-Korrektur: Order-Karten definieren **Prioritäten und Special-Einsatz**, nie Move-Distanzen —
Distanzen kommen ausschließlich aus Speed-Stat + Pfadlogik. **Ziehen aller Gruppenkarten
komplett zu Rundenbeginn** (§3.4.1-Nr. 6 — Entkopplung von der Aktivierungsreihenfolge), Ablage
bleibt liegen, Shuffle-Icon. **Warum fairer *und* schwerer:** Die Spieler lesen die Ablage und
antizipieren — Anspannung aus Lesbarkeit statt Random-Buffs.

### 3.3 Schicht C — Fokus- & Pfadlogik mit imperalem Siegermodell (v3.4-Umbau)

**Das Mensch-Argument (Thomas, 2026-10-26):** Ein Mensch fragt nie „wer ist am nächsten?",
sondern „**was ist mein Siegbedingung?**" — Auslöschen aller Helden → schwächster erreichbarer
Held; Rundenlimit überstehen → Rebellen am Ziel aufhalten, notfalls Gang versperren ohne
Angriff; Objekt schützen → Bedrohungen des Objekts priorisieren. Die Siegbedingung ist die
**Steuergröße**, Distanz nur die Randbedingung.

#### 3.3.0 `MissionVictoryModel` (Schicht C0, neu) — die Siegbedingung als First-Class-Eingabe

**Datenlage (verifiziert):** IC2 kodiert imperiale Siegbedingungen **nicht** als Enum, sondern
(a) `DataStore.sagaSessionData.gameVars.roundLimit` (Rundenlimit-Modus, Settings-UI „roundLimit")
und (b) Trigger-Events im Missions-Skript (Objekt-NPC zerstört → Defeat-Trigger, Terminal
aktiviert, Exit erreicht, Item aufgenommen). Das Siegermodell wird daraus **abgeleitet**:

| Posture (abgeleitet) | Zündlogik | Fokus-Implikation |
|---|---|---|
| **Eliminate/Attrition** | kein Rundenlimit, kein Schutzobjekt-Trigger | **VictoryCounter statt starrer Kill-Regel** (s.u.) |
| **Stall** | `roundLimit` aktiv | Priorität = Rebellen, die das Timer-relevante Ziel **bedrohen oder erreichen** könnten (Rennrechnung auf dem Pfadgraphen: Wer kann in verbleibenden Runden das Terminal/Item/Exit erreichen?); Angriffe nur wenn gerade behindernd; sonst Blockade des Engstellen-Knotens |
| **Protect** | Defeat-Trigger hängt an Entity (Wache, Tür, Terminal-Zustand) | Distanz-Achse kippt: bewertet wird Distanz des **Rebellen zum Schutzobjekt**, nicht zum Imperiale; Bedrohungsbeitrag (Reichweite + Bewegung + Schaden gegen Objekt) statt Nähe |
| **Deny** | Missionsziel = Terminal/Item/Exit für Rebellen unerreichbar halten | Intercept-Rennlauf: kürzeste Ankunftszeit zum umkämpften Knoten; Body-Block auf dem Knoten selbst (legale Bewegung, §3.5 Blocker) |

**`VictoryCounter` (v3.5, Thomas-Korrektur):** Siegbedingung ≠ immer „kampfunfähig". Viele
Missionen gewinnt die Hand schon, wenn **alle Rebellen nur verwundet** sind (Schaden ≥1);
andere verlangen Kampfunfähigsetzung, Gefangennahme oder schlicht Rundenüberstehen. Der Zähler
wird pro Mission aus Triggern/Text abgeleitet (Custom-Override per JSON) und definiert eine
**Zielwertfunktion ΔZähler pro Runde**:

- **woundAll:** ein **gesunder** Rebelle, der jetzt Schaden nimmt, rückt den Zähler
  unwiderruflich weiter → er ist das wertvollere Ziel als der nähere **bereits verwundete**.
  Thomas' Beispiel, 1:1 kodiert.
- **incapacitateAll/capture:** Focus-Fire auf killbaren/fangbaren Rebellen (sichtbare
  Vitalität ≤ erwartetem Schaden — Ø-Pips gedruckter Würfel, §1.1, nur öffentliche Daten);
  sonst höchster Schadens-/Unterstützungs-Beitrag pro Runde (Heiler > Hammer > Rest).
- **Ausnahme regiert über den Zähler (Thomas):** Ein Ziel, das eine **Rebellen-Siegbedingung
  auslösen oder blockieren** kann (Item-Träger, am Terminal, vor dem Exit), sticht jede
  Zähler-Progression — erst der Gewinnverhinderer, dann der Zähler.
- **Kampagnen-Meta (optional, Default an im Kampagnenmodus):** Kampfunfähig = „Verwundet"-Status
  in der nächsten Mission; einen fast erledigten Helden fertigzumachen hat Meta-Wert.
  Deterministisch als fester Bonus im ΔZähler, nie verdeckt; im Szenario-Modus aus.

**Posture-Wechsel deterministisch & öffentlich:** Schwellen (RundenRest vs. Limit, Objekt unter
Angriff, Item aufgenommen) verschieben die Posture mid-Mission; jeder Wechsel erscheint im Log
(„Missionslage: Terminal gesichert → Posture Protect") — alles öffentlich ableitbar,
Prinzip 5 bleibt gewahrt (keine versteckten Werte; Vitalität ist in IA offen).

#### 3.3.1 `FocusResolver` — zweistufig: App wählt **Engagement-Doktrin**, Brett löst Position

Grundmodell (positionsblind, null Buchführung): Die App gibt **keine Hex-Zielsprüche** aus,
sondern eine **Doktrin** = geordnete, deterministische Zielregel + geographisches Bewegungsziel
(„Attackiere den nächsten verwundeten Rebellen; sonst: wer Terminal T berührt; sonst: rücke zur
Reaktor-Tür vor und halte Reichweitenrand"). Der Mensch am Brett ist der **Aktuator**, der die
Regel lokal anwendet — exakt wie ein Overlord-Spieler es täte. Die Doktrin-Auswahl (welche
Regel, welches geographische Ziel) ist der taktische Kern und kommt aus Posture/VictoryCounter
(§3.3.0) + Pattern (§3.5) + Missionsgeometrie (§3.3.3).

1. **Pfadgraph** (Saga: `TileManager`/`MapEntityManager` liefern Wand-/Tür-/Geländedaten;
   Classic: Missions-Kachelplan). Er modelliert **Möglichkeiten** (Wer-kann-Was-erreichen-Räume),
   nicht Ist-Positionen: „von welcher Tür aus ist Terminal T in 2 Runden erreichbar?".
2. **Doktrin = argmax über Kandidaten-Regeln** (Wer-kann-Was-erreichen-Bewertung statt
   Distanz-Dreieck): Zielformulierung (nächster / verwundeteste / gesunde / Item-Träger /
   Objekt-Berührer) + Bewegungsgeographie (Konten-Punkt: Tür, Engstelle, Objekt) — alles
   formuliert in **Referenzen, die am Brett ablesbar sind**. Keine erfundenen Positionen.
3. **Brett-Tiebreak:** Gleichstände löst der Mensch mit der veröffentlichten Kaskade (§3.3.2) —
   die Doktrin-Karten drucken sie als Fußzeile, Verhalten bleibt lernbar wie bei GH-Monstern.
4. **Path Priority als Doktrin-Klausel:** „Overwatch-Felder meiden außer alternativlos" bleibt
   als Regeltext dabei (der Mensch sieht die Overwatch am Brett).
5. **Movement-Instruktionen = geographische Vorgaben + legale Grenzen:** „Bewege bis Speed 4
   Richtung Reaktor-Tür, stoppe am maximalen Reichweitenrand zum Terminal; innenbetretende
   Felder verboten (§3.8)." Detail-Position wählt der Mensch innerhalb der Legalität — die
   App prüft nur, was sie wissen kann (Größen/Interior, MP-Grenze, Legalität der Referenz).

#### 3.3.1a `HeroThreatProfile` — Zielwert aus dem gedruckten Kartentext (v3.10, Thomas)

**Idee (Thomas):** Fähigkeiten, Waffen und Ausrüstung der Helden sind am Tisch **öffentlich** —
eine menschliche imperiale Hand liest, was sie vor sich sieht. Das Wissen darf die AI auch
haben: kein Hidden-Info-Verstoß (das Profil hängt an der Helden-**Identität**, nicht am
Zustand/Sicht — Leak-Invariante bleibt). Da `heroes.json` keine Würfelwerte führt, ist das
Profil eine **kuratierte, öffentliche Asset-Tabelle** im Repo
(`Assets/Resources/Languages/En/tactical_hero_profiles.json`, Disclaimer wie beim
Letalitätsprior; identische Ehrlichkeits-Politik, Mensch liest dieselben Karten).

**Profil je Held (statisch, Setup-roster-basiert):**
- `role ∈ {damage, support, tank, control, scout}` (gedruckte Karteneignung)
- `evDamagePerRound` — erwarteter Schaden/Runde aus gewürfelten Angriffswürfeln + Surge-Rate
  (kuratierte Konstanten, dokumentiert)
- `rangeMax`, `ignoreLos` (Angriffe brauchen keine Sichtlinie), `selfHealPossible`,
  `mobility` (Bewegung/Manöver-Kanten), Besonderheiten-Flags (Konditionsresistenz, Stealth…)

**Zielwert W(h) = Posture-Gewicht × Profil (FocusResolver-Stufe 1, §3.3.1):**
- **woundAll:** Chip-Wert = erwartete Verwundungen bis „held_hurt"; **Non-Healer** hoch gewichtet
  (Kampagnen-Verwundet-Effekt persistiert; Heilerin zuerst verwunden ⇒ gegnerische
  Regeneration fällt aus — doppeltem Ertrag), **Support-Rolle** priorisiert.
- **protect/stall:** Erster = wer das Objekt als erster erreicht (mobility × rangeMax),
  Schadensbeitrag zweitrangig; Tank-Körper, die blockieren, bleiben relevant.
- **attrition:** höchster `evDamagePerRound` zuerst ( Uhr gegen uns — Rennen).

**Kopplung an RiskPosture (§3.5.1):** der grobe Letalitätsprior wird durch das Profil ersetzt.
**Rückzugswert ist flag-abhängig:** gegen `ignoreLos`/Großreichweiten-Helden ist Deckung
wertlos ⇒ `disengage` verliert seinen Nutzen (Posture-Rechnung kippt zu `press`/Tausch oder
Objektspiel); gegen reine Nahkämpfer ist Rückzug volle Wirksamkeit. Eine AI, die vor einem
Scharfschützen in Deckung hechtet und dort standhaft stirbt, ist genau die Dummheit, die wir
abschaffen.

**Ausrüstungs- & Fähigkeitskarten (v3.10b, Thomas):** Würfel und Boni stehen auf den
Fähigkeits- und Ausrüstungskarten — das Profil rechnet deshalb in Schichten:
`EV/Runde = Basis-Angriffswürfel ⊕ Fähigkeitskarten-Würfel ⊕ Waffen-/Ausrüstungsboni ⊕
Surge-/Crit-Konverter`. Die Würfelwerte je Karten-**ID** kommen aus einer zweiten kuratierten
öffentlichen Tabelle `tactical_card_dice.json` (Gedrucktes abtippen, Disclaimer, Repo-Asset —
Mensch liest dieselben Karten). **Erkannte Lücke:** Die App erfasst Held-Loadouts nicht
(Kampagnen-Kartenauswahl passiert am Tisch; `CardData/` führt keine Item-/Skill-Tabellen —
verifiziert). Deshalb zweistufig:
**Wissensquelle ist die App selbst (v3.11-Korrektur, Thomas — verifiziert):** Die
Kampagnenverwaltung ordnet Helden Ausrüstungs- UND Fähigkeitskarten zu und speichert das:
`sagaCampaign.campaignItems`, je Held `hero.campaignItems`
(Screens/CampaignManager.cs:229/358), Skill-Kauf zieht `skill.cost` vom `xpAmount`
(CampaignHeroPrefab.cs:80), Datenbestände `CampaignData/{items,skills}.json` je Sprache, Modell
`CampaignItem { tier, type, cost }` / `CampaignSkill`. **Ein Opt-In-Schirm ist damit
überflüssig** — die AI liest, was die App selbst verwaltet (AppView-Eigenschaft,
Leak-Invariante unberührt; die Kartenwerte sind ohnehin öffentlicher Print).
**Bewertungs-Proxies (Thomas-Vorgabe, sprachunabhängig robust statt Description-Parsing):**
Ausrüstung gewichtet nach `tier` (1–3) × `cost` nach `type` (Waffe → EV-Angriffsschicht,
Rüstung → Effektiv-Widerstandsfähigkeit, Sonstiges → Flag/Hint); Fähigkeiten gewichtet nach
Erfahrungspunkt-Kosten (teuer = wirkungsvoll; `xpCost`-Summe als Fähigkeits-Investitionsindex).
EV/Runde = Basis-Angriff (kuratierte Minimal-Tabelle je Held, gleiche Disclaimer-Politik) ⊕
Waffen-Tier-Kosten-Beitrag ⊕ Fähigkeits-Investitionsbeitrag. Eine optionale Verfeinerung
(kuratierte Würfel-Tabelle je Karten-ID) kann später additiv ergänzt werden — nicht nötig,
nicht blockierend.

**Skill-Bändigung ohne neuen Parameter:** Ranking-Qualität läuft über das bestehende δ
(Fokus-Band): S1 wählt mit billigen Schlüsseln (Nähe/zuerst gesehen) innerhalb des Bandes,
S5 mit vollem Profil-Score; Tiebreaker-Kaskade (§3.3.2) unverändert. Seed-deterministisch.

#### 3.3.1b Passiver Welt-Feed — was M5 wirklich ist (v4.0, Thomas-Entscheid 27.10)

**Entscheid:** Der geplante Upgrade-Pfad „Hex-World-Adapter / WorldProvider-Registrierung“ ist
**verworfen**. **Begründung:** In der App existiert kein Hex-Weltmodell. Figuren sind frei
ziehbare Token-Pixel in Container-Hierarchien, Kacheln haben 2D-Position und Biotyp — die
Hex-Legalität adjudiziert der Mensch am Brett. Hex-Wahrheit in die App zu holen hieße entweder,
der Mensch müsste Positionen und Einheiten melden — daraus wird sofort **Arbeit statt Spiel**
(verboten; dieselbe Regel, die wir bei der Schadensbuchführung angewandt haben: nur Signale, die
automatisch entstehen) — oder die aufgemalten Hexnetze aus Pixelgeometrie zurückzurechnen:
fragil, fehleranfällig, und als Rechtsbasis für Bewegung-/Reichweiten-Checks ohnehin untauglich.

**M5 konsumiert deshalb ausschließlich Signale, die beim Spiel ohnehin automatisch entstehen.**
Kanon-Regel (neu, gilt ausnahmslos): *Kein Feature darf jemals vom Menschen verlangen, der App
Positionen oder Zustände zu melden.*

1. **Kachel-Graph:** `MapTile.entityPosition` + Kachelmaße → Adjazenzgraph mit Biotyp-/Interior-
   Heuristik (Offener Punkt 3 bleibt). Modelliert Möglichkeitensräume (Wer-kann-Was, §3.3.3),
   nie Ist-Positionen.
2. **Token→Region (passives Lesen, null Eingabe):** Figuren werden mit Pixelpositionen
   gerendert; die Zuordnung „Token liegt auf/in Kachel R“ wird daraus automatisch abgeleitet.
   Das ist dieselbe Information, die der imperiale Spieler am geteilten Bildschirm ohnehin
   sieht (Leak-Invariante: Brettpositionen sind öffentliche Information; verdeckte Daten —
   Held-Karten, Würfel, MedPacks — bleiben gesperrt, §7.1).
3. **Ereignisstrom:** Schadensherde und Rad-Zustände (echte Rest-Vitalität §3.5 → füllt die
   `vitalityFrac`-Lücke), Fokus-/Engagement-Historie, Objektwechsel (`OnChangeObjective`),
   Missionsentitäten (Terminals, Kisten, Informanten), Threat-/Deploy-Ökonomie, Rundenlimit —
   alles fließt heute schon durch bestehende Hooks oder ist read-only zugänglich.
4. **Posture aus Missionseigenschaften:** Zieltexte und Sieges-Events (Punkte fangen? Halten?
   Eskortieren?) leiten die Posture ab; `eliminate` ist nur noch Fallback, nicht mehr Fixwert.

Unverändert bleibt: Der Doktrin-Layer bleibt im **Ausgabeformat** positionsblind
(Instruktionstext, Mensch ist Aktuator, §3.3.1) und Hex-Wahrheit lebt nur im Simulator für
Metriken weiter (§7 Zwei-Ebenen-Architektur). **Overlord und Reference sind auch ohne Hex
trennbar:** derselbe Feed, unterschiedliches Optimierungsziel — ORD = argmax über das
Scoring-Modell auf Zonen-Beliefs, REF = gedruckte Prioritätsregel je Karte. Instanz-Keying
(doppelte Karten-IDs) und DoS-Deckel (Großbretter) sind Unterpunkte dieses Meilensteins.

**Realisierungs-Hinweis (M5, v4.2):** Der Seam ist die **`TacticalAIBridge`** — ein `CoreApi`
existiert im App-Repo NICHT (ältere Entwurfs-skizzen nannten `CoreApi.IngestWorldEvents`; überholt).
Kern: `Core/WorldEvents.cs` (append-only Event-Historie, Payload-Whitelist nur öffentlicher
Informationen, seq-Nummern, Hash) + `Core/WorldEventVector.cs` (deterministische Projektierung →
`FeedWorldView`: Posture-Ableitung aus Missions-Eigenschaften mit Wortstamm-Präfix-Heuristik,
Vitalität aus beobachteten Rädern, Fallback `eliminate`). Der Adapter `WorldEventsUnity.cs` greift
bestehende App-Signale automatisch ab (HPTracker-Räder, `HeroState.isWounded`-Flips,
`SagaDGPrefab.OnDefeat`, Objekt-/Fokus-Klicks, Threat-Spend) und wird zur Laufzeit vom
Bridge-Sitzungsstart angehängt — keine Prefab-Chirurgie. Vor jeder Entscheidung zieht die Bridge
den Feed-Stand; Lücke in der seq ⇒ **Resync = Re-Log der kompletten Historie** (kein Schätzen).
Kanontest: `Assets/Tests/Fixtures/tacai/world_feed.json` (Harness + NUnit spiegelgleich).

#### 3.3.2 Tiebreaker-Kaskade (nur bei Gleichstand in W; in UI begründbar)

1. fewest movement points (GH) → 2. Distanz → 3. Missionsrollen-Gewicht (§3.5) →
4. frühere Aktivierungsreihenfolge → 5. geringere Vitalität. Die Kaskade ist bewusst
GH-geerbt: sie macht Gleichstände **vorhersagbar** — die Spieler können lernen, wie die App
bricht, genau wie bei Gloomhaven-Monstern. Die Posture-Wertfunktion entscheidet den Kopf,
die Kaskade nur den Gleichstand.

#### 3.3.3 Schicht C-minus — Theater-Modell: Strategie ohne Positions-Buchführung (v3.6, ersetzt Board-Mirror)

**Verifizierter Befund:** IC2 kennt **keine** Figurenpositionen (`MapEntityManager` = nur
Map-Entities; Instruktionen zielen namentlich via `{R1}`, `EventPopup.cs:121`). Ein Sync-Zwang
(Board-Mirror v3.5) wurde von Thomas verworfen: **Positions-Tippruiniert den Spielspaß** —
ein Brettspiel darf keine Buchführung verlangen. Neue Leitlinie: **Null zusätzliche Eingaben.**

**Das Theater-Modell:** Die App denkt in **Räumen, Türen, Objekten und Möglichkeiten**, nicht in
Ist-Positionen. Was sie sicher weiß: Missionsgeometrie (Saga digital; Classic: Kachelplan des
Missionsdiagramms), DP-Set, Objekt-/Terminalpositionen, Rundenstand, Threat, Missions-Ereignisse.
Missions-Ereignisse pflegt die App **ohnehin** (Terminal aktiviert, Tür geöffnet, Item genommen,
Rundenende — alles existierende UI-Buttons, keine neue Pflicht): diese Ereignisse sind
geographische Fingerabdrücke („Terminal T ist aktiv → Rebellen waren an T und werden flüchten
oder verteidigen") und schieben die **Zonen-Beliefs** — Wahrscheinlichkeits-Schwerpunkte pro
Zone, nie Behauptungen über Einzelfiguren.

1. **Glaubwürdigkeits-Regel:** Beliefs steuern ausschließlich **Was-kämpfen-Wo-kontrollieren**
   (Doktrin-Auswahl §3.3.1) und die Reihenfolge-Planung (§3.4.1). Keine Instruktion, kein Log,
   kein Tooltip behauptet eine Einzelfigur-Position. Das ist dasselbe Wissen, das ein Mensch
   ohne Hinschauen aus Ereignissen zieht — kein Schummel, keine Überwissen-Fantasie.
2. **Freiwillige Signale, nie Pflicht:** Wer dem Imperiale-Overlay antippt „Rebellen waren hier",
   schärft die Beliefs (optional, ein Tip). Bleibt es leer, spielt die Logik auf reinem
   Ereignis-Grund. Imperiale-Figuren: die App kennt ihre Deploy-Felder selbst; Abweichungen von
   eigenen Instruktionen sind dem Brett egal, weil die Doktrin relativ formuliert ist
   („rücke zur Tür vor" funktioniert von fast jedem Standpunkt).
2b. **Aktivierungs-Inferenz-Kanal (v3.8b, Thomas; verifizierte App-Zustände):** Fünf Signale,
   die die App **für den Spielverlauf ohnehin führt** (Thomas-Runde: „das nutzen wir auch") —
   null Zusatz-Buchführung:
   - **Per-Figur-Schadensräder:** `HPTrackerContainer` legt pro Gruppenkarte **einen `HPTracker`
     pro Figur** an (`i < card.size`), Werte persistiert in `DeploymentCard.woundTrackerValue[]`,
     Figuren-Identität über `trackerNumbers[index]` (Saga/Common/HPTracker*.cs) ⇒ die App kennt
     Schadens-**Deltas pro Einzelfigur**.
   - **Helden-Marker-Flips:** `HeroState.hasActivated[]` via Hero-Toggles
     (`SagaHGPrefab.cs:109/188`); die App sieht die Flip-Folge innerhalb der Runde (eigene
     State-Changes sind sequenzierbar) ⇒ Attribute-Fenster pro Held.
   - **Helden-Verwundet-Flips:** `HeroState.isWounded/isDefeated` (`Models/HeroState.cs`),
     Events `useHeroWounded/useAnyHeroWounded/useAllHeroesWounded` (`MissionEvent.cs:14-33`)
     ⇒ Held Kippt auf verwundet **während imperiale Aktivierung X läuft** ⇒ X' aktivierende
     Figur lag in Reichweite des Helden ⇒ Gegen-Ring um die Soll-Position von X; gleichzeitig
     Zähler-Progress für woundAll-VictoryCounter (§3.3.0) — öffentliche Information.
   - **Imperiale-Niederlagen:** `SagaDGPrefab.OnDefeat()` ⇒ ringförmige Held-Nähe um Soll-
     Position der gefallenen Figur zur Aktivierungszeit der Runde.
   - **Instruktions-Ledger:** Soll-Positionen der eigenen Gruppen (App-Autorität, Nr. 2).
   **Attributions-Regel (deterministisch):** Schadens-Delta einer Figur F in Runde R ⇒ Held aus
   dem zeitlich letzten Aktivierungs-Fenster vor dem Delta; mehrere Helden im Fenster ⇒
   Ring-Belief über allen, gewichtet nach Reichweiten-Schnitt. Delta ohne Held-Fenster ⇒
   Overwatch/Sonderfall, kein Held-Ring. Alles bleibt **Ringe (Bänder), nie Punkte**; der
   ganze Kanal ist reiner AppView-Bestand ⇒ Leak-Invariante unverändert.
   **Seiteneffekt für §3.4.1:** die Per-Figur-Schadensräder geben dem OrderPlanner die echte
   Abnutzungs-**Stärke** seiner Gruppen (Stärke-Effizienz-Matrix rechnet mit gedrucktem Wert
   × verbleibender Vitalität — genau wie ein Mensch das tut).
3. **Doktrin statt Positionsplan:** Ausgabeformat bleibt das heutige Instruktions-Popup —
   Karten-Text mit Determinismus-Fußzeile. Der taktische Wert sitzt in der **Auswahl** der
   Doktrin und des Konten-Punkts (Siegermodell!), nicht in Hex-Genauigkeit. Legacy-UX bleibt
   damit 1:1 erhalten: dasselbe Popup, klügerer Text.
4. **Was entfällt gegenüber v3.5:** Sync-Dialog, Frische-Tracking, Blind-Modus als Sonderzustand
   (es gibt nur noch einen Modus: positionsblind-by-design), Mirror-Serialisierung.
   **Was bleibt:** alle Legalitäts-Checks, die die App auch ohne Positionen prüfen kann
   (Größen/Interior bei Deploys §3.8, MP-Grenzen als Anweisungstext, Specials⊆keywords).
5. **Belief-Verfall:** ohne Ereignisse zerfallen Zonen-Beliefs gleichmäßig zur
   Missions-Start-Prior („Rebelle startet bei Eingang E, Ziel T → Korridorchancen"); der
   Zerfall ist deterministisch (halbiere pro rundenloser Periode) und damit Teil des
   balancierbaren §7-Modells.

### 3.4 Schicht D — Reihenfolge der Aktionen + Aktivierungsreihenfolge

Blockaufbau pro Figurenaktivierung (invariante Reihenfolge, Fix des Widerspruchs-Problems):

```
[1] Before-Abilities (nur Kartenblatt)
[2] Move — Zielhex + Begründung, VOR Bewegung sichtbar (MP = Speed)
[3] Attack/Special — Ziel + Würfel aus Focus-Logik (Budget nach Regelprofil)
[4] After-Abilities (nur Kartenblatt; Reposition hier und nur mit Kartenblatt)
```

**Aktivierungsreihenfolge (v3.8-Aufstieg vom Score zum Planer — Thomas' Hauptkritik an der
Zufallsreihenfolge der alten App):** Der imperiale Spieler **darf** laut Regeln jede Figur
seiner Stufe in beliebiger Reihenfolge aktivieren (alle, einmal; IA Reference „Activation"):
Reihenfolge ist reine **Entscheidungsqualität** — genau die Schach-Ebene, die die alte App dem
Zufall überließ. §3.4.1 ersetzt das.

#### 3.4.1 `OrderPlanner` — die Runden-Reihenfolge als taktischer Plan (v3.8, neu)

Einen Plan pro imperialer Stufe, deterministisch aus:

1. **Stärke-Effizienz-Matrix:** je Gruppe ihr gedruckter Wert (Schaden/Reichweite/HP aus
   Kartendaten) × aktuellem Einsatzwert aus Posture + Pattern + Beliefs (§3.3.3 inkl.
   Aktivierungs-Inferenz §3.3.3-Nr. 2b): Wer kann **jetzt** am meisten bewirken (Zielreichweite,
   Konten-Punkt-Nähe, Helden-Ringe in der Nachbarschaft)?
2. **Synergie-Ketten:** Hard-Slots in der Reihenfolge — Buff-Geber (Officer/Command) **vor**
   ihren Empfangs-Angreifern; Blocker **vor** den Angreifern, deren Ziel sie festhalten;
   Deploys **vor** Intercepts, die sie brauchen; Finisher **zuletzt** (Sicht auf Vorschäden).
3. **Tempo-Regel:** wer zuerst zieht, definiert die Geometrie des Rests (schwere Einheiten
   zuerst, wenn Blockade-Sicht frei; leichte Schwarm-Einheiten nach den Heavy-Schüssen, wenn
   diese Türpositionen klären).
4. **Skill-Bändigung (§6):** S1 = Pattern-Standardreihenfolge ohne Kettenoptimierung (legal,
   halt planlos — das alte App-Niveau, wiedererkennbar); S3 = Synergie-Slots + grobe
   Stärke-Effizienz mit seediertem Aussetzer; S5 = volle Ketten-Optimierung inkl.
   Finisher-Positionierung. Nie: Gruppe wird übersprungen oder doppelt aktiviert (Pflicht-
   Invariante: **jede lebende Gruppe genau einmal pro Stufe**, Plan ist Permutation, kein Filter).
5. **Ort des Plans:** Rundenbeginn, vor der ersten Aktivierung, als „Reihenfolge-Karte" im Log
   mit Begründung („Officer → Sturmtruppler → AT-ST: Buff vor Angriff; AT-ST zuletzt, weil
   AT-AT-Blockade erst Tür 2 frei schießt") — öffentlich, nachvollziehbar, replizierbar.
6. **Order-Deck-Profil (§2 B) entkoppelt (wichtig):** Karten werden **komplett zu
   Rundenbeginn** gezogen und fest zugeteilt (nicht erst bei Aktivierung!) — sonst könnte die
   Aktivierungsreihenfolge Zugriffe auf die Deck-Reihenfolge erlauben (Deck-Manipulation =
   Regelbruch) und der Plan wäre erpressbar. Ablage bleibt öffentlich.

### 3.5 Schicht E — Missionsziel-Bewusstsein & Verhaltensmuster

**Objective-Tag-Modell:** Map-Entities mit Rollen (`terminal`, `door_key`, `item`,
`objective_hex`, `exit`, `guard_zone`, `important_npc`), default abgeleitet aus bestehenden
Triggern/`MissionEventHandler`-Events (Terminal aktivieren, Item aufnehmen, Tür öffnen); Custom
Missions können Gewichte per JSON setzen.

**Verhaltensmuster pro Unit-Typ** (`tactical_behaviour_patterns.json`, default pro `DG###`,
je Mission überschreibbar; Frosthaven-Vorbild):

| Pattern | Verhalten | IA-Beispiele |
|---|---|---|
| Aggressive | Focus-Regeln pur (kürzester Angriffspfad) | Scouttrooper, Sith Acolyte, Rancor |
| Guard | Bewegung nur in `guard_zone` um Objekt; Pursuit erst bei Bedrohung | Stormtrooper am Terminal, Sentry Droid |
| Blocker | positioniert sich auf Schlüsselknoten des Rebellen-Kurzpfads (Tür, Engstelle) zwischen Rebellen und Ziel | Stormtrooper Captain, Imperial Guard |
| Hunter | priorisiert Rebellen mit Objective-Token/Item/Geleit-Charakter | Darth Vader, Trandhaner mit Orders |
| Support | hält Distanz ≥2, LOS auf verwundetesten Imperiale; heilt/bufft nur per Karte | Officer (Command), Medical Droid |
| Ambusher | bewegt verdeckt in Position; nutzt Hidden/Out of Sight wie Karte | Weequay mit Ambush |

Pattern überschreiben **nur Tiebreaker und Bewegungsfesseln**, nie legale Aktionen. Damit sind
„Terminal/Tür versperren“ und „wichtigen Charakter jagen“ regelkonform abgedeckt — ohne Zufall.

### 3.5.1 RiskPosture-Layer — „Angriff oder Deckung?" für verletzte Einheiten (v3.9, Thomas)

**Problem:** Eine verletzte Figur anzugreifen statt sie zu schützen, ist genau die
Menschen-Abwägung, die die alte App nie traf. Aber die App SIEHT das Brett nicht — keine
Sichtlinien, keine Deckungs-Felder am Tisch. Lösung: Die Abwägung passiert **ohne**
Sichtlinien-Wissen, auf AppView-Größen, und die Ausführung bekommt **Fallback-Zweige**, die
der Mensch mit echten Sichtlinien auflöst.

**Eingaben (alle verifiziert AppView):** Schadensstand je Figur (woundTrackerValue = Wundrad-Wert = **erlittener Schaden**, 0 = unbeschädigt — Thomas-Brettwissen 2026-10-27, schließt M7-F4; abgeleitete Restvitalität = gedruckte Health − Schadensstand, §3.3.3-2b);
Helden-Ring-Beliefs (Nähe-Band); **Letalitätsprior** je Held (kuratierte Prior-Tabelle aus
gedruckten Kartenwerten — `heroes.json` enthält nur Indexfelder, also gleiche Ehrlichkeit wie
der Grey-Dice-Proxy bei Villains, dokumentiert); Posture + VictoryCounter (§3.3.0);
Threat-Ökonomie (§3.9: imperiale Einheiten sind ERSETZBAR — Helden im Kampagnenmodus nicht
(Verwundet-Tragik)).

**Risikopositionen pro Gruppe & Runde (Selection-of-Doctrine, keine neuen Regeln):**

| Position | Wann (deterministische Schwellen) | Doktrin-Ausgabe (Text mit Fallback-Zweig) |
|---|---|---|
| **press** | Vitalität hoch ODER woundAll-Zähler braucht Schaden ODER Ziel ist Siegverhinderer | „Attackiere FOKUS; falls außer Reichweite: rücke auf Konten-Punkt vor" (heutiges Verhalten) |
| **hold** | verletzt, aber Ring-Belief leer (kein Held in mutmaßlicher Nähe) | „Halte Position, lade/nimm Ziel ins Visier, attackiere nur bei Gelegenheit" — nur Karten-legal |
| **guard_hold** | verletzt + Schutz-/Stall-Posture + Objekt- oder Engstellen-Dienst wichtig | „Verbleib am/bis zu Referenzpunkt C (Tür/Objektkante) und verwehre; attackiere nur, wenn ein Ziel legal erreichbar ist" |
| **disengage** | Restvitalität ≤ Schwelle × Ring-Belief eng × Einheit ökonomisch wertvoll (ersetzen teuer) × Posture erlaubt Verlust nicht | „Ziehe dich zu Referenz-Deckungspunkt D zurück (Kanten-Referenz aus Missionsgeometrie), max Speed; attackiere unterwegs nur falls legal und lohnend" |

**Warum das ohne Brett-Sicht funktioniert:** Die Schwellen rechnen mit **Bändern** (Ring=
Wahrscheinlichkeits-Nähe), nicht mit Sichtlinien; der **Fallback-Zweig** jeder Doktrin macht
die Unsichtbarkeit zum Menschen-Entscheid: „falls nicht in Reichweite" sieht NUR das Brett.
Die App plant beide Äste im Voraus — wie ein Mensch, der sagt „greif an, und wenn sie nicht
da sind, zieh dich zur Tür zurück". Referenzpunkte C/D kommen aus der Missionsgeometrie
(Türen, Wände, Objekt-Kanten als benannte Anker) und sind am Brett ablesbar.
**Skill-Bändigung (§6, neuer Parameter ρ):** S1 = immer press (die alte Gier — Verwundete
sterben lassen, wiedererkennbar); S3 = guard_hold/disengage nur bei klarer Schwelle; S5 =
volle Abwägung inkl. ökonomischem Einheitswert und Kampagnen-Vitalität. ρ ist ebenfalls reine
Auswahlverdichtung (Optionsmenge identisch), seed-deterministische Unsicherheit über Ringbreite.
**Harte Grenzen:** keine erfundene Deckungs-Regel (IA-Deckung ist Karten-/Geländethema — die
Doktrin nutzt nur Bewegung + Zielwahl), kein überspringen von Aktionen über Fallback-Klauseln
hinaus, Prior-Tabelle = öffentliches Asset im Repo.

**Verhältnis zur Posture (§3.3.0, v3.4):** Die Posture ist **missionsstrategisch** (gelten alle
Gruppen), das Pattern ist **unit-taktisch** (Bewegungsstil einer Gruppe). Posture setzt die
Wertfunktion W und kann Patterns hart umschalten (Stall befiehlt auch Aggressive-Gruppen zur
Blockade des Timer-Knotens, wenn freie Knoten knapp sind — Begründung im Log). Grenzen bleiben
absolut: Weder Pattern noch Posture erfinden Aktionen, Reichweiten oder Boni.

### 3.6 Schicht F — Secrets Deployment (v3.2-Korrektur: Ask-Modus gestrichen)

**Der Denkfehler in v1–v3.1:** Ask-Dialoge („DP Nord oder Süd?“) verraten die Aufstellungspunkte
und verletzen Prinzip 5 — im Coop sitzt der Rebellenspieler am selben Bildschirm. Die
Regelfakt-Basis (§1.4: DP-Wahl = Entscheidung des imperialen Spielers) bleibt richtig; die
Abbildung als *öffentliche Frage* war falsch.

**Datenlage (verifiziert 2026-10-26):** Saga-Modus kennt **jede** Mission digital: 138 Missions-
JSONs mit zusammen **1660 `DeploymentPoint`-Entities** (`entityType=3` laut `EntityType`-Enum in
`Interfaces-Enums.cs`: Tile=0, Terminal=1, Crate=2, **DeploymentPoint=3**, Token=4, Highlight=5,
Door=6) inkl. `entityPosition`, `deploymentColor` und `DeploymentPointProps` (Filter/Größe).
**Classic-/Kampagnen-Modus: Board physisch, keine DP-Geometrie in den Daten.**

**Neues Verfahren — `SecretDeployment`:**

1. **Saga (Geometrie bekannt):** Legale aktive DPs ermitteln (Farb-/Aktivierungsregeln,
   Props-Filter, Rechteck-Legality §3.8). App wählt **verdeckt den Argmax** des DP-Scores
   (`S = w1·(−D(Pfad zu Missionsziel/Blockerknoten)) + w2·(Rebellen-Routen-Glaube über
   Start-Eingänge/Zonen-Beliefs §3.3.3 — NIE Ist-Position) + w3·(Sicht vom DP auf Objekte) +
   w4·Deckungs-/Blockerwert`, Pattern-Gewichte). Ausgabe ist eine
   **Instruktion ans Ziel**, nie eine Frage: „Platziere die Stormtrooper am aktiven
   Aufstellungspunkt im Reaktorraum“ — der DP wird durch die Platzierung selbst öffentlich,
   die Informationslage entspricht exakt dem menschlichen Imperialen.
2. **Deterministische Gleichstandsauflösung** (Gruppenindex), Nachziehen weiterer Figuren:
   „as close as possible“ an den gewählten DP, Folgefiguren nächstgelegene legale Felder.
3. **Classic (keine Geometrie):** Fairer Fallback = App wählt **verdeckt gleichverteilt** aus
   den laut Missionsregel aktiven DPs (farbcodiert bekannt) und instruiert die Platzierung.
   Das ist informationstheoretisch nicht schlechter als ein offener Mensch und besser als
    vorheriger öffentlicher Zufall: keine Leaks, keine Vorteile für beide Seiten. Ein
   „Smart-Guess“ aus Missions-Textbausteinen (Richtungshinweise) ist Experiment-Flag, Default aus.
4. **Begründungen erst post-hoc und observable-only:** Nach der Landung darf die App sagen,
   was öffentlich einsichtig ist („nächster DP zum Terminal“), niemals „DP Süd war 2 Punkte
   besser“ (verrät die Gegenoption). Erklärungsstufe §3.7 regelt das.
5. **Hausregel-Option „Open Tables“:** Gruppen, die DPs/Hand offen spielen wollen, können
   Ask/Suggest reaktivieren — explizit als Hausregel gekennzeichnet, Default aus.
6. Missionsdesign-Zufall („random reinforcement“) bleibt respektiert: verdeckt gewürfelt,
   **Ergebnis** wird protokolliert (nicht die Würfe-Alternativen).

### 3.7 Schicht G — Transparenz-Layer mit Erklärungsstufen (v3.2)

„Warum?“-Tooltip: Focus-Kette, Pfadkosten, Tiebreak, Regelprofil-Zitat. Wegen Prinzip 5 gibt es
drei **Erklärungsstufen** (Setup-Option):

1. **Post-Hoc Public (Default):** Begründungen nur für bereits ausgeführte/öffentliche Züge und
   nur mit öffentlich einsichtigen Gründen. Keine Gegenoptionen, keine Hand-Infos.
2. **None:** Instruktion ohne jede Begründung (LotA-Stil).
3. **Full:** Volle Begründung inkl. Counterfactuals — nur sinnvoll im Solo-Spiel oder an
   Open-Table-Runden; klar als „verrät geheime Info“ markiert.

Die Order-Deck-**Ablage** bleibt immer offen (Design-Kanal, §3.2): sie enthält nur bereits
gespielte Karten.

### 3.8 Schicht H — Größen- & Raumlegalitäts-Layer (v3, neu — Thomas' AT-AT-Punkt)

Regelfakt (Reference Guide, MASSIVE, Abschrift):

> „The Massive keyword … • Figures do not block line of sight to or from a Massive figure.
> • **During a campaign, Massive figures cannot enter interior spaces.** • Massive figures can
> enter spaces containing blocking terrain and impassable terrain … move through and end their
> movement on blocked or impassable edges. • Massive figures can enter spaces containing hostile
> figures and/or difficult terrain at no additional movement cost. • Massive figures cannot enter
> spaces containing other Massive figures. • A Massive figure can end its movement in spaces that
> contain blocking terrain and/or other figures. Any figures in its spaces are pushed into the
> closest empty space …“

Ergänzend (MOVEMENT / BLOCKING & IMPASSABLE TERRAIN / DOORS): Mehrfelder-Figuren behalten beim
„Move X Spaces“ ihre Orientierung; Large Figures zahlen nur +1 MP in Difficult Terrain;
Large-Figure-Basen auf beiden Seiten einer zufallenden Tür werden an das nächste leere Feld
geschoben; Blocking Edges blocken keine Massive/Mobile-Figuren.

**IC2-Befund (§1.1 Um9):** `FigureSize`-Enum + DP-Filter existieren, aber Bewegung/Aufstellung
validieren weder Interior-Verbot noch Mehrfelder-Geometrie → AT-AT indoor war möglich.

Umsetzung in Tactical AI:

1. **`SpaceLegalityValidator`** (Teil von `ActionBuilder`/`PathGraphBuilder`): jede Move-/Place-/
   Reposition-Zielentscheidung läuft durch den Validator. Pro Feld: `interior`-Flag (aus
   Map-/Tile-Daten: Innenraum-Kacheln, Wände; Saga-Mode hat Raumstruktur, Classic: Missions-Kachel-
   typ), Blocking-/Impassable-Edges, Türstatus. Pro Figur: `FigureSize` (1x1/1x2/2x2/2x3 —
   **Alle-Felder-Menge** der Basis!) + `keywords` (Massive/Mobile). Illegaler Zielzustand →
   Ziel wird nicht vorgeschlagen (nicht: „vorgeschlagen und der Mensch merkt's“).
2. **Rechteck-Platzierung:** Large/Huge-Figuren belegen definierte Rechtecke; der Pfadgraph
   arbeitet auf **Ankerfeld + Orientierung**, Move-X-Abilities erhalten Orientierung (Regel).
   Deployment: DP-Filter prüft, dass das komplette Rechteck legal liegt („as close as possible“
   heißt: nächstgelegener legaler Rechteck-Anker).
3. **Taktik-Doktrin für Massive (AT-AT & Co.):** Weil sie nicht nach innen können, bekommen
   Massive-Figuren ein eigenes Pattern-Verhalten:
   - **Belagerungsmodus:** Focus-Auswahl nur auf Feldern, die von **außen** erreichbar sind —
     Rebellen in Innenräumen werden nicht „verfolgt“, sondern an Ausgängen **abgepasst**
     (Objective-Knoten: Außentüren, Treppen, Landeplätze → Blocker-Stellung davor).
   - **Druckmittel:** Rebellen-Ziele im Innenraum → Massive postiert sich vor dem Ausgang mit
     dem größten Flusswert (aus Rebellen-Pfadmodell §3.5), wartet (Rest-HP-Regel) statt zu
     blocken ohne Sinn.
   - **Tür-/Wandbrecher-Option:** nur wo Missionsregel/Kartenblatt es erlaubt (Massive ignoriert
     Blocking Terrain **im Außenbereich** — das ist legal und wird taktisch genutzt: Abkürzung
     durch Trümmer im Außenbereich).
   - **Nie:** Innenraum-Betretung, Ziel-Selection durch Wände hindurch als „Focus“ (Ausführung
     braucht LOS), zwei Massive in einem Feld.
4. Gleiche Geometrie-Prüfung greift bei **Deploy** (Rechteck am DP), **Push** (Tür-Regel,
   Massive-Push) und **Reinforcement**.
5. Legacy bleibt unangetastet (Known-Bug-Charakter); im Tactical-AI-Log wird jeder abgelehnte
   illegale Zielvorschlag protokolliert („AT-AT: Ziel X verworfen — interior“), damit klar ist,
   dass die Regel aktiv wacht.

### 3.9 Schicht I — Bedrohungs-Ökonomie-Doktrin (v3, neu — Thomas' Sparen-Punkt)

Befund: IC2 gibt Threat in Deployment-Fenstern **sofort und vollständig** aus (§1.1 Um9) und
würfelt dabei noch Eliten hoch (§1.1 Um8). Ein menschlicher Imperiale **spart** — Threat ist ein
Zeitbudget: früher billiger Blocker vs. später Elite/AT-ST. JinM zeigt dasselbe pattern
(Reservoire + Threat-Events, §1.8).

**`ThreatEconomy`-Modul** (Teil des Planners):

1. **Einkommensprognose:** Aus Missions-Triggern (bekannte Threat-Ereignisse/Runden-Delays)
   und dem Missionsbudget wird der Threat-Verlauf der Mission projiziert (Unsicherheiten als
   Spannbreite).
2. **Sparziel (Save Target):** Pro Mission wird ein taktisches Ziel identifiziert (stärkste
   legale Reserve-/Kauf-Einheit, die ein **aktuelles Problem** löst: Terminal-Runner → billiger
   Blocker jetzt oder Sentry Droid in Runde 3?). Entscheidungsregel:
   `Deploy jetzt, wenn (taktischer Wert jetzt) ≥ (erwarteter Wert des Sparens auf Ziel-Einheit,
   diskontiert um erwartete Runden bis zum Ziel)` — Werte aus Pattern-Score (§3.6-Score).
3. **Tempo-Regeln (Default):**
   - Deployment-Fenster heißt nicht „alles deployen“: es werden nur Gruppen deployt, deren
     Pattern-Score ≥ Schwelle; Rest bleibt Reserve (mit Log-Zeile „gespart für …“).
   - Anti-Frühstau: in Runden, in denen Rebellen noch außerhalb jeder Reichweite sind, werden
     keine billigen Kanonenfutter-Gruppen vors Gesetz gezogen (sie verlieren nur vor Overwatch/
     Finish-Down-Zonen an Wert) — stattdessen Guard-/Blocker-Positionen mit vorhandenen Einheiten.
   - Späte Bedrohung ist mehr wert: identische Threat-Kosten werden bevorzugt **später**
     eingesetzt, wenn das Missionsziel der Rebellen bereits konkret ist (Terminal aktiviert,
     Item getragen) — dann zielt auch der Kauf (Hunter-Futter statt Streukräfte).
   - Ask entfällt (Prinzip 5!): eine „Jetzt X oder sparen auf Y?“-Frage würde die
     **Handzusammensetzung** leaken. Die App gibt nur die **Entscheidung** aus („Deploy: X“
     oder „Keine Verstärkung in diesem Fenster“), Begründung post-hoc public-only (§3.7).
     Beides ist ohnehin observierbar: Nichts-deployen sieht man am leeren Feld.
4. **Verbindlichkeit ohne Leak (v3.2):** Threat-**Einnahmen/Ausgaben** laufen öffentlich im
   Missionslog (ist im Regelwerk ohnehin offenes Gut); **Spar-Absichten bleiben intern**
   (QA-/Sim-Ledger), sonst leakte die App die Hand. Nachrechnung ist trotzdem lückenlos:
   Ausgaben + sichtbare Reserve = Bestand.
5. Profil-Kompatibilität: MissionsSkripte, die zwingende Deployment-Events definieren
   (`EnemyDeployment`-Actions), bleiben Pflicht — die Ökonomie entscheidet nur über
   **optionale** Deployments/Verstärkungen.

---

## 4. Technische Umsetzung im Repo

### 4.1 Neue Dateien (Plan)

```
Assets/Scripts/TacticalAI/
  IActivationPlanner.cs          // Plan(ICardData, IBoardSnapshot) -> ActivationPlan
  LegacyPlanner.cs               // Wrapper um heutige instructions.json-Pfade
  TacticalAIPlanner.cs           // die neue Logik (deterministisch + Seed)
  RuleProfile.cs                 // A Reference / B Overlord / C Legacy (Budget-Regeln)
  SkillProfile.cs              // §6 Skill-Stufen S1–S5 (δ/h/β/π/θ) — kein Material-, kein Bonus-Kanal
  OrderDeckManager.cs            // 8-Karten-Decks pro Gruppe, Draw/Discard/Save
  FocusResolver.cs               // GH-Fokus + Tiebreaker-Kaskade
  ActionBuilder.cs               // 2-Aktionen-Budget + Specials aus Kartenblatt
  PathGraphBuilder.cs            // Wand/Tür/Terrain -> Graph (Saga + Classic), Anker+Orientierung für Mehrfelder
  SpaceLegalityValidator.cs      // §3.8: Massive/Interior/Rechteck/Push
  ThreatEconomy.cs               // §3.9: Sparziel, Tempo-Regeln, leakfreie Entscheidungs-Ausgabe
  ObjectiveModel.cs              // Rollen-Gewichte aus Mission/Triggern
  DeploymentScorer.cs            // §3.6: Secret-Deployment-Scoring (inkl. Rechteck-Legality)
  TacticalAISettings.cs          // serialisierbar wie SessionData
Assets/Resources/Languages/En/tactical_behaviour_patterns.json
```

Kernlogik **Unity-frei** (Interfaces `IBoardSnapshot`, `IFigureState`, `ICardData`) → NUnit-
testbar ohne PlayMode, prerequisite für §7.

### 4.2 Hooks in bestehenden Code (minimal-invasiv)

| Hook | Stelle | Änderung |
|---|---|---|
| Auswahl Planner | `SagaController.cs:718`, `SagaDGPrefab.cs:255` | Popup bekommt `ActivationPlan` statt `CardInstruction`; Legacy als `else` |
| Deck-Reset pro Runde | `SagaDGPrefab.cs:237–244` | `OrderDeckManager.DrawForRound(group)` statt `instructionOption = null` |
| Aufstellung | `MapEntityManager.GetActiveDeploymentPoint()` | `IDeploymentStrategy`: `SecretDeploymentStrategy` (Score, saga) / verdeckt-Zufall (classic) vs. `RandomDeploymentStrategy` (bisher) |
| Bonus | `EnemyActivationPopup.ParseBonus()` | Early-Return im neuen Modus |
| Elite-Roll | `DeploymentManager.DoDeployGroup()` Z. 122–152 | Im neuen Modus kein Difficulty-Upgrade/Downgrade-Wurf (§1.1 Um8) — Elite nur offen |
| Deploy-Tempo | `MissionEventHandler` (optional-deployment windows, Z. 285ff.) | `ThreatEconomy.DecideDeploymentWindow(...)` entscheidet über Pflicht-Events hinaus; Entscheidungsausgabe leak-frei (§3.9) |
| **Kampagnen-Menü (v3.3)** | `CampaignStructure` (per-Mission-Feld wie `threatLevel`), `MissionItemPrefab.OnStartMission()`, `CampaignManager.StartMission()` (Z. 665), `SagaCampaign.ModifyNextMissionThreatLevel()` als Vorbild | Neues Feld `aiMode` (null = Kampagnen-Default erben; alte Saves → null → Legacy) + 3-Zustands-Auswahl pro Mission **vor** dem Start (wie das Threat-Rad): Legacy | Tactical AI (Reference) | Tactical AI (Overlord-Profil). Snapshot-Pin: Modus wird bei `StartMission` eingefroren (RunningCampaign), ein laufender Mission ist niemals umschaltbar; Kampagnen-Default + per-Mission-Override; identische Auswahl auf dem `SagaSetup`-Startschirm |
| Setup-UI | `SagaSetupOptions` + `SettingsPanel` | Modus (Legacy | Tactical AI) + Regelprofil + **Skill-Stufe S1–S5** (§6; Threat-Budget bleibt Missionsfeld!) + Deployment (Secret-Auto | Open-Ask-Hausregel) + Erklärungsstufe (§3.7) |

SaveGame-Kompatibilität: Order-Deck-Zustand wie `instructionOption` serialisieren — alte Saves
bleiben lesbar (null → Legacy).

### 4.3 Bewusste Nicht-Ziele

Keine Auto-Spiel-Engine (die App instruiert weiterhin den Menschen); keine Held-Buff-Modelle;
kein Umbau Legacy-Inhalt über die wenigen Hooks in §4.2 hinaus. (v1-Zeile „keine
Simulations-Engine“ ersetzt: die **Test-**Simulation §7 ist dabei, die App selbst bleibt
Instruktion.)

---

## 5. Regeltreue-Testplan

1. **Unit-Tests (C#):** FocusResolver gegen GH-Beispiele (Rulebook S. 39–40 in IA-Geometrie);
   Tiebreaker-Kaskade; ActionBudget-Validierer (erfundene Aktion/Überdistanz → reject).
2. **Regression:** Legacy bitgleich (Saves, Popup-Inhalte).
3. **Spielnachweise:** „Lair of the Rancor“ (Guard), „Strike Out“ (Blocker an Tür), „The Search
   for the Tomorrow Tree“ (Hunter auf Item-Träger) — Zuglisten gegen manuelle Referenzpartie.
4. **Fairness-Audit** als CI-Test (§7).

## 6. Schwierigkeit = Spielstärke der AI (v3.7-VUmbau, Thomas' Schach-Modell)

**Grundsatzentscheidung (Thomas, 2026-10-26):** Mehr oder stärkere Einheiten zu deployen als die
Mission vorsieht ist **Schummeln durch die Hintertür** — Aufstellungskontingente und
Threat-Budget sind **Missionsdesign**, keine KI-Stellschraube. Die Schwierigkeitsskala verschiebt
sich auf die **Qualität des Spiels**: Wie strikt verfolgt die AI ihre Fokusziele? Wie gut
taktiert sie? Wie beim Schach — dieselben Steine, verschiedene Spielstärke.

**Zwei orthogonale Achsen (nie vermischt):**

1. **Threat-Budget / Missionsdesign (Regelachse, unverändert):** Was deployed werden *darf*,
   bestimmt ausschließlich Mission + Regelwerk (Bedrohungsgrad des Missionsaufbaus, Eliten
   gedruckt, Agenden). Tactical AI verändert daran **nichts** — auch nicht "zur Balance".
2. **Skill-Stufe S1–S5 (Spielstärke-Achse, das neue Balancing):** Was die App *innerhalb* der
   legalen Möglichkeiten tut.

| Skill-Parameter | S1 (Rekrut) | S3 (Veteran) | S5 (Overlord) |
|---|---|---|---|
| **Fokus-Disziplin δ** (Zielwahl aus W-Rangliste) | wählt noisebedingt Rang 2–4; unterscheidet gesund/verwundet im Counter **nicht**; kein Focus-Fire (jede Figur einzeln) | Rang 1–2 mit seltenem Aussetzer; Counter-Semantik voll | immer Argmax; Volley-Koordination (mehrere Figuren auf dasselbe killbare Ziel, §3.3.0) |
| **Planungs-Horizont h** (Timer-/Rennrechnungen) | h=1, rein reaktiv, Stall-Race wird ignoriert | h=2, Race bei offensichtlicher Timer-Gefahr | h=3, proaktive Blockade/Intercept-Vorwegnahme |
| **Glaubens-Trägheit β** (Event-Feed) | Ereignisse verspätet (2 Runden) und halbiert verarbeitet | 1 Runde Verzug | sofort, volle Ausnutzung (nie mehr als AppView hergibt) |
| **Positionierungs-Güte π** | erster legaler Bewegungs-Kandidat der Doktrin | bester Kandidat nach Reichweiten-/Deckungs-Einfach | bester nach LOS/Deckung/Overwatch-Meiden/Ranged-Disengage |
| **Ökonomie θ** (Threat-Tempo §3.9) | verschenkt Threat sofort greedy (Nostalgie: das ist das Niveau der alten App) | Sparregeln mit Rauschen | volle Tempo-Optimierung inkl. Reserve-Bündelung |
| **Risikoklugheit ρ** (§3.5.1) | immer press — Verwundete sterben lassen (altes App-Niveau) | Schwellen für guard_hold/disengage | volle Abwägung: Vitalität × Ring-Nähe × Einheits-Ersatzwert × Posture |

**Fairness-Grenzen der Skala (hart, §7-Testbar):**

- Skill-Parameter **dünnen nur die Auswahl innerhalb der legalen Optionen ein** (Rangwahl,
  Filter, Verzug). Sie verändern **nie**: Aktionsbudget, Reichweiten, Stats, Karten-Specials,
  Informationszugang (immer nur AppView). S5 = maximum regelkonforme Qualität, S1 = legaler
  schwacher Spieler — **kein** absichtliches Auslassen legaler Aktionen (wie ein schlechter
  Mensch Fehler *begeht*, nicht wie ein Computer Gnade *showt*).
- **Determinismus bleibt:** alle "Fehler" sind Draws aus dem Missions-Seed (Rangwahl-Band,
  Verzugszähler). Gleicher Seed + gleiche Skill-Stufe ⇒ identischer Log-Hash.
- **Erklärbarkeit:** why-Tooltip nennt das Handikap („Skill 2: Fokusziel Rang 2 gewählt —
  Fehlerband aktiv“). Transparenz statt versteckter Bremse.
- Eliten/Wachen: Einsatz nur wo Missionsdesign sie vorsieht; der alte 15-%-Elite-Wurf (§1.1 Um8)
  existiert in Tactical AI nicht.
- Der **offene Threat-Bonus-Kanal (JinM-Lehre, §1.8)** steht im Geist des Schach-Modells nicht
  mehr zur Verfügung (Stat-Aufschlag = Material-Vorteil). Status: entfernt als Balancing-Kanal;
  denkbar höchstens als ausdrücklich als Hausregel markierte Modifikatoren-Datei vor
  PR-Erstellung — Empfehlung: ersatzlos raus.

**Zielkurve für §7-Balancing (T2b):** Skill-Stufe × Fixtures, Threat-Budget pro Fixture
konstant auf Missions-Default: S1 ⇒ Rebellen-Siegrate ≈ 65 %, S3 ⇒ ≈ 50 %, S5 ⇒ ≈ 30 %
(Kalibrierband ±8 pp; Simulation entscheidet, Werte sind Startannahmen). Der
Sättigungs-Befund aus T2 (L1–L3 = 100 % imperiale Siege) wird dadurch ersetzt: Sättigung über
Material ist **kein Balancing-Problem mehr, sondern ein Balancing-Verbot**.

## 7. Simulations-/QA-Harness (neu)

Zwei Ebenen, identische Invarianten, gemeinsame JSON-Fixtures (Raster-Boards: Wände, Türen,
Objectives — handgeschnittene Fixtures: Rancor-Korridor, 2-Raum-Terminal, offene Karte 2 Eingänge,
**Belagerungs-Hof: Innen-Terminal + Außen-DPs + AT-AT-Verstärkung** für §3.8/§3.9):

### 7.1 C#-Ebene (Repo, CI)
- `TacticalAI.Core` (Unity-frei) + EditMode/NUnit-Tests: Golden Tests (GH-Beispiele,
  Blocker-/Terminal-Fixture); **Invarianten pro Zug in 1000-Zug-Simulationen**: MP ≤ Speed,
  Aktionen ≤ 2, Attacken ≤ 1 (Nicht-Held außer Karten-Special), Specials ⊆ keywords,
  kein `bonuseffects.json`-Zugriff, Reposition nur mit Kartenblatt; **Größen-Invarianten
  (§3.8):** Massive nie auf `interior`-Feld, Mehrfelder-Rechteck voll legal, keine zwei Massive
  in überlappenden Feldern, Move-X erhält Orientierung; **Threat-Ledger (§3.9):** Summe(Ausgaben)
  ≤ Summe(Einnahmen+budget), interne Spar-Ledger nie im öffentlichen Log; **Skill-Invariante
  (v3.7, §6):** alle Skill-Profile operieren auf identischer legaler Optionen-Menge (Diff der
  Optionslisten Skill-Profil vs. S5 ist leer), Fehler nur als Rangwahl/Verzug aus Seed-RNG; **Leak-Invariante
  (v3.2, §0-P5/§3.6/§3.7):** kein UI-Payload (Instruktion, Log, why-Tooltip) enthält in Stufe
  „Post-Hoc Public“ nicht-öffentliche Infos — prüfbar per Fixture-Test: kein DP wird vor seiner
  ersten Beprobung namentlich/positionsbezogen erwähnt, keine Gegenoptionen, keine Hand-Namen vor
  Landung; **Siegermodell-Invariante (v3.4, §3.3.0):** Posture ist aus öffentlichen
  Missionsdaten + öffentlichem Spielzustand reproduzierbar berechnet (kein verdeckter Wert),
  Posture-Wechsel immer mit Log-Zeitstempel; **Theater-Invariante (v3.6, §3.3.3):** keine
  Anweisung/Begründung/kein Tooltip behauptet eine Einzelfigur-Ist-Position — alles
  Positionslose läuft über Zonen-Beliefs + Brett-Ableitung; null Pflichteingaben über das
  bestehende UI hinaus; **Determinismus:** gleicher
  Seed ⇒ identischer Zug-Log-Hash (Deck-Shuffle = einziger Zufall); **Order-Plan-Invariante
  (v3.8, §3.4.1):** die Reihenfolge-Karte ist eine Permutation der lebenden Gruppen (jede genau
  einmal, keine Überspringung/Dopplung auf jedem Skill-Level), Karten werden vor der ersten
  Aktivierung gezogen (kein Aktivierungszeitpunkt beeinflusst Deck-Inhalte), Inferenz-Kanäle
  (§3.3.3-Nr. 2b) nutzen ausschließlich AppView-Zustände (woundTrackerValue-Deltas pro Figur,
  hasActivated-Toggles, HeroState.isWounded-Flips, OnDefeat, eigenes Instruktions-Ledger),
  Attribution nur über zeitliche Fenster-Koinzidenz, Ausgabe nur als Ring-Band.

### 7.2 Python-Referenzmodell (dieses Projekt, vor C#-Port)
- `sim/tactical_ai_sim.py`: Prototyp mit identischem Protokoll (FocusResolver, OrderDeck,
  RuleProfile, DifficultyProfile, ThreatEconomy, SpaceLegality) → 10.000-Runden-Scans zur
  **Balancing-Bestimmung der Skill-Stufen S1–S5 (§6) und der Threat-Tempi, bevor C#-Code
  existiert**. Metriken: Ø Schaden gegen Rebellen/Runde, Ø Runden bis Missionsziel,
  Terminal-Blockade-Rate, **Postur-Szenario-Erfüllung (Eliminate-Focus-Fire-Szenario, Stall-Blockade-Szenario, Protect-Umschalt-Szenario — jeweils erwartetes Verhalten in 100 % der Seeds), **Blind-Penalty (Regret der positionsblinden Doktrin gegen einen Vollwissens-Orakel-Planer auf derselben Welt — Zielband §7.2 Kalibrierung), Zwei-Ebenen-Architektur: Sim-Welt behält Hex-Wahrheit NUR für Metriken, Doktrin-Layer sieht ausschließlich Event-/Belief-View)**,) Leak-Freiheits-Check (0 Verstöße, §7.1-Invariante),
  Sparrate & Bedrohungs-Tempo
  (Runde der ersten/letzten Ausgabe), AT-AT-Innenraum-Verstöße = 0, Belagerungs-Erfolgsrate**.
- Balance-Tabelle → `docs/balance_sim.md` im Fork. **Profil-B-Kalibrierung (§2B):** Simulator
  sucht pro Archetyp den Deck-Mix, mit Ø-EV-Verhältnis zu Profil A ∈ [0.95, 1.05] bei
  definierter Spreizung; Villain-Decks manuell + Validator; Ergebnis als `order_decks.json`.

### 7.3 Abnahme
1. Invarianten grün (beide Ebenen), Legacy bitgleich. 2. Stufe 3 gegen ein 2-Helden-Modell
≈ 50/50 ±15 %; Stufen 1→5 monoton in Ø Rebellen-Verlusten. 3. Jeder Sim-Zug trägt
Tooltip-Payload (automatisiert geprüft).

## 8. Roadmap & PR-Strategie (v3-Stand)

M1 Planner-Interface + RuleProfile + Settings (verhaltensneutral) → M2 Core-Modelle Unity-frei
+ **Python-Sim mit Fixtures (validiert Design vor C#-Port; inkl. SpaceLegality-Basisschicht)** →
M3 Focus/Path + ActionBuilder + SpaceLegalityValidator (Referenz-Profil) → M4 Order-Deck +
Kadenz + Secret-Deployment (§3.6 leak-frei) → M5 **passiver Welt-Feed (§3.3.1b)**: Posture aus
Missions-Eigenschaften, Fokus HeroThreatProfile×Zonen-Beliefs, Vitalität aus Rädern,
Instanz-Keying, **ThreatEconomy**, DoS-Deckel — **keine Positionsregistrierung, kein Hex-Adapter** →
M6 Playtests + Docs → Upstream-Fork, kleine PR-Scheiben (Refactor-PR M1 → Core-PR M2–M3 →
UI-PR M4 → Content-PR M5), MIT-Lizenzen, Owner GlowPuff zuletzt 2026-08 aktiv.
(v4.0-Status: M1–M4 + M6-Paket sind im Fork `feature/tactical-ai` geliefert und playtest-geprüft;
M5 im §3.3.1b-Umfang offen. Ursprüngliche Zählung v3.0 wich von der Umsetzungs-Leiter ab.)

## 9. Offene Punkte

1. ~~Overlord-Vorlagen~~ — geklärt (v3.1): keine externen Vorlagen; Profil B = eigene,
   fairheitsbewiesene Order-Card-Fertigung mit Simulator-Kalibrierung (§2 Profil B, §7).
   Spike-Methode verifiziert (0,96× Reference bei 6×{M+A}+2×{A}-Mix).
2. ε und Pattern-Gewichte: Ergebnis aus §7.2-Simulation, nicht aus dem Bauch.
3. **Interior-Flag-Datenquelle Classic-Mode:** Saga-Missionen haben Raumstruktur; für Classic-
   Kampagnen-Kacheln muss das `interior`-Flag aus Kacheltypen/dargestellten Wänden abgeleitet
   werden (M2-Aufgabe, Daten-Spike). Wo nicht ableitbar: **Massive**-Figuren-Deploy auf
   Innen-DPs mit Warnhinweis sperren (Vorsichts-Default), statt zu raten.
4. ~~JinM-Verhaltensreferenz~~ — erledigt: echte App analysiert (§1.8); Detail-FSMs bleiben
   black box, brauchen wir aber nicht (GH/FH-Prozedur ausreichend).
5. Threat-Bonus-Tabelle (§6, optional): Beispielwerte stehen, Finalwerte aus Simulation.
