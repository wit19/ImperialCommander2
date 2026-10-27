// M5 world-feed NUnit suite (task t_006ccc2b Scope C). Mirrors the console
// harness' SecWorldFeed against the SAME canonical fixture
// (world_feed.json) and adds the four feed guarantees the M5 design owes:
//   (a) same feed -> bit-identical decisions (2x replay),
//   (b) seq gap   -> resync path (never a partial projection),
//   (c) hash stability of the history (key-order & instance independence),
//   (d) posture derivation from mission properties.
// Repo convention (see TacaiFixtures header): no Unity test framework is
// wired today, so every test is [Explicit]; the console harness
// (world_feed.json section, ALL PASS) is the proof of today.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using TacticalAI.Core;

namespace TacticalAI.Tests
{
    [TestFixture]
    [Explicit("Requires Test Framework")]
    public class WorldEventsTests
    {
        static readonly string[] ExpectKeys =
            { "posture", "objective", "round", "threat", "seq", "hash", "canon" };

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

        // ------------------------------------------- fixture mirror (canonical)
        [Test]
        public void Fixture_WorldFeed_Matches_Harness()
        {
            var doc = Json.AsDict(TacaiFixtures.Load("world_feed.json"));
            foreach (var raw in Json.AsList(doc["cases"]))
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var events = Events(Json.AsList(c["events"]));
                var view = WorldEventVector.Project(events, "vector|" + name);
                var exp = Json.AsDict(c["expect"]);
                var got = new Dictionary<string, object>
                {
                    { "posture", view.Posture },
                    { "objective", view.Objective },
                    { "round", (long)view.Round },
                    { "threat", view.ThreatBalance },
                    { "seq", (long)view.Seq },
                    { "hash", view.HistoryHash },
                    { "canon", view.Canon() },
                };
                foreach (var k in ExpectKeys)
                    TacaiFixtures.Check(name + "." + k, exp[k], got[k]);
            }
        }

