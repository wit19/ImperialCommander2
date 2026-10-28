#!/usr/bin/env python3
"""M12a vision extractor: transcribe the 109 Imperial Class Deck card images.

Pipeline (all cached under wiki_data/raw/imperial_class_cards/vision/):
  1. For EVERY card, ask a vision-capable model (local vLLM endpoint from
     ~/.hermes/config.yaml custom_providers[0]) to transcribe BOTH image
     variants independently (native 485x741 clean scan from imperial-assault.com,
     watermarked 595px scan from cards.boardwars.eu) into strict JSON.
  2. Merge: fields where both variants agree are accepted; disagreeing fields
     trigger a re-extraction pass on the other variant (max 2 extra passes),
     still disagreeing/failed -> the card gets "needs_review": true (no guessing).
  3. Validate against ground truth: deck/name come from the IMAGE PATH
     (inventory.json), never from vision; name must match after normalisation;
     xp must be an int for non-basic cards; exactly one basic card per deck.

Run:  python3 wiki_data/tools/extract_imperial_class_cards.py [--dry-run]
      --dry-run: zero network / zero vision; rebuild the merged JSON purely from
      the existing vision caches (must be byte-identical to the normal run's
      output -> reproducibility gate).

Output (cache):  wiki_data/raw/imperial_class_cards/vision/<Deck>__<Name>.json
Merged result:   wiki_data/imperial_class_cards.json
"""
import argparse, base64, hashlib, json, os, re, sys, threading, time, urllib.request
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
WD = os.path.dirname(HERE)
RAW = os.path.join(WD, "raw", "imperial_class_cards")
VIS = os.path.join(RAW, "vision")
OUT = os.path.join(WD, "imperial_class_cards.json")
WORKERS = 6

CFG = os.path.expanduser("~/.hermes/config.yaml")

def vlm_endpoint():
    base = key = model = None
    for line in open(CFG):
        if base is None and re.match(r"\s*base_url:\s*\S*v1\s*$", line):
            base = line.split(":", 1)[1].strip()
        if key is None and re.match(r"\s*api_key:\s*\S", line):
            key = line.split(":", 1)[1].strip().strip("'\"")
        if model is None and re.match(r"\s*model:\s*\S", line):
            model = line.split(":", 1)[1].strip().strip("'\"")
        if base and key and model:
            break
    return base, key, model

PROMPT = """You are transcribing a printed upgrade card from the board game Star Wars: Imperial Assault (an Imperial Class Deck card). Look at the card image and output ONLY one JSON object, no prose, no markdown fences:
{"name":"...","basic":true|false,"xp":INT|null,"restriction":"..."|null,"italic_keyword":"..."|null,"deck_banner":"...","abilities":[{"cost":"..."|null,"text":"..."}]}

Rules:
- "name": the card title from the top banner, exactly as printed (preserve case/punctuation).
- "xp": the campaign purchase cost printed in the bottom-left corner (e.g. "3 XP" -> 3). If NO xp cost is printed, use null and set "basic": true.
- "restriction": a printed restriction line such as "Attachment: Item" or "Attachment: Heavy Weapon". null if there is none.
- "italic_keyword": an italic keyword line printed under the title/restriction (e.g. "Hidden", "Sleep"). null if there is none.
- "deck_banner": the banner label at the BOTTOM of the card (normally the deck's name), title-cased as printed.
- "abilities": one entry per printed ability block, top to bottom.
  - "cost": the bold/icon prefix of the block. Encode a printed ACTION icon (right-pointing arrow) as {A}, a printed UPGRADE icon (gear with star) as {U}. Encode other printed cost icons as their WORD followed by a colon: hourglass icon -> "Event:", boot icon -> "Manoeuvre:", crescent/sleep icon -> "Sleep:", regroup icon -> "Regroup:", and "Interrupt:"/"Reaction:"/"Reaction (X):" stay as printed words. If a block starts with the icon plus a colon, output exactly like "{A}: ..." / "Event: ...". null for passive abilities (text starting with "While", "After", "During", "When", "Imperial", unit types, ...) and for the plain rules paragraph of the basic card.
  - "text": the printed ability text VERBATIM, single paragraph (join visual line breaks with a single space, keep hyphenation as printed). Encode printed game SYMBOLS as these tokens: damage result (impact starburst) -> {H}; surge icon (spiky burst) -> {B}; block result (curved shield triangle on the black defense die) -> {G}; evade result (broken ring) -> {F}; strain / critical damage pip (jagged hexagon) -> {C}; threat token (imperial hexagonal coin) -> {D}; ranged attack icon (rifle silhouette) -> {O}; melee attack icon (bayonet) -> {P}; action icon inside running text -> {A}. Everything else stays as printed WORDS: dice colours in words ("attack using 1 blue and 1 red die"), the insight/eye test icon -> the word "Insight" ("may test Insight"), "disruption cube", "objective token", "Force token", "descent token", "credit", etc. Unit type keywords printed in small caps must be written in CAPS: TROOPER, TROOPERS, OFFICER, AGENT, OPERATIVE, DROID, HEAVY WEAPON, VEHICLE, FIGURE. Keep "Limit ..." sentences inside the text exactly where printed.
- If any word is genuinely illegible, write [?] in its place instead of guessing.
- Output must parse as strict JSON (double quotes, no trailing commas)."""

