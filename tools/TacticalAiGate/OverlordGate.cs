// M6 overlord live gate (Core-only, mirrors the TacticalAIBridge overlord
// layer without Unity): feed -> projection -> focus candidates -> Overlord
// ranking + threat-economy mirror + risk positions. Same core calls the
// bridge performs on every overlord round with a live feed.
using System;
using System.Collections.Generic;
using System.Linq;
using TacticalAI.Core;

public static class OverlordGate
{
    static int fails = 0;
    static void Eq(object expected, object actual, string what)
    {
        bool ok = Equals(expected, actual);
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what +
                          " | expected=" + expected + " actual=" + actual);
        if (!ok) { fails++; Environment.ExitCode = 1; }
    }

    static readonly string App =
        "/home/open/work/ia-ai/ImperialCommander2/ImperialCommander2";

    static Dictionary<string, object> P(params object[] kv)
    {
        var d = new Dictionary<string, object>();
        for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
        return d;
    }

    static List<WorldEvent> Feed(string mission)
    {
        var f = new List<WorldEvent>();
        int seq = 1;
        f.Add(new WorldEvent("MissionStart", 1, P(
            "objective", "Eliminate the rebel leaders", "priorityTarget", "Rebel",
            "roundLimit", -1L, "mission", mission, "missionType", "Story")) { Seq = seq++ });
        f.Add(new WorldEvent("HeroActivated", 2, P("hero", "Han Solo")) { Seq = seq++ });
        f.Add(new WorldEvent("HeroActivated", 2, P("hero", "Gaarkhan")) { Seq = seq++ });
        f.Add(new WorldEvent("HeroWounded", 2, P("hero", "Gaarkhan")) { Seq = seq++ });
        f.Add(new WorldEvent("HeroActivated", 3, P("hero", "Han Solo")) { Seq = seq++ });
        f.Add(new WorldEvent("FocusChange", 3, P("focus", "Han Solo")) { Seq = seq++ });
        return f;
    }

    public static void Run()
    {
        Console.WriteLine("== ORD overlord gate ==");

        // curated public table primed from the shipped asset (bridge seam)
        var doc = Json.AsDict(Json.Parse(System.IO.File.ReadAllText(App +
            "/Assets/Resources/Languages/En/tactical_hero_profiles.json")));
        HeroProfiles.PrimeDoc(doc);
        var byName = HeroProfiles.ProfilesFromDoc(doc);

        var view = WorldEventVector.Project(Feed("ord1"), "gate|ord1");
        Eq("eliminate", view.Posture, "ORD::posture");

        // --- focus fire (bridge ComputeRoundFocus/BuildFocusCandidates path)
        List<FocusPick> Rank(FeedWorldView v)
        {
            var cands = new List<FocusCandidate>();
            int row = 0;
            foreach (var h in v.Heroes)
            {
                HeroThreatProfile p;
                byName.TryGetValue(h.HeroId, out p);
                cands.Add(new FocusCandidate
                {
                    HeroId = h.HeroId, SquadRow = row++, Profile = p,
                    Wounded = h.Wounded, Marks = h.Marks, MarkedRound = h.MarkedRound,
                    AttackDice = 0, Health = 0,
                });
            }
            return OverlordFocus.Rank(cands, v.Posture, null);
        }

        var rank = Rank(view);
        Eq("Han Solo", rank[0].HeroId, "ORD::focus_mark_first");
        Eq(true, rank[0].Marked, "ORD::focus_marked_flag");
        Eq(rank.Select(r => r.HeroId).OrderBy(x => x, StringComparer.Ordinal)
              .SequenceEqual(view.Heroes.Select(h => h.HeroId)
                             .OrderBy(x => x, StringComparer.Ordinal)),
           true, "ORD::permutation_only_identical_legal_set");

        // determinism: 2x replay bit-identical
        var r1 = string.Join(";", Rank(view).Select(x => JsonNorm(x.AsDict())));
        var r2 = string.Join(";", Rank(view).Select(x => JsonNorm(x.AsDict())));
        Eq(r1, r2, "ORD::determinism_2x");

        // feed isolation: equivalent history (event order swapped) projects
        // and ranks identically - nothing beyond the feed can be seen
        var swap = Feed("ord1");
        var t = swap[3]; swap[3] = swap[2]; swap[2] = t;   // Gaarkhan: wound<->activation
        for (int i = 0; i < swap.Count; i++) swap[i] = new WorldEvent(   // re-number in
            swap[i].Kind, swap[i].Round, swap[i].Payload) { Seq = i + 1 };// placement order
        var vB = WorldEventVector.Project(swap, "gate|ord1");
        Eq(JsonNorm(view.Canon()), JsonNorm(vB.Canon()), "ORD::isolation_canon");
        Eq(r1, string.Join(";", Rank(vB).Select(x => JsonNorm(x.AsDict()))),
           "ORD::isolation_rank");

        // --- threat-economy mirror (bridge EnsureThreatMirror path, S5 levers)
        var s5 = SkillProfile.For("S5");
        var led = new ThreatLedger(s5.EconSave, s5.EconIdle);
        Eq(s5.EconSave, led.SaveRounds, "ORD::ledger_save_lever_from_profile");
        for (int r = 1; r <= 3; r++) led.Tick(r, 2);
        var led2 = new ThreatLedger(s5.EconSave, s5.EconIdle);
        for (int r = 1; r <= 3; r++) led2.Tick(r, 2);
        Eq(JsonNorm(led.AsDict()), JsonNorm(led2.AsDict()), "ORD::ledger_determinism");
        Eq(true, led.Balance > 0, "ORD::ledger_earns");

        // --- risk positions (bridge BuildRiskPositions path, laws pinned)
        var press = RiskPosture.RiskPosition(new RiskFacts
        {
            GroupId = "G0", Counter = "woundAll", Vitality = Rational.One,
        }, s5.Rho);
        Eq("press", press.Position, "ORD::risk_woundall_press");
        var blocker = RiskPosture.RiskPosition(new RiskFacts
        {
            GroupId = "G1", Posture = "stall", Vitality = new Rational(1, 4),
            VictoryBlocker = true,
        }, s5.Rho);
        Eq("press", blocker.Position, "ORD::risk_victoryblocker_press");
        var rho0 = RiskPosture.RiskPosition(new RiskFacts
        {
            GroupId = "G2", Vitality = Rational.One,
        }, 0);
        Eq("press", rho0.Position, "ORD::risk_rho0_press");

        Console.WriteLine(fails == 0 ? "== ORD ALL PASS ==" : "== ORD FAILS=" + fails + " ==");
        if (fails != 0) Environment.ExitCode = 1;
    }

    // deterministic canonical text (sorted keys) - MiniJson has no writer
    static string JsonNorm(object v)
    {
        var d = v as Dictionary<string, object>;
        if (d != null)
            return "{" + string.Join(",", d.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => "\"" + x.Key + "\":" + JsonNorm(x.Value))) + "}";
        var l = v as List<object>;
        if (l != null) return "[" + string.Join(",", l.Select(JsonNorm)) + "]";
        if (v == null) return "null";
        if (v is string) return "\"" + v + "\"";
        return v.ToString();
    }
}
