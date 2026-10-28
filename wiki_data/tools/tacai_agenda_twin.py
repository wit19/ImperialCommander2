#!/usr/bin/env python3
"""M12d2 canon authoring twin (wiki_data/tools/tacai_agenda_twin.py).

Independent Python re-implementation of the M12d2 agenda rule exactly as
documented in Assets/Scripts/TacticalAI/Core/AgendaCards.cs + AgendaMarket.cs
headers - used to AUTHOR the expect blocks of
Tests/Fixtures/tacai/agenda.json ('authored, not recorded', the M11d/M12b/
M12c route). It reads the SHIPPED build asset (tactical_agenda_cards.json,
not a fixture copy), so a divergence between Core and this twin surfaces as
a harness FAIL, never a silent match.

Mirror map (C# -> twin), all primitives from the cross-verified M12b twin:
  AgendaCards.AgendaSeedFor      -> agenda_seed_for
  AgendaCards.NoiseForSet/Buy    -> noise_for (salts "agenda-set|","agenda-buy|")
  AgendaCards.SetSynergy         -> set_synergy  (= per-card CardSynergy sum,
                                   cap(.,1) + kept_secret slot 2*cap(agenda,4))
  AgendaMarket.CardSynergy       -> card_synergy (identical per-card function)
  AgendaCards.SetLegal           -> set_legal    (per-card printed [lo,hi])
  ChooseDeck stack+shuffle       -> choose_deck  (chosen-set order, agenda1..3,
                                   salt "agenda-shuffle|setup", i=n-1..1)
  AgendaMarket.RunStage          -> market_plan / run_stages (ranked greedy
                                   buys; NotPurchased in MARKET order;
                                   DeckAfter = remainder + NotPurchased)

NOT part of any gate; documentation/authoring tool.

Usage:
  tacai_agenda_twin.py fixture        -> prints the agenda.json fixture JSON
  tacai_agenda_twin.py stage N        -> prints market stage N of the pinned
                                         rich campaign (review)
"""
import importlib.util
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSET = (ROOT / "ImperialCommander2/Assets/Resources/Languages/En/"
         "tactical_agenda_cards.json")
DECK_FIXTURE = (ROOT / "ImperialCommander2/Assets/Tests/Fixtures/tacai/"
                "imperial_deck.json")

_spec = importlib.util.spec_from_file_location(
    "tacai_deck_twin", Path(__file__).resolve().parent / "tacai_deck_twin.py")
assert _spec is not None and _spec.loader is not None
dt = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(dt)


# ---------------------------------------------------------------- seeds
def agenda_seed_for(guid):
    h = dt.fnv1a64((guid or "") + "|agenda-deck")
    return 0xA5A5A5A5A5A5A5A5 if h == 0 else h


def noise_for(seed, salt, key):
    _, z = dt.splitmix_next64(seed ^ dt.fnv1a64(salt + key))
    return (z >> 32) % 4


# ------------------------------------------------- shuffle (Core law)
def shuffle_stream(ids, seed, salt):
    """AgendaCards.Shuffle on DeterministicRandom(seed ^ fnv(salt)):
    i = n-1..1, j = RandBelow(i+1) -> one SplitMix64 step per swap (i>=1
    always takes the n>1 branch). Returns the permuted list."""
    ids = list(ids)
    state = seed ^ dt.fnv1a64(salt)
    for i in range(len(ids) - 1, 0, -1):
        state, z = dt.splitmix_next64(state)
        j = (z >> 32) % (i + 1)
        ids[i], ids[j] = ids[j], ids[i]
    return ids


# ------------------------------------------------- per-card synergy
def card_synergy(c, prof):
    """AgendaMarket.CardSynergy == the per-card term of
    AgendaCards.SetSynergy (identical weights/caps, names excluded)."""
    fams = dt.family_counts(c["text"])
    s = 0
    vf = prof.get("villain_families", {})
    for f in dt.UNIT_FAMILIES:
        s += 3 * dt.cap(vf.get(f, 0), 4) * dt.cap(fams.get(f, 0), 1)
    s += 2 * dt.cap(prof.get("hero_count", 0), 4) * dt.cap(fams.get("HERO", 0), 1)
    s += 1 * dt.cap(prof.get("hero_count", 0), 4) * dt.cap(fams.get("REBEL", 0), 1)
    s += 2 * dt.cap(prof.get("agenda_missions", 0), 4) * dt.cap(fams.get("AGENDA", 0), 1)
    s += 1 * dt.cap(prof.get("mission_count", 0), 8) * dt.cap(fams.get("MISSION", 0), 1)
    if c["type"] == "kept_secret":
        s += 2 * dt.cap(prof.get("agenda_missions", 0), 4)
    return s


