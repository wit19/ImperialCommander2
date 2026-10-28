#!/usr/bin/env python3
"""M12a cross-check: validate extracted Imperial Class Deck card texts against the
'IP Class Deck Review' blog series (wretchedhivearmada.blogspot.com, one post per
deck, cached under wiki_data/raw/imperial_class_cards/blogs/).

Method (per card):
  1. Locate the card's section in its deck's blog post (name match, tolerant).
     Cards the blogger never mentions -> verdict "not_in_blog" (no penalty; the
     basic cards are typically skipped).
  2. Judge call (same local vLLM endpoint, temperature 0, cached per
     card-text-hash): is the transcription a faithful rendering of what the blog
     describes? Only MEANING differences count (missing/extra ability, reversed
     effect, contradicted number) - the blog paraphrases loosely by design.
  3. verdict "suspicious" -> automatic re-extraction on each image variant
     (max 2 passes, recorded in the card's vision cache under "retries"),
     re-judge after each; still suspicious -> "needs_review": true (no guessing).

Run:  python3 wiki_data/tools/validate_imperial_class_cards.py [--dry-run]
      --dry-run: no network/LLM; recompute the final JSON purely from caches.
"""
import argparse, hashlib, json, os, re, sys, threading

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import extract_imperial_class_cards as ex   # noqa: E402

RAW, WD, VIS, OUT = ex.RAW, ex.WD, ex.VIS, ex.OUT
BLOGS = os.path.join(RAW, "blogs")
VAL = os.path.join(RAW, "vision", "validation")
FANDOM_GT = os.path.join(RAW, "fandom_groundtruth.json")

def load_fandom():
    """(deck, norm(name)) -> fandom record, plus per-deck name list."""
    if not os.path.exists(FANDOM_GT):
        return {}, {}
    gt = json.load(open(FANDOM_GT, encoding="utf-8"))
    by_name, names = {}, {}
    for deck, cards in gt.items():
        names[deck] = cards
        for c in cards:
            by_name[(deck, ex.norm(c["name"]))] = c
    return by_name, names

BLOG_FILE = {
    "Technological Superiority": "technological_superiority",
    "Military Might": "military_might",
    "Subversive Tactics": "subversive_tactics",
    "Inspiring Leadership": "inspiring_leadership",
    "Armored Onslaught": "armored_onslaught",
    "Precision Training": "precision_training",
    "Imperial Black Ops": "imperial_black_ops",
    "Hutt Mercenaries": "hutt_mercenaries",
    "Nemeses": "nemeses",
    "Power of the Dark Side": "power_of_dark_side",
    "Reactive Defenses": "reactive_defenses",
    "Overwhelming Oppression": "overwhelming_oppression",  # blog title: "Suppression" (author slip)
}

def blog_text(deck):
    path = os.path.join(BLOGS, BLOG_FILE[deck] + ".html")
    html = open(path, encoding="utf-8", errors="replace").read()
    m = re.search(r'<div class="post-body[^"]*"[^>]*>(.*?)<div class="post-footer', html, re.S)
    body = m.group(1) if m else html
    body = re.sub(r"<script.*?</script>|<style.*?</style>", " ", body, flags=re.S)
    body = re.sub(r"<br[^>]*>|</p>|</div>|</li>", "\n", body)
    body = re.sub(r"<[^>]+>", " ", body)
    body = body.replace("&nbsp;", " ").replace("&amp;", "&").replace("&#39;", "'")
    body = body.replace("&rsquo;", "'").replace("&lsquo;", "'").replace("&ldquo;", '"').replace("&rdquo;", '"')
    body = re.sub(r"[ \t]+", " ", body)
    return body

def card_section(text, name, alt=""):
    """± window around the first occurrence of the card name in the post."""
    pat = re.compile(re.escape(name), re.I)
    hits = list(pat.finditer(text))
    if not hits and alt:
        hits = list(re.compile(re.escape(alt), re.I).finditer(text))
    if not hits:                       # spaced-tolerant: "Lead By Example" vs "Lead by Example"
        flexible = re.compile(r"\s+".join(re.escape(w) for w in name.split()), re.I)
        hits = list(flexible.finditer(text))
    if not hits:
        return None
    h = hits[0]
    return text[max(0, h.start() - 200):h.start() + 1800]

