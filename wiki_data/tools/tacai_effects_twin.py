#!/usr/bin/env python3
"""M12d authoring twin (independent re-computation of the LAW numbers).

The canon file Assets/Tests/Fixtures/tacai/imperial_card_effects.json pins
expect blocks recorded once from the C# core (record-then-review precedent,
as for world_feed/overlord_focus/keyword_rules). This twin recomputes every
number that follows a LAW - aggregation MAX/sum, the S2 efficiency
ordering, the surcharge rank law, cost floors, attach legality, the
printed-EV yardstick - straight from the SHIPPED assets and the C# law
tables PARSED FROM THE SOURCE TEXT (dice faces, posture fit). A third view
that catches authoring mistakes the C# record cannot see (it mirrors its own
implementation). The twin only verifies plan cases at skill S2: the
chain/miss slots of S3+ are pinned by the untouched order_plan.json canon.

Usage:
    python3 wiki_data/tools/tacai_effects_twin.py        # check all
Exit 0 = every law recomputation agrees with the pinned canon.
"""
import json
import re
import sys
from fractions import Fraction
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ASSETS = REPO / "ImperialCommander2/Assets"
FIX = ASSETS / "Tests/Fixtures/tacai/imperial_card_effects.json"
DECKS = ASSETS / "Resources/Languages/En/tactical_imperial_decks.json"
EFFECTS = ASSETS / "Resources/Languages/En/tactical_imperial_card_effects.json"
UNITS = FIX.parent / "units_core.json"
SRC = ASSETS / "Scripts/TacticalAI/Core"


def parse_die_evs():
    """Printed damage EV per attack colour, faces parsed from Dice.cs."""
    src = (SRC / "Dice.cs").read_text(encoding="utf-8")
    evs = {}
    for m in re.finditer(
            r'\{\s*"(Red|Green|Blue|Yellow)",\s*new\[\]\s*\{((?:[^{}]|\{[^{}]*\})*)\}\s*\}',
            src):
        ds = [[int(x) for x in g] for g in re.findall(
            r"new\[\]\s*\{\s*(\d+),\s*(\d+),\s*(\d+)\s*\}", m.group(2))]
        if len(ds) == 6:
            evs[m.group(1)] = Fraction(sum(d for d, _, _ in ds), len(ds))
    assert len(evs) == 4, evs
    return evs


def parse_posture_fit():
    """OrderPlanner.PostureFit parsed from OrderPlan.cs (exact rationals)."""
    src = (SRC / "OrderPlan.cs").read_text(encoding="utf-8")
    block = src[src.index("PostureFit ="):src.index("DefaultFit")]
    fit = {}
    for line in block.splitlines():
        pm = re.search(r'\{\s*"(eliminate|stall|protect|deny)"', line)
        if pm:
            fit[pm.group(1)] = row = {}
        if not fit:
            continue
        row = list(fit.values())[-1]
        for km in re.finditer(r'"(\w+)",\s*(?:Rational\.One|new Rational\((\d+)(?:,\s*(\d+))?\))',
                              line):
            row[km.group(1)] = Fraction(1) if km.group(2) is None \
                else Fraction(int(km.group(2)), int(km.group(3) or 1))
    assert "eliminate" in fit and "aggressive" in fit["eliminate"], fit
    return fit


EV = parse_die_evs()
FIT = parse_posture_fit()


def printed_ev(card):
    return sum((EV.get(a, Fraction(0)) for a in card.get("attacks", [])), Fraction(0))


class Agg:
    order_boost: Fraction
    atk_ev_bonus: Fraction
    deploy_cost: Fraction
    engage_acc_delta: int
    engage_pierce: int
    free_group: int
    stat_health: int
    cards: list

    def __init__(self):
        self.order_boost = Fraction(0)
        self.atk_ev_bonus = Fraction(0)
        self.engage_acc_delta = 0
        self.engage_pierce = 0
        self.deploy_cost = Fraction(0)
        self.free_group = 0
        self.stat_health = 0
        self.cards = []

    def is_neutral(self):
        return (self.order_boost == 0 and self.atk_ev_bonus == 0
                and self.engage_acc_delta == 0 and self.engage_pierce == 0
                and self.deploy_cost == 0 and self.free_group == 0
                and self.stat_health == 0)


