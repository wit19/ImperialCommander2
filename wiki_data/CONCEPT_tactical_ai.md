# Konzept: Wiki-Daten für den Taktischen KI-Modus (M11)

Stand 2026-10-27 · Datenbasis: `wiki_data/` (485 Wiki-Seiten Roh-Cache, geparst via
MediaWiki-API, Lizenz CC-BY-SA, Quellen in `MANIFEST.json`)

## 1. Was jetzt an Daten vorliegt (IST, alles verifiziert)

| Datei | Inhalt | Qualität |
|---|---|---|
| `heros.json` | alle 21 Kampagnen-Helden: Gesundheit/Ausdauer/Tempo/Verteidigung/Attribute je **gesund UND verwundet**, Spezialfähigkeiten mit Text + Strain-Kosten (37/68 haben welche, der Rest ist gratis), XP-Skill-Stufen 1–4, Seitenmission, Belohnung | 21/21, 0 Lücken |
| `hero_weapons.json` | alle 21 Basiswaffen: Würfelpool, Reichweite/Nahkampf, Surge-Effekte | 21/21 |
| `hero_class_cards.json` | 172 XP-Klassenkarten mit XP-Kosten + Effekten | 158/168 Hero-Sheet-Reihen gedeckt |
| `imperial_units.json` / `mercenary_units.json` / `rebel_units.json` | 141 Einheiten: Kosten, Gruppengröße, Traits, Gesundheit, Tempo, Verteidigung, **Angriffswürfel**, Surge-Fähigkeiten, Keyword-Fähigkeiten (Tripod, Assault, Snipe …) | 141/141 mit Keywords, 0 Statistiklücken |
| `items.json` | alle Items nach (Name, Tier) aufgefächert, Kosten, Würfel, Effekte | **83/84 App-Items identisch in Name+Tier+Kosten, 0 Abweichungen** |
| `rewards.json` | 83 Belohnungskarten (Rebel+Imperial) mit Effekten | 83/85 App-Skills gedeckt |
| `wiki_symbol_templates.json` | Legend der Wiki-Symbole aus Template-Docs | B=blau R=rot G=grün G… , K= schwarzer Verteidigungswürfel, {{S}}=Strain, {{Z}}=Surge, {{H}}=Threat … |

Gefundene Datenabweichungen (nicht stillschweigend korrigiert, dokumentiert):
Wiki-Seite „Bacta Pump" trägt im Feld den Tippfehler „Bacta Test" (Seitentitel
kanonisch genutzt); App schreibt „Telecoping Sights", Wiki „Telescoping Sights";
„Bacta Radiation" existiert auf dem Wiki gar nicht.

Spiel-Seiten-Abgleich: **117/117** Deployment-Einträge im Spiel haben ein
Wiki-Gegenstück. **88** haben im Spiel bereits Fähigkeiten, **29 sind leer und
können 1:1 gefüllt werden** (u. a. Stormtrooper ±Elite, Snowtrooper ±Elite,
Imperial Officer ±Elite, Sentry Droid ±Elite, Riot Trooper, Wing Guard,
Clawdite, HK Assassin, ISB Infiltrator, Dewback Rider, Captain Terro, Agent
Blaise, Hondo, Jabba, Darth Vader, Kayn Somos, Royal Guard Champion).

## 2. Wirkung 1 — Echtes Bedrohungsmodell für Helden (ORD-Fokus)

Heute: Kurationstabelle (im Kern Werte **normaler Rebellenkarten**, nur Han Solo
hat echte Zahlen) + neutrale Priors + M10a-Investitions-Brecher. Deshalb brauchte
M10b die Basen-Reinheits-Regel.

Mit den Wiki-Daten verschwindet die Ursache: **alle 21 Helden werden auf ihrer
eigen gedruckten Heldenkarte vergleichbar.**

Neuer Bedrohnungskern (alles öffentliches Tafelwissen, keine versteckten Stats):

1. **Erwarteter Schaden/Runde** — echter Würfelpool statt Proxy:
   Waffenwürfel (`hero_weapons.json`) + Item-Würfel/Modifikatoren
   (`items.json` effects, z. B. „+1 Schaden", „Pierce") — die Loadouts kennt die
   App schon aus der Kampagne. Wahrscheinlichkeiten der Angriffswürfel =
   gedrucktes Regelwerk (Würfeltabelle als Grunddatensatz, M11a).
