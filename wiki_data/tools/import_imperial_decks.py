#!/usr/bin/env python3
"""M12b: deterministic projection wiki_data/imperial_class_cards.json ->
ImperialCommander2/Assets/Resources/Languages/En/tactical_imperial_decks.json.

The build asset is a 1:1 text projection of the curated M12a file: the card
texts stay verbatim (Thomas-Doktrin M11g: gedruckte Kartentexte sind
Brettspiel-Inhalt und bleiben Build-Inhalt; wiki_data/LICENSE_DATA.md Punkt 5,
Transkriptions-Medium Wiki). DROPPED for the build: images/image_urls
(local raw-cache paths + third-party watermarked URLs are tool-side evidence,
never runtime data). DROPPED: deck_banner (equals the deck name in all 12
Era-1 decks - verified below, the script REFUSES to drop a differing banner).

Deterministic: fixed key order, decks sorted by printed name (the M12a file
already is; the script re-sorts and refuses duplicates), cards keep source
order, no timestamps. Re-running on the same source yields the same bytes.
"""
import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]   # repo root (ImperialCommander2/)
SRC = ROOT / "wiki_data" / "imperial_class_cards.json"
DST = (ROOT / "ImperialCommander2" / "Assets" / "Resources" / "Languages" / "En" /
       "tactical_imperial_decks.json")


def slug(s):
    s = unicodedata_strip(s.lower())
    return re.sub(r"[^a-z0-9]+", "_", s).strip("_")


def unicodedata_strip(s):
    # the 12 Era-1 deck/card names are pure ASCII; refuse anything else so a
    # future expansion import cannot silently invent a transliteration
    assert s.isascii(), f"non-ascii name needs a documented slug rule: {s!r}"
    return s


def main():
    raw = SRC.read_bytes()
    src_sha = hashlib.sha256(raw).hexdigest()
    cards = json.loads(raw.decode("utf-8"))

    decks = {}
    for c in cards:
        d = c["deck"]
        if c.get("deck_banner") not in (None, d):
            sys.exit(f"deck_banner differs from deck name: {c!r}")
        decks.setdefault(d, []).append(c)

    deck_rows = []
    for name in sorted(decks):
        deck_id = slug(name)
        card_rows = []
        for i, c in enumerate(decks[name]):
            card_rows.append({
                "id": f"{deck_id}__{slug(c['name'])}",
                "name": c["name"],
                "basic": bool(c.get("basic")),
                "xp": c.get("xp"),                       # null = basic (0 XP)
                "restriction": c.get("restriction"),
                "keyword": c.get("keyword"),
                "abilities": [
                    {"cost": a.get("cost"), "text": a.get("text")}
                    for a in c.get("abilities", [])
                ],
                "needs_review": bool(c.get("needs_review")),
            })
        ids = [r["id"] for r in card_rows]
        assert len(ids) == len(set(ids)), f"card id collision in {name}"
        deck_rows.append({"id": deck_id, "name": name, "cards": card_rows})

    all_ids = [r["id"] for d in deck_rows for r in d["cards"]]
    assert len(all_ids) == len(set(all_ids)), "global card id collision"

    doc = {
        "schema": "tacai.imperial_decks/1",
        "source": ("wiki_data/imperial_class_cards.json (M12a, sha256 "
                   + src_sha + "); Projection: wiki_data/tools/import_"
                   "imperial_decks.py; Kartentexte 1:1 (LICENSE_DATA.md Punkt 5); "
                   "images/image_urls bewusst out-of-build"),
        "conventions": {
            "ids": "deck = slug(Deckname), Karte = deck_slug__karten_slug (stabiler Import-Key)",
            "xp": "xp=null bei Basic-Karten (0 XP, Setup-Schritt 3 'Choose Imperial Class': "
                  "liegt ab sofort aus); sonst 1/2/3/4 - Imperiale XP kosten in der Imperial Upgrade Stage",
            "families": "Einheiten-Familien (TROOPER, REBEL, VEHICLE, DROID, HEAVY WEAPON, MINE, "
                        "HERO, OFFICER, GUARDIAN, COMMANDER, AGENDA, MISSION) werden zur Laufzeit "
                        "aus abilities.text + restriction + keyword extrahiert (Core ImperialDecks.FamilyCounts) - "
                        "das Asset bleibt textnah und damit pruefbar",
            "needs_review": "M12a-Ehrlichkeitsflag bleibt erhalten (47/109), Modell gewichtet es NICHT",
        },
        "deck_count": len(deck_rows),
        "card_count": len(all_ids),
        "decks": deck_rows,
    }
    text = json.dumps(doc, indent=2, ensure_ascii=False) + "\n"
    DST.write_text(text, encoding="utf-8")
    print(f"wrote {DST.relative_to(ROOT)} decks={len(deck_rows)} cards={len(all_ids)} "
          f"bytes={len(text.encode('utf-8'))}")


if __name__ == "__main__":
    main()
