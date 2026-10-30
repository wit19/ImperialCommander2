# QA: Imperiale Level-Phase — Ist Kartenkauf überhaupt angebunden? (2026-10-30)

Auftrag (Thomas): In keinem Testlauf wurde je eine Klassen-/Agendakarte
zusätzlich zur Gratis-Startkarte erworben. Prüfen: (1) ist die Level-Phase
angebunden, (2) wird sie aufgerufen, (3) bekommt das Imperium Punkte.

## Kurzbefund

Die Level-Phase **existiert vollständig und läuft bei jedem Missionswechsel
mit** — aber sie kauft sichtbar nichts, weil **kein einziges Missions-Event
dem Imperium jemals XP oder Influence gutschreibt**. Beide Einkommens-
Aktionstypen sind gebaut, aber in den ausgelieferten Missionen **0×
verwendet**. Das Konto startet bei 0 und bleibt auf 0 → die gierige
Kaufschleife kauft 0 Karten, lautlos. Thomas' Spielbeobachtung ist exakt
das, was der Code tut.

## Befund 1 — Ist die Level-Phase angebunden? JA (beide Stufen)

- `SagaCampaign.LoadCampaignState()` (SagaCampaign.cs:224-253) ruft bei
  jedem Campaign-Load:
  1. `EnsureImperialDeck()` — M12b-Backfill (Klassendeck-Wahl, Seed).
  2. `RunImperialUpgradeStage()` — M12c, greedy-Kaufschleife
     (SagaCampaign.cs:583-613) über `TacticalAI.Core.ImperialPurchases.Plan`
     → Buchung ausschließlich via `ImperialDeckState.TryBuyDeckCard`.
  3. `EnsureAgendaDeck()` + `RunAgendaUpgradeStage()` — M12d2
     (SagaCampaign.cs:723+), Markt = 1 Zug pro Stufe,
     `AgendaMarket.RunStage` gegen `agenda.influenceBalance`.
- Timing ist RR-konform: Mission-Ende landet auf dem Campaign-Screen
  (SagaController.EndMission → Campaign-Load), die nächste Einsatzkarte
  kommt danach — „nach Missionsende-Wertung, vor der nächsten
  Einsatzkarte" ist genau dieser Punkt.
