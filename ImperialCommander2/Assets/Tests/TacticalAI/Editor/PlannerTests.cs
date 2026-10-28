// OrderPlanner permutation invariance on fixture worlds, the LegacyPlanner
// pass-through (bit-compatibility anchor) and the Default-OFF settings gate.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TacticalAI.Core;

namespace TacticalAI.Tests
{
    [Explicit("Requires Test Framework")]
    [TestFixture]
    public class OrderPlannerTests
    {
        static List<GroupFacts> MakeGroups(int n)
        {
            var pats = Patterns.PatternsList;
            var groups = new List<GroupFacts>();
            for (int gi = 0; gi < n; gi++)
                groups.Add(new GroupFacts(
                    "G" + gi, gi,
                    gi % 2 == 0 ? "DG021" : "DG009",
                    "Group " + gi, pats[gi % pats.Length], 2,
                    gi == 0 ? Rational.One : new Rational(3, 4),
                    gi < 3 ? "Z1" : "Z2",
                    gi == 4 ? 3 : (int?)null,
                    gi == 2, gi % 2 == 1));
            return groups;
        }

        [Test]
        public void PlanRound_Is_A_Permutation_For_Every_Posture_And_Skill()
        {
            var catalog = TacaiFixtures.Catalog();
            foreach (var posture in new[] { "eliminate", "stall", "protect", "deny" })
                foreach (var skill in new string[] { null, "S1", "S3", "S5" })
                {
                    var groups = MakeGroups(Patterns.PatternsList.Length);
                    var plan = OrderPlanner.PlanRound(3, groups, catalog, posture,
                        skill == null ? null : SkillProfile.For(skill),
                        new DeterministicRandom(7), 1, new[] { "Z2" }, "Z1", "woundAll");
                    // throws AssertionError if not an exact permutation:
                    OrderPlanner.AssertPermutation(plan, groups.Select(g => g.GroupId));
                    Assert.AreEqual(groups.Count, plan.Slots.Count);
                    CollectionAssert.AreEquivalent(
                        groups.Select(g => g.GroupId), plan.GroupIds());
                }
        }

        [Test]
        public void PlanRound_Permutation_Invariant_Across_Rng_Seeds()
        {
            // the order-plan law is deterministic: any two seeds must give the
            // SAME activation permutation (seeds only feed S3/S5 rank noise,
            // which can never reorder living groups - pinned per golden row)
            var catalog = TacaiFixtures.Catalog();
            var a = OrderPlanner.PlanRound(3, MakeGroups(6), catalog, "eliminate", null,
                new DeterministicRandom(7), 1, null, null, null);
            var b = OrderPlanner.PlanRound(3, MakeGroups(6), catalog, "eliminate", null,
                new DeterministicRandom(4242), 1, null, null, null);
            OrderPlanner.AssertPermutation(a, a.GroupIds());
            OrderPlanner.AssertPermutation(b, b.GroupIds());
            Assert.AreEqual(a.GroupIds(), b.GroupIds());
        }

        [Test]
        public void First_Golden_Plan_Rows_ByteExact()
        {
            // same construction as harness SecOrderPlan cases[0] (posture
            // eliminate / skill null / seed 7): order + public line verbatim.
            var catalog = TacaiFixtures.Catalog();
            var v = Json.AsDict(TacaiFixtures.Load("order_plan.json"));
            var exp = Json.AsDict(Json.AsList(v["cases"])[0]);
            Assert.AreEqual("eliminate", Json.AsStr(exp["posture"]));

            var groups = MakeGroups(Patterns.PatternsList.Length);
            var plan = OrderPlanner.PlanRound(3, groups, catalog, "eliminate", null,
                new DeterministicRandom(7), 1, new[] { "Z2" }, "Z1", "woundAll");
            OrderPlanner.AssertPermutation(plan, groups.Select(g => g.GroupId));
            var names = groups.ToDictionary(g => g.GroupId, g => g.Name);
            TacaiFixtures.Check("order_plan.cases[0].plan", exp["plan"], plan.AsDict());
            TacaiFixtures.Check("order_plan.cases[0].public", exp["public"], plan.PublicLine(names));
        }
    }

    [Explicit("Requires Test Framework")]
    [TestFixture]
    public class LegacyPlannerTests
    {
        sealed class Snap : IBoardSnapshot
        {
            public Snap(Board b, List<Figure> f) { Board = b; Figures = f; }
            public Board Board { get; }
            public IReadOnlyList<Figure> Figures { get; }
        }

        [Test]
        public void LegacyPlanner_Passes_Imperial_Living_Order_Untouched()
        {
            var card = TacaiFixtures.Catalog()["DG001"];
            var figs = new List<Figure>
            {
                new Figure { Id = "i1", GroupId = "i1", Card = card, Team = "imperial", Alive = true },
                new Figure { Id = "r1", GroupId = "r1", Card = card, Team = "rebel",    Alive = true },
                new Figure { Id = "i2", GroupId = "i2", Card = card, Team = "imperial", Alive = true },
                new Figure { Id = "i3", GroupId = "i3", Card = card, Team = "imperial", Alive = false },
            };
            var snap = new Snap(null, figs);
            var plan = new LegacyPlanner();
            Assert.AreEqual("legacy", plan.ModeName);
            var order = plan.PlanRound(snap, 1);
            CollectionAssert.AreEqual(new[] { "i1", "i2" }, order,
                "legacy identity: living imperial ids, input order, dead and rebels excluded");
        }

