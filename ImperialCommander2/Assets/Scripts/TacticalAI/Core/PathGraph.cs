// Port of sim/pathgraph.py — rule-true movement costs over states
// (anchor, orientation); Dijkstra with focus/move modes.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public struct Transition
    {
        public string Kind;            // step | diagonal | rotate
        public Cell Anchor;
        public int Orientation;
        public int Cost;
        public List<Cell> Entered;
        public Transition(string kind, Cell anchor, int orientation, int cost, List<Cell> entered)
        { Kind = kind; Anchor = anchor; Orientation = orientation; Cost = cost; Entered = entered; }
    }

    public sealed class PathResult
    {
        public int Cost;
        public List<(Cell, int)> States;   // intermediate + final, excludes start
        public (Cell, int) Start;
        public PathResult(int cost, List<(Cell, int)> states, (Cell, int) start)
        { Cost = cost; States = states; Start = start; }
        public (Cell, int) Final => States.Count > 0 ? States[States.Count - 1] : Start;
    }

    public sealed class PathGraph
    {
        public readonly Board Board;
        readonly Func<Dictionary<Cell, List<Figure>>> _occupants;

        public PathGraph(Board board, Func<Dictionary<Cell, List<Figure>>> occupantsProvider)
        { Board = board; _occupants = occupantsProvider; }

        static bool EdgeExempt(DeploymentCard card, string reason)
        {
            if (reason == "wall" || reason == "locked_door" || reason == "closed_door") return false;
            return SpaceLegality.MassiveLike(card);
        }

        static bool SpaceHasHostile(Figure fig, Cell cell, Dictionary<Cell, List<Figure>> occupants)
        {
            if (!occupants.TryGetValue(cell, out var list)) return false;
            foreach (var o in list)
                if (!ReferenceEquals(o, fig) && o.Alive && o.Team != fig.Team) return true;
            return false;
        }

        static bool HasHostileEntered(Figure fig, List<Cell> entered, Dictionary<Cell, List<Figure>> occupants)
        {
            foreach (var c in entered) if (SpaceHasHostile(fig, c, occupants)) return true;
            return false;
        }

        // ------------------------------------------------------------ corner rule
        bool CornerSealed(Cell src, Cell dst)
        {
            int px = Math.Min(src.X, dst.X) + 1;
            int py = Math.Min(src.Y, dst.Y) + 1;
            var hard = new HashSet<string> { "wall", "blocking_edge", "impassable_edge", "closed_door", "locked_door" };
            var segs = new (Cell, Cell)[]
            {
                (new Cell(px - 1, py - 1), new Cell(px, py - 1)),   // vertical, row below
                (new Cell(px - 1, py), new Cell(px, py)),           // vertical, row above
                (new Cell(px - 1, py - 1), new Cell(px - 1, py)),   // horizontal, col left
                (new Cell(px, py - 1), new Cell(px, py)),           // horizontal, col right
            };
            int n = 0;
            foreach (var (a, b) in segs)
                if (hard.Contains(Board.BlocksMovement(a, b))) n++;
            var quads = new HashSet<Cell>
            {
                new Cell(px - 1, py), new Cell(px, py), new Cell(px - 1, py - 1), new Cell(px, py - 1),
            };
            quads.Remove(src); quads.Remove(dst);
            foreach (var q in quads)
                if (Board.InBounds(q) && (Board.IsBlockingCell(q) || Board.IsImpassableCell(q))) n++;
            return n >= 2;
        }

        // ------------------------------------------------------------ transitions
        public List<Transition> Transitions(Figure fig, Cell anchor, int orientation,
                                            string mode, int? remainingMp = null)
        {
            var board = Board;
            var card = fig.Card;
            var occupants = mode == "focus" ? new Dictionary<Cell, List<Figure>>() : _occupants();
            var outl = new List<Transition>();

            foreach (var d in Geometry.DirVectors)
            {
                var t = Translate(fig, anchor, new Cell(anchor.X + d.X, anchor.Y + d.Y),
                                  orientation, occupants, mode, remainingMp, "step");
                if (t.HasValue) outl.Add(t.Value);
            }

            // diagonal steps: small figures only (corner-intersection rule)
            if (card.MiniSize == "Small1x1")
                foreach (var d in Geometry.DiagVectors)
                {
                    var tgt = new Cell(anchor.X + d.X, anchor.Y + d.Y);
                    if (!board.InBounds(tgt)) continue;
                    if (CornerSealed(anchor, tgt)) continue;
                    var t = Translate(fig, anchor, tgt, orientation, occupants, mode, remainingMp, "diagonal");
                    if (t.HasValue) outl.Add(t.Value);
                }

            // rotation in place (large non-square figures only)
            var f0 = card.Footprint(0); var f1 = card.Footprint(1);
            if (card.IsLargeBase && !(f0[0] == f1[0] && f0[1] == f1[1]))
            {
                int newOrient = 1 - orientation;
                var leg = SpaceLegality.RotateLegal(board, fig, anchor, orientation, occupants);
                if (leg.Ok)
                {
                    var oldSet = new HashSet<Cell>(Geometry.FootprintCells(anchor, card.MiniSize, orientation));
                    var newCells = Geometry.FootprintCells(anchor, card.MiniSize, newOrient);
                    var entered = new List<Cell>();
                    foreach (var c in newCells) if (!oldSet.Contains(c)) entered.Add(c);
                    entered.Sort();
                    int cost = 1 + Surcharge(fig, entered, occupants, mode);
                    outl.Add(new Transition("rotate", anchor, newOrient, cost, entered));
                }
            }
            return outl;
        }

        Transition? Translate(Figure fig, Cell anchor, Cell to, int orientation,
                              Dictionary<Cell, List<Figure>> occupants, string mode,
                              int? remainingMp, string kind)
        {
            var board = Board;
            var card = fig.Card;
            var old = Geometry.FootprintCells(anchor, card.MiniSize, orientation);
            var newf = Geometry.FootprintCells(to, card.MiniSize, orientation);
            var oldSet = new HashSet<Cell>(old);
            var entered = new List<Cell>();
            foreach (var c in newf) if (!oldSet.Contains(c)) entered.Add(c);
            entered.Sort();
            var moved = new Cell(to.X - anchor.X, to.Y - anchor.Y);
            bool isDir = false;
            foreach (var d in Geometry.DirVectors) if (d == moved) isDir = true;
            if (isDir)
                foreach (var c in entered)
                {
                    var src = new Cell(c.X - moved.X, c.Y - moved.Y);
                    var reason = board.BlocksMovement(src, c);
                    if (reason != null && !EdgeExempt(card, reason)) return null;
                }
            var leg = SpaceLegality.FootprintEnterLegal(board, fig, to, orientation, occupants, new[] { fig });
            if (!leg.Ok) return null;
            int sur = Surcharge(fig, entered, occupants, mode);
            if (mode == "move" && remainingMp.HasValue && !card.IsMassive)
            {
                int cost = 1 + sur;
                if (remainingMp.Value - cost <= 0 && HasHostileEntered(fig, entered, occupants))
                    return null;
            }
            return new Transition(kind, to, orientation, 1 + sur, entered);
        }

        int Surcharge(Figure fig, List<Cell> entered, Dictionary<Cell, List<Figure>> occupants, string mode)
        {
            var card = fig.Card;
            if (SpaceLegality.MassiveLike(card)) return 0;
            bool LargeBase = card.IsLargeBase;
            if (LargeBase)
            {
                foreach (var c in entered)
                {
                    bool hit = mode == "move"
                        ? (Board.IsDifficult(c) || SpaceHasHostile(fig, c, occupants))
                        : Board.IsDifficult(c);
                    if (hit) return 1;
                }
                return 0;
            }
            int total = 0;
            foreach (var c in entered)
            {
                if (Board.IsDifficult(c)) total++;
                if (mode == "move" && SpaceHasHostile(fig, c, occupants)) total++;
            }
            return total;
        }

        // ---------------------------------------------------------------- Dijkstra
        struct PQItem : IComparable<PQItem>
        {
            public int Dist; public Cell Anchor; public int Orient; public int Seq;
            public int CompareTo(PQItem o)
            {
                int c = Dist.CompareTo(o.Dist);
                if (c != 0) return c;
                c = Anchor.CompareTo(o.Anchor);
                if (c != 0) return c;
                c = Orient.CompareTo(o.Orient);
                if (c != 0) return c;
                return Seq.CompareTo(o.Seq);
            }
        }

        bool IsGoal(Figure fig, Cell anchor, int orient, Func<Cell, int, bool> goal, string mode)
        {
            if (!goal(anchor, orient)) return false;
            if (mode != "move") return true;
            return SpaceLegality.FootprintPlaceLegal(Board, fig, anchor, orient, _occupants(), null, new[] { fig }).Ok;
        }

        public PathResult Shortest(Figure fig, Cell startAnchor, int startOrient,
                                   Func<Cell, int, bool> goal, string mode,
                                   int? budget = null, HashSet<Cell> avoidCells = null)
        {
            var start = (startAnchor, startOrient);
            var dist = new Dictionary<(Cell, int), int> { [start] = 0 };
            var prev = new Dictionary<(Cell, int), (Cell, int)?> { [start] = null };
            var pq = new List<PQItem> { new PQItem { Dist = 0, Anchor = startAnchor, Orient = startOrient, Seq = 0 } };
            int seq = 0;
            (Cell, int)? best = null;
            while (pq.Count > 0)
            {
                // heap-pop semantics via sorted list keep Python tie behaviour
                pq.Sort();
                var item = pq[0];
                pq.RemoveAt(0);
                var state = (item.Anchor, item.Orient);
                if (item.Dist > (dist.TryGetValue(state, out var dv) ? dv : int.MaxValue / 2)) continue;
                if (item.Dist > (budget ?? int.MaxValue / 2)) break;
                if (IsGoal(fig, item.Anchor, item.Orient, goal, mode)) { best = state; break; }
                int? remaining = budget.HasValue ? budget.Value - item.Dist : (int?)null;
                foreach (var t in Transitions(fig, item.Anchor, item.Orient, mode, remaining))
                {
                    if (avoidCells != null && avoidCells.Count > 0)
                    {
                        bool hit = false;
                        foreach (var c in t.Entered) if (avoidCells.Contains(c)) { hit = true; break; }
                        if (hit) continue;
                    }
                    int nd = item.Dist + t.Cost;
                    var ns = (t.Anchor, t.Orientation);
                    if (nd < (dist.TryGetValue(ns, out var nv) ? nv : int.MaxValue / 2))
                    {
                        dist[ns] = nd;
                        prev[ns] = state;
                        pq.Add(new PQItem { Dist = nd, Anchor = t.Anchor, Orient = t.Orientation, Seq = ++seq });
                    }
                }
            }
            if (best == null) return null;
            var path = new List<(Cell, int)>();
            var s = best.Value;
            while (prev[s].HasValue) { path.Add(s); s = prev[s].Value; }
            path.Reverse();
            return new PathResult(dist[best.Value], path, start);
        }

        /// All end-legal states within budget -> cost map (move mode).
        public Dictionary<(Cell, int), int> Exhaust(Figure fig, Cell startAnchor, int startOrient,
                                                    int budget, HashSet<Cell> avoidCells = null,
                                                    string mode = "move")
        {
            var start = (startAnchor, startOrient);
            var dist = new Dictionary<(Cell, int), int> { [start] = 0 };
            var pq = new List<PQItem> { new PQItem { Dist = 0, Anchor = startAnchor, Orient = startOrient } };
            int seq = 0;
            while (pq.Count > 0)
            {
                pq.Sort();
                var item = pq[0]; pq.RemoveAt(0);
                var state = (item.Anchor, item.Orient);
                if (item.Dist > (dist.TryGetValue(state, out var dv) ? dv : int.MaxValue / 2)) continue;
                int remaining = budget - item.Dist;
                foreach (var t in Transitions(fig, item.Anchor, item.Orient, mode, remaining))
                {
                    if (avoidCells != null && avoidCells.Count > 0)
                    {
                        bool hit = false;
                        foreach (var c in t.Entered) if (avoidCells.Contains(c)) { hit = true; break; }
                        if (hit) continue;
                    }
                    int nd = item.Dist + t.Cost;
                    if (nd > budget) continue;
                    var ns = (t.Anchor, t.Orientation);
                    if (nd < (dist.TryGetValue(ns, out var nv) ? nv : int.MaxValue / 2))
                    {
                        dist[ns] = nd;
                        pq.Add(new PQItem { Dist = nd, Anchor = t.Anchor, Orient = t.Orientation, Seq = ++seq });
                    }
                }
            }
            var outp = new Dictionary<(Cell, int), int>();
            foreach (var kv in dist)
            {
                if (mode == "move" && !SpaceLegality.FootprintPlaceLegal(Board, fig, kv.Key.Item1, kv.Key.Item2, _occupants(), null, new[] { fig }).Ok)
                    continue;
                outp[kv.Key] = kv.Value;
            }
            return outp;
        }

        public List<(Cell, int)> PathTo(Figure fig, Cell startAnchor, int startOrient,
                                        (Cell, int) goalState, int budget, string mode = "move")
        {
            var start = (startAnchor, startOrient);
            if (goalState.Equals(start)) return new List<(Cell, int)>();
            var dist = new Dictionary<(Cell, int), int> { [start] = 0 };
            var prev = new Dictionary<(Cell, int), (Cell, int)>();
            var pq = new List<PQItem> { new PQItem { Dist = 0, Anchor = startAnchor, Orient = startOrient } };
            int seq = 0;
            bool found = false;
            while (pq.Count > 0)
            {
                pq.Sort();
                var item = pq[0]; pq.RemoveAt(0);
                var state = (item.Anchor, item.Orient);
                if (item.Dist > (dist.TryGetValue(state, out var dv) ? dv : int.MaxValue / 2)) continue;
                if (state.Equals(goalState)) { found = true; break; }
                int remaining = budget - item.Dist;
                foreach (var t in Transitions(fig, item.Anchor, item.Orient, mode, remaining))
                {
                    int nd = item.Dist + t.Cost;
                    if (nd > budget) continue;
                    var ns = (t.Anchor, t.Orientation);
                    if (nd < (dist.TryGetValue(ns, out var nv) ? nv : int.MaxValue / 2))
                    {
                        dist[ns] = nd;
                        prev[ns] = state;
                        pq.Add(new PQItem { Dist = nd, Anchor = t.Anchor, Orient = t.Orientation, Seq = ++seq });
                    }
                }
            }
            if (!found) return null;
            var chain = new List<(Cell, int)> { goalState };
            while (!chain[chain.Count - 1].Equals(start)) chain.Add(prev[chain[chain.Count - 1]]);
            chain.Reverse();
            chain.RemoveAt(0);
            return chain;
        }
    }
}
