#!/usr/bin/env python3
"""M12a scraper: the 12 Imperial Class Decks of Era 1 -> complete card inventory.

Sources (both image variants per card, cached under wiki_data/raw/imperial_class_cards/):
  1. Ground truth inventory + native card images:
     https://imperial-assault.com/Imperial_Class_Decks/ (server-rendered; every card
     image is /images/imperialClassDecks/<Deck>/<Name>.jpg, so (deck, name) membership
     is EXACTLY the image path). Native images are 485x741 px.
  2. Bigger watermarked scans: cards.boardwars.eu (Zenphoto gallery). Each card page
     /<album-path>/<Name>.jpg embeds <img src="/cache/<album>/<Name>_595_..._watermark...">
     (595 px height, logo watermark).

Outputs (all under wiki_data/raw/imperial_class_cards/):
  inventory.json          [{deck,name,native_url,boardwars_page,boardwars_img}, ...]
  native/<Deck>/<Name>.jpg
  watermarked/<Deck>/<Name>.jpg
  boardwars_albums.json   deck -> boardwars album path (discovery cache)
  scrape_report.json      per-deck counts + mismatches

Re-runs are cache-based: any existing file is NOT re-downloaded. --dry-run performs
zero network access and fails if the cache is incomplete (reproducibility gate).

Run:  python3 wiki_data/tools/scrape_imperial_class_decks.py [--dry-run]
"""
import argparse, json, os, re, sys, time, urllib.parse, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
WD = os.path.dirname(HERE)                                     # wiki_data/
RAW = os.path.join(WD, "raw", "imperial_class_cards")
UA = {"User-Agent": "Mozilla/5.0 ImperialCommander2-tactical-ai-data/1.0 (fan project data collection)"}
INV_URL = "https://imperial-assault.com/Imperial_Class_Decks/"
IA_BASE = "https://imperial-assault.com/"
BW_BASE = "https://cards.boardwars.eu"
SLEEP = 0.35                                                   # politeness for boardwars

# Deck -> expansion (per FFG release; used only to bootstrap the boardwars album
# discovery, which is then verified against the deck name itself).
EXPANSION = {
    "Technological Superiority": "Core", "Military Might": "Core",
    "Subversive Tactics": "Core",
    "Inspiring Leadership": "Twin Shadows",
    "Armored Onslaught": "Return to Hoth", "Precision Training": "Return to Hoth",
    "Imperial Black Ops": "Bespin Gambit",
    "Hutt Mercenaries": "Jabba's Realm", "Nemeses": "Jabba's Realm",
    "Power of the Dark Side": "Heart of the Empire",
    "Reactive Defenses": "Heart of the Empire",
    "Overwhelming Oppression": "Tyrants of Lothal",
}
BW_PARENTS = {  # boardwars album parents per expansion (verified Oct 2026)
    "Core": ["Core-Box"],
    "Twin Shadows": ["Expansion-Boxes/Twin-Shadows"],
    "Return to Hoth": ["Expansion-Boxes/Return-to-Hoth"],
    "Bespin Gambit": ["Expansion-Boxes/The-Bespin-Gambit"],
    "Jabba's Realm": ["Expansion-Boxes/Jabbas-Realm"],
    "Heart of the Empire": ["Expansion-Boxes/Heart-of-the-Empire"],
    "Tyrants of Lothal": ["Expansion-Boxes/Tyrants-of-Lothal"],
}

def fetch(url, binary=False, cache=None):
    if cache and os.path.exists(cache) and os.path.getsize(cache) > 0:
        return open(cache, "rb").read() if binary else open(cache, encoding="utf-8", errors="replace").read()
    req = urllib.request.Request(url, headers=UA)
    for attempt in range(4):
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                data = r.read()
            break
        except Exception:
            if attempt == 3:
                raise
            time.sleep(1.5 * (attempt + 1))
    if cache:
        os.makedirs(os.path.dirname(cache), exist_ok=True)
        with open(cache, "wb") as f:
            f.write(data)
    return data if binary else data.decode("utf-8", errors="replace")

