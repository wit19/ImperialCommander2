// Port of sim/board.py — tacai.board/1 fixture schema, loader, topology.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public sealed class Door
    {
        public Cell A, B;                 // canonical order (A < B)
        public bool Locked;
        public bool IsOpen;
        public bool Blocks => !IsOpen;    // RefGuide DOORS: closed = blocks all
    }

    public sealed class DeploymentPoint
    {
        public string Id;
        public List<Cell> Cells = new List<Cell>();
        public string[] Colors = { "green" };
        public bool MassiveAllowed;
        public string Description = "";
    }

    public sealed class ObjectiveDef
    {
        public string Id;
        public string Role;               // terminal|item|escort|escape|guard_zone
        public List<Cell> Cells = new List<Cell>();
        public double Weight = 1.0;
    }

    public sealed class Board
    {
        public const string Schema = "tacai.board/1";
        public static readonly string[] CellFlagNames = { "interior", "difficult", "blocking", "impassable" };
        public static readonly string[] EdgeTypeNames = { "wall", "blocking", "impassable" };
        public static readonly string[] PostureNames = { "eliminate", "stall", "protect", "deny" };
        public static readonly string[] CounterTypes = { "woundAll", "incapacitateAll", "surviveRounds", "capture" };

        public string Name;
        public int Width, Height;
        readonly Dictionary<Cell, HashSet<string>> _flags = new Dictionary<Cell, HashSet<string>>();
        public readonly Dictionary<(Cell, Cell), string> Edges = new Dictionary<(Cell, Cell), string>();
        public readonly Dictionary<(Cell, Cell), Door> Doors = new Dictionary<(Cell, Cell), Door>();
        public readonly List<Dictionary<string, object>> Hazards = new List<Dictionary<string, object>>();
        public readonly List<DeploymentPoint> DeploymentPoints = new List<DeploymentPoint>();
        public readonly List<ObjectiveDef> Objectives = new List<ObjectiveDef>();
        public int? RoundLimit;
        public readonly List<Dictionary<string, string>> Triggers = new List<Dictionary<string, string>>();
        public readonly List<Dictionary<string, object>> ProtectedEntities = new List<Dictionary<string, object>>();
        public string PostureOverride;      // null
        public string VictoryCounterName;   // null (board.victory_counter)
        public bool CampaignMetaBonus;

        public Board(string name, int width, int height)
        { Name = name; Width = width; Height = height; }

        public static (Cell, Cell) EdgeKey(Cell a, Cell b)
        {
            if (a.Equals(b)) throw new ArgumentException("degenerate edge");
            if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) != 1)
                throw new ArgumentException("edge endpoints must be cardinal neighbours");
            return a.CompareTo(b) < 0 ? (a, b) : (b, a);
        }

        public void SetFlag(Cell c, string flag)
        {
            if (Array.IndexOf(CellFlagNames, flag) < 0) throw new ArgumentException(flag);
            if (!_flags.TryGetValue(c, out var s)) { s = new HashSet<string>(); _flags[c] = s; }
            s.Add(flag);
        }

        public void AddEdge(Cell a, Cell b, string kind)
        {
            if (Array.IndexOf(EdgeTypeNames, kind) < 0) throw new ArgumentException(kind);
            var k = EdgeKey(a, b);
            if (Edges.ContainsKey(k)) throw new ArgumentException("duplicate edge " + k);
            Edges[k] = kind;
        }

        public void AddDoor(Cell a, Cell b, bool locked, bool initiallyOpen)
        {
            var k = EdgeKey(a, b);
            if (Doors.ContainsKey(k) || Edges.ContainsKey(k)) throw new ArgumentException("duplicate door/wall edge");
            Doors[k] = new Door { A = k.Item1, B = k.Item2, Locked = locked, IsOpen = initiallyOpen };
        }

        public bool InBounds(Cell c) => c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;
        static bool AreNeighbours(Cell a, Cell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;
        bool Has(Cell c, string f) => _flags.TryGetValue(c, out var s) && s.Contains(f);

        public bool IsInterior(Cell c) => Has(c, "interior");
        public bool IsDifficult(Cell c) => Has(c, "difficult");
        public bool IsBlockingCell(Cell c) => Has(c, "blocking");
        public bool IsImpassableCell(Cell c) => Has(c, "impassable");

        public HashSet<Cell> FlaggedCells(string flag)
        {
            var outl = new HashSet<Cell>();
            foreach (var kv in _flags) if (kv.Value.Contains(flag)) outl.Add(kv.Key);
            return outl;
        }

        public string EdgeType(Cell a, Cell b)
        {
            if (!AreNeighbours(a, b)) return null;
            return Edges.TryGetValue(EdgeKey(a, b), out var t) ? t : null;
        }

        public Door DoorAt(Cell a, Cell b)
        {
            if (!AreNeighbours(a, b)) return null;
            return Doors.TryGetValue(EdgeKey(a, b), out var d) ? d : null;
        }

        /// Blocking reason for a normal (non-Massive/Mobile) figure.
        public string BlocksMovement(Cell a, Cell b)
        {
            if (!AreNeighbours(a, b)) return "not_adjacent";
            var t = EdgeType(a, b);
            if (t == "wall") return "wall";
            if (t == "blocking") return "blocking_edge";
            if (t == "impassable") return "impassable_edge";
            var d = DoorAt(a, b);
            if (d != null && d.Blocks) return d.Locked ? "locked_door" : "closed_door";
            return null;
        }

        public void OpenDoor(Cell a, Cell b)
        {
            var d = DoorAt(a, b);
            if (d == null || d.Locked) throw new InvalidOperationException("cannot open locked/absent door");
            d.IsOpen = true;
        }

        public string HazardAt(Cell c)
        {
            foreach (var hz in Hazards)
                if (Json.AsCells(hz["cells"]).Contains(c)) return Json.AsStr(hz["id"]);
            return null;
        }

        // ---- LOS segment builders (shared with Los.cs) ----
        public Geometry.Segment Seg(Cell a, Cell b)
        {
            // shared border of edge-adjacent cells (verbatim board.py._seg)
            if (a.Y == b.Y)
            {
                int x = Math.Max(a.X, b.X);
                return new Geometry.Segment(
                    new Geometry.Point(x, a.Y), new Geometry.Point(x, a.Y + 1));
            }
            if (a.X == b.X)
            {
                int y = Math.Max(a.Y, b.Y);
                return new Geometry.Segment(
                    new Geometry.Point(a.X, y), new Geometry.Point(a.X + 1, y));
            }
            throw new ArgumentException("not edge-adjacent");
        }

        public List<Geometry.Segment> WallSegments()
        {
            var segs = new List<Geometry.Segment>();
            foreach (var kv in Edges)
                if (kv.Value == "wall" || kv.Value == "blocking")
                    segs.Add(Seg(kv.Key.Item1, kv.Key.Item2));
            foreach (var d in Doors.Values)
                if (d.Blocks) segs.Add(Seg(d.A, d.B));
            return segs;
        }

        public List<Geometry.Segment> OpaqueSegments()
        {
            var segs = WallSegments();
            foreach (var c in FlaggedCells("blocking"))
            {
                var corners = new Geometry.Point[]
                {
                    new Geometry.Point(c.X, c.Y), new Geometry.Point(c.X + 1, c.Y),
                    new Geometry.Point(c.X + 1, c.Y + 1), new Geometry.Point(c.X, c.Y + 1),
                };
                for (int i = 0; i < 4; i++) segs.Add(new Geometry.Segment(corners[i], corners[(i + 1) % 4]));
            }
            return segs;
        }

        public HashSet<Cell> BlockingCells() => FlaggedCells("blocking");

        public bool DpIsIndoor(DeploymentPoint dp)
        {
            foreach (var c in dp.Cells) if (IsInterior(c)) return true;
            return false;
        }

        public bool DpAllowsCard(DeploymentPoint dp, DeploymentCard card)
        {
            if (card.IsMassive) return dp.MassiveAllowed && !DpIsIndoor(dp);
            return true;
        }

        // ------------------------------------------------------------------
        public static Board FromJson(Dictionary<string, object> obj)
        {
            if (Json.AsStr(Json.Get(obj, "schema", "")) != Schema)
                throw new ArgumentException("board fixture must declare schema 'tacai.board/1'");
            var board = new Board(Json.AsStr(obj["name"]), Json.AsInt(obj["width"]), Json.AsInt(obj["height"]));

            foreach (var kv in Json.AsDict(Json.Get(obj, "cells", new Dictionary<string, object>())))
            {
                var parts = kv.Key.Split(',');
                var c = Check(board, new Cell(int.Parse(parts[0]), int.Parse(parts[1])), "cells");
                foreach (var fv in Json.AsDict(kv.Value))
                    if (Array.IndexOf(CellFlagNames, fv.Key) < 0)
                        throw new ArgumentException("unknown cell flag " + fv.Key);
                    else if (Json.AsBool(fv.Value)) board.SetFlag(c, fv.Key);
            }

            var flagsObj = Json.AsDict(Json.Get(obj, "flags", new Dictionary<string, object>()));
            foreach (var fname in CellFlagNames)
                foreach (var v in Json.AsList(Json.Get(flagsObj, fname, new List<object>())))
                    board.SetFlag(Check(board, Json.AsCell(v), "flags." + fname), fname);

            var edgesObj = Json.AsDict(Json.Get(obj, "edges", new Dictionary<string, object>()));
            foreach (var etype in EdgeTypeNames)
                foreach (var pair in Json.AsList(Json.Get(edgesObj, etype, new List<object>())))
                {
                    var pl = Json.AsList(pair);
                    board.AddEdge(Check(board, Json.AsCell(pl[0]), "edges"),
                                  Check(board, Json.AsCell(pl[1]), "edges"), etype);
                }

            foreach (var d in Json.AsList(Json.Get(obj, "doors", new List<object>())))
            {
                var dd = Json.AsDict(d);
                var cells = Json.AsCells(dd["cells"]);
                board.AddDoor(Check(board, cells[0], "doors"), Check(board, cells[1], "doors"),
                              Json.AsBool(Json.Get(dd, "locked", false)),
                              Json.AsBool(Json.Get(dd, "initially_open", false)));
            }

            foreach (var hz in Json.AsList(Json.Get(obj, "hazards", new List<object>())))
            {
                var h = Json.AsDict(hz);
                var kind = Json.AsStr(h["kind"]);
                if (kind != "overwatch" && kind != "trap" && kind != "mine")
                    throw new ArgumentException("unknown hazard kind " + kind);
                var cells = new List<Cell>();
                foreach (var c in Json.AsList(h["cells"])) cells.Add(Check(board, Json.AsCell(c), "hazards"));
                var entry = new Dictionary<string, object>
                {
                    { "id", Json.AsStr(h["id"]) }, { "kind", kind }, { "cells", cells },
                    { "damage", Json.AsInt(Json.Get(h, "damage", 1L)) },
                };
                board.Hazards.Add(entry);
            }

            foreach (var dp in Json.AsList(Json.Get(obj, "deployment_points", new List<object>())))
            {
                var d = Json.AsDict(dp);
                var dpObj = new DeploymentPoint
                {
                    Id = Json.AsStr(d["id"]),
                    MassiveAllowed = Json.AsBool(Json.Get(d, "massive_allowed", false)),
                    Description = Json.AsStr(Json.Get(d, "description", "")),
                };
                foreach (var c in Json.AsList(d["cells"])) dpObj.Cells.Add(Check(board, Json.AsCell(c), "deployment_points"));
                if (dpObj.Cells.Count == 0) throw new ArgumentException("deployment point has no cells: " + dpObj.Id);
                var colors = new List<string>();
                foreach (var col in Json.AsList(Json.Get(d, "colors", null) ?? (object)new List<object>()))
                    colors.Add(Json.AsStr(col));
                if (colors.Count > 0) dpObj.Colors = colors.ToArray();
                board.DeploymentPoints.Add(dpObj);
            }

            var objRoles = new HashSet<string> { "terminal", "item", "escort", "escape", "guard_zone" };
            foreach (var ob in Json.AsList(Json.Get(obj, "objectives", new List<object>())))
            {
                var o = Json.AsDict(ob);
                var role = Json.AsStr(o["role"]);
                if (!objRoles.Contains(role)) throw new ArgumentException("unknown objective role " + role);
                var def = new ObjectiveDef
                {
                    Id = Json.AsStr(o["id"]), Role = role,
                    Weight = Json.AsDouble(Json.Get(o, "weight", 1.0)),
                };
                foreach (var c in Json.AsList(o["cells"])) def.Cells.Add(Check(board, Json.AsCell(c), "objectives"));
                board.Objectives.Add(def);
            }

            var rl = Json.Get(obj, "roundLimit");
            if (rl != null)
            {
                board.RoundLimit = Json.AsInt(rl);
                if (board.RoundLimit < 1) throw new ArgumentException("roundLimit must be >= 1");
            }
            var trigEvents = new HashSet<string> { "entity_death", "item_taken", "terminal_activated", "exit_reached", "objective_reached" };
            var seenTrig = new HashSet<string>();
            foreach (var t in Json.AsList(Json.Get(obj, "triggers", new List<object>())))
            {
                var td = Json.AsDict(t);
                var ev = Json.AsStr(Json.Get(td, "event", ""));
                var ent = Json.AsStr(Json.Get(td, "entity", ""));
                var eff = Json.AsStr(Json.Get(td, "effect", ""));
                if (!trigEvents.Contains(ev)) throw new ArgumentException("unknown trigger event " + ev);
                if (eff != "defeat") throw new ArgumentException("unknown trigger effect " + eff);
                if (ent.Length == 0) throw new ArgumentException("trigger needs an entity id");
                if (!seenTrig.Add(ev + "|" + ent)) throw new ArgumentException("duplicate trigger " + ev + "/" + ent);
                board.Triggers.Add(new Dictionary<string, string> { { "event", ev }, { "entity", ent }, { "effect", eff } });
            }
            var seenNpc = new HashSet<string>();
            foreach (var e in Json.AsList(Json.Get(obj, "protectedEntities", new List<object>())))
            {
                var ed = Json.AsDict(e);
                var eid = Json.AsStr(ed["id"]);
                if (!seenNpc.Add(eid)) throw new ArgumentException("duplicate protected entity " + eid);
                var cells = new List<Cell>();
                foreach (var c in Json.AsList(Json.Get(ed, "cells", new List<object>())))
                    cells.Add(Check(board, Json.AsCell(c), "protectedEntities." + eid));
                if (cells.Count == 0) throw new ArgumentException("protected entity " + eid + " has no cells");
                int vit = Json.AsInt(Json.Get(ed, "vitality", 1L));
                if (vit < 1) throw new ArgumentException("protected entity " + eid + ": vitality must be >= 1");
                board.ProtectedEntities.Add(new Dictionary<string, object>
                { { "id", eid }, { "cells", cells }, { "vitality", vit } });
            }
            var po = Json.Get(obj, "postureOverride");
            if (po != null)
            {
                var s = Json.AsStr(po);
                if (Array.IndexOf(PostureNames, s) < 0) throw new ArgumentException("bad postureOverride " + s);
                board.PostureOverride = s;
            }
            var vc = Json.Get(obj, "victoryCounter");
            if (vc != null)
            {
                var s = Json.AsStr(vc);
                if (Array.IndexOf(CounterTypes, s) < 0) throw new ArgumentException("bad victoryCounter " + s);
                board.VictoryCounterName = s;
            }
            board.CampaignMetaBonus = Json.AsBool(Json.Get(obj, "campaignMetaBonus", false));

            foreach (var dp in board.DeploymentPoints)
                foreach (var c in dp.Cells)
                    if (board.IsImpassableCell(c))
                        throw new ArgumentException("DP " + dp.Id + ": impassable cell cannot deploy");
            foreach (var e in board.ProtectedEntities)
                foreach (var c in (List<Cell>)e["cells"])
                {
                    if (!board.InBounds(c)) throw new ArgumentException("protected entity out of bounds");
                    if (board.IsImpassableCell(c) || board.IsInterior(c))
                        throw new ArgumentException("protected entity: cell not standable");
                }
            return board;
        }

        static Cell Check(Board b, Cell c, string what)
        {
            if (!b.InBounds(c)) throw new ArgumentException(what + " cell " + c + " out of bounds");
            return c;
        }
    }
}
