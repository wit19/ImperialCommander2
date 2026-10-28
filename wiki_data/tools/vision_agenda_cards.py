#!/usr/bin/env python3
"""M12d2 vision cross-extraction for Agenda cards.

For each of the 126 Agenda cards documented on imperial-assault.fandom.com
(raw/agenda_cards/parsed.json, 'Category:' strays dropped) we locate the
native scan on imperial-assault.com/Agenda_Decks via normalized-name match,
download it to raw/agenda_cards/native/, and have the local VLM transcribe
name / printed cost / printed time period / printed set label / type / full
rules text as strict JSON.  Every successful transcription is cached at
raw/agenda_cards/vision/<slug>.json together with the sha256 of the image,
so reruns are incremental and --dry-run rebuilds stats from cache alone
(same M12a doctrine).

Set membership is NOT taken from here - it stays authoritative on the wiki
set pages; vision 'set'/'cost'/'type' are cross-check fields only, merged
(and conflict-flagged) by gen_agenda_cards.py.

Run:  python3 wiki_data/tools/vision_agenda_cards.py [--workers N] [--only Card] [--dry-run]
"""
import argparse
import base64
import hashlib
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "..", "raw", "agenda_cards")
NATIVE = os.path.join(RAW, "native")
VISION_CACHE = os.path.join(RAW, "vision")
PARSED = os.path.join(RAW, "parsed.json")
INVENTORY_HTML = "/tmp/ia_agenda.html"
SITE = "https://imperial-assault.com"
MODEL = "Qwen/Qwen3.6-27B-FP8"   # served id; the config 'model:' line is empty
UA = {"User-Agent": "Mozilla/5.0 (M12d2 data curation; contact werth IT)"}


def vlm_endpoint():
    """Same config seam as extract_imperial_class_cards.py; the 'model:' line
    in config.yaml is empty for this deployment, so we hardcode MODEL."""
    cfg = os.path.expanduser("~/.hermes/config.yaml")
    base = key = None
    with open(cfg, encoding="utf-8") as f:
        for line in f:
            if base is None and re.match(r"\s*base_url:\s*\S*v1\s*$", line):
                base = line.split(":", 1)[1].strip()
            if key is None and re.match(r"\s*api_key:\s*\S", line):
                key = line.split(":", 1)[1].strip().strip("'\"")
    if not base or not key:
        raise RuntimeError("could not read VLM base_url/api_key from config")
    return base, key


def norm(s):
    """Aggressive name normalisation for matching wiki titles to scan names."""
    if not s:
        return ""
    s = s.replace("\u2019", "'").replace("\u2018", "'")
    return re.sub(r"[^a-z0-9]+", "", s.lower())


# Documented one-off name pairs between the wiki card title and the scan file
# name (verified 2026-10-28 against the Agenda_Decks inventory).
KNOWN_IMAGE_PAIRS = {
    "abandoncontract": "Abandoned Contract",   # wiki 'Abandon Contract'
    "piecebypiece": "Piece by Peace",          # printed scan typo on site
}

SUFFIX_RE = re.compile(r"\s*\((agenda card|imperial agenda)\)\s*$", re.I)


def image_candidates(title):
    """Normalised-name keys under which `title` may appear in the scan
    inventory (exact norm first, then suffix/'The'-stripped variants)."""
    keys = [norm(title)]
    stripped = SUFFIX_RE.sub("", title).strip()
    keys.append(norm(stripped))
    keys.append(norm(re.sub(r"^the\s+", "", stripped, flags=re.I)))
    if norm(title) in KNOWN_IMAGE_PAIRS:
        keys.append(norm(KNOWN_IMAGE_PAIRS[norm(title)]))
    return keys


