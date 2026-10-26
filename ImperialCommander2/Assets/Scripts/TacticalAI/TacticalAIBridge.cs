// M3 integration bridge between the IC2 Unity runtime and TacticalAI.Core
// (concept section 4.2 hook table). EVERY entry point here is called behind
//   if (TacticalAI.TacticalAISettings.Enabled)
// and every hook site keeps its original legacy code as the else/default
// branch. With the settings default (off / "legacy") nothing in this file
// ever executes - the game is bit-compatible with the shipped build.
//
// What is faithful TODAY, inside this repo's runtime model:
//  * round activation ORDER  -> Core OrderPlanner (initiative/elite/deterministic
//    tiebreak, seeded DeterministicRandom, why-strings; never GlowEngine RNG),
//  * per-round order DECK CARD (printed blocks) -> Core OrderDeckManager from
//    the public Resources/TacticalAI/order_decks.json asset,
//  * deployment-point choice -> deterministic (no hidden RNG),
//  * target selection        -> deterministic priority order, no hidden RNG.
//
// Documented deviations (see docs/PR_draft.md): the IC2 Saga map is a
// tilemap of deployment points, NOT a hex grid with figure positions - there
// is no hex world model in the Unity code to snapshot. So the spatial Core
// Focus/belief pipeline is exposed through the WorldProvider seam below and
// "overlord" currently plans like "reference" until a hex adapter registers
// a provider. Posture is fixed to "eliminate" until the mission objective
// mapping lands; group pattern defaults to "aggressive" until the M4 pattern
// classification asset ships. None of these degrade the legacy path.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Tac = TacticalAI.Core;

namespace TacticalAI
{
    /// <summary>Everything the activation popup needs for ONE tactical
    /// activation. Null plan => the popup runs its untouched legacy path.</summary>
    public sealed class ActivationPlanData
    {
        public string DeckCardId;
        public string[] Blocks;         // printed order-card blocks (public text)
        public string TargetName;       // deterministic focus target
        public string TargetWhy;
        public string ExplainLine;      // printed why-string (ExplainLevel)
        public bool HasTarget;
    }

    public static class TacticalAIBridge
    {
        /// <summary>Seam for a future hex-map adapter: when a World provider is
        /// registered, overlord mode runs the full Core belief/focus loop.
        /// Null in this repo state (no hex world model in the Unity runtime).</summary>
        public static Func<Tac.World> WorldProvider;

        static Tac.OrderDeckManager _deckMgr;
        static Dictionary<string, Tac.DeploymentCard> _catalog;
        static int _planRound = -1;
        static List<Tac.OrderSlot> _slots = new List<Tac.OrderSlot>();
        static string _publicLine = "";
        static ulong _sessionSeed;

        public static void ResetSession()
        {
            _deckMgr = null;
            _catalog = null;
            _planRound = -1;
            _slots = new List<Tac.OrderSlot>();
            _publicLine = "";
            _sessionSeed = 0;
        }

        // ---------------------------------------------------------------- plan

        /// <summary>Round planner, called from the IncreaseRound hook (the
        /// nearest existing equivalent of the concept's deployment_finished
        /// event trigger - see PR draft deviation note) and lazily before any
        /// activation. Rebuilds order + deals EVERY living group's card at
        /// round start (concept 3.4.1: deal-all-first, never at activation).</summary>
        public static void OnNewRound(int roundNo)
        {
            if (!TacticalAISettings.Enabled) return;   // belt and braces
            EnsureRound(roundNo);
            // post_hoc explain: summary of the round we just played
            if (TacticalAISettings.ExplainLevel == "post_hoc" && roundNo > 1
                && !string.IsNullOrEmpty(_publicLine))
            {
                var qm = GlowEngine.FindUnityObject<QuickMessage>();
                if (qm != null) qm.Show(_publicLine);
            }
        }