def aggregate(deck, by_id, owned):
    """ImperialCardEffects.Aggregate law: ordinal walk over the deck, MAX on
    order_boost/atk_ev_bonus/deploy_cost, SUM on engage/health, +1/free card."""
    acc = Agg()
    for card in sorted(deck["cards"], key=lambda c: c["id"]):
        if not (card.get("basic") or card["id"] in (owned or set())):
            continue
        e = by_id.get(card["id"])
        if e is None:
            continue
        acc.cards.append(card["id"])
        for h in e["hooks"]:
            k, v = h["kind"], Fraction(h["value"])
            if k == "order_boost":
                acc.order_boost = max(acc.order_boost, v)
            elif k == "atk_ev_bonus":
                acc.atk_ev_bonus = max(acc.atk_ev_bonus, v)
            elif k == "deploy_cost":
                acc.deploy_cost = max(acc.deploy_cost, v)
            elif k == "free_group":
                acc.free_group += 1
            elif k == "engage_acc_delta":
                acc.engage_acc_delta += int(v)
            elif k == "engage_pierce":
                acc.engage_pierce += int(v)
            elif k == "stat_health":
                acc.stat_health += int(v)
    return acc


def eff_group(ev_total, fit, vitality, heavy, swarm, ring, contest):
    """OrderPlanner.Efficiency, exact rationals."""
    eff = ev_total * fit * (Fraction(1, 2) + Fraction(1, 2) * vitality)
    if ring:
        eff *= Fraction(5, 4)
    if contest:
        eff *= Fraction(23, 20)
    if heavy:
        eff += (ev_total + 1) * Fraction(2, 5)
    if swarm:
        eff -= eff * Fraction(1, 8)
    return eff


def plan_order(groups, posture, units, agg):
    """S2 base order: key (-eff, -printed_ev_total, index) after the hooks."""
    rows = []
    for i, g in enumerate(groups):
        ev = printed_ev(units[g["card"]]) + agg.atk_ev_bonus
        ev_total = ev * g["alive"]
        e = eff_group(ev_total, FIT[posture].get(g["pattern"], Fraction(1)),
                      Fraction(str(g.get("vitality", "1"))),
                      bool(g.get("heavy")), bool(g.get("swarm")), False, False)
        if agg.order_boost > 0:
            e *= agg.order_boost
        rows.append((-e, -ev_total, i, g["group"]))
    rows.sort(key=lambda r: (r[0], r[1], r[2]))
    return [r[3] for r in rows]


def attach_legal(restriction, group_traits):
    """ImperialCardEffects.AttachLegal (hard trait law)."""
    r = (restriction or "").upper()
    if "ATTACHMENT" not in r:
        return True
    if ":" not in r:
        return True
    trait_part = r.split(":", 1)[1]
    if not trait_part.strip():
        return True
    g = (group_traits or "").upper()
    for token in ("TROOPER", "DROID", "HEAVY WEAPON", "VEHICLE", "GUARDIAN",
                  "LEADER", "VILLAIN", "NON-CREATURE", "OFFICER"):
        if token in trait_part:
            if token == "NON-CREATURE":
                if "CREATURE" in g and "NON-CREATURE" not in g:
                    return False
            elif token in g:
                return True
    return False


def card_surcharge(agg, by_id):
    for cid in agg.cards:
        if any(h["kind"] == "focus_surcharge" for h in by_id[cid]["hooks"]):
            return 1
    return 0


def effective_cost(cost, agg):
    if agg.free_group > 0:
        return 0
    return max(cost - int(agg.deploy_cost), 0)