JUDGE_PROMPT = """You are quality-checking a card transcription. The TRANSCRIPTION claims to be the verbatim printed text of a Star Wars: Imperial Assault Imperial Class Deck card. The REFERENCE EXCERPT is one or more human paraphrases of the same card from a fan wiki and/or an informal review blog (these sources paraphrase loosely and may themselves contain small errors).

Decide: does the transcription COVER the same rules as the blog describes, without contradicting it? A transcription may legitimately contain MORE detail than the blog (it is verbatim). Contradictions, an entirely missing or extra ability, or effects the blog clearly describes but the transcription lacks -> "suspicious". Pure wording differences, the blog omitting details, or the blog being vague -> "ok".

Answer ONLY JSON: {"verdict":"ok"|"suspicious"|"unknowable","reason":"<max 25 words>"}

CARD: {card}
TRANSCRIPTION:
{tr}

REFERENCE EXCERPT:
{ex}"""

def judge(base, key, model, card_name, transcription, excerpt):
    prompt = (JUDGE_PROMPT.replace("{card}", card_name)
              .replace("{tr}", transcription).replace("{ex}", excerpt[:3500]))
    # NB: this endpoint is a reasoning model - it burns tokens on the reasoning
    # trace first. A small budget (120) hits finish_reason=length before any
    # final JSON appears (observed 2026-10-28: 108/109 verdicts degraded to
    # "unknowable"). Budget must comfortably cover reasoning + answer.
    payload = {"model": model, "temperature": 0, "max_tokens": 2500,
               "messages": [{"role": "user", "content": prompt}]}
    import urllib.request, urllib.parse
    req = urllib.request.Request(base.rstrip("/") + "/chat/completions",
                                 data=json.dumps(payload).encode(),
                                 headers={"Content-Type": "application/json",
                                          "Authorization": "Bearer " + key})
    import time
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=180) as r:
                d = json.loads(r.read().decode())
            msg = d["choices"][0]["message"]
            raw = msg.get("content") or msg.get("reasoning") or msg.get("reasoning_content")
            if not raw:
                raise RuntimeError("empty completion")
            j = ex.parse_json(raw)
            v = (j or {}).get("verdict", "unknowable")
            return v if v in ("ok", "suspicious", "unknowable") else "unknowable", raw
        except Exception:
            if attempt == 4:
                return "error", "judge call failed"
            time.sleep(2 * (attempt + 1))

def validation_cache(card, text_hash):
    return os.path.join(VAL, re.sub(r"[^\w()+'.-]+", "_",
                                    f'{card["deck"]}__{card["name"]}__{text_hash[:16]}') + ".json")