        static void EnsureRound(int roundNo)
        {
            if (_planRound == roundNo && _deckMgr != null) return;
            BuildGroups(out var facts, out var figures);
            if (facts.Count == 0)
            {
                _planRound = roundNo;
                _slots = new List<Tac.OrderSlot>();
                _publicLine = "";
                return;
            }

            var mode = EffectiveMissionMode();
            var posture = "eliminate";                 // TODO(world adapter): mission posture
            var skill = Tac.SkillProfile.For(TacticalAISettings.SkillLevel);
            // NOTE(overlord): full belief/doctrine needs the hex World adapter
            // (WorldProvider seam). Until it registers, overlord plans like the
            // reference level; the difference is only logged, never hidden.
            if (mode == "overlord" && WorldProvider == null)
                Debug.Log("TacticalAIBridge::overlord without WorldProvider - planning at reference ruleset");

            var rng = new Tac.DeterministicRandom(SessionSeed() ^ (ulong)roundNo * 0x9E3779B97F4A7C15UL);
            var plan = Tac.OrderPlanner.PlanRound(roundNo, facts, _catalog, posture, skill, rng,
                                                  roundNo, null, null, null);
            Tac.OrderPlanner.AssertPermutation(plan, facts.Select(f => f.GroupId));
            _slots = plan.Slots;
            var names = facts.ToDictionary(f => f.GroupId, f => f.Name);
            _publicLine = plan.PublicLine(names);
            _planRound = roundNo;

            // deck discipline: reap what died before activating, then deal
            // EVERY living group's card for this round (canonical figure order)
            EnsureDecks();
            if (_deckMgr != null)
            {
                _deckMgr.ReapUnused(roundNo);
                _deckMgr.DrawRoundAll(roundNo, figures, (int)(SessionSeed() & 0x7FFFFFFF));
            }
        }

        // ------------------------------------------------------ activation hook

        /// <summary>OnActivateImperial hook: next group in the planned order
        /// instead of the legacy 70%-priority random draw. Returns false (=>
        /// caller keeps legacy RNG path verbatim) when the planned group is
        /// not among the candidates (e.g. custom-imported cards without a
        /// catalog entry, deployed but never planned).</summary>
        public static bool TryPeekNextActivation(List<global::DeploymentCard> candidates,
                                                 out global::DeploymentCard next)
        {
            next = null;
            if (!TacticalAISettings.Enabled || candidates == null || candidates.Count == 0)
                return false;
            EnsureRound(CurrentRound());
            foreach (var slot in _slots)
            {
                var cd = candidates.FirstOrDefault(x => x.id == slot.GroupId);
                if (cd != null) { next = cd; return true; }
            }
            return false;
        }

        /// <summary>Activation popup hook: printed order-deck card blocks +
        /// deterministic target + explain line, replacing the random
        /// instruction option, hidden bonus roll and random hero pick.
        /// False => popup keeps its legacy blocks verbatim.</summary>
        public static bool TryBuildActivationPlan(global::DeploymentCard cd, out ActivationPlanData plan)
        {
            plan = null;
            if (!TacticalAISettings.Enabled || cd == null) return false;
            EnsureRound(CurrentRound());
            EnsureDecks();
            if (_deckMgr == null) return false;    // asset failed -> legacy + error already logged

            if (!_deckMgr.RoundCards.TryGetValue(cd.id, out var card))
            {
                // group joined mid-round: it was not part of the round-start
                // deal; draw its card now (documented reinforcements delta).
                var fig = MakeFigure(cd, Math.Max(0, IndexOf(cd)));
                if (fig != null) _deckMgr.DrawRoundAll(_planRound, new List<Tac.Figure> { fig },
                                                       (int)(SessionSeed() & 0x7FFFFFFF));
                if (!_deckMgr.RoundCards.TryGetValue(cd.id, out card)) return false;
            }
            var blocks = card.Blocks ?? Array.Empty<string>();
            _deckMgr.FinishCard(cd.id);            // activation resolves the dealt card

            plan = new ActivationPlanData { DeckCardId = card.Id, Blocks = blocks };
            if (TrySelectTarget(cd, out var targetName, out var why))
            {
                plan.HasTarget = true;
                plan.TargetName = targetName;
                plan.TargetWhy = why;
            }
            plan.ExplainLine = ExplainFor(cd.id);
            if (plan.ExplainLine != null && plan.HasTarget)
                plan.ExplainLine += " | " + plan.TargetWhy;
            return true;
        }

        // ------------------------------------------------------- deployment hook

