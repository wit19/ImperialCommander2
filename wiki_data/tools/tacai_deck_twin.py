#!/usr/bin/env python3
"""M12b canon authoring twin (wiki_data/tools/tacai_deck_twin.py).

Independent Python re-implementation of the M12b score model exactly as
documented in Assets/Scripts/TacticalAI/Core/ImperialDecks.cs (header) - used
to AUTHOR the expect blocks of Tests/Fixtures/tacai/imperial_deck.json, the
same 'authored, not recorded' route M11d/M11h used for formula vectors.
It reads the SHIPPED build asset (not a fixture copy), so a divergence
between Core and this twin surfaces as a harness FAIL, never a silent match.

NOT part of any gate; documentation/authoring tool.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSET = (ROOT / "ImperialCommander2/Assets/Resources/Languages/En/"
         "tactical_imperial_decks.json")
MASK = (1 << 64) - 1

# plural rule (?:ES|S)? - must mirror ImperialDecks.cs EXACTLY
FAMILY_PATTERNS = {
    "TROOPER": r"\bTROOPER(?:ES|S)?\b",
    "REBEL": r"\bREBEL(?:ES|S)?\b",
    "VEHICLE": r"\bVEHICLE(?:ES|S)?\b",
    "DROID": r"\bDROID(?:ES|S)?\b",
    "HEAVY WEAPON": r"\bHEAVY\s+WEAPON(?:ES|S)?\b",
    "MINE": r"\bMINE(?:ES|S)?\b",
    "HERO": r"\bHERO(?:ES|S)?\b",
    "OFFICER": r"\bOFFICER(?:ES|S)?\b",
    "GUARDIAN": r"\bGUARDIAN(?:ES|S)?\b",
    "COMMANDER": r"\bCOMMANDER(?:ES|S)?\b",
    "AGENDA": r"\bAGENDA(?:ES|S)?\b",
    "MISSION": r"\bMISSION(?:ES|S)?\b",
}
UNIT_FAMILIES = ["TROOPER", "VEHICLE", "DROID", "HEAVY WEAPON", "MINE",
                 "OFFICER", "GUARDIAN", "COMMANDER"]
MISSION_TYPE_NAMES = ["Story", "Side", "Forced", "Introduction", "Interlude",
                      "Finale"]  # Saga.MissionType (Interfaces-Enums.cs)


def fnv1a64(s):
    h = 1469598103934665603
    for ch in (s or ""):
        h = ((h ^ ord(ch)) * 1099511628211) & MASK
    return h


def deck_seed_for(guid):
    h = fnv1a64((guid or "") + "|imperial-deck")
    return 0xA5A5A5A5A5A5A5A5 if h == 0 else h


def splitmix_next64(state):
    state = (state + 0x9E3779B97F4A7C15) & MASK
    z = state
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & MASK
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & MASK
    return state, (z ^ (z >> 31)) & MASK


def noise_for(seed, deck_id):
    _, z = splitmix_next64(seed ^ fnv1a64("noise|" + deck_id))
    return (z >> 32) % 4


def family_counts(text):
    out = {}
    if not text:
        return out
    up = text.upper()
    for fam, pat in FAMILY_PATTERNS.items():
        n = len(re.findall(pat, up))
        if n:
            out[fam] = out.get(fam, 0) + n
    return out


def add_into(acc, counts):
    for k, v in counts.items():
        acc[k] = acc.get(k, 0) + v


def deck_family_counts(deck):
    acc = {}
    for c in deck["cards"]:
        for a in c["abilities"]:
            add_into(acc, family_counts(a["text"]))
        add_into(acc, family_counts(c["restriction"]))
        add_into(acc, family_counts(c["keyword"]))
    return acc


def cap(v, c):
    return 0 if v < 0 else (c if v > c else v)


def synergy(deck_fams, prof):
    s = 0
    vf = prof.get("villain_families", {})
    for f in UNIT_FAMILIES:
        s += 3 * cap(vf.get(f, 0), 4) * cap(deck_fams.get(f, 0), 4)
    s += 2 * cap(prof.get("hero_count", 0), 4) * cap(deck_fams.get("HERO", 0), 4)
    s += 1 * cap(prof.get("hero_count", 0), 4) * cap(deck_fams.get("REBEL", 0), 4)
    s += 2 * cap(prof.get("agenda_missions", 0), 4) * cap(deck_fams.get("AGENDA", 0), 4)
    s += 1 * cap(prof.get("mission_count", 0), 8) * cap(deck_fams.get("MISSION", 0), 4)
    return s


def profile_from_save(save):
    prof = {"hero_count": 0, "villain_families": {}, "mission_count": 0,
            "agenda_missions": 0}
    for h in save.get("campaignHeroes") or []:
        if h.get("heroID"):
            prof["hero_count"] += 1
    for v in save.get("campaignVillains") or []:
        fams = set(family_counts(v.get("name", "")))
        fams |= set(v.get("families") or [])
        for f in fams:
            prof["villain_families"][f] = prof["villain_families"].get(f, 0) + 1
    for m in save.get("campaignStructure") or []:
        prof["mission_count"] += 1
        mt = m.get("missionType")
        tname = mt if isinstance(mt, str) else (
            MISSION_TYPE_NAMES[mt] if isinstance(mt, int) and 0 <= mt < len(MISSION_TYPE_NAMES) else "")
        if tname == "Agenda" or m.get("isAgendaMission"):
            prof["agenda_missions"] += 1
    return prof


def choose(table, prof, seed):
    rows = []
    for deck in table["decks"]:
        syn = synergy(deck_family_counts(deck), prof)
        noise = noise_for(seed, deck["id"])
        rows.append({"deck": deck["id"], "synergy": syn, "noise": noise,
                     "total": syn + noise})
    rows.sort(key=lambda r: (-r["total"], r["deck"]))
    return rows


def choice_block(table, save):
    prof = profile_from_save(save)
    seed = deck_seed_for(save["GUID"])
    rows = choose(table, prof, seed)
    return {"seed": "%016x" % seed, "deck": rows[0]["deck"], "scores": rows}


def build_fixture(table, saves):
    vehicles_enriched = {
        "GUID": saves["start_vehicles"]["GUID"],  # same seed, enriched families
        "campaignHeroes": saves["start_vehicles"]["campaignHeroes"],
        "campaignVillains": [
            {"name": "Imperial Speeder Bike", "families": ["VEHICLE", "TROOPER"]},
            {"name": "Juggernaut", "families": ["VEHICLE", "HEAVY WEAPON"]},
            {"name": "Reinforced Juggernaut", "families": ["VEHICLE", "HEAVY WEAPON"]},
            {"name": "TIE Fighter", "families": ["VEHICLE"]},
            {"name": "Scout Trooper"},
        ],
        "campaignStructure": saves["start_vehicles"]["campaignStructure"],
    }
    backfill_enriched = {
        "GUID": saves["backfill"]["GUID"],
        "campaignHeroes": saves["backfill"]["campaignHeroes"],
        "campaignVillains": [
            {"name": "Shoretrooper", "families": ["TROOPER"]},
            {"name": "Shoretrooper", "families": ["TROOPER"]},
            {"name": "Heavy Weapon Expert"},
            {"name": "Imperial Shoretrooper", "families": ["TROOPER"]},
        ],
        "campaignStructure": saves["backfill"]["campaignStructure"],
    }
    empty_save = {"GUID": "empty-campaign-guid"}
    guids = [saves["start_core"]["GUID"], saves["start_vehicles"]["GUID"],
             saves["backfill"]["GUID"], empty_save["GUID"]]
    return {
        "_comment": (
            "M12b Imperial-Klassendeck: Deck-Persistenz + deterministische "
            "KI-Deckwahl. AUTHORED, nicht recorded (wie strain_budget.json): "
            "Erwartungswerte aus der im Core-Header dokumentierten Regel "
            "(Assets/Scripts/TacticalAI/Core/ImperialDecks.cs) - unabhaengiger "
            "Python-Twin wiki_data/tools/tacai_deck_twin.py liest das SHIPPED "
            "Asset (tactical_imperial_decks.json) und berechnet Synergie + "
            "SplitMix64-Noise; der Harness laeuft gegen das C#-Original, eine "
            "Divergenz faellt als FAIL auf. profile-Mapping = "
            "ProfileFromCampaignSave auf save-Form (missionType als int wie im "
            "Save). WICHTIG: echte Saves serialisieren campaignVillains OHNE "
            "traits ([JsonIgnore]) - Familien-Signal aus dem Save sind die "
            "Kartennamen; zusammengesetzte Namen wie 'Shoretrooper' liefern "
            " bewusst KEIN TROOPER-Token (Wortgrenze!), das schliesst der "
            "Live-Adapter ueber die App-eigene Trait-Tabelle (families-"
            "Feld = dokumentierter Anreicherungs-Naeht, im Save nie "
            "vorhanden). backfill pinnt zusaetzlich den Ordinal-Tiebreak "
            "(hutt_mercenaries vs subversive_tactics, gleicher Total)."),
        "schema": "tacai.imperial_deck/1",
        "table": {
            "deck_count": len(table["decks"]),
            "card_count": sum(len(d["cards"]) for d in table["decks"]),
            "deck_order": [d["id"] for d in table["decks"]],
            "paid_xp_expect": [1, 1, 2, 2, 3, 3, 4, 4],
        },
        "seeds": {
            "fnv_empty_hex": "%016x" % fnv1a64(""),
            "vectors": [{"key": g, "expect": "%016x" % deck_seed_for(g)}
                        for g in guids],
        },
        "start_cases": [
            {"name": "start_core", "campaign_save": saves["start_core"],
             "expect": choice_block(table, saves["start_core"])},
            {"name": "start_vehicles", "campaign_save": saves["start_vehicles"],
             "expect": choice_block(table, saves["start_vehicles"])},
            {"name": "vehicles_enriched", "campaign_save": vehicles_enriched,
             "expect": choice_block(table, vehicles_enriched)},
        ],
        "backfill": {
            "name": "backfill",
            "campaign_save": saves["backfill"],
            "expect": choice_block(table, saves["backfill"]),
        },
        "backfill_enriched": {
            "name": "backfill_enriched",
            "campaign_save": backfill_enriched,
            "expect": choice_block(table, backfill_enriched),
        },
        "empty_profile": {
            "name": "empty_profile",
            "campaign_save": empty_save,
            "expect": choice_block(table, empty_save),
        },
    }


def main():
    table = json.loads(ASSET.read_text(encoding="utf-8"))
    cmd = sys.argv[1] if len(sys.argv) > 1 else "profiles"

    # per-deck family profiles for review
    if cmd == "families":
        for d in table["decks"]:
            print(d["id"], deck_family_counts(d))
        return

    saves = {
        # core A5-style campaign: trooper+droid+heavy weapon lineup, 3 heroes,
        # 7 missions of which 2 agenda
        "start_core": {
            "GUID": "0f1e2d3c-4b5a-4938-8726-152433221100",
            "campaignHeroes": [{"heroID": "H1"}, {"heroID": "H2"}, {"heroID": "H4"}],
            "campaignVillains": [
                {"name": "Scout Trooper"},
                {"name": "Assault Trooper"},
                {"name": "Imperial Probe Droid"},
                {"name": "Heavy Weapon Expert"},
                {"name": "Omblesse Officer"},
            ],
            "campaignStructure": [
                {"missionType": 0}, {"missionType": 0},
                {"missionType": 1, "isAgendaMission": True},
                {"missionType": 1}, {"missionType": 0},
                {"missionType": 0, "isAgendaMission": True},
                {"missionType": 5},
            ],
        },
        # vehicle-heavy Overlord roster, 2 heroes, short story arc
        "start_vehicles": {
            "GUID": "11111111-2222-3333-4444-555555555555",
            "campaignHeroes": [{"heroID": "H1"}, {"heroID": "H2"}],
            "campaignVillains": [
                {"name": "Imperial Speeder Bike"},
                {"name": "Juggernaut"},
                {"name": "Reinforced Juggernaut"},
                {"name": "TIE Fighter"},
                {"name": "Scout Trooper"},
            ],
            "campaignStructure": [
                {"missionType": 0}, {"missionType": 0}, {"missionType": 0},
                {"missionType": 5},
            ],
        },
        # older save WITHOUT any deck data - backfill case
        "backfill": {
            "GUID": "a7b3c9d1-5e2f-4a8b-9c0d-1e2f3a4b5c6d",
            "formatVersion": 2,
            "campaignHeroes": [{"heroID": "H7"}, {"heroID": "H12"}],
            "campaignVillains": [
                {"name": "Shoretrooper"},
                {"name": "Shoretrooper"},
                {"name": "Heavy Weapon Expert"},
                {"name": "Imperial Shoretrooper"},
            ],
            "campaignStructure": [
                {"missionType": 0}, {"missionType": 0},
                {"missionType": 1, "isAgendaMission": True},
            ],
        },
    }

    if cmd == "fixture":
        fixture = build_fixture(table, saves)
        dst = (ROOT / "ImperialCommander2/Assets/Tests/Fixtures/tacai/"
               "imperial_deck.json")
        dst.write_text(json.dumps(fixture, indent=1, ensure_ascii=False) + "\n",
                       encoding="utf-8")
        print("wrote", dst.relative_to(ROOT))
        return

    if cmd == "profiles":
        print(json.dumps({"fnv_empty": fnv1a64(""),
                          "seeds": {k: "%016x" % deck_seed_for(v["GUID"])
                                    for k, v in saves.items()},
                          "profiles": {k: profile_from_save(v) for k, v in saves.items()},
                          "choices": {k: choose(table, profile_from_save(v),
                                                deck_seed_for(v["GUID"]))
                                      for k, v in saves.items()}},
                         indent=1))
    else:
        # hand-check one noise draw on stdout
        seed = deck_seed_for(saves["backfill"]["GUID"])
        print("backfill seed", "%016x" % seed)
        for d in table["decks"]:
            print(d["id"], noise_for(seed, d["id"]))


if __name__ == "__main__":
    main()
