// Port of sim/los.py — line of sight per IA Rules Reference Guide.
//
// "To determine line of sight, the player draws two imaginary, non-intersecting
// lines from one corner of the attacking figure's space to two adjacent corners
// of the target's space. If either of these lines passes through a wall, figure,
// or blocking terrain, then the figure does not have line of sight to the
// target." Corner/edge tracing is explicitly legal; impassable terrain does not
// block LOS; LOS from/to ANY cell of a large figure; blocking terrain occupied
// by the attacker or target (Massive) is transparent for that query.
//
// GRID ADAPTATION (design decision - IA is a hex game, this sim is square
// grid; the C# port uses IC2's own raycaster, concept 3.6). Rules:
//   1. Opaque unit edges (walls, closed doors, blocking-terrain borders and
//      blocking edges) are merged into maximal straight CHAINS.
//   2. A LOS line is blocked if it properly crosses a chain, or touches/crosses
//      a point in the INTERIOR of a chain while running non-collinear to it
//      (this also kills straight shots and junction pinches through wall
//      columns of >= 2 edges, whose junctions are chain-interior points).
//   3. Contact at a chain TERMINAL endpoint is legal = cornering around a wall
//      end (the classic IA corner peek). Collinear overlap along a chain is
//      legal = tracing along a wall ("lines can be traced along the corners
//      and edges").
//   3b. Boundary pinch: a chain endpoint lying ON the board's outer border
//      with the chain perpendicular to that border (wall flush to map edge)
//      is NOT a legal grazing point - there is no space to trace around it.
//   4. Consequence to pin as intentional: a lone single-edge wall between two
//      directly adjacent figures can be seen past by corner tracing (both
//      corner lines graze the wall's endpoints) - IA hex cornering analogue.
//   5. The two lines of a pair must be non-intersecting: pairs where the
//      attacker corner and the target face are collinear (overlapping lines)
//      are rejected.
//
// Dynamic blockers (other figures) are supplied per query; the focus procedure
// uses static LOS only (LOS is validated at execution time, concept 3.3/3.6).
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    using Point = Geometry.Point;
    using Segment = Geometry.Segment;

    public sealed class Los
    {
        public readonly Board Board;
        int _version = -1;
        List<Geometry.Segment> _chains = new List<Geometry.Segment>();
        HashSet<Cell> _blockingCells = new HashSet<Cell>();
        // Python keys on (frozenset(src), frozenset(tgt)); a sorted-cell
        // string key gives identical set semantics.
        Dictionary<string, bool> _pairCache = new Dictionary<string, bool>();

        public Los(Board board) { Board = board; }

        int BoardVersion()
        {
            // sum(1 for d in self.board.doors.values() if d.open)
            int n = 0;
            foreach (var d in Board.Doors.Values) if (d.IsOpen) n++;
            return n;
        }

        void EnsureStatic()
        {
            int v = BoardVersion();
            if (v != _version)
            {
                _version = v;
                _chains = MergeChains(Board.WallSegments());
                _blockingCells = Board.BlockingCells();
                _pairCache.Clear();
            }
        }

        // ------------------------------------------------------------ internals

        static List<Point> Corners(Cell cell)
        {
            Rational x = new Rational(cell.X), y = new Rational(cell.Y);
            return new List<Point>
            {
                new Point(x, y), new Point(x + 1, y),
                new Point(x + 1, y + 1), new Point(x, y + 1),
            };
        }

        /// The four adjacent-corner pairs of a unit square.
        static List<(Point, Point)> CornerPairs(Cell cell)
        {
            var c = Corners(cell);
            var outl = new List<(Point, Point)>();
            for (int i = 0; i < 4; i++) outl.Add((c[i], c[(i + 1) % 4]));
            return outl;
        }

        /// Merge collinear edge segments that touch endpoint-to-endpoint into
        /// maximal straight chains.
        public static List<Segment> MergeChains(List<Segment> segments)
        {
            var h = new Dictionary<Rational, List<Rational[]>>();   // y -> x spans
            var v = new Dictionary<Rational, List<Rational[]>>();   // x -> y spans
            foreach (var seg in segments)
            {
                var a = seg.A; var b = seg.B;
                if (a.Y == b.Y)                                     // horizontal
                {
                    if (!h.TryGetValue(a.Y, out var l)) { l = new List<Rational[]>(); h[a.Y] = l; }
                    var lo = Rational.Min(a.X, b.X); var hi = Rational.Max(a.X, b.X);
                    l.Add(new Rational[] { lo, hi });
                }
                else if (a.X == b.X)                                // vertical
                {
                    if (!v.TryGetValue(a.X, out var l)) { l = new List<Rational[]>(); v[a.X] = l; }
                    var lo = Rational.Min(a.Y, b.Y); var hi = Rational.Max(a.Y, b.Y);
                    l.Add(new Rational[] { lo, hi });
                }
                else
                    throw new ArgumentException("non-grid-aligned opaque segment "
                        + "(" + a.X + ", " + a.Y + ")-(" + b.X + ", " + b.Y + ")");
            }

            var chains = new List<Segment>();
            foreach (var kv in h)
                foreach (var sp in MergeSpans(kv.Value))
                    chains.Add(new Segment(new Point(sp[0], kv.Key), new Point(sp[1], kv.Key)));
            foreach (var kv in v)
                foreach (var sp in MergeSpans(kv.Value))
                    chains.Add(new Segment(new Point(kv.Key, sp[0]), new Point(kv.Key, sp[1])));
            return chains;
        }

        static List<Rational[]> MergeSpans(List<Rational[]> spans)
        {
            spans.Sort((p, q) =>
            {
                int c = p[0].CompareTo(q[0]);
                return c != 0 ? c : p[1].CompareTo(q[1]);
            });
            var outl = new List<Rational[]>();
            foreach (var sp in spans)
            {
                if (outl.Count > 0 && sp[0] <= outl[outl.Count - 1][1])
                {
                    var last = outl[outl.Count - 1];
                    last[1] = Rational.Max(last[1], sp[1]);
                }
                else outl.Add(new Rational[] { sp[0], sp[1] });
            }
            return outl;
        }

        /// s contacts chain m at m's endpoint which sits on the board's
        /// outer border with m perpendicular to it: no room to corner-trace.
        bool BoundaryPinched(Segment s, Segment m)
        {
            Rational W = new Rational(Board.Width), H = new Rational(Board.Height);
            var a = s.A; var b = s.B;
            var m1 = m.A; var m2 = m.B;
            foreach (var v in new[] { m1, m2 })
            {
                if (Geometry.Orient2D(a, b, v) != 0) continue;
                if (!(Rational.Min(a.X, b.X) <= v.X && v.X <= Rational.Max(a.X, b.X)
                      && Rational.Min(a.Y, b.Y) <= v.Y && v.Y <= Rational.Max(a.Y, b.Y)))
                    continue;
                if (Geometry.Orient2D(m1, m2, a) == 0 && Geometry.Orient2D(m1, m2, b) == 0)
                    continue;                                    // collinear: trace along wall
                bool onHBorder = v.Y == Rational.Zero || v.Y == H;
                bool onVBorder = v.X == Rational.Zero || v.X == W;
                bool mVertical = m1.X == m2.X;
                bool mHorizontal = m1.Y == m2.Y;
                if ((onHBorder && mVertical) || (onVBorder && mHorizontal))
                    return true;
            }
            return false;
        }

        /// True if s hits a point in the open interior of chain m while not
        /// running collinear to it (crossing, junction pass-through, or s ending
        /// on the wall line). Terminal endpoint contacts are legal.
        static bool TouchesChainInterior(Segment s, Segment m)
        {
            var a = s.A; var b = s.B;
            var m1 = m.A; var m2 = m.B;
            // is s collinear with m's line?
            if (Geometry.Orient2D(m1, m2, a) == 0 && Geometry.Orient2D(m1, m2, b) == 0)
                return false;
            // Compute intersection of s with m's supporting line, then check it
            // is inside the open span of m. m is axis-aligned (grid chains):
            // test by coordinate.
            var dx = b.X - a.X; var dy = b.Y - a.Y;
            if (m1.Y == m2.Y)                                    // horizontal chain at y=Y
            {
                var Y = m1.Y;
                var lo = Rational.Min(m1.X, m2.X);
                var hi = Rational.Max(m1.X, m2.X);
                if (dy.IsZero)                                   // horizontal s: collinear ruled out above
                    return false;
                var t = (Y - a.Y) / dy;
                if (!(Rational.Zero <= t && t <= Rational.One))
                    return false;
                var X = a.X + t * dx;
                return lo < X && X < hi;
            }
            else                                                 // vertical chain at x=X
            {
                var X = m1.X;
                var lo = Rational.Min(m1.Y, m2.Y);
                var hi = Rational.Max(m1.Y, m2.Y);
                if (dx.IsZero)
                    return false;
                var t = (X - a.X) / dx;
                if (!(Rational.Zero <= t && t <= Rational.One))
                    return false;
                var Y = a.Y + t * dy;
                return lo < Y && Y < hi;
            }
        }

        bool SegmentBlocked(Segment s, List<Segment> chains,
                            ICollection<Cell> blockingCells, ICollection<Cell> figureCells)
        {
            foreach (var m in chains)
                if (Geometry.SegmentsCrossProperly(s, m) || TouchesChainInterior(s, m)
                        || BoundaryPinched(s, m))
                    return true;
            foreach (var c in blockingCells)
                if (Geometry.SegmentEntersSquareInterior(s, c))
                    return true;
            foreach (var c in figureCells)
                if (Geometry.SegmentEntersSquareInterior(s, c))
                    return true;
            return false;
        }

        // --------------------------------------------------------------- public
        public bool HasLos(IEnumerable<Cell> sourceCells, IEnumerable<Cell> targetCells,
                           IEnumerable<Cell> figureCells = null,
                           IEnumerable<Cell> ignoreBlockingCells = null)
        {
            EnsureStatic();
            var src = Distinct(sourceCells);                     // dict.fromkeys order
            var tgt = Distinct(targetCells);
            var blkCells = new HashSet<Cell>(_blockingCells);
            if (ignoreBlockingCells != null) blkCells.ExceptWith(ignoreBlockingCells);
            var fig = figureCells != null ? Distinct(figureCells) : new List<Cell>();

            foreach (var a in src)
                foreach (var p in Corners(a))
                    foreach (var b in tgt)
                        foreach (var pair in CornerPairs(b))
                        {
                            var q1 = pair.Item1; var q2 = pair.Item2;
                            var s1 = new Segment(p, q1);
                            var s2 = new Segment(p, q2);
                            // overlapping lines violate RAW "non-intersecting"
                            if (Geometry.Orient2D(p, q1, q2) == 0) continue;
                            if (Geometry.SegmentsCrossProperly(s1, s2)) continue;
                            if (SegmentBlocked(s1, _chains, blkCells, fig)) continue;
                            if (SegmentBlocked(s2, _chains, blkCells, fig)) continue;
                            return true;
                        }
            return false;
        }

        /// Static-only LOS (walls/doors/blocking terrain); used by focus.
        public bool StaticLos(IEnumerable<Cell> sourceCells, IEnumerable<Cell> targetCells)
        {
            var key = CacheKey(sourceCells) + "|" + CacheKey(targetCells);
            bool cached;
            if (!_pairCache.TryGetValue(key, out cached))
            {
                cached = HasLos(sourceCells, targetCells);
                _pairCache[key] = cached;
            }
            return cached;
        }

        static string CacheKey(IEnumerable<Cell> cells)
        {
            var sorted = new List<Cell>(new HashSet<Cell>(cells));
            sorted.Sort();
            var sb = new System.Text.StringBuilder();
            foreach (var c in sorted) sb.Append(c.X).Append(',').Append(c.Y).Append(';');
            return sb.ToString();
        }

        static List<Cell> Distinct(IEnumerable<Cell> cells)
        {
            var seen = new HashSet<Cell>();
            var outl = new List<Cell>();
            foreach (var c in cells) if (seen.Add(c)) outl.Add(c);
            return outl;
        }
    }
}
