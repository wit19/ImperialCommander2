// M11e villain-option vectors (tactical AI, ab S4): villain_options.json pins
// the VillainOptions layer - the curated {A} special-action table (keyed by
// printed card id like CardKeywords), the printed-yardstick comparison against
// the regular dice attack, and BOTH caller modes:
//
//   * table ............. curated id -> option -> effect-class coverage and
//                         the UNUSED prints with their machine-readable reasons
//   * live .............. the popup law without positions (PickLive): strict
//                         EV gate + the documented reach-surplus clause; the
//                         S3 gate closes the whole layer
//   * position .......... the full law with real distance/LOS
//                         (PickForPosition, harness today / WorldProvider
//                         seam later): legality decides which side is even
//                         compared, ties keep the plain dice attack in front
//
// Expect blocks were cast with `--record`, reviewed diff by diff, then pinned
// - never hand-written (same law as keyword_rules.json / overlord_focus.json).
// The S1..S3 / reference side is proven by the gate cases (active=false) plus
// the fact that no existing canonical fixture moved when the layer shipped.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        static void SecVillainOptions()
        {
            var v = LoadJson("villain_options.json");
            var scenarios = Json.AsDict(Json.Get(v, "scenarios", new Dictionary<string, object>()));
            var cases = Json.AsList(v["cases"]);
            var recorded = new Dictionary<string, object>(StringComparer.Ordinal);
            // exact IC2 CardData/villains.json values (units_core.json stays
            // untouched); figures carry a placeholder card and the case
            // swaps the printed villain card in AFTER the world build
            // (same override law as keyword_rules.json).
            var extraCards = v.ContainsKey("cards")
                ? DeploymentCard.LoadCatalog(Json.AsDict(v["cards"]))
                : new Dictionary<string, DeploymentCard>();
            if (v.ContainsKey("boards"))
                foreach (var br in Json.AsList(v["boards"]))
                {
                    var nb = Board.FromJson(Json.AsDict(br));
                    Boards()[nb.Name] = nb;
                }

            foreach (var raw in cases)
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var kind = Json.AsStr(Json.Get(c, "kind", (object)"position"));

                Dictionary<string, object> got;
                if (kind == "table")
                    got = Norm(M11eTable()) as Dictionary<string, object>;
                else if (kind == "live")
                    got = Norm(M11eLive(c, extraCards)) as Dictionary<string, object>;
                else if (kind == "position")
                {
                    Func<Dictionary<string, object>> PickOnce = () =>
                        Norm(M11ePosition(c, scenarios, extraCards)) as Dictionary<string, object>;
                    var first = PickOnce();
                    if (first == null) { Fail(name, "figure missing from world"); continue; }
                    // replay law: rebuild the world from the same spec -
                    // the option law is a pure function, no RNG anywhere.
                    Check(name + ".replay", first, PickOnce());
                    got = first;
                }
                else throw new Exception("villain_options case " + name + ": unknown kind " + kind);

                if (Record) { recorded[name] = got; continue; }

                if (!c.ContainsKey("expect"))
                {
                    Check(name + ".expect", "present", "missing - run with --record");
                    continue;
                }
                Check(name + ".expect", Json.AsDict(c["expect"]), got);
            }
            if (Record)
            {
                var path = Path.Combine(FIX, "villain_options_record.json");
                File.WriteAllText(path, DumpJson(recorded));
                Console.WriteLine("   (record) wrote " + path + " for " + recorded.Count + " cases");
            }
        }

        /// Curated table coverage: which ids carry which class, every option
        /// with its pinned values, the unused prints, the active skill rungs.
        static Dictionary<string, object> M11eTable()
        {
            var cardsWith = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var cls in new[] { VillainOptions.AttackLike, VillainOptions.OrderLike,
                                        VillainOptions.Utility })
                cardsWith[cls] = VillainOptions.CardsWith(cls).Select(x => (object)x).ToList();
            var table = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var id in SortedIds())
                table[id] = VillainOptions.OptionsFor(id)
                    .Select(o => o.AsDict()).ToList();
            return new Dictionary<string, object>
            {
                { "cards_with", cardsWith },
                { "table", table },
                { "unused", VillainOptions.Unused
                    .Select(u => (object)u.Select(x => (object)x).ToList()).ToList() },
                { "active_levels", CardKeywords.ActiveLevels.Select(x => (object)x).ToList() },
            };
        }

        static List<string> SortedIds()
        {
            var ids = new List<string>();
            foreach (var cls in new[] { VillainOptions.AttackLike, VillainOptions.OrderLike,
                                        VillainOptions.Utility })
                foreach (var id in VillainOptions.CardsWith(cls))
                    if (!ids.Contains(id)) ids.Add(id);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// Popup mode: no world, card + (optional) public threat balance.
        static Dictionary<string, object> M11eLive(Dictionary<string, object> c,
                                                   Dictionary<string, DeploymentCard> cards)
        {
            var card = cards[Json.AsStr(c["card"])];
            string skill = c.ContainsKey("skill") && c["skill"] != null
                ? Json.AsStr(c["skill"]) : null;
            var profile = skill == null ? null : SkillProfile.For(skill);
            bool active = VillainOptions.ActiveFor(profile, card.Id);
            if (!active)
                return new Dictionary<string, object>
                {
                    { "active", false }, { "pick", null },
                };
            int? balance = c.ContainsKey("balance") && c["balance"] != null
                ? (int?)Json.AsLong(c["balance"]) : null;
            return new Dictionary<string, object>
            {
                { "active", true },
                { "pick", VillainOptions.PickLive(card, balance).AsDict() },
            };
        }

        /// Full mode: scenario world + skill profile, real distance/LOS.
        static Dictionary<string, object> M11ePosition(Dictionary<string, object> c,
                                                       Dictionary<string, object> scenarios,
                                                       Dictionary<string, DeploymentCard> cards)
        {
            var scenario = Json.AsDict(scenarios[Json.AsStr(c["scenario"])]);
            var built = BuildScenarioWorld(scenario);
            var world = built.Item2;
            string skill = c.ContainsKey("skill") && c["skill"] != null
                ? Json.AsStr(c["skill"]) : null;
            world.Skill = skill == null ? null : SkillProfile.For(skill);
            var fig = world.ById(Json.AsStr(c["fig"]));
            if (fig == null) return null;
            if (c.ContainsKey("card_override") && c["card_override"] != null)
                fig.Card = cards[Json.AsStr(c["card_override"])];
            var target = world.ById(Json.AsStr(c["target"]));
            bool active = VillainOptions.ActiveFor(world.Skill, fig.Card.Id);
            if (!active)
                return new Dictionary<string, object>
                {
                    { "active", false }, { "pick", null },
                };
            int? balance = c.ContainsKey("balance") && c["balance"] != null
                ? (int?)Json.AsLong(c["balance"]) : null;
            return new Dictionary<string, object>
            {
                { "active", true },
                { "pick", VillainOptions.PickForPosition(world, fig, target, balance).AsDict() },
            };
        }
    }
}
