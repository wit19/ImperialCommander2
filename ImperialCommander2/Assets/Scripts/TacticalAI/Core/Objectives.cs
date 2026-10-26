// Port of sim/objectives.py — runtime objective state machine + the shared
// terrain cost field.
//
// IA Rules Reference Guide verbatim (OBJECTIVES + ESCAPE):
//   * "Objectives with timers require a specified number of interactions to
//     be completed." "One interaction is any action that requires an
//     interaction with an object (defined by the mission)."
//   * "The Imperial player must prevent any hero from achieving the
//     scenario's goal... They do so by incapacitating all heroes or by
//     standing on the objective space at the end of the final round."
//   * ESCAPE: "Escape doors can be opened with the Open special move or
//     with the Interact icon on a hero's order card, which closes after use."
//   * "Once the Escape door is open, any hero can move through it and escape."
//     A figure leaving the map through an open Escape door is removed,
//     counted as escaped, and can no longer be targeted (hero not dead).
//
// Imperial-side doctrine consequences (concept 3.5):
//   * Terminal/item/escape objectives with interactions left are HARD
//     blockers (the AI cannot legally interrupt an Interact, so it must
//     react at range or via stun); a Guard's zone is the objective space.
//   * The Imperial win check at the final round is "nobody stands in the
//     zone", NOT "the objective is uncompleted" (refguide guard quirk).
//   * Escort figures never enter the Imperial targeting weights.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public sealed class ObjectiveState
    {
        public string Id;
        public string Role;
        public HashSet<Cell> Cells;
        public double Weight;
        public int Needed;
        public int Progress;
        public bool Done;

        // interactions needed per RefGuide-typical objective role; 0 = open
        // on trigger (escape doors, zone checks)
        public static readonly Dictionary<string, int> InteractCost =
            new Dictionary<string, int>
        {
            { "terminal", 2 }, { "item", 1 }, { "escape", 0 }, { "guard_zone", 0 },
        };

        public static readonly string[] Roles = { "terminal", "item", "escape", "guard_zone" };

        public ObjectiveState(string id, string role, IEnumerable<Cell> cells, double weight)
        {
            Id = id;
            Role = role;
            Cells = new HashSet<Cell>(cells);
            Weight = weight;
            if (!InteractCost.TryGetValue(role, out Needed))
                throw new ArgumentException("unknown objective role '" + role + "' in " + id);
        }

        /// Python accepts a dict spec {id, role?, cells, weight} or an
        /// ObjectiveDef spec (Board.cs).
        public static ObjectiveState FromSpec(Dictionary<string, object> rec)
        {
            return new ObjectiveState(
                Json.AsStr(rec["id"]),
                Json.AsStr(Json.Get(rec, "role", "item")),
                Json.AsCells(rec["cells"]),
                Json.AsDouble(Json.Get(rec, "weight", 1.0)));
        }

        public static ObjectiveState FromSpec(ObjectiveDef def)
        {
            return new ObjectiveState(def.Id, def.Role, def.Cells, def.Weight);
        }

        public int InteractionsLeft => Math.Max(0, Needed - Progress);

        public bool HeroStandsOn(Figure fig)
        {
            foreach (var c in SpaceLegality.FigureCells(fig))
                if (Cells.Contains(c)) return true;
            return false;
        }

        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "id", Id },
                { "role", Role },
                { "weight", Weight },
                { "progress", (long)Progress },
                { "needed", (long)Needed },
                { "done", Done },
            };
        }
    }

    public sealed class ObjectiveRuntime
    {
        public readonly List<ObjectiveState> Objectives;
        readonly Dictionary<string, ObjectiveState> _byId = new Dictionary<string, ObjectiveState>();

        public ObjectiveState this[string id] => _byId[id];

        public ObjectiveState ById(string id) => _byId.TryGetValue(id, out var o) ? o : null;

        public ObjectiveRuntime(Board board)
        {
            Objectives = new List<ObjectiveState>();
            foreach (var d in board.Objectives)
            {
                var o = ObjectiveState.FromSpec(d);
                Objectives.Add(o);
                _byId[o.Id] = o;
            }
        }

        public List<ObjectiveState> ByRole(string role)
        {
            var outl = new List<ObjectiveState>();
            foreach (var o in Objectives) if (o.Role == role) outl.Add(o);
            return outl;
        }

        /// One Interact action against obj_id. Returns True if progress was
        /// made; False for open-on-trigger objectives (escape, guard zone).
        public bool Interact(string objId)
        {
            var obj = _byId[objId];                 // KeyError in Python
            if (obj.Needed == 0 || obj.Done) return false;
            obj.Progress += 1;
            if (obj.Progress >= obj.Needed) obj.Done = true;
            return true;
        }

        public void WalkedEscape(ObjectiveState o)
        {
            // Escape door must be open first; opening one closes all others
            // (single switch - escape.json vectors).
            if (o.Needed == 0 && !o.Done) o.Done = true;
            if (!o.Done) return;
            var esc = new List<ObjectiveState>();
            foreach (var x in Objectives)
                if (x.Role == "escape" && !x.Done) esc.Add(x);
            for (int i = 0; i < esc.Count; i++)
                if (!ReferenceEquals(esc[i], esc[0])) esc[i].Done = true;
        }

        /// Python open_objectives(): unfinished, escape-role excluded
        /// (escapes join the race set only through EscapeOpen()).
        public List<ObjectiveState> OpenObjectives()
        {
            var outl = new List<ObjectiveState>();
            foreach (var o in Objectives)
                if (!o.Done && o.Role != "escape") outl.Add(o);
            return outl;
        }

        /// Python escape_gate_open(): the escape counts once EVERY terminal
        /// objective is done (vacuously true when the board has none).
        public bool EscapeGateOpen()
        {
            foreach (var o in Objectives)
                if (o.Role == "terminal" && !o.Done) return false;
            return true;
        }

        /// Python escape_open(): unfinished escapes, only while the gate is
        /// open. Returns an empty list (Python truthiness: []) when closed.
        public List<ObjectiveState> EscapeOpen()
        {
            var outl = new List<ObjectiveState>();
            if (!EscapeGateOpen()) return outl;
            foreach (var o in Objectives)
                if (o.Role == "escape" && !o.Done) outl.Add(o);
            return outl;
        }

        // --- aliases used by the Victory.cs porter (Python: escape_open /
        // the raw objectives list; Victory.cs filters .Done itself) ---
        public List<ObjectiveState> OpenEscape() => EscapeOpen();

        public List<ObjectiveState> OpenObjectivesAll() => new List<ObjectiveState>(Objectives);

        /// Python rebel_victory(): every non-escape objective done AND
        /// every escape objective done.
        public bool RebelVictory()
        {
            foreach (var o in Objectives)
                if (o.Role != "escape" && !o.Done) return false;
            foreach (var o in Objectives)
                if (o.Role == "escape" && !o.Done) return false;
            return true;
        }

        /// Refguide guard win: NO hero standing in the zone at round cap
        /// (a completed objective would mean the rebels already won).
        public bool GuardZoneHeldAtRoundCap(List<Figure> heroes)
        {
            var zones = ByRole("guard_zone");
            if (zones.Count == 0) return false;
            foreach (var h in heroes)
                foreach (var oz in zones)
                    if (oz.HeroStandsOn(h)) return false;
            return true;
        }

        /// Mission role weight (concept 3.5): higher = more valuable to hold
        /// near. Objectives with interactions left get an urgency bump.
        public double HeroWeightForGoal(ObjectiveState goal)
        {
            if (goal == null) return 0.0;
            if (goal.Role == "guard_zone") return goal.Weight;
            double bump = goal.InteractionsLeft > 0 ? 1.0 : 0.0;
            return goal.Weight + bump;
        }

        public List<Dictionary<string, object>> AsList()
        {
            var outl = new List<Dictionary<string, object>>();
            foreach (var o in Objectives)
            {
                var cellsList = new List<object>();
                foreach (var c in o.Cells)
                    cellsList.Add(new List<object> { (long)c.X, (long)c.Y });
                outl.Add(new Dictionary<string, object>
                {
                    { "id", o.Id },
                    { "role", o.Role },
                    { "cells", cellsList },
                    { "progress", (long)o.Progress },
                    { "needed", (long)o.Needed },
                    { "done", o.Done },
                });
            }
            return outl;
        }

        // ---------------------------------------------------------- terrain cost
        static int? TerrainStep(Board board, Cell a, Cell b)
        {
            string reason = board.BlocksMovement(a, b);
            if (reason == "wall" || reason == "blocking_edge"
                || reason == "closed_door" || reason == "locked_door") return null;
            if (board.IsBlockingCell(b)) return null;
            if (board.IsImpassableCell(b)) return 10;
            if (board.IsDifficult(b)) return 2;
            return 1;
        }

        /// Movement-point cost from goal_cells to every standable cell.
        /// Walls/doors/blocking cells are absolute barriers; difficult = 2,
        /// impassable = 10 (crossable but strategically terrible), figures
        /// cost nothing (IA counting). Deterministic Dijkstra (terrain costs
        /// are static, so the heuristic degenerates to a priority queue).
        public static Dictionary<Cell, int> TerrainCostField(Board board, IEnumerable<Cell> goalCells)
        {
            var dist = new Dictionary<Cell, int>();
            var pq = new List<(int dist, Cell cell)>();
            foreach (var g in goalCells)
            {
                if (!dist.ContainsKey(g))
                {
                    dist[g] = 0;
                    pq.Add((0, g));
                }
            }
            while (pq.Count > 0)
            {
                pq.Sort();                                       // (dist, cell) order like heapq
                var (d, cur) = pq[0];
                pq.RemoveAt(0);
                if (d > (dist.TryGetValue(cur, out int cd) ? cd : World.Inf)) continue;
                for (int i = 0; i < 4; i++)
                {
                    var dv = Geometry.DirVectors[i];
                    var nxt = new Cell(cur.X + dv.X, cur.Y + dv.Y);
                    if (!board.InBounds(nxt)) continue;
                    int? step = TerrainStep(board, cur, nxt);
                    if (step == null) continue;
                    int nd = d + step.Value;
                    if (nd < (dist.TryGetValue(nxt, out int ndOld) ? ndOld : World.Inf))
                    {
                        dist[nxt] = nd;
                        pq.Add((nd, nxt));
                    }
                }
            }
            return dist;
        }
    }
}
