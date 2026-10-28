import json, os, re

OUT = "/home/open/work/ia-ai/ImperialCommander2/wiki_data"
APP = "/home/open/work/ia-ai/ImperialCommander2/ImperialCommander2/Assets/Resources/Languages/En/"

def clean(v):
    v = str(v).replace("<br>", "\n").replace("'''", "").replace("''", "")
    v = re.sub(r"\[\[([^\]|]+)\|([^\]]+)\]\]", r"\2", v)
    v = re.sub(r"\[\[([^\]]+)\]\]", r"\1", v)
    return v.strip()

def parse_tier(v):
    v = clean(v)
    if re.fullmatch(r"[123]", v): return int(v)
    return {"I": 1, "II": 2, "III": 3}.get(v.upper(), None)

rows = json.load(open(os.path.join(OUT, "wiki_items_raw.json")))
out = []
for r in rows:
    f = r["fields"]
    page_name = re.sub(r"\s*\((Tier|Item) \d?\)\s*$", "", r["page"]).strip()
    base = {"name": page_name, "wiki_name_field": clean(f.get("name", "")), "page": r["page"],
            "type": clean(f.get("type", "")), "source": clean(f.get("source", ""))}
    if r["infobox"] in ("Infobox_item", "Infobox item"):
        traits = [clean(v) for k, v in sorted(f.items()) if re.match(r"trait\d", k)]
        effs = [clean(v) for k, v in sorted(f.items()) if re.match(r"ability\d", k)]
        tiers = [parse_tier(f[k]) for k in f if re.match(r"tier\d", k)]
        cost = int(str(f["cost"]).strip()) if "cost" in f else None
        for t in [x for x in tiers if x]:
            out.append({**base, "tier": t, "cost": cost, "traits": traits,
                        "dice": None, "effects": effs})
    else:
        effs = [clean(v) for k, v in sorted(f.items()) if k.startswith("effect")]
        if "tiers" in f:
            tiers = [parse_tier(x) for x in clean(f["tiers"]).split(",")]
        else:
            tiers = [parse_tier(f.get("tier", ""))]
        for t in [x for x in tiers if x]:
            out.append({**base, "tier": t,
                        "cost": int(str(f.get("cost", 0)).strip()),
                        "traits": [x.strip() for x in clean(f.get("traits", "")).split(",") if x.strip()],
                        "dice": clean(f.get("dice", "")), "effects": effs,
                        "number_modifications": clean(f.get("numbermodifications", ""))})
json.dump(out, open(os.path.join(OUT, "items.json"), "w"), indent=1, ensure_ascii=False)

def norm(s):
    s = re.sub(r"\s*\(.*?\)\s*", " ", s.lower()).replace("'", "").strip()
    return s[:-1] if s.endswith("s") and len(s) > 4 else s

app_items = json.loads(re.sub(r",\s*([}\]])", r"\1", open(APP + "CampaignData/items.json").read()))
wmap = {}
for w in out: wmap.setdefault((norm(w["name"]), w["tier"]), []).append(w)
hit = costmm = 0; misses = []
for a in app_items:
    k = (norm(a["name"]), a["tier"])
    if k in wmap:
        hit += 1
        wc = wmap[k][0]["cost"]
        if wc is not None and wc != a["cost"]: costmm += 1; misses.append((a, wmap[k][0]))
    else: misses.append((a, None))
print(f"items.json rows {len(out)} | app {len(app_items)} | match {hit} | cost mismatch {costmm}")
for a, w in misses: print("  MISS:", a["name"], "T", a["tier"], a["cost"])
