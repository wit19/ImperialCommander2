// M8/F2 regression (findings_m56_qa_2026-10-27): per-mission OFF badge with
// the global switch ON must keep BOTH activation hooks fully legacy.
// TryPeekNextActivation and TryBuildActivationPlan used to gate on
// TacticalAISettings.Enabled only, so a mission whose frozen badge says
// "off" still got planner order + popup plan (silent contract violation at
// the user's escape hatch). The gate is ActiveNow() - same semantics the
// OnNewRound/TryChooseDeploymentPoint hooks already used.
// Repo convention (TacaiFixtures header): no Unity test framework is wired
// today, so [Explicit]; the live gate in tools/TacticalAiGate mirrors the
// settings half of this proof, this file pins the hook half.
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace TacticalAI.Tests
{
    // The shipping game declares a global `DeploymentCard`; inside this
    // namespace the alias keeps the harness' global type explicit (the hooks
    // take List<global::DeploymentCard>). The Saga types need their
    // qualification (TacticalAI.Tests would otherwise sit closer than Saga).
    using GameCard = DeploymentCard;
    using SagaSession = Saga.SagaSession;
    using SagaSetupOptions = Saga.SagaSetupOptions;
    using ProjectItem = Saga.ProjectItem;

    [Explicit("Requires Test Framework")]
    [TestFixture]
    public class MissionOverrideHookTests
    {
        static readonly Guid CampA = new Guid("11111111-1111-1111-1111-111111111111");
        static readonly Guid CampB = new Guid("22222222-2222-2222-2222-222222222222");

        Saga.SagaSession _savedSession;
        Guid _savedCampaign;
        bool _savedEnabled;

        [SetUp]
        public void SetUp()
        {
            _savedSession = DataStore.sagaSessionData;
            _savedCampaign = Saga.RunningCampaign.sagaCampaignGUID;
            _savedEnabled = TacticalAI.TacticalAISettings.Enabled;
            TacticalAIBridge.ResetSession();
            TacticalAI.TacticalAISettings.ResetDefaults();
            TacticalAI.TacticalAISettings.Enabled = true;   // GLOBAL on
        }

        [TearDown]
        public void TearDown()
        {
            TacticalAIBridge.ResetSession();
            DataStore.sagaSessionData = _savedSession;
            Saga.RunningCampaign.sagaCampaignGUID = _savedCampaign;
            TacticalAI.TacticalAISettings.ResetDefaults();
            TacticalAI.TacticalAISettings.Enabled = _savedEnabled;
        }

        static void Mission(string aiMode, Guid campaign)
        {
            Saga.RunningCampaign.sagaCampaignGUID = campaign;
            DataStore.sagaSessionData = new SagaSession(new SagaSetupOptions
            {
                aiMode = aiMode,
                projectItem = new ProjectItem { missionID = "m8_f2_probe" },
            });
        }

        // planner-untouched witness: _planRound stays at its ResetSession
        // value (-1) when the hook never reached EnsureRound => the caller
        // keeps the legacy RNG path verbatim, no plan state is touched.
        static int PlanRoundProbe()
        {
            var f = typeof(TacticalAIBridge).GetField("_planRound",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "bridge internal _planRound vanished - update probe");
            return (int)f.GetValue(null);
        }

        [Test]
        public void MissionOverrideOff_BothHooks_False_And_LegacyPathUntouched()
        {
            Mission("off", CampA);                       // badge OFF, global ON
            Assert.IsFalse(TacticalAIBridge.TacticalMissionActive(),
                "ActiveNow must be false for a frozen 'off' badge");

            var candidates = new List<GameCard>
            {
                new GameCard { id = "DG001", name = "Stormtroopers", size = 2 },
            };
            GameCard next;
            Assert.IsFalse(TacticalAIBridge.TryPeekNextActivation(candidates, out next),
                "peek must not order a group in an OFF mission");
            Assert.IsNull(next);

            TacticalAI.ActivationPlanData plan;
            Assert.IsFalse(TacticalAIBridge.TryBuildActivationPlan(candidates[0], out plan),
                "popup must not plan in an OFF mission");
            Assert.IsNull(plan);

            Assert.AreEqual(-1, PlanRoundProbe(),
                "OFF-mission hook calls must not touch planner state (legacy path verbatim)");
        }

        [Test]
        public void MissionOverrideAuto_ActiveNow_True_GlobalStillGates()
        {
            Mission("", CampB);                          // AUTO badge, global ON
            Assert.IsTrue(TacticalAIBridge.TacticalMissionActive(),
                "AUTO badge with global ON must run the tactical path");

            TacticalAI.TacticalAISettings.Enabled = false;   // global OFF
            Assert.IsFalse(TacticalAIBridge.TacticalMissionActive(),
                "global OFF must win over any badge");
        }
    }
}
