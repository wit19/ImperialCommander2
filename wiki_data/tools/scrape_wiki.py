#!/usr/bin/env python3
"""Imperial Assault wiki -> structured JSON. Raw wikitext via MediaWiki API
(polite, 1 request/page, cached under wiki_data/raw/). CC-BY-SA source."""
import json, os, re, sys, time, urllib.parse, urllib.request

WIKI = "https://imperial-assault.fandom.com"
OUT = "/home/open/work/ia-ai/ImperialCommander2/wiki_data"
RAW = os.path.join(OUT, "raw")
os.makedirs(RAW, exist_ok=True)
UA = {"User-Agent": "ImperialCommander2-tactical-ai-data/1.0 (fan project data collection)"}

def api(params):
    qs = urllib.parse.urlencode(params)
    req = urllib.request.Request(WIKI + "/api.php?" + qs, headers=UA)
    for attempt in range(4):
        try:
            with urllib.request.urlopen(req, timeout=40) as r:
                return json.loads(r.read().decode("utf-8"))
        except Exception as e:
            if attempt == 3:
                raise
            time.sleep(1.5 * (attempt + 1))

def cat_members(cat):
    out, cont = [], None
    while True:
        p = {"action": "query", "list": "categorymembers", "cmtitle": cat,
             "cmlimit": 500, "format": "json", "formatversion": 2}
        if cont: p["cmcontinue"] = cont
        d = api(p)
        out += [m["title"] for m in d["query"]["categorymembers"]]
        cont = d.get("continue", {}).get("cmcontinue")
        if not cont: return out

def slug(title):
    s = re.sub(r"[^A-Za-z0-9_.()-]", "_", title.replace(" ", "_"))
    return s[:120]

def wikitext(title):
    path = os.path.join(RAW, slug(title) + ".wikitext")
    if os.path.exists(path):
        return open(path).read()
    d = api({"action": "parse", "prop": "wikitext", "format": "json",
             "formatversion": 2, "redirects": 1, "page": title})
    wt = d["parse"]["wikitext"]
    with open(path, "w") as f: f.write(wt)
    time.sleep(0.15)
    return wt

def resolve(title):
    d = api({"action": "query", "redirects": 1, "titles": title,
             "format": "json", "formatversion": 2})
    return d["query"]["pages"][0].get("title", title)

def split_fields(s):
    """Split infobox body on top-level '|' (brace-aware); returns OrderedDict
    of name -> raw value (list if name repeats with different casing kept)."""
    fields, depth, buf, i = [], 0, [], 0
    while i < len(s):
        c = s[i]
        if s.startswith("[[", i) or s.startswith("{{", i): depth += 1; buf.append(s[i:i+2]); i += 2; continue
        if s.startswith("]]", i) or s.startswith("}}", i): depth -= 1; buf.append(s[i:i+2]); i += 2; continue
        if c == "|" and depth == 0:
            fields.append("".join(buf)); buf = []; i += 1; continue
        buf.append(c); i += 1
    fields.append("".join(buf))
    out = {}
    for f in fields:
        f = f.strip()
        if not f: continue
        if "=" in f:
            k, v = f.split("=", 1)
            out[k.strip()] = v.strip()
        elif f:
            out.setdefault("_unnamed", []).append(f)
    return out

def infobox(wt):
    """Find the first {{...Infobox...}} block, brace-aware; returns (type, fields)."""
    m = re.search(r"\{\{\s*([A-Za-z_ ]*Infobox[A-Za-z_ ]*)", wt, re.I)
    if not m: return None, {}
    start = m.start() + 2
    depth, i = 1, start
    while i < len(wt) and depth > 0:
        if wt.startswith("{{", i) or wt.startswith("[[", i): depth += 1; i += 2; continue
        if wt.startswith("}}", i) or wt.startswith("]]", i): depth -= 1; i += 2; continue
        i += 1
    body = wt[start:i-2]
    # drop the template name portion before first |
    p = body.find("|")
    return m.group(1).strip(), split_fields(body[p+1:] if p >= 0 else "")

