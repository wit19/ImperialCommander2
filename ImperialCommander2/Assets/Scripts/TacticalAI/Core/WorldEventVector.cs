// M5 Scope B (concept 3.3.1b): deterministic projection of the passive
// world-feed history into a planner-facing view of the world ("Tac.World
// Sicht"). PURE mapping - replay the append-only public events in seq order;
// no RNG, no clock, no hidden state, no estimation: a group nobody wounded
// keeps full vitality (vitalityFrac = 1), a figure never observed keeps its
// printed assumption, and nothing in here ever claims a figure position.
//
// The projection is a pure function of the event list: replaying the same
// history always yields a bit-identical view (NUnit pin: 2x replay), and the
// history hash travels with the view so consumers can pin what they decided
// on.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TacticalAI.Core
{
    /// <summary>Planner-facing snapshot projected from the public feed.</summary>
    public sealed class FeedWorldView
    {
        public string MissionKey = "solo";
        public int Round;
        public string Posture = "eliminate";
        public string Objective = "";
        // M10b: hero credited with the LAST objective progress (public
        // activation-order attribution, "" = unknown/none). Event-derived,
        // never a position read - the app cannot see hero cells.
        public string ObjectiveActor = "";
        // M8/F5: MissionStart clock inputs, kept so a runtime RoundLimitChanged
        // can re-derive the posture exactly like a fresh MissionStart would.
        // (Canon text unchanged - posture, not its inputs, is the decision.)
        public string PriorityTarget = "";
        public long RoundLimit = -1;
        public long ThreatBalance;
        public bool ThreatSeen;            // at least one public ThreatSpend event
        public int Seq;
        public string HistoryHash = "";

        public readonly List<GroupView> Groups = new List<GroupView>();      // sorted by GroupId
        public readonly List<HeroView> Heroes = new List<HeroView>();        // sorted by HeroId

        public sealed class GroupView
        {
            public string GroupId;
            public string Name;
            public bool Deployed;
            public bool Removed;
            public int Size = 1;                 // printed group size (Spawn event)
            public int Alive = 1;                // Spawn(size) seeds, Death decrements, floor 0
            public int DeployedRound;
            public int Health;                   // printed, from GroupDamage events (0 = never observed)
            public int FiguresSeen;              // distinct figure indices in wheel events
            public readonly Dictionary<int, long> Wheels = new Dictionary<int, long>();

            /// <summary>Remaining group vitality as exact Rational (M9-F6/A0,
            /// closes M7-F4): the wound dials show ERLITTENER Schaden per
            /// figure — a fresh group reads 0 (Thomas board fact 2026-10-27;
            /// DeploymentCard.cs:55 fresh {0,0,0}, HPTracker.cs:43-48
            /// double-click reset to 0, label groupWoundNumber). Remaining
            /// HP per figure is therefore health - min(health, dial); the
            /// old reading summed the dials AS life and inverted every
            /// partially damaged group. A figure with NO wheel event never
            /// moved its dial -> damage 0 -> full HP (same "never guessed
            /// from absence" invariant that keeps an untouched group at 1).
            /// Denominator: printed card health of EVERY figure the group
            /// deployed with (FINAL-QA audit #10 group granularity); the
            /// result is capped by the alive figures' HP so a Death event
            /// with dials not yet clicked down cannot inflate vitality.
            /// Dials are absolute public facts; health rides the event.
            /// Known public blind spot: a group deployed PRE-DAMAGED at the
            /// baseline sees its starting dials invisible (no event) - same
            /// class as the M5 baseline rule, documented, not guessed.</summary>
            public Rational VitalityFrac
            {
                get
                {
                    if (Wheels.Count == 0 || Health <= 0) return Rational.One;
                    int n = Math.Max(1, Math.Max(Size, FiguresSeen));
                    long hp = (long)Health * n;
                    long dmg = 0;
                    foreach (var kv in Wheels) dmg += Math.Max(0, Math.Min(Health, kv.Value));
                    long alive = Math.Max(0, Math.Min(n, Alive));
                    long rem = Math.Min(Math.Max(0, hp - dmg), (long)Health * alive);
                    return new Rational(rem, hp <= 0 ? 1 : hp);
                }
            }
        }

        public sealed class HeroView
        {
            public string HeroId;
            public string Name;
            public bool Wounded;
            public bool Defeated;
            public int Activations;
            public int LastActivatedRound;
            // M6 aim tokens (concept 3.3.1b FocusChange vocabulary + public
            // designation): a PUBLIC mission designation - the current
            // objective text naming a hero ("kill Jabba", "escort Han") is
            // table text every player reads; the FocusChange vocabulary slot
            // carries an explicit designation the moment the app grows a
            // focus UI. Marks NAME heroes, never positions, and every player
            // sees the same designation - not extra sight.
            public int Marks;                  // how often the mark hit (cumulative)
            public int MarkedRound;            // round of the LAST mark, 0 = never
        }

        public GroupView FindGroup(string id)
        {
            foreach (var g in Groups) if (g.GroupId == id) return g;
            return null;
        }

        /// <summary>Stable canonical text of the whole view - the equality
        /// unit for replay tests and fixture vectors.</summary>
        public string Canon()
        {
            var sb = new StringBuilder();
            sb.Append("mission=").Append(MissionKey).Append('\n');
            sb.Append("round=").Append(Round.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("posture=").Append(Posture).Append('\n');
            sb.Append("objective=").Append(Objective).Append('\n');
            sb.Append("objective_actor=").Append(ObjectiveActor).Append('\n');
            sb.Append("threat=").Append(ThreatBalance.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("threatseen=").Append(ThreatSeen ? 1 : 0).Append('\n');
            sb.Append("seq=").Append(Seq.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var g in Groups)
                sb.Append("G|").Append(g.GroupId).Append('|').Append(g.Name).Append('|')
                  .Append(g.Deployed ? 1 : 0).Append('|').Append(g.Removed ? 1 : 0).Append('|')
                  .Append(g.Alive).Append('|').Append(g.Size).Append('|').Append(g.DeployedRound).Append('|')
                  .Append(g.Health).Append('|').Append(g.VitalityFrac.ToString()).Append('\n');
            foreach (var h in Heroes)
                sb.Append("H|").Append(h.HeroId).Append('|').Append(h.Name).Append('|')
                  .Append(h.Wounded ? 1 : 0).Append('|').Append(h.Defeated ? 1 : 0).Append('|')
                  .Append(h.Activations).Append('|').Append(h.LastActivatedRound).Append('|')
                  .Append(h.Marks).Append('|').Append(h.MarkedRound).Append('\n');
            return sb.ToString();
        }
    }

    public static class WorldEventVector
    {
        /// <summary>Replay the public history into a FeedWorldView. Pure:
        /// same events -> bit-identical view, no side effects.</summary>
        public static FeedWorldView Project(IReadOnlyList<WorldEvent> events,
                                            string missionKey = null,
                                            string historyHash = null)
        {
            var gap = WorldEventFeed.VerifySeq(events);
            if (gap != null)
                throw new InvalidOperationException(
                    "WorldEventVector.Project: seq gap at " + gap
                    + " - demand a Resync (re-log of the complete history), never project a partial window");
            var v = new FeedWorldView
            {
                MissionKey = missionKey ?? "solo",
                Seq = events.Count == 0 ? 0 : events[events.Count - 1].Seq,
                HistoryHash = historyHash ?? WorldEventFeed.HashOf(events),
            };

            // M10b objective attribution (Thomas revision 27.10): the feed
            // knows objective PROGRESS (ObjectiveChange) and WHO ACTIVATED
            // (HeroActivated) - never cell positions. The captor of the last
            // objective step is the hero whose activation FOLLOWS the change
            // ("im Anschluss seine Aktivierung beendet"); if nobody acted
            // after it, the hero who activated just before keeps the credit.
            // Pure replay state - same events, same attribution, resync-safe.
            string lastActivator = null;
            bool attributionPending = false;
            foreach (var e in events)
            {
                if (e.Round > v.Round) v.Round = e.Round;
                switch (e.Kind)
                {
                    case "MissionStart":
                        v.Objective = e.Str("objective") ?? "";
                        v.PriorityTarget = e.Str("priorityTarget") ?? "";
                        v.RoundLimit = e.Long("roundLimit", -1);
                        v.Posture = DerivePosture(e.Str("objective"),
                                                  e.Str("priorityTarget"),
                                                  e.Long("roundLimit", -1));
                        ApplyDesignation(v, v.Objective, e.Round);   // initial public designation
                        break;
                    case "ObjectiveChange":
                        v.Objective = e.Str("objective") ?? v.Objective;
                        ApplyDesignation(v, v.Objective, e.Round);
                        // M10b: provisional credit = last activator before
                        // the change; a later HeroActivated overwrites it
                        // (the hero who finishes his turn AFTER the capture
                        // is the one who did it - public activation order).
                        v.ObjectiveActor = lastActivator ?? "";
                        attributionPending = true;
                        break;
                    case "RoundLimitChanged":
                        // M8/F5: the mission clock is a posture input (clock ->
                        // stall unless protect wins). Re-wire = re-derive with
                        // the MissionStart priority target, exactly the way a
                        // fresh MissionStart would decide.
                        v.RoundLimit = e.Long("roundLimit", -1);
                        v.Posture = DerivePosture(v.Objective, v.PriorityTarget, v.RoundLimit);
                        break;
                    case "ThreatSpend":
                        v.ThreatBalance = e.Long("balance");
                        v.ThreatSeen = true;
                        break;
                    case "Spawn":
                        {
                            var g = Group(v, e.Str("group"));
                            g.Deployed = true;
                            if (g.DeployedRound == 0) g.DeployedRound = e.Round;
                            long size = e.Long("size", 0);
                            if (size > 0)
                            {
                                g.Size = (int)size;
                                g.Alive = Math.Max(g.Alive, g.Size);
                            }
                            break;
                        }
                    case "Death":
                        {
                            var g = Group(v, e.Str("group"));
                            g.Alive = Math.Max(0, g.Alive - 1);
                            break;
                        }
                    case "Removal":
                        Group(v, e.Str("group")).Removed = true;
                        break;
                    case "GroupDamage":
                        {
                            var g = Group(v, e.Str("group"));
                            int fig = (int)e.Long("figure");
                            g.Health = (int)e.Long("health");
                            g.Wheels[fig] = e.Long("wheel");
                            g.FiguresSeen = Math.Max(g.FiguresSeen, fig + 1);
                            break;
                        }
                    case "HeroWounded":
                        Hero(v, e.Str("hero")).Wounded = true;
                        break;
                    case "HeroDefeated":
                        {
                            var h = Hero(v, e.Str("hero"));
                            h.Defeated = true;
                            h.Wounded = true;      // the App marks defeated heroes wounded too
                            break;
                        }
                    case "HeroActivated":
                        {
                            var h = Hero(v, e.Str("hero"));
                            h.Activations++;
                            h.LastActivatedRound = e.Round;
                            lastActivator = e.Str("hero");
                            if (attributionPending)
                            {
                                v.ObjectiveActor = lastActivator;
                                attributionPending = false;
                            }
                            break;
                        }
                    // M6 aim tokens: the focus marker the human sets in the
                    // existing focus panel (public designation, read passively
                    // from the click - ZERO new input). The mark names a hero;
                    // it never claims sight, position or hidden state.
                    case "FocusChange":
                        {
                            var focus = e.Str("focus");
                            if (!string.IsNullOrEmpty(focus))
                            {
                                var h = Hero(v, focus);
                                h.Marks++;
                                h.MarkedRound = e.Round;
                            }
                            break;
                        }
                    // ActivationStart/End, Move, TileChange,
                    // RoundEnd: recorded history for the audit trail and for
                    // consumers that DO have a belief layer; the view itself
                    // only exposes the state facts above.
                }
            }
            return v;
        }

        // ------------------------------------------------------------ posture
        // Mission properties only - public authored data (concept 3.3.1b
        // item 4: "Posture aus Missionseigenschaften ... eliminate ist nur
        // noch Fallback"). Deterministic priority:
        //   1. hold/defend/escort goals -> protect
        //   2. mission clock / rebel progress goals (round limit, escape,
        //      terminals, recovery) -> stall (block the Konten-Punkte)
        //   3. everything else -> eliminate (legacy fallback, honest default)
        // Text matching runs on the ENGLISH authored mission data strings
        // (ui.json localisation happens later; the authored objective text is
        // public table text). Matching is WORD-prefix based: a token matches
        // when it starts with a keyword ("escorted" yes, "stronghold" does
        // NOT match "hold") - deterministic, documented heuristic; every
        // non-match honestly falls back to eliminate.
        static readonly string[] ProtectKeywords =
            { "defend", "protect", "guard", "escort", "hold" };
        static readonly string[] StallKeywords =
            { "escape", "flee", "survive", "reach", "activate", "recover",
              "steal", "deliver", "evacuate" };

        public static string DerivePosture(string objective, string priorityTarget, long roundLimit)
        {
            var words = ((objective ?? "") + " " + (priorityTarget ?? "")).ToLowerInvariant()
                        .Split(new[] { ' ', '\t', '\n', '\r', '-', ',', ';', ':', '.', '!', '?' },
                               StringSplitOptions.RemoveEmptyEntries);
            foreach (var k in ProtectKeywords)
                foreach (var w in words)
                    if (w.StartsWith(k, StringComparison.Ordinal)) return "protect";
            if (roundLimit >= 0) return "stall";
            foreach (var k in StallKeywords)
                foreach (var w in words)
                    if (w.StartsWith(k, StringComparison.Ordinal)) return "stall";
            return "eliminate";
        }

        static FeedWorldView.GroupView Group(FeedWorldView v, string id)
        {
            id = id ?? "?";
            var g = v.FindGroup(id);
            if (g == null)
            {
                g = new FeedWorldView.GroupView { GroupId = id, Name = id };
                v.Groups.Add(g);
                v.Groups.Sort((a, b) => string.CompareOrdinal(a.GroupId, b.GroupId));
            }
            return g;
        }

        static FeedWorldView.HeroView Hero(FeedWorldView v, string id)
        {
            id = id ?? "?";
            foreach (var h in v.Heroes) if (h.HeroId == id) return h;
            var x = new FeedWorldView.HeroView { HeroId = id, Name = id };
            v.Heroes.Add(x);
            v.Heroes.Sort((a, b) => string.CompareOrdinal(a.HeroId, b.HeroId));
            // M6 retro mark: a designation can exist BEFORE the hero first
            // appears in the stream (healthy heroes emit nothing until they
            // act or bleed) - the standing designation names every hero.
            if (NamesHero(v.Objective, id))
            {
                x.Marks++;
                x.MarkedRound = v.Round;
            }
            return x;
        }

        /// <summary>M6 public designation: the current objective text names
        /// this hero as a whole word / whole name (same deterministic
        /// word-splitting law as DerivePosture: 'smuggler' must NOT hit
        /// 'smuggle', 'Han' must not hit 'Hans').</summary>
        static void ApplyDesignation(FeedWorldView v, string text, int round)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var h in v.Heroes)
                if (NamesHero(text, h.HeroId))
                {
                    h.Marks++;
                    h.MarkedRound = round;
                }
        }

        static bool NamesHero(string text, string heroId)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(heroId) || heroId == "?")
                return false;
            var words = SplitWords(text);
            var name = SplitWords(heroId);
            if (name.Count == 0 || words.Count == 0) return false;
            // full-name sequence first (covers names whose parts are all short)
            if (words.Count >= name.Count)
                for (int i = 0; i + name.Count <= words.Count; i++)
                {
                    bool hit = true;
                    for (int j = 0; j < name.Count; j++)
                        if (string.Compare(words[i + j], name[j], StringComparison.OrdinalIgnoreCase) != 0)
                        { hit = false; break; }
                    if (hit) return true;
                }
            // single name part as a word ("kill Jabba" names 'Jabba the Hutt');
            // length>=3 guard: stray 1-2 letter tokens must not designate.
            foreach (var n in name)
                if (n.Length >= 3)
                    foreach (var w in words)
                        if (string.Compare(w, n, StringComparison.OrdinalIgnoreCase) == 0)
                            return true;
            return false;
        }

        static List<string> SplitWords(string text)
        {
            var words = new List<string>();
            var cur = new StringBuilder();
            foreach (var ch in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) cur.Append(ch);
                else { if (cur.Length > 0) { words.Add(cur.ToString()); cur.Length = 0; } }
            }
            if (cur.Length > 0) words.Add(cur.ToString());
            return words;
        }
    }
}
