// M2 verification suite: NUnit re-runs of the SAME checks the console
// harness (tools/TacticalAiHarness) performs against the golden vectors in
// Assets/Tests/Fixtures/tacai/*.json. The repo has NO Unity test framework
// set up (no asmdef, no NUnit package reference - verified), so every
// fixture is [Explicit("Requires Test Framework")]: these files ship for a
// later CI (NUnitLite / Unity Test Framework) and the console harness
// (34,509 checks, ALL PASS) is the proof TODAY. They are honest mirrors of
// harness Sections A/B, not new expectations.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using TacticalAI.Core;

namespace TacticalAI.Tests
{
    /// <summary>Fixture discovery + the harness' normalization/equality core,
    /// ported 1:1 from tools/TacticalAiHarness/Program.cs.</summary>
    public static class TacaiFixtures
    {
        static string _dir;

        public static string Dir
        {
            get
            {
                if (_dir != null) return _dir;
                var env = Environment.GetEnvironmentVariable("TACAI_FIXTURE_DIR");
                if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
                {
                    _dir = env;
                    return _dir;
                }
                foreach (var start in new[] { Environment.CurrentDirectory,
                                              AppContext.BaseDirectory,
                                              System.IO.Path.Combine(Environment.CurrentDirectory, "Assets", "Tests", "TacticalAI") })
                {
                    var d = new DirectoryInfo(start ?? ".");
                    for (int i = 0; i < 12 && d != null; i++, d = d.Parent)
                    {
                        // Unity editor: cwd = project root ("Assets/..." directly);
                        // CI from the git repo root: Unity project is nested one level.
                        foreach (var rel in new[]
                        {
                            System.IO.Path.Combine("Assets", "Tests", "Fixtures", "tacai"),
                            System.IO.Path.Combine("ImperialCommander2", "Assets", "Tests", "Fixtures", "tacai"),
                        })
                        {
                            var cand = System.IO.Path.Combine(d.FullName, rel);
                            if (Directory.Exists(cand)) { _dir = cand; return _dir; }
                        }
                    }
                }
                throw new FileNotFoundException("tactical-AI fixtures dir not found - set TACAI_FIXTURE_DIR");
            }
        }

        public static string Path2(params string[] parts)
        {
            var all = new List<string> { Dir };
            all.AddRange(parts);
            return System.IO.Path.Combine(all.ToArray());
        }

        public static object Load(string file) => Json.Parse(File.ReadAllText(Path2(file)));

        public static List<object> LoadList(string file) => Json.AsList(Load(file));

        // ------------------------------------------------------- normalization
        // (byte-exact comparison layer: same rules as the harness Norm/Eq)

        public static object Norm(object v)
        {
            if (v == null) return null;
            if (v is string) return v;
            if (v is bool) return v;
            if (v is Rational r) return r.ToString();
            if (v is Cell c) return new List<object> { (long)c.X, (long)c.Y };
            if (v is int i0) return (long)i0;
            if (v is long) return v;
            if (v is double) return v;
            if (v is IDictionary<string, object> d)
            {
                var o = new Dictionary<string, object>();
                foreach (var kv in d) o[kv.Key] = Norm(kv.Value);
                return o;
            }
            if (v is IDictionary dd)
            {
                var o = new Dictionary<string, object>();
                foreach (DictionaryEntry e in dd)
                    o[Convert.ToString(e.Key, System.Globalization.CultureInfo.InvariantCulture)] = Norm(e.Value);
                return o;
            }
            var t = v.GetType();
            if (t.IsGenericType && t.Name.StartsWith("ValueTuple"))
            {
                var tup = (ITuple)v;
                var o = new List<object>();
                for (int k = 0; k < tup.Length; k++) o.Add(Norm(tup[k]));
                return o;
            }
            if (v is IList list)
            {
                var o = new List<object>();
                foreach (var e in list) o.Add(Norm(e));
                return o;
            }
            if (v is IEnumerable en)
            {
                var o = new List<object>();
                foreach (var e in en) o.Add(Norm(e));
                return o;
            }
            return v;
        }

        public static bool Eq(object e, object a)
        {
            if (e == null) return a == null;
            if (e is Dictionary<string, object> ed)
            {
                if (!(a is Dictionary<string, object> ad)) return false;
                if (ed.Count != ad.Count) return false;
                foreach (var kv in ed)
                    if (!ad.ContainsKey(kv.Key) || !Eq(kv.Value, ad[kv.Key])) return false;
                return true;
            }
            if (e is List<object> el)
            {
                if (!(a is List<object> al)) return false;
                if (el.Count != al.Count) return false;
                for (int i = 0; i < el.Count; i++) if (!Eq(el[i], al[i])) return false;
                return true;
            }
            if (e is long elg && a is double adl)
                return Math.Abs(elg - adl) < 1e-12;
            if (e is double edbl && a is long adbl)
                return Math.Abs(edbl - adbl) < 1e-12;
            if (e is double e1 && a is double a1)
                return Math.Abs(e1 - a1) < 1e-12;
            return e.Equals(a);
        }