def categories(wt):
    return re.findall(r"\[\[Category:([^\]|]+)", wt)

if __name__ == "__main__":
    what = sys.argv[1]
    os.makedirs(OUT, exist_ok=True)
    if what == "heroes":
        titles = [t for t in cat_members("Category:Hero") if t.endswith("(Hero)")]
        # base cards (skirmish deployment versions) of the SAME heroes exist as
        # plain pages (e.g. 'Diala Passil') - they are the rebel deployment-card
        # stats; the hero campaign sheet is what we want -> (Hero) suffix only.
        rows = []
        for t in sorted(titles):
            wt = wikitext(t)
            typ, f = infobox(wt)
            rows.append({"page": t, "infobox": typ, "fields": f, "categories": categories(wt)})
            print("hero", t, typ, len(f), flush=True)
        json.dump(rows, open(os.path.join(OUT, "wiki_heros_raw.json"), "w"), indent=1)
    elif what == "classcards":
        titles = cat_members("Category:Hero Class Card")
        rows = []
        for t in sorted(titles):
            wt = wikitext(t)
            typ, f = infobox(wt)
            rows.append({"page": t, "infobox": typ, "fields": f, "categories": categories(wt)})
            print("card", t, typ, flush=True)
        json.dump(rows, open(os.path.join(OUT, "wiki_class_cards_raw.json"), "w"), indent=1)
    elif what == "units":
        for cat, fn in (("Category:Imperial Deployment Card", "wiki_imperial_units_raw.json"),
                        ("Category:Mercenary Deployment Card", "wiki_mercenary_units_raw.json"),
                        ("Category:Rebel Deployment Card", "wiki_rebel_units_raw.json")):
            rows = []
            for t in sorted(cat_members(cat)):
                wt = wikitext(t)
                typ, f = infobox(wt)
                rows.append({"page": t, "infobox": typ, "fields": f, "categories": categories(wt)})
                print("unit", cat[:8], t, typ, flush=True)
            json.dump(rows, open(os.path.join(OUT, fn), "w"), indent=1)
    elif what == "items":
        tiermap = {}
        for tier, cat in ((1, "Category:Tier 1"), (2, "Category:Tier 2"), (3, "Category:Tier 3")):
            for t in cat_members(cat):
                tiermap.setdefault(t, []).append(tier)
        rows = []
        for t in sorted(tiermap):
            wt = wikitext(t)
            typ, f = infobox(wt)
            rows.append({"page": t, "tiers": tiermap[t], "infobox": typ, "fields": f,
                         "categories": categories(wt)})
            print("item", t, typ, flush=True)
        json.dump(rows, open(os.path.join(OUT, "wiki_items_raw.json"), "w"), indent=1)
    elif what == "rewards":
        wt = wikitext("Reward Cards (Rebel)")
        # list lines: * [[Card]] ( [[Hero]] ) _( [[Mission]] )_
        entries = []
        for line in wt.splitlines():
            m = re.match(r"\*\s*\[\[([^\]|]+)", line)
            if m:
                hero = re.search(r"\(\s*\[\[([^\]|]+)\(", m.string[m.start():])
                miss = re.search(r"_\(\s*\[\[([^\]]+)", line)
                entries.append({"card": m.group(1).strip(),
                                "hero": hero.group(1).strip() if hero else None,
                                "mission": miss.group(1).strip() if miss else None})
        rows = []
        for e in entries:
            t = resolve(e["card"])
            w2 = wikitext(t)
            typ, f = infobox(w2)
            rows.append({**e, "page": t, "redirected": t != e["card"], "infobox": typ,
                         "fields": f, "categories": categories(w2)})
            print("reward", t, typ, flush=True)
        json.dump({"list_wikitext_head": wt[:2000], "rows": rows},
                  open(os.path.join(OUT, "wiki_rewards_raw.json"), "w"), indent=1)
