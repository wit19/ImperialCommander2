import json, os, re, urllib.parse, urllib.request

OUT = "/home/open/work/ia-ai/ImperialCommander2/wiki_data"
APP = "/home/open/work/ia-ai/ImperialCommander2/ImperialCommander2/Assets/Resources/Languages/En/"

def load(p): return json.load(open(p))
def appjson(p):
    s = open(os.path.join(APP, p)).read()
    return json.loads(re.sub(r",\s*([}\]])", r"\1", s))

# ---- dice/strain symbol legend from template docs (grounding, no guessing) ----
UA = {"User-Agent": "ImperialCommander2-tactical-ai-data/1.0"}
def wt(title):
    qs = urllib.parse.urlencode({"action": "parse", "prop": "wikitext", "format": "json",
                                 "formatversion": 2, "redirects": 1, "page": title})
    req = urllib.request.Request("https://imperial-assault.fandom.com/api.php?" + qs, headers=UA)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.loads(r.read().decode())["parse"]["wikitext"]
    except Exception as e:
        return "ERR " + str(e)
legend = {}
for t in ("S", "D", "Z", "O", "B", "K", "W", "R", "G", "Y", "F", "C", "P", "A", "H", "L", "I", "T"):
    w = wt("Template:" + t)
    first = w.strip().splitlines()[0] if w.strip() else ""
    legend[t] = (w[:180] if not w.startswith("ERR") else "missing")
json.dump(legend, open(os.path.join(OUT, "wiki_symbol_templates.json"), "w"), indent=1, ensure_ascii=False)
print("legend saved; sample:", {k: legend[k][:60].replace("\n", " ") for k in ("S", "D", "Z", "K", "W")})

# ---- normalizers ----
def clean(v):
    v = v.replace("<br>", "\n").replace("'''", "").replace("''", "")
    v = re.sub(r"\[\[([^\]|]+)\|([^\]]+)\]\]", r"\2", v)
    v = re.sub(r"\[\[([^\]]+)\]\]", r"\1", v)
    return v.strip()

def num(v):
    v = clean(str(v))
    return int(v) if re.fullmatch(r"-?\d+", v) else v

# HEROES
out = []
for r in load(os.path.join(OUT, "wiki_heros_raw.json")):
    f = r["fields"]
    def g(k): return clean(f[k]) if k in f else None
    def sfx(p): return {s: {k[len(p):].strip(): num(v) for k, v in f.items() if k.startswith(p)}
                        for s in (p,)}[p]
    xp_skills = {}
    for k, v in f.items():
        m = re.match(r"(\d+)xp skill (\d)", k)
        if m: xp_skills.setdefault(int(m.group(1)), []).append(clean(v))
    ab = []
    for k, v in sorted(f.items()):
        m = re.match(r"(f|b) ability (\d)", k)
        if m:
            text = clean(v)
            st = re.match(r"(\d+)\s*\{\{S\}\}", v)
            ab.append({"when": "healthy" if m.group(1) == "f" else "wounded",
                       "slot": int(m.group(2)), "strain": int(st.group(1)) if st else None,
                       "text": text})
    out.append({"name": g("name"), "title": g("title"), "source": g("source"),
                "weapon": g("weapon"), "mission": g("mission"), "reward": g("reward"),
                "healthy": {"health": num(f.get("f health")), "endurance": num(f.get("f endurance")),
                            "speed": num(f.get("f speed")), "defense": g("f defense"),
                            "strength": g("f strength"), "insight": g("f insight"), "tech": g("f tech")},
                "wounded": {"health": num(f.get("b health")), "endurance": num(f.get("b endurance")),
                            "speed": num(f.get("b speed")), "defense": g("b defense"),
                            "strength": g("b strength"), "insight": g("b insight"), "tech": g("b tech")},
                "abilities": ab, "xp_skill_tiers": {str(k): sorted(v) for k, v in sorted(xp_skills.items())},
                "page": r["page"]})
json.dump(out, open(os.path.join(OUT, "heros.json"), "w"), indent=1, ensure_ascii=False)
print("heros.json", len(out))

# CLASS CARDS
out = []
for r in load(os.path.join(OUT, "wiki_class_cards_raw.json")):
    f = r["fields"]
    eff = [clean(v) for k, v in sorted(f.items()) if k.startswith("effect")]
    out.append({"card": r["page"], "hero": clean(f.get("hero", "")), "xp_cost": num(f.get("cost")),
                "source": clean(f.get("source", "")), "effects": eff,
                "categories": [c for c in r["categories"] if c.endswith("Set") or c in
                               ("Twin Shadows", "Return to Hoth", "Jabba's Realm", "Heart of the Empire",
                                "Bespin Gambit", "The Bespin Gambit", "Rise of the Incubator",
                                "Tyrants of Lothal", "Lothal")],
                "page": r["page"]})
json.dump(out, open(os.path.join(OUT, "hero_class_cards.json"), "w"), indent=1, ensure_ascii=False)
print("hero_class_cards.json", len(out))

