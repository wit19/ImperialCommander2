// Port of sim/space_legality.py — IA Reference-Guide space rules.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public struct Legality
    {
        public bool Ok;
        public string Reason;
        public Legality(bool ok, string reason = "") { Ok = ok; Reason = reason; }
        public static Legality Legal => new Legality(true);
        public static implicit operator bool(Legality l) => l.Ok;
    }

    public static class SpaceLegality
    {
        public static List<Cell> FigureCells(Figure fig) =>
            Geometry.FootprintCells(fig.Anchor, fig.Card.MiniSize, fig.Orientation);

        /// Terrain handling shared by Massive and Mobile.
        public static bool MassiveLike(DeploymentCard card) => card.IsMassive || card.IsMobile;

        // ---------------------------------------------------- placement/entry
        public static Legality FootprintPlaceLegal(Board board, Figure fig, Cell anchor,
                                                   int orientation,
                                                   Dictionary<Cell, List<Figure>> occupants,
                                                   bool? allowFigures = null,
                                                   IEnumerable<Figure> ignore = null)
        {
            var card = fig.Card;
            var cells = Geometry.FootprintCells(anchor, card.MiniSize, orientation);
            foreach (var c in cells)
            {
                if (!board.InBounds(c)) return new Legality(false, "out_of_bounds");
                if (board.IsInterior(c) && card.IsMassive) return new Legality(false, "massive_interior_ban");
                if ((board.IsImpassableCell(c) || board.IsBlockingCell(c)) && !MassiveLike(card))
                    return new Legality(false, "blocking_or_impassable_space");
            }
            var dims = card.Footprint(orientation);
            int w = dims[0], h = dims[1];
            foreach (var c in cells)
            {
                if (c.X + 1 < c.X + w)
                {
                    var reason = board.BlocksMovement(c, new Cell(c.X + 1, c.Y));
                    if (reason != null && !StraddleExempt(card, reason))
                        return new Legality(false, "footprint_over_" + reason);
                }
                if (c.Y + 1 < c.Y + h)
                {
                    var reason = board.BlocksMovement(c, new Cell(c.X, c.Y + 1));
                    if (reason != null && !StraddleExempt(card, reason))
                        return new Legality(false, "footprint_over_" + reason);
                }
            }
            bool endInFigures = allowFigures ?? card.IsMassive;
            foreach (var c in cells)
            {
                if (!occupants.TryGetValue(c, out var others)) continue;
                foreach (var other in others)
                {
                    if (ReferenceEquals(other, fig) || (ignore != null && Contains(ignore, other)) || !other.Alive) continue;
                    if (card.IsMassive && other.Card.IsMassive) return new Legality(false, "two_massive_same_space");
                    if (!endInFigures) return new Legality(false, "cannot_end_in_figure_space");
                }
            }
            return Legality.Legal;
        }

        public static Legality FootprintEnterLegal(Board board, Figure fig, Cell anchor,
                                                   int orientation,
                                                   Dictionary<Cell, List<Figure>> occupants,
                                                   IEnumerable<Figure> ignore = null)
        {
            var card = fig.Card;
            var cells = Geometry.FootprintCells(anchor, card.MiniSize, orientation);
            foreach (var c in cells)
            {
                if (!board.InBounds(c)) return new Legality(false, "out_of_bounds");
                if (board.IsInterior(c) && card.IsMassive) return new Legality(false, "massive_interior_ban");
                if ((board.IsImpassableCell(c) || board.IsBlockingCell(c)) && !MassiveLike(card))
                    return new Legality(false, "blocking_or_impassable_space");
            }
            var dims = card.Footprint(orientation);
            int w = dims[0], h = dims[1];
            foreach (var c in cells)
            {
                if (c.X + 1 < c.X + w)
                {
                    var reason = board.BlocksMovement(c, new Cell(c.X + 1, c.Y));
                    if (reason != null && !StraddleExempt(card, reason))
                        return new Legality(false, "footprint_over_" + reason);
                }
                if (c.Y + 1 < c.Y + h)
                {
                    var reason = board.BlocksMovement(c, new Cell(c.X, c.Y + 1));
                    if (reason != null && !StraddleExempt(card, reason))
                        return new Legality(false, "footprint_over_" + reason);
                }
            }
            foreach (var c in cells)
            {
                if (!occupants.TryGetValue(c, out var others)) continue;
                foreach (var other in others)
                {
                    if (ReferenceEquals(other, fig) || (ignore != null && Contains(ignore, other)) || !other.Alive) continue;
                    if (card.IsMassive && other.Card.IsMassive) return new Legality(false, "two_massive_same_space");
                }
            }
            return Legality.Legal;
        }

        static bool Contains(IEnumerable<Figure> set, Figure f)
        {
            foreach (var x in set) if (ReferenceEquals(x, f)) return true;
            return false;
        }

        /// Which edge types may a Massive/Mobile footprint straddle? Doors/walls are absolute.
        static bool StraddleExempt(DeploymentCard card, string reason)
        {
            if (reason == "locked_door" || reason == "closed_door" || reason == "wall") return false;
            return MassiveLike(card);
        }

        public static Legality MoveStepLegal(Board board, Figure fig, Cell fromAnchor, Cell toAnchor,
                                             int orientation, Dictionary<Cell, List<Figure>> occupants,
                                             bool lastMp)
        {
            var leg = FootprintEnterLegal(board, fig, toAnchor, orientation, occupants, new[] { fig });
            if (!leg.Ok) return leg;
            var newCells = new HashSet<Cell>(Geometry.FootprintCells(toAnchor, fig.Card.MiniSize, orientation));
            var oldCells = new HashSet<Cell>(Geometry.FootprintCells(fromAnchor, fig.Card.MiniSize, orientation));
            newCells.ExceptWith(oldCells);
            if (lastMp)
                foreach (var c in newCells)
                    if (occupants.TryGetValue(c, out var others))
                        foreach (var other in others)
                        {
                            if (ReferenceEquals(other, fig) || !other.Alive) continue;
                            if (other.Team != fig.Team && !fig.Card.IsMassive)
                                return new Legality(false, "hostile_space_with_last_mp");
                        }
            return Legality.Legal;
        }

        // ----------------------------------------------------------- rotation
        public static Legality RotateLegal(Board board, Figure fig, Cell anchor, int orientation,
                                           Dictionary<Cell, List<Figure>> occupants)
        {
            var card = fig.Card;
            if (!card.IsLargeBase) return new Legality(false, "not_a_large_figure");
            var f0 = card.Footprint(0); var f1 = card.Footprint(1);
            if (f0[0] == f1[0] && f0[1] == f1[1])
                return new Legality(false, "square_footprint_rotation_noop");
            int newOrient = 1 - orientation;
            var oldCells = new HashSet<Cell>(Geometry.FootprintCells(anchor, card.MiniSize, orientation));
            var newCells = new HashSet<Cell>(Geometry.FootprintCells(anchor, card.MiniSize, newOrient));
            oldCells.IntersectWith(newCells);
            if (oldCells.Count < Geometry.RotationOverlap(card.MiniSize))
                return new Legality(false, "rotation_keeps_less_than_half");
            var leg = FootprintEnterLegal(board, fig, anchor, newOrient, occupants, new[] { fig });
            return leg;
        }

        // -------------------------------------------------------------- pushes
        /// Occupants in fig's cells; own team first, then hostiles; stable by index.
        public static List<Figure> PushCandidates(Board board, Figure fig, Dictionary<Cell, List<Figure>> occupants)
        {
            var cells = new HashSet<Cell>(FigureCells(fig));
            var victims = new List<Figure>();
            var seen = new HashSet<Figure>();
            var sorted = new List<Cell>(cells); sorted.Sort();
            foreach (var c in sorted)
                if (occupants.TryGetValue(c, out var list))
                    foreach (var other in list)
                    {
                        if (ReferenceEquals(other, fig) || !other.Alive) continue;
                        if (!seen.Add(other)) continue;
                        victims.Add(other);
                    }
            victims.Sort((x, y) =>
            {
                int a = x.Team == fig.Team ? 0 : 1, b = y.Team == fig.Team ? 0 : 1;
                return a != b ? a.CompareTo(b) : x.Index.CompareTo(y.Index);
            });
            return victims;
        }

        public static Cell? NearestEmptyCell(Board board, Cell fromCell,
                                             Dictionary<Cell, List<Figure>> occupants,
                                             Figure forFig)
        {
            var seen = new HashSet<Cell> { fromCell };
            var q = new Queue<Cell>();
            q.Enqueue(fromCell);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                var leg = FootprintPlaceLegal(board, forFig, c, forFig.Orientation, occupants, null, new[] { forFig });
                var fc = Geometry.FootprintCells(c, forFig.Card.MiniSize, forFig.Orientation);
                if (leg.Ok && CellFree(occupants, fc, forFig)) return c;
                foreach (var d in Geometry.DirVectors)
                {
                    var n = new Cell(c.X + d.X, c.Y + d.Y);
                    if (seen.Contains(n) || !board.InBounds(n)) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
            }
            return null;
        }

        public static bool CellFree(Dictionary<Cell, List<Figure>> occupants, IEnumerable<Cell> cells, Figure mover)
        {
            foreach (var c in cells)
                if (occupants.TryGetValue(c, out var list))
                    foreach (var other in list)
                    {
                        if (ReferenceEquals(other, mover) || !other.Alive) continue;
                        return false;
                    }
            return true;
        }

        public static Cell? NearestAnchor(Board board, Figure victim, Dictionary<Cell, List<Figure>> occupants, Figure mover)
        {
            var oldAnchor = victim.Anchor;
            var seen = new HashSet<Cell> { oldAnchor };
            var q = new Queue<Cell>();
            q.Enqueue(oldAnchor);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                var leg = FootprintPlaceLegal(board, victim, c, victim.Orientation, occupants, null, new[] { victim, mover });
                var fc = Geometry.FootprintCells(c, victim.Card.MiniSize, victim.Orientation);
                if (leg.Ok && CellFree(occupants, fc, victim)) return c;
                foreach (var d in Geometry.DirVectors)
                {
                    var n = new Cell(c.X + d.X, c.Y + d.Y);
                    if (seen.Contains(n) || !board.InBounds(n)) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
            }
            return null;
        }

        public static List<Figure> ApplyMassivePush(Board board, Figure mover, Dictionary<Cell, List<Figure>> occupants)
        {
            var moved = new List<Figure>();
            foreach (var victim in PushCandidates(board, mover, occupants))
            {
                var newAnchor = NearestAnchor(board, victim, occupants, mover);
                if (newAnchor == null) continue;
                Detach(occupants, victim);
                victim.Anchor = newAnchor.Value;
                Attach(occupants, victim);
                moved.Add(victim);
            }
            return moved;
        }

        public static void Detach(Dictionary<Cell, List<Figure>> occupants, Figure fig)
        {
            foreach (var c in FigureCells(fig))
                if (occupants.TryGetValue(c, out var list) && list.Contains(fig))
                    list.Remove(fig);
        }

        public static void Attach(Dictionary<Cell, List<Figure>> occupants, Figure fig)
        {
            foreach (var c in FigureCells(fig))
            {
                if (!occupants.TryGetValue(c, out var list)) { list = new List<Figure>(); occupants[c] = list; }
                list.Add(fig);
            }
        }

        public static bool DoorClosePush(Board board, Figure fig, (Cell, Cell) doorEdge,
                                         Dictionary<Cell, List<Figure>> occupants,
                                         out Cell newAnchor)
        {
            newAnchor = default;
            var cells = new HashSet<Cell>(FigureCells(fig));
            if (!(cells.Contains(doorEdge.Item1) && cells.Contains(doorEdge.Item2))) return false;
            var anchor = NearestAnchor(board, fig, occupants, fig);
            if (anchor == null) return false;
            Detach(occupants, fig);
            fig.Anchor = anchor.Value;
            Attach(occupants, fig);
            newAnchor = anchor.Value;
            return true;
        }
    }
}
