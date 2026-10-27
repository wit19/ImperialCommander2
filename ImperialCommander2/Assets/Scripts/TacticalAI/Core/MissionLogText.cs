// M8/F1 (findings_m56_qa_2026-10-27): the mission logger stores the FULL
// localized rich-text piece in LogItem.message (MissionLogger.LogEvent:
// label + <color> NAME + labels, all language-dependent). The world-feed
// adapter needs the RAW name back out of it - comparing the display string
// against card names never matches in the shipping game (death/removal/
// activation events were dead in the App and the localized text leaked into
// GroupId/HistoryHash).
//
// Extraction law (reviewer-recommended, pinned by the logger_extract.json
// vectors): the RAW value is the content of the LAST <color=…>…</color>
// span - the value colour always comes after the label colour in every
// MissionLogger event format, in every language. Culture-stable: pure
// string/regex work, no locale, no label knowledge, no ':' splitting
// (names and labels may legitimately contain colons).
using System.Text.RegularExpressions;

namespace TacticalAI.Core
{
    public static class MissionLogText
    {
        // Non-greedy inner span; event texts keep their color tags balanced,
        // and a name containing a raw '<' is not representable in Unity's
        // rich text anyway (the label would break long before us).
        static readonly Regex ColorSpan =
            new Regex(@"<color[^>]*>(.*?)</color>",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Raw name behind a localized logger event text, or null
        /// when no color span carries content. Already Trim()-ed; whitespace
        /// around the name is logger formatting, never part of a card name
        /// (LogEvent itself drops whitespace-only messages via Trim).</summary>
        public static string ExtractRawName(string eventText)
        {
            if (string.IsNullOrEmpty(eventText)) return null;
            Match last = null;
            foreach (Match m in ColorSpan.Matches(eventText))
                if (m.Groups[1].Value.Trim().Length > 0) last = m;
            return last == null ? null : last.Groups[1].Value.Trim();
        }
    }
}