def set_synergy(cards, prof):
    return sum(card_synergy(c, prof) for c in cards)


# --------------------------------------------------------- legality
def set_legal(cards, campaign_tp):
    """AgendaCards.SetLegal: a set is illegal as soon as ONE card prints a
    [lo,hi] range excluding the campaign number; no campaign number ->
    vacuously legal (the app models no campaign time period)."""
    if campaign_tp is None:
        return True
    for c in cards:
        tp = c.get("time_period")
        if not tp:
            continue
        if campaign_tp < tp[0] or campaign_tp > tp[1]:
            return False
    return True


# ------------------------------------------------------ setup choice
def choose_deck(sets, cards_by_set, prof, seed, campaign_tp=None):
    rows = []
    for s in sets:
        cards = cards_by_set[s["id"]]
        rows.append({
            "set": s["id"],
            "synergy": set_synergy(cards, prof),
            "noise": noise_for(seed, "agenda-set|", s["id"]),
            "total": set_synergy(cards, prof)
                     + noise_for(seed, "agenda-set|", s["id"]),
            "legal": set_legal(cards, campaign_tp),
        })
    rows.sort(key=lambda r: (-r["total"], r["set"]))
    chosen = [r["set"] for r in
              [r for r in rows if r["legal"]]][:6]
    stacked = []
    for sid in chosen:                       # chosen order, agenda1..3
        stacked += [c["id"] for c in cards_by_set[sid]]
    return {"seed": seed, "chosen_sets": chosen,
            "deck": shuffle_stream(stacked, seed, "agenda-shuffle|setup"),
            "rows": rows}


# ------------------------------------------------- market stage (RR)
def market_plan(deck, seed, stage, balance, table):
    d = shuffle_stream(deck, seed, "agenda-market|%d" % stage)
    n = min(4, len(d))
    market, remainder = d[:n], d[n:]
    ranked = []
    for cid in market:
        c = table["by_id"][cid]
        syn = card_synergy(c, table["prof"])
        nz = noise_for(seed, "agenda-buy|", cid)
        ranked.append({"row": {
            "card": cid, "name": c["name"], "type": c["type"],
            "cost": c["cost"], "read_aloud": c["type"] != "kept_secret",
            "synergy": syn, "noise": nz, "total": syn + nz}})
    ranked.sort(key=lambda r: (-r["row"]["total"], r["row"]["card"]))
    left = balance
    buys = []
    for r in ranked:                         # ranked greedy, skip if unaffordable
        if r["row"]["cost"] > left:
            continue
        left -= r["row"]["cost"]
        buys.append(r["row"])
    bought = {b["card"] for b in buys}
    not_purchased = [cid for cid in market if cid not in bought]  # MARKET order
    return {"stage": stage, "market": market, "buys": buys,
            "not_purchased": not_purchased,
            "deck_after": list(remainder) + not_purchased,
            "influence_before": balance, "influence_after": left}


def run_stages(deck, seed, influence_events, table):
    """Mirrors Saga.AgendaState.RunUpgradeStage: booked influence FIRST,
    market second; buys spend, remainder carries over (saving up is legal)."""
    d, bal, out = list(deck), 0, []
    for i, delta in enumerate(influence_events):
        bal += delta
        plan = market_plan(d, seed, i, bal, table)
        bal = plan["influence_after"]
        d = plan["deck_after"]
        out.append({k: plan[k] for k in
                    ("stage", "market", "buys", "not_purchased",
                     "deck_after", "influence_before", "influence_after")})
    return {"deck_after": d, "balance": bal, "stages": out}


def load_table():
    asset = json.loads(ASSET.read_text(encoding="utf-8-sig"))
    assert asset["schema"] == "tacai.agenda_cards/1", asset["schema"]
    by_id = {c["id"]: c for c in asset["cards"]}
    by_set = {s["id"]: [by_id[cid] for cid in s["cards"]] for s in asset["sets"]}
    return {"asset": asset, "sets": asset["sets"],
            "by_set": by_set, "by_id": by_id}


