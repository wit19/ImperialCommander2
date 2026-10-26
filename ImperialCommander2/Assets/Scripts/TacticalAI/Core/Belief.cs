// Port of sim/belief.py — Zonen-Beliefs, the doctrine layer's world view
// (concept §3.3.3 v3.6, M2a.2 + M2a.3 activation-inference rings).
// Two-level operation:
//   * WorldFeed  — reads world truth, publishes ONLY public events, zone
//     facts and metrics; doctrine_view() is the single sanctioned bridge.
//   * AppView    — the doctrine layer's entire input surface: structurally
//     NO rebel positions; Public()/Dump() are coordinate-free by design and
//     PositionScan()/AssertPositionFree() double-check against living rebel
//     footprints (structural leak check).
// Golden vectors: Assets/Tests/Fixtures/tacai/belief.json + rings.json.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
// Alias: inside WorldFeed/ActivationInference the property `Belief`
// (a BeliefState) shadows the static helper class `Belief`.
using BS = TacticalAI.Core.Belief;

namespace TacticalAI.Core
{
    public static class Belief
    {
        // public event vocabulary (what the app logs anyway + round end)
        public static readonly string[] EventKinds =
        {
            "door_opened", "objective_worked", "objective_done",
            "item_taken", "overwatch_placed", "signal_seen", "round_end",
            // M2a.3 (concept 3.3.3 Nr. 2b v3.8b): activation-inference rings
            // are ZONE beliefs — distance bands, never a point.
            "hero_ring", "counter_ring", "defeat_ring",
        };
        public static readonly string[] RingKinds = { "hero_ring", "counter_ring", "defeat_ring" };

        internal static readonly Regex CoordRe =
            new Regex(@"\[\s*-?\d+\s*,\s*-?\d+\s*\]|\(\s*-?\d+\s*,\s*-?\d+\s*\)");

        internal static readonly Dictionary<string, string> RoleWords =
            new Dictionary<string, string>
        {
            { "terminal", "Terminalraum" }, { "item", "Objektraum" },
            { "escape", "Fluchtraum" }, { "guard_zone", "Wachzone" },
        };

        // ---------------------------------------------------------- door edge utils
        /// Python str((a, b)) for a door edge key: "((9, 3), (10, 3))".
        /// Used ONLY for sort-order parity with the Python feed diff.
        public static string EdgeKeyStr((Cell, Cell) k) =>
            "((" + k.Item1.X + ", " + k.Item1.Y + "), (" + k.Item2.X + ", " + k.Item2.Y + "))";

        public static Dictionary<(Cell, Cell), string> DoorLabels(Board board)
        {
            /// Deterministic fixture-legible door naming: reading order of the
            /// tile plan (sorted by (y, x) of the edge's first endpoint) ->
            /// 'Tür A', 'Tür B' ...
            var keys = new List<(Cell, Cell)>(board.Doors.Keys);
            keys.Sort((a, b) =>
            {
                int c = a.Item1.Y.CompareTo(b.Item1.Y);
                return c != 0 ? c : a.Item1.X.CompareTo(b.Item1.X);
            });
            var outp = new Dictionary<(Cell, Cell), string>();
            for (int i = 0; i < keys.Count; i++)
                outp[keys[i]] = i < 26 ? "Tür " + (char)('A' + i) : "Tür " + (i + 1);
            return outp;
        }

        public static Dictionary<int, string> ZoneLabels(Board board, ZoneMap zmap,
                                                         Dictionary<(Cell, Cell), string> dlabels)
        {
            /// Fixture-legible zone names; collisions suffixed by ordinal in
            /// zone order. Fully deterministic.
            var feats = new Dictionary<int, List<string>>();
            foreach (var z in zmap.Cells.Keys) feats[z] = new List<string>();
            foreach (var o in board.Objectives)
            {
                Cell first = o.Cells.Count > 0 ? o.Cells[0] : new Cell(0, 0);
                int z = zmap.ZoneOfCell(first);
                string word = RoleWords.TryGetValue(o.Role, out var w) ? w : "Raum";
                if (z >= 0) feats[z].Add(word + " " + o.Id);
            }
            foreach (var e in board.ProtectedEntities)
            {
                var cells = VictoryHelpers.EntityCells(e);
                if (cells.Count > 0)
                {
                    int z = zmap.ZoneOfCell(cells[0]);
                    if (z >= 0) feats[z].Add("Wachraum " + (e.TryGetValue("id", out var id) ? id : null));
                }
            }
            var dps = new List<DeploymentPoint>(board.DeploymentPoints);
            dps.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            for (int i = 0; i < dps.Count; i++)
            {
                if (dps[i].Cells.Count > 0)
                {
                    int z = zmap.ZoneOfCell(dps[i].Cells[0]);
                    if (z >= 0)
                        feats[z].Add(dps.Count == 1 ? "Ankunftszone" : "Ankunftszone " + (i + 1));
                }
            }
            var labels = new Dictionary<int, string>();
            foreach (var z in SortedKeys(zmap.Cells))
            {
                if (feats[z].Count > 0)
                {
                    labels[z] = string.Join(" + ", OrdSorted(feats[z]));
                    continue;
                }
                var touched = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var kv in board.Doors)
                    if (zmap.ZoneOfCell(kv.Key.Item1) == z || zmap.ZoneOfCell(kv.Key.Item2) == z)
                        touched.Add(dlabels[kv.Key]);
                labels[z] = touched.Count > 0 ? "Raum an " + string.Join(" und ", touched) : "Korridor";
            }
            var seen = new Dictionary<string, int>();
            foreach (var z in SortedKeys(labels))
            {
                string baseLabel = labels[z];
                int n = seen.TryGetValue(baseLabel, out var s) ? s : 0;
                seen[baseLabel] = n + 1;
                if (n != 0) labels[z] = baseLabel + " " + (n + 1);
            }
            return labels;
        }

        internal static List<int> SortedKeys<T>(Dictionary<int, T> d)
        {
            var ks = new List<int>(d.Keys);
            ks.Sort();
            return ks;
        }

        internal static List<string> OrdSorted(IEnumerable<string> xs)
        {
            var l = new List<string>(xs);
            l.Sort(StringComparer.Ordinal);
            return l;
        }