def load_inventory():
    """{norm(name): url} for the imperial-assault.com/Agenda_Decks images."""
    urls = None
    if os.path.exists(INVENTORY_HTML):
        html = open(INVENTORY_HTML, encoding="utf-8", errors="replace").read()
        urls = re.findall(r"/images/agendaDecks/[^\"]*\.jpg", html)
    if not urls:  # inventory scrape gone -> refetch the page once
        req = urllib.request.Request(SITE + "/Agenda_Decks", headers=UA)
        with urllib.request.urlopen(req, timeout=60) as r:
            html = r.read().decode("utf-8", "replace")
        with open(INVENTORY_HTML, "w", encoding="utf-8") as f:
            f.write(html)
        urls = re.findall(r"/images/agendaDecks/[^\"]*\.jpg", html)
    inv = {}
    for u in sorted(set(urls)):
        name = urllib.parse.unquote(u.rsplit("/", 1)[1][:-4])
        inv.setdefault(norm(name), u)
    return inv


def match_image(title, inventory):
    for k in image_candidates(title):
        if k in inventory:
            return inventory[k]
    return None


def load_cards():
    """The 126 real card pages + the authoritative set pages (strays dropped)."""
    d = json.load(open(PARSED, encoding="utf-8"))
    cards = {k: v for k, v in d["cards"].items() if not k.startswith("Category:")}
    return d["sets"], cards


def slugify(title):
    return re.sub(r"[^a-z0-9]+", "_", title.lower()).strip("_")


def image_of(card):
    return os.path.join(NATIVE, card + ".jpg")


def cache_of(card):
    return os.path.join(VISION_CACHE, slugify(card) + ".json")


PROMPT = """You are transcribing one printed card from the board game Imperial Assault (Return to the Rising Sin / RR agenda-card era). This is an Agenda Card scan.

Transcribe ONLY what is printed on the card. Output ONLY one JSON object on a single line, no prose, no code fence. Exactly these keys:
{"name": the card title as printed,
 "cost": the printed Influence purchase cost as an integer (top-left coin area) or null if not visible,
 "time_period": a time period range printed as "lo-hi" (e.g. "1-6") or a single printed number as "N" (e.g. "4"), or null. A time period is printed on SOME cards only, as text like 'Time Period: 1-6', usually near the set label or the bottom of the card. If NO time period is printed, output null. Do not invent one.
 "set": the set label printed in the upper right corner of the card, verbatim,
 "type": exactly one of "Side Mission", "Forced Mission", "Ongoing", "Kept Secret", "?" if unreadable,
 "text": the full rules text block (not the italic flavour line), verbatim, as one string with line breaks joined by single spaces}"""


def transcribe(base, key, img_path):
    """Returns (parsed_obj_or_None, raw_answer). Reads content AND reasoning:
    this serving stack sometimes lands the answer only in message.reasoning."""
    b64 = base64.b64encode(open(img_path, "rb").read()).decode()
    body = {
        "model": MODEL, "temperature": 0, "max_tokens": 8192,
        "messages": [{"role": "user", "content": [
            {"type": "text", "text": PROMPT},
            {"type": "image_url",
             "image_url": {"url": "data:image/jpeg;base64," + b64}}]}],
    }
    req = urllib.request.Request(
        base.rstrip("/") + "/chat/completions",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json",
                 "Authorization": "Bearer " + key})
    with urllib.request.urlopen(req, timeout=180) as r:
        resp = json.load(r)
    msg = (resp.get("choices") or [{}])[0].get("message", {}) or {}
    content = msg.get("content")
    if not content:
        content = msg.get("reasoning") or msg.get("reasoning_content")
    if not content:
        raise RuntimeError("empty completion (content=None)")
    return parse_json(content), content


def parse_json(text):
    """Largest dict-with-'text'-key inside possibly chatty/reasoning output."""
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
    for pos in [i for i, ch in enumerate(t) if ch == "{"]:
        try:
            val, end = dec.raw_decode(t[pos:])
        except Exception:
            continue
        if isinstance(val, dict) and ("text" in val or "name" in val):
            spans.append((end, val))
    if not spans:
        return None
    return max(spans, key=lambda x: x[0])[1]


def time_period_field(obj):
    """'1-6' -> [1,6]; '4' -> [4,4]; else None (printed value only)."""
    tp = obj.get("time_period")
    if isinstance(tp, list) and len(tp) == 2 and all(isinstance(x, int) for x in tp):
        return [tp[0], tp[1]]
    if not isinstance(tp, str):
        return None
    m = re.match(r"^\s*(\d+)\s*[-\u2013]\s*(\d+)\s*$", tp)
    if m:
        return [int(m.group(1)), int(m.group(2))]
    m = re.match(r"^\s*(\d+)\s*$", tp)
    if m:
        return [int(m.group(1)), int(m.group(1))]
    return None


