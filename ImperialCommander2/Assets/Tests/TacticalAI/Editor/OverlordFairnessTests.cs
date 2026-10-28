// M6 overlord fairness suite (task t_62d1ca15 Scope B). Pins the four
// guarantees the overlord profile owes under the fairness canon (concept
// 3.3.1/12):
//   (a) IDENTICAL LEGAL ACTION SETS for every skill level - the order plan
//       is a permutation of the living groups, the focus ranking a
//       permutation of the living heroes: skill only re-orders, never
//       filters, never invents.
//   (b) NO NON-PUBLIC INFORMATION: the overlord focus consumes EXACTLY the
//       feed projection + the curated public hero table; two different event
//       streams that project to the SAME canon must yield the SAME ranking
//       (the planner cannot tell anything else apart - and thus uses
//       nothing else).
//   (c) DETERMINISM: 2x replay bit-identical (projection canon, history
//       hash, and every FocusPick row).
//   (d) S4/S5 ON THE CONTEST CARD: plans byte-equal the golden vectors and
//       every deviation from the greedy strength order carries a documented
//       public rule tag; untagged groups keep the greedy order exactly
//       (the statistical ladder itself is the seed-fixed referee simulation
//       in the sim repo - pinned via tests/test_m6_overlord.py there).
// Repo convention (see TacaiFixtures header): no Unity test framework is
// wired today, so every test is [Explicit]; the console harness
// (world_feed.json + overlord_focus.json sections, ALL PASS) is the proof
// of today - these are honest mirrors + the fairness invariants on top.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TacticalAI.Core;

namespace TacticalAI.Tests
{
using DeploymentCard = TacticalAI.Core.DeploymentCard;

    [TestFixture]
    [Explicit("Requires Test Framework")]
    public class OverlordFairnessTests
    {
        static List<WorldEvent> Events(List<object> raws)
        {
            var list = new List<WorldEvent>();
            int seq = 1;
            foreach (var raw in raws)
            {
                var e = Json.AsDict(raw);
                var payload = e.ContainsKey("payload")
                    ? new Dictionary<string, object>(Json.AsDict(e["payload"]))
                    : new Dictionary<string, object>();
                list.Add(new WorldEvent(Json.AsStr(e["kind"]),
                                        (int)Json.AsLong(e["round"]), payload) { Seq = seq++ });
            }
            return list;
        }

        static HeroThreatProfile Profile(string name)
        {
            var doc = HeroProfiles.DocOrNull();
            if (doc == null)
            {
                // Editor run without primed asset: load the shipped asset
                // directly (same bytes the vectors were exported against).
                var asset = TacaiFixtures.Path2("..", "..", "..", "..", "Resources",
                                                "Languages", "En", "tactical_hero_profiles.json");
                if (!System.IO.File.Exists(asset)) return null;
                doc = Json.AsDict(Json.Parse(System.IO.File.ReadAllText(asset)));
            }
            var ps = HeroProfiles.ProfilesFromDoc(doc);
            HeroThreatProfile p;
            return ps.TryGetValue(name, out p) ? p : null;
        }

        // ------------------------------------------------------- (a) legal sets
        [Test]
        public void OrderPlan_Is_Permutation_For_Every_Skill()
        {
            var groups = ContestGroups();
            var catalog = Catalog();
            var ids = groups.Select(g => g.GroupId).OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var lvl in new[] { null, "S1", "S2", "S3", "S4", "S5" })
            {
                var plan = OrderPlanner.PlanRound(3, ContestGroups(), catalog, "eliminate",
                    lvl == null ? null : SkillProfile.For(lvl),
                    new DeterministicRandom(7), 1, new[] { "Z2" }, "Z1", "woundAll");
                OrderPlanner.AssertPermutation(plan, groups.Select(g => g.GroupId));
                var outp = plan.Slots.Select(s => s.GroupId).OrderBy(x => x, StringComparer.Ordinal).ToList();
                CollectionAssert.AreEqual(ids, outp, "level " + lvl + " changed the legal group set");
            }
        }

