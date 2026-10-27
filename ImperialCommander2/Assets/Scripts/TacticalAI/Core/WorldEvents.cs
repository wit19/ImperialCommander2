// M5 (concept 3.3.1b, v4.0 Thomas-Entscheid 27.10): PASSIVE world feed.
// An append-only, per-mission event history built ONLY from signals the App
// produces anyway while playing - canon rule: no feature may ever require
// the human to report positions or states to the App (zero new input).
//
// LEAK INVARIANT (principle 5 / 7.1): every payload is public table
// knowledge - names, zone labels, rounds, damage-WHEEL values, threat
// balance. Hidden information (hero cards, dice, hands, medpacks, villain
// deck) has no home in this vocabulary: Append() rejects unknown payload
// keys fail-loud, and the kind/key tables below are the whole surface.
//
// Pure C# (no Unity): the runtime capture lives in WorldEventsUnity.cs, the
// deterministic projection into a planner-facing view in WorldEventVector.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TacticalAI.Core
{
    public sealed class WorldEvent
    {
        public int Seq;                 // 1-based, contiguous per mission
        public string Kind;             // must be in WorldEventFeed.Kinds
        public int Round;               // gameVars.round at emit time
        public Dictionary<string, object> Payload;

        public WorldEvent(string kind, int round, Dictionary<string, object> payload)
        {
            Kind = kind;
            Round = round;
            Payload = payload ?? new Dictionary<string, object>();
        }

        public string Str(string key)
        {
            object v;
            return Payload.TryGetValue(key, out v) ? Json.AsStr(v) : null;
        }
        public long Long(string key, long dflt = 0)
        {
            object v;
            return Payload.TryGetValue(key, out v) ? Json.AsLong(v) : dflt;
        }
    }

    /// <summary>Append-only public-event history for ONE mission run. Seq is
    /// contiguous by construction; consumers that read a stale window detect
    /// the gap with VerifySeq and demand a Resync (re-log of the complete
    /// history from current App state - never a guess).</summary>
    public sealed class WorldEventFeed
    {
        // M5 event vocabulary (task t_006ccc2b Scope B). Kinds the App can
        // derive passively TODAY are wired in WorldEventsUnity; Move and
        // TileChange are vocabulary slots reserved for the tile-graph read
        // (concept 3.3.1b items 1-2, open point 3) - no producer yet, by
        // design: inventing movement from beliefs would violate the
        // no-estimation rule.
        public static readonly HashSet<string> Kinds = new HashSet<string>
        {
            "MissionStart", "Spawn", "Move", "Death", "Removal",
            "ObjectiveChange", "TileChange", "FocusChange", "ThreatSpend",
            "ActivationStart", "ActivationEnd",
            "HeroActivated", "HeroWounded", "HeroDefeated",
            "GroupDamage", "RoundEnd",
            // M8/F5: mission clock re-wired at runtime (ModifyRndLimit event
            // or the roundLimitToggle flip) - public table info, the feed
            // gate re-derives posture from it.
            "RoundLimitChanged",
        };

        // The COMPLETE payload surface. Anything else (a die value, a hero
        // card field, a hidden counter) cannot enter the feed.
        public static readonly HashSet<string> PayloadKeys = new HashSet<string>
        {
            "group", "figure", "wheel", "health", "hero", "zone", "tile",
            "objective", "focus", "balance", "roundLimit", "priorityTarget",
            "mission", "missionType", "resync", "size",
        };

        // Minimal per-kind shape guard (typos fail loud, not silently).
        static readonly Dictionary<string, string[]> RequiredKeys =
            new Dictionary<string, string[]>
        {
            { "MissionStart",     new[] { "objective", "priorityTarget", "roundLimit", "mission" } },
            { "Spawn",            new[] { "group" } },
            { "Move",             new[] { "group", "zone" } },
            { "Death",            new[] { "group" } },
            { "Removal",          new[] { "group" } },
            { "ObjectiveChange",  new[] { "objective" } },
            { "TileChange",       new[] { "tile" } },
            { "FocusChange",      new[] { "focus" } },
            { "ThreatSpend",      new[] { "balance" } },
            { "ActivationStart",  new[] { "group" } },
            { "ActivationEnd",    new[] { "group" } },
            { "HeroActivated",    new[] { "hero" } },
            { "HeroWounded",      new[] { "hero" } },
            { "HeroDefeated",     new[] { "hero" } },
            { "GroupDamage",      new[] { "group", "figure", "wheel", "health" } },
            { "RoundEnd",         new string[0] },
            { "RoundLimitChanged", new[] { "roundLimit" } },
        };

        public readonly string MissionKey;
        readonly List<WorldEvent> _events = new List<WorldEvent>();
        int _resyncs;

        public WorldEventFeed(string missionKey)
        {
            MissionKey = missionKey ?? "solo";
        }

        public IReadOnlyList<WorldEvent> Events => _events;
        public int Count => _events.Count;
        public int LastSeq => _events.Count == 0 ? 0 : _events[_events.Count - 1].Seq;
        public int ResyncCount => _resyncs;

        /// <summary>Append one public event; seq = count+1 (contiguous by
        /// construction). Throws on unknown kind / payload key (leak guard).</summary>
        public WorldEvent Append(string kind, int round, Dictionary<string, object> payload)
        {
            Validate(kind, payload);
            var e = new WorldEvent(kind, Math.Max(0, round), payload)
            { Seq = _events.Count + 1 };
            _events.Add(e);
            return e;
        }

        /// <summary>Replace the history with a full re-log (seq must come in
        /// contiguous from 1). Used when a consumer saw a seq gap: the whole
        /// truth is re-logged from current App state, never estimated.</summary>
        public void Resync(IEnumerable<WorldEvent> fullLog)
        {
            var rebuilt = new List<WorldEvent>();
            int expect = 1;
            foreach (var e in fullLog)
            {
                Validate(e.Kind, e.Payload);
                if (e.Seq != expect)
                    throw new InvalidOperationException(
                        "WorldEventFeed.Resync: seq gap in re-log (expected "
                        + expect + ", got " + e.Seq + ")");
                rebuilt.Add(new WorldEvent(e.Kind, e.Round, e.Payload) { Seq = expect++ });
            }
            _events.Clear();
            _events.AddRange(rebuilt);
            _resyncs++;
        }

        static void Validate(string kind, Dictionary<string, object> payload)
        {
            if (kind == null || !Kinds.Contains(kind))
                throw new ArgumentException("WorldEventFeed: unknown event kind " + kind);
            string[] req;
            if (!RequiredKeys.TryGetValue(kind, out req)) req = new string[0];
            foreach (var k in req)
                if (payload == null || !payload.ContainsKey(k))
                    throw new ArgumentException("WorldEventFeed: " + kind + " requires key " + k);
            if (payload == null) return;
            foreach (var k in payload.Keys)
                if (!PayloadKeys.Contains(k))
                    throw new ArgumentException("WorldEventFeed: payload key not public-vocabulary: " + k);
        }

        /// <summary>First missing seq in a consumer's window, or null when
        /// contiguous 1..n. A non-null answer mandates Resync, not guessing.</summary>
        public static int? VerifySeq(IReadOnlyList<WorldEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i].Seq != i + 1) return i + 1;
            return null;
        }

        // -------------------------------------------------------------- hash
        /// <summary>SHA-256 over the canonical history text. Deterministic:
        /// payload keys sorted ordinal, invariant-culture scalars.</summary>
        public string Hash() => HashOf(_events);

        public static string HashOf(IReadOnlyList<WorldEvent> events)
        {
            var sb = new StringBuilder();
            foreach (var e in events) sb.Append(CanonicalLine(e)).Append('\n');
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        public static string CanonicalLine(WorldEvent e)
        {
            return e.Seq.ToString(CultureInfo.InvariantCulture) + "|" + e.Kind + "|"
                 + e.Round.ToString(CultureInfo.InvariantCulture) + "|" + CanonPayload(e.Payload);
        }

        public static string CanonPayload(Dictionary<string, object> payload)
        {
            if (payload == null || payload.Count == 0) return "{}";
            var keys = new List<string>(payload.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder("{");
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(keys[i]).Append(":").Append(CanonValue(payload[keys[i]]));
            }
            return sb.Append("}").ToString();
        }

        static string CanonValue(object v)
        {
            if (v == null) return "~";
            if (v is bool) return ((bool)v) ? "1" : "0";
            if (v is string) return "\"" + ((string)v).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            if (v is double) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            if (v is float) return ((float)v).ToString("R", CultureInfo.InvariantCulture);
            if (v is IList<object>)
            {
                var list = (IList<object>)v;
                var parts = new List<string>();
                foreach (var x in list) parts.Add(CanonValue(x));
                return "[" + string.Join(",", parts.ToArray()) + "]";
            }
            if (v is Dictionary<string, object>)
                return CanonPayload((Dictionary<string, object>)v);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }
}
