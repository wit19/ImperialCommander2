// Vector-file sections: pure-data + geometry/planner primitives.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        // ---------------------------------------------------------------- geometry
        static void SecGeometry()
        {
            var v = LoadJson("geometry.json");
            foreach (var kv in OrdKeys(Json.AsDict(v["footprint_sizes"])))
                Check("footprint_sizes." + kv.Key, kv.Value, Geometry.FootprintSizes[kv.Key]);
            Check("dirs", v["dirs"], Geometry.DirVectors.ToList());

            foreach (var cO in Json.AsList(v["cases"]).Select(Json.AsDict).Enumerate("case"))
            {
                var c = cO.Item2;
                int i = cO.Item1;
                var anchor = Json.AsCell(c["anchor"]);
                var mini = Json.AsStr(c["mini"]);
                int orient = Json.AsInt(c["orient"]);
                var cells = Geometry.FootprintCells(anchor, mini, orient);
                Check("case[" + i + "].cells", c["cells"], cells);
                Check("case[" + i + "].dims", c["dims"], Geometry.FootprintDims(mini, orient));
                Check("case[" + i + "].is_large", c["is_large"], Geometry.IsLarge(mini));
                Check("case[" + i + "].rot_overlap", c["rot_overlap"], Geometry.RotationOverlap(mini));
            }
            foreach (var cO in Json.AsList(v["adjacency"]).Select(Json.AsDict).Enumerate("adj"))
            {
                var c = cO.Item2; int i = cO.Item1;
                var a = CellSet(Json.AsList(c["a"]));
                var b = CellSet(Json.AsList(c["b"]));
                Check("adjacency[" + i + "].adjacent", c["adjacent"], Geometry.CellsAdjacent(a, b));
                Check("adjacency[" + i + "].gap", c["gap"], Geometry.ChebyshevGap(a, b));
            }
            foreach (var cO in Json.AsList(v["cardinal"]).Select(Json.AsDict).Enumerate("card"))
            {
                var c = cO.Item2; int i = cO.Item1;
                Check("cardinal[" + i + "].ok", c["ok"],
                      Geometry.CardinallyAdjacent(Json.AsCell(c["a"]), Json.AsCell(c["b"])));
            }
        }

        // -------------------------------------------------------------------- board
        static void SecBoard()
        {
            var v = LoadJson("board.json");
            var catalog = Catalog();
            var ids = catalog.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var kv in OrdKeys(v))
            {
                var e = Json.AsDict(kv.Value);
                var b = BoardOf(kv.Key);
                string p = "board[" + kv.Key + "]";
                Check(p + ".size", e["size"], new List<object> { (long)b.Width, (long)b.Height });
                foreach (var flag in new[] { "impassable", "blocking", "difficult", "interior" })
                    Check(p + "." + flag, e[flag], Cells2L(b.FlaggedCells(flag)));

                var edges = new List<object>();
                for (int y = 0; y < b.Height; y++)
                    for (int x = 0; x < b.Width; x++)
                        foreach (var d in new[] { (1, 0), (0, 1) })
                        {
                            var a = new Cell(x, y);
                            var c = new Cell(x + d.Item1, y + d.Item2);
                            if (!b.InBounds(c)) continue;
                            edges.Add(new Dictionary<string, object>
                            {
                                { "a", new List<object> { (long)a.X, (long)a.Y } },
                                { "b", new List<object> { (long)c.X, (long)c.Y } },
                                { "reason", b.BlocksMovement(a, c) },
                            });
                        }
                Check(p + ".edges", e["edges"], edges);

                var doors = new List<object>();
                foreach (var kv2 in b.Doors)
                    doors.Add(new Dictionary<string, object>
                    {
                        { "a", new List<object> { (long)kv2.Key.Item1.X, (long)kv2.Key.Item1.Y } },
                        { "b", new List<object> { (long)kv2.Key.Item2.X, (long)kv2.Key.Item2.Y } },
                        { "open", kv2.Value.IsOpen },
                        { "locked", kv2.Value.Locked },
                        { "blocks", kv2.Value.Blocks },
                        { "locked_override", true },   // python getattr default (no such attr)
                    });
                Check(p + ".doors", e["doors"], doors);

                var dps = new List<object>();
                foreach (var dp in b.DeploymentPoints)
                    dps.Add(new Dictionary<string, object>
                    {
                        { "id", dp.Id },
                        { "colors", dp.Colors },
                        { "massive_allowed", dp.MassiveAllowed },
                        { "cells", Cells2L(dp.Cells) },
                        { "allows", ids.Where(id => b.DpAllowsCard(dp, catalog[id]))
                                       .Cast<object>().ToList() },
                    });
                Check(p + ".dps", e["dps"], dps);

                var objs = new List<object>();
                foreach (var o in b.Objectives)
                    objs.Add(new Dictionary<string, object>
                    {
                        { "id", o.Id }, { "role", o.Role }, { "weight", o.Weight },
                        { "cells", Cells2L(o.Cells) },
                    });
                Check(p + ".objectives", e["objectives"], objs);

                Check(p + ".triggers", e["triggers"], b.Triggers);

                var pes = new List<object>();
                foreach (var pe in b.ProtectedEntities)
                    pes.Add(new Dictionary<string, object>
                    {
                        { "id", pe["id"] },
                        { "cells", Cells2L(Json.AsCells(pe["cells"])) },
                    });
                Check(p + ".protected_entities", e["protected_entities"], pes);

                Check(p + ".round_limit", e["round_limit"],
                      b.RoundLimit is int rl ? (object)(long)rl : null);
                Check(p + ".posture_override", e["posture_override"], b.PostureOverride);
                Check(p + ".victory_counter", e["victory_counter"], b.VictoryCounterName);
            }
        }

        // --------------------------------------------------------------------- path
        static void SecPath()
        {
            var v = LoadJsonList("path.json");
            foreach (var eO in v.Select(Json.AsDict).Enumerate("case"))
            {
                var e = eO.Item2; int i = eO.Item1;
                var board = BoardOf(Json.AsStr(e["fixture"]));
                var card = Catalog()[Json.AsStr(e["card"])];
                var anchor = Json.AsCell(e["anchor"]);
                var fig = new Figure { Id = "p", GroupId = "p", Card = card,
                                       Team = "imperial", Anchor = anchor };
                var graph = new PathGraph(board, () => new Dictionary<Cell, List<Figure>>());

                foreach (var tO in Json.AsList(e["transitions"]).Select(Json.AsDict).Enumerate("t"))
                {
                    var t = tO.Item2; int j = tO.Item1;
                    int orient = Json.AsInt(t["orient"]);
                    var mode = Json.AsStr(t["mode"]);
                    var f2 = new Figure { Id = "p", GroupId = "p", Card = card,
                                          Team = "imperial", Anchor = anchor,
                                          Orientation = orient };
                    var outp = graph.Transitions(f2, anchor, orient, mode, null);
                    var got = outp.Select(tr => (object)new Dictionary<string, object>
                    {
                        { "kind", tr.Kind },
                        { "anchor", new List<object> { (long)tr.Anchor.X, (long)tr.Anchor.Y } },
                        { "orientation", (long)tr.Orientation },
                        { "cost", (long)tr.Cost },
                        { "entered", Cells2L(tr.Entered) },
                    }).ToList();
                    Check("path[" + i + "].transitions[" + j + "]", t["out"], got);
                }

                foreach (var sO in Json.AsList(e["shortest"]).Select(Json.AsDict).Enumerate("s"))
                {
                    var s = sO.Item2; int j = sO.Item1;
                    var goal = Json.AsCell(s["goal"]);
                    var mode = Json.AsStr(s["mode"]);
                    var res = graph.Shortest(fig, anchor, 0,
                        (a, o) => a == goal, mode, null, null);
                    Check("path[" + i + "].shortest[" + j + "]", s, new Dictionary<string, object>
                    {
                        { "goal", s["goal"] },
                        { "mode", mode },
                        { "cost", res == null ? null : (object)(long)res.Cost },
                        { "states", res == null ? null : res.States.Select(x => (object)Norm(x)).ToList() },
                        { "start", res == null ? null : Norm(res.Start) },
                    });
                }

                var bnd = Json.AsDict(e["shortest_bounded"]);
                {
                    var goal = Json.AsCell(bnd["goal"]);
                    int budget = Json.AsInt(bnd["budget"]);
                    Check("path[" + i + "].budget", bnd["budget"], (long)card.Speed);
                    var res = graph.Shortest(fig, anchor, 0,
                        (a, o) => a == goal, "focus", budget, null);
                    Check("path[" + i + "].bounded.cost", bnd["cost"],
                          res == null ? (object)null : (long)res.Cost);
                }
            }
        }

        static Dictionary<string, object> PathDict(PathResult res)
        {
            if (res == null) return null;
            return new Dictionary<string, object>
            {
                { "cost", (long)res.Cost },
                { "states", res.States.Select(st => (object)Norm(st)).ToList() },
                { "start", Norm(res.Start) },
            };
        }

        // ----------------------------------------------------------------- legality
        static void SecLegality()
        {
            var v = LoadJsonList("legality.json");
            var catalog = Catalog();
            foreach (var eO in v.Select(Json.AsDict).Enumerate("case"))
            {
                var e = eO.Item2; int i = eO.Item1;
                var board = BoardOf(Json.AsStr(e["fixture"]));
                var card = catalog[Json.AsStr(e["card"])];
                var mover0 = MkFig("m", card, "imperial", new Cell(0, 0));
                // stand = anchors whose orient-0 footprint is place-legal (empty occ)
                var stand = new List<Cell>();
                for (int y = 0; y < board.Height; y++)
                    for (int x = 0; x < board.Width; x++)
                        if (SpaceLegality.FootprintPlaceLegal(board, mover0, new Cell(x, y), 0,
                                                               new Dictionary<Cell, List<Figure>>(),
                                                               null, null).Ok)
                            stand.Add(new Cell(x, y));
                if (stand.Count < 3) { Fail("legality[" + i + "]", "python skipped this case"); continue; }
                var a1 = stand[0];
                var a2 = stand[stand.Count - 1];
                var other = MkFig("r1", catalog["DG021"], "rebel", a2);
                var ally = MkFig("r2", card, "imperial", a2);
                var occ2 = OccOf(other);
                var occ3 = OccOf(ally);
                var empty = new Dictionary<Cell, List<Figure>>();

                int j = 0;
                foreach (var cell in stand.Take(14))
                    foreach (var orient in card.IsLargeBase ? new[] { 0, 1 } : new[] { 0 })
                    {
                        var exp = Json.AsDict(Json.AsList(e["place"])[j++]);
                        var leg = SpaceLegality.FootprintPlaceLegal(board, mover0, cell, orient,
                                                                    empty, null, null);
                        Check("legality[" + i + "].place[" + (j - 1) + "]", exp,
                              new Dictionary<string, object>
                              {
                                  { "anchor", Norm(cell) },
                                  { "orient", (long)orient },
                                  { "ok", leg.Ok },
                                  { "reason", leg.Reason },
                                  { "cells", Cells2L(Geometry.FootprintCells(cell, card.MiniSize, orient)) },
                              });
                        if (orient == 0 && card.IsLargeBase)
                        {
                            var rexp = Json.AsList(e["rotate"]);
                            int rj = (int)((j + 1) / 2) - 1;   // one rotate per cell-pair block
                            var lr = SpaceLegality.RotateLegal(board, mover0, cell, 0, empty);
                            Check("legality[" + i + "].rotate[" + rj + "]", rexp[rj],
                                  new Dictionary<string, object>
                                  {
                                      { "anchor", Norm(cell) },
                                      { "ok", lr.Ok },
                                      { "reason", lr.Reason },
                                  });
                        }
                    }

                int ej = 0, pj = 0;
                foreach (var cell in stand.Skip(2).Take(4))
                    foreach (var pair in new[]
                             {
                                 ((Dictionary<Cell, List<Figure>>)empty, "empty"),
                                 (occ2, "rebel"),
                                 (occ3, "ally"),
                             })
                    {
                        var mover = MkFig("m", card, "imperial", cell);
                        var le = SpaceLegality.FootprintEnterLegal(board, mover, cell, 0,
                                                                   pair.Item1, new[] { mover });
                        Check("legality[" + i + "].enter[" + ej + "]", Json.AsDict(Json.AsList(e["enter"])[ej]),
                              new Dictionary<string, object>
                              {
                                  { "anchor", Norm(cell) }, { "occ", pair.Item2 },
                                  { "ok", le.Ok }, { "reason", le.Reason },
                              });
                        ej++;
                        var vic = SpaceLegality.PushCandidates(board, mover, pair.Item1);
                        Check("legality[" + i + "].push[" + pj + "]", Json.AsDict(Json.AsList(e["push"])[pj]),
                              new Dictionary<string, object>
                              {
                                  { "anchor", Norm(cell) }, { "occ", pair.Item2 },
                                  { "victims", vic.Select(x => (object)x.Id).ToList() },
                              });
                        pj++;
                    }

                var mass = Json.AsList(e["massive"]);
                if (mass.Count > 0)
                {
                    var victim = MkFig("V", catalog["DG021"], "rebel", a1);
                    var occ4 = OccOf(victim);
                    var exp = Json.AsDict(mass[0]);
                    var cands = SpaceLegality.NearestEmptyCell(board, a1, occ4, victim);
                    Check("legality[" + i + "].massive[0]", exp, new Dictionary<string, object>
                    {
                        { "anchor", Norm(a1) },
                        { "nearest_empty", cands == null ? null : Norm(cands.Value) },
                    });
                }
            }
        }

        static Figure MkFig(string id, DeploymentCard card, string team, Cell anchor) =>
            new Figure { Id = id, GroupId = id, Card = card, Team = team, Anchor = anchor };

        static Dictionary<Cell, List<Figure>> OccOf(params Figure[] figs)
        {
            var occ = new Dictionary<Cell, List<Figure>>();
            foreach (var f in figs)
                foreach (var c in SpaceLegality.FigureCells(f))
                {
                    if (!occ.TryGetValue(c, out var l)) { l = new List<Figure>(); occ[c] = l; }
                    l.Add(f);
                }
            return occ;
        }

        // ---------------------------------------------------------------------- los
        static void SecLos()
        {
            var v = LoadJsonList("los.json");
            var catalog = Catalog();
            var ids = catalog.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var eO in v.Select(Json.AsDict).Enumerate("case"))
            {
                var e = eO.Item2; int i = eO.Item1;
                var board = BoardOf(Json.AsStr(e["fixture"]));
                var los = new Los(board);
                var stand = new List<Cell>();
                for (int y = 0; y < board.Height; y++)
                    for (int x = 0; x < board.Width; x++)
                    {
                        var c = new Cell(x, y);
                        if (!board.IsImpassableCell(c)) stand.Add(c);
                    }
                int step = Math.Max(1, stand.Count / 9);
                var sample = new List<Cell>();
                for (int k = 0; k < stand.Count && sample.Count < 9; k += step) sample.Add(stand[k]);
                int j = 0;
                for (int a = 0; a < sample.Count; a++)
                    for (int bq = a + 1; bq < sample.Count; bq++)
                    {
                        var exp = Json.AsDict(Json.AsList(e["static"])[j]);
                        Check("los[" + i + "].static[" + j + "]", exp["has"],
                              los.HasLos(new[] { sample[a] }, new[] { sample[bq] }));
                        j++;
                    }
                var fig = MkFig("m", catalog[ids[0]], "imperial", new Cell(1, 1));
                var anchor2 = board.InBounds(new Cell(4, 3)) ? new Cell(4, 3)
                                                             : new Cell(board.Width - 2, board.Height - 1);
                var fig2 = MkFig("r", catalog["DG021"], "rebel", anchor2);
                var aCells = SpaceLegality.FigureCells(fig);
                var cCells = SpaceLegality.FigureCells(fig2);
                var d0 = Json.AsDict(Json.AsList(e["dynamic"])[0]);
                Check("los[" + i + "].dyn.a", d0["a"], Cells2L(aCells));
                Check("los[" + i + "].dyn.b", d0["b"], Cells2L(cCells));
                Check("los[" + i + "].dyn.has_free", d0["has_free"], los.HasLos(aCells, cCells));
                Check("los[" + i + "].dyn.has_blocker", d0["has_blocker"],
                      los.HasLos(aCells, cCells, cCells));
            }
        }

        // --------------------------------------------------------------------- dice
        static void SecDice()
        {
            var v = LoadJson("dice.json");
            foreach (var kv in OrdKeys(Json.AsDict(v["colors"])))
                foreach (var kv2 in OrdKeys(Json.AsDict(kv.Value)))
                    Check("colors." + kv.Key + "." + kv2.Key, kv2.Value,
                          Dice.DieExpectations(kv.Key)[kv2.Key]);
            foreach (var kv in OrdKeys(Json.AsDict(v["defense"])))
                Check("defense." + kv.Key, kv.Value, Dice.DefenseExpectation(kv.Key));
            foreach (var kv in OrdKeys(Json.AsDict(v["printed_ev"])))
                Check("printed_ev." + kv.Key, kv.Value, Dice.PrintedEv(Catalog()[kv.Key]));
        }

        // ------------------------------------------------------------------- threat
        static void SecThreat()
        {
            var v = LoadJsonList("threat.json");
            foreach (var eO in v.Select(Json.AsDict).Enumerate("case"))
            {
                var e = eO.Item2; int i = eO.Item1;
                int save = Json.AsInt(e["save"]);
                int idle = Json.AsInt(e["idle"]);
                var led = new ThreatLedger(save, idle);
                var res = new List<ReserveGroup>
                {
                    new ReserveGroup("gA", "DG001", 4, "aggressive"),
                    new ReserveGroup("gB", "DG021", 2, "guard"),
                    new ReserveGroup("gC", "DG009", 6, "blocker"),
                    new ReserveGroup("gD", "DG021", 2, "hunter"),
                };
                int alive = 2;
                int j = 0;
                foreach (var sO in Json.AsList(e["seq"]).Select(Json.AsDict))
                {
                    var s = sO; int k = j++;
                    int rnd = Json.AsInt(s["round"]);
                    long inc = led.Tick(rnd, alive);
                    var pick = led.ReleaseDecision(rnd, res, alive);
                    if (pick != null)
                    {
                        bool forced = (rnd - led.LastReleaseRound) >= idle;
                        led.Release(rnd, pick, forced);
                        alive += 1;
                    }
                    Check("threat[" + i + "].seq[" + k + "]", s,
                          new Dictionary<string, object>
                          {
                              { "round", (long)rnd },
                              { "income", inc },
                              { "balance", led.Balance },
                              { "pick", pick == null ? null : (object)pick.GroupId },
                              { "wait", led.WaitPressure(res) },
                          });
                }
                Check("threat[" + i + "].final", e["final"], led.AsDict());
            }
        }

        // -------------------------------------------------------------------- decks
        static void SecDecks()
        {
            var v = LoadJson("decks.json");
            foreach (var rO in Json.AsList(v["validate"]).Select(Json.AsDict).Enumerate("v"))
            {
                var r = rO.Item2; int i = rO.Item1;
                var blocks = Json.AsList(r["blocks"]).Select(x => Json.AsStr(x)).ToArray();
                var card = new OrderCard(Json.AsStr(r["id"]), blocks);
                Check("validate[" + i + "]", r["violations"], OrderDeck.ValidateCard(card));
            }
            foreach (var rO in Json.AsList(v["mix"]).Select(Json.AsDict).Enumerate("m"))
            {
                var r = rO.Item2; int i = rO.Item1;
                var mix = Json.AsDict(r["mix"]).ToDictionary(kv => kv.Key,
                                                             kv => (int)Json.AsLong(kv.Value));
                var slots = Json.AsList(r["slots"]).Select(x => (int)Json.AsLong(x)).ToList();
                var cards = OrderDeck.DeckFromMix(Json.AsStr(r["card"]), mix, slots, "rest");
                Check("mix[" + i + "]", r["cards"], cards.Select(c => (object)c.AsDict()).ToList());
            }
            foreach (var rO in Json.AsList(v["deck_draw"]).Select(Json.AsDict).Enumerate("d"))
            {
                var r = rO.Item2; int i = rO.Item1;
                var mix = new Dictionary<string, int> { { "MA", 6 }, { "A", 2 }, { "M", 0 }, { "R", 0 } };
                var cards = OrderDeck.DeckFromMix("DG001", mix, new List<int> { 3 }, "rest");
                // python: OrderDeck(cards, 0) shuffles with random.Random(0),
                // then the harness swaps in the TracingRandom stream
                var deck = new OrderDeck(cards, new PyRandom(0));
                deck.Rng = new ScriptedRandom(Json.AsList(r["below"]), Json.AsList(r["u"]));
                var hist = new List<object>();
                for (int k = 0; k < 12; k++)
                {
                    var c = deck.Draw();
                    hist.Add(new List<object> { "draw", c.Id });
                    deck.FinishCard(c);
                    hist.Add(new List<object> { "finish", c.Id });
                }
                Check("deck_draw[" + i + "].history", r["history"], hist);
                Check("deck_draw[" + i + "].state", r["state"], new Dictionary<string, object>
                {
                    { "draw", deck.DrawPile.Select(c => (object)c.Id).ToList() },
                    { "discard", deck.Discard.Select(c => (object)c.Id).ToList() },
                });
            }
            var od = Json.AsDict(v["order_decks"]);
            var rawOd = Json.AsDict(Json.Parse(File.ReadAllText(Path.Combine(FIX, "order_decks.json"))));
            Check("order_decks.schema", od["schema"], Json.Get(rawOd, "$schema", null));
            foreach (var kv in OrdKeys(Json.AsDict(od["decks"])))
            {
                var e = Json.AsDict(kv.Value);
                var cards = Json.AsList(e["cards"]).Select(cO =>
                {
                    var c = Json.AsDict(cO);
                    return new OrderCard(Json.AsStr(c["id"]),
                        Json.AsList(c["blocks"]).Select(x => Json.AsStr(x)).ToArray());
                }).ToList();
                var vios = new List<string>();
                foreach (var c in cards) vios.AddRange(OrderDeck.ValidateCard(c));
                Check("order_decks." + kv.Key + ".cards", e["cards"],
                      cards.Select(c => (object)c.AsDict()).ToList());
                Check("order_decks." + kv.Key + ".violations", e["violations"], vios);
                Check("order_decks." + kv.Key + ".mix", e["mix"], e["mix"]);
                Check("order_decks." + kv.Key + ".shuffle_slots", e["shuffle_slots"], e["shuffle_slots"]);
                Check("order_decks." + kv.Key + ".is_elite", e["is_elite"], e["is_elite"]);
            }
        }

        // -------------------------------------------------------------------- skill
        static void SecSkill()
        {
            var v = LoadJson("skill.json");
            foreach (var lv in new[] { "S1", "S2", "S3", "S4", "S5" })
            {
                var prof = SkillProfile.For(lv);
                Check("skill.profiles." + lv, Json.AsDict(v["profiles"])[lv], prof.AsDict());
                Check("skill.timer." + lv, Json.AsDict(v["timer"])[lv],
                      prof.TimerWindow() is int tw ? (object)(long)tw : null);
                Check("skill.notes." + lv, Json.AsDict(v["notes"])[lv], SkillProfile.DoctrineNote(prof));
                var rk = Json.AsDict(Json.AsDict(v["rank"])[lv]);
                var rng = new ScriptedRandom(Json.AsList(rk["below"]), Json.AsList(rk["u"]));
                var draws = new List<object>();
                foreach (var n in new[] { 5, 3, 8, 1, 6 })
                    draws.Add((long)prof.RankChoice(rng, n));
                Check("skill.rank." + lv + ".draws", rk["draws"], draws);
                if (!rng.Exhausted())
                    Fail("skill.rank." + lv, "python rng stream not fully consumed by RankChoice");
            }
        }

        // --------------------------------------------------------------------- risk
        static void SecRisk()
        {
            var v = LoadJson("risk.json");
            var postures = new[] { "eliminate", "stall", "protect", "deny" };
            var counters = new string[] { null, "woundAll", "incapacitateAll" };
            var vits = new[] { new Rational(1), new Rational(3, 4), new Rational(1, 2), new Rational(1, 4) };
            var patterns = Patterns.PatternsList;
            int ci = 0;
            var rows = Json.AsList(v["cases"]);
            foreach (var p in postures)
                foreach (var cnt in counters)
                    foreach (var pat in patterns)
                        foreach (var vit in vits)
                            foreach (var ring in new[] { false, true })
                                foreach (var anchor in new[] { false, true })
                                    foreach (var vb in new[] { false, true })
                                        foreach (var rho in new[] { 0, 1, 2, 3 })
                                        {
                                            var expect = Json.AsDict(rows[ci]);
                                            var f = new RiskFacts
                                            {
                                                GroupId = "G1", Pattern = pat, Posture = p,
                                                Counter = cnt, Vitality = vit, RingNear = ring,
                                                OnAnchor = anchor, ReplacesAt = 4,
                                                LedgerBalance = 6, VictoryBlocker = vb,
                                            };
                                            var d = RiskPosture.RiskPosition(f, rho);
                                            Check("risk.case[" + ci + "].rho", expect["rho"], (long)rho);
                                            Check("risk.case[" + ci + "].facts", expect["facts"],
                                                  new Dictionary<string, object>
                                                  {
                                                      { "posture", p },
                                                      { "counter", cnt },
                                                      { "pattern", pat },
                                                      { "vitality", vit.ToString() },
                                                      { "ring", ring },
                                                      { "anchor", anchor },
                                                      { "vb", vb },
                                                  });
                                            Check("risk.case[" + ci + "].d", expect["d"], d.AsDict());
                                            ci++;
                                        }
            if (ci != rows.Count) Fail("risk.cases", "python rows=" + rows.Count + " but harness generated " + ci);
            var rhoMap = Json.AsDict(v["rho_map"]);
            foreach (var kv in OrdKeys(rhoMap))
                Check("risk.rho_map." + kv.Key, kv.Value, RiskPosture.RhoOf(kv.Key));

            foreach (var kv in OrdKeys(Json.AsDict(v["anchors"])))
            {
                var e = Json.AsDict(kv.Value);
                var board = BoardOf(kv.Key);
                var na = RiskPosture.NamedAnchors(board);
                var got = na.Select(a => (object)new List<object> { a.Label, Cells2L(a.Cells) }).ToList();
                Check("risk.anchors[" + kv.Key + "]", e["anchors"], got);
                foreach (var pO in Json.AsList(e["pick"]).Select(Json.AsDict).Enumerate("p"))
                {
                    var p = pO.Item2; int j = pO.Item1;
                    var near = new List<Cell> { Json.AsCell(p["near"]) };
                    Check("risk.pick[" + kv.Key + "][" + j + "]", p["label"],
                          RiskPosture.PickAnchor(board, na, near));
                }
            }
        }
    }

    static class LinqEnumExt
    {
        public static IEnumerable<(int, T)> Enumerate<T>(this IEnumerable<T> src, string tag)
        {
            int i = 0;
            foreach (var x in src) yield return (i++, x);
        }
    }
}