        // (a) same feed -> bit-identical decisions, 2x replay
        [Test]
        public void SameFeed_Bitidentical_Decisions_Twice()
        {
            var doc = Json.AsDict(TacaiFixtures.Load("world_feed.json"));
            foreach (var raw in Json.AsList(doc["cases"]))
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var raws = Json.AsList(c["events"]);

                // two INDEPENDENT feeds, same scripted events
                var feedA = new WorldEventFeed("m");
                var feedB = new WorldEventFeed("m");
                foreach (var e in Events(raws))
                    feedA.Append(e.Kind, e.Round, e.Payload);
                foreach (var e in Events(raws))
                    feedB.Append(e.Kind, e.Round, e.Payload);

                var viewA = WorldEventVector.Project(feedA.Events, "m", feedA.Hash());
                var viewB = WorldEventVector.Project(feedB.Events, "m", feedB.Hash());
                TacaiFixtures.Check(name + ".canon", viewA.Canon(), viewB.Canon());
                TacaiFixtures.Check(name + ".hash", feedA.Hash(), feedB.Hash());

                // decisions: OrderPlanner over the VIEW-derived facts, twice
                var names = new Dictionary<string, string>();
                var groups = new List<GroupFacts>();
                int idx = 0;
                foreach (var g in viewA.Groups)
                {
                    groups.Add(new GroupFacts(g.GroupId, idx++, g.GroupId, g.Name,
                        "aggressive", Math.Max(1, g.Alive), g.VitalityFrac));
                    names[g.GroupId] = g.Name;
                }
                if (groups.Count == 0) continue;
                var cat = TacaiFixtures.Catalog();
                var plan1 = OrderPlanner.PlanRound(3, groups, cat, viewA.Posture,
                    null, new DeterministicRandom(7), 1, null, null, null);
                OrderPlanner.AssertPermutation(plan1, new List<string>(names.Keys));
                var plan2 = OrderPlanner.PlanRound(3, groups, cat, viewB.Posture,
                    null, new DeterministicRandom(7), 1, null, null, null);
                OrderPlanner.AssertPermutation(plan2, new List<string>(names.Keys));
                TacaiFixtures.Check(name + ".plan_slots",
                    string.Join("|", System.Linq.Enumerable.Select(plan1.Slots, s => s.GroupId)),
                    string.Join("|", System.Linq.Enumerable.Select(plan2.Slots, s => s.GroupId)));
                TacaiFixtures.Check(name + ".plan_why",
                    plan1.PublicLine(names), plan2.PublicLine(names));
            }
        }

        // (b) seq gap -> resync path
        [Test]
        public void SeqGap_VerifyProjectAndResync()
        {
            var feed = new WorldEventFeed("gapcase");
            feed.Append("MissionStart", 1, new Dictionary<string, object>
            {
                { "objective", "Destroy the rebels" }, { "priorityTarget", "Rebel" },
                { "roundLimit", -1L }, { "mission", "core1" }, { "missionType", "Story" },
            });
            feed.Append("Spawn", 1, new Dictionary<string, object> { { "group", "DG001" }, { "size", 2L } });
            feed.Append("GroupDamage", 2, new Dictionary<string, object>
            {
                { "group", "DG001" }, { "figure", 0L }, { "wheel", 2L }, { "health", 4L },
            });
            var full = new List<WorldEvent>(feed.Events);
            var fullHash = feed.Hash();

            // consumer window with a hole (event 2 lost)
            var hole = new List<WorldEvent> { full[0], full[2] };
            TacaiFixtures.Check("verifyseq.gap_at", 2L, (long)WorldEventFeed.VerifySeq(hole).Value);
            bool threw = false;
            try { WorldEventVector.Project(hole); }
            catch (InvalidOperationException) { threw = true; }
            TacaiFixtures.Check("project.rejects_gap", true, threw);

            // Resync = re-log of the COMPLETE history (here: the same truth).
            feed.Resync(full);
            TacaiFixtures.Check("resync.seq_contiguous", null, WorldEventFeed.VerifySeq(feed.Events));
            TacaiFixtures.Check("resync.hash_restored", fullHash, feed.Hash());
            TacaiFixtures.Check("resync.counted", 1L, (long)feed.ResyncCount);
            // the resynced stream projects again - no partial state, no guess
            var view = WorldEventVector.Project(feed.Events, "gapcase", feed.Hash());
            TacaiFixtures.Check("resync.view_vitality", "1/4",
                view.FindGroup("DG001").VitalityFrac.ToString());

            // a broken re-log is refused (fail-loud, never auto-repair)
            threw = false;
            try { feed.Resync(new List<WorldEvent> { full[0], full[2] }); }
            catch (InvalidOperationException) { threw = true; }
            TacaiFixtures.Check("resync.rejects_gapped_relog", true, threw);
        }

        // (c) hash stability
        [Test]
        public void HistoryHash_Stable_AcrossInstances_AndPayloadKeyOrder()
        {
            var a = new WorldEventFeed("k");
            a.Append("GroupDamage", 2, new Dictionary<string, object>
            {
                { "group", "G" }, { "figure", 1L }, { "wheel", 3L }, { "health", 5L },
            });
            var b = new WorldEventFeed("k");
            b.Append("GroupDamage", 2, new Dictionary<string, object>
            {
                { "health", 5L }, { "wheel", 3L }, { "figure", 1L }, { "group", "G" },
            });
            TacaiFixtures.Check("hash.key_order_invariant", a.Hash(), b.Hash());
            TacaiFixtures.Check("hash.stable_repeat", a.Hash(), a.Hash());

            a.Append("ThreatSpend", 3, new Dictionary<string, object> { { "balance", 9L } });
            TacaiFixtures.Check("hash.changes_on_append", false, a.Hash() == b.Hash());
        }

        // leak guard: hidden information cannot enter, even by typo
        [Test]
        public void Payload_Whitelist_FailLoud()
        {
            var feed = new WorldEventFeed("leak");
            bool threw = false;
            try
            {
                feed.Append("HeroWounded", 1, new Dictionary<string, object>
                {
                    { "hero", "Luke" }, { "hand_size", 3L },   // hidden info: forbidden
                });
            }
            catch (ArgumentException) { threw = true; }
            TacaiFixtures.Check("leak.unknown_key_rejected", true, threw);

            threw = false;
            try { feed.Append("MindRead", 1, new Dictionary<string, object>()); }
            catch (ArgumentException) { threw = true; }
            TacaiFixtures.Check("leak.unknown_kind_rejected", true, threw);
        }

        // (d) posture derivation
        [Test]
        public void Posture_From_Mission_Properties()
        {
            TacaiFixtures.Check("posture.destroy", "eliminate",
                WorldEventVector.DerivePosture("Destroy the rebel cell", "Hero", -1));
            TacaiFixtures.Check("posture.defend", "protect",
                WorldEventVector.DerivePosture("Defend the outpost", "Other", -1));
            TacaiFixtures.Check("posture.hold_beats_clock", "protect",
                WorldEventVector.DerivePosture("Hold the gate until the convoy departs", "Rebel", 6));
            TacaiFixtures.Check("posture.clock", "stall",
                WorldEventVector.DerivePosture("Engage the rebels", "Rebel", 8));
            TacaiFixtures.Check("posture.escape", "stall",
                WorldEventVector.DerivePosture("The rebels must reach the shuttle and escape", "Rebel", -1));
            TacaiFixtures.Check("posture.smuggler_is_not_smuggling", "eliminate",
                WorldEventVector.DerivePosture("Hunt the smuggler gang", "Hero", -1));
            TacaiFixtures.Check("posture.stronghold_is_not_hold", "eliminate",
                WorldEventVector.DerivePosture("Storm the rebel stronghold", "Rebel", -1));
        }
    }
}
