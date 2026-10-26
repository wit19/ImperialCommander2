// Port of sim/geometry.py — grid primitives, footprints, exact segments.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public readonly struct Cell : IEquatable<Cell>, IComparable<Cell>
    {
        public readonly int X;
        public readonly int Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public bool Equals(Cell o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => X * 39323 + Y;
        public int CompareTo(Cell o) => X != o.X ? X.CompareTo(o.X) : Y.CompareTo(o.Y);
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
        public override string ToString() => "(" + X + "," + Y + ")";
    }

    public static class Geometry
    {
        public static readonly Dictionary<string, int[]> FootprintSizes =
            new Dictionary<string, int[]>
        {
            { "Small1x1",  new int[] { 1, 1 } },
            { "Medium1x2", new int[] { 1, 2 } },
            { "Large2x2",  new int[] { 2, 2 } },
            { "Huge2x3",   new int[] { 2, 3 } },
        };

        // 0=N,1=E,2=S,3=W
        public static readonly Cell[] DirVectors =
            { new Cell(0, 1), new Cell(1, 0), new Cell(0, -1), new Cell(-1, 0) };
        public static readonly Cell[] DiagVectors =
            { new Cell(1, 1), new Cell(1, -1), new Cell(-1, -1), new Cell(-1, 1) };

        public static int[] SizeOf(string miniSize)
        {
            if (!FootprintSizes.TryGetValue(miniSize, out var s))
                throw new ArgumentException("unknown miniSize '" + miniSize + "'");
            return s;
        }

        public static bool IsLarge(string miniSize)
        {
            var s = SizeOf(miniSize);
            return s[0] != 1 || s[1] != 1;
        }

        public static int[] FootprintDims(string miniSize, int orientation)
        {
            var s = SizeOf(miniSize);
            if (orientation != 0 && orientation != 1)
                throw new ArgumentException("orientation must be 0 or 1");
            return orientation == 0 ? new int[] { s[0], s[1] } : new int[] { s[1], s[0] };
        }

        public static List<Cell> FootprintCells(Cell anchor, string miniSize, int orientation)
        {
            var d = FootprintDims(miniSize, orientation);
            var outl = new List<Cell>();
            for (int y = anchor.Y; y < anchor.Y + d[1]; y++)
                for (int x = anchor.X; x < anchor.X + d[0]; x++)
                    outl.Add(new Cell(x, y));
            return outl;
        }

        public static int RotationOverlap(string miniSize)
        {
            var s = SizeOf(miniSize);
            return (s[0] * s[1] + 1) / 2;
        }

        public static bool CardinallyAdjacent(Cell a, Cell b) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

        public static bool CellsAdjacent(ICollection<Cell> a, ICollection<Cell> b)
        {
            foreach (var c in a)
                foreach (var d in DirVectors)
                    if (b.Contains(new Cell(c.X + d.X, c.Y + d.Y))) return true;
            return false;
        }

        public static int ChebyshevGap(IEnumerable<Cell> a, IEnumerable<Cell> b)
        {
            int best = int.MaxValue;
            foreach (var pa in a)
                foreach (var pb in b)
                {
                    int d = Math.Max(Math.Abs(pa.X - pb.X), Math.Abs(pa.Y - pb.Y));
                    if (d < best) best = d;
                }
            return best;
        }

        // ---- exact segment helpers (shared with Los.cs) ----
        public struct Point { public Rational X, Y; public Point(Rational x, Rational y) { X = x; Y = y; } }
        public struct Segment { public Point A, B; public Segment(Point a, Point b) { A = a; B = b; } }

        public static int Orient2D(Point a, Point b, Point c)
        {
            var v = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            return v > Rational.Zero ? 1 : v < Rational.Zero ? -1 : 0;
        }

        /// True iff the segments cross at a point interior to BOTH (strict
        /// sign flips on both sides); grazing touches are NOT crossings
        /// (IA LOS rule: lines may run along corners and edges).
        public static bool SegmentsCrossProperly(Segment s1, Segment s2)
        {
            int o1 = Orient2D(s1.A, s1.B, s2.A);
            int o2 = Orient2D(s1.A, s1.B, s2.B);
            int o3 = Orient2D(s2.A, s2.B, s1.A);
            int o4 = Orient2D(s2.A, s2.B, s1.B);
            return o1 * o2 < 0 && o3 * o4 < 0;
        }

        /// True iff the segment passes through the OPEN interior of the unit
        /// square of `cell` (Liang-Barsky slab clip in exact rationals).
        public static bool SegmentEntersSquareInterior(Segment s, Cell cell)
        {
            var p = s.A; var q = s.B;
            var loX = new Rational(cell.X); var hiX = new Rational(cell.X + 1);
            var loY = new Rational(cell.Y); var hiY = new Rational(cell.Y + 1);
            var dx = q.X - p.X; var dy = q.Y - p.Y;
            var t0 = Rational.Zero; var t1 = Rational.One;
            for (int axis = 0; axis < 2; axis++)
            {
                var d = axis == 0 ? dx : dy;
                var lo = axis == 0 ? loX : loY;
                var hi = axis == 0 ? hiX : hiY;
                var start = axis == 0 ? p.X : p.Y;
                if (d.IsZero)
                {
                    if (!(lo < start && start < hi)) return false;
                }
                else
                {
                    var ta = (lo - start) / d;
                    var tb = (hi - start) / d;
                    if (ta > tb) { var tmp = ta; ta = tb; tb = tmp; }
                    t0 = Rational.Max(t0, ta);
                    t1 = Rational.Min(t1, tb);
                    if (t0 >= t1) return false;
                }
            }
            var tm = (t0 + t1) / 2;
            var mx = p.X + tm * dx;
            var my = p.Y + tm * dy;
            return loX < mx && mx < hiX && loY < my && my < hiY;
        }
    }
}
