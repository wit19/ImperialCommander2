// Port of sim/focus.py — focus procedure (concept section 3.3), Gloomhaven-
// derived, IA-adapted.
//
// Procedure per activation:
//   1. distance to every surviving rebel = fewest movement points to reach a
//      legal ATTACK cell (terrain metric only: occupancy and LOS ignored;
//      attackable-without-moving = cost 0);
//   2. focus = minimum; tiebreaker cascade (concept 3.3):
//         1 fewest movement points (Gloomhaven)
//         2 shortest distance ("counting spaces" metric, IA)
//         3 mission role weight (objective tags, section 3.5; higher wins)
//         4 earlier activation order (initiative, section 3.4)
//         5 lower remaining vitality (optional, toggleable)
//   3. PATH PRIORITY: of all shortest paths take the one without
//      Overwatch/trap/mined fields - unless avoidance would cost extra;
//   4. focus dies mid-activation -> immediate re-run (runner);
//   5. every decision carries a visible `why` payload (fairness requirement).
//
// Gloomhaven Rulebook p.39-40 (Monster Acting) verified behaviours encoded as
// golden tests: ignore LOS for focus (check at execution), prefer the target
// reachable with fewer MP even when another looks closer geometrically, never
// gain free movement, attack if possible.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TacticalAI.Core
{
    public sealed class FocusDecision
    {
        public Figure Target;
        public int FocusCostMp;                 // MP metric to a legal attack cell
        public int Dist;                        // counting-spaces distance
        // NOTE: the contract sketched List<string>; the golden vectors carry
        // per-row dicts, so the trace rows are dictionaries (key order is
        // irrelevant — fixtures dump sorted keys).
        public List<Dictionary<string, object>> TiebreakTrace = new List<Dictionary<string, object>>();
        public string Why = "";
        public PathResult DetailPath;           // Python detail={"path": res}
        public string Posture = "";             // "" = pure GH, T1
        // Python-typed components (boxed long/double, as Victory.cs emits);
        // null = pure GH (Python w_score=()).
        public List<object> W;
        public string SkillLevel = "";          // "" = Referenzpfad
        public int SkillRank;                   // gezogener Rang im Fehlerband (0 = n/a)

        public Dictionary<string, object> AsDict()
        {
            var d = new Dictionary<string, object>
            {
                { "target", Target.Id },
                { "focus_cost_mp", (long)FocusCostMp },
                { "count_dist", (long)Dist },
                { "why", Why },
                { "tiebreak_trace", TiebreakTrace },
            };
            // additive keys ONLY in posture mode, so T1 hashes stay byte-identical
            if (Posture.Length > 0)
            {
                d["posture"] = Posture;
                d["w"] = NormalizeW(W);
            }
            // additive keys ONLY with a skill profile
            if (SkillLevel.Length > 0)
            {
                d["skill"] = SkillLevel;
                d["skill_rank"] = (long)SkillRank;
            }
            return d;
        }

        /// W -> MiniJson-comparable numbers: C# int normalised to long (json
        /// "1"), damage components stay double (json "2.666667", matching the
        /// exporter's round(printed_attack_ev, 6) floats).
        internal static List<object> NormalizeW(List<object> w)
        {
            var outl = new List<object>();
            if (w == null) return outl;
            foreach (var c in w) outl.Add(PyNumber(c));
            return outl;
        }

        /// Box a Python-typed number: integral -> long, fractional -> double.
        internal static object PyNumber(object v)
        {
            if (v is int i) return (long)i;
            if (v is long) return v;
            if (v is double) return v;
            if (v is Rational r) return r.Den == 1 ? (object)(long)r.Num : (object)r.ToDouble();
            return v;
        }
    }

    public static class FocusResolver
    {
        // ------------------------------------------------------------------
        // space counting (IA "Counting Spaces": impassable terrain/difficult/
        // figures cost nothing, walls/doors/blocking terrain cannot be crossed)
        // ------------------------------------------------------------------

        public static int CountSpaces(Board board, IEnumerable<Cell> a, IEnumerable<Cell> b2)
        {
            var aSet = new HashSet<Cell>(a);
            var bSet = new HashSet<Cell>(b2);
            var inter = new HashSet<Cell>(aSet);
            inter.IntersectWith(bSet);
            if (inter.Count > 0) return 0;
            var seen = new HashSet<Cell>(aSet);
            // Python seeds the deque in set-iteration order; BFS layering makes
            // the returned distance order-independent, so seed sorted.
            var seeds = new List<Cell>(aSet);
            seeds.Sort();
            var q = new Queue<(Cell cell, int d)>();
            foreach (var c in seeds) q.Enqueue((c, 0));
            while (q.Count > 0)
            {
                var (cur, d) = q.Dequeue();
                foreach (var dv in Geometry.DirVectors)
                {
                    var n = new Cell(cur.X + dv.X, cur.Y + dv.Y);
                    if (seen.Contains(n) || !board.InBounds(n)) continue;
                    if (board.IsBlockingCell(n)) continue;
                    var reason = board.BlocksMovement(cur, n);
                    if (reason == "wall" || reason == "blocking_edge"
                        || reason == "closed_door" || reason == "locked_door") continue;
                    if (bSet.Contains(n)) return d + 1;
                    seen.Add(n);
                    q.Enqueue((n, d + 1));
                }
            }
            return 1 << 30;                                    // unreachable
        }

        // ------------------------------------------------------------------
        // attack geometry
        // ------------------------------------------------------------------

        /// Pure geometric attackability of targetCells from cells.
        ///
        /// withLos=false: focus metric (LOS ignored per GH; Reach distance kept).
        /// withLos=true : execution legality (ranged/Reach need real LOS).
        public static bool AttackGeometryOk(World world, Figure attacker,
                                            HashSet<Cell> targetCells, HashSet<Cell> cells,
                                            bool withLos, IEnumerable<Cell> figureCells = null)
        {
            var card = attacker.Card;
            bool sameSpace = Intersects(cells, targetCells);
            if (card.AttackType == "Melee" && !card.HasReach)
            {
                // melee: adjacent or same space (RefGuide MELEE verbatim)
                return sameSpace || Geometry.CellsAdjacent(cells, targetCells);
            }
            if (card.AttackType == "Melee" && card.HasReach)
            {
                // Reach: melee attack up to 2 spaces away, LOS required (verbatim)
                bool ok = CountSpaces(world.Board, cells, targetCells) <= 2;
                if (!ok || !withLos) return ok;
                return world.Los.HasLos(cells, targetCells,
                                        figureCells != null ? new List<Cell>(figureCells) : (List<Cell>)null,
                                        MassiveBlocking(world, attacker));
            }
            // ranged: any hostile within LOS; focus metric = not inside own way,
            // geometry alone is unconstrained -> legal from any standable cell
            if (!withLos) return true;
            return world.Los.HasLos(cells, targetCells,
                                    figureCells != null ? new List<Cell>(figureCells) : (List<Cell>)null,
                                    MassiveBlocking(world, attacker));
        }

        static bool Intersects(HashSet<Cell> a, HashSet<Cell> b)
        {
            var small = a.Count <= b.Count ? a : b;
            var big = a.Count <= b.Count ? b : a;
            foreach (var c in small) if (big.Contains(c)) return true;
            return false;
        }

        /// Massive standing in blocking terrain keeps LOS (RefGuide MASSIVE).
        static List<Cell> MassiveBlocking(World world, Figure fig)
        {
            var outl = new List<Cell>();
            if (!(fig.Card.IsMassive || fig.Card.IsMobile)) return outl;
            foreach (var c in SpaceLegality.FigureCells(fig))
                if (world.Board.IsBlockingCell(c)) outl.Add(c);
            return outl;
        }

        // ------------------------------------------------------------------
        // compute_focus
        // ------------------------------------------------------------------

        class Row
        {
            public Figure Fig;
            public long Mp;
            public long Dist;
            public double Weight;
            public long Order;
            public long Vitality;
            public PathResult Path;
            public bool HasW;
            public List<object> W;
            public int Index;                                    // original row position
        }

        /// Mission role weight from objective tags (concept 3.5). T2 supplies
        /// world.RoleWeight(fig); without it all figures weigh 0.
        static double RoleWeight(World world, Figure fig)
        {
            return world.RoleWeight != null ? world.RoleWeight(fig) : 0.0;
        }

        /// Full focus procedure for one activation. Returns null if no rebel lives.
        ///
        /// Posture mode (concept 3.3.1): when world.Victory (a
        /// MissionVictoryModel) is attached, the target order is the argmax of
        /// the posture value function W; the Gloomhaven cascade below only
        /// judges W-ties. Without a victory model the ordering is byte-identical
        /// to the pure T1 cascade.
        public static FocusDecision ComputeFocus(World world, Figure fig, bool vitalityTiebreak = true)
        {
            var rebels = new List<Figure>();
            foreach (var r in world.Figures)
                if (r.Alive && r.Team != fig.Team && r.Team != "npc") rebels.Add(r);

            // M2a.2 two-level seam: a doctrine in the theatre runtime may
            // restrict the candidate set (world.FocusCandidates = ids).
            // Default null keeps every ordering byte-identical to T1/M2a.1.
            if (world.FocusCandidates != null)
            {
                var sel = new List<Figure>();
                foreach (var r in rebels)
                    if (world.FocusCandidates.Contains(r.Id)) sel.Add(r);
                rebels = sel.Count > 0 ? sel : rebels;          // Python `sel or rebels`
            }
            if (rebels.Count == 0) return null;

            var vm = world.Victory;
            string posture = vm != null ? vm.Posture : "";

            var rows = new List<Row>();
            foreach (var r in rebels)
            {
                var targetCells = new HashSet<Cell>(SpaceLegality.FigureCells(r));
                var attacker = fig;
                var w = world;
                var res = world.Graph.Shortest(
                    fig, fig.Anchor, fig.Orientation,
                    (anchor, orient) => AttackGeometryOk(
                        w, attacker, targetCells,
                        new HashSet<Cell>(Geometry.FootprintCells(
                            anchor, attacker.Card.MiniSize, orient)), false),
                    "focus");
                long cost = res != null ? res.Cost : World.Inf;
                var row = new Row
                {
                    Fig = r,
                    Mp = cost,
                    Dist = CountSpaces(world.Board, SpaceLegality.FigureCells(fig), targetCells),
                    Weight = RoleWeight(world, r),
                    Order = r.Index,
                    Vitality = r.Vitality,
                    Path = res,
                    Index = rows.Count,
                };
                if (vm != null) { row.HasW = true; row.W = vm.PostureValue(world, fig, r); }
                rows.Add(row);
            }

            // Python's sort is STABLE; keys end with fig.index and rows get a
            // final original-position component to replicate that exactly.
            rows.Sort((x, y) =>
            {
                int c = CompareKeys(SortKey(x, vitalityTiebreak), SortKey(y, vitalityTiebreak));
                return c != 0 ? c : x.Index.CompareTo(y.Index);
            });
            var best = rows[0];

            // ---- δ Fokus-Disziplin (Konzept §6): Skill-Handicap ------------
            // Counter-Blindheit (S1/S2): Rangfolge OHNE Zähler-Semantik — kein
            // W, keine Vitalität (reine GH-Kaskade wie ein Spieler, der die
            // Damage-Counter nicht liest). Rangwahl ist ein Draw aus dem
            // Skill-RNG. Die Legalität aller Zeilen ist davon unberührt
            // (§6-Fairnessgrenze).
            var skill = world.Skill;
            var chosen = best;
            int skillRank = 0;
            if (skill != null)
            {
                List<Row> order;
                if (skill.CounterBlind)
                {
                    order = new List<Row>(rows);
                    order.Sort((x, y) =>
                    {
                        int c = CompareKeyBase(SortBase(x, false), SortBase(y, false));
                        return c != 0 ? c : x.Index.CompareTo(y.Index);
                    });
                }
                else order = rows;
                skillRank = skill.RankChoice(world.SkillRng, order.Count);
                chosen = order[skillRank - 1];
                // NOTE: world.skill_stats (runner bookkeeping) has no field on
                // the frozen World class; the counter bump is skipped there.
            }

            // ---- why payload: name every metric that decided ----------------
            var whyBits = new List<string>();
            if (posture.Length > 0)
            {
                var sb = new StringBuilder();
                foreach (var r in rows)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(r.Fig.Id).Append(":W").Append(PyListStr(FocusDecision.NormalizeW(r.W)));
                }
                string wSum = sb.ToString();
                int tiedW = 0;
                foreach (var r in rows) if (WEquals(r.W, best.W)) tiedW++;
                if (tiedW == 1)
                    whyBits.Add("posture " + posture + ": W-argmax " + best.Fig.Id + " [" + wSum + "]");
                else
                    whyBits.Add("posture " + posture + ": W tie of " + tiedW
                                + " targets [" + wSum + "] -> cascade decides");
            }
            var tied = new List<Row>();
            foreach (var r in rows) if (r.Mp == best.Mp) tied.Add(r);
            if (tied.Count == 1)
            {
                if (best.Mp == 0)
                    whyBits.Add("can attack " + best.Fig.Id + " from current position (0 MP)");
                else
                {
                    var others = new StringBuilder();
                    for (int i = 1; i < rows.Count; i++)
                    {
                        if (others.Length > 0) others.Append(", ");
                        others.Append(rows[i].Fig.Id).Append(": ").Append(rows[i].Mp).Append(" MP");
                    }
                    whyBits.Add("reachable target " + best.Fig.Id + " at " + best.Mp
                                + " MP (others: " + others + ")");
                }
            }
            else
            {
                var bits = new StringBuilder();
                foreach (var r in tied)
                {
                    if (bits.Length > 0) bits.Append(", ");
                    bits.Append(r.Fig.Id).Append("(dist ").Append(r.Dist).Append(")");
                }
                whyBits.Add(tied.Count + " targets tied at " + best.Mp + " MP: " + bits);
                string used = null;
                var levels = new (string level, string label, object val, Func<Row, object> get)[]
                {
                    ("dist", "shorter counting-space distance", best.Dist, r => r.Dist),
                    ("weight", "higher mission role weight", best.Weight, r => r.Weight),
                    ("order", "earlier activation order", best.Order, r => r.Order),
                    ("vitality", "lower vitality", best.Vitality, r => r.Vitality),
                };
                foreach (var (level, label, val, get) in levels)
                {
                    if (!vitalityTiebreak && level == "vitality") continue;
                    var bv = get(best);
                    var sub = new List<Row>();
                    foreach (var r in tied) if (PyEquals(get(r), bv)) sub.Add(r);
                    if (sub.Count < tied.Count)
                    {
                        whyBits.Add("tiebreak " + level + ": " + best.Fig.Id + " wins ("
                                    + label + " = " + PyStr(val) + ")");
                        used = level;
                        break;
                    }
                    tied = sub;
                }
                if (used == null)
                    whyBits.Add("fully tied -> lowest activation index picks " + best.Fig.Id);
            }

            if (skill != null && (!ReferenceEquals(chosen, best) || skill.CounterBlind))
            {
                string s = "Skill " + skill.Level + ": Fokusziel " + chosen.Fig.Id + " — Rang "
                           + skillRank + " von " + rows.Count;
                if (skill.CounterBlind)
                    s += " (Gegenzug: Counter nicht gelesen, kein koordinierter Feuerbefehl)";
                if (!ReferenceEquals(chosen, best))
                    s += "; Kaskaden-Bester " + best.Fig.Id + " nicht gewählt";
                whyBits.Add(s);
            }

            var trace = new List<Dictionary<string, object>>();
            foreach (var r in rows)
            {
                var t = new Dictionary<string, object>
                {
                    { "id", r.Fig.Id },
                    { "mp", r.Mp },
                    { "dist", r.Dist },
                    { "weight", r.Weight },
                    { "order", r.Order },
                    { "vitality", r.Vitality },
                };
                if (r.HasW) t["w"] = FocusDecision.NormalizeW(r.W);
                trace.Add(t);
            }

            return new FocusDecision
            {
                Target = chosen.Fig,
                FocusCostMp = (int)chosen.Mp,
                Dist = (int)chosen.Dist,
                TiebreakTrace = trace,
                Why = string.Join("; ", whyBits),
                DetailPath = chosen.Path,
                Posture = posture,
                W = chosen.HasW ? chosen.W : null,
                SkillLevel = skill != null ? skill.Level : "",
                SkillRank = skillRank,
            };
        }

        // ------------------------------------------------------------------
        // Python-faithful tuple keys / comparison / repr helpers
        // ------------------------------------------------------------------

        // key(row) = neg(W) + (mp, dist, -weight, order, vitality?, fig.index)
        static List<object> SortKey(Row row, bool vitalityTiebreak)
        {
            var key = new List<object>();
            if (row.HasW)
                foreach (var c in row.W) key.Add(PyNeg(c));
            key.AddRange(SortBase(row, vitalityTiebreak));
            return key;
        }

        static List<object> SortBase(Row row, bool vitalityTiebreak)
        {
            return new List<object>
            {
                row.Mp, row.Dist, -row.Weight, row.Order,
                vitalityTiebreak ? row.Vitality : 0L, (long)row.Fig.Index,
            };
        }

        static int CompareKeys(List<object> ka, List<object> kb)
        {
            int n = Math.Min(ka.Count, kb.Count);
            for (int i = 0; i < n; i++)
            {
                int c = PyCompare(ka[i], kb[i]);
                if (c != 0) return c;
            }
            return ka.Count.CompareTo(kb.Count);
        }

        static int CompareKeyBase(List<object> ka, List<object> kb) => CompareKeys(ka, kb);

        /// Python numeric tuple comparison: ints exact, int/float mixed by
        /// value (game-scale magnitudes; doubles are safe here).
        public static int PyCompare(object a, object b)
        {
            if (a is Rational ra && b is Rational rb) return ra.CompareTo(rb);
            if (IsIntLike(a) && IsIntLike(b)) return AsLong(a).CompareTo(AsLong(b));
            return AsDouble(a).CompareTo(AsDouble(b));
        }

        public static bool PyEquals(object a, object b) => PyCompare(a, b) == 0;

        static bool IsIntLike(object v) => v is long || v is int;
        static long AsLong(object v) => v is long l ? l : Convert.ToInt64(v);
        static double AsDouble(object v)
        {
            if (v is double d) return d;
            if (v is Rational r) return r.ToDouble();
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        static bool WEquals(List<object> x, List<object> y)
        {
            if (x == null || y == null) return x == null && y == null;
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++) if (!PyEquals(x[i], y[i])) return false;
            return true;
        }

        /// -c preserving the Python int/float boxing.
        static object PyNeg(object v)
        {
            if (v is int i) return (long)(-i);
            if (v is long l) return -l;
            if (v is double d) return -d;
            if (v is Rational r) return -r;
            return v;
        }

        /// str() of a Python value as it appears in f-strings (why payloads).
        public static string PyStr(object v)
        {
            if (v is Rational r)
                return r.Den == 1 ? r.Num.ToString(CultureInfo.InvariantCulture) : PyFloatStr(r.ToDouble());
            if (v is double d) return PyFloatStr(d);
            if (v is bool b) return b ? "True" : "False";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// Python repr() of a list of numbers: "[1, 0, 0, 2.666667, -2]".
        public static string PyListStr(List<object> items)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(PyStr(items[i]));
            }
            return sb.Append("]").ToString();
        }

        // Python str(float): shortest roundtrip decimal, always with a '.'
        // ("2.0", "0.0"); exponent form mirrors repr() outside -4..17.
        public static string PyFloatStr(double d)
        {
            if (double.IsNaN(d)) return "nan";
            if (double.IsPositiveInfinity(d)) return "inf";
            if (double.IsNegativeInfinity(d)) return "-inf";
            bool neg = d < 0 || (d == 0 && double.IsNegative(d));   // -0.0 -> "-0.0"
            double x = Math.Abs(d);
            // shortest fixed-point roundtrip (game-scale magnitudes)
            string fixedStr = null;
            for (int p = 0; p <= 17; p++)
            {
                string s = x.ToString("F" + p, CultureInfo.InvariantCulture);
                if (double.Parse(s, CultureInfo.InvariantCulture) == x) { fixedStr = s; break; }
            }
            int decExp = DecimalExponent(x);
            if (decExp < -4 || decExp >= 17)
            {
                // exponential repr: shortest digits, e-XX / e+XX (2-digit exp)
                for (int p = 0; p <= 16; p++)
                {
                    string e = x.ToString("E" + p, CultureInfo.InvariantCulture);
                    if (double.Parse(e, CultureInfo.InvariantCulture) == x)
                    {
                        e = TrimMantissaZeros(e);
                        var parts = e.Split('E');
                        int ex = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        return (neg ? "-" : "") + parts[0] + "e" + (ex < 0 ? "-" : "+")
                               + Math.Abs(ex).ToString("D2", CultureInfo.InvariantCulture);
                    }
                }
            }
            if (fixedStr == null) fixedStr = x.ToString("R", CultureInfo.InvariantCulture);
            if (fixedStr.IndexOf('.') < 0 && fixedStr.IndexOf('e') < 0) fixedStr += ".0";
            return (neg ? "-" : "") + fixedStr;
        }

        static string TrimMantissaZeros(string e)
        {
            var parts = e.Split('E');
            string m = parts[0];
            if (m.IndexOf('.') >= 0)
            {
                m = m.TrimEnd('0').TrimEnd('.');
                if (m == "") m = "0";
            }
            return m + "E" + parts[1];
        }

        static int DecimalExponent(double x)
        {
            if (x == 0) return 0;
            return (int)Math.Floor(Math.Log10(x));
        }
    }
}