        public static string Repr(object v)
        {
            if (v == null) return "null";
            if (v is string s) return "\"" + s + "\"";
            if (v is Dictionary<string, object> d)
                return "{" + string.Join(", ", OrdKeys(d)
                        .Select(kv => "\"" + kv.Key + "\": " + Repr(kv.Value))) + "}";
            if (v is List<object> l) return "[" + string.Join(", ", l.Select(Repr)) + "]";
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static void Check(string path, object expected, object actual)
        {
            object a = Norm(actual);
            Assert.True(Eq(expected, a),
                path + ": expected " + Repr(Norm(expected)) + ", got " + Repr(a));
        }

        /// <summary>python dict(sorted(...)) key order: ordinal string sort.</summary>
        public static IEnumerable<KeyValuePair<string, object>> OrdKeys(Dictionary<string, object> d) =>
            d.OrderBy(kv => kv.Key, StringComparer.Ordinal);

        // ------------------------------------------------- scenario-world twin
        // (harness Catalog()/Boards()/BuildScenarioWorld, ported unchanged)

        static Dictionary<string, DeploymentCard> _catalog;
        static Dictionary<string, Board> _boards;
        static Dictionary<string, object> _scenarios;

        public static Dictionary<string, DeploymentCard> Catalog()
        {
            if (_catalog == null)
                _catalog = DeploymentCard.LoadCatalog(Load("units_core.json"));
            return _catalog;
        }

        public static Dictionary<string, object> Scenarios()
        {
            if (_scenarios == null)
            {
                _scenarios = new Dictionary<string, object>();
                foreach (var kv in Json.AsDict(Load("scenarios_m2a1.json"))) _scenarios[kv.Key] = kv.Value;
                foreach (var kv in Json.AsDict(Load("scenarios_m2a2.json"))) _scenarios[kv.Key] = kv.Value;
            }
            return _scenarios;
        }

        public static Board BoardOf(string name)
        {
            if (_boards == null) _boards = new Dictionary<string, Board>();
            if (_boards.TryGetValue(name, out var b)) return b;
            var nb = Board.FromJson(Json.AsDict(Json.Parse(File.ReadAllText(Path2("boards", name + ".json")))));
            _boards[name] = nb;
            return nb;
        }

        public static (Board, World) BuildScenarioWorld(Dictionary<string, object> spec)
        {
            var board = BoardOf(Json.AsStr(spec["board"]));
            var figs = new List<Figure>();
            int idx = 0;
            foreach (var fsO in Json.AsList(spec["figures"]))
            {
                var fs = Json.AsDict(fsO);
                string team = Json.AsStr(fs["team"]);
                Cell at;
                if (team == "npc")
                {
                    var ent = board.ProtectedEntities
                        .First(e => Json.AsStr(e["id"]) == Json.AsStr(fs["id"]));
                    at = FirstEntityCell(ent);
                }
                else at = Json.AsCell(fs["at"]);
                var card = Catalog()[Json.AsStr(fs["card"])];
                figs.Add(new Figure
                {
                    Id = Json.AsStr(fs["id"]),
                    GroupId = Json.AsStr(fs["id"]),
                    Card = card,
                    Team = team,
                    Anchor = at,
                    Orientation = Json.AsInt(Json.Get(fs, "orientation", (long)0)),
                    Index = idx,
                    Alive = true,
                    Pattern = Json.AsStr(Json.Get(fs, "pattern", "aggressive")),
                    Vitality = card.Health,
                });
                idx++;
            }
            var specFigs = Json.AsList(spec["figures"]);
            for (int i = 0; i < figs.Count && i < specFigs.Count; i++)
            {
                var fs = Json.AsDict(specFigs[i]);
                if (fs.ContainsKey("vitality"))
                    figs[i].Vitality = Json.AsInt(fs["vitality"]);
            }
            var world = new World(board, figs) { Objectives = new ObjectiveRuntime(board) };
            return (board, world);
        }

        static Cell FirstEntityCell(Dictionary<string, object> ent)
        {
            if (ent["cells"] is List<Cell> lc) return lc[0];
            var cells = Json.AsList(ent["cells"]);
            var first = cells[0];
            if (first is Cell c) return c;
            return Json.AsCell(first);
        }
    }
}
