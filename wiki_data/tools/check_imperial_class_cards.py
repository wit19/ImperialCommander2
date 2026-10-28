#!/usr/bin/env python3
"""M12a final check: schema probe + deck/XP statistics for imperial_class_cards.json.

Gate parts: json.load, field schema on EVERY record (not just a sample),
deck counts vs inventory ground truth, XP rules (basic -> xp null,
non-basic -> int), needs_review inventory. Exit != 0 on any violation.

Run: python3 wiki_data/tools/check_imperial_class_cards.py
"""
import json, os, sys

WD = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(WD, "imperial_class_cards.json")
INV = os.path.join(WD, "raw", "imperial_class_cards", "inventory.json")

STR_FIELDS = {"deck": str, "name": str, "deck_banner": str}
ALLOWED = set(STR_FIELDS) | {"basic", "xp", "restriction", "keyword", "deck_banner",
                             "abilities", "needs_review", "review_issues", "notes",
                             "images", "image_urls"}

def main():
    cards = json.load(open(OUT, encoding="utf-8"))
    inv = json.load(open(INV, encoding="utf-8"))
    errs = []

    inv_keys = {(c["deck"], c["name"]) for c in inv}
    out_keys = {(c["deck"], c["name"]) for c in cards}
    if inv_keys != out_keys:
        errs.append(f"key mismatch missing={sorted(inv_keys - out_keys)} "
                    f"extra={sorted(out_keys - inv_keys)}")

    for c in cards:
        k = f"{c.get('deck')}/{c.get('name')}"
        for f, t in STR_FIELDS.items():
            if not isinstance(c.get(f), t):
                errs.append(f"{k}: field {f} missing/not str")
        unknown = set(c) - ALLOWED
        if unknown:
            errs.append(f"{k}: unknown fields {sorted(unknown)}")
        if not isinstance(c.get("basic"), bool):
            errs.append(f"{k}: basic not bool")
        if c.get("basic") is True:
            if c.get("xp") is not None:
                errs.append(f"{k}: basic with xp={c.get('xp')}")
        else:
            if not isinstance(c.get("xp"), int):
                errs.append(f"{k}: non-basic xp={c.get('xp')!r} not int")
        for f in ("restriction", "keyword"):
            if c.get(f) is not None and not isinstance(c.get(f), str):
                errs.append(f"{k}: {f} not str/null")
        if not isinstance(c.get("abilities"), list) or not c["abilities"]:
            errs.append(f"{k}: abilities empty/not list")
        for a in c.get("abilities", []):
            if set(a) - {"cost", "text"} or not isinstance(a.get("text"), str) \
                    or not a["text"].strip():
                errs.append(f"{k}: malformed ability {a}")
            if a.get("cost") is not None and not isinstance(a.get("cost"), str):
                errs.append(f"{k}: cost not str/null")

    print(f"records: {len(cards)} | decks: {len({c['deck'] for c in cards})}")
    import collections
    per_deck = collections.Counter(c["deck"] for c in cards)
    for d in sorted(per_deck):
        xs = [c["xp"] for c in cards if c["deck"] == d and c["xp"] is not None]
        print(f"  {d}: {per_deck[d]} cards, XP {min(xs)}-{max(xs)}, "
              f"basic {sum(1 for c in cards if c['deck']==d and c['basic'])}")
    nr = [f"{c['deck']}/{c['name']}" for c in cards if c.get("needs_review")]
    print(f"needs_review: {len(nr)}")
    for x in nr:
        print("  -", x)

    if errs:
        print("SCHEMA ERRORS:")
        for e in errs:
            print("  *", e)
        sys.exit(1)
    print("OK: schema + deck counts + XP rules all hold")

if __name__ == "__main__":
    main()
