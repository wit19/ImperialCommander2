#!/usr/bin/env python3
"""M12c canon authoring twin (wiki_data/tools/tacai_purchase_twin.py).

Independent Python re-implementation of the M12c purchase rule exactly as
documented in Assets/Scripts/TacticalAI/Core/ImperialPurchases.cs (header) -
used to AUTHOR the expect blocks of
Tests/Fixtures/tacai/imperial_purchases.json ('authored, not recorded', the
M11d/M11h/M12b route). It reads the SHIPPED build asset (not a fixture copy)
and the campaign-save blocks of the M12b fixture (imperial_deck.json), so a
divergence between Core and this twin surfaces as a harness FAIL, never a
silent match.

The seed/FNV/SplitMix64/token-scan primitives are imported from the M12b
twin (already cross-verified against the C# original by the imperial_deck
canon); only the PURCHASE layer (per-card synergy, "buy|" noise salt, greedy
pick) is re-implemented here per the M12c header.

NOT part of any gate; documentation/authoring tool.

Usage:
  tacai_purchase_twin.py fixture   -> prints the fixture JSON
  tacai_purchase_twin.py plan      -> prints one full ranked plan (review)
"""
import importlib.util
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSET = (ROOT / "ImperialCommander2/Assets/Resources/Languages/En/"
         "tactical_imperial_decks.json")
DECK_FIXTURE = (ROOT / "ImperialCommander2/Assets/Tests/Fixtures/tacai/"
                "imperial_deck.json")

_spec = importlib.util.spec_from_file_location(
    "tacai_deck_twin", Path(__file__).resolve().parent / "tacai_deck_twin.py")
assert _spec is not None and _spec.loader is not None
dt = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(dt)


def noise_for_purchase(seed, card_id):
    """Mirrors ImperialPurchases.NoiseForPurchase: DeterministicRandom(
    seed ^ fnv("buy|"+id)).RandBelow(4) - one SplitMix64 step, multiply-
    shift (identical to the deck twin's noise_for, different salt)."""
    _, z = dt.splitmix_next64(seed ^ dt.fnv1a64("buy|" + card_id))
    return (z >> 32) % 4


def card_family_counts(card):
    acc = {}
    for a in card["abilities"]:
        dt.add_into(acc, dt.family_counts(a["text"]))
    dt.add_into(acc, dt.family_counts(card.get("restriction")))
    dt.add_into(acc, dt.family_counts(card.get("keyword")))
    return acc


def card_synergy(card_fams, prof):
    """Same vocabulary + weights as the deck score, per-card mention factor
    capped at 1 (a single card can only contribute its own mention)."""
    s = 0
    vf = prof.get("villain_families", {})
    for f in dt.UNIT_FAMILIES:
        s += 3 * dt.cap(vf.get(f, 0), 4) * dt.cap(card_fams.get(f, 0), 1)
    s += 2 * dt.cap(prof.get("hero_count", 0), 4) * dt.cap(card_fams.get("HERO", 0), 1)
    s += 1 * dt.cap(prof.get("hero_count", 0), 4) * dt.cap(card_fams.get("REBEL", 0), 1)
    s += 2 * dt.cap(prof.get("agenda_missions", 0), 4) * dt.cap(card_fams.get("AGENDA", 0), 1)
    s += 1 * dt.cap(prof.get("mission_count", 0), 8) * dt.cap(card_fams.get("MISSION", 0), 1)
    return s


def ranked_candidates(deck, prof, owned, seed):
    """Every remaining printed purchasable card (non-basic, XP cost, not
    owned), ranked total desc then card id asc (ordinal) - the total order
    from the Core header."""
    rows = []
    for c in deck["cards"]:
        if c.get("basic") or c.get("xp") is None:
            continue
        if c["id"] in owned:
            continue
        syn = card_synergy(card_family_counts(c), prof)
        noise = noise_for_purchase(seed, c["id"])
        rows.append({"card": c["id"], "name": c["name"], "cost": c["xp"],
                     "synergy": syn, "noise": noise, "total": syn + noise})
    rows.sort(key=lambda r: (-r["total"], r["card"]))
    return rows


def first_affordable(rows, balance):
    """The greedy step: buy the BEST AFFORDABLE card; higher-ranked but
    unaffordable cards are skipped for this step, never waited for."""
    for r in rows:
        if r["cost"] <= balance:
            return r
    return None


def run_stage(deck, prof, owned, balance, seed, trace):
    """One upgrade stage: repeat the greedy step while affordable.
    Returns the balance after the stage (purchases subtracted)."""
    while True:
        rows = ranked_candidates(deck, prof, owned, seed)
        buy = first_affordable(rows, balance)
        if buy is None:
            return balance
        owned.add(buy["card"])
        balance -= buy["cost"]
        trace.append(buy["card"])


def simulate(table, save, xp_events):
    prof = dt.profile_from_save(save)
    seed = dt.deck_seed_for(save["GUID"])
    deck_rows = dt.choose(table, prof, seed)  # the M12b choice
    deck_id = deck_rows[0]["deck"]
    deck = next(d for d in table["decks"] if d["id"] == deck_id)
    owned = {c["id"] for c in deck["cards"] if c.get("basic")}
    balance, bought = 0, []
    for xp in xp_events:
        balance += xp
        balance = run_stage(deck, prof, owned, balance, seed, bought)
    return {"deck": deck_id, "bought": bought, "balance": balance}