# UNITS
def unit_row(r):
    f = r["fields"]
    traits = [clean(f[k]) for k in f if re.match(r"trait \d", k)]
    shorts = [clean(f[k]) for k, _ in sorted(f.items()) if re.match(r"short ability \d", k)]
    longs = []
    for k, v in sorted(f.items()):
        m = re.match(r"long ability (\d)", k)
        if m:
            t = clean(v)
            km = re.match(r"([^:]+):\s*(.+)", t, re.S)
            longs.append({"keyword": km.group(1).strip() if km else None, "text": t})
    sources = [clean(v) for k, v in sorted(f.items()) if k.startswith("source")]
    return {"name": clean(f.get("name", r["page"])), "title": clean(f.get("title", "")),
            "affiliation": clean(f.get("affiliation", "")), "training": clean(f.get("training", "")),
            "unique": bool(f.get("unique")), "figure_size": clean(f.get("figure size", "")),
            "group_size": clean(f.get("group size", "")),
            "cost_major": num(f.get("cost major")), "cost_minor": num(f.get("cost minor")) if "cost minor" in f else None,
            "traits": traits, "health": num(f.get("health")), "speed": num(f.get("speed")),
            "defense": clean(f.get("defense", "")), "attack_type": clean(f.get("attack type", "")),
            "attack_dice": clean(f.get("attack dice", "")), "attack_free": clean(f.get("attack", "")),
            "surge_abilities": shorts, "keyword_abilities": longs,
            "reward_missions": [clean(v) for k, v in sorted(f.items()) if k.startswith("reward mission")],
            "sources": sources, "page": r["page"]}
for src, dst in (("wiki_imperial_units_raw", "imperial_units.json"),
                 ("wiki_mercenary_units_raw", "mercenary_units.json"),
                 ("wiki_rebel_units_raw", "rebel_units.json")):
    out = [unit_row(r) for r in load(os.path.join(OUT, src + ".json"))]
    json.dump(out, open(os.path.join(OUT, dst), "w"), indent=1, ensure_ascii=False)
    print(dst, len(out))

# ITEMS
ROMAN = {"I": 1, "II": 2, "III": 3, "": 0}
out = []
for r in load(os.path.join(OUT, "wiki_items_raw.json")):
    f = r["fields"]
    eff = [clean(v) for k, v in sorted(f.items()) if k.startswith("effect")]
    out.append({"name": clean(f.get("title", r["page"])), "tiers_wiki_page": ROMAN.get(clean(f.get("tier", "")), clean(f.get("tier", ""))),
                "tiers_categories": r["tiers"], "type": clean(f.get("type", "")),
                "traits": clean(f.get("traits", "")), "cost": num(f.get("cost")),
                "dice": clean(f.get("dice", "")), "defense_bonus": clean(f.get("defense", "")),
                "number_modifications": clean(f.get("numbermodifications", "")),
                "effects": eff, "source": clean(f.get("source", "")).replace("[[", "").replace("]]", ""),
                "page": r["page"]})
json.dump(out, open(os.path.join(OUT, "items.json"), "w"), indent=1, ensure_ascii=False)
print("items.json", len(out))

# REWARDS
out = []
for r in load(os.path.join(OUT, "wiki_rewards_raw.json"))["rows"]:
    f = r["fields"]
    eff = [clean(v) for k, v in sorted(f.items()) if k.startswith("effect") or k in ("text", "description")]
    out.append({"card": clean(r["card"]), "hero": r["hero"], "mission": r["mission"],
                "page": r["page"], "fields_clean": {k: clean(v) for k, v in f.items() if k != "image"},
                "effects": eff})
json.dump(out, open(os.path.join(OUT, "rewards.json"), "w"), indent=1, ensure_ascii=False)
print("rewards.json", len(out))

# ---- cross-match against the GAME ----
def norm(s): return re.sub(r"\s*\(.*?\)\s*", " ", s.lower()).replace("'", "").strip()

app_items = appjson("CampaignData/items.json")
wiki_items = load(os.path.join(OUT, "items.json"))
wmap = {}
for w in wiki_items: wmap.setdefault(norm(w["name"]), w)
hit = sum(1 for a in app_items if norm(a["name"]) in wmap)
print(f"items: app {len(app_items)}, deckungsgrad wiki {hit}")
mism = [(a["name"], a["tier"], a["cost"], wmap[norm(a["name"])]["cost"] if norm(a["name"]) in wmap else None,
         wmap[norm(a["name"])]["tiers_wiki_page"] if norm(a["name"]) in wmap else None)
        for a in app_items if norm(a["name"]) in wmap]
bad = [m for m in mism if m[2] != m[3] or (m[1] != m[4] and (norm(a["name"]) if False else True))]
print("item mismatches cost/tier:", [m for m in mism if m[2] != m[3]][:8])
tier_ok = [m for m in mism if m[1] == m[4]]
print("tier equal:", len(tier_ok), "/", len(mism), " multi-tier pages:",
      sum(1 for w in wiki_items if len(w["tiers_categories"]) > 1))

app_skills = appjson("CampaignData/skills.json")
wiki_cards = load(os.path.join(OUT, "hero_class_cards.json"))
wc = {}
for c in wiki_cards: wc.setdefault(norm(c["card"]), c)
hit = sum(1 for s in app_skills if norm(s["name"]) in wc)
print(f"skills: app {len(app_skills)}, im wiki wieder gefunden: {hit}")

dg = {}
for fn in ("DeploymentGroups/enemies.json", "DeploymentGroups/villains.json", "DeploymentGroups/allies.json"):
    for e in appjson(fn): dg.setdefault(norm(e["name"]), []).append((fn.split("/")[-1], e))
wiki_units = load(os.path.join(OUT, "imperial_units.json")) + load(os.path.join(OUT, "mercenary_units.json")) + load(os.path.join(OUT, "rebel_units.json"))
wu = {}
for u in wiki_units:
    wu.setdefault(norm(u["name"]), []).append(u)
matched = empty_ab = 0
for n, entries in dg.items():
    for fn, e in entries:
        cands = wu.get(n) or wu.get(norm(e["name"]) + " elite") or []
        if not cands:
            for k in wu:
                if k.startswith(norm(e["name"])) or n in k: cands = wu[k]; break
        if cands:
            matched += 1
            if not e.get("abilities") and cands[0]["keyword_abilities"]: empty_ab += 1
print(f"game unit entries {len(dg)}, wiki matched: {matched}, davon mit Faeligkeiten befuellbar: {empty_ab}")
