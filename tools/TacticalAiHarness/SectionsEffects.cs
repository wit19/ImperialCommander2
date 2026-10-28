// M12d imperial-card-effects vectors: imperial_card_effects.json pins the
// classification table + the pipeline hooks of the Imperial class decks
// (tactical_imperial_card_effects/1 asset, primed from the SHIPPED file -
// same seam as the deck/hero-card assets, never a fixture copy).
//
// Case kinds (each proves a DIFFERENT law):
//   * integrity ..... table covers every shipped deck card exactly once,
//                    legal buckets, (c) carries no hooks, printed
//                    Attachment rows carry the lifecycle hook; ALL basics
//                    modelled (a/b) - the task's Pflicht rule.
//   * aggregate ..... owned-card aggregation law per deck: basics-only
//                    (what every campaign owns from round 1) and explicit
//                    owned lists. MAX-once for order_boost/atk_ev_bonus/
//                    deploy_cost, SUM for engage pips/pierce/health/free
//                    groups (M11h combination law: same shortage once).
//   * plan .......... OrderPlanner under the SAME seed with and without the
//                    deck's effects: every case must either FLIP the
//                    activation order (atk_ev_bonus rides the printed-EV
//                    tie key) or ANNOUNCE (order unchanged, every group
//                    carries the card_effects tag + Kartenwirkung why-line -
//                    the order_boost is a uniform multiplier, so fairness
//                    law says: visible, never silent). Baseline replays
//                    stay bit-identical to the no-effects plan (canon law).
//   * threat ........ HeroThreatModelV2 engage hook on shipped hero cards:
//                    acc pips move the hero's own exchange (his threat as a
//                    target), printed Pierce rides the existing pierce seam.
//   * rank .......... OverlordFocus card surcharge: +1 on the conditioned
//                    candidate only (M10b objective-actor slot, flips ties
//                    and <1 gaps - never outranks strain/chip/mark).
//   * cost .......... ThreatLedger.EffectiveCost + ImperialCardEffects
//                    .EffectiveVitality printed seams (floored at 0).
//   * lifecycle ..... hard trait legality of printed Attachment restriction
//                    rows (store persistence law, pinned here on the Core
//                    seam; the Saga store lifecycle is gate-checked).
//   * infos ......... M12e UI projection: one row per owned card with
//                    bucket/modelled/reason/attachable (no UI in M12d).
//
// Expect blocks were recorded once with `--record`, reviewed against the
// printed value law (asset header + ImperialCardEffects.cs), then pinned -
// never hand-written. The independent authoring twin
// (wiki_data/tools/tacai_effects_twin.py) recomputes the aggregate/threat/
// cost/law numbers from the shipped assets in Python and must agree.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        static void SecImperialCardEffects()
        {
            var v = LoadJson("imperial_card_effects.json");
            var cases = Json.AsList(v["cases"]);
            var table = ImperialCardEffects.Load();
            var decks = ImperialDecks.Load();
            var profiles = HeroProfiles.ProfilesFromDoc(
                HeroProfiles.DocOrNull() ?? throw new InvalidOperationException("asset doc not primed"));
            var recorded = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var raw in cases)
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var kind = Json.AsStr(c["kind"]);
                object got;

                switch (kind)
                {
                    case "integrity":
                    {
                        var errs = table.ValidateAgainst(decks);
                        var basics = decks.Decks.SelectMany(d => d.BasicCards)
                                                .Select(x => x.Id).ToList();
                        var unmodelled = basics
                            .Where(id => table.ById.TryGetValue(id, out var e) && !e.Modelled)
                            .OrderBy(x => x, StringComparer.Ordinal)
                            .Select(x => (object)x).ToList();
                        got = new Dictionary<string, object>
                        {
                            { "rows", (long)table.ById.Count },
                            { "validate_errors", new List<object>(errs) },
                            { "basics_total", (long)basics.Count },
                            { "basics_unmodelled", unmodelled },
                        };
                        break;
                    }
                    case "aggregate":
                    {
                        var deckId = Json.AsStr(c["deck"]);
                        List<string> owned = c.ContainsKey("owned") && c["owned"] != null
                            ? Json.AsList(c["owned"]).ConvertAll(o => Json.AsStr(o))
                            : null;   // null = basics only (setup-legal ownership)
                        var eff = ImperialCardEffects.Aggregate(decks, deckId, owned);
                        got = eff.AsDict();
                        ((Dictionary<string, object>)got)["is_neutral"] = eff.IsNeutral;
                        break;
                    }
                    case "plan":
                    {
                        var deckId = Json.AsStr(c["deck"]);
                        List<string> owned = c.ContainsKey("owned") && c["owned"] != null
                            ? Json.AsList(c["owned"]).ConvertAll(o => Json.AsStr(o))
                            : null;
                        var eff = ImperialCardEffects.Aggregate(decks, deckId, owned);
                        var catalog = Catalog();
                        var posture = Json.AsStr(c["posture"]);
                        var skillTok = Json.AsStr(Json.Get(c, "skill", (object)"S3"));
                        var skill = skillTok == "" ? null : SkillProfile.For(skillTok);
                        long seed = Json.AsLong(Json.Get(c, "seed", 7L));
                        var groups = BuildCardGroups(Json.AsList(c["groups"]));
                        // baseline FIRST, same seed: the plan WITHOUT cards is
                        // the pre-M12d canon - replayed twice (purity), then
                        // the carded plan on the identical seed.
                        var rngA = new DeterministicRandom((ulong)seed);
                        var basePlan = OrderPlanner.PlanRound(3, groups, catalog, posture,
                            skill, rngA, 1, new string[0], null, null, null);
                        var rngA2 = new DeterministicRandom((ulong)seed);
                        var basePlan2 = OrderPlanner.PlanRound(3, groups, catalog, posture,
                            skill, rngA2, 1, new string[0], null, null, null);
                        Check(name + ".baseline_replay",
                              basePlan.AsDict(), basePlan2.AsDict());
                        var rngB = new DeterministicRandom((ulong)seed);
                        var cardPlan = OrderPlanner.PlanRound(3, groups, catalog, posture,
                            skill, rngB, 1, new string[0], null, null, eff);
                        OrderPlanner.AssertPermutation(cardPlan,
                            groups.Select(g => g.GroupId));
                        var names = groups.ToDictionary(g => g.GroupId, g => g.Name);
                        bool sameOrder = basePlan.Slots.Select(s => s.GroupId)
                            .SequenceEqual(cardPlan.Slots.Select(s => s.GroupId));
                        bool announced = cardPlan.Slots.All(s =>
                            s.Tags.Contains("card_effects")
                            && s.Why.Contains("Kartenwirkung"));
                        var flipped = new List<object>();
                        if (!sameOrder)
                            foreach (var s in cardPlan.Slots)
                            {
                                int old = basePlan.Slots.FindIndex(x => x.GroupId == s.GroupId);
                                if (old != s.Rank) flipped.Add(s.GroupId);
                            }
                        got = new Dictionary<string, object>
                        {
                            { "order_base", new List<object>(basePlan.Slots
                                .Select(s => (object)s.GroupId)) },
                            { "order_cards", new List<object>(cardPlan.Slots
                                .Select(s => (object)s.GroupId)) },
                            { "same_order", sameOrder },
                            { "flipped", flipped },
                            { "announced", eff.IsNeutral ? false : announced },
                            { "reasons", new List<object>(cardPlan.Slots
                                .Select(s => (object)(s.GroupId + "|"
                                    + string.Join(",", s.Tags)
                                    + "|" + s.Why))) },
                            { "public", cardPlan.PublicLine(names) },
                        };
                        break;
                    }
                    case "threat":
                    {
                        var deckId = Json.AsStr(c["deck"]);
                        List<string> owned = c.ContainsKey("owned") && c["owned"] != null
                            ? Json.AsList(c["owned"]).ConvertAll(o => Json.AsStr(o))
                            : null;
                        var eff = ImperialCardEffects.Aggregate(decks, deckId, owned);
                        var engage = ImperialCardEffects.EngagementOf(eff);
                        var hero = HeroCards.For(Json.AsStr(c["hero"]));
                        HeroThreatProfile prof = null;
                        var profKey = Json.AsStr(Json.Get(c, "profile", (object)""));
                        if (profKey.Length > 0) profiles.TryGetValue(profKey, out prof);
                        var posture = Json.AsStr(c["posture"]);
                        var defense = Json.Get(c, "defense", null) == null ? null
                            : Json.AsList(c["defense"]).ConvertAll(o => Json.AsStr(o));
                        bool wounded = Json.AsBool(Json.Get(c, "wounded", (object)false));
                        var t0 = HeroThreatModelV2.Threat(hero, HeroCards.Load(), prof,
                                                          wounded, posture, defense, null);
                        var t1 = HeroThreatModelV2.Threat(hero, HeroCards.Load(), prof,
                                                          wounded, posture, defense, engage);
                        got = new Dictionary<string, object>
                        {
                            { "engage", engage == null ? null : engage.AsDict() },
                            { "threat_base", t0.HasValue ? t0.Value.ToString() : "null" },
                            { "threat_cards", t1.HasValue ? t1.Value.ToString() : "null" },
                        };
                        break;
                    }
                    case "rank":
                    {
                        var posture = Json.AsStr(c["posture"]);
                        int surcharge = Json.AsInt(Json.Get(c, "surcharge", 0L));
                        var defense = Json.Get(c, "defense", null) == null ? null
                            : Json.AsList(c["defense"]).ConvertAll(o => Json.AsStr(o));
                        List<FocusCandidate> Build()
                        {
                            var list = new List<FocusCandidate>();
                            foreach (var rawH in Json.AsList(c["heroes"]))
                            {
                                var h = Json.AsDict(rawH);
                                HeroThreatProfile p = null;
                                var pk = Json.AsStr(Json.Get(h, "profile", (object)""));
                                if (pk.Length > 0) profiles.TryGetValue(pk, out p);
                                list.Add(new FocusCandidate
                                {
                                    HeroId = Json.AsStr(h["hero"]),
                                    SquadRow = Json.AsInt(h["row"]),
                                    Profile = p,
                                    Wounded = Json.AsBool(Json.Get(h, "wounded", (object)false)),
                                    Marks = (int)Json.AsLong(Json.Get(h, "marks", 0L)),
                                    MarkedRound = (int)Json.AsLong(Json.Get(h, "marked_round", 0L)),
                                    AttackDice = Json.AsInt(Json.Get(h, "attack_dice", 0L)),
                                    Health = Json.AsInt(Json.Get(h, "health", 0L)),
                                    Card = HeroCards.For(Json.AsStr(h["hero"])),
                                    Conditioned = Json.AsBool(Json.Get(h, "conditioned", (object)false)),
                                });
                            }
                            return list;
                        }
                        var d1 = OverlordFocus.Rank(Build(), posture, null, defense, 0);
                        var d2 = OverlordFocus.Rank(Build(), posture, null, defense, surcharge);
                        Check(name + ".replay",
                              d2.Select(x => (object)x.AsDict()).ToList(),
                              OverlordFocus.Rank(Build(), posture, null, defense, surcharge)
                                  .Select(x => (object)x.AsDict()).ToList());
                        got = new Dictionary<string, object>
                        {
                            { "order_base", new List<object>(d1
                                .Select(x => (object)x.HeroId)) },
                            { "order_cards", new List<object>(d2
                                .Select(x => (object)x.HeroId)) },
                            { "rows", new List<object>(d2.Select(x => (object)x.AsDict())) },
                        };
                        break;
                    }
                    case "cost":
                    {
                        var deckId = Json.AsStr(c["deck"]);
                        List<string> owned = c.ContainsKey("owned") && c["owned"] != null
                            ? Json.AsList(c["owned"]).ConvertAll(o => Json.AsStr(o))
                            : null;
                        var eff = ImperialCardEffects.Aggregate(decks, deckId, owned);
                        var costs = Json.AsList(Json.Get(c, "costs", new List<object>()))
                            .ConvertAll(o => Json.AsLong(o));
                        var effCosts = new List<object>();
                        foreach (var cst in costs)
                            effCosts.Add((long)ThreatLedger.EffectiveCost((int)cst, eff));
                        var vit = Rational.Parse(Json.AsStr(Json.Get(c, "vitality", (object)"1")));
                        got = new Dictionary<string, object>
                        {
                            { "effective_costs", effCosts },
                            { "vitality_effective",
                              ImperialCardEffects.EffectiveVitality(vit, eff.HealthBonus).ToString() },
                        };
                        break;
                    }
                    case "lifecycle":
                    {
                        var deckId = Json.AsStr(c["deck"]);
                        var cardId = Json.AsStr(c["card"]);
                        var deck = decks.Find(deckId);
                        string restriction = null;
                        foreach (var cc in deck.Cards)
                            if (cc.Id == cardId) restriction = cc.Restriction;
                        var rows = new List<object>();
                        foreach (var tr in Json.AsList(c["traits"]))
                        {
                            var t = Json.AsStr(tr);
                            rows.Add((object)ImperialCardEffects.AttachLegal(restriction, t));
                        }
                        got = new Dictionary<string, object>
                        {
                            { "restriction", restriction ?? "" },
                            { "legal", rows },
                        };
                        break;
                    }
                    case "infos":
                    {
                        var deckId = Json.AsStr(c["deck"]);
                        List<string> owned = c.ContainsKey("owned") && c["owned"] != null
                            ? Json.AsList(c["owned"]).ConvertAll(o => Json.AsStr(o))
                            : null;
                        var groupTraits = Json.Get(c, "group_traits", null) == null
                            ? null : Json.AsStr(c["group_traits"]);
                        got = new List<object>(ImperialCardEffects.ActiveCardInfos(
                            decks, deckId, owned, groupTraits));
                        break;
                    }
                    default:
                        Fail(name, "unknown case kind: " + kind);
                        continue;
                }

                if (Record) { recorded[name] = got; continue; }
                if (!c.ContainsKey("expect"))
                {
                    Check(name + ".expect", "present", "missing - run with --record");
                    continue;
                }
                Check(name + ".expect", c["expect"], got);
            }
            if (Record)
            {
                var path = Path.Combine(FIX, "imperial_card_effects_record.json");
                File.WriteAllText(path, DumpJson(recorded));
                Console.WriteLine("   (record) wrote " + path + " for " + recorded.Count + " cases");
            }
        }

        static List<GroupFacts> BuildCardGroups(List<object> rows)
        {
            var list = new List<GroupFacts>();
            int i = 0;
            foreach (var raw in rows)
            {
                var g = Json.AsDict(raw);
                list.Add(new GroupFacts(
                    Json.AsStr(g["group"]), i++,
                    Json.AsStr(g["card"]), Json.AsStr(g["name"]),
                    Json.AsStr(g["pattern"]), Json.AsInt(g["alive"]),
                    Rational.Parse(Json.AsStr(Json.Get(g, "vitality", (object)"1"))),
                    Json.Get(g, "zone", null) == null ? null : Json.AsStr(g["zone"]),
                    Json.Get(g, "arrived", null) == null ? (int?)null
                        : (int?)Json.AsInt(g["arrived"]),
                    Json.AsBool(Json.Get(g, "heavy", (object)false)),
                    Json.AsBool(Json.Get(g, "swarm", (object)false))));
            }
            return list;
        }
    }
}
