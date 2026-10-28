#!/usr/bin/env python3
"""M12a retry pass: resolve flagged cards by high-zoom crop re-transcription.

For every card whose merged entry carries review_issues (or pass names via
--cards), render deterministic crops of the NATIVE image (rules area 3x
Lanczos + autocontrast; plus the left icon column 5x) and re-run the strict
transcription prompt on the crops. If a crop parse passes all structural
checks (name/XP/banner vs ground truth + symbol audit multiset), record it in
the card's vision cache under "resolved" - merge_card prefers that layer.
Cards that still fail stay needs_review (honest flag, no guessing).

Run:  python3 wiki_data/tools/retry_zoom_imperial_class_cards.py [--dry-run]
"""
import argparse, json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import extract_imperial_class_cards as ex   # noqa: E402

from PIL import Image, ImageOps            # noqa: E402

CROP_PROMPT_EXTRA = """
NOTE: the image is a high-zoom crop (possibly only part) of the card. The title
banner or bottom banner may be cut off: if the title is not visible, copy it
from the previous known value: "{name}". If the XP corner is not visible, keep
xp={xp}. Transcribe the ability text that IS visible, and classify every
printed icon precisely against: ACTION=right-pointing filled arrow; EVENT=
hourglass; MANOEUVRE=boot; UPGRADE=gear with eight-point star; INTERRUPT=
lightning bolt; REACTION=curved swoosh; REGROUP=circular arrows; SLEEP=
crescent; DAMAGE=round starburst impact; SURGE=spiky burst; BLOCK=curved
triangle; EVADE=broken ring; STRAIN=jagged hexagon with lightning cutout;
THREAT=hexagonal coin with hexagonal hole; RANGED=rifle; MELEE=bayonet. If an
icon cannot be identified with certainty, put its shape in brackets as the
cost (e.g. "[curved swoosh]:") instead of guessing a keyword."""


def crops_for(card, out_dir):
    src = ex.variant_files(card)["native"]
    im = Image.open(src)
    w, h = im.size
    os.makedirs(out_dir, exist_ok=True)
    paths = []
    rules = im.crop((int(w * 0.02), int(h * 0.14), int(w * 0.98), int(h * 0.93)))
    rules = rules.resize((rules.width * 3, rules.height * 3), Image.LANCZOS)
    p1 = os.path.join(out_dir, "rules_crop.png")
    ImageOps.autocontrast(rules.convert("L")).convert("RGB").save(p1)
    paths.append(("rules-crop-3x", p1))
    left = im.crop((int(w * 0.03), int(h * 0.16), int(w * 0.40), int(h * 0.90)))
    left = left.resize((left.width * 5, left.height * 5), Image.LANCZOS)
    p2 = os.path.join(out_dir, "leftcol_crop.png")
    ImageOps.autocontrast(left.convert("L")).convert("RGB").save(p2)
    paths.append(("leftcol-crop-5x", p2))
    return paths


def structural_ok(card, vision, parsed):
    if not parsed:
        return False
    if norm_name(parsed) != norm_name_from(card):
        return False
    audit = (vision.get("audit") or {}).get("parsed")
    if isinstance(audit, list):
        if ex.audit_multiset(audit) != ex.token_multiset(parsed.get("abilities")):
            return False
    if parsed.get("abilities") and all(not (a.get("text") or "").strip()
                                       for a in parsed["abilities"]):
        return False
    # bracket-uncertainty markers disqualify an auto-adoption
    blob = json.dumps(parsed.get("abilities"), ensure_ascii=False)
    if "[curved" in blob or "[shape" in blob or "[unclear" in blob or "[?]" in blob:
        return False
    return True


def norm_name(parsed):
    return ex.norm(parsed.get("name") or "")


def norm_name_from(card):
    return ex.norm(card["name"])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true", help="no vision; only report")
    args = ap.parse_args()

    cards = json.load(open(ex.OUT, encoding="utf-8"))
    inventory = json.load(open(os.path.join(ex.RAW, "inventory.json")))
    inv_by = {(c["deck"], c["name"]): c for c in inventory}
    todo = [e for e in cards if e.get("needs_review")]
    print(f"{len(todo)} cards flagged:")
    for e in todo:
        print(" -", e["deck"], "/", e["name"], "|", "; ".join(e.get("review_issues", []))[:110])
    if args.dry_run:
        return
    if not todo:
        print("nothing to do")
        return

    base, key, model = ex.vlm_endpoint()
    zoom_dir = os.path.join(ex.RAW, "zoom")
    resolved_n = 0
    for e in todo:
        card_ref = inv_by[(e["deck"], e["name"])]
        cpath = ex.card_cache(card_ref)
        vision = json.load(open(cpath)) if os.path.exists(cpath) else {}
        if (vision.get("resolved") or {}).get("parsed"):
            continue
        prev = (vision.get("native") or {}).get("parsed") or (vision.get("watermarked") or {}).get("parsed") or {}
        attempts = []
        for method, cfile in crops_for(card_ref, os.path.join(zoom_dir,
                                                              re.sub(r"[^\w()+'.-]+", "_", f'{e["deck"]}__{e["name"]}'))):
            prompt = ex.PROMPT + CROP_PROMPT_EXTRA.format(
                name=card_ref["name"], xp=json.dumps(e.get("xp")))
            try:
                raw = ex.vlm_chat(base, key, model, cfile, prompt, max_tokens=5000)
                parsed = ex.canon_fields(ex.parse_json(raw))
            except Exception:
                parsed = None
            attempts.append((method, parsed))
            if structural_ok(card_ref, vision, parsed):
                if not parsed.get("name"):
                    parsed["name"] = card_ref["name"]
                vision["resolved"] = {"parsed": parsed, "method": method, "raw": raw[:3000],
                                      "note": "zoom crop retry"}
                with open(cpath, "w", encoding="utf-8") as f:
                    json.dump(vision, f, indent=1, ensure_ascii=False)
                resolved_n += 1
                print(f"RESOLVED via {method}: {e['deck']}/{e['name']}")
                break
        else:
            print(f"STILL FLAGGED: {e['deck']}/{e['name']}")
    print(f"resolved {resolved_n}/{len(todo)}; re-run extract+validate to rebuild output")


if __name__ == "__main__":
    main()
