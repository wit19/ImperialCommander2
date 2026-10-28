#!/usr/bin/env python3
"""M12a helper: independent (name, XP) ground truth per Imperial Class deck.

Source: imperial-assault.fandom.com deck pages (fan wiki). Each deck page has a
"Cards contained in set:" bullet list like "- Devastating Legion(4XP)" or
"- Powerful Foes(Starter)". Used ONLY as a cross-check for the vision
extraction (names + XP costs), never as a text source.

Writes ../raw/imperial_class_cards/fandom_groundtruth.json:
  {deck: [{"name": ..., "xp": int|None}, ...]}
"""
import json
import os
import re
import sys
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "..", "raw", "imperial_class_cards")
API = "https://imperial-assault.fandom.com/api.php"
UA = {"User-Agent": "Mozilla/5.0 (M12a data curation; contact werth IT)"}
DECKS = [
    "Technological Superiority", "Military Might", "Subversive Tactics",
    "Inspiring Leadership", "Armored Onslaught", "Precision Training",
    "Imperial Black Ops", "Hutt Mercenaries", "Nemeses",
    "Power of the Dark Side", "Reactive Defenses", "Overwhelming Oppression",
]

def wikitext(title):
    q = urllib.parse.urlencode({"action": "parse", "page": title, "redirects": "1",
                                "prop": "wikitext", "format": "json"})
    req = urllib.request.Request(API + "?" + q, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        d = json.load(r)
    return d["parse"]["wikitext"]["*"]

def parse_cards(wt):
    wt = wt.replace("&nbsp;", " ")
    wt = re.sub(r"<[^>]+>", "", wt)          # strip span markup around costs
    low = wt.lower()
    i = low.find("cards contained in set")
    if i < 0:
        i = low.find("consists of")
    if i < 0:
        return None
    seg = wt[i:i + 12000]
    card_re = re.compile(
        r"^\*+\s*\[\[([^|\]]+)(?:\|([^\]]+))?\]\][^\n]*?"
        r"(?:\(\s*(?:(\d+)\s*(?:XP|Exp)|Starter|Basic)\s*\)|$)",
        re.I | re.M)
    hits = [m for m in card_re.finditer(seg)]
    out = []
    for n, m in enumerate(hits):
        name = (m.group(2) or m.group(1)).strip()
        cost = (m.group(3) or "").strip()
        xp = int(cost) if cost.isdigit() else None
        tail = seg[m.start():m.end()]
        starter = bool(re.search(r"\(\s*(?:Starter|Basic)\s*\)", tail, re.I))
        end = hits[n + 1].start() if n + 1 < len(hits) else len(seg)
        bullets = [re.sub(r"^\*+\s*", "", ln).strip()
                   for ln in seg[m.end():end].splitlines() if ln.strip().startswith("*")]
        out.append({"name": name, "xp": xp, "starter": starter, "text": bullets})
    return out or None

def main():
    os.makedirs(RAW, exist_ok=True)
    gt, missing = {}, []
    for deck in DECKS:
        try:
            cards = parse_cards(wikitext(deck))
        except Exception as e:
            cards = None
            print(f"  {deck}: fetch error {e}", file=sys.stderr)
        if cards:
            gt[deck] = cards
            print(f"  {deck}: {len(cards)} cards")
        else:
            missing.append(deck)
    path = os.path.join(RAW, "fandom_groundtruth.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(gt, f, indent=1, ensure_ascii=False)
    print("wrote", path, "| decks:", len(gt), "| missing:", missing or "none")
    return 0 if not missing else 1

if __name__ == "__main__":
    sys.exit(main())