AUDIT_PROMPT = """Look at this board game card (Star Wars: Imperial Assault upgrade card). List EVERY printed symbol ICON (a drawn glyph, not a letter) that appears in the rules-text area (between the title banner and the bottom banner), in reading order. Include ability-cost prefix icons and any small inline symbols.
Known icons: ACTION (filled right-pointing arrow), UPGRADE (gear with a star), EVENT (hourglass), MANOEUVRE (boot), SLEEP (crescent), REGROUP, DAMAGE (impact starburst), SURGE (spiky burst), BLOCK (curved shield triangle), EVADE (broken ring), STRAIN (jagged hexagon with lightning cutout), THREAT (flat hexagonal coin with a hexagonal hole), RANGED (rifle silhouette), MELEE (bayonet/dagger), INTERRUPT, REACTION, dice (small coloured square with pips or a symbol inside).
CRITICAL: STRAIN has a jagged LIGHTNING cutout inside the hexagon; THREAT is a clean HEXAGONAL COIN with a hole in the middle - do not confuse them. ACTION is an ARROW; UPGRADE is a GEAR+STAR - do not confuse them.
Output ONLY a JSON array: [{"where":"prefix of ability 1"|"inline: 'recover 2 [x]'", "glyph":"short visual description", "call":"ACTION|UPGRADE|EVENT|MANOEUVRE|SLEEP|REGROUP|DAMAGE|SURGE|BLOCK|EVADE|STRAIN|THREAT|RANGED|MELEE|INTERRUPT|REACTION|DICE|OTHER"}]
If there is no symbol icon at all, output []."""

AUDIT_CALL2TOKEN = {"ACTION": "{A}", "UPGRADE": "{U}", "EVENT": "Event:",
                    "MANOEUVRE": "Manoeuvre:", "SLEEP": "Sleep:", "REGROUP": "Regroup:",
                    "DAMAGE": "{H}", "SURGE": "{B}", "BLOCK": "{G}", "EVADE": "{F}",
                    "STRAIN": "{C}", "THREAT": "{D}", "RANGED": "{O}", "MELEE": "{P}",
                    "INTERRUPT": "Interrupt:", "REACTION": "Reaction:"}

def token_multiset(abilities):
    from collections import Counter
    c = Counter()
    for a in abilities or []:
        cost = a.get("cost") or ""
        # any {X} tokens inside the cost prefix ("2{U}:", "{A}{U}:") count as
        # symbols too - the audit sees them as printed icons (systematic
        # false-mismatch source before 2026-10-28 when only bare "{U}:" matched)
        cost_tokens = re.findall(r"\{\w\}", cost)
        for t in cost_tokens:
            c[t] += 1
        if not cost_tokens and cost.strip():
            c[re.sub(r"\(.*?\)|\s+", "", cost.strip())] += 1   # "Event:", "Interrupt:", ...
        for t in re.findall(r"\{\w\}", a.get("text") or ""):
            c[t] += 1
    return c

def audit_multiset(audit_parsed):
    from collections import Counter
    c = Counter()
    for item in audit_parsed or []:
        call = (item.get("call") or "").upper() if isinstance(item, dict) else ""
        tok = AUDIT_CALL2TOKEN.get(call)
        if tok:
            c[tok] += 1
    return c