        /// <summary>GetActiveDeploymentPoint hook: deterministic choice among
        /// the already-filtered active DPs instead of the uniform random pick
        /// (concept 3.4.2: random DP = exploitable unfairness; the legacy
        /// filter list is reused verbatim). Spatial scoring (objective/zone/
        /// colour) is deferred to the hex adapter - see PR draft. Name-ordinal
        /// then GUID-ordinal keeps the choice stable across reloads.</summary>
        public static bool TryChooseDeploymentPoint(global::DeploymentCard card,
                                                    List<Saga.IMapEntity> dps, out Guid chosen)
        {
            chosen = Guid.Empty;
            if (!TacticalAISettings.Enabled || dps == null || dps.Count == 0) return false;
            var best = dps
                .OrderBy(x => x.name ?? "", StringComparer.Ordinal)
                .ThenBy(x => x.GUID.ToString(), StringComparer.Ordinal)
                .First();
            chosen = best.GUID;
            Debug.Log($"TacticalAIBridge::DP -> {best.name} ({card?.name}) [deterministic, no RNG]");
            return true;
        }

        // ---------------------------------------------------------- targeting

        /// <summary>Deterministic focus WITHOUT the hex board: printed
        /// preferred-target traits first, then original deployment order,
        /// wounded/defeated skipped, NO random draw and no hidden state. The
        /// WorldProvider seam replaces this with the Core FocusResolver as
        /// soon as a hex adapter exists (documented deviation).</summary>
        static bool TrySelectTarget(global::DeploymentCard cd, out string heroName, out string why)
        {
            heroName = null; why = null;
            var heroes = Saga.DataStore.deployedHeroes;
            if (heroes == null || heroes.Count == 0) return false;
            var living = heroes.Where(h => h.heroState == null || !h.heroState.isDefeated).ToList();
            if (living.Count == 0) living = heroes;    // all defeated: legacy would find none either

            global::DeploymentCard pick = null;
            string pickedWhy = "Focus: gedruckte Vorzugziele, dann Aufstellungsreihenfolge (kein Zufall)";
            if (cd.preferredTargets != null && cd.preferredTargets.Length > 0)
            {
                pick = living.FirstOrDefault(h => h.traits != null &&
                    cd.preferredTargets.Any(t => h.traits.Contains(t.ToString())));
                if (pick != null)
                    pickedWhy = "Focus: gedrucktes Vorzugziel " +
                                string.Join("/", cd.preferredTargets.Select(t => t.ToString())) +
                                ", dann Aufstellungsreihenfolge (kein Zufall)";
            }
            if (pick == null) pick = living[0];
            heroName = pick.name;
            why = pickedWhy;
            return true;
        }

        // ------------------------------------------------------------- explain

        static string ExplainFor(string groupId)
        {
            if (TacticalAISettings.ExplainLevel == "none") return null;
            if (TacticalAISettings.ExplainLevel == "full")
            {
                var slot = _slots.FirstOrDefault(s => s.GroupId == groupId);
                if (slot != null) return "Warum jetzt: " + slot.Why + " (Rang " + (slot.Rank + 1) + ")";
            }
            // post_hoc: printed at round end via OnNewRound quick message
            return null;
        }

        static string EffectiveMissionMode()
        {
            string aiMode = null;
            try { aiMode = Saga.DataStore.sagaSessionData?.setupOptions?.aiMode; }
            catch { /* session not up yet */ }
            return TacticalAISettingsUnity.MissionMode(aiMode);
        }

        // ------------------------------------------------------------- plumbing

        static int CurrentRound()
        {
            try { return Math.Max(1, Saga.DataStore.sagaSessionData?.gameVars?.round ?? 1); }
            catch { return 1; }
        }

        static ulong SessionSeed()
        {
            if (_sessionSeed != 0) return _sessionSeed;
            var key = Saga.RunningCampaign.sagaCampaignGUID + "|" +
                      (Saga.DataStore.sagaSessionData?.setupOptions?.projectItem?.missionID ?? "solo");
            ulong h = 1469598103934665603UL;
            foreach (var c in key) { h ^= c; h *= 1099511628211UL; }
            if (h == 0) h = 0xA5A5A5A5A5A5A5A5UL;
            _sessionSeed = h;
            return h;
        }

