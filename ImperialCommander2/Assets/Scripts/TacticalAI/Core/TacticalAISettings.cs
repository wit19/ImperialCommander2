// M1 settings seam (concept section 4.2 "Settings"). Pure C# - NO UnityEngine
// imports: the same class compiles into the console harness csproj and the
// Unity Assembly-CSharp. Persistence is injected by the Unity side via
// SetSaveHook (PlayerPrefs binding in TacticalAISettingsUnity).
//
// HARD RULE: every Tactical-AI behaviour in the game checks
//   if (TacticalAI.TacticalAISettings.Enabled)
// and keeps the original legacy path verbatim as the else/default branch.
// DEFAULT IS OFF ("legacy") - behaviour bit-compatible with the shipped game.
//
// NOTE (documented deviation): the class lives in the parent namespace
// `TacticalAI` (not `TacticalAI.Core`) so the hook-table condition
// `TacticalAI.TacticalAISettings.Enabled` compiles verbatim in every hook
// site without a using-directive. The FILE still lives under Core/ because
// it is pure C# and belongs to the Core compilation.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TacticalAI
{
    public static class TacticalAISettings
    {
        public static readonly string[] ModeValues = { "legacy", "reference", "overlord" };
        public static readonly string[] SkillValues = { "S1", "S2", "S3", "S4", "S5" };
        public static readonly string[] DeploymentModeValues = { "secret", "open" };
        public static readonly string[] ExplainLevelValues = { "none", "post_hoc", "full" };

        // ---- defaults: OFF, legacy, printed rules only -------------------
        public const bool DefaultEnabled = false;
        // dev 20: no stored mode dial; kept as legacy token for missions/tests.
        public const string DefaultMode = "legacy";
        public const string DefaultSkillLevel = "S1";
        public const string DefaultDeploymentMode = "secret";
        public const string DefaultExplainLevel = "post_hoc";

        static bool _enabled = DefaultEnabled;
        // Dev 20 (option A, Thomas): the procedural mode is DERIVED from the
        // difficulty ladder - two switches for one screw was the flaw he
        // named. S1-S3 = reference (book execution, growing discretion
        // quality), S4-S5 = overlord (score optimisation on top). The Mode
        // setter survives only as an explicit test/console override seam.
        static string _modeOverride;
        static string _skillLevel = DefaultSkillLevel;
        static string _deploymentMode = DefaultDeploymentMode;
        static string _explainLevel = DefaultExplainLevel;

        static Func<string, string> _saveHook;   // (jsonToStore) -> storedJson|null

        /// <summary>Global master switch. FALSE by default: every hook falls
        /// through to the untouched legacy path.</summary>
        public static bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; Save(); }
        }

        /// <summary>Derived from the difficulty ladder (dev 20): "reference"
        /// for Rekrut..Veteran (S1-S3), "overlord" for Kommandant/General
        /// (S4-S5) - unless a test/console override is set. Never "legacy":
        /// legacy-ness is decided by Enabled/EffectiveMode.</summary>
        public static string Mode
        {
            get { return _modeOverride ?? (SkillRank >= 4 ? "overlord" : "reference"); }
        }

        /// <summary>Test/console seam: force a procedural mode independent of
        /// the ladder; null/empty restores derivation. Unknown values are
        /// ignored (never throw). "legacy" here is accepted for the mission-
        /// badge test paths only - the settings UI cannot produce it.</summary>
        public static void SetModeOverride(string mode)
        {
            if (string.IsNullOrEmpty(mode)) { _modeOverride = null; Save(); return; }
            _modeOverride = Validate(mode, ModeValues, null);
            Save();
        }

        public static bool HasModeOverride { get { return _modeOverride != null; } }

        /// <summary>1..5 for "S1".."S5" (unknown -> 1).</summary>
        public static int SkillRank
        {
            get { int i = IndexOf(_skillLevel, SkillValues); return i < 0 ? 1 : i + 1; }
        }

        /// <summary>"S1".."S5" - the error-band skill of the reference level
        /// (concept 3.2). Only consulted when Mode != "legacy".</summary>
        public static string SkillLevel
        {
            get { return _skillLevel; }
            set { _skillLevel = Validate(value, SkillValues, DefaultSkillLevel); Save(); }
        }

        /// <summary>"secret" (planner picks DPs silently) | "open" (DP
        /// highlight kept on screen).</summary>
        public static string DeploymentMode
        {
            get { return _deploymentMode; }
            set { _deploymentMode = Validate(value, DeploymentModeValues, DefaultDeploymentMode); Save(); }
        }

        /// <summary>"none" | "post_hoc" (round summary after the round) |
        /// "full" (per-activation why-strings). Printed info only.</summary>
        public static string ExplainLevel
        {
            get { return _explainLevel; }
            set { _explainLevel = Validate(value, ExplainLevelValues, DefaultExplainLevel); Save(); }
        }

        /// <summary>Injected by the Unity side: the hook receives the
        /// serialized settings, stores them, and returns the previously
        /// stored JSON (or null when nothing was stored yet).</summary>
        public static void SetSaveHook(Func<string, string> hook) { _saveHook = hook; }

        /// <summary>Push current state through the save hook (no-op when no
        /// hook is installed, e.g. in the console harness).</summary>
        public static void Save()
        {
            if (_saveHook == null) return;
            _saveHook(ToJson());
        }

        /// <summary>Pull state through the save hook (pass null to read).
        /// Invalid/unknown fields keep their defaults; nothing throws.</summary>
        public static void Load()
        {
            if (_saveHook == null) return;
            string json = _saveHook(null);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var d = Core.Json.AsDict(Core.Json.Parse(json));
                _enabled = Core.Json.AsBool(Core.Json.Get(d, "enabled", (object)DefaultEnabled));
                // "mode" is no longer a stored dial (dev 20 derivation); only
                // an explicit override round-trips, and only when valid.
                _modeOverride = Validate(Core.Json.AsStr(Core.Json.Get(d, "modeOverride", "")), ModeValues, null);
                _skillLevel = Validate(Core.Json.AsStr(Core.Json.Get(d, "skill", DefaultSkillLevel)), SkillValues, DefaultSkillLevel);
                _deploymentMode = Validate(Core.Json.AsStr(Core.Json.Get(d, "deployment", DefaultDeploymentMode)), DeploymentModeValues, DefaultDeploymentMode);
                _explainLevel = Validate(Core.Json.AsStr(Core.Json.Get(d, "explain", DefaultExplainLevel)), ExplainLevelValues, DefaultExplainLevel);
            }
            catch (Exception)
            {
                ResetDefaults();   // corrupt save data must NEVER enable the mode
            }
        }

        public static void ResetDefaults()
        {
            _enabled = DefaultEnabled;
            _modeOverride = null;
            _skillLevel = DefaultSkillLevel;
            _deploymentMode = DefaultDeploymentMode;
            _explainLevel = DefaultExplainLevel;
        }

        /// <summary>Per-mission override resolution (concept 4.2.7): the
        /// campaign mission card stores CampaignStructure.aiMode; empty or
        /// unknown values fall back to the global Mode. The override can
        /// only pick among the tactical levels - "legacy" always means the
        /// untouched original code path.</summary>
        public static string EffectiveMode(string missionAiMode)
        {
            if (!_enabled) return "legacy";
            if (string.IsNullOrEmpty(missionAiMode)) return Mode;
            return Validate(missionAiMode, ModeValues, Mode);
        }

        public static bool IsTactical(string mode)
        {
            return _enabled && mode != null && mode != "legacy" && IndexOf(mode, ModeValues) >= 0;
        }

        // ------------------------------------------------------------------ JSON

        public static string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"enabled\":").Append(_enabled ? "true" : "false").Append(',');
            Kv(sb, "mode", Mode); Kv(sb, "skill", _skillLevel);
            if (_modeOverride != null) Kv(sb, "modeOverride", _modeOverride);
            Kv(sb, "deployment", _deploymentMode);
            sb.Append("\"explain\":\"").Append(_explainLevel).Append('"');
            sb.Append('}');
            return sb.ToString();
        }

        static void Kv(StringBuilder sb, string key, string val)
        {
            sb.Append('"').Append(key).Append("\":\"").Append(val).Append("\",");
        }

        static string Validate(string value, string[] allowed, string fallback)
        {
            if (IndexOf(value, allowed) >= 0) return value;
            return fallback;
        }

        static int IndexOf(string value, string[] allowed)
        {
            if (value == null) return -1;
            for (int i = 0; i < allowed.Length; i++)
                if (string.Equals(allowed[i], value, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
