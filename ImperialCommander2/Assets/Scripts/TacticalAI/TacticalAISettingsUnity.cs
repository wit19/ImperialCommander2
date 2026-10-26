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
            Debug.Log($"TacticalAISettingsUnity::mode={TacticalAISettings.Mode} enabled={TacticalAISettings.Enabled} " +
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