def clean_obj(obj):
    return {
        "name": (obj.get("name") or "").strip(),
        "cost": obj.get("cost") if isinstance(obj.get("cost"), int)
                and not isinstance(obj.get("cost"), bool) else None,
        "time_period": time_period_field(obj),
        "set": (obj.get("set") or "").strip() or None,
        "type": (obj.get("type") or "").strip(),
        "text": re.sub(r"\s+", " ", obj.get("text") or "").strip(),
    }


def process(card, inventory, base, key, force=False):
    cache_path = cache_of(card)
    if os.path.exists(cache_path):
        cached = json.load(open(cache_path, encoding="utf-8"))
        if cached.get("obj") and cached.get("ok", True) and not force:
            return cached
    url = match_image(card, inventory)
    entry = {"card": card, "image_url": url}
    if not url:
        entry.update({"ok": False, "needs_review": True,
                      "reason": "no image", "obj": None})
        json.dump(entry, open(cache_path, "w", encoding="utf-8"),
                  ensure_ascii=False, indent=1)
        return entry
    path = image_of(card)
    os.makedirs(NATIVE, exist_ok=True)
    if not os.path.exists(path):
        full = SITE + urllib.parse.quote(url)
        req = urllib.request.Request(full, headers=UA)
        data = None
        for attempt in range(3):
            try:
                with urllib.request.urlopen(req, timeout=60) as r:
                    data = r.read()
                break
            except Exception:
                if attempt == 2:
                    raise
                time.sleep(2 * (attempt + 1))
        if data is None:
            raise RuntimeError("image download failed: " + full)
        with open(path, "wb") as f:
            f.write(data)
    entry["sha256"] = hashlib.sha256(open(path, "rb").read()).hexdigest()
    obj = raw = None
    for attempt in range(2):  # one retry per card on unparseable JSON
        try:
            obj, raw = transcribe(base, key, path)
            if obj is not None:
                break
        except Exception as exc:
            raw = repr(exc)
            time.sleep(2 * (attempt + 1))
    entry["ok"] = obj is not None
    entry["obj"] = clean_obj(obj) if obj is not None else None
    if obj is None:
        entry["needs_review"] = True
        entry["reason"] = "vision unparseable"
        entry["raw_answer"] = (raw or "")[:2000]
    json.dump(entry, open(cache_path, "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    return entry


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--only", action="append", default=[])
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--dry-run", action="store_true",
                    help="no network: report from on-disk vision cache only")
    args = ap.parse_args()
    os.makedirs(VISION_CACHE, exist_ok=True)
    _, cards = load_cards()
    titles = sorted(args.only or cards)
    if args.dry_run:
        done = sum(1 for t in titles if os.path.exists(cache_of(t)))
        print("dry-run: %d/%d cards have vision cache entries" % (done, len(titles)))
        return
    inventory = load_inventory()
    print("scan inventory: %d unique images; %d wiki cards" %
          (len(inventory), len(titles)))
    base, key = vlm_endpoint()
    ok = fail = 0
    t0 = time.time()
    with ThreadPoolExecutor(max_workers=min(args.workers, 4)) as ex:
        futs = {ex.submit(process, t, inventory, base, key, args.force): t
                for t in titles}
        for i, fut in enumerate(as_completed(futs), 1):
            t = futs[fut]
            try:
                res = fut.result()
                if res.get("ok"):
                    ok += 1
                else:
                    fail += 1
                    print("REVIEW: %s (%s)" % (t, res.get("reason")))
            except Exception as exc:
                fail += 1
                print("FAIL: %s: %s" % (t, exc), file=sys.stderr)
            if i % 10 == 0 or i == len(titles):
                print("[%d/%d] ok=%d fail=%d %.0fs" %
                      (i, len(titles), ok, fail, time.time() - t0))
    print("done: ok=%d fail=%d of %d" % (ok, fail, len(titles)))


if __name__ == "__main__":
    main()
