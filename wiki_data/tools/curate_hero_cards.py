#!/usr/bin/env python3
# M11b curation generator: tactical_hero_cards.json
# ---------------------------------------------------------------------------
# Builds the RUNTIME asset from the local wiki_data normalisates and the
# APP's own tables for ids and xp costs. Cleanroom policy
# (wiki_data/LICENSE_DATA.md Punkt 5, Klaerung M11g): printed card numbers,
# die letters and keyword slugs; where an ability is printed IN FULL on the
# card, its slug carries the full printed text too -- board-game content from
# the licensed copy, transcribed 1:1 (precedent: the app's own allies.json/
# villains.json ship the same printed rule texts); the wiki served only as the
# transcription medium for that printed text. Raw wiki dumps (HTML/scraper
# output) stay out of the build.
#   * CardData/heroes.json        hero name <-> app hero id (H1..H21)
#   * CampaignData/items.json     item name/tier/cost -> app item id
#   * CampaignData/skills.json    purchased skill names -> xp cost
# The app tables win wherever they speak (ids/costs); the wiki normalisates
# only supply the printed card numbers (health/endurance/speed/defense/
# attributes healthy AND wounded, weapon die pool, strain costs).
#
# Range convention (printed rules ATTACKS: ranged needs Accuracy >= distance):
#   reichweite = int(AccEV(pool) + 1/2)  (same rounding as Dice.AccGate),
#   melee: 1, melee with printed "Reach": 2.
#   (IC2 band semantics: "Acc-Gate der gedruckten Wuerfel",
#    cf. tactical_hero_profiles.json band_semantics.)
#
# Run:  python3 wiki_data/tools/curate_hero_cards.py   (from the repo root)
import json, os, re, sys
from fractions import Fraction as F

HERE = os.path.dirname(os.path.abspath(__file__))
WD = os.path.dirname(HERE)                                        # wiki_data/
ROOT = os.path.dirname(WD)                                        # repo root
OUT = os.path.join(ROOT, "ImperialCommander2", "Assets", "Resources",
                   "Languages", "En", "tactical_hero_cards.json")

def load(p):
    s = open(p, encoding="utf-8").read()
    return json.loads(re.sub(r",\s*([\}\]])", r"\1", s))

# printed attack-die faces from Core/Dice.cs AttackDieFaces (dmg, surge, acc)
FACES = {
    "R": [(0,0,0),(2,0,0),(2,0,0),(2,1,0),(3,0,0),(3,0,0)],
    "G": [(1,1,2),(1,1,2),(2,0,1),(2,0,2),(2,0,2),(0,1,1)],
    "B": [(2,0,2),(2,0,2),(1,1,3),(0,1,2),(0,0,4),(0,0,6)],
    "Y": [(2,0,0),(1,1,1),(1,2,1),(0,1,2),(0,1,1),(0,0,0)],
}
def acc_ev(letters):
    return sum(F(sum(f[2] for f in FACES[c]), 6) for c in letters)
def dmg_ev(letters):
    return sum(F(sum(f[0] for f in FACES[c]), 6) for c in letters)
def gate(letters):
    return int(acc_ev(letters) + F(1, 2))     # int(AccEv + 1/2) == Dice.AccGate

# Black defense die faces (blocks): 0 0 1 2 2 3
BLOCKS_EV = {"K": F(8, 6), "W": F(6, 6)}     # white incl. dodge=2 folded

def ev_damage(letters, dist, defend):
    """continuous EV of the frozen pairing formula (Dice EV branch), no surge."""
    if not letters:
        return F(0)
    raw = dmg_ev(letters)
    accok = (letters and (gate(letters) >= dist))
    if not accok:
        return F(0)
    avail = acc_ev(letters) - dist
    valid = min(raw, max(F(0), avail))
    blocks = sum(BLOCKS_EV.get(d, F(0)) for d in (defend or ""))
    return max(F(0), valid - blocks)

def slug(name):
    s = re.sub(r"<[^>]+>", "", name)
    s = s.replace("\u2019", "'")
    s = re.sub(r"[^A-Za-z0-9]+", "_", s).strip("_").lower()
    return s

def norm_name(n):
    if not n:
        return ""
    n = n.replace("\u2019", "'").replace("\u2018", "'")
    n = re.sub(r"\s+", " ", n).strip()
    return n.lower()

def letters_of(dice):
    d = (dice or "").strip().upper().replace(" ", "")
    if d and all(c in "BRGY" for c in d):
        return d
    # raw template form "{{Dice| BY}}" (wiki parse artifact: leading space
    # emptied the normalized dice_pool field, e.g. Gideon Argus)
    m = re.search(r"\{\{Dice\|\s*([BRGY ]+)\}\}", dice or "", re.IGNORECASE)
    if m:
        d = m.group(1).upper().replace(" ", "")
        if d and all(c in "BRGY" for c in d):
            return d
    return ""

