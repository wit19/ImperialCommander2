// M11c2 keyword-rule vectors (tactical AI, ab S4): keyword_rules.json pins
// the CardKeywords layer on the Profil-A pattern planner. The card IDs are
// the join key (DGxxx, language-independent); the worlds are the M2a
// scenario builder with the ACTIVE SKILL PROFILE attached to the world -
// the gate itself (S1..S3 + reference never read the table) is the other
// half of what these vectors prove:
//
//   * table_coverage ...... the curated rule -> printed-card-id mapping
//   * tripod S4/S5 ........ E-Web figures with a real move+attack option
//                           NEVER combine move and attack in one activation
//   * tripod S1..S3 + ref.. the SAME worlds cast while the gate was
//                           INACTIVE (recorded before the rule shipped):
//                           they must still PASS unchanged after the change,
//                           which is the bit-identity proof for the
//                           reference/S1..S3 ("legacy behaviour") side
//   * assault schema cap .. an Assault (Sturm) figure keeps attack_actions
//                           <= 1: the M11-retired ORD card schema / concept-2
//                           fairness law (OrderDeck.ValidateCard: max ONE
//                           attack block per card) could never represent the
//                           printed second attack, so MultiAttack/FocusFire
//                           are carried in the table but NOT enforced
//                           (documented M11c2 schema limit, unchanged by the
//                           deck removal - ThreatEconomy has no attack-count
//                           lever and enforcement stays out of scope).
//
// Like world_feed.json / overlord_focus.json this is a C#-side canonical
// vector file (the sim has no keyword layer); expect blocks were recorded
// with `--record`, reviewed diff by diff, then pinned - never hand-written.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        static void SecKeywordRules()
        {
            var v = LoadJson("keyword_rules.json");
            var scenarios = Json.AsDict(v["scenarios"]);
            var cases = Json.AsList(v["cases"]);
            var recorded = new Dictionary<string, object>(StringComparer.Ordinal);
            // extra exact card values (units_core.json stays untouched -
            // board.json pins its allows-lists); applied as a per-case
            // override AFTER the shared world build, keyed by figure id.
            var extraCards = v.ContainsKey("cards")
                ? DeploymentCard.LoadCatalog(Json.AsDict(v["cards"]))
                : new Dictionary<string, DeploymentCard>();
            // inline helper boards (keyword_wall): registered into the board
            // cache under their own names - canonical boards stay untouched
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
                var kind = Json.AsStr(Json.Get(c, "kind", (object)"plan"));

                Dictionary<string, object> got;
                if (kind == "table")
                {
                    // pure lookup table (no world, no RNG) - sorted ids
                    var ids = new Func<string, object>(rule =>
                        CardKeywords.CardsWith(rule).Select(x => (object)x).ToList());
                    got = new Dictionary<string, object>
                    {
                        { "no_move_and_attack", ids(CardKeywords.NoMoveAndAttack) },
                        { "multi_attack", ids(CardKeywords.MultiAttack) },
                        { "focus_fire", ids(CardKeywords.FocusFire) },
                        { "active_levels",
                          CardKeywords.ActiveLevels.Select(x => (object)x).ToList() },
                    };
                }
                else
                {
                    // Fresh world per plan call: RankChoice consumes the skill
                    // rng stream, so the replay law runs on an identically
                    // (re)built world - same world in, same plan out. The
                    // skill rng seam is C#-side only (make_skill_rng's
                    // MT19937 lives in the sim exporter, not in this layer).
                    Func<(Dictionary<string, object> norm, bool moveAndAttack, long attacks)>
                        PlanOnce = () =>
                        {
                            var scenario = Json.AsDict(scenarios[Json.AsStr(c["scenario"])]);
                            var built = BuildScenarioWorld(scenario);
                            var board = built.Item1;
                            var world = built.Item2;
                            // same victory-model wiring as SecActionPlan: the
                            // W-table posture/roles decide the focus, exactly
                            // like in the canonical action_plan vectors
                            var vm = MissionVictoryModel.FromBoard(board);
                            if (scenario.ContainsKey("victoryCounter"))
                                vm.Counter = new VictoryCounter(Json.AsStr(scenario["victoryCounter"]));
                            int roundNo = (int)Json.AsLong(Json.Get(scenario, "startRound", (long)1));
                            world.RoundNo = roundNo - 1;
                            vm.Evaluate(roundNo, world);
                            world.Victory = vm;
                            string skill = c.ContainsKey("skill") && c["skill"] != null
                                ? Json.AsStr(c["skill"]) : null;
                            world.Skill = skill == null ? null : SkillProfile.For(skill);
                            if (world.Skill != null)
                                world.SkillRng = new DeterministicRandom(Fnv1a(name));
                            var fig = world.ById(Json.AsStr(c["fig"]));
                            if (fig == null) return default;
                            if (c.ContainsKey("card_override") && c["card_override"] != null)
                                fig.Card = extraCards[Json.AsStr(c["card_override"])];
                            var plan = ActionBuilder.PlanActivation(world, fig);
                            bool hasMove = false, hasAttack = false;
                            foreach (var a in plan.Actions)
                            {
                                if (a.Type == "move") hasMove = true;
                                if (a.Type == "attack") hasAttack = true;
                            }
                            return (Norm(plan.AsDict()) as Dictionary<string, object>,
                                    hasMove && hasAttack, (long)plan.AttackActions);
                        };

                    var first = PlanOnce();
                    if (first.norm == null) { Fail(name, "figure missing from world"); continue; }
                    var again = PlanOnce();
                    Check(name + ".replay", first.norm, again.norm);

                    got = new Dictionary<string, object>
                    {
                        { "plan", first.norm },
                        { "move_and_attack", (object)first.moveAndAttack },
                        { "attack_actions", first.attacks },
                    };
                }

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
                var path = Path.Combine(FIX, "keyword_rules_record.json");
                File.WriteAllText(path, DumpJson(recorded));
                Console.WriteLine("   (record) wrote " + path + " for " + recorded.Count + " cases");
            }
        }

        /// Stable string -> ulong (FNV-1a) - case-seed for the skill rng seam;
        /// deliberately NOT GetHashCode (runtime-order dependent).
        static ulong Fnv1a(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (var ch in s) { h ^= ch; h *= 1099511628211UL; }
            return h;
        }
    }
}
