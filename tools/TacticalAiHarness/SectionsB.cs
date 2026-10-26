// Scenario-world + posture + AppView sections, incl. the action-plan vectors.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        // ------------------------------------------------------------------ focus
        static void SecFocus()
        {
            var v = LoadJsonList("focus.json");
            foreach (var eO in v.Select(Json.AsDict).Enumerate("case"))
            {
                var e = eO.Item2; int i = eO.Item1;
                var scenario = Json.AsDict(Scenarios()[Json.AsStr(e["scenario"])]);
                var (board, world) = BuildScenarioWorld(scenario);

                var imps = world.Figures.Where(f => f.Team == "imperial").ToList();
                var rebs = world.Figures.Where(f => f.Team == "rebel").ToList();

                var pure = Json.AsList(e["pure"]);
                for (int k = 0; k < imps.Count; k++)
                    Check("focus[" + i + "].pure[" + k + "]", pure[k],
                          FocusResolver.ComputeFocus(world, imps[k]).AsDict());

                var vm = MissionVictoryModel.FromBoard(board);
                if (scenario.ContainsKey("victoryCounter"))
                    vm.Counter = new VictoryCounter(Json.AsStr(scenario["victoryCounter"]));
                vm.Evaluate(1, world);
                world.Victory = vm;

                var post = Json.AsList(e["posture"]);
                for (int k = 0; k < imps.Count; k++)
                    Check("focus[" + i + "].posture[" + k + "]", post[k],
                          FocusResolver.ComputeFocus(world, imps[k]).AsDict());
                world.Victory = null;

                int j = 0;
                var all = imps.Concat(rebs).ToList();
                foreach (var a in all)
                    foreach (var r in all)
                    {
                        if (ReferenceEquals(a, r)) continue;
                        var exp = Json.AsDict(Json.AsList(e["metric"])[j++]);
                        Check("focus[" + i + "].metric[" + (j - 1) + "].a", exp["a"], a.Id);
                        Check("focus[" + i + "].metric[" + (j - 1) + "].b", exp["b"], r.Id);
                        Check("focus[" + i + "].metric[" + (j - 1) + "].count", exp["count"],
                              FocusResolver.CountSpaces(board, SpaceLegality.FigureCells(a),
                                                        SpaceLegality.FigureCells(r)));
                    }

                j = 0;
                foreach (var a in imps)
                    foreach (var r in rebs)
                        foreach (var wl in new[] { false, true })
                        {
                            var exp = Json.AsDict(Json.AsList(e["attack_geom"])[j++]);
                            Check("focus[" + i + "].ag[" + (j - 1) + "]", exp,
                                  new Dictionary<string, object>
                                  {
                                      { "a", a.Id }, { "r", r.Id }, { "with_los", wl },
                                      { "ok", FocusResolver.AttackGeometryOk(world, a,
                                            new HashSet<Cell>(SpaceLegality.FigureCells(r)),
                                            new HashSet<Cell>(SpaceLegality.FigureCells(a)),
                                            wl, new List<Cell>()) },
                                  });
                        }
            }
        }

        // ---------------------------------------------------------------- victory
        static void SecVictory()
        {
            var v = LoadJsonList("victory.json");
            foreach (var eO in v.Select(Json.AsDict).Enumerate("case"))
            {
                var e = eO.Item2; int i = eO.Item1;
                var scenario = Json.AsDict(Scenarios()[Json.AsStr(e["scenario"])]);
                var (board, world) = BuildScenarioWorld(scenario);
                var vm = MissionVictoryModel.FromBoard(board);
                if (scenario.ContainsKey("victoryCounter"))
                    vm.Counter = new VictoryCounter(Json.AsStr(scenario["victoryCounter"]));

                Check("victory[" + i + "].public", e["public"], vm.PublicFields());
                Check("victory[" + i + "].class", e["class"], vm.DeriveMissionClass());

                var events = vm.Evaluate(1, world);
                Check("victory[" + i + "].events", e["events"],
                      events.Select(s => (object)s.AsDict()).ToList());

                var imps = world.Figures.Where(f => f.Team == "imperial").ToList();
                var rebs = world.Figures.Where(f => f.Team == "rebel").ToList();
                var r1 = Json.AsDict(Json.AsList(e["rounds"])[0]);

                var wGot = new List<object>();
                var nodeGot = new List<object>();
                foreach (var f in imps)
                {
                    foreach (var r in rebs)
                        wGot.Add(new Dictionary<string, object>
                        {
                            { "att", f.Id }, { "tgt", r.Id },
                            { "w", vm.PostureValue(world, f, r).Select(WVal).ToList() },
                        });
                    var node = vm.ContestedNode(world, f, null);
                    nodeGot.Add(new Dictionary<string, object>
                    {
                        { "fig", f.Id },
                        { "node", node == null ? null : Norm(node.Value) },
                    });
                }
                foreach (var r in rebs)
                {
                    var node = vm.ContestedNode(world, null, r);
                    nodeGot.Add(new Dictionary<string, object>
                    {
                        { "tgt", r.Id },
                        { "node", node == null ? null : Norm(node.Value) },
                    });
                    var imm = vm.TargetIsImmediate(world, r);
                    Check("victory[" + i + "].imm[" + r.Id + "]",
                          Json.AsList(r1["immediate"]).Any(o => Json.AsDict(o)["tgt"] as string == r.Id
                              && Json.AsBool(Json.AsDict(o)["imm"]) == imm), true);
                }
                Check("victory[" + i + "].w", r1["w"], wGot);
                Check("victory[" + i + "].node", r1["node"], nodeGot);
                Check("victory[" + i + "].posture", r1["posture"], vm.Posture);

                if (vm.Counter != null)
                {
                    Check("victory[" + i + "].counter", r1["counter"], vm.Counter.PublicFields());
                    if (r1.ContainsKey("progress"))
                        Check("victory[" + i + "].progress", r1["progress"], vm.Counter.Progress(world));
                    var cv = new List<object>();
                    foreach (var f in imps)
                        foreach (var r in rebs)
                            cv.Add(new Dictionary<string, object>
                            {
                                { "att", f.Id }, { "tgt", r.Id },
                                { "v", vm.Counter.TargetValue(world, f, r).Select(WVal).ToList() },
                            });
                    if (r1.ContainsKey("cv"))
                        Check("victory[" + i + "].cv", r1["cv"], cv);
                }

                var wc = new VictoryCounter("woundAll");
                var wr = new List<object>();
                foreach (var f in imps)
                    foreach (var r in rebs)
                        wr.Add(new Dictionary<string, object>
                        {
                            { "att", f.Id }, { "tgt", r.Id },
                            { "v", wc.TargetValue(world, f, r).Select(WVal).ToList() },
                        });
                Check("victory[" + i + "].woundall", e["woundall"], wr);
            }
        }

        // posture_value/target_value items: Fraction -> "p/q", else raw value.
        // The C# core boxes exactly that (Rational vs long/double).
        static object WVal(object x) => Norm(x);

        // ----------------------------------------------------------------- belief
        static void SecBelief()
        {
            var v = LoadJsonList("belief.json");
            foreach (var eO in v.Select(Json.AsDict).Enumerate("case"))
            {
                var e = eO.Item2; int i = eO.Item1;
                var board = BoardOf(Json.AsStr(e["fixture"]));
                var zs = new ZoneMap(board);
                var dl = Belief.DoorLabels(board);
                var zl = Belief.ZoneLabels(board, zs, dl);
                var adj = Belief.ZoneDoorGraph(zs, dl);
                var belief = BeliefState.Build(board);

                Check("belief[" + i + "].zones", e["zones"], (long)zs.N);

                var zc = new Dictionary<string, object>();
                foreach (var z in zs.Cells.Keys.OrderBy(x => x))
                    zc[z.ToString(CultureInfo.InvariantCulture)] = Cells2L(zs.Cells[z]);
                Check("belief[" + i + "].zone_cells", e["zone_cells"], zc);

                var dlGot = new Dictionary<string, object>();
                foreach (var kv in dl.OrderBy(k => k.Key.Item1.X).ThenBy(k => k.Key.Item1.Y)
                                     .ThenBy(k => k.Key.Item2.X).ThenBy(k => k.Key.Item2.Y))
                    dlGot[kv.Key.Item1.X + "," + kv.Key.Item1.Y + "~" +
                          kv.Key.Item2.X + "," + kv.Key.Item2.Y] = kv.Value;
                Check("belief[" + i + "].door_labels", e["door_labels"], dlGot);

                var zlGot = new Dictionary<string, object>();
                foreach (var kv in zl.OrderBy(k => k.Key)) zlGot[kv.Key.ToString()] = kv.Value;
                Check("belief[" + i + "].zone_labels", e["zone_labels"], zlGot);

                var adjGot = new Dictionary<string, object>();
                foreach (var kv in adj.Keys.OrderBy(x => x))
                    adjGot[kv.ToString()] = adj[kv]
                        .Select(p => (object)new List<object> { p.Item1, (long)p.Item2 }).ToList();
                Check("belief[" + i + "].adj", e["adj"], adjGot);

                var priorGot = new Dictionary<string, object>();
                foreach (var z in zs.Cells.Keys.OrderBy(x => x))
                    priorGot[z.ToString()] = belief.Prior[z];
                Check("belief[" + i + "].prior", e["prior"], priorGot);

                var pathsGot = new List<object>();
                foreach (var start in zs.Cells.Keys.OrderBy(x => x))
                    foreach (var goal in zs.Cells.Keys.OrderBy(x => x))
                    {
                        var (doors, zones) = Belief.ZonePath(adj, start, goal);
                        pathsGot.Add(new Dictionary<string, object>
                        {
                            { "from", (long)start }, { "to", (long)goal },
                            { "doors", doors.Cast<object>().ToList() },
                            { "zones", zones.Select(z => (object)(long)z).ToList() },
                        });
                    }
                Check("belief[" + i + "].paths", e["paths"], pathsGot);

                // scripted event feed (mirrors vec_belief.feed_events)
                Cell doorCell = board.Doors.Count > 0
                    ? board.Doors.Keys.OrderBy(k => k.Item1.X).ThenBy(k => k.Item1.Y)
                        .ThenBy(k => k.Item2.X).ThenBy(k => k.Item2.Y).First().Item1
                    : new Cell(0, 0);
                Cell objCell = board.Objectives.Count > 0
                    ? FirstObjectiveCell(board.Objectives[0])
                    : new Cell(0, 0);
                belief.Observe("door_opened", new[] { doorCell }, 1);
                belief.Observe("objective_worked", new[] { objCell }, 1);
                belief.RoundEnd(1);
                belief.Observe("item_taken", new[] { objCell }, 2);
                belief.Observe("objective_done", new[] { objCell }, 2);
                belief.Observe("overwatch_placed", new[] { new Cell(1, 1) }, 3);
                belief.Observe("signal_seen", new[] { new Cell(2, 2) }, 3);
                belief.Observe("hero_ring", new[] { new Cell(3, 3) }, 3);
                belief.Observe("counter_ring", new[] { new Cell(1, 2) }, 3);
                belief.Observe("defeat_ring", new[] { new Cell(2, 1) }, 3);
                belief.RoundEnd(3);
                belief.RoundEnd(4);

                Check("belief[" + i + "].feed", e["feed"],
                      belief.Feed.Select(f => (object)f).ToList());

                var heatGot = new Dictionary<string, object>();
                foreach (var z in zs.Cells.Keys.OrderBy(x => x)) heatGot[z.ToString()] = belief.Heat[z];
                Check("belief[" + i + "].heat", e["heat"], heatGot);

                var scoreGot = new Dictionary<string, object>();
                foreach (var z in zs.Cells.Keys.OrderBy(x => x)) scoreGot[z.ToString()] = belief.Score(z);
                Check("belief[" + i + "].score", e["score"], scoreGot);

                Check("belief[" + i + "].ranked", e["ranked"],
                      belief.Ranked().Select(z => (object)(long)z).ToList());
                Check("belief[" + i + "].focus_zone", e["focus_zone"],
                      belief.FocusZone() is int fz ? (object)(long)fz : null);
                Check("belief[" + i + "].hot", e["hot"],
                      belief.Hot().OrderBy(x => x).Select(z => (object)(long)z).ToList());
            }
        }

        static Cell FirstObjectiveCell(ObjectiveDef o)
        {
            // python: next(iter(set_of_cells)) — board.py stores objective cells
            // as a LIST (json order); iter order = first element.
            return o.Cells[0];
        }

        // ------------------------------------------------------------------ rings
        static void SecRings()
        {
            var v = LoadJsonList("rings.json");
            var leth = new Dictionary<string, int[]>();
            foreach (var hO in Json.AsList(Json.AsDict(LoadJson("hero_lethality.json"))["heroes"]))
            {
                var h = Json.AsDict(hO);
                var band = Json.AsList(h["range_band"]);
                leth[Json.AsStr(h["name"])] = new[] { Json.AsInt(band[0]), Json.AsInt(band[1]) };
            }
            foreach (var eO in v.Select(Json.AsDict).Enumerate("case"))
            {
                var e = eO.Item2; int i = eO.Item1;
                var board = BoardOf(Json.AsStr(e["fixture"]));
                var belief = BeliefState.Build(board);
                var inf = new ActivationInference(belief, leth.Count > 0 ? leth : null);
                var dps = board.DeploymentPoints;
                var home = dps.Count > 0 && dps[0].Cells.Count > 0 ? dps[0].Cells[0] : new Cell(0, 0);

                inf.BeginRound(1);
                inf.Instruction("G1", home);
                var seq = Json.AsList(e["seq"]);

                Check("rings[" + i + "].seq[0]", seq[0], new Dictionary<string, object>
                { { "op", "hero_activated" }, { "hero", "R1" } });
                inf.HeroActivated("R1");

                var ring = inf.Damage("G1", 2);
                Check("rings[" + i + "].seq[1]", seq[1], new Dictionary<string, object>
                { { "op", "damage" }, { "ring", ring == null ? null : ring.AsDict() } });

                ring = inf.WoundFlip("R1", null, "G1");
                Check("rings[" + i + "].seq[2]", seq[2], new Dictionary<string, object>
                { { "op", "wound_flip" }, { "ring", ring == null ? null : ring.AsDict() } });

                ring = inf.OnDefeat("G2");
                Check("rings[" + i + "].seq[3]", seq[3], new Dictionary<string, object>
                { { "op", "on_defeat" }, { "ring", ring == null ? null : ring.AsDict() } });

                int fz = belief.FocusZone() ?? 0;
                var taps = inf.DamageTap(belief.Labels[fz]);
                Check("rings[" + i + "].seq[4]", seq[4], new Dictionary<string, object>
                {
                    { "op", "tap" },
                    { "rings", taps.Select(t => (object)t.AsDict()).ToList() },
                });

                Check("rings[" + i + "].active", e["active"],
                      inf.ActiveRings(2).Select(r => (object)r.AsDict()).ToList());

                var heatGot = new Dictionary<string, object>();
                foreach (var z in belief.Heat.Keys.OrderBy(x => x)) heatGot[z.ToString()] = belief.Heat[z];
                Check("rings[" + i + "].heat", e["heat"], heatGot);
            }
        }

        // --------------------------------------------------------------- doctrine
        static void SecDoctrine()
        {
            var v = LoadJsonList("doctrine.json");
            foreach (var eO in v.Select(Json.AsDict).Enumerate("case"))
            {
                var e = eO.Item2; int i = eO.Item1;
                string name = Json.AsStr(e["scenario"]);
                var scenario = Json.AsDict(Scenarios()[name]);
                var (board, world) = BuildScenarioWorld(scenario);
                var vm = MissionVictoryModel.FromBoard(board);
                if (scenario.ContainsKey("victoryCounter"))
                    vm.Counter = new VictoryCounter(Json.AsStr(scenario["victoryCounter"]));
                vm.Evaluate(1, world);
                world.Victory = vm;
                var entrances = Json.AsCells(Json.Get(scenario, "entrances", new List<object>()));
                var feed = new WorldFeed(world, entrances);

                int rounds = Math.Max(1, (int)Json.AsLong(Json.Get(scenario, "rounds", (long)1)));
                var expRounds = Json.AsList(e["rounds"]);
                for (int rnd = 1; rnd <= rounds; rnd++)
                {
                    vm.Evaluate(rnd, world);
                    feed.Advance(rnd);
                    var view = feed.DoctrineView(rnd, vm);
                    var exp = Json.AsDict(expRounds[rnd - 1]);
                    Check("doctrine[" + i + "].r" + rnd + ".round", exp["round"], (long)rnd);
                    Check("doctrine[" + i + "].r" + rnd + ".appview", exp["appview"], view.Public());
                    var expDocs = Json.AsDict(exp["doctrines"]);
                    var docs = new Dictionary<string, object>();
                    foreach (var pat in Patterns.PatternsList)
                        docs[pat] = Doctrine.SelectDoctrine(view, pat).AsDict();
                    Check("doctrine[" + i + "].r" + rnd + ".doctrines", expDocs, docs);
                    feed.RoundEnd(rnd);
                }
            }
        }

        // ------------------------------------------------------------- order_plan
        static void SecOrderPlan()
        {
            var v = LoadJson("order_plan.json");
            var catalog = Catalog();
            var pats = Patterns.PatternsList;
            int ci = 0;
            foreach (var posture in new[] { "eliminate", "stall", "protect", "deny" })
                foreach (var skill in new string[] { null, "S1", "S2", "S3", "S4", "S5" })
                    foreach (var seed in new[] { 7, 13 })
                    {
                        var exp = Json.AsDict(Json.AsList(v["cases"])[ci++]);
                        var groups = new List<GroupFacts>();
                        for (int gi = 0; gi < pats.Length; gi++)
                            groups.Add(new GroupFacts(
                                "G" + gi, gi,
                                gi % 2 == 0 ? "DG021" : "DG009",
                                "Group " + gi, pats[gi], 2,
                                gi == 0 ? Rational.One : new Rational(3, 4),
                                gi < 3 ? "Z1" : "Z2",
                                gi == 4 ? 3 : (int?)null,
                                gi == 2, gi % 2 == 1));
                        // python seeds the plan rng from "vec|1|<seed>|<skill>";
                        // both seeds yield identical plans (rng_u == None,
                        // seed-verified: plan(7) == plan(13) in the vector file)
                        var plan = OrderPlanner.PlanRound(3, groups, catalog, posture,
                            skill == null ? null : SkillProfile.For(skill),
                            new DeterministicRandom((ulong)seed), 1,
                            new[] { "Z2" }, "Z1", "woundAll");
                        OrderPlanner.AssertPermutation(plan, groups.Select(g => g.GroupId));
                        var names = groups.ToDictionary(g => g.GroupId, g => g.Name);
                        Check("order_plan.cases[" + (ci - 1) + "].plan", exp["plan"], plan.AsDict());
                        Check("order_plan.cases[" + (ci - 1) + "].public", exp["public"],
                              plan.PublicLine(names));
                    }

            foreach (var tO in Json.AsList(v["trace"]).Select(Json.AsDict).Enumerate("t"))
            {
                var t = tO.Item2; int i = tO.Item1;
                var posture = Json.AsStr(t["posture"]);
                var skill = Json.AsStr(t["skill"]);
                var groups = new List<GroupFacts>();
                for (int gi = 0; gi < 6; gi++)
                    groups.Add(new GroupFacts(
                        "G" + gi, gi,
                        gi % 2 == 0 ? "DG021" : "DG009",
                        "Group " + gi, pats[gi % pats.Length], 2,
                        new Rational(3, 4), "Z1", null, gi == 2, gi % 2 == 1));
                var rng = new ScriptedRandom(Json.AsList(t["below"]), Json.AsList(t["u_stream"]));
                var plan = OrderPlanner.PlanRound(3, groups, catalog, posture,
                    SkillProfile.For(skill), rng, 1, new string[0], null, null);
                Check("order_plan.trace[" + i + "].plan", t["plan"], plan.AsDict());
            }
        }

        // ------------------------------------------------------------ action_plan
        static void SecActionPlan()
        {
            var v = LoadJson("action_plan.json");
            var rows = Json.AsList(v["rows"]).Select(Json.AsDict).ToList();
            var byScenario = rows.GroupBy(r => Json.AsStr(r["scenario"]))
                                 .ToDictionary(g => g.Key, g => g.ToList());
            foreach (var sname in byScenario.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                var scenario = Json.AsDict(Scenarios()[sname]);
                var (board, world) = BuildScenarioWorld(scenario);
                var vm = MissionVictoryModel.FromBoard(board);
                if (scenario.ContainsKey("victoryCounter"))
                    vm.Counter = new VictoryCounter(Json.AsStr(scenario["victoryCounter"]));
                int roundNo = (int)Json.AsLong(Json.Get(scenario, "startRound", (long)1));
                world.RoundNo = roundNo - 1;
                vm.Evaluate(roundNo, world);
                world.Victory = vm;

                foreach (var exp in byScenario[sname])
                {
                    string p = "action_plan[" + sname + "/" + Json.AsStr(exp["fig"]) + "]";
                    var fig = world.ById(Json.AsStr(exp["fig"]));
                    if (fig == null) { Fail(p, "figure missing from world"); continue; }
                    Check(p + ".posture", exp["posture"], vm.Posture);

                    var basePlan = ActionBuilder.PlanActivation(world, fig);
                    var plan = ActionBuilder.PosturePlan(world, fig, basePlan);
                    Check(p + ".overlaid", exp["overlaid"], !ReferenceEquals(plan, basePlan));

                    Check(p + ".base_plan", exp["base_plan"], basePlan.AsDict());
                    Check(p + ".plan", exp["plan"], plan.AsDict());
                    Check(p + ".base_violations", exp["base_violations"],
                          ActionBuilder.ValidatePlan(fig, basePlan));
                    Check(p + ".violations", exp["violations"],
                          ActionBuilder.ValidatePlan(fig, plan));
                    Check(p + ".posture_violations", exp["posture_violations"],
                          ActionBuilder.ValidatePosturePlan(world, fig, basePlan, plan));

                    Check(p + ".base_hash", exp["base_hash"], Sha256Hex(Canonical(basePlan.AsDict())));
                    Check(p + ".plan_hash", exp["plan_hash"], Sha256Hex(Canonical(plan.AsDict())));

                    // per-action fields explicitly (in addition to whole-plan dict)
                    var expActs = Json.AsList(Json.AsDict(exp["plan"])["actions"]);
                    var acts = plan.Actions;
                    if (expActs.Count != acts.Count)
                        Fail(p + ".actions", "count " + expActs.Count + " vs " + acts.Count);
                    else
                        for (int ai = 0; ai < acts.Count; ai++)
                        {
                            var ea = Json.AsDict(expActs[ai]);
                            Check(p + ".actions[" + ai + "].type", ea["type"], acts[ai].Type);
                            Check(p + ".actions[" + ai + "].why", ea["why"], acts[ai].Why);
                            Check(p + ".actions[" + ai + "].params", ea["params"],
                                  Norm(acts[ai].AsDict()["params"]));
                        }
                }
            }
        }
    }
}