- Einheits-Buchungspfad, Append-only-Logs (`xpLog`/`influenceLog`),
  In-Memory-Doktrin mit Save beim nächsten regulären Speichern — alles
  so implementiert und durch Fixture/Gates getestet
  (imperial_purchases.json, 50 Checks; M12c run#45).

## Befund 2 — Wird sie aufgerufen? JA (lautlos)

- Player.log-Beweis (Player-13/14/15): `***CAMPAIGN LOADED***` erscheint
  erst NACH den Stage-Aufrufen (SagaCampaign.cs:255) — die Stufen laufen
  also in jedem Testlauf.
- `RunImperialUpgradeStage` loggt **nur pro gekauftem** Kartenritt
  (ImpBuyLog-Zeile, SagaCampaign.cs:604). Bei Balance 0: `Plan` liefert
  `CardId == null` → `break` → **keine einzige Log-Zeile**. Die Stage ist
  unsichtbar, weil sie nichts zu tun hat — nicht, weil sie fehlt.

## Befund 3 — Bekommt das Imperium Punkte? NEIN. Das ist der Fehler.

Einzige Gutschriften im Code (vollständige Suche `AddImperialXp` /
`AddInfluence`, +deltas):

- **CM6 `CampaignModifyImperialXP`** (eventActionType **31**,
  EventActionConverter.cs:130, Handler MissionEventHandler.cs:856-878) —
  bucht `imperial_xp:<reason>` auf das Deck-Konto.
- **CM7 `CampaignModifyInfluence`** (eventActionType **32**, Converter
  :132-133, Handler :906) — bucht Influence für die Agendastufe.

Verwendung in den Missionen (beide grep über `Resources/SagaMissions/`
und `Resources/Missions/`):

    grep '"eventActionType": 31' → 0 Treffer
    grep '"eventActionType": 32' → 0 Treffer

Weitere Einkommensquellen existieren nicht (weitere +delta-Aufrufe:
keine; `TryBuyDeckCard`/`TryBuyAgenda` schreiben nur −deltas).
`ImperialDeckState.FromChoice` startet `xpBalance = 0` und gibt nur die
**BasicCards gratis** (das ist die beobachtete „Startkarte gratis").
Design-Notiz M12c: „XP ARRIVES between missions (CM6 bookings from
mission outcome branches)" — die **Redaktion der Missions-JSONs für CM6/
CM7 war Teil dieses Plans und ist nie erfolgt.** 94 Missionen wurden
nie angefasst. Das ist die Lücke: Hahn gebaut, kein Wasser dran.

Nebenbefund — Sichtbarkeit der Punkte:

- Der XP-Stand war bisher an zwei Orten sichtbar: (a) Imperium-Button in
  der Campaign-Manager-Leiste (`impCampaignXpLine`, CampaignManager.cs:
  668) — dieser Button **installiert nie** (Player.log seit 12: „AddImpe-
  rialDeckButtonToBar skipped: help button not found"); Ursache ist die
  gleiche, die heute für den „?" gefunden wurde: `FindImperialDeckBarAnchor`
  sucht `Help Activation Button`/`info Button` **mit Button-Komponente**,
  und die Objekte haben nur den Namen, keine Komponente. (b) Header in
  der Missions-Box — auf Thomas' Wunsch in M13aa entfernt. Ergebnis:
  **derzeit ist das Imperium-XP nirgends sichtbar.**
- Manuelle Kaufoberfläche fehlt: `ManualBuyImperialDeckCard`
  (SagaCampaign.cs:615+) hat **null Aufrufer** — der menschliche Pfad
  (Scope 3) hat Engine, aber kein UI.

## Was fehlt, ist also NUR die Einkommens-Quelle. Optionen

A) **Missions-Redaktion (RR-wörtlich)**: CM6/CM7 in Outcome-Branches der
   Missionen einbauen. Regel-rein (RR p.12: XP kommt „as mission rewards"
   pro Missionstext), aber 94 Dateien × Outcome-Branches + DE-Übersetzung
   — größte Last, wenig deterministischer Nutzen pro Zeile.

B) **Spiegelung des Helden-XP (empfohlen)**: CM1 `CampaignModifyXP` bucht
   bereits pro Mission XP aus dem Missions-Text (MissionEventHandler.cs:
   828). In `ModifyXP` zusätzlich, **nur bei aktivem Tactical-Modus**,
   CM6/CM7-Buchung mit gleichem Betrag + Modus-Regel-Faktor. Vorteile:
   ein Haken, alle Missionen sofort versorgt, Timing identisch
   („mission outcome branch"), pro Mission unterschiedlich wie vom
   Autor gemeint, deterministisch, öffentlich announce-bar.
   Kosten: Hausregel (Imperium-XP := Helden-XP-Wert der Mission) — muss
   als Modus-Regel dokumentiert werden (RR kennt keine generische
   Imperium-XP-Formel; das Konzept-Dokument ist die Quelle).

C) **Pauschal-Gage pro Missionsende**: feste XP/Influence pro beendeter
   Mission bei Campaign-Load. Am einfachsten, aber am weitesten vom
   Missionstext weg; wirkt in allen Missionen gleich (langweilig,
   „loot-Dispenser").

D) **Interaktive Level-Phase (UI)**: Human-Pfad mit `ManualBuy…` +
   Kartenbrowser auf dem Campaign-Screen (M12e-Geist). Setzt eine
   funktionierende Einkommensquelle voraus (A/B/C) und ist der
   größte UI-Aufwand; als zweiter Schritt sinnvoll: überlord kauft
   automatisch, wer's interaktiv will, bekommt das UI nach.

Empfehlung: **B jetzt (+ Ankündigungszeile im Campaign-Log), D als
geplanter Folgeschritt.** A nur, falls Thomas die Modus-Hausregel nicht
will. Dazu gehört zwingend der Ein-Zeilen-Fix am CM-Bar-Button
(Name statt Button-Komponente suchen), sonst bleibt XP unsichtbar.

## Beweismittel

- grep: `'"eventActionType": 3[12]'` → 0 Treffer (beide Konten, heute)
- grep `AddImperialXp|AddInfluence`: nur CM6/CM7 (+deltas), buys (−deltas)
- Player-13/14/15: keine `ModifyImperialXP`-, keine ImpBuyLog-Zeile,
  trotz 2+ Missionswechsel je Lauf; `***CAMPAIGN LOADED***` nach Stufen
- SagaCampaign.cs:213-253 (Hooks), :583-613 (Stage), :615+ (Manual, 0 Aufr.)
- ImperialDeckState.FromChoice: xpBalance 0, BasicCards gratis
- MissionEventHandler.cs:828 (CM1-Haken, existiert und läuft)
- CampaignManager.cs:417-428 + Player.log „help button not found"
  (CM-Button-Ausfall, gleiche Namens-vs-Komponenten-Ursache wie „?",
  heute in M13aa diagnostiziert)

## Umsetzung (Thomas wählt A, 30.10.)

tools/mission_income_editor.py (im Repo-stamm ../tools/) hat in alle
Saga-Missionen die CM1-Anker (Typ 24, xpToAdd>0 — sämtliche 279 liegen in
Missionsend-/Kampagnenend-Events, keine Mid-Mission-Anker; geprüft) 1:1
gespiegelt: 279× CM6 (xpToAdd = Autorenwert) + 279× CM7 (influenceToAdd =
derselbe Wert), reason „mission-reward", neue GUIDs, keine Spieltexte →
keine Übersetzungseinträge nötig (TranslationController überschreibt nur
Texte per GUID). Run: 127 Dateien editiert (+ EMPIRE13-Sample; 10 Missionen
ohne CM1-Anker bleiben unberührt). Validierung: alle 138 Dateien strikt
parsebar, 279=279=279 CM1+/CM6/CM7, Idempotenz (Zweitlauf 0 Änderungen),
Editor bricht je Datei ohne Teilschreibvorgang bei jeder Abweichung.
Tempo: 1-2 XP + 1-2 Influence pro Missionsende → ~25 XP/~25 Influence pro
Kampagne (Era-1-Deckkosten 36 XP; Agenda-Preise 1-4, 61 von 126 kosten 1).
Damit: erste Karte typisch nach Mission 2-3, Agenda fast jede Mission.

**Offen (folgend):** Imperium-Button in der Campaign-Manager-Leiste
installiert nie (zeigt XP-Stand; Name-ohn-Button-Komponente, M13aa-Muster)
und optionale Kauf-Auswahl-UI (Engine kauft greedy).