def author(profile_note=""):
    table = load_table()
    deckfx = json.loads(DECK_FIXTURE.read_text(encoding="utf-8-sig"))
    save_core = deckfx["start_cases"][0]["campaign_save"]
    # second profile: 5 heroes, agenda-heavy structure (2 agenda missions,
    # a forced mission, a finale) -> exercises the agenda_missions slots
    save_rich = json.loads(json.dumps(save_core))
    save_rich["GUID"] = "77aa55cc-33bb-44dd-9eff-011223344556"
    save_rich["campaignHeroes"] = [{"heroID": "H%d" % i} for i in range(1, 6)]
    save_rich["campaignStructure"] = [
        {"missionType": 0}, {"missionType": 1, "isAgendaMission": True},
        {"missionType": 3}, {"missionType": 1, "isAgendaMission": True},
        {"missionType": 0}, {"missionType": 5}]

    prof1 = dt.profile_from_save(save_core)
    prof2 = dt.profile_from_save(save_rich)
    seed1 = agenda_seed_for(save_core["GUID"])
    seed2 = agenda_seed_for(save_rich["GUID"])
    ch1 = choose_deck(table["sets"], table["by_set"], prof1, seed1)
    ch2 = choose_deck(table["sets"], table["by_set"], prof2, seed2)
    ch1_tp6 = choose_deck(table["sets"], table["by_set"], prof1, seed1, 6)

    by_type, by_cost, tp_printed = {}, {}, {}
    for c in table["by_id"].values():
        by_type[c["type"]] = by_type.get(c["type"], 0) + 1
        by_cost[str(c["cost"])] = by_cost.get(str(c["cost"]), 0) + 1
        if c.get("time_period"):
            tp_printed[str(c["time_period"])] = \
                tp_printed.get(str(c["time_period"]), 0) + 1

    fx = {"_comment":
          "M12d2 Agenda system: influence economy + 4-card market + AI buys. "
          "AUTHORED, not recorded (like imperial_deck.json / "
          "imperial_purchases.json): expectations computed by the "
          "independent Python twin wiki_data/tools/tacai_agenda_twin.py from "
          "the rules documented in Assets/Scripts/TacticalAI/Core/"
          "AgendaCards.cs + AgendaMarket.cs + Saga/Managers/AgendaState.cs, "
          "against the SHIPPED asset (tactical_agenda_cards.json). RR basis: "
          "FFG Rules Reference (wording secured 28.10.2026): setup chooses 6 "
          "sets of 3 -> 18-card deck; upgrade stage: XP first, then influence "
          "-> shuffles agenda deck, SECRETLY draws 4, buys any for printed "
          "influence, unpaid cards shuffle BACK unrevealed; types side/"
          "forced/ongoing/kept secret; influence over the campaign log, "
          "saving up legal; kept secrets secret until resolved (anti-leak: "
          "Saga AgendaState.RebelView, fixture anti_leak block). Time-period "
          "RR filter is per-card [lo,hi] (fixture time_period_gate) - the app "
          "models no campaign-level time period, so the production default "
          "stays vacuous-null (honest, documented). Campaign-save blocks "
          "reuse the M12b profile fixture (GUID varied -> agenda seed)."
          + profile_note,
          "schema": 1,
          "asset_stats": {
              "schema": table["asset"]["schema"],
              "set_count": len(table["sets"]),
              "card_count": len(table["by_id"]),
              "time_period_cards": tp_printed,
              "by_type": by_type, "by_cost": by_cost}}

    # --- fixture (a): 6-set choice reproducible + TP gate
    fx["setup_cases"] = [
        {"name": "agenda_core", "campaign_save": save_core,
         "expect": {"seed": "%016x" % seed1,
                    "chosen_sets": ch1["chosen_sets"],
                    "deck": ch1["deck"],
                    "top_totals": [r["total"] for r in ch1["rows"][:8]]}},
        {"name": "agenda_rich", "campaign_save": save_rich,
         "expect": {"seed": "%016x" % seed2,
                    "chosen_sets": ch2["chosen_sets"],
                    "deck": ch2["deck"],
                    "top_totals": [r["total"] for r in ch2["rows"][:8]]}},
        {"name": "time_period_gate_6", "campaign_save": save_core,
         "campaign_time_period": 6,
         "expect": {"seed": "%016x" % seed1,
                    "chosen_sets": ch1_tp6["chosen_sets"],
                    "deck": ch1_tp6["deck"],
                    "illegal_sets": sorted(
                        r["set"] for r in ch1_tp6["rows"] if not r["legal"])}},
    ]

    # --- fixture (b): market sequence over 3 upgrade stages (fixed seed,
    #     unpaid-return law incl. frozen deck_after) + saving-up carry-over
    table["prof"] = prof2
    infl = [3, 1, 5]
    sim = run_stages(ch2["deck"], seed2, infl, table)
    d = ch2["deck"]
    for st in sim["stages"]:
        assert len(st["market"]) == min(4, len(d)), st["stage"]
        assert len(st["deck_after"]) == len(d) - len(st["buys"])
        assert st["influence_after"] >= 0
        d = st["deck_after"]
    total_buys = sum(len(st["buys"]) for st in sim["stages"])
    fx["market_stages"] = {
        "name": "agenda_rich_3stages", "campaign_save": save_rich,
        "influence_events": infl,
        "expect_stages": sim["stages"],
        "expect_final": {"deck_after": sim["deck_after"],
                         "balance": sim["balance"],
                         "total_buys": total_buys}}

    # --- fixture (b2): empty-profile market (no kept_secret preference -
    #     agenda_missions=0 zeroes the kept-secret slot) so the pinned buys
    #     exercise the PUBLIC read-aloud variety alongside the hidden one.
    save_empty = {"GUID": "00000000-0000-0000-0000-000000000000",
                  "campaignHeroes": [], "campaignVillains": [],
                  "campaignStructure": []}
    prof0 = dt.profile_from_save(save_empty)
    seed0 = agenda_seed_for(save_empty["GUID"])
    ch0 = choose_deck(table["sets"], table["by_set"], prof0, seed0)
    table["prof"] = prof0
    sim0 = run_stages(ch0["deck"], seed0, [4, 4], table)
    fx["market_stages_plain"] = {
        "name": "agenda_empty_profile_2stages", "campaign_save": save_empty,
        "influence_events": [4, 4],
        "expect_stages": sim0["stages"],
        "expect_final": {"deck_after": sim0["deck_after"],
                         "balance": sim0["balance"],
                         "total_buys": sum(len(st["buys"])
                                           for st in sim0["stages"])}}

    # --- fixture (c): backfill identity (store round-trip is checked by the
    #     harness against THIS pinned deck; the twin pins the deck itself)
    fx["backfill_identity"] = {
        "campaign_save": save_rich,
        "expect": {"seed": "%016x" % seed2, "deck": ch2["deck"]}}

    # --- fixture (d): kept-secret anti-leak contract
    hidden = [b for st in sim["stages"] for b in st["buys"]
              if b["type"] == "kept_secret"]
    fx["anti_leak"] = {
        "campaign_save": save_rich, "influence_events": infl,
        "expect": {
            "kept_secret_buys": [b["card"] for b in hidden],
            "buy_rows": [{"type": b["type"], "cost": b["cost"],
                          "card": "" if b["type"] == "kept_secret"
                          else b["card"]}
                         for st in sim["stages"] for b in st["buys"]],
            "forbidden_strings": sorted(
                {b["card"] + "|" + table["by_id"][b["card"]]["name"]
                 for b in hidden})}}

    # --- fixture (e): classification table counts (scope 5 verdict)
    fx["classification"] = {"expect": {
        "rows": len(table["by_id"]),
        "a_store": by_type.get("ongoing", 0) + by_type.get("kept_secret", 0),
        "b_public": by_type.get("side_mission", 0)
                    + by_type.get("forced_mission", 0)}}

    # --- fixture (f): store type ROUTING (deterministic asset picks, no
    #     market luck): one ongoing + one side mission + one kept secret
    #     bought directly through Saga.AgendaState.TryBuyAgenda; the harness
    #     pins the routing result (Play-Area row, public row, hidden row)
    #     exactly as the twin derived it from the printed asset.
    def first_of(typ):
        return sorted((c for c in table["by_id"].values() if c["type"] == typ),
                      key=lambda c: c["id"])[0]
    picks = [first_of("ongoing"), first_of("side_mission"),
             first_of("forced_mission"), first_of("kept_secret")]
    fx["store_routing"] = {"expect": {
        "picks": [{"card": c["id"], "name": c["name"], "type": c["type"],
                   "cost": c["cost"]} for c in picks]}}
    return fx


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "fixture"
    if mode == "fixture":
        print(json.dumps(author(), indent=1, sort_keys=False))
        return 0
    if mode.startswith("stage"):
        fx = author()
        i = int(sys.argv[2]) if len(sys.argv) > 2 else 0
        print(json.dumps(fx["market_stages"]["expect_stages"][i], indent=1))
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