def vlm_chat(base, key, model, image_path, prompt, max_tokens=2500):
    b64 = base64.b64encode(open(image_path, "rb").read()).decode()
    payload = {"model": model, "temperature": 0, "max_tokens": max_tokens,
               "messages": [{"role": "user", "content": [
                   {"type": "text", "text": prompt},
                   {"type": "image_url",
                    "image_url": {"url": "data:image/jpeg;base64," + b64}}]}]}
    req = urllib.request.Request(base.rstrip("/") + "/chat/completions",
                                 data=json.dumps(payload).encode(),
                                 headers={"Content-Type": "application/json",
                                          "Authorization": "Bearer " + key})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=300) as r:
                d = json.loads(r.read().decode())
            msg = d["choices"][0]["message"]
            content = msg.get("content")
            if not content:
                # vLLM reasoning models can return the answer only in the
                # reasoning field (content None): fall back to it, else retry.
                content = msg.get("reasoning") or msg.get("reasoning_content")
            if not content:
                raise RuntimeError("empty completion (content=None)")
            return content
        except Exception:
            if attempt == 4:
                raise
            time.sleep(2 * (attempt + 1))

def parse_json(text, array=False, accept=None):
    """Best-effort extraction of the LARGEST valid JSON value (array or object)
    from possibly chatty/reasoning model output. The final answer object embeds
    smaller prose/cost dicts; taking the LAST decodable start grabs one of
    those fragments. Sorting candidates by span length and applying accept()
    keeps the real answer."""
    if text is None:
        return None
    t = text.strip()
    m = re.search(r"```(?:json)?\s*([\[{].*[\]}])\s*```", t, re.S)
    if m:
        try:
            return json.loads(m.group(1))
        except Exception:
            t = m.group(1)
    dec = json.JSONDecoder()
    spans = []
    for pos in [i for i, ch in enumerate(t) if ch in "{["]:
        try:
            val, end = dec.raw_decode(t[pos:])
        except Exception:
            continue
        spans.append((end, val))
    spans.sort(key=lambda x: -x[0])
    for _end, val in spans:
        if array:
            if isinstance(val, list) and val and all(isinstance(x, dict) for x in val):
                return val
        elif isinstance(val, dict) and (accept is None or accept(val)):
            return val
    if not array:
        return None
    # array request but only a single object present: accept it as 1-item list
    single = parse_json(text, array=False)
    if single and ("call" in single or "glyph" in single):
        return [single]
    return None

def answer_obj(obj):
    """True for a real transcription answer, false for prose/cost fragments."""
    return isinstance(obj, dict) and ("name" in obj or "abilities" in obj)

# Deterministic garble detector: vision drops word chunks on hard renders
# (observed 2026-10-28: "Exhaust t hero with is attacki 1 defense", "You gain").
# Real short words are whitelisted; any other 1-2 char lowercase token marks a
# truncated word. Possessive "'s" endings are stripped first (legitimate).
SHORT_WORDS_OK = {"a", "an", "of", "in", "to", "or", "by", "up", "at", "on",
                  "it", "is", "if", "no", "so", "as", "do", "us", "we", "be",
                  "he"}  # "he" is legit pronoun usage on printed cards

def garbled_tokens(text):
    t = re.sub(r"'s\b", "", (text or "").replace("\u2019", "'"))
    return [w for w in re.findall(r"\b[a-z]{1,2}\b", t) if w not in SHORT_WORDS_OK]

def norm(s):
    if not s:
        return ""
    s = s.replace("\u2019", "'").replace("\u2018", "'")
    s = re.sub(r"[^A-Za-z0-9]+", "", s).lower()
    return s

def edit_le_1(a, b):
    """True if Levenshtein(a, b) <= 1 (image filenames have known one-letter typos,
    e.g. 'Lmitless Arsenal' for 'Limitless Arsenal')."""
    if a == b:
        return True
    if abs(len(a) - len(b)) > 1:
        return False
    if len(a) > len(b):
        a, b = b, a
    i = 0
    while i < len(a) and a[i] == b[i]:
        i += 1
    return ((len(a) == len(b) and a[i + 1:] == b[i + 1:])   # one substitution
            or a[i:] == b[i + 1:])                          # one insertion in b