        public static Dictionary<int, List<(string, int)>> ZoneDoorGraph(
            ZoneMap zmap, Dictionary<(Cell, Cell), string> dlabels)
        {
            /// Zone adjacency through doors: {zone: sorted [(label, other)]}.
            var adj = new Dictionary<int, List<(string, int)>>();
            foreach (var z in zmap.Cells.Keys) adj[z] = new List<(string, int)>();
            foreach (var kv in dlabels)
            {
                int za = zmap.ZoneOfCell(kv.Key.Item1), zb = zmap.ZoneOfCell(kv.Key.Item2);
                if (za != zb)
                {
                    adj[za].Add((kv.Value, zb));
                    adj[zb].Add((kv.Value, za));
                }
            }
            foreach (var z in adj.Keys)
                adj[z].Sort((a, b) =>
                {
                    int c = string.CompareOrdinal(a.Item1, b.Item1);
                    return c != 0 ? c : a.Item2.CompareTo(b.Item2);
                });
            return adj;
        }

        public static (List<string>, List<int>) ZonePath(Dictionary<int, List<(string, int)>> adj,
                                                         int start, int goal)
        {
            /// BFS zone path; (door labels to pass, zones) or (null, null).
            /// Deterministic: neighbours visited in sorted order.
            if (start == goal) return (new List<string>(), new List<int> { start });
            var prev = new Dictionary<int, (int?, string)> { [start] = (null, null) };
            var frontier = new List<int> { start };
            while (frontier.Count > 0)
            {
                var nxt = new List<int>();
                foreach (var z in frontier)
                    foreach (var (lab, other) in adj.TryGetValue(z, out var a) ? a : new List<(string, int)>())
                    {
                        if (prev.ContainsKey(other)) continue;
                        prev[other] = (z, lab);
                        if (other == goal)
                        {
                            var doors = new List<string>();
                            var zones = new List<int>();
                            int? cur = other;
                            while (cur is { } cu)
                            {
                                zones.Add(cu);
                                var (pz, pl) = prev[cu];
                                if (pl != null) doors.Add(pl);
                                cur = pz;
                            }
                            doors.Reverse();
                            zones.Reverse();
                            return (doors, zones);
                        }
                        nxt.Add(other);
                    }
                nxt.Sort((x, y) => y.CompareTo(x));   // descending: -z
                frontier = nxt;
            }
            return (null, null);
        }