        [Test]
        public void LegacyPlanner_Null_Board_Is_Empty_Not_Crash()
        {
            var order = new LegacyPlanner().PlanRound(null, 1);
            Assert.IsNotNull(order);
            Assert.IsEmpty(order);
        }
    }

    [Explicit("Requires Test Framework")]
    [TestFixture]
    public class SettingsGateTests
    {
        [Test]
        public void Defaults_Are_Off_And_Legacy()
        {
            TacticalAISettings.ResetDefaults();
            Assert.IsFalse(TacticalAISettings.Enabled, "Default MUST be off");
            Assert.AreEqual("reference", TacticalAISettings.Mode,
                "dev 20: mode is DERIVED from the ladder (default S1 -> reference)");
            // M13g: the deployment-mode switch is gone (deployments are
            // always open, Thomas 28.10); ResetDefaults must also
            // neutralise stale save state cleanly.
            Assert.AreEqual("post_hoc", TacticalAISettings.ExplainLevel);
            Assert.IsFalse(TacticalAISettings.IsTactical("reference"),
                "no tactical behaviour while the global switch is off");
            Assert.AreEqual("legacy", TacticalAISettings.EffectiveMode("overlord"),
                "mission override cannot out-run the global switch");
        }

        [Test]
        public void Mode_Values_Validate_And_Effective_Mode_Respects_Override()
        {
            TacticalAISettings.ResetDefaults();
            TacticalAISettings.Enabled = true;
            // dev 20 (option A, Thomas): single difficulty dial. The ladder
            // derives the procedural mode; only tests/console may override.
            TacticalAISettings.SkillLevel = "S3";
            Assert.AreEqual("reference", TacticalAISettings.Mode, "Veteran = book execution");
            TacticalAISettings.SkillLevel = "S4";
            Assert.AreEqual("overlord", TacticalAISettings.Mode, "Kommandant = optimiser");
            Assert.AreEqual("overlord", TacticalAISettings.EffectiveMode("bogus"),
                "unknown mission values fall back to the derived mode, never crash");
            TacticalAISettings.SetModeOverride("bogus");
            Assert.AreEqual("overlord", TacticalAISettings.Mode, "bogus override ignored, derivation stands");
            TacticalAISettings.SetModeOverride("reference");
            Assert.AreEqual("reference", TacticalAISettings.Mode, "console override wins");
            TacticalAISettings.SetModeOverride(null);
            Assert.AreEqual("overlord", TacticalAISettings.Mode, "null override restores derivation");
            Assert.AreEqual("legacy", TacticalAISettings.EffectiveMode("legacy"),
                "per-mission badge OFF survives the ladder");

            TacticalAISettings.ResetDefaults();
        }

        [Test]
        public void Mission_Badge_Cycles_The_Ladder_Dev21()
        {
            TacticalAISettings.ResetDefaults();
            TacticalAISettings.Enabled = true;
            TacticalAISettings.SkillLevel = "S3";
            // AUTO -> OFF -> S1..S5 -> AUTO
            string t = "";
            var seen = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 8; i++) { t = TacticalAISettings.NextMissionToken(t); seen.Add(t); }
            Assert.AreEqual("off", seen[0]);
            Assert.AreEqual("S1", seen[1]);
            Assert.AreEqual("S5", seen[5]);
            Assert.AreEqual("", seen[6], "S5 wraps to AUTO");
            // semantics
            Assert.AreEqual("legacy", TacticalAISettings.EffectiveMode("off"), "OFF badge = legacy play");
            Assert.AreEqual("reference", TacticalAISettings.EffectiveMode("S2"), "mission rung derives mode");
            Assert.AreEqual("overlord", TacticalAISettings.EffectiveMode("S5"), "Thrawn rung = optimiser");
            Assert.AreEqual("S2", TacticalAISettings.EffectiveSkill("S2"), "rung pins skill");
            Assert.AreEqual("S3", TacticalAISettings.EffectiveSkill(""), "AUTO follows global ladder");
            Assert.AreEqual("S3", TacticalAISettings.EffectiveSkill("legacy"), "OFF uses global skill (irrelevant, legacy plays)");
            // deprecated tokens stay playable (old saves), never crash
            Assert.AreEqual("overlord", TacticalAISettings.EffectiveMode("overlord"));
            Assert.AreEqual("reference", TacticalAISettings.EffectiveMode("reference"));

            TacticalAISettings.ResetDefaults();
        }

        [Test]
        public void Corrupt_Save_Data_Cannot_Enable_The_Mode()
        {
            TacticalAISettings.ResetDefaults();
            TacticalAISettings.SetSaveHook(_ => "{not json at all");
            TacticalAISettings.Load();
            Assert.IsFalse(TacticalAISettings.Enabled);
            TacticalAISettings.SetSaveHook(null);
            TacticalAISettings.ResetDefaults();
        }
    }
}
