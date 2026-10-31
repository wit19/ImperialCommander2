# M11 — Order-Decks entfernt (Konzeptleiche) — 31.10.2026

## Auslöser (Thomas)

"Jede Figur hat 2 Aktionen, diese sind über die Figuren-Aktionstexte
definiert. Wir brauchen gar kein Deck. [...] Das ist eine Konzeptleiche,
die wir da mitschleppen."

## Befund (verifiziert, nicht aus dem Kopf)

- Nach dem M10-RR-Gesetz enthielt das Artefakt 709 KAERTEN MIT IDENTISCHEM
  Block [move,attack], 3 reine Angriffskarten (nur Stativ DG007) und 141
  Shuffle-Marker — mischen identischer Karten ist Informationsgehalt null.
- `DrawRoundAll` teilte pro Runde an JEDE lebende Gruppe eine Karte; keine
  Rest-Karte mehr => das Deck konnte keine Aktivierung mehr verweigern.
- Die AktivierungsREIHENFOLGE steuerte immer der Planner (OrderPlan/
  Doctrine), nie das Deck.
- Modellursprung war das gedruckte IA-Kommando-Deck mit Aktivierungs-Budget
  (5-Karten-Zug); diesen Notch hat unsere Referenz-Regel nie implementiert.
- Verbliebene echte Funktionen: Stativ-Ausschlussregel (hing ZUSÄTZLICH schon
  als IsTripod an den Figurendaten) + Popup-Textquelle + Röhrenwerk.

## Schnitt

C#-Runtime (Assets/Scripts):
- TacticalAIBridge: OrderDeckManager/_deckMgr/EnsureDecks/Deal/FinishCard
  raus; TryBuildActivationPlanInner synthetisiert Blöcke aus den gedruckten
  Fakten: Standard {move,attack}, Stativ {attack,move} mit den M13t-
  Wachzone-Bäumen ("leaving your space means no shot" — die Exklusivität
  steht bereits im Aktionstext, kein neuer Sprachschlüssel nötig).
  ActivationPlanData.DeckCardId -> CardId.
- Neue statische Whitelist TacticalShippedIds (89 Karten: DG001-DG069 +
  DG071-DG090, DG070 ungenutzt — exakt der Schlüssel-Satz des retired
  Artefakts, verifiziert gegen data/order_decks.json) ersetzt die
  Deck-Mitgliedschaft als Katalog-Zulassung (Bösewichte in der separaten
  villainCards-Liste bleiben drin, Custom-Imports bleiben Legacy-Pfad).
- CountActionBlocks (Solo-/Nummeriert-Label) vom Core ins Bridge geholt.
- Core/OrderDeck.cs GELÖSCHT; TacticalAIAssets: order_decks-Asset-Loader
  raus; Resources/TacticalAI/order_decks.json GELÖSCHT.
- Datenstore-Stempel: dev 38.

Harness/Gate/Tests:
- SecDecks + SecActionTree + fixtures decks.json/order_decks.json/
  action_tree.json + ActionTreeGate + Editor-Test OrderDeckAssetTests +
  toter PyRandom (MT19937-Replay nur für Deck-Shuffle) GELÖSCHT.
- Kommentar-Lügen korrigiert (CardKeywords/VillainOptions/AgendaCards/
  AgendaMarket/SectionsF: Schema-Gesetze jetzt als "M11-retired" markiert —
  die Nicht-Durchsetzung von MultiAttack bleibt als begründet dokumentiert).

Python-Labor (tacai_build) — Bewusst NICHT weggeräumt:
- gen_order_decks/calibrate bleiben als historische Analyse-Werkzeuge +
  Regressionstests (Suite bleibt grün); der Exporter liefert die Deck-Layer-
  Ausgabe (decks.json-Vektoren, order_decks.json-Fixtures-Kopie) nicht mehr
  ans Spiel aus. data/order_decks.json = Archiv-Artefakt, nichts im
  ausgelieferten Spiel liest es.

## Verhaltensdelta

NULL für 88/89 Gruppen (sie bekamen ohnehin immer [move,attack]).
Stativ DG007: Popup führt jetzt den Angriff zuerst und zeigt die Exklusivität
explizit im Bewegungstext statt zufällig mal A-/mal MA-Karten — das ist
regeltreuer als vorher (die MA-Karten des alten Decks verletzten die
gedruckte Regel, sie erlaubten beides in einer Aktivierung).

## QA (M11-Lauf, Ergebnisse siehe Commit-Message)

- Harness:checks=36974 fails=0 (die 508 weggefallenen Checks waren
  ausschließlich Deck-/action_tree-Pins).
- TacticalAiGate: siehe unten (LIVE im Moment des Commits).
- Python-Suite: volle Suite siehe unten.
- compile_check APP+TESTS rc=0.
- Byte-Stabilität: Exporter-Neuguss gegen Repo-Fixtures verglichen (nur die
  drei retired-Dateien fehlen, alle anderen identisch).
