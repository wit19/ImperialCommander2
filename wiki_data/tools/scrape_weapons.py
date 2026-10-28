import json, os, re, sys
sys.path.insert(0, "/home/open/.hermes/cache/scratch")
from wikiscrape import wikitext, infobox, resolve, OUT, api

heroes = json.load(open(OUT + "/heros.json"))
weapons = sorted({(h["name"], h["weapon"]) for h in heroes if h.get("weapon")})
print("Waffen:", [w for _, w in weapons])
def clean(v):
    v = str(v).replace("<br>", "\n").replace("'''", "").replace("''", "")
    v = re.sub(r"\[\[([^\]|]+)\|([^\]]+)\]\]", r"\2", v)
    v = re.sub(r"\[\[([^\]]+)\]\]", r"\1", v)
    return v.strip()
out = []
missing = []
for hero, wname in weapons:
    d = api({"action": "query", "titles": wname, "redirects": 1, "format": "json", "formatversion": 2})
    pg = d["query"]["pages"][0]
    if "missing" in pg:
        d2 = api({"action": "query", "list": "search", "srsearch": wname, "srlimit": 1,
                  "format": "json", "formatversion": 2})
        hits = [x["title"] for x in d2["query"]["search"]]
        if hits:
            pg2 = api({"action": "query", "titles": hits[0], "redirects": 1, "format": "json", "formatversion": 2})
            pg = pg2["query"]["pages"][0]
    if "missing" in pg:
        missing.append(wname); continue
    t = pg["title"]; wt = wikitext(t)
    typ, f = infobox(wt)
    out.append({"weapon": t, "hero": hero, "infobox": typ,
                "dice": clean(f.get("dice", "")), "type": clean(f.get("type", "")),
                "traits": clean(f.get("traits", "")),
                "effects": [clean(v) for k, v in sorted(f.items()) if k.startswith("effect")],
                "raw_fields": {k: clean(v) for k, v in f.items() if k != "image"}})
json.dump(out, open(OUT + "/hero_weapons.json", "w"), indent=1, ensure_ascii=False)
print("weapons geparst:", len(out), "fehlend:", missing)
for w in out[:5]: print("  ", w["weapon"], "|", w["dice"], "|", w["type"], "|", w["effects"][:1])
