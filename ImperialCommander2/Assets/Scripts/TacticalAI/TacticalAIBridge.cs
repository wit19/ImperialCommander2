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
        public string Pattern;          // doctrine pattern (dev 11); null = aggressive
    }

    public static class TacticalAIBridge
    {
        /// <summary>Seam for a future hex-map adapter: when a World provider is
        /// registered, overlord mode runs the full Core belief/focus loop.
        /// Null in this repo state (no hex world model in the Unity runtime).</summary>
        public static Func<Tac.World> WorldProvider;

        static Tac.OrderDeckManager _deckMgr;
        static Dictionary<string, Tac.DeploymentCard> _catalog;
        // dev 11: card id -> doctrine pattern for the CURRENT round (fed from
        // the Core Figure records; 'aggressive' everywhere until M4 assigns
        // per-mission roles - the renderer is ready for all six patterns).
        static Dictionary<string, string> _patternByCard;
        static int _planRound = -1;
        static List<Tac.OrderSlot> _slots = new List<Tac.OrderSlot>();
        static string _publicLine = "";
        static ulong _sessionSeed;
        static string _sessionKey;   // campaign|mission identity of the loaded session

        public static void ResetSession()
        {
            _deckMgr = null;
            _catalog = null;
            _patternByCard = null;
            _planRound = -1;
            _slots = new List<Tac.OrderSlot>();
            _publicLine = "";
            _sessionSeed = 0;
            _sessionKey = null;
        }

        /// <summary>Identity of the loaded session (same material SessionSeed
        /// hashes). Null when no saga session is up.</summary>
        static string SessionKey()
        {
            try
            {
                return Saga.RunningCampaign.sagaCampaignGUID + "|" +
                       (DataStore.sagaSessionData?.setupOptions?.projectItem?.missionID ?? "solo");
            }
            catch { return null; }
        }

        /// <summary>Save-resume and mission switches bypass SagaSetup, so the
        /// static planner state would leak the previous mission's decks/plan.
        /// Every hook entry revalidates the campaign|mission identity and
        /// resets when it changed (audit FINAL-QA).</summary>
        /// <summary>FNV-1a string ordinal -> ulong (deterministic, locale-free).</summary>
        static ulong OrdinalOf(string text)
        {
            ulong h = 1469598103934665603UL;
            foreach (var c in text ?? "") { h ^= c; h *= 1099511628211UL; }
            return h == 0 ? 0x9E3779B97F4A7C15UL : h;
        }

        /// <summary>Hook gate: master switch on AND the loaded mission's
        /// effective mode is not "legacy" (concept 4.2.7). The mode frozen
        /// into setupOptions at mission start decides - mid-mission global
        /// toggles do not silently re-plan a legacy mission (FINAL-QA audit).</summary>
        static bool ActiveNow()
        {
            if (!TacticalAISettings.Enabled) return false;
            var mission = DataStore.sagaSessionData?.setupOptions?.aiMode;
            return TacticalAISettingsUnity.MissionMode(mission) != "legacy";
        }

        static void EnsureSessionFresh()
        {
            var k = SessionKey();
            if (k == null) return;                 // no session context yet
            if (_sessionKey != null && k != _sessionKey) ResetSession();
            _sessionKey = k;
        }

        // ---------------------------------------------------------------- plan

        /// <summary>Round planner, called from the IncreaseRound hook (the
        /// nearest existing equivalent of the concept's deployment_finished
        /// event trigger - see PR draft deviation note) and lazily before any
        /// activation. Rebuilds order + deals EVERY living group's card at
        /// round start (concept 3.4.1: deal-all-first, never at activation).</summary>
        public static void OnNewRound(int roundNo)
        {
            if (!ActiveNow()) return;   // master off OR this mission is legacy
            EnsureSessionFresh();
            // post_hoc explain: summary of the round we just played - capture the
            // PREVIOUS plan before EnsureRound rebuilds it for the new round,
            // otherwise this leaks the upcoming activation order (concept 3.7:
            // post_hoc is after-the-fact only).
            var lastRoundLine = _publicLine;
            EnsureRound(roundNo);
            if (TacticalAISettings.ExplainLevel == "post_hoc" && roundNo > 1
                && !string.IsNullOrEmpty(lastRoundLine))
            {
                var qm = GlowEngine.FindUnityObject<QuickMessage>();
                if (qm != null) qm.Show(lastRoundLine);
            }
        }

        static void EnsureRound(int roundNo)
        {
            if (_planRound == roundNo && _deckMgr != null) return;
            BuildGroups(out var facts, out var figures);
            _patternByCard = new Dictionary<string, string>();
            foreach (var f in figures) if (f != null) _patternByCard[f.Id] = f.Pattern;
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
                // custom/embedded cards (DataStore.AddEmbeddedImportsToPools) have no
                // order deck: deal them nothing (their popup falls back to legacy) and
                // never let a missing deck abort the round flow (KeyNotFound previously
                // escaped into SagaController.IncreaseRound and hung the mission).
                var dealt = figures.FindAll(f => f != null && _deckMgr.DecksByCard.ContainsKey(f.Card.Id));
                try { _deckMgr.DrawRoundAll(roundNo, dealt, (int)(SessionSeed() & 0x7FFFFFFF)); }
                catch (Exception e)
                {
                    Debug.LogError("TacticalAIBridge::deal failed - legacy this round: " + e.Message);
                }
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
            EnsureSessionFresh();
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
            EnsureSessionFresh();
            if (!TacticalAISettings.Enabled || cd == null) return false;
            EnsureRound(CurrentRound());
            EnsureDecks();
            if (_deckMgr == null) return false;    // asset failed -> legacy + error already logged

            if (!_deckMgr.RoundCards.TryGetValue(cd.id, out var card))
            {
                // group joined mid-round: it was not part of the round-start
                // deal; draw its card now (documented reinforcements delta).
                var fig = MakeFigure(cd, Math.Max(0, IndexOf(cd)));
                if (fig != null && _deckMgr.DecksByCard.ContainsKey(cd.id))
                {
                    try
                    {
                        _deckMgr.DrawRoundAll(_planRound, new List<Tac.Figure> { fig },
                                              (int)(SessionSeed() & 0x7FFFFFFF));
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("TacticalAIBridge::delta draw failed, legacy popup: " + e.Message);
                        return false;
                    }
                }
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
            // Doctrine pattern from the card's Core catalog record (dev 11).
            // All shipped cards are 'aggressive' until M4 assigns per-mission
            // roles - but the renderer already carries full action-tree text
            // for ALL six patterns, so no future doctrine ships as bare tokens.
            EnsureCatalog();
            plan.Pattern = _patternByCard != null && _patternByCard.TryGetValue(cd.id, out var pat)
                           && !string.IsNullOrEmpty(pat) ? pat : "aggressive";
            // Playtest 27.10 (Thomas): raw deck tokens ('move', 'attack') are
            // not an instruction a human Imperial player can execute. Render
            // every block as a concrete, localized order naming the focus
            // target (which names are PUBLIC once dealt - fairness unaffected).
            plan.Blocks = RenderBlocks(blocks, plan, cd.speed);
            return true;
        }

        static string[] RenderBlocks(string[] tokens, ActivationPlanData plan, int speed)
        {
            var toks = tokens ?? Array.Empty<string>();
            var L = UIx;                    // Languages/<Lang>/ui.json: uiTacticalAI
            if (L == null) return toks;     // broken language asset -> legacy path
            var outp = new List<string>();
            var t = plan.HasTarget ? plan.TargetName : null;
            // Doctrine header (dev 11): every order NAMES its doctrine. All
            // texts come from the language files (PR convention after Thomas'
            // review 27.10: zero hardcoded player strings in the bridge).
            var pat = plan.Pattern ?? "aggressive";
            bool priorAttack = false;   // one-attack-per-activation rule tracker
            outp.Add(TF(L.doctrineHeader, DoctrineName(pat, L)));
            for (int n = 0; n < toks.Length; n++)
            {
                var tok = toks[n];
                // Thomas' doctrine template: every action is a numbered
                // Wenn-Dann decision tree; the IMPERIAL PLAYER evaluates the
                // conditions at the public board (only they see positions).
                System.Func<string, string> act = txt => TF(L.actionFormat, n + 1, txt);
                switch (tok)
                {
                    case "move":
                        if (pat == "guard") outp.Add(act(TF(L.moveGuard, speed)));
                        else if (pat == "blocker") outp.Add(act(TF(L.moveBlocker, speed)));
                        else if (pat == "support") outp.Add(act(TF(L.moveSupport, speed, t)));
                        else if (pat == "ambusher") outp.Add(act(TF(L.moveAmbusher, speed)));
                        else
                            // aggressive + hunter (the sweep IS the advance on
                            // the focus target): hit&run tree with the
                            // stand-and-shoot anti-pin guard (dev 10).
                            outp.Add(act(t != null ? TF(L.moveFocus, speed, t)
                                                   : TF(L.moveFocusNoTarget, speed)));
                        break;
                    case "attack":
                        if (priorAttack)
                        {
                            // Thomas round 10: the SECOND attack block does not
                            // assert anything - it CHECKS: already attacked?
                            // second shot forbidden unless the unit's own card
                            // prints multi-attack explicitly (printed card text
                            // the human reads; not a machine keyword) -> then
                            // this block repeats Aktion 1. Never attacked?
                            // block plays like an Aktion-1 attack. The move
                            // body is the doctrine's disengage/hold logic.
                            string body;
                            if (pat == "guard") body = TF(L.attack2MoveGuard, speed);
                            else if (pat == "ambusher") body = TF(L.attack2MoveAmbush, speed);
                            else if (pat == "blocker") body = TF(L.attack2MoveBlocker, speed, t);
                            else body = t != null ? TF(L.attack2Move, speed, t)
                                                   : TF(L.attack2MoveNoTarget, speed);
                            outp.Add(act(TF(L.attackSecondIntro, body) + " " + TF(L.attackSecondTail)));
                            break;
                        }
                        priorAttack = true;
                        if (pat == "guard")
                            outp.Add(act(t != null ? TF(L.attackGuard, t) : TF(L.attackGuardNoTarget)));
                        else if (pat == "ambusher")
                            outp.Add(act(t != null ? TF(L.attackAmbush, t) : TF(L.attackAmbushNoTarget)));
                        else if (pat == "support")
                            outp.Add(act(t != null ? TF(L.attackSupport, t) : TF(L.attackSupportNoTarget)));
                        else
                            // aggressive/hunter/blocker: Thomas' Wenn-Dann-Schema
                            // (focus if engaged, close in via the action, shoot
                            // the line-blocker).
                            outp.Add(act(t != null ? TF(L.attackFocus, t, speed)
                                                   : TF(L.attackNoTarget)));
                        break;
                    case "special":
                        outp.Add(act(TF(L.specialAsPrinted)));
                        break;
                    case "rest":
                        outp.Add(act(TF(L.restLine)));
                        break;
                    case "interact":
                        outp.Add(act(TF(L.interactLine)));
                        break;
                    case "shuffle":
                        outp.Add(TF(L.shuffleNote));
                        break;
                    default:
                        outp.Add(tok);   // unknown token: verbatim, never silently dropped
                        break;
                }
            }
            return outp.ToArray();
        }

        // ------------------------------------------------------- language files
        // PR convention (Thomas' review 27.10): every player-facing string of
        // this layer lives in Resources/Languages/<Lang>/ui.json under
        // uiTacticalAI; code only holds format keys and calls TF().
        // NOTE: slot.Why (activation ranking) is Core text - Core is the
        // vector-verified mirror of the Python sim, so its why-strings ship
        // German-first (documented deviation in the PR draft, sim i18n is a
        // separate work item).

        static UITacticalAI UIx =>
            DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;

        static string TF(string fmt, params object[] args)
        {
            if (string.IsNullOrEmpty(fmt)) return "";
            try { return args.Length > 0 ? string.Format(fmt, args) : fmt; }
            catch (System.FormatException) { return fmt; }
        }

        static string DoctrineName(string p, UITacticalAI L)
        {
            switch (p)
            {
                case "guard":    return L.docGuard;
                case "blocker":  return L.docBlocker;
                case "hunter":   return L.docHunter;
                case "support":  return L.docSupport;
                case "ambusher": return L.docAmbusher;
                default:         return L.docAggressive;
            }
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
            if (dps == null || dps.Count == 0 || !ActiveNow()) return false;
            // Mission-EVENT deployments call this with card == null (the shared
            // GetActiveDeploymentPoint helper). Those were never part of the
            // planner's contract and must keep the legacy random line - a fixed
            // point here would cluster every reinforcement wave (audit FINAL-QA).
            if (card == null) return false;
            var best = dps
                // round-varying deterministic pick: seeded by the campaign|mission
                // key + round (all public/campaign-scoped info), so it is neither
                // hidden RNG nor a constant the players can farm (audit FINAL-QA).
                .OrderBy(x => OrdinalOf(x.name ?? "") ^ (SessionSeed() ^ (ulong)Math.Max(1, CurrentRound()) * 0x2545F4914F6CDD1DUL))
                .ThenBy(x => x.name ?? "", StringComparer.Ordinal)
                .ThenBy(x => x.GUID.ToString(), StringComparer.Ordinal)
                .First();
            chosen = best.GUID;
            Debug.Log($"TacticalAIBridge::DP -> {best.name} ({card?.name}) [deterministic, no RNG]");
            return true;
        }

        // ---------------------------------------------------------- targeting

        /// <summary>Deterministic focus WITHOUT the hex board (playtest 27.10
        /// rounds 4-6). Ranking over ALL living heroes, public information
        /// only: (1) wounded heroes first - the manual damage tracker is
        /// shared table knowledge and finishing a wounded hero is what a human
        /// Imperial always does; (2) printed firepower proxy: attack dice, then
        /// health (exact loadout EV via HeroThreatProfile/campaignItems is the
        /// M5 upgrade, the plumbing exists); (3) squad row left-to-right.
        /// NO printed preferred-target rule (Thomas round 6: real Imperial
        /// cards print no target preference - the internal preferredTargets
        /// field is legacy engine data and must never surface to players).
        /// No chance, no hidden state. Proximity stays only as order-level
        /// fallback so a decoy parked next to an Imperial cannot pull focus
        /// across the map. The WorldProvider seam replaces this with the Core
        /// FocusResolver as soon as a hex adapter exists (documented
        /// deviation).</summary>
        static bool TrySelectTarget(global::DeploymentCard cd, out string heroName, out string why)
        {
            heroName = null; why = null;
            var heroes = DataStore.deployedHeroes;
            if (heroes == null || heroes.Count == 0) return false;
            var living = heroes.Where(h => h.heroState == null || !h.heroState.isDefeated).ToList();
            if (living.Count == 0) living = heroes;    // all defeated: legacy would find none either

            bool Wounded(global::DeploymentCard h) => h.heroState != null && h.heroState.isWounded;

            // ranking is total + deterministic (see doc-comment)
            var pick = living.Select((h, i) => new { h, i })
                .OrderBy(x => Wounded(x.h) ? 0 : 1)
                .ThenByDescending(x => x.h.attacks?.Length ?? 0)
                .ThenByDescending(x => x.h.health)
                .ThenBy(x => x.i)          // squad row left-to-right
                .First().h;

            var L = UIx;
            if (L == null) return false;    // broken language asset -> legacy popup
            var parts = new List<string>();
            if (Wounded(pick)) parts.Add(TF(L.focusWounded));
            parts.Add(TF(L.focusFirepower));
            parts.Add(TF(L.focusNoChance));
            heroName = pick.name;
            why = TF(L.focusPrefix) + heroName + " \u2014 " + string.Join("; ", parts);
            return true;
        }

        // ------------------------------------------------------------- explain

        static string ExplainFor(string groupId)
        {
            if (TacticalAISettings.ExplainLevel == "none") return null;
            if (TacticalAISettings.ExplainLevel == "full")
            {
                var slot = _slots.FirstOrDefault(s => s.GroupId == groupId);
                if (slot != null && UIx != null)
                    return TF(UIx.whyPrefix) + slot.Why + " " + TF(UIx.whyRank, slot.Rank + 1);
            }
            // post_hoc: printed at round end via OnNewRound quick message
            return null;
        }

        static string EffectiveMissionMode()
        {
            string aiMode = null;
            try { aiMode = DataStore.sagaSessionData?.setupOptions?.aiMode; }
            catch { /* session not up yet */ }
            return TacticalAISettingsUnity.MissionMode(aiMode);
        }

        // ------------------------------------------------------------- plumbing

        static int CurrentRound()
        {
            try { return Math.Max(1, DataStore.sagaSessionData?.gameVars?.round ?? 1); }
            catch { return 1; }
        }

        static ulong SessionSeed()
        {
            if (_sessionSeed != 0) return _sessionSeed;
            var key = SessionKey() ?? "solo";
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
            EnsureDecks();   // known-deck ids decide villain inclusion (DG071-DG090 ship decks)
            _catalog = new Dictionary<string, Tac.DeploymentCard>();
            foreach (var cd in DataStore.deploymentCards)
            {
                if (cd.isDummy) continue;
                bool hasDeck = _deckMgr != null && _deckMgr.DecksByCard.ContainsKey(cd.id);
                if (cd.characterType != Saga.CharacterType.Imperial && !hasDeck) continue;
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
            foreach (var cd in DataStore.deployedEnemies)
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
            var list = DataStore.deployedEnemies;
            for (int i = 0; i < list.Count; i++) if (list[i] == cd) return i;
            return -1;
        }
    }
}