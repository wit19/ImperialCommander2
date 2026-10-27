// M1 Unity-side binding for TacticalAISettings (concept 4.2 "Settings").
// Injects a PlayerPrefs save hook and reloads the persisted state at boot.
// No behaviour changes: with the default (Enabled=false, Mode="legacy")
// every Tactical-AI hook in the game takes its untouched legacy branch.
using UnityEngine;

namespace TacticalAI
{
    public static class TacticalAISettingsUnity
    {
        // Single PlayerPrefs key holding the serialized settings JSON.
        const string PREF_KEY = "tacai_settings";

        static bool _booted;

        /// <summary>Runs before the first scene, in every scene (Title,
        /// Setup, Campaign, Saga) and in all builds.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Boot()
        {
            if (_booted) return;
            _booted = true;
            TacticalAISettings.SetSaveHook(PlayerPrefsHook);
            TacticalAISettings.Load();
            // Dev 20 (option A, Thomas): the mode is derived from the
            // difficulty ladder (S1-S3 reference, S4-S5 overlord) - a stored
            // legacy/reference/overlord "mode" from old saves is ignored by
            // the loader, so no migration is needed any more.
            Debug.Log($"TacticalAISettingsUnity::mode={TacticalAISettings.Mode}{(TacticalAISettings.HasModeOverride ? " (OVERRIDE)" : "")} enabled={TacticalAISettings.Enabled} " +
                      $"skill={TacticalAISettings.SkillLevel} deploy={TacticalAISettings.DeploymentMode} " +
                      $"explain={TacticalAISettings.ExplainLevel}");
        }

        /// <summary>save-hook contract (see TacticalAISettings.SetSaveHook):
        /// non-null argument = store this JSON and return what was stored
        /// BEFORE; null argument = just return the currently stored JSON.</summary>
        static string PlayerPrefsHook(string jsonToStore)
        {
            string previous = PlayerPrefs.GetString(PREF_KEY, "");
            if (!string.IsNullOrEmpty(jsonToStore))
            {
                PlayerPrefs.SetString(PREF_KEY, jsonToStore);
                PlayerPrefs.Save();
            }
            return previous;
        }

        /// <summary>Mission-scoped effective mode (concept 4.2.7): resolves
        /// the per-mission aiMode ("legacy" | "reference" | "overlord")
        /// against the global switch. Returns "legacy" when the feature is
        /// globally OFF - hooks must then run their original code path.</summary>
        public static string MissionMode(string missionAiMode)
        {
            return TacticalAISettings.EffectiveMode(missionAiMode);
        }
    
        /// <summary>Badge text for the per-mission cycle (dev 21, Thomas):
        /// AUTO / OFF / the ladder names from the language files. Deprecated
        /// mission tokens (legacy/reference/overlord from old saves) still
        /// render, so old campaigns never show raw internals.</summary>
        public static string MissionBadgeLabel(string token)
        {
            var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
            string auto = (tl != null && !string.IsNullOrEmpty(tl.badgeAuto)) ? tl.badgeAuto : "AUTO";
            string off = (tl != null && !string.IsNullOrEmpty(tl.badgeOff)) ? tl.badgeOff : "OFF";
            if (string.IsNullOrEmpty(token)) return "AI:" + auto;
            if (token == "off" || token == "legacy") return "AI:" + off;
            if (token == "reference") return "AI:REF";
            if (token == "overlord") return "AI:ORD";
            if (TacticalAISettings.IsMissionSkillToken(token))
                return "AI:" + SkillDisplayName(tl, token);
            return "AI:" + token.ToUpperInvariant();
        }

        static string SkillDisplayName(UITacticalAI tl, string token)
        {
            if (tl == null) return token;
            string v;
            switch (token)
            {
                case "S1": v = tl.skillS1; break;
                case "S2": v = tl.skillS2; break;
                case "S3": v = tl.skillS3; break;
                case "S4": v = tl.skillS4; break;
                default: v = tl.skillS5; break;
            }
            return string.IsNullOrEmpty(v) ? token : v;
        }
}
}
