# Befund + Umsetzung: RR-Deck-Gesetz (M10) — 31.10.2026

Auslöser: Thomas' Screenshot (k17): Imperialer Offizier zeigt im
Aktivierungs-Popup nur EINE Aktion. Erwartung gemäß Referenz-Regeln: immer
Angriff UND Bewegung.

## Wurzelanalyse (kein Render-Bug)

Das Order-Deck verhielt sich exakt wie kalibriert. Die Mischdecks aus M4a
(Profil B, Konzept 2B.2) enthielten reine Angriffskarten ({attack} =
„Feuerstellung") zur EV-Abstimmung nach unten:

- 66 von 89 Gruppen zogen 1-3 von 8 Karten ohne Bewegungsanteil pro Zyklus
  (23×1, 39×2, 4×3 → 12,5-37,5 % der Aktivierungen mit nur einer Aktion).
- Offizier DG004: Mix {A:2, MA:6} → in 2/8 Runden eine reine Angriffskarte;
  war das Ziel außer Reichweite, passierte sichtbar nichts („nichts passiert"
  aus dem Playtest-Log, intern: `hold position: attack only`).
- Solo-„Aktion"-Rendering ist für eine 1-Block-Karte korrekt (M13a-Gate);
  der Bug-Glaube war ein Design-Widerspruch, kein Code-Fehler.

## Entscheidung (Thomas, 31.10.2026)

„Mache deinen Vorschlag A … Stativ-Einheiten musst du weiterhin strategisch
spielen … B-Karten kommen komplett weg. Wir spielen nach Referenz Regeln!"

Gesetz: Jede Deckkarte = volle RR-Aktivierung [move, attack]. Reine
Angriffs-, Bewegungs- und Ruhescheiden sind abgeschafft. Einzige Ausnahme:
Einheiten, deren GEDRUCKTE Karte Bewegen und Angreifen ausschließt —
Tripod/Stativ (CardData `ignored:"Tripod"`, C# `IsTripod`, M13t-Erzwingung):
E-Web Engineer DG007 behält Mix {A:3, MA:5}, DG008 (Elite) war ohnehin MA:8.

## Umsetzung

1. `tacai_build/tools/gen_order_decks.py`: RR-Deck-Gesetz als harter Code-Pfad
   (`rr_exempt_rec`, `build_all` erzwingt MA:8 für alle Nicht-Stativ-Gruppen
   inkl. Bösewichte; Spec-Abweichung = Build-Fehler; `why`-Felder der
   Bösewicht-Flavormischungen dokumentieren die Entwertung;
   `check_artifact` prüft das Gesetz am Artifact).
2. `tacai_build/tools/calibrate_decks.py`: Gruppen- und Bösewicht-Stufe
   liefern unter dem Gesetz direkt die gemessenen Referenz-Probe-Werte
   (MAx8 == die Referenzmessung selbst → Bit-identische Wiederverwendung des
   Messcaches, Ratio 1.0 per Definition; Spread = gemessene Ziehreihenfolge-
   CV, ehrlich als ok/tight markiert). Archetyp-/Mischsuche bleibt für
   Stativ aktiv. Spec/Report tragen `deck_law`-Feld.
3. Artifact neu gegossen (89 Decks, VALIDATOR OK), Deploy auf
   `Assets/Resources/TacticalAI/order_decks.json`, Exporteur hat
   C#-Fixtures (decks.json, order_decks.json) neu geschrieben.
4. `tools/TacticalAiGate/ActionTreeGate.cs`: neue Rechtszahl — solo 3 /
   numbered 709 (nur noch die 3 E-Web-Stativ-Karten), DG001-Check auf 8/0,
   DG007-Ausnahme-Check 3/5 mit Stativ-Begründung.
5. Version dev 37, PLAYTEST-Abschnitt 4d.

## QA-Status

- Kalibrierung: 77 Gruppen in Band [0.95,1.05] (Ratio 1.0), Rest
  dokumentierte degenerierte Fälle (Vader/Maul/0-0-0 EV=0 wie vor dem
  Gesetz — gedruckte Bösewicht-Werte ohne Probe-Score, unverändert).
- Python-Vollsuite: 416/416 passed (kein Nachpinnen nötig — die
  Skill-Grid-Pins modellieren Orderkarten nicht; `deck_from_mix` wird nur in
  Gen-/Export-/Testpfad verwendet, nie im Sim-Runner).
- C#-Harness: ALL PASS 37482 Checks gegen die neuen Fixtures.
- TacticalAiGate: ALL PASS (inkl. neuem ActionTreeGate).
- compile_check (APP+TESTS): siehe Commit-Message.

## Balance-Nachwirkung (ehrlich)

Das imperiale Aufgebot handelt jetzt in JEDEr Aktivierung vollständig
(Angriff + Bewegung) — es ist minimal stärker als zur M6/M9-Messzeit, in der
12,5-37,5 % der Aktivierungen ohne Bewegungsanteil liefen. Das ist eine
qualitative Aussage über das Live-Spiel (C#-Engine mit Orderkarten-Zug):
Die Skill-Grid-Pins von M6/M9 (docs/balance_skill_summary.json,
test_skill_anomalies-/test_m9_grid_pins-Fixwerte) modellieren die
Planer-/Skill-Maschine, aber NICHT die Orderkarten-Ziehungen
(`deck_from_mix` lebt nur im Gen-/Export-/Testpfad) — sie liefen unverändert
durch, ein Nachmessen war und ist dort nicht aussagefähig. Eine
Grid-Neumessung MIT Kartenzug wäre ein eigener Auftrag (M11-Kandidat).