def abilities_text(entry):
    return "\n".join(((a["cost"] + ": ") if a.get("cost") else "") + a.get("text", "")
                     for a in entry.get("abilities", [])) or "(no abilities parsed)"

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true",
                    help="no LLM/network: recompute final JSON from cached verdicts")
    args = ap.parse_args()
    os.makedirs(VAL, exist_ok=True)

    cards = json.load(open(OUT, encoding="utf-8"))
    inventory = json.load(open(os.path.join(RAW, "inventory.json")))
    inv_by = {(c["deck"], c["name"]): c for c in inventory}
    base, key, model = ex.vlm_endpoint()

    texts = {d: blog_text(d) for d in BLOG_FILE}
    fan_by_name, fan_names = load_fandom()
    lock = threading.Lock()
    stats = {"ok": 0, "suspicious": 0, "not_in_blog": 0, "unknowable": 0, "error": 0}

    def excerpt_for(entry):
        """Fandom bullet text (close to printed) when known, plus blog window."""
        parts = []
        fan = fan_by_name.get((entry["deck"], ex.norm(entry["name"])))
        if fan and fan.get("text"):
            parts.append("[fan wiki]\n" + "\n".join(fan["text"]))
        blog = card_section(texts[entry["deck"]], entry["name"],
                            alt=entry.get("deck_banner", ""))
        if blog:
            parts.append("[review blog]\n" + blog)
        return "\n---\n".join(parts) if parts else None

    def check(entry):
        deck = entry["deck"]
        excerpt = excerpt_for(entry)
        text_hash = hashlib.sha256(abilities_text(entry).encode()).hexdigest()
        cache = validation_cache(entry, text_hash)
        card_ref = inv_by[(deck, entry["name"])]
        if excerpt is None:
            verdict, reason = "not_in_blog", "card not mentioned in the deck's blog post"
        elif os.path.exists(cache):
            v = json.load(open(cache))
            verdict, reason = v["verdict"], v.get("reason", "")
        elif args.dry_run:
            raise SystemExit(f"--dry-run: missing judge cache for {deck}/{entry['name']}")
        else:
            verdict, raw = judge(base, key, model, entry["name"],
                                 abilities_text(entry), excerpt)
            j = ex.parse_json(raw) or {}
            if not isinstance(j, dict):
                j = {}
            reason = str(j.get("reason", ""))[:200]
            with lock:
                with open(cache, "w", encoding="utf-8") as f:
                    json.dump({"verdict": verdict, "reason": str(j.get("reason", ""))[:200],
                               "excerpt_found": True}, f, ensure_ascii=False)
        return entry, card_ref, verdict, reason

    results = [check(e) for e in cards]        # sequential: politeness + simple determinism

    sus, cleared_set = {}, set()
    for entry, card_ref, verdict, reason in results:
        stats[verdict] = stats.get(verdict, 0) + 1
        if verdict in ("ok", "not_in_blog", "unknowable"):
            continue
        # suspicious/error: re-extract on each variant (max 2 passes), re-judge.
        vpath = ex.card_cache(card_ref)
        vision = json.load(open(vpath)) if os.path.exists(vpath) else {}
        # replay cached re-extractions first (dry-determinism + fast path)
        cleared = False
        if vision.get("retries"):
            merged_f, _i, _n = ex.merge_card(card_ref, vision)
            t = abilities_text(merged_f) if merged_f else ""
            nc = validation_cache(entry, hashlib.sha256(t.encode()).hexdigest())
            if t and os.path.exists(nc) and json.load(open(nc))["verdict"] == "ok":
                cleared = True
        if cleared:
            cleared_set.add((card_ref["deck"], card_ref["name"]))
            continue
        if args.dry_run:
            sus[(card_ref["deck"], card_ref["name"])] = reason[:120]
            continue
        cleared = False
        for variant in ("native", "watermarked"):
            if cleared:
                break
            if len([r for r in vision.get("retries", [])]) >= 2:
                break
            try:
                one = ex.run_vision({**card_ref, "name": card_ref["name"]}, base, key, model)
                p = (one.get(variant) or {}).get("parsed")
            except Exception:
                p = None
            if not p:
                continue
            vision.setdefault("retries", []).append(
                {"variant": variant, "parsed": p, "note": f"blog judge: {reason[:120]}"})
            with open(vpath, "w", encoding="utf-8") as f:
                json.dump(vision, f, indent=1, ensure_ascii=False)
            merged, issues, _notes = ex.merge_card(card_ref, vision)
            new_text = abilities_text(merged) if merged else ""
            if merged and new_text:
                excerpt = card_section(texts[entry["deck"]], entry["name"],
                                       alt=entry.get("deck_banner", ""))
                nhash = hashlib.sha256(new_text.encode()).hexdigest()
                ncache = validation_cache(entry, nhash)
                if os.path.exists(ncache):
                    v2 = json.load(open(ncache))["verdict"]
                elif args.dry_run:
                    break
                else:
                    v2, raw2 = judge(base, key, model, entry["name"], new_text, excerpt or "")
                    j2 = ex.parse_json(raw2) or {}
                    if not isinstance(j2, dict):
                        j2 = {}
                    with open(ncache, "w", encoding="utf-8") as f:
                        json.dump({"verdict": v2, "reason": str(j2.get("reason", ""))[:200]}, f,
                                  ensure_ascii=False)
                if v2 == "ok":
                    cleared = True
                    entry["_reextracted"] = variant
        if not cleared and verdict == "suspicious":
            sus[(card_ref["deck"], card_ref["name"])] = reason[:120]
        elif cleared:
            cleared_set.add((card_ref["deck"], card_ref["name"]))

    # re-apply merge state (retries above changed vision caches)
    out2, needs_review = [], []
    for card_ref in sorted(inventory, key=lambda c: (c["deck"], c["name"])):
        cache = ex.card_cache(card_ref)
        vision = json.load(open(cache)) if os.path.exists(cache) else {}
        merged, issues, notes = ex.merge_card(card_ref, vision)
        entry = dict(merged) if merged else {
            "deck": card_ref["deck"], "name": card_ref["name"], "basic": None,
            "xp": None, "restriction": None, "keyword": None,
            "deck_banner": card_ref["deck"], "abilities": []}
        key_k = (card_ref["deck"], card_ref["name"])
        # deterministic fan-wiki cross-check (names + XP), independent of the judge
        fan = fan_by_name.get(key_k[:0] + (card_ref["deck"], ex.norm(card_ref["name"])))
        if fan is None:
            cand = [c for c in fan_names.get(card_ref["deck"], [])
                    if ex.edit_le_1(ex.norm(c["name"]), ex.norm(card_ref["name"]))]
            fan = cand[0] if len(cand) == 1 else None
        if fan is not None and fan["name"] != card_ref["name"] \
                and not ex.edit_le_1(ex.norm(fan["name"]), ex.norm(card_ref["name"])):
            fan = None
        if fan is not None and fan.get("starter") and entry.get("xp") is None:
            # merge flags "non-basic without xp" when a variant's basic field
            # disagrees with xp=null; the independent fan-wiki starter flag
            # resolves it in favour of the (ground-truth) null XP.
            issues = [i for i in issues if i != "non-basic without xp"]
        if fan_names.get(card_ref["deck"]) and fan is None:
            issues = [i for i in issues if not i.startswith("name mismatch")]
            issues.append("fan-wiki cross-check: name not found in wiki deck list")
        elif fan is not None:
            if ex.norm(fan["name"]) != ex.norm(card_ref["name"]):
                # >1-edit difference: source-site filename typo unless vision sides with path
                vnames = [((vision.get(k) or {}).get("parsed") or {}).get("name")
                          for k in ("resolved", "native", "watermarked")]
                side_path = any(n and ex.edit_le_1(ex.norm(n or ""), ex.norm(card_ref["name"]))
                                and not ex.edit_le_1(ex.norm(n or ""), ex.norm(fan["name"]))
                                for n in vnames if n)
                if side_path:
                    issues.append(f'fan wiki spells it "{fan["name"]}" (path+vision agree; '
                                  f'kept path name)')
                else:
                    issues = [i for i in issues if not i.startswith("name mismatch")]
                    issues.append(f'name-vs-wiki: path "{card_ref["name"]}" vs '
                                  f'wiki "{fan["name"]}" (kept path name)')
                    notes = (notes or []) + [f'printed_name likely "{fan["name"]}"']
            if fan["starter"]:
                if entry.get("xp") is not None or entry.get("basic") is not True:
                    issues.append(f'fan wiki says starter card (no XP); got xp={entry.get("xp")}')
            elif fan["xp"] is not None:
                if entry.get("xp") != fan["xp"] or entry.get("basic") is True:
                    issues.append(f'XP mismatch vs fan wiki: extracted '
                                  f'{entry.get("xp")} vs wiki {fan["xp"]}')
        if key_k in sus and key_k not in cleared_set:
            issues.append("blog cross-check suspicious: " + sus[key_k])
        # deterministic text-quality checks (no LLM, cache-pure -> dry-run stable)
        g = ex.garbled_tokens(abilities_text(entry))
        if g:
            issues.append(f"possible garbled transcription (truncated words): {sorted(set(g))}")
        vres = vision.get("resolved") or {}
        if str(vres.get("method", "")).startswith("leftcol") \
                and len(abilities_text(entry)) < 40:
            issues.append("zoom resolution came from left-column crop; text likely "
                          "incomplete (left crop sees only the icon column)")
        entry["needs_review"] = bool(issues)
        if issues:
            entry["review_issues"] = issues
            needs_review.append(f'{card_ref["deck"]}/{card_ref["name"]}')
        if notes:
            entry["notes"] = notes
        entry["images"] = {v: os.path.relpath(p, WD) for v, p in ex.variant_files(card_ref).items()
                           if os.path.exists(p)}
        entry["image_urls"] = {"native": card_ref["native_url"],
                               "watermarked": card_ref.get("boardwars_img")}
        out2.append(entry)

    blob = json.dumps(out2, indent=1, ensure_ascii=False) + "\n"
    if args.dry_run and os.path.exists(OUT):
        same = open(OUT, encoding="utf-8").read() == blob
        print("DRY-RUN " + ("OK: final JSON identical from caches" if same
                            else "FAIL: cache rebuild differs"))
        sys.exit(0 if same else 1)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(blob)
    print(json.dumps({"cards": len(out2), "judge_stats": stats,
                      "needs_review": needs_review}, indent=1))

if __name__ == "__main__":
    main()