def get_json(path, default=None):
    if os.path.exists(path):
        return json.load(open(path))
    return default

def scrape_inventory(dry):
    """Parse the imperial-assault.com inventory page -> (deck, name) list."""
    cache = os.path.join(RAW, "inventory.html")
    if dry:
        html = fetch(INV_URL, cache=cache)          # cache must exist in dry mode
    else:
        html = fetch(INV_URL, cache=cache if os.path.exists(cache) else None)
        if os.path.exists(cache):
            pass
        else:
            with open(cache, "w", encoding="utf-8") as f:
                f.write(html)
    paths = sorted(set(re.findall(r"images/imperialClassDecks/([^\"']+?\.jpg)", html)))
    cards, backs = [], []
    for p in paths:
        deck, _, fname = p.partition("/")
        name = os.path.splitext(fname)[0]
        (backs if name == "back" else cards).append(
            {"deck": deck, "name": name,
             "native_url": IA_BASE + "images/imperialClassDecks/" + urllib.parse.quote(p)})
    cards.sort(key=lambda c: (c["deck"], c["name"]))
    return cards, sorted(set(b["deck"] for b in backs))

def download_native(cards, dry):
    missing = []
    for c in cards:
        dest = os.path.join(RAW, "native", c["deck"], c["name"] + ".jpg")
        if os.path.exists(dest) and os.path.getsize(dest) > 0:
            continue
        if dry:
            missing.append(dest)
            continue
        fetch(c["native_url"], binary=True, cache=dest)
        time.sleep(0.1)
    return missing

def discover_albums(cards, dry):
    """deck -> boardwars album path; cached in boardwars_albums.json."""
    cache = os.path.join(RAW, "boardwars_albums.json")
    found = get_json(cache, {})
    todo = [d for d in EXPANSION if d not in found]
    if todo and dry:
        raise SystemExit(f"--dry-run: boardwars_albums.json incomplete (missing: {todo})")
    for deck in todo:
        album = None
        for parent in BW_PARENTS[EXPANSION[deck]]:
            cand = f"{parent}/Imperial Class Decks/{deck}"
            try:
                html = fetch(f"{BW_BASE}/{urllib.parse.quote(cand)}/")
            except Exception:
                continue
            hrefs = set(re.findall(r'href="(/[^"]+/)"', html))
            if any(h.rstrip("/").endswith(urllib.parse.quote(deck)) for h in hrefs):
                album = cand
                break
        if album is None:                       # fallback: search one card of the deck
            # NB: boardwars has a typo album ("Overwhelming Opression"), so match
            # the card NAME via the search hit, not the deck folder name.
            sample = next(c["name"] for c in cards if c["deck"] == deck)
            time.sleep(SLEEP)
            html = fetch(f"{BW_BASE}/page/search/?s={urllib.parse.quote_plus(sample)}")
            for h in re.findall(r'href="(/[^"]+/)[^"/]*\.jpg"', html):
                dec = urllib.parse.unquote(h)
                leaf = dec.split("/")[-1].replace("+", " ")
                leaf = re.sub(r"\.jpg$", "", leaf).strip().lower()
                if dec.split("/")[-2:-1] and "Imperial Class Decks" in dec \
                        and sample.lower().startswith(leaf[:8]):
                    album = dec.strip("/")
                    break
        if album is None:
            raise RuntimeError(f"boardwars album not found for deck '{deck}'")
        found[deck] = album
        with open(cache, "w") as f:
            json.dump(found, f, indent=1)
        time.sleep(SLEEP)
    return found

