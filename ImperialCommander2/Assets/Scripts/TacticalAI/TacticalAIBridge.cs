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

        static bool IsGerman => DataStore.languageCode != 0;
        static string T(string de, string en) => IsGerman ? de : en;

        static string[] RenderBlocks(string[] tokens, ActivationPlanData plan, int speed)
        {
            var outp = new List<string>();
            var t = plan.HasTarget ? plan.TargetName : null;
            var toks = tokens ?? Array.Empty<string>();
            // Doctrine header (dev 11): every order NAMES its doctrine, so the
            // human always knows which book of war rules a group follows.
            // Texts from Core Doctrine.StanceText; unknown/legacy patterns
            // render as Vorstoß (the shipped default - all live groups are
            // 'aggressive' until the M4 pattern assignment lands).
            var pat = plan.Pattern ?? "aggressive";
            outp.Add(T("Doktrin: " + DoctrineName(pat) + " — nach dieser Vorschrift spielst du diese Aktivierung",
                       "Doctrine: " + DoctrineNameEn(pat) + " — this rulebook governs your activation"));
            for (int n = 0; n < toks.Length; n++)
            {
                var tok = toks[n];
                // Thomas' doctrine template (27.10): every action is a
                // readable decision tree, numbered like the two actions of
                // an Imperial activation. The IMPERIAL PLAYER evaluates the
                // conditions at the public board (only they see positions).
                string act = "Aktion " + (n + 1);
                System.Func<string, string, string> Act =
                    (de, en) => T(act + " – " + de, "Action " + (n + 1) + " – " + en);
                switch (tok)
                {
                    case "move":
                        if (pat == "guard")
                            outp.Add(Act($"Bewegung {speed}: Halte deine Stellung in der Wachzone (das Missionsziel, das du verteidigst, samt Umkreis). Ein Held betritt sie: stelle dich ihm entgegen — deine Bewegung endet aber IMMER in der Wachzone. Lass dich nicht herauslocken",
                                         $"Move {speed}: hold your position inside your guard zone (the mission objective you defend, plus its rim). A hero enters it: face him - but your move ALWAYS ends back inside the guard zone. Never be lured out"));
                        else if (pat == "blocker")
                            outp.Add(Act($"Bewegung {speed}: Suche das Feld zwischen dem Missionsziel und dem nächsten Helden - sein kürzester Weg zum Ziel soll über dein Feld führen. Kein solches Feld erreichbar: halte die nächste Engstelle auf seinem Weg",
                                         $"Move {speed}: take the field between the mission objective and the nearest hero - his shortest route to the objective must cross your tile. Not reachable: hold the next chokepoint on his route"));
                        else if (pat == "support")
                            outp.Add(Act($"Bewegung {speed}: Distanz halten. Schon angegriffen? Dann bis zu {speed} Felder Abstand vom nächsten Helden, dabei siehst du ihn weiter. Noch nicht angegriffen? Nähere {t} nur, wenn danach kein Held in Nahkampfreichweite von dir steht",
                                         $"Move {speed}: keep your distance. Already attacked? Back off up to {speed} spaces from the nearest hero while keeping him in sight. Not attacked yet: close in on {t} only if no hero would end up in melee reach of you"));
                        else if (pat == "ambusher")
                            outp.Add(Act($"Bewegung {speed}: Bewege dich nur auf Feldern, die kein Held sehen kann - zeig dich nicht, bis ein Ziel in deiner Reichweite steht. Erster Held in Reichweite: greife an",
                                         $"Move {speed}: only step onto tiles no hero can see - stay hidden until a target stands inside your range. First hero in range: attack"));
                        else
                            // aggressive + hunter: hunter = aggressive tree, the
                            // sweep IS the advance on the focus target.
                            outp.Add(t != null
                                ? Act($"Bewegung {speed}: Steht schon ein Held in deiner Angriffreichweite: stehen bleiben und Feuer vorbereiten. Sonst gilt: In dieser Aktivierung bereits angegriffen? Dann entferne dich bis zu {speed} Felder von {t} und behalte ihn im Auge. Noch nicht angegriffen? Dann nähere {t}",
                                      $"Move {speed}: If a hero already stands inside your attack range: hold your ground and ready your fire. Otherwise: if this activation already attacked, back off up to {speed} spaces from {t} while keeping him in sight; if it has not attacked yet, close in on {t}")
                                : Act($"Bewegung {speed}: Steht schon ein Held in deiner Angriffreichweite: stehen bleiben und Feuer vorbereiten. Sonst gilt: Nach einem Angriff entferne dich vom nächsten Helden; vor jedem Angriff nähere ihm",
                                      $"Move {speed}: If a hero already stands inside your attack range: hold and ready fire. Otherwise: after an attack, back off from the nearest hero; before any attack, close in on it"));
                        break;
                    case "attack":
                        // Thomas' Wenn-Dann-Schema (dev 9) per doctrine (dev 11).
                        if (pat == "guard")
                            outp.Add(t != null
                                ? Act($"Angriff: der nächste Held IN deiner Wachzone (Reichweite + freie Linie). Keiner drin, aber {t} ist schießbar: {t}. Trifft beides nicht zu: kein Schuss, Stellung halten",
                                      $"Attack: the nearest hero INSIDE your guard zone (range + clear line). None inside but {t} is shootable: {t}. Neither: hold your ground, no shot")
                                : Act("Angriff: der nächste Held in deiner Wachzone (Reichweite + freie Linie). Keiner drin: kein Schuss, Stellung halten",
                                      "Attack: the nearest hero inside your guard zone (range + clear line). None inside: hold, no shot"));
                        else if (pat == "ambusher")
                            outp.Add(t != null
                                ? Act($"Angriff aus dem Versteck: der erste Held, den du von hier schießen kannst (Reichweite + freie Linie) - Überraschung schlägt Zielwahl, auch gegen {t}. Danach: nicht weiter enttarnen",
                                      $"Ambush shot: the first hero you can shoot from here (range + clear line) - surprise beats target choice, even over {t}. Afterwards: do not unmask further")
                                : Act("Angriff aus dem Versteck: der erste Held, den du von hier schießen kannst (Reichweite + freie Linie). Danach: nicht weiter enttarnen",
                                      "Ambush shot: the first hero you can shoot from here (range + clear line). Afterwards: stay masked"));
                        else if (pat == "support")
                            outp.Add(t != null
                                ? Act($"Angriff auf {t}, wenn Reichweite und freie Sichtlinie - bevorzuge weiche Ziele aus der Deckung. Keine Sichtlinie / zu weit: weiche zurück statt vorzudrängen. Sichtlinie durch eine ANDERE Heldenfigur blockiert: diese blockierende Heldenfigur angreifen",
                                      $"Attack {t} if in range with a clear line - prefer soft targets from cover. No line / out of range: back off instead of pushing in. Line blocked by ANOTHER hero: attack that blocking hero")
                                : Act("Angriff: das weichste Ziel, das du aus Deckung schießen kannst (Reichweite + freie Linie). Kein Ziel: zurückweichen",
                                      "Attack: the softest target you can shoot from cover (range + clear line). No target: back off"));
                        else
                            // aggressive / hunter / blocker: blocker's job is delay,
                            // so nearest-shootable is doctrinally correct here.
                            outp.Add(t != null
                                ? Act($"Angriff auf {t}, wenn Reichweite und freie Sichtlinie. Keine Sichtlinie / zu weit: {speed} Felder auf {t} zubewegen. Sichtlinie durch eine ANDERE Heldenfigur blockiert: diese blockierende Heldenfigur angreifen",
                                      $"Attack {t} if in range with a clear line. No line / out of range: move up to {speed} spaces toward {t}. Line blocked by ANOTHER hero: attack that blocking hero")
                                : Act("Angriff: der nächste Held, den du schießen kannst (Reichweite + freie Linie). Kein Zugriff: nähere dem nächsten Helden",
                                      "Attack: the nearest hero you can shoot (range + clear line). No access: close in on the nearest hero"));
                        break;
                    case "special":
                        outp.Add(Act("Sonderaktion: genau so ausführen, wie auf der Karte gedruckt steht",
                                     "Special: execute exactly as printed on the card"));
                        break;
                    case "rest":
                        outp.Add(Act("Rasten: 1 Schaden regenerieren",
                                     "Rest: recover 1 damage"));
                        break;
                    case "interact":
                        outp.Add(Act("Interaktion: dein eigenes Positionierungsziel erfüllen",
                                     "Interact: fulfill this unit's own placement objective"));
                        break;
                    case "shuffle":
                        outp.Add(T("(Danach: Gruppen-Deck mischen)",
                                   "(afterwards: shuffle this group's deck)"));
                        break;
                    default:
                        outp.Add(tok);   // unknown token: verbatim, never silently dropped
                        break;
                }
            }
            return outp.ToArray();
        }

        // Doctrine display names (dev 11). Short forms of Core
        // Doctrine.StanceText - the header line must fit one popup row; the
        // per-action text carries the full behaviour, so no information is
        // lost. Unknown patterns display as Vorstoß (the shipped default).
        static readonly Dictionary<string, string> DoctrineDe = new Dictionary<string, string>
        {
            { "aggressive", "Vorstoß (Kontaktpunkt erreichen)" },
            { "guard",      "Wachschutz (Zone halten, nie herauslocken)" },
            { "blocker",    "Blockierer (Engstelle halten)" },
            { "hunter",     "Jäger (Schwerpunktzone abkämmen)" },
            { "support",    "Support (Distanz halten, aus der Deckung)" },
            { "ambusher",   "Hinterhalt (ungesehen bis zum ersten Schuss)" },
        };
        static readonly Dictionary<string, string> DoctrineEn = new Dictionary<string, string>
        {
            { "aggressive", "Advance (reach the contact point)" },
            { "guard",      "Guard (hold the zone, never be lured out)" },
            { "blocker",    "Blocker (hold the chokepoint)" },
            { "hunter",     "Hunter (sweep the priority zone)" },
            { "support",    "Support (keep distance, shoot from cover)" },
            { "ambusher",   "Ambush (stay unseen until the first shot)" },
        };
        static string DoctrineName(string p) =>
            DoctrineDe.TryGetValue(p ?? "", out var v) ? v : DoctrineDe["aggressive"];
        static string DoctrineNameEn(string p) =>
            DoctrineEn.TryGetValue(p ?? "", out var v) ? v : DoctrineEn["aggressive"];

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

            // player-facing reason chain (only the rules that actually fired)
            var parts = new List<string>();
            if (Wounded(pick))
                parts.Add(T("er ist verwundet – zuerst erledigen (Wunden-Tracker am Brett)",
                            "he is wounded - finish him first (table damage tracker)"));
            parts.Add(T("größte Feuerkraft unter den übrigen Helden (Angriffswürfel, dann Lebenspunkte)",
                        "highest firepower among the remaining heroes (attack dice, then health)"));
            parts.Add(T("kein Zufall", "no chance"));

            heroName = pick.name;
            why = (DataStore.languageCode != 0
                    ? "Fokus-Ziel: "
                    : "focus target: ")
                  + heroName + " — " + string.Join("; ", parts);
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