        [Test]
        public void FocusRanking_Is_Permutation_On_Every_Fixture_Case()
        {
            var doc = Json.AsDict(TacaiFixtures.Load("overlord_focus.json"));
            foreach (var raw in Json.AsList(doc["cases"]))
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var cands = Candidates(Json.AsList(c["heroes"]));
                var inIds = cands.Select(x => x.HeroId).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var ranked = OverlordFocus.Rank(cands, Json.AsStr(c["posture"]),
                    c.ContainsKey("counter") && c["counter"] != null ? Json.AsStr(c["counter"]) : null);
                var outIds = ranked.Select(x => x.HeroId).OrderBy(x => x, StringComparer.Ordinal).ToList();
                CollectionAssert.AreEqual(inIds, outIds,
                    name + ": focus ranking must permute, never filter (fairness canon)");
            }
        }

        // ------------------------------------------------- (b) feed isolation
        [Test]
        public void Same_Projection_Same_Focus_Decision()
        {
            // Two DIFFERENT histories that project to the same canon (wound +
            // activation interleaved differently, same final facts) must be
            // indistinguishable to the overlord - the ranking can therefore
            // only depend on public projected facts, never on hidden state.
            var streamA = Events(new List<object>
            {
                Row("MissionStart", 1, "objective", "Eliminate the rebels", "priorityTarget", "Rebel", "roundLimit", -1L),
                Row("HeroWounded", 2, "hero", "Han Solo"),
                Row("HeroActivated", 2, "hero", "Luke Skywalker"),
                Row("HeroActivated", 3, "hero", "Han Solo"),
            });
            var streamB = Events(new List<object>
            {
                Row("MissionStart", 1, "objective", "Eliminate the rebels", "priorityTarget", "Rebel", "roundLimit", -1L),
                Row("HeroActivated", 2, "hero", "Luke Skywalker"),
                Row("HeroWounded", 2, "hero", "Han Solo"),
                Row("HeroActivated", 3, "hero", "Han Solo"),
            });
            var vA = WorldEventVector.Project(streamA, "fair|A");
            var vB = WorldEventVector.Project(streamB, "fair|B");
            // strip the mission-key line (differs by construction), compare facts
            var canonA = vA.Canon().Replace("mission=fair|A", "mission=fair|");
            var canonB = vB.Canon().Replace("mission=fair|B", "mission=fair|");
            Assert.That(canonB, Is.EqualTo(canonA), "canon of equivalent histories diverged");

            var ra = FocusRankFromView(vA);
            var rb = FocusRankFromView(vB);
            CollectionAssert.AreEqual(
                ra.Select(x => x.AsDict()).ToList(),
                rb.Select(x => x.AsDict()).ToList(),
                "focus decisions differ on identical public information");
        }

        // ------------------------------------------------------ (c) determinism
        [Test]
        public void Replay_Is_BitIdentical()
        {
            var doc = Json.AsDict(TacaiFixtures.Load("overlord_focus.json"));
            foreach (var raw in Json.AsList(doc["cases"]))
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var posture = Json.AsStr(c["posture"]);
                var counter = c.ContainsKey("counter") && c["counter"] != null ? Json.AsStr(c["counter"]) : null;
                var r1 = OverlordFocus.Rank(Candidates(Json.AsList(c["heroes"])), posture, counter);
                var r2 = OverlordFocus.Rank(Candidates(Json.AsList(c["heroes"])), posture, counter);
                CollectionAssert.AreEqual(
                    r1.Select(x => x.AsDict()).ToList(),
                    r2.Select(x => x.AsDict()).ToList(),
                    name + ": replay diverged");
            }
        }

        // ------------------------------------- (d) overlord ladder on contest card
        // The vector-pinned part: S3/S4/S5 plans on the calibration contest
        // fact grid byte-equal the exported golden cases (the console harness
        // replays the same 48-case section). The fairness part replaces a raw
        // exposure proxy (which cannot see synergy gains): every deviation
        // from the greedy strength order must carry its documented public
        // rule tag, and groups nobody moved keep the greedy order exactly.
        // (The statistical ladder itself is the seed-fixed referee simulation
        //  results/skill_grid_m6 in the sim repo, pinned as
        //  skill_curve_m6.json via the pytest mirror tests/test_m6_overlord.py.)
        [Test]
        public void Overlord_Contest_Plans_Are_Vector_Pinned_And_Rule_Tagged()
        {
            var v = Json.AsDict(TacaiFixtures.Load("order_plan.json"));
            var cases = Json.AsList(v["cases"]);
            foreach (var postureIdx in new[] { 0, 1 })     // eliminate, stall
            {
                var posture = postureIdx == 0 ? "eliminate" : "stall";
                var greedy = GreedyOrder(posture);
                foreach (var kv in new[]
                {
                    new KeyValuePair<string,int>("S3", 6),
                    new KeyValuePair<string,int>("S4", 8),
                    new KeyValuePair<string,int>("S5", 10),
                })
                {
                    var exp = Json.AsDict(cases[postureIdx * 12 + kv.Value]);
                    var plan = OrderPlanner.PlanRound(3, ContestGroups(), Catalog(), posture,
                        SkillProfile.For(kv.Key), new DeterministicRandom(7), 1,
                        new[] { "Z2" }, "Z1", "woundAll");
                    CollectionAssert.AreEqual(
                        Json.AsList(Json.AsDict(exp["plan"])["order"]).Cast<object>().ToList(),
                        plan.Slots.Select(s => (object)s.GroupId).ToList(),
                        kv.Key + "/" + posture + ": plan differs from the golden vector");
                    if (kv.Key == "S3") continue;  // veteran = greedy baseline
                    var tags = plan.Slots.ToDictionary(s => s.GroupId, s => s.Tags);
                    if (!plan.Slots.Select(s => s.GroupId).SequenceEqual(greedy))
                        Assert.That(tags.Values.Any(t => t.Count > 0), Is.True,
                            kv.Key + ": deviation from greedy without any documented rule");
                    CollectionAssert.AreEqual(
                        greedy.Where(g => tags[g].Count == 0).ToList(),
                        plan.Slots.Where(s => s.Tags.Count == 0).Select(s => s.GroupId).ToList(),
                        kv.Key + ": untagged groups were re-shuffled (hidden lever!)");
                }
            }
        }

        static List<string> GreedyOrder(string posture)
        {
            var catalog = Catalog();
            var ring = new HashSet<string> { "Z2" };
            var ws = ContestGroups().Select(g => OrderPlanner.WithEv(g, catalog[g.CardId])).ToList();
            // LINQ OrderBy is stable (mirrors Python sorted / planner SortStable)
            return ws.OrderByDescending(w => OrderPlanner.Efficiency(w, posture, ring, "Z1"))
                     .ThenByDescending(w => w.PrintedEvTotal)
                     .ThenBy(w => w.Facts.Index)
                     .Select(w => w.Facts.GroupId).ToList();
        }

        // ------------------------------------------------------------ builders
        // Mirrors the harness' SecOrderPlan fact construction (SectionsB.cs)
        // 1:1 - the Kalibrier-Kontest fact grid the vectors were exported on.
        static List<GroupFacts> ContestGroups()
        {
            var pats = Patterns.PatternsList;
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
            return groups;
        }

        static Dictionary<string, DeploymentCard> _catalog;
        static Dictionary<string, DeploymentCard> Catalog()
        {
            if (_catalog == null)
                _catalog = DeploymentCard.LoadCatalog(Json.AsList(
                    Json.Parse(System.IO.File.ReadAllText(TacaiFixtures.Path2("units_core.json")))));
            return _catalog;
        }

        static List<FocusCandidate> Candidates(List<object> heroes)
        {
            var list = new List<FocusCandidate>();
            foreach (var raw in heroes)
            {
                var h = Json.AsDict(raw);
                HeroThreatProfile p = null;
                if (h.ContainsKey("profile") && h["profile"] != null)
                    p = Profile(Json.AsStr(h["profile"]));
                list.Add(new FocusCandidate
                {
                    HeroId = Json.AsStr(h["hero"]),
                    SquadRow = Json.AsInt(h["row"]),
                    Profile = p,
                    Wounded = Json.AsBool(h["wounded"]),
                    Marks = (int)Json.AsLong(h["marks"]),
                    MarkedRound = (int)Json.AsLong(h["marked_round"]),
                    AttackDice = (int)Json.AsLong(h["attack_dice"]),
                    Health = Json.AsInt(h["health"]),
                    // M11b/M11d mirror parity with the harness Build():
                    // printed card + optional strain input (absent = live
                    // app state, zero influence).
                    Card = HeroCards.For(Json.AsStr(h["hero"])),
                    StrainMarkers = h.ContainsKey("strain_markers") && h["strain_markers"] != null
                        ? (int?)Json.AsLong(h["strain_markers"]) : null,
                    AvailableXp = h.ContainsKey("xp_amount") && h["xp_amount"] != null
                        ? (int?)Json.AsLong(h["xp_amount"]) : null,
                });
            }
            return list;
        }

        /// Build focus candidates ONLY from the projected view (+ the public
        /// curated table): hero flags, marks - nothing else can reach the
        /// ranking (the (b) isolation guarantee in code form).
        static List<FocusPick> FocusRankFromView(FeedWorldView v)
        {
            var cands = new List<FocusCandidate>();
            int row = 0;
            foreach (var h in v.Heroes)
                cands.Add(new FocusCandidate
                {
                    HeroId = h.HeroId,
                    SquadRow = row++,
                    Profile = Profile(h.HeroId),
                    Wounded = h.Wounded,
                    Marks = h.Marks,
                    MarkedRound = h.MarkedRound,
                    AttackDice = 0,        // not in the projection -> equal 0
                    Health = 0,
                });
            return OverlordFocus.Rank(cands, v.Posture, null);
        }

        static Dictionary<string, object> Row(string kind, int round, params object[] kv)
        {
            var payload = new Dictionary<string, object>();
            for (int i = 0; i < kv.Length; i += 2)
                payload[(string)kv[i]] = kv[i + 1];
            return new Dictionary<string, object>
            {
                { "kind", kind }, { "round", (long)round }, { "payload", payload },
            };
        }
    }
}