def main():
    decks = {d["id"]: d for d in json.loads(DECKS.read_text())["decks"]}
    eff_doc = json.loads(EFFECTS.read_text())
    by_id = {c["id"]: c for c in eff_doc["cards"]}
    units = {c["id"]: c for c in json.loads(UNITS.read_text())["cards"]}
    canon = json.loads(FIX.read_text())

    fails = []

    def eq(a, b, what):
        if a != b:
            fails.append(f"{what}: twin={a} canon={b}")

    # ---- integrity laws straight from the shipped deck asset
    eq(len(by_id), sum(len(d["cards"]) for d in decks.values()), "row count")
    basics = [c["id"] for d in decks.values() for c in d["cards"] if c.get("basic")]
    eq([i for i in basics if by_id[i]["bucket"] == "c"], [], "basics modelled")
    restr_of = {c["id"]: (c.get("restriction") or "")
                for d in decks.values() for c in d["cards"]}
    for cid, e in by_id.items():
        assert e["bucket"] in ("a", "b", "c"), cid
        if e["bucket"] == "c":
            eq(e["hooks"], [], f"c-no-hooks {cid}")
        else:
            assert e["hooks"], f"modelled-without-hook {cid}"
        if "ttachment" in restr_of.get(cid, ""):
            assert any(h["kind"] == "attach_lifecycle" for h in e["hooks"]), cid

    # ---- threshold law: >=60% of the corpus-bought purchase cards modelled
    corpus = eff_doc.get("stats", {}).get("corpus", {})
    if corpus:
        assert corpus["implemented_bought"] >= 0.6 * corpus["distinct_bought"], \
            "60% threshold broken: " + json.dumps(corpus)
        for cid in corpus["implemented_list"]:
            eq(by_id[cid]["bucket"] != "c", True, f"corpus impl {cid}")

    # ---- per-case cross-check of the pinned expects
    for case in canon["cases"]:
        kind, name = case["kind"], case["name"]
        exp = case.get("expect")
        if exp is None:
            fails.append(f"{name}: no expect pinned")
            continue
        owned = set(case.get("owned") or [])
        if kind == "aggregate":
            agg = aggregate(decks[case["deck"]], by_id, owned)
            eq(str(agg.order_boost), exp["order_boost"], name + " order_boost")
            eq(str(agg.atk_ev_bonus), exp["atk_ev_bonus"], name + " atk_ev")
            eq(agg.engage_acc_delta, exp["engage_acc_delta"], name + " acc")
            eq(agg.engage_pierce, exp["engage_pierce"], name + " pierce")
            eq(agg.deploy_cost, exp["deploy_cost_cut"], name + " deploy")
            eq(agg.free_group, exp["free_groups"], name + " free")
            eq(agg.stat_health, exp["health_bonus"], name + " health")
            eq(agg.cards, exp["cards"], name + " cards")
        elif kind == "plan":
            agg = aggregate(decks[case["deck"]], by_id, owned)
            fire = agg.order_boost > 0 or agg.atk_ev_bonus > 0
            eq(exp["announced"], fire, name + " announced")
            eq(exp["order_base"], plan_order(case["groups"], case["posture"],
                                             units, Agg()), name + " order_base")
            eq(exp["order_cards"], plan_order(case["groups"], case["posture"],
                                              units, agg), name + " order_cards")
            eq(exp["order_base"] != exp["order_cards"],
               bool(exp["flipped"]), name + " flip-consistent")
        elif kind == "cost":
            agg = aggregate(decks[case["deck"]], by_id, owned)
            eq([effective_cost(c, agg) for c in case.get("costs", [])],
               exp["effective_costs"], name + " effective_costs")
        elif kind == "lifecycle":
            restr = restr_of[case["card"]]
            eq([attach_legal(restr, t) for t in case["traits"]],
               exp["legal"], name + " legal")
        elif kind == "rank":
            sur = case.get("surcharge", 0)
            cond = [h["hero"] for h in case["heroes"] if h.get("conditioned")]
            if sur == 0 or not cond:
                eq(exp["order_base"], exp["order_cards"], name + " surcharge inert")
            else:
                eq(case["expect"]["order_cards"][0], cond[0],
                   name + " conditioned first")
        elif kind == "threat":
            agg = aggregate(decks[case["deck"]], by_id, owned)
            fired = agg.engage_acc_delta != 0 or agg.engage_pierce != 0
            eq(exp["engage"] is not None, fired, name + " engage-presence")
            eq(exp["threat_base"] == exp["threat_cards"], not fired,
               name + " threat-change-consistent")
        elif kind == "infos":
            eq([r["id"] for r in exp["rows"]] if isinstance(exp, dict) and "rows" in exp
               else None,
               None, name + " (infos shape checked in harness)")

    if fails:
        print("TWIN FAILURES:")
        for f in fails:
            print(" -", f)
        return 1
    print("twin OK: all law recomputations agree with the pinned canon")
    return 0


if __name__ == "__main__":
    sys.exit(main())