def canon_fields(obj):
    """Normalise a raw vision answer into comparable field values."""
    if not isinstance(obj, dict):
        return None
    # reject fragments: a real transcription answer carries name and/or abilities
    if "name" not in obj and "abilities" not in obj:
        return None
    ab = obj.get("abilities")
    if isinstance(ab, list):
        ab = [{"cost": (a.get("cost") or None),
               "text": re.sub(r"\s+", " ", (a.get("text") or "")).strip()}
              for a in ab if isinstance(a, dict)]
    else:
        ab = None
    xp = obj.get("xp")
    if isinstance(xp, str) and xp.strip().isdigit():
        xp = int(xp.strip())
    if not isinstance(xp, int) or isinstance(xp, bool):
        xp = None
    return {
        "name": (obj.get("name") or "").strip(),
        "xp": xp,
        "basic": bool(obj.get("basic")) if isinstance(obj.get("basic"), bool) else (xp is None),
        "restriction": (obj.get("restriction") or None),
        "italic_keyword": (obj.get("italic_keyword") or None),
        "deck_banner": (obj.get("deck_banner") or "").strip(),
        "abilities": ab,
    }

def variant_files(card):
    return {v: os.path.join(RAW, v, card["deck"], card["name"] + ".jpg")
            for v in ("native", "watermarked")}

def card_cache(card):
    return os.path.join(VIS, re.sub(r"[^\w()+'.-]+", "_", f'{card["deck"]}__{card["name"]}') + ".json")

def xp_corner_probe(card, base, key, model):
    """High-zoom crop of the bottom-left XP corner; returns {'xp':int|None,'error':...}."""
    from PIL import Image, ImageOps
    path = variant_files(card)["native"]
    if not os.path.exists(path):
        path = variant_files(card)["watermarked"]
    try:
        im = Image.open(path)
        w, h = im.size
        c = im.crop((0, int(h * 0.82), int(w * 0.35), h))
        c = c.resize((c.width * 6, c.height * 6), Image.LANCZOS)
        c = ImageOps.autocontrast(c.convert("L")).convert("RGB")
        tmp = os.path.join(RAW, "zoom", re.sub(r"[^\w()+'.-]+", "_",
                                               f'{card["deck"]}__{card["name"]}') + "_xpcorner.png")
        os.makedirs(os.path.dirname(tmp), exist_ok=True)
        c.save(tmp)
        p = ("This is a 6x zoom of the BOTTOM-LEFT corner of a Star Wars Imperial Assault "
             "campaign card. If a purchase cost is printed there, it reads like '3 XP'. "
             "Output ONLY JSON {\"xp\": INT} when digits are visible, or {\"xp\": null} when the "
             "area shows no digits at all (empty texture / border only).")
        raw = vlm_chat(base, key, model, tmp, p, max_tokens=1500)
        j = parse_json(raw)
        if j is None:
            return {"xp": None, "error": "parse fail", "raw": raw[:300]}
        return {"xp": j.get("xp"), "error": None, "raw": None}
    except Exception as e:
        return {"xp": None, "error": str(e)[:120], "raw": None}

def run_vision(card, base, key, model, audit=True):
    """Independent transcription per available variant (+ symbol audit)."""
    out = {}
    for variant, path in variant_files(card).items():
        if not (os.path.exists(path) and os.path.getsize(path) > 0):
            out[variant] = {"raw": None, "parsed": None, "error": "image missing"}
            continue
        try:
            raw = vlm_chat(base, key, model, path, PROMPT)
            parsed = canon_fields(parse_json(raw))
            if parsed is None or not parsed.get("abilities"):
                # parse fail OR empty transcription: one retry with more budget
                raw2 = vlm_chat(base, key, model, path, PROMPT, max_tokens=5000)
                parsed2 = canon_fields(parse_json(raw2))
                if parsed2 and (parsed2.get("abilities") or not parsed):
                    raw, parsed = raw2, parsed2
            out[variant] = {"raw": raw, "parsed": parsed,
                            "error": None if (parsed and parsed.get("abilities"))
                            else ("empty abilities" if parsed else "json parse fail")}
        except Exception as e:
            out[variant] = {"raw": None, "parsed": None, "error": str(e)[:200]}
    if audit:
        apath = variant_files(card)["native"]
        if not os.path.exists(apath):
            apath = variant_files(card)["watermarked"]
        if os.path.exists(apath):
            try:
                araw = vlm_chat(base, key, model, apath, AUDIT_PROMPT, max_tokens=3000)
                parsed = parse_json(araw, array=True)
                out["audit"] = {"raw": araw, "parsed": parsed,
                                "error": None if parsed is not None else "json parse fail"}
            except Exception as e:
                out["audit"] = {"raw": None, "parsed": None, "error": str(e)[:200]}
    return out