        static void EnsureDecks()
        {
            if (_deckMgr != null) return;
            var root = TacticalAIAssets.OrderDecksRoot;
            if (root == null) return;
            try { _deckMgr = Tac.OrderDeckManager.Load(root); }
            catch (Exception e)
            {
                Debug.LogError("TacticalAIBridge::order_decks load failed: " + e.Message);
                _deckMgr = null;
            }
        }

        static void EnsureCatalog()
        {
            if (_catalog != null) return;
            _catalog = new Dictionary<string, Tac.DeploymentCard>();
            foreach (var cd in Saga.DataStore.deploymentCards)
            {
                if (cd.characterType != Saga.CharacterType.Imperial || cd.isDummy) continue;
                if (_catalog.ContainsKey(cd.id)) continue;
                try { _catalog[cd.id] = Tac.DeploymentCard.FromRecord(ToRecord(cd)); }
                catch (Exception e) { Debug.LogWarning("TacticalAIBridge::catalog skip " + cd.id + ": " + e.Message); }
            }
        }

        /// <summary>Only the PRINTED card values go through (fairness
        /// whitelist Core DeploymentCard.CardFieldWhitelist); no bonus data.</summary>
        static Dictionary<string, object> ToRecord(global::DeploymentCard cd)
        {
            return new Dictionary<string, object>
            {
                { "id", cd.id },
                { "name", cd.name },
                { "subname", cd.subname ?? "" },
                { "miniSize", cd.miniSize.ToString() },
                { "speed", cd.speed },
                { "health", cd.health },
                { "attackType", cd.attackType.ToString() },
                { "attacks", (cd.attacks ?? new DiceColor[0]).Select(a => a.ToString()).ToArray() },
                { "keywords", cd.keywords ?? new string[0] },
                { "isElite", cd.isElite },
                { "defense", (cd.defense ?? new DiceColor[0]).Select(a => a.ToString()).ToArray() },
                { "surges", cd.surges ?? new string[0] },
                { "cost", cd.cost },
                { "characterType", "Imperial" },
            };
        }

        /// <summary>Group facts + Core figures (round-deal order) from the
        /// deployed enemy list. One GroupFacts per group id (= card id), the
        /// same identity the legacy group manager uses.</summary>
        static void BuildGroups(out List<Tac.GroupFacts> facts, out List<Tac.Figure> figures)
        {
            EnsureCatalog();
            facts = new List<Tac.GroupFacts>();
            figures = new List<Tac.Figure>();
            var seen = new HashSet<string>();
            int idx = 0;
            foreach (var cd in Saga.DataStore.deployedEnemies)
            {
                if (cd == null || cd.isDummy) continue;
                if (cd.currentSize <= 0) continue;             // wiped group
                if (!seen.Add(cd.id)) continue;                // first instance = the group
                if (!_catalog.TryGetValue(cd.id, out var core)) continue;  // unknown card: legacy fallback
                facts.Add(new Tac.GroupFacts(
                    cd.id, idx, cd.id, cd.name,
                    pattern: "aggressive",                     // TODO(M4): printed-trait classification
                    nAlive: Math.Max(1, cd.currentSize),
                    vitalityFrac: Tac.Rational.One,            // TODO(world adapter): wheel state
                    zone: null, arrivedRound: null,
                    isHeavy: core.IsLargeBase, isSwarm: false));
                var fig = new Tac.Figure
                {
                    Id = cd.id, GroupId = cd.id, Card = core, Team = "imperial",
                    Anchor = new Tac.Cell(0, 0), Orientation = 0,
                    Vitality = core.Health, Index = idx, Alive = true, Pattern = "aggressive",
                };
                figures.Add(fig);
                idx++;
            }
        }

        static Tac.Figure MakeFigure(global::DeploymentCard cd, int index)
        {
            EnsureCatalog();
            if (cd == null || !_catalog.TryGetValue(cd.id, out var core)) return null;
            return new Tac.Figure
            {
                Id = cd.id, GroupId = cd.id, Card = core, Team = "imperial",
                Anchor = new Tac.Cell(0, 0), Orientation = 0,
                Vitality = core.Health, Index = Math.Max(0, index), Alive = true, Pattern = "aggressive",
            };
        }

        static int IndexOf(global::DeploymentCard cd)
        {
            var list = Saga.DataStore.deployedEnemies;
            for (int i = 0; i < list.Count; i++) if (list[i] == cd) return i;
            return -1;
        }
    }
}
