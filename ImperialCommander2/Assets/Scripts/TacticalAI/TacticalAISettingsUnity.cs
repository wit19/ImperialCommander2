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
    }
}
