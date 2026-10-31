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
                      $"skill={TacticalAISettings.SkillLevel} " +
                      $"explain={TacticalAISettings.ExplainLevel}" +
                     $" info={TacticalAISettings.InfoText}");
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

        /// <summary>M11f (Einsatzkarte/Klassik-Start): Klartext-Badge, der die
        /// Legacy-Difficulty ersetzt, solange die taktische KI calibriert:
        /// Modusname + S-Stufe mit Klartext, z.B. "Tactical AI — Level 3
        /// (Veteran)". Ein AUTO-Token ("") loest sich auf die globale Leiter
        /// auf und bekommt die (auto)-Variante; off/legacy rendern OFF.
        /// Kurzes MissionBadgeLabel (Kampagnenliste) bleibt unveraendert.</summary>
        public static string MissionBadgeText(string token)
        {
            var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
            if (token == "off" || token == "legacy")
                return Fallback(tl != null ? tl.badgeMissionOff : null, "Tactical AI \u2014 OFF");
            string skill = TacticalAISettings.EffectiveSkill(token);
            int rank = TacticalAISettings.RankOfToken(skill);
            string name = SkillDisplayName(tl, skill);
            string fmt;
            if (TacticalAISettings.IsMissionSkillToken(token))
                fmt = Fallback(tl != null ? tl.badgeMissionFormat : null, "Tactical AI \u2014 Level {0} ({1})");
            else
                fmt = Fallback(tl != null ? tl.badgeMissionAutoFormat : null, "Tactical AI \u2014 Level {0} ({1}) (auto)");
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, fmt, rank, name);
        }

        static string Fallback(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        /// <summary>M12c: the persistent Imperial Upgrade Stage purchase
        /// line (ui.json uiTacticalAI.impBuyLog). Used by both the booking
        /// Debug.Log and the M12e campaign-side display; the store itself
        /// keeps only the machine key ("buy:&lt;cardId&gt;") so the wording
        /// lives in the language files alone. Card names stay EN (printed
        /// board-game content, M11g doctrine).</summary>
        public static string ImperialBuyLogText(string cardName, long xp)
        {
            var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
            var fmt = Fallback(tl != null ? tl.impBuyLog : null,
                               "The Empire acquires: {0} ({1} XP)");
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                 fmt, cardName, xp);
        }

        /// <summary>M12d2: the public agenda purchase line, mapped by the RR
        /// type (ui.json uiTacticalAI.agenda* keys). Read-aloud law: the
        /// three open types announce name + cost (the table reads the card
        /// aloud anyway); a Kept Secret buy announces the INFLUENCE SPEND
        /// ONLY - the spend is public campaign-log data, the card is not,
        /// so this function refuses the name even if a caller passes one
        /// (anti-leak lives in code, not in caller discipline).</summary>
        public static string AgendaBuyLogText(string type, string cardName, long influence)
        {
            var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
            string fmt;
            switch (type)
            {
                case "side_mission":
                    fmt = Fallback(tl != null ? tl.agendaBuySide : null,
                        "Agenda SIDE MISSION: {0} ({1} influence). It is now an active side mission - execution is at the table's discretion.");
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                         fmt, cardName, influence);
                case "forced_mission":
                    fmt = Fallback(tl != null ? tl.agendaBuyForced : null,
                        "Agenda FORCED MISSION: {0} ({1} influence). Its mission is played immediately - execution is at the table's discretion.");
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                         fmt, cardName, influence);
                case "ongoing":
                    fmt = Fallback(tl != null ? tl.agendaBuyOngoing : null,
                        "Agenda ONGOING: {0} ({1} influence). It stays face up in the Imperial play area.");
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                         fmt, cardName, influence);
                default:   // kept_secret and anything unknown: never the card
                    fmt = Fallback(tl != null ? tl.agendaBuyKept : null,
                        "The Empire purchases an Agenda card - kept secret ({0} influence).");
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                         fmt, influence);
            }
        }

        /// <summary>M12e (Einsatzkarte): second badge line "Militaermacht - 4
        /// Karten" under the M11f level badge. Deck NAME is printed board-game
        /// content and stays EN; only the surrounding word shapes translate.
        /// Returns null when there is nothing to show (no deck chosen) - the
        /// caller then hides the line entirely, never renders an empty row.</summary>
        public static string ImperialDeckBadgeText(string deckName, int cardCount)
        {
            if (string.IsNullOrEmpty(deckName) || cardCount < 0) return null;
            var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                Fallback(tl != null ? tl.impBadgeDeckLine : null, "{0} \u2014 {1} cards"),
                deckName, cardCount);
        }

        /// <summary>M12e + M12d2 anti-leak: an OPEN agenda (ongoing/side/forced
        /// buys announce read-aloud anyway) shows its name next to the deck
        /// line; a Kept Secret shows NOTHING here - the spend line at buy time
        /// is already the full public record. Callers pass the name only for
        /// open types; this function additionally refuses unknown types.</summary>
        public static string AgendaOpenLineText(string type, string cardName)
        {
            if (type != "ongoing" && type != "side_mission" && type != "forced_mission")
                return null;
            if (string.IsNullOrEmpty(cardName)) return null;
            var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                Fallback(tl != null ? tl.impAgendaLine : null, "Agenda: {0}"), cardName);
        }

        /// <summary>M12e (Play-Area-Panel / Kampagnen-Sektion): the deck row
        /// "Militaermacht - 4 Karten" (shared wording with the badge line).</summary>
        public static string ImperialDeckLineText(string deckName, int cardCount)
        {
            return ImperialDeckBadgeText(deckName, cardCount);
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
