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
        public long ThreatBalance;
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

            /// <summary>Remaining group vitality as exact Rational: alive HP
            /// over full card health of every figure the group deployed with
            /// (FINAL-QA audit #10 group-granularity semantics). A group
            /// without ANY wheel event is UNTOUCHED -> 1 (never guessed from
            /// absence). Wheels are the human's damage wheels: absolute
            /// wheel values are public facts; the health denominator is the
            /// printed card health that the event itself carries.</summary>
            public Rational VitalityFrac
            {
                get
                {
                    if (Wheels.Count == 0 || Health <= 0) return Rational.One;
                    int n = Math.Max(1, Math.Max(Size, FiguresSeen));
                    long hp = (long)Health * n;
                    long sum = 0;
                    foreach (var kv in Wheels) sum += Math.Max(0, Math.Min(Health, kv.Value));
                    long alive = Math.Max(0, Math.Min(n, Alive));
                    long cap = (long)Health * alive;
                    return new Rational(Math.Min(sum, cap), hp <= 0 ? 1 : hp);
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
            sb.Append("threat=").Append(ThreatBalance.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("seq=").Append(Seq.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var g in Groups)
                sb.Append("G|").Append(g.GroupId).Append('|').Append(g.Name).Append('|')
                  .Append(g.Deployed ? 1 : 0).Append('|').Append(g.Removed ? 1 : 0).Append('|')
                  .Append(g.Alive).Append('|').Append(g.Size).Append('|').Append(g.DeployedRound).Append('|')
                  .Append(g.Health).Append('|').Append(g.VitalityFrac.ToString()).Append('\n');
            foreach (var h in Heroes)
                sb.Append("H|").Append(h.HeroId).Append('|').Append(h.Name).Append('|')
                  .Append(h.Wounded ? 1 : 0).Append('|').Append(h.Defeated ? 1 : 0).Append('|')
                  .Append(h.Activations).Append('|').Append(h.LastActivatedRound).Append('\n');
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

            foreach (var e in events)
            {
                if (e.Round > v.Round) v.Round = e.Round;
                switch (e.Kind)
                {
                    case "MissionStart":
                        v.Objective = e.Str("objective") ?? "";
                        v.Posture = DerivePosture(e.Str("objective"),
                                                  e.Str("priorityTarget"),
                                                  e.Long("roundLimit", -1));
                        break;
                    case "ObjectiveChange":
                        v.Objective = e.Str("objective") ?? v.Objective;
                        break;
                    case "ThreatSpend":
                        v.ThreatBalance = e.Long("balance");
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
                            break;
                        }
                    // ActivationStart/End, Move, TileChange, FocusChange,
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
        // public table text).
        static readonly string[] ProtectKeywords =
            { "defend", "hold", "protect", "guard", "escort", "secure" };
        static readonly string[] StallKeywords =
            { "escape", "flee", "survive", "reach", "activate", "recover",
              "steal", "deliver", "open", "smuggle", "evacuate" };

        public static string DerivePosture(string objective, string priorityTarget, long roundLimit)
        {
            var text = ((objective ?? "") + " " + (priorityTarget ?? "")).ToLowerInvariant();
            foreach (var k in ProtectKeywords)
                if (text.Contains(k)) return "protect";
            if (roundLimit >= 0) return "stall";
            foreach (var k in StallKeywords)
                if (text.Contains(k)) return "stall";
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
            return x;
        }
    }
}