def plan_block(table, save, xp_balance):
    prof = dt.profile_from_save(save)
    seed = dt.deck_seed_for(save["GUID"])
    deck_rows = dt.choose(table, prof, seed)
    deck_id = deck_rows[0]["deck"]
    deck = next(d for d in table["decks"] if d["id"] == deck_id)
    owned = {c["id"] for c in deck["cards"] if c.get("basic")}
    rows = ranked_candidates(deck, prof, owned, seed)
    buy = first_affordable(rows, xp_balance)
    return {"name": "plan_first_ranked", "campaign_save": save,
            "xp_balance": xp_balance,
            "seed": "%016x" % seed, "deck": deck_id,
            "bought": buy["card"] if buy else "",
            "cost": buy["cost"] if buy else 0,
            "scores": rows}


def build_fixture(table):
    m12b = json.loads(DECK_FIXTURE.read_text(encoding="utf-8"))
    saves = {c["name"]: c["campaign_save"] for c in m12b["start_cases"]}
    empty_save = m12b["empty_profile"]["campaign_save"]

    cases = [
        # Mission 1 pays 2 XP: exact greedy pick of the best affordable card.
        ("core_first_stage", saves["start_core"], [2]),
        # Fixed XP-Historie: 8 mission-end payouts (1,1,2,2,3,3,4,4 = the
        # printed XP structure of every deck) -> exact purchase order, WITH
        # the affordability interleave: a higher-ranked card nobody can pay
        # yet is skipped for the step, never waited for (Core header).
        ("core_slow_ledger", saves["start_core"], [1, 1, 2, 2, 3, 3, 4, 4]),
        # One rich stage pays for everything at once -> the PURE priority
        # order. Comparing the two hutt_mercenaries ledgers pins that the
        # only difference between them is the affordability interleave.
        ("core_rich_single_stage", saves["start_core"], [20]),
        # Vehicle campaign (deck pinned by M12b start_vehicles): 5 XP.
        ("vehicles_ledger", saves["start_vehicles"], [5]),
        # No XP at all -> nothing bought, account stays 0 ('kein Kauf').
        ("core_zero", saves["start_core"], []),
        # Empty profile campaign (deck pinned by M12b empty_profile): 3 XP.
        ("empty_profile_ledger", empty_save, [3]),
    ]

    noise_cards = [c["id"] for d in table["decks"] if d["id"] in
                   ("hutt_mercenaries", "nemeses") for c in d["cards"]]
    vectors = []
    for key in (saves["start_core"]["GUID"], saves["start_vehicles"]["GUID"]):
        seed = dt.deck_seed_for(key)
        for cid in noise_cards[:6]:
            vectors.append({"key": key, "card": cid,
                            "expect": noise_for_purchase(seed, cid)})

    return {
        "_comment": (
            "M12c Imperial Upgrade Stage: deterministische KI-Kauflogik. "
            "AUTHORED, nicht recorded (wie imperial_deck.json): Erwartungswerte "
            "aus der im Core-Header dokumentierten Regel (Assets/Scripts/"
            "TacticalAI/Core/ImperialPurchases.cs) - unabhaengiger Python-Twin "
            "wiki_data/tools/tacai_purchase_twin.py gegen das SHIPPED Asset "
            "(tactical_imperial_decks.json); Kampagnen-Save-Blöcke = die "
            "identischen Profile aus imperial_deck.json (Deckwahl dort von M12b "
            "gepinnt, hier wiederverwendet - dasselbe Profil, dieselbe "
            "Familien-Synergie). Regeln: RR 'Imperial Upgrade Stage' - volles "
            "Deck sichtbar, kein Markt/Zufall; KI kauft greedy nach Score gegen "
            "das M12b-Profil, Gleichstand -> Karten-id aufsteigend; ein Schritt "
            "= '1 Karte, wenn XP >= Preis', die Stage wiederholt den Schritt "
            "(RR erlaubt beliebig viele Kaeufe). Basic-Karten sind bei "
            "Deckwahl bereits Owned und NIE Kandidat. Noise-Salz 'buy|' "
            "(distinct von 'noise|'). XP-Historie = xp_events (Buchungen, dann "
            "Stage); 'bought' = exakte Kaufreihenfolge, 'balance' = Endstand."),
        "schema": "tacai.imperial_purchases/1",
        "noise_vectors": vectors,
        "purchase_cases": [
            {"name": n, "campaign_save": s, "xp_events": ev,
             "expect": simulate(table, s, ev)}
            for (n, s, ev) in cases
        ],
        "plan_snapshot": plan_block(table, saves["start_core"], 20),
    }


def main():
    table = json.loads(ASSET.read_text(encoding="utf-8"))
    cmd = sys.argv[1] if len(sys.argv) > 1 else "fixture"
    if cmd == "plan":
        m12b = json.loads(DECK_FIXTURE.read_text(encoding="utf-8"))
        saves = {c["name"]: c["campaign_save"] for c in m12b["start_cases"]}
        print(json.dumps(plan_block(table, saves["start_core"], 20),
                         indent=2, ensure_ascii=False))
        return
    print(json.dumps(build_fixture(table), indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