2. **Verteidigung** — Pool aus Heldendaten (W/K/R) → Trefferwahrscheinlichkeit
   → ein verletzbarer Held ist schwerer fertigzuspielen (multipliziert die
   „Finisher-Kosten", ergänzt die existing Wunden-Ökonomie um echte Gesundheit
   statt Proxy-`health`).
3. **Runner-Wert (protect/stall)** — echtes Tempo von der Karte statt Prior 2×5.
4. **Verwundet-Zustand** — die Karten-Rückseite liefert echte Werte
   (Tempo −1, Ausdauer −1, andere Fähigkeit), nicht mehr unsere Vermutung.
5. **Strain-Budget** — Ausdauer + Fähigkeitstakes: ein Held in Spät-Runden hat
   Druckmittel nur begrenzt; der Akteur-Indikator (M10b) bleibt, bekommt aber
   Kontext (Terminal-Kaper mit 5 freien Ausdauer punktet anders als einer mit 0).

Konsequenz: Kurationstabelle wird Referenz/Verlassenheits-Anker für Harness-
Vergleiche; **M10b-Mischbasen-Problem existiert nicht mehr**, weil nur noch eine
Basis rechnet. M10a-Investition bleibt als public tie-break stehen.

Runtime-Fairness: Gesundheit/Verwundet kommt wie bisher aus dem Feed; Loadout
aus der Kampagne (App); Strain-Marker wären pollbar (M11d-Recherche) — notfalls
Runden-Budget-Modell.

## 3. Wirkung 2 — Imperiale Fähigkeiten statt „ignoriere Fähigkeiten"

Drei Schichten:

1. **Daten füllen (kein Code):** 29 lehrbuchhafte Einträge in
   `DeploymentGroups/enemies.json` (`abilities`/`keywords`) aus den Wiki-Keywords
   und Surge-Effekten. Damit hat jede Spielfigur ihre gedruckten Fähigkeiten.
2. **Planungs-Semantik (kuratiert, klein halten):** pro Keyword eine Regel mit
   Regelwerk-Beleg, z. B.
   - `Tripod`: Angriff und Bewegen ausschließend → Einsatzplan plant beides nie zugleich (E-Web steht und zockt).
   - `Assault`: mehrere Angriffe pro Aktivierung → Angriffsbudget der Gruppe wird wirklich ausgeschöpft.
   - `Scatter`/`Pierce`: Deckung/Ballung der Helden verschieben den Zielwert.
   - `Guardian`:Beschützer-Figur ändert Zielpriorität (erst sie, oder an ihr vorbei).
   - `Snipe`/Reichweite: Annäherungsrisiko in der Bewegungsauswahl.
   Diese Semantik speist die Muster-Auswahl (welches Ziel, welche Bewegung),
   nicht nur die Erklärung.
3. **UI/Meldung:** Im taktischen Modus ersetzt der Text „Fähigkeiten werden
   berücksichtigt (X, Y pro Gruppe)" die aktuelle „ignoriere Fähigkeiten xyz"
   — 11 Sprachen wie bei den bisherigen Explain-Zeilen.

Fairness-Check: Alles gedruckte Karten, die auf dem Tisch liegen — keine neue
Sicht. S1–S3 nutzen weiter nur Basics; **Fähigkeits-Play erst ab S4/S5**
(Vorschlag — Du entscheidest die Leiter).

## 4. Wirkung 3 — Items im Plan (klein, aber echt)

Trägt ein Held „Portable Medkit"/„Bacta Pump" (bekanntes Loadout + Wiki-Effekt),
ist Finisher-Logik sinnlos, solange er gesund bleibt → Fokus-Verschiebung auf
seine Bedrohungslage statt auf Kill-Versuch. „Shock Emitter" & Co. =
Kontrollfähigkeiten mit Auslösungswissen. Nur als Threat-Gewicht, kein
Simulieren der Karten.

## 5. Meilensteine

- **M11a — Datenintegration:** `wiki_data/` ins Repo (parsere Dateien; `raw/`-Cache
  optional per svn:ignore/gitignore), Loader + Schema-Check (App-Harness),
  CC-BY-SA-Attribution (`LICENSE-WIKI.md`), Würfelwahrscheinlichkeits-Tabelle als
  Grunddatensatz.