def main():
    heros = load(os.path.join(WD, "heros.json"))
    weapons = load(os.path.join(WD, "hero_weapons.json"))
    witems = load(os.path.join(WD, "items.json"))
    app_heroes = load(os.path.join(ROOT, "ImperialCommander2/Assets/Resources/CardData/heroes.json"))
    app_items = load(os.path.join(ROOT, "ImperialCommander2/Assets/Resources/Languages/En/CampaignData/items.json"))
    app_skills = load(os.path.join(ROOT, "ImperialCommander2/Assets/Resources/Languages/En/CampaignData/skills.json"))

    id_by_name = {norm_name(h["name"]): h["id"] for h in app_heroes}
    wby = {norm_name(w["hero"]): w for w in weapons}

    # ---------------- items: wiki -> app id, static printed effects ---------
    ATTR_POOL = {"I": "einsicht", "C": "tech", "F": "staerke"}
    wiki_by_key = {}
    for it in witems:
        wiki_by_key.setdefault((norm_name(it["name"]), it.get("tier")), []).append(it)

    report = {"item_unmatched": [], "item_skipped_effects": [], "item_attr": [],
              "item_static": []}
    items_out = []
    used_wiki = set()
    # documented name deviations app -> wiki (CONCEPT_tactical_ai.md 1.):
    ALIAS = {"telecoping sights": "telescoping sights",
             "concussion grenade": "concussion grenades",
             "r5 astromech": "r5 astromech (item)"}
    for ai in app_items:
        an = norm_name(ai["name"])
        an = ALIAS.get(an, an)
        cand = [w for k in wiki_by_key if k[0] == an for w in wiki_by_key[k]]
        if not cand:
            report["item_unmatched"].append(ai["id"] + " " + ai["name"])
            continue
        # prefer exact tier, then cost
        pick = sorted(cand, key=lambda w: (w.get("tier") != ai.get("tier"),
                                           w.get("cost") != ai.get("cost")))[0]
        used_wiki.add(pick["name"])
        entry = {"id": ai["id"], "typ": ai["type"]}
        dice = letters_of(pick.get("dice"))
        attribut = None
        if (pick.get("dice") or "").strip() == "???":
            for eff in pick.get("effects") or []:
                m = re.search(r"attack pool with this weapon is your \{\{(\w)\}\} pool", eff)
                if m and m.group(1) in ATTR_POOL:
                    attribut = ATTR_POOL[m.group(1)]
            if attribut is None:
                report["item_unmatched"].append(ai["id"] + " " + ai["name"] + " (attribute pool unknown)")
                continue
            report["item_attr"].append(ai["id"] + " " + ai["name"] + " -> " + attribut)
        elif dice:
            entry["wuerfel"] = dice
        acc = dmg = pierce = 0
        for eff in pick.get("effects") or []:
            e = re.sub(r"\{\{Icon\|(\w+)\}\}", r"{\1}", eff)
            if re.match(r"\s*(\{\{Z\}\}|\{Z\}|\{\{A\}\}|\{A\}|Exhaust|While|When|After|At the start|Limit|This item|Your attack pool|If )", e):
                continue                       # surge/activated/triggered: not static
            m = re.match(r"\s*\+(\d+)\s*(Accuracy|Ac\.)\s*$", e)
            if m: acc += int(m.group(1)); continue
            m = re.match(r"\s*\+(\d+)\s*(\{\{D\}\}|\{D\}|\{\{DAMAGE\}\}|\{DAMAGE\}|\{Damage\})\s*$", e)
            if m: dmg += int(m.group(1)); continue
            m = re.match(r"\s*Pierce\s+(\d+)\s*$", e)
            if m: pierce += int(m.group(1)); continue
            if e.strip() in ("Reach",): continue
            if re.match(r"\s*\+(\d+)\s*(Health|S)\s*$", e): continue
            if e.strip(): report["item_skipped_effects"].append(ai["id"] + " :: " + e.strip()[:70])
        if acc: entry["genauigkeit"] = acc
        if dmg: entry["schaden"] = dmg
        if pierce: entry["durchschlag"] = pierce
        if attribut: entry["attribut"] = attribut
        if any(k in entry for k in ("wuerfel", "attribut", "genauigkeit", "schaden", "durchschlag")):
            items_out.append(entry)

    # ---------------- skills: app table owns xp costs ----------------------
    skills_by_owner = {}
    for sk in app_skills:
        if sk.get("cost", 0) > 0:
            skills_by_owner.setdefault(sk.get("owner"), []).append(sk)

    # ---------------- heroes ------------------------------------------------
    heroes_out = []
    threat_report = []
    for h in sorted(heros, key=lambda x: x["name"]):
        name = h["name"]
        hid = id_by_name.get(norm_name(name))
        w = wby.get(norm_name(name))
        if hid is None or w is None:
            sys.exit("join failure for hero " + name)
        dice = letters_of(w.get("dice_pool") or w.get("dice"))
        melee = (w.get("type") or "").lower() == "melee"
        reach = any(re.sub(r"\s+", " ", e).strip() == "Reach" for e in w.get("effects") or [])
        if melee:
            reich = 2 if reach else 1
            basis = dice + ("+Reach" if reach else "")
        else:
            reich = gate(dice)
            basis = dice
        faeh = {}
        for ab in h.get("abilities") or []:
            label = (ab.get("text") or "").split("\n")[0]
            label = re.sub(r"'+", "", label).strip()
            s = slug(label)
            if not s: continue
            st = int(ab.get("strain") or 0)
            cur = faeh.setdefault(s, [None, st])
            cur[1] = min(cur[1], st)
        for sk in skills_by_owner.get(hid, []):
            s = slug(sk["name"])
            cur = faeh.setdefault(s, [int(sk["cost"]), 0])
            cur[0] = int(sk["cost"])
        faehigkeiten = [{"bezeichnung": s, "xp_kosten": v[0], "strain_kosten": v[1]}
                        for s, v in sorted(faeh.items())]
        ges, ver = h["healthy"], h["wounded"]
        heroes_out.append({
            "name": name, "appHeroId": hid,
            "gesUnderhalt": ges["health"], "gesundeAusdauer": ges["endurance"],
            "verwundetesGesundheit": ver["health"], "verwundeteAusdauer": ver["endurance"],
            "tempo": ges["speed"], "tempoVerwundet": ver["speed"],
            "verteidigung": ges["defense"], "verteidigungVerwundet": ver["defense"],
            "attribute": {"staerke": ges["strength"], "einsicht": ges["insight"], "tech": ges["tech"]},
            "attributeVerwundet": {"staerke": ver["strength"], "einsicht": ver["insight"], "tech": ver["tech"]},
            "angriffsTyp": "P" if melee else "O",
            "basisAngriffswuerfel": basis,
            "reichweite": reich,
            "faehigkeiten": faehigkeiten,
        })
        threat_report.append((name, basis, reich,
                              str(ev_damage(dice, 1 if not melee else 0, "K"))))

    asset = {
        "schema": "tacai.hero_cards/1",
        "source": ("Kuratierung: oeffentlich gedruckte Heldenkarten-Werte aller 21 "
                   "Kampagnen-Helden (gesund + verwundete Rueckseite), Basiswaffen und "
                   "Item-Wuerfel/Modifikatoren; ids und xp-Kosten aus den App-Tabellen "
                   "(CardData/heroes.json, CampaignData/items.json, CampaignData/"
                   "skills.json). Cleanroom-Policy: wiki_data/LICENSE_DATA.md — nur "
                   "Zahlen, Wuerfelbuchstaben und reine Keyword-Slugs, keine Prosa."),
        "conventions": {
            "wuerfelBuchstaben": "B/R/G/Y = gedruckte Angruesfel (App-Konvention); verteidigung: K=schwarz, W=weiss",
            "basisAngriffswuerfel": "Basiswuerfel der gedruckten Basiswaffe, Nahkampf-Waffe mit aufgedrucktem Reach: Suffix '+Reach'",
            "angriffsTyp": "P = Nahkampf, O = Fernkampf (App-Symbolkonvention keyword_table.json)",
            "reichweite": "Nahkampf 1 (mit Reach 2); Fernkampf = int(Genauigkeits-EW + 1/2) der gedruckten Waffe — Acc-Gate der ATTACKS-Regelseite (IC2-Bandensemantik)",
            "attribut": "Waffen mit Attribut-Pool ('Your attack pool is your X pool'): staerke/einsicht/tech — im verwundeten Zustand schrumpfen die Attributswuerfel (verwundete Kartenseite)",
            "items": "nur Items mit Angriffswirkung; genauigkeit/schaden/durchschlag = statische (nicht-Surge) Aufdrucke; Surge-Zeilen bleiben bewusst aussen vor (konservative Vereinfachung, wie Dice.cs)",
            "faehigkeiten": "reine Keyword-Slugs + Zahlen (xp-Kosten aus App-Tabelle, strain-Kosten von der gedruckten Karte); Konsum erst M11d",
        },
        "heroes": heroes_out,
        "items": sorted(items_out, key=lambda x: x["id"]),
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(asset, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print("wrote", OUT, "heroes:", len(heroes_out), "items:", len(items_out))
    print("unmatched app items:", report["item_unmatched"] or "-")
    print("attr-pool items:", report["item_attr"] or "-")
    print("skipped effects sample:")
    for x in report["item_skipped_effects"][:25]: print("   ", x)
    print("\nname | basis | reich | EV dmg vs black dist1 (melee dist0)")
    for t in threat_report: print("  %-16s %-9s %2d  %s" % t)

if __name__ == "__main__":
    main()
