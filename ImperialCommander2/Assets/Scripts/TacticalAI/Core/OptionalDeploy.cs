using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    /// <summary>
    /// M13c: the PURE decisions behind the revived optional-deployment flow
    /// (Thomas 28.10: full revival - tactical mode = the AI places and the
    /// spot is MARKED on the map + announced; legacy mode = the old manual
    /// DM placement window). Presentation/branching logic only - the DP
    /// choice itself stays in the existing deterministic hook
    /// (TacticalAIBridge.TryChooseDeploymentPoint) and the DP validity
    /// filter stays in MapEntityManager.FilterDP, so there is no second
    /// truth. Hand-pinned fixture: Assets/Tests/Fixtures/tacai/
    /// optional_deploy.json (same documented route as action_tree.json -
    /// no Python counterpart, Canon vectors untouched).
    /// </summary>
    public static class OptionalDeploy
    {
        public const string PathTactical = "tactical";
        public const string PathManual = "manual";

        /// <summary>Mode fork (Scope D): tactical campaign missions run the
        /// AI path, legacy campaign missions the manual DM window. Mirrors
        /// the mission-frozen gate of TacticalAIBridge.ActiveNow: the mode
        /// captured at mission start decides, mid-mission global toggles
        /// never silently switch a mission's path. enabled/mode/missionAiMode
        /// are fed in by the caller (the Unity side reads TacticalAISettings +
        /// setupOptions.aiMode) so this stays UnityEngine-free and testable.</summary>
        public static string Path(bool enabled, string mode, string missionAiMode)
        {
            if (!enabled) return PathManual;
            string eff;
            if (string.IsNullOrEmpty(missionAiMode)) eff = mode;
            else if (missionAiMode == "off" || missionAiMode == "legacy") eff = "legacy";
            else if (missionAiMode == "reference" || missionAiMode == "overlord") eff = missionAiMode;
            else if (missionAiMode == "S1" || missionAiMode == "S2" || missionAiMode == "S3") eff = "reference";
            else if (missionAiMode == "S4" || missionAiMode == "S5") eff = "overlord";
            else eff = mode;   // unknown token: follow the global mode (settings-legal values only)
            return eff == "legacy" ? PathManual : PathTactical;
        }

        /// <summary>Group candidates (Scope A/C): the deployment hand first
        /// (printed-cost groups, same pool the fuzzy Landing draw uses),
        /// then the manual deploy list (earned villains etc.), deduplicated
        /// by card id, minus already-deployed ids. Order is preserved
        /// exactly as given (hand order, then manual-list order) - the
        /// picker must not reshuffle what the DM sees.</summary>
        public static List<string> CandidateIds(IEnumerable<string> handIds,
                                                IEnumerable<string> manualIds,
                                                IEnumerable<string> deployedIds)
        {
            var deployed = new HashSet<string>(deployedIds ?? new string[0]);
            var seen = new HashSet<string>();
            var outList = new List<string>();
            foreach (var source in new[] { handIds, manualIds })
            {
                if (source == null) continue;
                foreach (var id in source)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (deployed.Contains(id)) continue;
                    if (!seen.Add(id)) continue;
                    outList.Add(id);
                }
            }
            return outList;
        }

        /// <summary>Cost shown in the manual window (Scope C: "costs as
        /// printed, when wired"): the groups printed threat cost, clamped to
        /// the 0..100 threat track. The UI passes card.cost straight in.</summary>
        public static int DisplayedCost(int cardCost)
        {
            return cardCost < 0 ? 0 : (cardCost > 100 ? 100 : cardCost);
        }

        /// <summary>Threat actually spent on confirm (manual window): the
        /// spend line mirrors the alive EnemyDeployment path
        /// (ModifyThreat(-clamp(cost + modifier,0,100)); the event-sourced
        /// optional path keeps useThreat=true as a GAIN - that legacy
        /// accounting is untouched here, this function is only the manual
        /// confirm law). modifier = the optional event override's threatCost
        /// when the window is event-driven, else 0.</summary>
        public static int ThreatSpend(int cardCost, int modifier)
        {
            int raw = cardCost + modifier;
            if (raw < 0) raw = 0;
            if (raw > 100) raw = 100;
            return raw;
        }

        /// <summary>Manual confirm gate: a group with a positive printed
        /// cost AND a picked, still-valid deployment point. A zero-cost
        /// group is not deployable through this window (defensive: real
        /// group cards print cost >= 1), and an empty/invalid DP never
        /// confirms - the caller shows the existing noDPWarning instead.</summary>
        public static bool CanConfirmManual(string groupId, int cardCost,
                                           bool dpPicked, bool dpStillValid)
        {
            if (string.IsNullOrEmpty(groupId)) return false;
            if (DisplayedCost(cardCost) <= 0) return false;
            return dpPicked && dpStillValid;
        }

        /// <summary>Tactical marker announcement line (Scope B): pure
        /// template substitution into the ui.json
        /// optionalTacticalAnnounceUC template ("{0}" = group name, "{1}" =
        /// DP name). The marker itself is the existing pulsing DP sprite
        /// (DPointPrefab), visible exactly as long as the deployment
        /// instruction is on screen.</summary>
        public static string AnnounceLine(string template, string groupName, string dpName)
        {
            if (string.IsNullOrEmpty(template)) return "";
            return template
                .Replace("{0}", groupName ?? "")
                .Replace("{1}", dpName ?? "");
        }
    }
}
