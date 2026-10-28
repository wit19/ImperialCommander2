#!/usr/bin/env python3
"""M12a targeted repair for the 4 cards the batch passes could not clean:
Hutt Mercenaries/Scouted, Power of the Dark Side/Embrace Fear,
Technological Superiority/Jetpacks (left-column crop truncated the text),
Inspiring Leadership/Field Officer (never parsed; it is the deck's starter).

Method: render FULL-WIDTH rules crops (both image variants, 3-4x Lanczos,
autocontrast) - unlike retry_zoom's left-column crop these always contain the
complete rules text - and re-run the strict transcription prompt with an
extra completeness order. A crop is adopted only if:
  - printed name matches the path ground truth (<=1 edit),
  - symbol-audit multiset matches the transcribed tokens,
  - the garble detector finds no truncated words,
  - no bracket-uncertainty markers.
Adopted crops land in vision["resolved"] (merge prefers that layer); a
leftcol-based previous resolution is demoted to "resolved_discarded".
Honest flagging: cards still failing stay needs_review.

Run: python3 wiki_data/tools/fix_m12a_cards.py [--dry-run]
"""
import argparse, json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import extract_imperial_class_cards as ex
import retry_zoom_imperial_class_cards as rz

from PIL import Image, ImageOps  # noqa: E402

TARGETS = [
    ("Hutt Mercenaries", "Scouted"),
    ("Power of the Dark Side", "Embrace Fear"),
    ("Technological Superiority", "Jetpacks"),
    ("Inspiring Leadership", "Field Officer"),
]

COMPLETENESS_ORDER = """
CRITICAL: transcribe the rules text COMPLETELY, word for word, from the very
first word to the very last printed word. Do not summarise, do not skip line
fragments, do not stop early. Every line of the rules area must appear. If one
word is unreadable, write [illegible] for exactly that word and continue."""


def full_width_crops(card_ref, out_dir):
    src = ex.variant_files(card_ref)["watermarked"]
    native = ex.variant_files(card_ref)["native"]
    os.makedirs(out_dir, exist_ok=True)
    paths = []
    for tag, path, zoom in (("wm-rules-3x", src, 3), ("nat-rules-4x", native, 4)):
        im = Image.open(path)
        w, h = im.size
        rules = im.crop((int(w * 0.02), int(h * 0.13), int(w * 0.99), int(h * 0.94)))
        rules = rules.resize((rules.width * zoom, rules.height * zoom), Image.LANCZOS)
        p = os.path.join(out_dir, tag.replace("-", "_") + ".png")
        ImageOps.autocontrast(rules.convert("L")).convert("RGB").save(p)
        paths.append((tag, p))
    return paths


def acceptable(card_ref, vision, parsed):
    if not parsed:
        return False
    if not ex.edit_le_1(ex.norm(parsed.get("name") or ""), ex.norm(card_ref["name"])):
        return False
    audit = (vision.get("audit") or {}).get("parsed")
    if isinstance(audit, list) and \
            ex.audit_multiset(audit) != ex.token_multiset(parsed.get("abilities")):
        return False
    blob = json.dumps(parsed.get("abilities"), ensure_ascii=False)
    if any(m in blob for m in ("[curved", "[shape", "[unclear", "[?]")):
        return False
    text = "\n".join((a.get("text") or "") for a in parsed.get("abilities") or [])
    if ex.garbled_tokens(text):
        return False
    return bool(text.strip())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true", help="only render crops, no vision")
    args = ap.parse_args()

    inventory = json.load(open(os.path.join(ex.RAW, "inventory.json")))
    inv_by = {(c["deck"], c["name"]): c for c in inventory}
    base, key, model = ex.vlm_endpoint() if not args.dry_run else (None, None, None)

    for deck, name in TARGETS:
        card_ref = inv_by[(deck, name)]
        cpath = ex.card_cache(card_ref)
        vision = json.load(open(cpath)) if os.path.exists(cpath) else {}
        if (vision.get("resolved") or {}).get("verified_fix"):
            print("SKIP (already fixed):", deck, "/", name)
            continue
        crop_dir = os.path.join(ex.RAW, "zoom",
                                re.sub(r"[^\w()+'.-]+", "_", f"{deck}__{name}") + "_fix")
        prev = (vision.get("resolved") or {}).get("parsed") or \
            (vision.get("native") or {}).get("parsed") or {}
        for method, cfile in full_width_crops(card_ref, crop_dir):
            if args.dry_run:
                print("crop:", cfile)
                continue
            prompt = ex.PROMPT + rz.CROP_PROMPT_EXTRA.format(
                name=card_ref["name"], xp=json.dumps((prev or {}).get("xp"))) + COMPLETENESS_ORDER
            try:
                raw = ex.vlm_chat(base, key, model, cfile, prompt, max_tokens=6000)
                parsed = ex.parse_json(raw)
                if not isinstance(parsed, dict):
                    parsed = None
                parsed = ex.canon_fields(parsed) if parsed else None
            except Exception as e:
                print("call failed:", deck, "/", name, method, repr(e)[:120])
                continue
            if acceptable(card_ref, vision, parsed):
                if not parsed.get("name"):
                    parsed["name"] = card_ref["name"]
                if vision.get("resolved"):
                    vision["resolved_discarded"] = vision.pop("resolved")
                vision["resolved"] = {"parsed": parsed, "method": method,
                                      "raw": raw[:3000], "verified_fix": True,
                                      "note": "full-width rules-crop targeted repair"}
                with open(cpath, "w", encoding="utf-8") as f:
                    json.dump(vision, f, indent=1, ensure_ascii=False)
                print("FIXED", method, ":", deck, "/", name)
                break
            else:
                print("still bad:", deck, "/", name, method)
        else:
            if not args.dry_run:
                print("NOT FIXED (stays needs_review):", deck, "/", name)


if __name__ == "__main__":
    main()