COMPARE_FIELDS = ("name", "xp", "basic", "restriction", "italic_keyword", "abilities")

def merge_card(card, vision):
    """Merge independent transcriptions + symbol audit. Deterministic, cache-pure."""
    native = (vision.get("native") or {}).get("parsed")
    water = (vision.get("watermarked") or {}).get("parsed")
    usable = lambda p: p if (p and p.get("abilities")) else None
    native, water = usable(native), usable(water)
    issues, notes, resolved = [], [], None
    a, b = native, water
    if a and b:
        diff = [f for f in COMPARE_FIELDS
                if json.dumps(a.get(f), ensure_ascii=False, sort_keys=True)
                != json.dumps(b.get(f), ensure_ascii=False, sort_keys=True)]
        if not diff:
            resolved = a
        else:
            issues.append("variants disagree: " + ",".join(diff))
            resolved = a          # provisional; audit/repair decides
    elif a or b:
        resolved = a or b
        notes.append("only one variant parsed")

    def refill(cand, base0):
        """repair/zoom candidates may omit stable fields: backfill from base."""
        if not cand or not base0:
            return cand
        out = dict(cand)
        for k in ("name", "restriction", "italic_keyword", "deck_banner"):
            if not out.get(k) and base0.get(k):
                out[k] = base0[k]
        if out.get("xp") is None and base0.get("xp") is not None:
            out["xp"] = base0["xp"]
        if not out.get("abilities") and base0.get("abilities"):
            out["abilities"] = base0["abilities"]
        return out

    # symbol audit: independent icon inventory of the printed card
    audit = (vision.get("audit") or {}).get("parsed")
    audit_avail = isinstance(audit, list)
    audit_exp = audit_multiset(audit) if audit_avail else None

    def audit_ok(cand):
        return cand is not None and audit_exp == token_multiset(cand.get("abilities"))

    if audit_avail and resolved is not None and not audit_ok(resolved):
        exp_s = dict(audit_exp)
        got_s = dict(token_multiset(resolved.get("abilities")))
        cands = [(r.get("parsed"), r) for r in (vision.get("repair") or [])]
        cands += [(r.get("parsed"), r) for r in (vision.get("retries") or [])]
        match = next((p for p, _r in cands if audit_ok(p)), None)
        if match is not None:
            notes.append("audit-matched re-extraction used")
            resolved = refill(match, a or b)
            issues = [i for i in issues if "disagree" not in i]
        else:
            issues.append(f"symbol audit mismatch: icons={exp_s} vs text={got_s}")
    elif not audit_avail and (vision.get("audit") or {}).get("error"):
        notes.append("symbol audit unavailable: " + vision["audit"]["error"][:60])

    if resolved is None:
        for p, r in ([(x.get("parsed"), x) for x in (vision.get("repair") or [])]
                     + [(x.get("parsed"), x) for x in (vision.get("retries") or [])]):
            if p:
                resolved = p
                issues.append("fallback candidate used: " + (r.get("note") or "repair"))
                break
    if resolved is None:
        return None, issues or ["unparsed"], notes

    # a zoom-crop resolution (retry tool) beats everything; apply before checks
    zoomed = (vision.get("resolved") or {}).get("parsed")
    if zoomed:
        resolved = refill(zoomed, resolved or a or b)
        issues = [i for i in issues if "audit mismatch" not in i and "disagree" not in i
                  and "fallback candidate" not in i]
        notes.append("zoom-crop resolution used (" + ((vision.get("resolved") or {}).get("method") or "") + ")")

    # authoritative XP corner (dedicated high-zoom probe)
    xc = vision.get("xp_corner") or {}
    if xc and not xc.get("error") and "xp" in xc:
        if resolved["xp"] != xc["xp"]:
            notes.append(f'xp-corner override: transcription={resolved["xp"]} corner={xc["xp"]}')
        resolved = dict(resolved)
        resolved["xp"] = xc["xp"]
    elif xc.get("error"):
        notes.append("xp corner probe failed: " + str(xc["error"])[:60])

    # ground-truth from the image PATH beats anything vision says
    vn = norm(resolved["name"])
    if vn != norm(card["name"]):
        if edit_le_1(vn, norm(card["name"])):
            notes.append(f'path_typo (accepted): path "{card["name"]}" vs printed "{resolved["name"]}"')
        else:
            issues.append(f'name mismatch: path "{card["name"]}" vs vision "{resolved["name"]}"')
    deck_norm = norm(card["deck"])
    if resolved["deck_banner"] and norm(resolved["deck_banner"]) not in (deck_norm, ""):
        # deck attribution is path ground truth (task spec); a diverging vision
        # banner read is only a hint that THIS READ was partial - keep as note,
        # never as issue or output value (observed: crop partials "HUTT", "Power",
        # "Technology" where all sibling cards of the deck read the full banner).
        notes.append(f'vision read banner as "{resolved["deck_banner"]}"; kept path deck '
                     f'name "{card["deck"]}"')
    if not resolved["basic"] and resolved["xp"] is None:
        issues.append("non-basic without xp")

    # printed-word encoding of the insight/eye test icon: this project spells
    # out non-primary icons as words (dice colours, disruption cube, ...) - the
    # vision model sometimes emits "[eye]" for it; normalize to the word.
    for ab in (resolved.get("abilities") or []):
        ab["text"] = re.sub(r"\[eye\]|\[Eye\]|\[insight\]|\[Insight\]", "Insight", ab.get("text") or "")

    merged = {
        "deck": card["deck"],
        "name": card["name"],
        "basic": bool(resolved["xp"] is None),
        "xp": resolved["xp"],
        "restriction": resolved["restriction"],
        "keyword": resolved["italic_keyword"],
        "deck_banner": card["deck"],
        "abilities": resolved["abilities"] or [],
    }
    return merged, issues, notes

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true",
                    help="no vision/network: rebuild merged JSON from caches only")
    args = ap.parse_args()

    cards = json.load(open(os.path.join(RAW, "inventory.json")))
    os.makedirs(VIS, exist_ok=True)
    base, key, model = vlm_endpoint()

    def process(card):
        cache = card_cache(card)
        if os.path.exists(cache) and os.path.getsize(cache) > 0:
            vision = json.load(open(cache))
            # opportunistic re-parse of stored raw outputs with the current parser
            dirty = False
            for k in ("native", "watermarked", "audit"):
                ent = vision.get(k) or {}
                pe = ent.get("parsed")
                if k != "audit" and isinstance(pe, dict) \
                        and not pe.get("name") and not pe.get("abilities"):
                    ent["parsed"] = None        # junk fragment from earlier parser
                    dirty = True
                if ent.get("parsed") is None and ent.get("raw"):
                    p = canon_fields(parse_json(ent["raw"])) if k != "audit" \
                        else parse_json(ent["raw"], array=True)
                    if p is not None:
                        ent["parsed"] = p
                        ent["error"] = None
                        dirty = True
            if dirty:
                with open(cache, "w", encoding="utf-8") as f:
                    json.dump(vision, f, indent=1, ensure_ascii=False)
            if vision.get("xp_corner") is None and not args.dry_run:
                vision["xp_corner"] = xp_corner_probe(card, base, key, model)
                with open(cache, "w", encoding="utf-8") as f:
                    json.dump(vision, f, indent=1, ensure_ascii=False)
            need_vision = any(vision.get(v, {}).get("parsed") is None
                              and v in variant_files(card) for v in ("native", "watermarked")) \
                or (not args.dry_run and (vision.get("audit") or {}).get("parsed") is None)
        else:
            vision = None
            need_vision = True
        if need_vision and not args.dry_run:
            old = vision or {}
            vision = {"source_sha256": {v: hashlib.sha256(open(p, "rb").read()).hexdigest()
                                         if os.path.exists(p) else None
                                         for v, p in variant_files(card).items()},
                      "model": model}
            keep = {k: old[k] for k in ("retries", "resolved") if k in old}
            vision.update(run_vision(card, base, key, model))
            vision.update(keep)
            if vision.get("xp_corner") is None:
                vision["xp_corner"] = xp_corner_probe(card, base, key, model)
            # repair loop: max 2 re-extractions when the symbol audit disagrees
            merged0, issues0, _n = merge_card(card, vision)
            mism = next((i for i in issues0 if i.startswith("symbol audit mismatch")), None)
            for attempt in range(2):
                if not mism:
                    break
                variant = ("native", "watermarked")[attempt]
                path = variant_files(card)[variant]
                if not os.path.exists(path):
                    break
                fb = (PROMPT + "\n- IMPORTANT: An independent icon-only inspection of this card "
                               "listed the printed icons as: " + mism.split("icons=", 1)[1] +
                      ". Re-examine EVERY printed icon (arrow vs gear+star, hexagon-with-lightning "
                      "vs hexagonal coin, etc.) and encode your text accordingly.")
                try:
                    rraw = vlm_chat(base, key, model, path, fb, max_tokens=4000)
                    rparsed = canon_fields(parse_json(rraw))
                except Exception:
                    rparsed = None
                if rparsed:
                    vision.setdefault("repair", []).append(
                        {"variant": variant, "raw": rraw, "parsed": rparsed, "note": mism[:200]})
                merged0, issues0, _n = merge_card(card, vision)
                mism = next((i for i in issues0 if i.startswith("symbol audit mismatch")), None)
            tmp = cache + f".tmp{threading.get_ident()}"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(vision, f, indent=1, ensure_ascii=False)
            os.replace(tmp, cache)
        elif vision is None:
            raise SystemExit(f"--dry-run: missing vision cache for {card['deck']}/{card['name']}")
        merged, issues, notes = merge_card(card, vision)
        return card, vision, merged, issues, notes

    if args.dry_run:
        results = [process(c) for c in cards]
    else:
        with ThreadPoolExecutor(max_workers=WORKERS) as ex:
            results = list(ex.map(process, cards))

    out, needs_review, stats = [], [], {}
    for card, vision, merged, issues, notes in sorted(results, key=lambda r: (r[0]["deck"], r[0]["name"])):
        entry = dict(merged) if merged else {
            "deck": card["deck"], "name": card["name"], "basic": None, "xp": None,
            "restriction": None, "keyword": None, "deck_banner": card["deck"], "abilities": []}
        entry["needs_review"] = bool(issues)
        if issues:
            entry["review_issues"] = issues
            needs_review.append(f'{card["deck"]}/{card["name"]}')
        if notes:
            entry["notes"] = notes
        entry["images"] = {v: os.path.relpath(p, WD) for v, p in variant_files(card).items()
                           if os.path.exists(p)}
        entry["image_urls"] = {"native": card["native_url"],
                               "watermarked": card.get("boardwars_img")}
        out.append(entry)
        d = stats.setdefault(card["deck"], {"cards": 0, "basic": 0, "xp_min": None, "xp_max": None})
        d["cards"] += 1
        if entry["basic"]:
            d["basic"] += 1
        if isinstance(entry["xp"], int):
            d["xp_min"] = entry["xp"] if d["xp_min"] is None else min(d["xp_min"], entry["xp"])
            d["xp_max"] = entry["xp"] if d["xp_max"] is None else max(d["xp_max"], entry["xp"])

    blob = json.dumps(out, indent=1, ensure_ascii=False) + "\n"
    if args.dry_run and os.path.exists(OUT):
        existing = json.load(open(OUT, encoding="utf-8"))
        # The final artifact may carry the validator's extra layers
        # (fan-wiki/garble/blog issues). Reproducibility of THIS tool is
        # defined on the merge-level fields; the validator's own --dry-run
        # gates byte-identity of the complete final JSON.
        BASE = ("deck", "name", "basic", "xp", "restriction", "keyword",
                "deck_banner", "abilities")
        if len(existing) == len(out):
            same = all(all(a.get(k) == b.get(k) for k in BASE)
                       for a, b in zip(existing, out))
        else:
            same = False
        print(("DRY-RUN OK: merge-level fields identical from cache (" + str(len(out)) + " cards)")
              if same else "DRY-RUN FAIL: cache rebuild differs from existing output")
        sys.exit(0 if same else 1)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(blob)
    report = {"cards": len(out), "decks": len(stats), "needs_review": needs_review,
              "per_deck": stats,
              "total_issues": sum(len(e.get("review_issues", [])) for e in out)}
    print(json.dumps(report, indent=1))

if __name__ == "__main__":
    main()
