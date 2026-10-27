using System;
using System.Collections.Generic;
using TacticalAI.Core;

public static class WorldFeedGate
{
    static int fails = 0;
    static void Eq(object expected, object actual, string what)
    {
        bool ok = Equals(expected, actual);
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what + " | expected=" + expected + " actual=" + actual);
        if (!ok) fails++;
    }

    public static void Run()
    {
        // (b) seq gap -> resync
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
        var hole = new List<WorldEvent> { full[0], full[2] };
        Eq(2, WorldEventFeed.VerifySeq(hole), "verifyseq.gap_at");
        bool threw = false;
        try { WorldEventVector.Project(hole); } catch (InvalidOperationException) { threw = true; }
        Eq(true, threw, "project.rejects_gap");
        feed.Resync(full);
        Eq(null, WorldEventFeed.VerifySeq(feed.Events), "resync.seq_contiguous");
        Eq(fullHash, feed.Hash(), "resync.hash_restored");
        Eq(1L, (long)feed.ResyncCount, "resync.counted");
        var view = WorldEventVector.Project(feed.Events, "gapcase", feed.Hash());
        Eq("1/4", view.FindGroup("DG001").VitalityFrac.ToString(), "resync.view_vitality");
        threw = false;
        try { feed.Resync(new List<WorldEvent> { full[0], full[2] }); }
        catch (InvalidOperationException) { threw = true; }
        Eq(true, threw, "resync.rejects_gapped_relog");

        // (c) hash stability + key order
        var a = new WorldEventFeed("k");
        a.Append("GroupDamage", 2, new Dictionary<string, object>
            { { "group", "G" }, { "figure", 1L }, { "wheel", 3L }, { "health", 5L } });
        var b = new WorldEventFeed("k");
        b.Append("GroupDamage", 2, new Dictionary<string, object>
            { { "health", 5L }, { "wheel", 3L }, { "figure", 1L }, { "group", "G" } });
        Eq(a.Hash(), b.Hash(), "hash.key_order_invariant");
        Eq(a.Hash(), a.Hash(), "hash.stable_repeat");

        // (d) posture
        Eq("eliminate", WorldEventVector.DerivePosture("Destroy the rebel cell", "Hero", -1), "posture.destroy");
        Eq("protect", WorldEventVector.DerivePosture("Defend the outpost", "Other", -1), "posture.defend");
        Eq("protect", WorldEventVector.DerivePosture("Hold the gate until the convoy departs", "Rebel", 6), "posture.hold_beats_clock");
        Eq("stall", WorldEventVector.DerivePosture("Engage the rebels", "Rebel", 8), "posture.clock");
        Eq("stall", WorldEventVector.DerivePosture("The rebels must reach the shuttle and escape", "Rebel", -1), "posture.escape");
        Eq("eliminate", WorldEventVector.DerivePosture("Hunt the smuggler gang", "Hero", -1), "posture.smuggler");
        Eq("eliminate", WorldEventVector.DerivePosture("Storm the rebel stronghold", "Rebel", -1), "posture.stronghold_not_hold");

        // leak guard
        threw = false;
        try
        {
            var f2 = new WorldEventFeed("leak");
            f2.Append("HeroWounded", 1, new Dictionary<string, object> { { "hero", "Luke" }, { "hand_size", 3L } });
        }
        catch (ArgumentException) { threw = true; }
        Eq(true, threw, "leak.unknown_key_rejected");
        threw = false;
        try { feed.Append("MindRead", 1, new Dictionary<string, object>()); }
        catch (ArgumentException) { threw = true; }
        Eq(true, threw, "leak.unknown_kind_rejected");

        // (a) replay bit-identical incl. OrderPlanner decisions
        var script = new List<WorldEvent>(full);
        var fa = new WorldEventFeed("m");
        var fb = new WorldEventFeed("m");
        foreach (var e in script) fa.Append(e.Kind, e.Round, e.Payload);
        foreach (var e in script) fb.Append(e.Kind, e.Round, e.Payload);
        var va = WorldEventVector.Project(fa.Events, "m", fa.Hash());
        var vb = WorldEventVector.Project(fb.Events, "m", fb.Hash());
        Eq(va.Canon(), vb.Canon(), "replay.canon");
        var names = new Dictionary<string, string>();
        var groups = new List<GroupFacts>();
        int idx = 0;
        foreach (var g in va.Groups)
        {
            groups.Add(new GroupFacts(g.GroupId, idx++, g.GroupId, g.Name, "aggressive",
                Math.Max(1, g.Alive), g.VitalityFrac));
            names[g.GroupId] = g.Name;
        }
        var cat = TacaiGateCatalog();
        var p1 = OrderPlanner.PlanRound(3, groups, cat, va.Posture, null,
            new DeterministicRandom(7), 1, null, null, null);
        var p2 = OrderPlanner.PlanRound(3, groups, cat, vb.Posture, null,
            new DeterministicRandom(7), 1, null, null, null);
        Eq(p1.PublicLine(names), p2.PublicLine(names), "replay.plan_publicline");

        // M8/F5: the mission clock is LIVE public info - RoundLimitChanged
        // re-derives the posture (start snapshot alone went stale); missing
        // required keys fail loud like every other shape violation.
        var clockOff = new List<WorldEvent>
        {
            new WorldEvent("MissionStart", 1, new Dictionary<string, object>
            { { "objective", "Engage the rebels" }, { "priorityTarget", "Rebel" },
              { "roundLimit", -1L }, { "mission", "gate" } }) { Seq = 1 },
            new WorldEvent("RoundLimitChanged", 2, new Dictionary<string, object>
            { { "roundLimit", 8L } }) { Seq = 2 },
        };
        Eq("stall", WorldEventVector.Project(clockOff).Posture, "m8.clock_on_gives_stall");
        clockOff[1] = new WorldEvent("RoundLimitChanged", 3, new Dictionary<string, object>
            { { "roundLimit", -1L } }) { Seq = 2 };
        Eq("eliminate", WorldEventVector.Project(clockOff).Posture, "m8.clock_off_clears_stall");
        threw = false;
        try { new WorldEventFeed("k").Append("RoundLimitChanged", 1, null); }
        catch (ArgumentException) { threw = true; }
        Eq(true, threw, "m8.roundlimitchanged_requires_key");

        // M8/F1: the logger seam - the RAW card name is the last color span
        // of the localized rich-text LogItem.message (DE+EN here; the full
        // label matrix lives in the logger_extract.json harness vectors).
        Eq("IG-88 Death Cube", MissionLogText.ExtractRawName(
            "<b><color=red>Gruppe besiegt:</color></b> <color=orange>IG-88 Death Cube</color>\n\n"),
            "m8.logger_extract_de");
        Eq("Sturmtrupp: Staffel 2", MissionLogText.ExtractRawName(
            "<b><color=red>Gruppe entfernt:</color></b> <color=orange>Sturmtrupp: Staffel 2</color>\n\n"),
            "m8.logger_extract_colon_safe");
        Eq(null, MissionLogText.ExtractRawName("no rich text at all"), "m8.logger_extract_plain_null");

        Console.WriteLine(fails == 0 ? "WORLDGATE ALL PASS" : "WORLDGATE FAILS=" + fails);
        if (fails != 0) Environment.ExitCode = 1;
    }

    static Dictionary<string, DeploymentCard> TacaiGateCatalog()
    {
        // canonical catalog fixture (same source as the console harness)
        return DeploymentCard.LoadCatalog(Json.Parse(System.IO.File.ReadAllText(
            "/home/open/work/ia-ai/ImperialCommander2/ImperialCommander2/Assets/Tests/Fixtures/tacai/units_core.json")));
    }
}
