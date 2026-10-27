// M5 world-feed vectors: world_feed.json pins the DETERMINISTIC projection
// of a public-event history into the planner-facing FeedWorldView
// (posture derivation, wheel vitality, hero flags, threat, seq + SHA-256
// history hash). The app layer owns this vocabulary (the sim has no app
// feed), so this file is the C#-side canonical vector: the console harness
// and the NUnit editor suite assert against the SAME fixture.
//
// expect blocks were recorded once with `--record` (see Program.cs) from
// the Core implementation, reviewed, then pinned - never hand-written.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        // set by the `--record` CLI flag (parsed in Program.Main)
        public static bool Record;

        static void SecWorldFeed()
        {
            var v = LoadJson("world_feed.json");
            var cases = Json.AsList(v["cases"]);
            var recorded = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var raw in cases)
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var events = BuildEvents(Json.AsList(c["events"]));

                // replay twice: the projection must be a pure function
                var view1 = WorldEventVector.Project(events, "vector|" + name);
                var view2 = WorldEventVector.Project(events, "vector|" + name);
                Check(name + ".replay_canon", view1.Canon(), view2.Canon());
                Check(name + ".replay_hash", view1.HistoryHash, view2.HistoryHash);

                var got = new Dictionary<string, object>
                {
                    { "posture", view1.Posture },
                    { "objective", view1.Objective },
                    { "round", (long)view1.Round },
                    { "threat", view1.ThreatBalance },
                    { "seq", (long)view1.Seq },
                    { "hash", view1.HistoryHash },
                    { "canon", view1.Canon() },
                };

                if (Record) { recorded[name] = got; continue; }

                if (!c.ContainsKey("expect"))
                {
                    Check(name + ".expect", "present", "missing - run with --record");
                    continue;
                }
                var exp = Json.AsDict(c["expect"]);
                foreach (var k in new[] { "posture", "objective", "round", "threat", "seq", "hash", "canon" })
                    Check(name + "." + k, exp[k], got[k]);
            }
            if (Record)
            {
                var path = Path.Combine(FIX, "world_feed_record.json");
                File.WriteAllText(path, DumpJson(recorded));
                Console.WriteLine("   (record) wrote " + path + " for " + recorded.Count + " cases");
            }
        }

        static List<WorldEvent> BuildEvents(List<object> raws)
        {
            var list = new List<WorldEvent>();
            int seq = 1;
            foreach (var raw in raws)
            {
                var e = Json.AsDict(raw);
                var payload = e.ContainsKey("payload")
                    ? new Dictionary<string, object>(Json.AsDict(e["payload"]))
                    : new Dictionary<string, object>();
                list.Add(new WorldEvent(Json.AsStr(e["kind"]),
                                        (int)Json.AsLong(e["round"]), payload) { Seq = seq++ });
            }
            return list;
        }

        // minimal JSON writer for the record side-file (strings/longs/nested)
        public static string DumpJson(object o)
        {
            var sb = new StringBuilder();
            WriteJson(sb, o);
            return sb.ToString();
        }

        static void WriteJson(StringBuilder sb, object o)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is bool) { sb.Append(((bool)o) ? "true" : "false"); return; }
            if (o is long || o is int) { sb.Append(Convert.ToInt64(o).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is double) { sb.Append(((double)o).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (o is string)
            {
                sb.Append('"');
                foreach (var ch in (string)o)
                {
                    switch (ch)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                            else sb.Append(ch);
                            break;
                    }
                }
                sb.Append('"');
                return;
            }
            if (o is Dictionary<string, object>)
            {
                var d = (Dictionary<string, object>)o;
                var keys = new List<string>(d.Keys);
                keys.Sort(StringComparer.Ordinal);
                sb.Append('{');
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteJson(sb, keys[i]);
                    sb.Append(':');
                    WriteJson(sb, d[keys[i]]);
                }
                sb.Append('}');
                return;
            }
            if (o is List<object>)
            {
                var l = (List<object>)o;
                sb.Append('[');
                for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(','); WriteJson(sb, l[i]); }
                sb.Append(']');
                return;
            }
            throw new InvalidOperationException("DumpJson: unsupported type " + o.GetType());
        }
    }
}