        public static string LabelForCell(BeliefState belief, Cell cell)
        {
            int z = belief.Zmap.ZoneOfCell(cell);
            return z >= 0 ? belief.Labels[z] : "?";
        }
    }

    /// Rooms/corridors = connected components of standable cells when the
    /// movement graph is CUT AT DOOR EDGES (plus walls/impassable cells): a
    /// door always connects two distinct zones — the geographic Konten-Punkt
    /// a doctrine names.
    public sealed class ZoneMap
    {
        public readonly Board Board;
        public readonly Dictionary<Cell, int> ZoneOf;
        public readonly Dictionary<int, List<Cell>> Cells;
        public readonly int N;

        static readonly Cell[] NeighbourOrder =
            { new Cell(0, 1), new Cell(0, -1), new Cell(1, 0), new Cell(-1, 0) };

        public ZoneMap(Board board)
        {
            Board = board;
            var stand = new List<Cell>();
            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                    if (!board.IsImpassableCell(new Cell(x, y))) stand.Add(new Cell(x, y));
            stand.Sort();   // Python sorted(stand): x then y — zone-id order
            var standSet = new HashSet<Cell>(stand);
            var compOf = new Dictionary<Cell, int>();
            var comps = new List<HashSet<Cell>>();
            foreach (var c in stand)
            {
                if (compOf.ContainsKey(c)) continue;
                var stack = new List<Cell> { c };
                var comp = new List<Cell>();
                compOf[c] = comps.Count;
                while (stack.Count > 0)
                {
                    var cur = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    comp.Add(cur);
                    foreach (var dv in NeighbourOrder)
                    {
                        var nb = new Cell(cur.X + dv.X, cur.Y + dv.Y);
                        if (standSet.Contains(nb) && !compOf.ContainsKey(nb)
                            && board.DoorAt(cur, nb) is null
                            && board.BlocksMovement(cur, nb) is null)
                        {
                            compOf[nb] = comps.Count;
                            stack.Add(nb);
                        }
                    }
                }
                comps.Add(new HashSet<Cell>(comp));
            }
            ZoneOf = compOf;
            Cells = new Dictionary<int, List<Cell>>();
            for (int z = 0; z < comps.Count; z++) Cells[z] = new List<Cell>();
            foreach (var kv in compOf) Cells[kv.Value].Add(kv.Key);
            N = comps.Count;
        }

        public int ZoneOfCell(Cell cell) => ZoneOf.TryGetValue(cell, out var z) ? z : -1;
    }

    // ---------------------------------------------------------------- BeliefState
    /// heat = accumulated event mass (halves per event-less round);
    /// prior = static geometric expectation from entrances -> objectives.
    /// belief display = prior + heat; per-zone only, never per figure.
    public sealed class BeliefState
    {
        public readonly Board Board;
        public readonly ZoneMap Zmap;
        public readonly Dictionary<int, string> Labels;
        public Dictionary<int, Rational> Prior;
        public Dictionary<int, Rational> Heat;
        public Dictionary<int, int> LastEventRound;
        public readonly List<Dictionary<string, object>> Feed = new List<Dictionary<string, object>>();

        static readonly HashSet<string> KindSet = new HashSet<string>(Belief.EventKinds);

        public BeliefState(Board board, ZoneMap zmap, Dictionary<int, string> labels,
                           Dictionary<int, Rational> priorWeights)
        {
            Board = board;
            Zmap = zmap;
            Labels = labels;
            Prior = new Dictionary<int, Rational>();
            Heat = new Dictionary<int, Rational>();
            LastEventRound = new Dictionary<int, int>();
            foreach (var z in Belief.SortedKeys(zmap.Cells))
            {
                Prior[z] = priorWeights.TryGetValue(z, out var p) ? p : Rational.Zero;
                Heat[z] = Rational.Zero;
                LastEventRound[z] = 0;
            }
        }

        /// Events feed the belief ONLY through published kinds; zones from
        /// cells, never per-figure claims (§3.3.3 #1).
        public void Observe(string kind, IEnumerable<Cell> cells, int roundNo,
                            string objId = "", string role = "")
        {
            if (!KindSet.Contains(kind))
                throw new ArgumentException("unknown belief event '" + kind + "'");
            var zids = new HashSet<int>();
            foreach (var c in cells)
            {
                int z = Zmap.ZoneOfCell(c);
                if (z >= 0) zids.Add(z);
            }
            // zones in ascending zone-id order (Python: [labels[z] for z in
            // sorted(zids)] — zone order, NOT label order)
            var entry = new Dictionary<string, object>
            {
                { "round", roundNo },
                { "kind", kind },
                { "zones", SortedZids(zids).ConvertAll(z => Labels[z]) },
            };
            if (!string.IsNullOrEmpty(objId)) entry["objekt"] = objId;
            Feed.Add(entry);
            var zl = new List<int>(zids);
            if (kind == "objective_worked")
            {
                Bump(zl, 2, roundNo);
            }
            else if (kind == "objective_done" || kind == "item_taken")
            {
                Bump(zl, 3, roundNo);
                // completed terminal/item: rebels were here and will head for
                // the exit (fingerprint, §3.3.3 #1 — zone hint, no figure claim)
                string esc = !string.IsNullOrEmpty(role) ? role
                    : (kind == "objective_done" ? "terminal" : "item");
                if (esc == "terminal" || esc == "item")
                    foreach (var z in Belief.SortedKeys(Zmap.Cells))
                        if (ZoneHasEscape(z)) Bump(new List<int> { z }, 2, roundNo);
            }
            else if (kind == "door_opened")
            {
                Bump(zl, 1, roundNo);
                foreach (var z in zl)
                    foreach (var nb in NeighbourZones(z))
                        Bump(new List<int> { nb }, 1, roundNo);
            }
            else if (kind == "overwatch_placed" || kind == "signal_seen")
            {
                Bump(zl, 2, roundNo);   // own token / explicit human tap
            }
            else if (kind == "hero_ring")
            {
                Bump(zl, 1, roundNo);   // attributed hero band (§3.3.3 2b)
            }
            else if (kind == "counter_ring" || kind == "defeat_ring")
            {
                Bump(zl, 2, roundNo);   // hero in own range / kill site
            }
        }

        static List<int> SortedZids(HashSet<int> zids)
        {
            var l = new List<int>(zids);
            l.Sort();
            return l;
        }

        void Bump(IEnumerable<int> zids, int amount, int roundNo)
        {
            foreach (var z in zids)
            {
                Heat[z] += amount;
                LastEventRound[z] = Math.Max(LastEventRound[z], roundNo);
            }
        }

        /// Deterministic decay (§3.3.3 #5): every zone WITHOUT an event this
        /// round loses half of its heat (exact fractions, no float drift).
        public void RoundEnd(int roundNo)
        {
            Feed.Add(new Dictionary<string, object>
            {
                { "round", roundNo }, { "kind", "round_end" }, { "zones", new List<string>() },
            });
            foreach (var z in new List<int>(Heat.Keys))
                if (LastEventRound[z] < roundNo)
                    Heat[z] = Heat[z] / 2;
        }

        public Rational Score(int z) => Prior[z] + Heat[z];

        /// Total zone order: score desc, then zone id asc (deterministic).
        public List<int> Ranked()
        {
            var zs = Belief.SortedKeys(Zmap.Cells);
            zs.Sort((a, b) =>
            {
                int c = Score(b).CompareTo(Score(a));
                return c != 0 ? c : a.CompareTo(b);
            });
            return zs;
        }

        public int? FocusZone(int? exclude = null)
        {
            foreach (var z in Ranked())
                if (z != exclude) return z;
            return null;
        }

        public HashSet<int> Hot(Rational? threshold = null)
        {
            Rational th = threshold ?? new Rational(2);
            var outp = new HashSet<int>();
            foreach (var kv in Heat) if (kv.Value >= th) outp.Add(kv.Key);
            return outp;
        }

        HashSet<int> NeighbourZones(int z)
        {
            var outp = new HashSet<int>();
            foreach (var c in Zmap.Cells[z])
                foreach (var dv in new[] { new Cell(0, 1), new Cell(0, -1), new Cell(1, 0), new Cell(-1, 0) })
                {
                    int nb = Zmap.ZoneOfCell(new Cell(c.X + dv.X, c.Y + dv.Y));
                    if (nb >= 0 && nb != z) outp.Add(nb);
                }
            return outp;
        }

        bool ZoneHasEscape(int z)
        {
            foreach (var o in Board.Objectives)
            {
                if (o.Role != "escape") continue;
                foreach (var c in o.Cells)
                    if (Zmap.ZoneOfCell(c) == z) return true;
            }
            return false;
        }

        /// Deterministic setup: zones + geometric prior from the heroes'
        /// likely corridors (entrance -> objective cost valley, §3.3.3 #5).
        /// entrances = public mission-setup hero entry cells only.
        public static BeliefState Build(Board board, IEnumerable<Cell> entrances = null)
        {
            var zmap = new ZoneMap(board);
            var labels = Belief.ZoneLabels(board, zmap, Belief.DoorLabels(board));
            var goals = new List<Cell>();
            foreach (var o in board.Objectives) goals.AddRange(o.Cells);
            var ent = new List<Cell>(entrances ?? Array.Empty<Cell>());
            var valley = new HashSet<Cell>();
            if (ent.Count > 0 && goals.Count > 0)
            {
                var fEnt = VictoryHelpers.TerrainCostField(board, ent);
                var fGoal = VictoryHelpers.TerrainCostField(board, goals);
                int total = int.MaxValue;
                bool anyGoal = false;
                foreach (var g in goals)
                {
                    anyGoal = true;
                    int v = fEnt.TryGetValue(g, out var fv) ? fv : 1000000000;
                    if (v < total) total = v;
                }
                if (!anyGoal) total = 1000000000;
                foreach (var kv in fEnt)
                    if (fGoal.TryGetValue(kv.Key, out var dg) && kv.Value + dg <= total + 1)
                        valley.Add(kv.Key);
            }
            var prior = new Dictionary<int, Rational>();
            foreach (var z in Belief.SortedKeys(zmap.Cells))
            {
                int count = 0;
                foreach (var c in zmap.Cells[z]) if (valley.Contains(c)) count++;
                prior[z] = count;
            }
            Rational tot = Rational.Zero;
            foreach (var kv in prior) tot += kv.Value;
            if (tot > Rational.Zero)
                foreach (var z in new List<int>(prior.Keys)) prior[z] = prior[z] / tot;
            else
                foreach (var z in new List<int>(prior.Keys))
                    prior[z] = new Rational(1, Math.Max(1, zmap.N));
            return new BeliefState(board, zmap, labels, prior);
        }
    }

    // ---------------------------------------------------------------- AppView
    /// The doctrine layer's ENTIRE input surface. Constructed exclusively via
    /// WorldFeed.DoctrineView(); structurally contains no figure and no
    /// coordinate data — Public() is coordinate-free by design,
    /// PositionScan() double-checks against living rebel footprints.
    public sealed class AppView
    {
        public int RoundNo;
        public string Posture;
        public string PostureReason;
        public Dictionary<string, object> Counter;      // VictoryCounter public fields
        public List<string> DenyIds;
        public List<string> ProtectIds;
        public List<Dictionary<string, object>> BeliefFeed;   // belief display [{zone,value}]
        public string FocusZone;
        public List<string> HotZones;
        public List<Dictionary<string, object>> Doors;
        public List<Dictionary<string, object>> Objectives;
        public string OwnZone;
        public List<string> Entrances;
        public List<Dictionary<string, object>> Feed;
        public Dictionary<string, Dictionary<string, object>> Contest;
        public List<Dictionary<string, object>> Rings;

        /// Field set in Python __slots__ order (public payload keys).
        public Dictionary<string, object> Public()
        {
            return new Dictionary<string, object>
            {
                { "round_no", RoundNo },
                { "posture", Posture },
                { "posture_reason", PostureReason },
                { "counter", Counter },
                { "deny_ids", DenyIds },
                { "protect_ids", ProtectIds },
                { "belief", BeliefFeed },
                { "focus_zone", FocusZone },
                { "hot_zones", HotZones },
                { "doors", Doors },
                { "objectives", Objectives },
                { "own_zone", OwnZone },
                { "entrances", Entrances },
                { "feed", Feed },
                { "contest", Contest },
                { "rings", Rings },
            };
        }

        public string Dump() => DumpValue(Public());

        /// Python-style json.dumps(payload, sort_keys=True, default=str) —
        /// the canonical text the coordinate regex scans. Numbers/lists are
        /// rendered so a leaked coordinate pair shows up exactly as
        /// "[x, y]" / "(x, y)".
        public static string DumpValue(object v)
        {
            var sb = new StringBuilder();
            WriteValue(sb, v);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case string s: WriteString(sb, s); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    if (d == Math.Floor(d) && !double.IsInfinity(d)) sb.Append(".0");
                    return;
                case Rational r: WriteString(sb, r.ToString()); return;
                case IDictionary<string, object> dict:
                    {
                        sb.Append('{');
                        var keys = new List<string>(dict.Keys);
                        keys.Sort(StringComparer.Ordinal);   // sort_keys=True
                        for (int i = 0; i < keys.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            WriteString(sb, keys[i]);
                            sb.Append(": ");
                            WriteValue(sb, dict[keys[i]]);
                        }
                        sb.Append('}');
                        return;
                    }
                case System.Collections.IEnumerable en:
                    {
                        sb.Append('[');
                        bool first = true;
                        foreach (var item in en)
                        {
                            if (!first) sb.Append(", ");
                            first = false;
                            WriteValue(sb, item);
                        }
                        sb.Append(']');
                        return;
                    }
                default:
                    WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
                    return;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        // Python json.dumps: ensure_ascii=True -> every
                        // non-ASCII char becomes \uXXXX (lower hex, BMP;
                        // astral chars arrive as a surrogate pair).
                        if (ch < 0x20 || ch > 0x7E)
                            sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }

        /// Every coordinate occurrence in the public surface that names a
        /// cell a LIVING REBEL currently occupies and that is not already
        /// public fixture geometry (objective/door/DP cells).
        public List<(int, int)> PositionScan(HashSet<Cell> bannedCells)
        {
            var hits = new List<(int, int)>();
            foreach (Match m in Belief.CoordRe.Matches(Dump()))
            {
                var nums = new List<int>();
                foreach (Match n in Regex.Matches(m.Value, @"-?\d+"))
                    nums.Add(int.Parse(n.Value, CultureInfo.InvariantCulture));
                if (nums.Count == 2 && bannedCells.Contains(new Cell(nums[0], nums[1])))
                    hits.Add((nums[0], nums[1]));
            }
            return hits;
        }

        /// Structural leak check: the AppView must never carry a banned
        /// rebel cell as a coordinate token. Message format matches Python
        /// `sorted(set(hits))` — a list of '(x, y)' tuples, numeric order.
        public void AssertPositionFree(HashSet<Cell> bannedCells)
        {
            var hits = PositionScan(bannedCells);
            if (hits.Count > 0)
            {
                var uniq = new List<(int, int)>(new HashSet<(int, int)>(hits));
                uniq.Sort();
                var parts = new List<string>();
                foreach (var (x, y) in uniq) parts.Add("(" + x + ", " + y + ")");
                throw new InvalidOperationException(
                    "AppView leaked rebel cells: [" + string.Join(", ", parts) + "]");
            }
        }

        /// The type boundary (compile-time in C#; runtime for dynamic callers).
        public static void AssertAppView(object x)
        {
            if (!(x is AppView))
                throw new InvalidCastException("doctrine layer may only see AppView, got "
                    + (x?.GetType().Name ?? "null")
                    + " — WorldFeed/world truth is metrics-side only");
        }
    }

    // ---------------------------------------------------------------- WorldFeed
    /// Truth-side observer. Reads the sim world (figures, doors, objective
    /// runtime) but EMITS ONLY published events, zone facts and metrics.
    public sealed class WorldFeed
    {
        public readonly World World;
        public readonly BeliefState Belief;
        public readonly ZoneMap Zmap;
        public readonly Dictionary<int, string> Labels;
        public readonly Dictionary<(Cell, Cell), string> Dlabels;
        public readonly Dictionary<int, List<(string, int)>> Adj;
        public ActivationInference Inference;   // opt-in (M2a.3)

        Dictionary<(Cell, Cell), bool> _doorsPrev;
        Dictionary<string, (int, bool)> _objsPrev;

        public WorldFeed(World world, IEnumerable<Cell> entrances = null, BeliefState belief = null)
        {
            World = world;
            Belief = belief ?? BeliefState.Build(world.Board, entrances);
            Zmap = Belief.Zmap;
            Labels = Belief.Labels;
            Dlabels = BS.DoorLabels(world.Board);
            Adj = BS.ZoneDoorGraph(Zmap, Dlabels);
            _doorsPrev = DoorState();
            _objsPrev = ObjState();
        }

        // ---- internal truth reads (never returned to the doctrine side) ----
        Dictionary<(Cell, Cell), bool> DoorState()
        {
            var outp = new Dictionary<(Cell, Cell), bool>();
            foreach (var kv in World.Board.Doors) outp[kv.Key] = kv.Value.IsOpen;
            return outp;
        }

        Dictionary<string, (int, bool)> ObjState()
        {
            var outp = new Dictionary<string, (int, bool)>();
            var rt = World.Objectives;
            if (rt is null) return outp;
            foreach (var o in rt.Objectives) outp[o.Id] = (o.Progress, o.Done);
            return outp;
        }

        /// Diff the truth since the last call and publish the resulting
        /// public events (Terminal aktiv / Tür offen / Item genommen ...).
        public List<Dictionary<string, object>> Advance(int roundNo)
        {
            var events = new List<Dictionary<string, object>>();
            var doors = DoorState();
            var keys = new List<(Cell, Cell)>(doors.Keys);
            keys.Sort((a, b) => string.CompareOrdinal(BS.EdgeKeyStr(a), BS.EdgeKeyStr(b)));
            foreach (var k in keys)
            {
                bool isOpen = doors[k];
                bool was = _doorsPrev.TryGetValue(k, out var w) ? w : false;
                if (isOpen && !was)
                {
                    string lab = Dlabels[k];
                    var cells = new List<Cell> { k.Item1, k.Item2 };
                    Belief.Observe("door_opened", cells, roundNo);
                    events.Add(new Dictionary<string, object>
                    {
                        { "round", roundNo }, { "kind", "door_opened" }, { "door", lab },
                        { "zones", ZoneLabelsFor(cells) },
                    });
                }
            }
            _doorsPrev = doors;
            var objs = ObjState();
            var rt = World.Objectives;
            var oids = new List<string>(objs.Keys);
            oids.Sort(StringComparer.Ordinal);
            foreach (var oid in oids)
            {
                var (prog, done) = objs[oid];
                var (oldProg, oldDone) = _objsPrev.TryGetValue(oid, out var old) ? old : (0, false);
                if (done && !oldDone)
                {
                    var st = rt.ById(oid);
                    string role = st.Role;
                    string kind = role == "item" ? "item_taken" : "objective_done";
                    var cells = SortedCells(st.Cells);
                    Belief.Observe(kind, cells, roundNo, objId: oid, role: role);
                    events.Add(new Dictionary<string, object>
                    {
                        { "round", roundNo }, { "kind", kind }, { "objekt", oid },
                        { "zones", ZoneLabelsFor(cells) },
                    });
                }
                else if (prog > oldProg)
                {
                    var cells = SortedCells(rt.ById(oid).Cells);
                    Belief.Observe("objective_worked", cells, roundNo, objId: oid);
                    events.Add(new Dictionary<string, object>
                    {
                        { "round", roundNo }, { "kind", "objective_worked" }, { "objekt", oid },
                    });
                }
            }
            _objsPrev = objs;
            return events;
        }

        List<string> ZoneLabelsFor(IEnumerable<Cell> cells)
        {
            var labs = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var c in cells)
            {
                int z = Zmap.ZoneOfCell(c);
                if (z >= 0) labs.Add(Labels[z]);
            }
            return new List<string>(labs);
        }

        static List<Cell> SortedCells(IEnumerable<Cell> cells)
        {
            var l = new List<Cell>(cells);
            l.Sort();
            return l;
        }

        /// The only sanctioned extra channel (§3.3.3 #2): a HUMAN explicitly
        /// tells the app 'rebels were here'. Feeds nothing but zone heat.
        public void SignalSeen(IEnumerable<Cell> cells, int roundNo) =>
            Belief.Observe("signal_seen", cells, roundNo);

        /// Opt-in activation-inference channel (§3.3.3 Nr. 2b v3.8b):
        /// without it the feed behaves EXACTLY like M2a.2.
        public ActivationInference EnableInference(Dictionary<string, int[]> ranges = null,
            Dictionary<string, Rational> lethality = null, int[] defaultBand = null)
        {
            Inference = new ActivationInference(Belief, ranges,
                defaultBand ?? new[] { 1, 5 }, lethality);
            return Inference;
        }

        public List<Dictionary<string, object>> RingsPublic(int roundNo)
        {
            var outp = new List<Dictionary<string, object>>();
            if (Inference is null) return outp;
            foreach (var r in Inference.ActiveRings(roundNo)) outp.Add(r.AsDict());
            return outp;
        }

        /// Own-unit knowledge: the app logged its own Overwatch token.
        public void OwnOverwatch(IEnumerable<Cell> cells, int roundNo) =>
            Belief.Observe("overwatch_placed", cells, roundNo);

        public void RoundEnd(int roundNo) => Belief.RoundEnd(roundNo);

        /// Metrics side (balance/QA only — never doctrine input).
        public Dictionary<string, object> Metrics()
        {
            var occ = new Dictionary<string, int>();
            foreach (var f in World.Figures)
            {
                if (f.Team != "rebel" || !f.Alive) continue;
                foreach (var c in f.Cells())
                {
                    int z = Zmap.ZoneOfCell(c);
                    if (z >= 0) occ[Labels[z]] = (occ.TryGetValue(Labels[z], out var n) ? n : 0) + 1;
                }
            }
            var belief = new Dictionary<string, object>();
            foreach (var z in BS.SortedKeys(Belief.Zmap.Cells))
                belief[Labels[z]] = Belief.Score(z).ToString();
            return new Dictionary<string, object>
            {
                { "zone_occupancy", occ },
                { "belief", belief },
            };
        }

        // ---- the single sanctioned bridge ---------------------------------
        public AppView DoctrineView(int roundNo, MissionVictoryModel model = null)
        {
            var rt = World.Objectives;
            var objectives = new List<Dictionary<string, object>>();
            if (rt != null)
            {
                var objs = new List<ObjectiveState>(rt.Objectives);
                objs.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                foreach (var o in objs)
                {
                    int z = Zmap.ZoneOfCell(MinCell(o.Cells));
                    objectives.Add(new Dictionary<string, object>
                    {
                        { "id", o.Id }, { "role", o.Role },
                        { "progress", o.Progress }, { "needed", o.Needed },
                        { "done", o.Done },
                        { "zone", z >= 0 ? Labels[z] : null },
                    });
                }
            }
            var doors = new List<Dictionary<string, object>>();
            {
                var dk = new List<(Cell, Cell)>(World.Board.Doors.Keys);
                dk.Sort((a, b) => string.CompareOrdinal(Dlabels[a], Dlabels[b]));
                foreach (var k in dk)
                {
                    var d = World.Board.Doors[k];
                    doors.Add(new Dictionary<string, object>
                    {
                        { "label", Dlabels[k] },
                        { "state", d.IsOpen ? "geöffnet" : (d.Locked ? "schlossfest" : "geschlossen") },
                        { "zones", ZoneLabelsFor(new List<Cell> { k.Item1, k.Item2 }) },
                    });
                }
            }
            string own = null;
            var dps = World.Board.DeploymentPoints;
            if (dps.Count > 0 && dps[0].Cells.Count > 0)
            {
                int z = Zmap.ZoneOfCell(dps[0].Cells[0]);
                own = z >= 0 ? Labels[z] : null;
            }
            var entranceZones = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var c in PublicEntranceCells(World))
            {
                int z = Zmap.ZoneOfCell(c);
                if (z >= 0) entranceZones.Add(Labels[z]);
            }
            var rings = RingsPublic(roundNo);
            Dictionary<string, object> counter = null;
            string posture = null, postureReason = null;
            var denyIds = new List<string>();
            var protectIds = new List<string>();
            if (model != null)
            {
                posture = model.Posture;
                postureReason = model.Switches.Count > 0
                    ? model.Switches[model.Switches.Count - 1].Reason
                    // same-round replay: the round_no assignment inside is
                    // idempotent here, the posture math itself is pure
                    : model.ComputePosture(roundNo, World).Reason;
                if (model.Counter != null) counter = model.Counter.PublicFields();
                var pf = model.PublicFields();
                denyIds = (List<string>)pf["denyTriggers"];
                protectIds = (List<string>)pf["protectTriggers"];
            }
            int? focus = Belief.FocusZone();
            var beliefDisp = new List<Dictionary<string, object>>();
            foreach (var z in Belief.Ranked())
                beliefDisp.Add(new Dictionary<string, object>
                {
                    { "zone", Labels[z] }, { "value", Belief.Score(z).ToString() },
                });
            var hot = new List<string>();
            foreach (var z in Belief.Hot()) hot.Add(Labels[z]);
            hot.Sort(StringComparer.Ordinal);
            var view = new AppView
            {
                RoundNo = roundNo,
                Posture = posture,
                PostureReason = postureReason,
                Counter = counter,
                DenyIds = new List<string>(denyIds),
                ProtectIds = new List<string>(protectIds),
                BeliefFeed = beliefDisp,
                FocusZone = focus is { } fz ? Labels[fz] : null,
                HotZones = hot,
                Doors = doors,
                Objectives = objectives,
                OwnZone = own,
                Entrances = new List<string>(entranceZones),
                Feed = new List<Dictionary<string, object>>(Belief.Feed),
                Contest = Contest(model, rt),
                Rings = rings,
            };
            // the bridge itself guarantees the boundary before handing over
            view.AssertPositionFree(BannedCells());
            return view;
        }

        static Cell MinCell(IEnumerable<Cell> cells)
        {
            Cell? best = null;
            foreach (var c in cells) if (best is null || c.CompareTo(best.Value) < 0) best = c;
            return best.Value;
        }

        /// Geographic contest points (labels only — computed on the feed side
        /// from geometry + public fixture fields, never from positions).
        public Dictionary<string, Dictionary<string, object>> Contest(
            MissionVictoryModel model, ObjectiveRuntime rt)
        {
            int? own = null;
            var dps = World.Board.DeploymentPoints;
            if (dps.Count > 0 && dps[0].Cells.Count > 0) own = Zmap.ZoneOfCell(dps[0].Cells[0]);

            Dictionary<string, object> Entry(int? zone, List<string> targets)
            {
                if (zone is null)
                    return new Dictionary<string, object>
                    {
                        { "zone", null }, { "door", null },
                        { "path", new List<string>() }, { "targets", new List<string>(targets) },
                    };
                List<string> doorList = null;
                List<int> zoneList = null;
                if (own != null && Adj.ContainsKey(zone.Value))
                {
                    var (doors2, zones2) = BS.ZonePath(Adj, own.Value, zone.Value);
                    doorList = doors2;
                    zoneList = zones2;
                }
                var zs = zoneList ?? new List<int>();
                var path = new List<string>();
                for (int i = 1; i < zs.Count; i++) path.Add(Labels[zs[i]]);
                return new Dictionary<string, object>
                {
                    { "zone", Labels[zone.Value] },
                    { "door", (doorList != null && doorList.Count > 0) ? doorList[0] : null },
                    { "path", path },
                    { "targets", new List<string>(targets) },
                };
            }

            bool OpenObj(ObjectiveDef o)
            {
                if (rt != null) return !rt.ById(o.Id).Done;
                return true;
            }

            var objs = new List<ObjectiveDef>(World.Board.Objectives);
            objs.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            var openObjs = objs.FindAll(o => o.Role != "escape" && OpenObj(o));
            int? focus = Belief.FocusZone();
            var outp = new Dictionary<string, Dictionary<string, object>>();

            // eliminate: belief focus, else first open objective's zone
            int? elimZone = focus;
            var elimTargets = new List<string>();
            foreach (var o in openObjs)
            {
                int z = MinZone(o.Cells);
                if (focus != null && z == focus) elimTargets.Add(o.Id);
            }
            if (elimTargets.Count == 0 && openObjs.Count > 0)
            {
                elimZone = MinZone(openObjs[0].Cells);
                elimTargets.Add(openObjs[0].Id);
            }
            outp["eliminate"] = Entry(elimZone, elimTargets);

            // stall: the timer terminal (public fixture role)
            var terminals = openObjs.FindAll(o => o.Role == "terminal");
            var t = terminals.Count > 0 ? terminals[0]
                : (openObjs.Count > 0 ? openObjs[0] : null);
            outp["stall"] = Entry(t != null ? MinZone(t.Cells) : focus,
                                  t != null ? new List<string> { t.Id } : new List<string>());

            // deny: first open deny-trigger objective (public fixture field)
            var denyIds = model != null ? BS.OrdSorted(model.DenyIds) : new List<string>();
            ObjectiveDef d = null;
            foreach (var o in openObjs) if (denyIds.Contains(o.Id)) { d = o; break; }
            outp["deny"] = Entry(d != null ? MinZone(d.Cells) : (int?)null,
                                 d != null ? new List<string> { d.Id } : new List<string>());

            // protect: zone of the protected entity (static fixture cells)
            var ents = new List<Dictionary<string, object>>(World.Board.ProtectedEntities);
            ents.Sort((a, b) => string.CompareOrdinal(EntId(a), EntId(b)));
            var protIds = model != null ? BS.OrdSorted(model.ProtectIds) : new List<string>();
            var picked = ents.FindAll(e => protIds.Contains(EntId(e)));
            if (picked.Count == 0) picked = ents;
            var e = picked.Count > 0 ? picked[0] : null;
            int? protZone = null;
            List<string> protTargets = new List<string>();
            if (e != null)
            {
                protTargets.Add(EntId(e));
                var ec = VictoryHelpers.EntityCells(e);
                if (ec.Count > 0)
                {
                    int? best = null;
                    foreach (var c in ec)
                    {
                        int z = Zmap.ZoneOfCell(c);
                        if (z >= 0 && (best is null || z < best)) best = z;
                    }
                    protZone = best;
                }
            }
            outp["protect"] = Entry(protZone, protTargets);
            return outp;
        }

        static string EntId(Dictionary<string, object> e) =>
            e.TryGetValue("id", out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        int MinZone(IEnumerable<Cell> cells)
        {
            int best = int.MaxValue;
            foreach (var c in cells) best = Math.Min(best, Zmap.ZoneOfCell(c));
            return best;
        }

        /// Living rebel footprint cells minus public fixture geometry:
        /// anything the AppView could not already derive from the JSON.
        public HashSet<Cell> BannedCells()
        {
            var cells = new HashSet<Cell>();
            foreach (var f in World.Figures)
                if (f.Team == "rebel" && f.Alive)
                    foreach (var c in f.Cells()) cells.Add(c);
            var pub = new HashSet<Cell>();
            var board = World.Board;
            foreach (var o in board.Objectives) foreach (var c in o.Cells) pub.Add(c);
            foreach (var e in board.ProtectedEntities)
                foreach (var c in VictoryHelpers.EntityCells(e)) pub.Add(c);
            foreach (var dp in board.DeploymentPoints) foreach (var c in dp.Cells) pub.Add(c);
            cells.ExceptWith(pub);
            return cells;
        }

        /// Python reads world.hero_spawns/entrances (MissionWorld seam); the
        /// frozen World carries neither, so the vector runs see an empty set.
        static List<Cell> PublicEntranceCells(World world) => new List<Cell>();
    }

    // ---------------------------------------------------------------- rings
    /// One inferred hero-proximity ring. Public surface = kind, round, ZONE
    /// LABELS, band scalars and weight. No cells, no coordinates.
    public sealed class RingBelief
    {
        public string Kind;              // hero_ring | counter_ring | defeat_ring
        public int RoundNo;
        public string GroupId;           // own (commanded/victim) group
        public List<string> HeroIds;     // attributed heroes (ordered)
        public int MinR;
        public int MaxR;
        public List<string> Zones;       // zone labels the band crosses
        public Rational Weight;
        public bool Sharpened;           // optional damage-tap confirmed it

        public RingBelief(string kind, int roundNo, string groupId,
                          IEnumerable<string> heroIds, int minR, int maxR,
                          IEnumerable<string> zones, Rational? weight = null,
                          bool sharpened = false)
        {
            Kind = kind; RoundNo = roundNo; GroupId = groupId;
            HeroIds = new List<string>(heroIds);
            MinR = minR; MaxR = maxR; Zones = new List<string>(zones);
            Weight = weight ?? Rational.One; Sharpened = sharpened;
        }

        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "kind", Kind }, { "round", RoundNo }, { "group", GroupId },
                { "heroes", new List<string>(HeroIds) },
                { "min_dist", MinR }, { "max_dist", MaxR },
                { "zones", new List<string>(Zones) },
                { "weight", Weight.ToString() },
                { "sharpened", Sharpened },
            };
        }
    }

    /// Feed-side inference engine (lives with WorldFeed, never in the
    /// doctrine layer). Consumes ONLY app-published flip events and the App's
    /// own Instruktions-Ledger; emits zone-granular RingBeliefs + heat bumps.
    public sealed class ActivationInference
    {
        public readonly BeliefState Belief;
        public readonly Board Board;
        public readonly Dictionary<string, int[]> Ranges;    // hero -> [minR, maxR]
        public readonly int[] DefaultBand;
        public readonly Dictionary<string, Rational> Lethality;  // hero -> weight prior
        public readonly int RingHorizon;
        public readonly List<RingBelief> Rings = new List<RingBelief>();
        public int RoundNo;

        int _seq;
        readonly Dictionary<int, List<(string, int)>> _activations = new Dictionary<int, List<(string, int)>>();
        public readonly Dictionary<string, Cell> Soll = new Dictionary<string, Cell>();   // Instruktions-Ledger
        public readonly List<Dictionary<string, object>> WoundFlips = new List<Dictionary<string, object>>();

        public ActivationInference(BeliefState belief, Dictionary<string, int[]> ranges = null,
                                   int[] defaultBand = null, Dictionary<string, Rational> lethality = null,
                                   int ringHorizon = 3)
        {
            Belief = belief;
            Board = belief.Board;
            Ranges = ranges is null ? new Dictionary<string, int[]>() : new Dictionary<string, int[]>(ranges);
            DefaultBand = defaultBand ?? new[] { 1, 5 };
            Lethality = lethality is null ? new Dictionary<string, Rational>() : new Dictionary<string, Rational>(lethality);
            RingHorizon = ringHorizon;
        }

        public void BeginRound(int roundNo)
        {
            RoundNo = roundNo;
            _seq = 0;
            if (!_activations.ContainsKey(roundNo)) _activations[roundNo] = new List<(string, int)>();
        }

        /// The App's own commanded position for one of ITS groups (command
        /// authority — §3.3.3 Nr. 2b, not hero knowledge).
        public void Instruction(string groupId, Cell cell, int? roundNo = null) =>
            Soll[groupId] = cell;

        int NextSeq() => ++_seq;

        /// hasActivated flip: which hero, which round (seq = position in the
        /// round's published flip stream).
        public int HeroActivated(string hero, int? roundNo = null)
        {
            int rnd = roundNo ?? RoundNo;
            int seq = NextSeq();
            if (!_activations.ContainsKey(rnd)) _activations[rnd] = new List<(string, int)>();
            _activations[rnd].Add((hero, seq));
            return seq;
        }

        /// woundTrackerValue delta on one of OUR groups. Attribution -> hero
        /// ring or none (no window => real overwatch token, no hero ring).
        public RingBelief Damage(string victimGroup, int amount, int? roundNo = null)
        {
            int rnd = roundNo ?? RoundNo;
            int seq = NextSeq();
            var acts = _activations.TryGetValue(rnd, out var a) ? a : new List<(string, int)>();
            var earlier = new List<(string, int)>();
            foreach (var (h, s) in acts) if (s <= seq) earlier.Add((h, s));
            if (earlier.Count == 0) return null;   // kein Fenster => kein Held-Ring
            int maxS = int.MinValue;
            foreach (var (_, s) in earlier) maxS = Math.Max(maxS, s);
            var cands = new List<string>();        // window occupants
            foreach (var (h, s) in earlier) if (s == maxS) cands.Add(h);
            return MakeRing("hero_ring", victimGroup, cands, BandFor(cands), rnd);
        }

        /// isWounded flip of a hero that fired during imperial activation
        /// duringGroup -> that group was inside the hero's range: counter
        /// ring around the GROUP's own Soll-Position.
        public RingBelief WoundFlip(string hero, int? roundNo = null, string duringGroup = null)
        {
            int rnd = roundNo ?? RoundNo;
            NextSeq();
            WoundFlips.Add(new Dictionary<string, object>
            {
                { "hero", hero }, { "round", rnd }, { "during", duringGroup },
            });
            if (duringGroup is null) return null;
            return MakeRing("counter_ring", duringGroup, new[] { hero },
                            BandFor(new[] { hero }), rnd);
        }

        /// OnDefeat: killer must have been within the widest known hero band
        /// around the fallen figure's commanded position.
        public RingBelief OnDefeat(string groupId, int? roundNo = null)
        {
            int rnd = roundNo ?? RoundNo;
            NextSeq();
            // Python: lo = min(b[0] for bands, default=1),
            //         hi = max(b[1] for bands, default=default_band[1])
            int lo = 1, hi = DefaultBand[1];
            bool first = true;
            foreach (var b in Ranges.Values)
            {
                if (first) { lo = b[0]; hi = b[1]; first = false; }
                else { lo = Math.Min(lo, b[0]); hi = Math.Max(hi, b[1]); }
            }
            return MakeRing("defeat_ring", groupId, Array.Empty<string>(), new[] { lo, hi }, rnd);
        }

        /// OPTIONAL human input: 'the damage came from that direction'.
        /// Sharpens every still-valid ring crossing the tapped zone (x2);
        /// rings without a tap stay exactly as they were.
        public List<RingBelief> DamageTap(string zoneLabel, int? roundNo = null)
        {
            int rnd = roundNo ?? RoundNo;
            var touched = new List<RingBelief>();
            for (int i = 0; i < Rings.Count; i++)
            {
                var r = Rings[i];
                if (r.Sharpened || !r.Zones.Contains(zoneLabel)) continue;
                if (rnd - r.RoundNo > RingHorizon) continue;
                Rings[i] = new RingBelief(r.Kind, r.RoundNo, r.GroupId, r.HeroIds,
                    r.MinR, r.MaxR, r.Zones, r.Weight * 2, true);
                touched.Add(Rings[i]);
                Belief.Observe("signal_seen", ZoneCells(zoneLabel), rnd);
            }
            return touched;
        }

        int[] BandFor(IEnumerable<string> heroes)
        {
            var bands = new List<int[]>();
            foreach (var h in heroes)
                bands.Add(Ranges.TryGetValue(h, out var b) ? b : DefaultBand);
            if (bands.Count == 0) return DefaultBand;
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (var b in bands) { lo = Math.Min(lo, b[0]); hi = Math.Max(hi, b[1]); }
            return new[] { lo, hi };
        }

        RingBelief MakeRing(string kind, string groupId, IEnumerable<string> heroes,
                            int[] band, int rnd)
        {
            if (!Soll.TryGetValue(groupId, out var cell)) return null;   // nothing commanded -> nothing to ring
            var zids = RingZones(cell, band);
            if (zids.Count == 0) return null;
            Rational weight = Rational.Zero;
            foreach (var h in heroes)
                weight += Lethality.TryGetValue(h, out var w) ? w : Rational.One;
            if (weight.IsZero) weight = Rational.One;
            if (kind == "counter_ring") weight *= new Rational(3, 2);   // hero stood in OUR fire lane
            var zList = new List<int>(zids);
            zList.Sort();
            var zones = new List<string>();
            foreach (var z in zList) zones.Add(Belief.Labels[z]);
            var ring = new RingBelief(kind, rnd, groupId, heroes, band[0], band[1], zones, weight);
            Rings.Add(ring);
            var cells = new List<Cell>();
            foreach (var z in zList) cells.AddRange(Belief.Zmap.Cells[z]);
            Belief.Observe(kind, cells, rnd);
            return ring;
        }

        /// Zones crossed by the distance band [minR, maxR] around cell
        /// (graph distance; feed-side geometry only — what leaves this class
        /// is zone labels + band scalars).
        HashSet<int> RingZones(Cell cell, int[] band)
        {
            int lo = band[0], hi = band[1];
            var zids = new HashSet<int>();
            var src = new List<Cell> { cell };
            foreach (var z in BS.SortedKeys(Belief.Zmap.Cells))
            {
                foreach (var c in Belief.Zmap.Cells[z])
                {
                    int d = FocusResolver.CountSpaces(Board, src, new List<Cell> { c });
                    if (lo <= d && d <= hi) { zids.Add(z); break; }
                }
            }
            return zids;
        }

        List<Cell> ZoneCells(string label)
        {
            foreach (var kv in Belief.Labels)
                if (kv.Value == label) return new List<Cell>(Belief.Zmap.Cells[kv.Key]);
            return new List<Cell>();
        }

        public List<RingBelief> ActiveRings(int roundNo)
        {
            var outp = new List<RingBelief>();
            foreach (var r in Rings) if (roundNo - r.RoundNo <= RingHorizon) outp.Add(r);
            return outp;
        }

        /// Zone ids carrying a live ring (OrderPlanner / RiskPosture input).
        public HashSet<int> RingZoneIds(int roundNo, Func<string, int?> zidOfLabel)
        {
            var outp = new HashSet<int>();
            foreach (var r in ActiveRings(roundNo))
                foreach (var lab in r.Zones)
                {
                    int? z = zidOfLabel(lab);
                    if (z != null) outp.Add(z.Value);
                }
            return outp;
        }
    }
}
