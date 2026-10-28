# Datenherkunft und Lizenz (wiki_data/)

Stand: 2026-10-28, Entscheidung Thomas (M11-Vorbereitung).

## Policy (Cleanroom)
Die normalisierten Datendateien in diesem Ordner beschreiben **gedrucktes Material des
Brettspiels Imperial Assault** (Werte, Kosten, Würfel, Mechaniken, Fähigkeitstexte der
Karten und Regelbuch-Keywords). Sie sind **ohne Übernahme von Wiki-Text** erstellt:

1. **Fakten sind nicht copyrightfähig**: Gesundheit, Ausdauer, Tempo, Verteidigung,
   Attributswürfel, Kosten, Tier, XP-Kosten, Strain-Kosten - Zahlen von den Karten.
2. **Mechaniken als Funktionsbeschreibung**: Keyword-Fähigkeiten, auf deren Karten nur der
   Keyword-NAME aufgedruckt ist (Stativ, Truppenausbildung, Zielcomputer …), wurden von uns
   als kurze Funktionsbeschreibung in eigener Sprache gefasst (EN) und ins Deutsche
   übertragen (DE) - angelehnt an Stil und offizielle deutsche Nomenklatur, wie sie das
   Spiel selbst in seinen Sprachdateien führt. Token-Bezeichnungen (Schadens-/Block-/
   Wild-Marker) folgen dem Spiel. Fähigkeitstexte, die auf den Karten SELBST vollständig
   aufgedruckt sind, sind 1:1-Transkripte des Kartentextes (genau so trägt das Spiel sie
   auch in allies.json/villains.json); das Wiki diente dabei nur als Transportmedium für
   den gedruckten Text.
3. **Keine Wiki-Prosa, keine Wiki-Bilder, keine Quellenattribute im Build.** Das Fandom-Wiki
   diente als Recherchewerkzeug (Abgleich, ob ein Wert auf der gedruckten Karte steht);
   seine Texte wurden nicht kopiert. Roh-Mitschnitte (wiki_data/raw/, *_raw.json) sind
   **nicht versioniert** und gehören nicht ins Spiel.
4. Die Spiel-Selbst-Daten (Assets/Resources/Languages/**) bleiben die einzige Quelle zur
   Laufzeit; diese Ordner-Dateien sind Werkzeuge für Ableitungen (Threat-Modelle,
   Keyword-Semantik, M11b/c).
5. **Zwei Materialarten (Klärung M11g, Stand 2026-10-28):**
   (a) **Roh-Wiki-Dumps** (HTML/Scraper-Auswurf, wiki_data/raw/ und *_raw.json): nicht
   versioniert, nicht buildfähig — das bleibt wie bisher. (b) **Kuratierte
   Brettspiel-Kartentexte** — Fähigkeitstexte, die auf den Karten SELBST vollständig
   aufgedruckt sind, samt ihrer Werte: Spielinhalt aus dem lizenzierten
   Brettspiel-Exemplar, 1:1 vom Aufdruck (Präzedenz: allies.json/villains.json der App,
   siehe Punkt 2). buildfähig; das Wiki diente dabei als Transkriptions-Medium für den
   gedruckten Text — dessen Originallizenz ist nicht einwandfrei geklärt, wir behaupten
   keine Sauberkeit und benennen das Medium. Vor M11g galt Punkt 5 als offener Punkt mit
   Neufassung in eigener Sprache; der Controller hat am 2026-10-28 ausdrücklich
   entschieden, dass die gedruckten Fähigkeitstexte 1:1 ins Spiel gehören ("damit der
   Spieler vor der App weiß, was zu tun ist"). Reine Zahlenwerte (Würfelpools, Kosten,
   Strain) sind von alledem nicht berührt.

## Werkzeuge
`tools/` enthält den Scraper/Normalisierer (reproduzierbar, MediaWiki-API). Lizenz des
Scrapers: MIT (wie das Hauptprojekt).

## Symbole
Die Glyphen in den Texteinträgen folgen der Konvention der App-Sprachdateien:
{A}=Aktion, {B}=Surge, {H}=Schaden/Regeneration, {C}=Strain, {G}=Block, {F}=Ausweichen,
{O}=Fernkampf-Angriff, {P}=Nahkampf-Angriff, {I}=Insight. (Nicht
wiki-Konvention; wiki {{...}}-Symbole wurden abgebildet.)