- **M11b — Helden-Threat v2:** Rechenmodell (Punkt 2) ersetzt Kurationsbasis in
  ORD; overlord_focus-Fixtures rekalkuliert + neu gepinnt; Fairheits-Permutations-
  Check bleibt; Kurationstabelle auf „Referenz" zurückbauen.
- **M11c — Einheiten-Fähigkeiten:** enemies.json füllen, Keyword-Semantik in der
  Planung (Schicht 2), „ignoriere Fähigkeiten"-Meldung ersetzen, Explain-Zeilen.
- **M11d (optional) — Strain & Sheet-Genauigkeit:** verwundete Werte von der
  echten Rückseite, Strain-Budget im Threat, Ausdauer-Polling erforschen.

Reihen-Vorschlag: M11a → M11c → M11b → M11d, weil M11c die von Dir benannte
sichtbare Baustelle („ignoriere Fähigkeiten") am schnellsten schließt.

## 6. Zu entscheiden, bevor wir umsetzen

1. **Fähigkeitstexte im UI:** Wiki ist EN; das Spiel liefert 12 Sprachen.
   Vorschlag: KI-Erklärungen in Spielsprache (wie bisher), Fähigkeitstext der
   Karten-Showcase-UI in EN (Original) — oder Übersetzungs-Pass für DE.
2. **Lizenz im Build:** CC-BY-SA-Daten (Texte!) ins Auslieferungs-Bundle mit
   Attributionsdatei — oder Build bekommt nur abgeleitete Zahlen (Würfel,
   Kosten, Keywords) und die Volltexte bleiben nur im Repo für Tooling.
3. **S-Level für Fähigkeiten** (Vorschlag: ab S4).
4. **Mengen:** alle 29 Füllungen oder nur Deine Kampagnen-Figuren?
5. `raw/`-Cache (485 Seiten, ~4 MB) mit ins Versionsarchiv oder nur die
   Normalisate?

---

## Entscheidungsnachtrag (28.10., Thomas) — umgesetzt als M11a/M11c-Grundlage (dev 33)

1. **Übersetzung:** neue Texte EN + DE über die Sprachdateien; die übrigen 9 Sprachen
   bleiben bewusst unverändert (alt, aber in ihrer Sprache).
2. **Lizenz/Cleanroom:** es wird nur genutzt, was aus dem Brettspiel selbst kommt
   (Kartenwerte = Fakten, Mechaniken = Funktionsbeschreibung in eigener Sprache).
   Keine Wiki-Prosa, keine Quellenangabe im Build; Roh-Mitschnitte bleiben
   unversioniert (wiki_data/.gitignore). Details: wiki_data/LICENSE_DATA.md.
   Bonus-Fund: 102 der Fähigkeiten trugen Name + DE-Übersetzung bereits im
   `ignored`-Feld des Spiels — die Texte kommen also faktisch aus dem Spiel.
3. **S-Level für Fähigkeiten-Play (M11c-Betrieb):** ab S4. OK.
4. **Alle betroffenen Einträge** füllen (nicht nur Kampagnen-relevante).
5. **Nur Normalisate ins Repo.**

### Status M11c-i (dev 33, diese Lieferung)
Alle 83 Einträge mit `ignored`-Feld (63 Feinde + 20 Bösewichte, EN+DE) auf reguläre
`abilities` gemappt (Merge: bereits vorhandene Fähigkeiten behalten, keine Duplikate);
Keyword-Tabelle mit 85 Einträgen EN+DE in wiki_data/keyword_table.json;
DynamicCardPrefab zeigt den roten „Ignorierte Kartenfähigkeiten"-Block nur noch bei
tatsächlich ignorierten Fähigkeiten.

### Nächste Schritte (M11c-ii / M11b)
- Keyword-Semantik in der Musterwahl (Stativ=nie Bewegen+Angreifen, Assault=voller
  Angriffsbudget, Scatter/Pierce=Deckungsziel, Guardian=Zielpriorität), aktiv ab S4.
- Helden-Threat v2 auf eigener Heldenkarte (M11b) — löst das Mischbasen-Problem von
  M10b an der Wurzel.
