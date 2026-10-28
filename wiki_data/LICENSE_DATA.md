# Datenherkunft und Lizenz (wiki_data/)

Stand: 2026-10-28, Entscheidung Thomas (M11-Vorbereitung).

## Policy (Cleanroom)
Die normalisierten Datendateien in diesem Ordner beschreiben **gedrucktes Material des
Brettspiels Imperial Assault** (Werte, Kosten, Würfel, Mechaniken, Fähigkeitstexte der
Karten und Regelbuch-Keywords). Sie sind **ohne Übernahme von Wiki-Text** erstellt:

1. **Fakten sind nicht copyrightfähig**: Gesundheit, Ausdauer, Tempo, Verteidigung,
   Attributswürfel, Kosten, Tier, XP-Kosten, Strain-Kosten - Zahlen von den Karten.
2. **Mechaniken als Funktionsbeschreibung**: Fähigkeits- und Keyword-Texte wurden in
   eigener kurzer Sprache neu gefasst (EN) und von uns ins Deutsche übertragen (DE),
   angelehnt an den Stil und die offizielle deutsche Nomenklatur, die das Spiel selbst
   in seinen Sprachdateien führt (z. B. Truppenausbildung, Zielcomputer, Konzentrierter
   Beschuss). Token-Bezeichnungen (Schadens-/Block-/Wild-Marker) folgen dem Spiel.
3. **Keine Wiki-Prosa, keine Wiki-Bilder, keine Quellenattribute im Build.** Das Fandom-Wiki
   diente als Recherchewerkzeug (Abgleich, ob ein Wert auf der gedruckten Karte steht);
   seine Texte wurden nicht kopiert. Roh-Mitschnitte (wiki_data/raw/, *_raw.json) sind
   **nicht versioniert** und gehören nicht ins Spiel.
4. Die Spiel-Selbst-Daten (Assets/Resources/Languages/**) bleiben die einzige Quelle zur
   Laufzeit; diese Ordner-Dateien sind Werkzeuge für Ableitungen (Threat-Modelle,
   Keyword-Semantik, M11b/c).

## Werkzeuge
`tools/` enthält den Scraper/Normalisierer (reproduzierbar, MediaWiki-API). Lizenz des
Scrapers: MIT (wie das Hauptprojekt).

## Symbole
Die Glyphen in den Texteinträgen folgen der Konvention der App-Sprachdateien:
{A}=Aktion, {B}=Surge, {H}=Schaden/Regeneration, {C}=Strain, {G}=Block, {F}=Ausweichen,
{O}=Fernkampf-Angriff, {P}=Nahkampf-Angriff, {I}=Insight. (Nicht
wiki-Konvention; wiki {{...}}-Symbole wurden abgebildet.)