def scrape_boardwars(cards, albums, dry):
    """Per deck: album page -> card page links; per card: page -> cache-img URL."""
    deck_page_cache = os.path.join(RAW, "boardwars_pages")
    missing = []
    for deck, album in sorted(albums.items()):
        html = fetch(f"{BW_BASE}/{urllib.parse.quote(album)}/",
                     cache=os.path.join(deck_page_cache, deck + ".album.html") if not dry else os.path.join(deck_page_cache, deck + ".album.html"))
        hrefs = sorted(set(re.findall(r'href="(/[^"]+/[^"]+\.jpg)"', html)))
        # Zenphoto encodes spaces as '+' in hrefs; unquote does NOT decode '+' -> fix manually
        by_name = {os.path.splitext(urllib.parse.unquote(h.split("/")[-1]).replace("+", " "))[0]: h
                   for h in hrefs}
        for c in [x for x in cards if x["deck"] == deck]:
            page = by_name.get(c["name"])
            if page is None:
                c["boardwars_page"] = None
                c["boardwars_img"] = None
                continue
            c["boardwars_page"] = BW_BASE + page
            page_cache = os.path.join(deck_page_cache, deck, c["name"] + ".page.html")
            if dry:
                if not (os.path.exists(page_cache) and os.path.getsize(page_cache) > 0):
                    missing.append(c)
                img = None
            else:
                ph = fetch(c["boardwars_page"], cache=page_cache)
                m = re.search(r'<img[^>]*src="(/cache/[^"]*_595_[^"]+)"', ph)
                # keep the percent-encoding from the HTML (raw path would contain spaces)
                img = BW_BASE + m.group(1) if m else None
                c["boardwars_img"] = img
            if not dry and img is None:
                missing.append(c)
            time.sleep(SLEEP)
    return missing

def download_watermarked(cards, dry):
    missing = []
    for c in cards:
        dest = os.path.join(RAW, "watermarked", c["deck"], c["name"] + ".jpg")
        if os.path.exists(dest) and os.path.getsize(dest) > 0:
            continue
        if dry or not c.get("boardwars_img"):
            missing.append(dest)
            continue
        fetch(c["boardwars_img"], binary=True, cache=dest)
        time.sleep(0.15)
    return missing

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true",
                    help="no network, no writes: verify cache completeness only")
    args = ap.parse_args()

    os.makedirs(RAW, exist_ok=True)
    cards, back_decks = scrape_inventory(args.dry_run)
    albums = discover_albums(cards, args.dry_run)
    bw_missing_pages = scrape_boardwars(cards, albums, args.dry_run)
    native_missing = download_native(cards, args.dry_run)
    wm_missing = download_watermarked(cards, args.dry_run)

    if args.dry_run:
        problems = native_missing + wm_missing + bw_missing_pages
        if problems:
            print(f"DRY-RUN FAIL: {len(problems)} cache gaps: {problems[:5]}")
            sys.exit(1)
        print(f"DRY-RUN OK: {len(cards)} cards, {len(albums)} decks fully cached")
        return

    inv_path = os.path.join(RAW, "inventory.json")
    with open(inv_path, "w", encoding="utf-8") as f:
        json.dump(cards, f, indent=1, ensure_ascii=False)

    decks = {}
    for c in cards:
        decks.setdefault(c["deck"], []).append(c)
    report = {
        "total_cards": len(cards),
        "total_decks": len(decks),
        "expected_decks": len(EXPANSION),
        "back_images_seen": len(back_decks),
        "per_deck": {d: {"cards": len(v),
                         "native_ok": sum(1 for c in v if os.path.getsize(os.path.join(RAW, "native", d, c["name"] + ".jpg")) > 0),
                         "watermarked_ok": sum(1 for c in v if os.path.exists(os.path.join(RAW, "watermarked", d, c["name"] + ".jpg"))),
                         "boardwars_page_found": sum(1 for c in v if c.get("boardwars_page"))}
                    for d, v in sorted(decks.items())},
        "no_boardwars_page": [f'{c["deck"]}/{c["name"]}' for c in cards if not c.get("boardwars_page")],
        "no_watermarked_img": [f'{c["deck"]}/{c["name"]}' for c in cards if not c.get("boardwars_img")],
    }
    with open(os.path.join(RAW, "scrape_report.json"), "w") as f:
        json.dump(report, f, indent=1, ensure_ascii=False)
    print(json.dumps(report, indent=1))

if __name__ == "__main__":
    main()
