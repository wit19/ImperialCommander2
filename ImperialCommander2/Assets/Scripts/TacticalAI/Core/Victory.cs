// Port of sim/victory.py — M2 victory model + posture state machine
// (concept section 3.1) + M2b.3 VictoryCounter target values. German
// posture-ladder + switch strings are BYTE-EXACT (App hand-off).
// Golden vectors: Assets/Tests/Fixtures/tacai/victory.json.
//
// Kept sim-only (documented deviations, see task notes):
//   * scenario harness: load_scenarios/_build_scenario_world/run_scenario/
//     _scenario_hash — the C# test harness builds its own World fixtures.
//   * posture_motion_order/posture_plan/validate_posture_plan — ActionBuilder
//     (ActivationPlan/PlannedAction) overlay layer, not in this contract.
//   * world.stats_of / world.hero_goals / world.hero_spawns seams exist only
//     on the Python MissionWorld; the frozen C# World has none, so the
//     printed-card branches — the ones the golden vectors pin — are the ones
//     implemented.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TacticalAI.Core
{
    // ------------------------------------------------------------------ public helpers
    public static class VictoryHelpers
    {
        /// Wounded = vitality below printed health (RefGuide WOUNDED).
        public static bool IsWounded(Figure fig) => fig.Vitality < fig.Card.Health;

        static readonly Regex KwFlatRe = new Regex(@"\+(\d+)\s*\{H\}");

        /// Printed dice damage EV of the target's attack (surge included at
        /// printed chance). Dice.PrintedEv is the exact Rational over the
        /// printed attack dice; the golden vectors agree with Python's
        /// per-die float sum on every pinned card.
        public static double PrintedDamageEv(Figure fig) => Dice.PrintedEv(fig.Card).ToDouble();

        /// Attack EV with printed dice + printed surge-flat keyword damage.
        /// Python tries world.stats_of FIRST (MissionWorld seam); the plain
        /// runner.World used by the vectors KeyErrors there, so the printed
        /// path below is the one the vectors exercise.
        public static double PrintedAttackEv(World world, Figure fig)
        {
            var kw = string.Join(" ", fig.Card.Keywords);
            var m = KwFlatRe.Match(kw);
            // Python: m = re.search(...); value = int(m.group(1)) if m else 0.
            // A .NET Match is never null - the no-match case is !Success
            // (Groups[1] is then the empty string, which must NOT be parsed).
            double flat = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0.0;
            return PrintedDamageEv(fig) + flat;
        }

        /// Printed support keywords (heal/support count). Python also appends
        /// card.abilities; DeploymentCard (Python AND frozen C#) has no
        /// abilities field, so keywords are the whole payload.
        public static int HealContribution(Figure fig)
        {
            var text = string.Join(" ", fig.Card.Keywords).ToLowerInvariant();
            return Regex.Matches(text, "heal").Count + Regex.Matches(text, "support").Count;
        }

        /// True when the figure can reach an entity cell within speed+1 MP.
        public static bool Threatens(World world, Figure fig, IEnumerable<Cell> cells)
        {
            return TerrainCost(world, fig.Cells(), cells) <= fig.Card.Speed + 1;
        }

        /// Targets that could TRIGGER or BLOCK a rebel victory this round:
        /// object touchers, rebels at/vor dem open exit (terrain question,
        /// never a hidden position log).
        public static bool VictoryBearer(World world, Figure target)
        {
            var rt = world.Objectives;
            if (rt is null || target is null || !target.Alive || target.Team != "rebel")
                return false;
            var cells = new HashSet<Cell>(target.Cells());
            foreach (var o in rt.OpenObjectives())               // Objekt-Berührer
            {
                foreach (var c in o.Cells)
                    if (cells.Contains(c)) return true;
            }
            var esc = new List<ObjectiveState>(rt.OpenEscape());
            if (esc.Count > 0)
            {
                var from = new List<Cell>(cells);
                foreach (var o in esc)
                {
                    foreach (var c in o.Cells)
                        if (cells.Contains(c)) return true;      // at the exit
                    foreach (var c in o.Cells)
                        if (TerrainCost(world, from, new List<Cell> { c }) <= 1)
                            return true;                          // vor dem Exit (one step)
                }
                // Python additionally consults world.hero_goals (scripted
                // MissionWorld seam) — not on the frozen World.
            }
            return false;
        }

        // ------------------------------------------------- terrain cost field
        /// Cost a 1x1 figure pays a->b ignoring figures; null = impassable.
        /// (Python objectives._terrain_step: doors block while closed,
        /// diagonals need all shared cardinal edges open.)
        static int? TerrainStep(Board board, Cell a, Cell b)
        {
            int dx = b.X - a.X, dy = b.Y - a.Y;
            if (Math.Abs(dx) + Math.Abs(dy) == 1)
            {
                if (board.BlocksMovement(a, b) is object) return null;
            }
            else if (Math.Abs(dx) == 1 && Math.Abs(dy) == 1)
            {
                if (board.BlocksMovement(a, new Cell(a.X, b.Y)) is object
                    || board.BlocksMovement(a, new Cell(b.X, a.Y)) is object
                    || board.BlocksMovement(new Cell(b.X, a.Y), b) is object
                    || board.BlocksMovement(new Cell(a.X, b.Y), b) is object)
                    return null;
            }
            else return null;
            if (board.IsBlockingCell(b) || board.IsImpassableCell(b)) return null;
            int cost = 1;
            if (board.IsDifficult(b)) cost += 1;
            return cost;
        }

        /// Multi-source Dijkstra (Python heapq on (dist, cell) — the frozen
        /// heap comparison below reproduces that tie order exactly).
        public static Dictionary<Cell, int> TerrainCostField(Board board, IEnumerable<Cell> goalCells)
        {
            int INF = World.Inf;
            var dist = new Dictionary<Cell, int>();
            var pq = new MinHeap<(int, Cell)>();
            foreach (var g in goalCells)
            {
                if (!dist.ContainsKey(g))
                {
                    dist[g] = 0;
                    pq.Push((0, g));
                }
            }
            while (pq.Count > 0)
            {
                var (d, cur) = pq.Pop();
                if (d > (dist.TryGetValue(cur, out var dv) ? dv : INF)) continue;
                foreach (var dv2 in DirThenDiag)
                {
                    var nxt = new Cell(cur.X + dv2.X, cur.Y + dv2.Y);
                    if (!board.InBounds(nxt)) continue;
                    int? step = TerrainStep(board, cur, nxt);
                    if (step is null) continue;
                    int? fstep = TerrainStep(board, nxt, cur);
                    if (fstep is null) continue;
                    int nd = d + fstep.Value;
                    if (nd < (dist.TryGetValue(nxt, out var od) ? od : INF))
                    {
                        dist[nxt] = nd;
                        pq.Push((nd, nxt));
                    }
                }
            }
            return dist;
        }

        static readonly Cell[] DirThenDiag = BuildDirThenDiag();
        static Cell[] BuildDirThenDiag()
        {
            var a = new Cell[8];
            Array.Copy(Geometry.DirVectors, 0, a, 0, 4);
            Array.Copy(Geometry.DiagVectors, 0, a, 4, 4);
            return a;
        }

        /// Terrain-only cost from a cell set to the nearest goal cell.
        public static double TerrainCost(World world, IEnumerable<Cell> fromCells, IEnumerable<Cell> toCells)
        {
            var cells = new HashSet<Cell>(fromCells);
            var goal = new List<Cell>(toCells);
            foreach (var c in cells)
                if (goal.Contains(c)) return 0;
            var field = TerrainCostField(world.Board, goal);
            int best = World.Inf;
            foreach (var c in cells)
                if (field.TryGetValue(c, out var v) && v < best) best = v;
            return best;
        }

        public static int ArrivalRounds(World world, Figure fig, IEnumerable<Cell> cells, int left = 0)
        {
            double cd = TerrainCost(world, fig.Cells(), cells);
            int cost = (int)cd;
            if (cost >= World.Inf) return World.Inf;
            int num = cost + Math.Max(0, left);
            int speed = fig.Card.Speed;
            return (num + speed - 1) / speed;   // == ceil(num/speed), num>=0
        }

        /// (id, cells, left) race set: progressible objectives with
        /// interactions left, plus the escape objective when the gate is open
        /// (left = 0). Python pulls the escape entries via rt.by_role
        /// ("escape"); with escape_open() non-empty that set is exactly
        /// OpenEscape().
        public static List<(string Id, List<Cell> Cells, int Left)> OpenTimerObjectives(World world)
        {
            var rt = world.Objectives;
            var outl = new List<(string, List<Cell>, int)>();
            if (rt is null) return outl;
            foreach (var o in rt.OpenObjectives())
            {
                int needed = o.Role switch
                {
                    "terminal" => 2,
                    "item" => 1,
                    "escape" => 0,
                    "guard_zone" => 0,
                    _ => 2,
                };
                outl.Add((o.Id, new List<Cell>(o.Cells), needed - o.Progress));
            }
            if (rt.OpenEscape().Count > 0)
                foreach (var o in rt.OpenEscape())
                    outl.Add((o.Id, new List<Cell>(o.Cells), 0));
            return outl;
        }

        public static List<Figure> AliveRebels(World world) => world.AliveOf("rebel");

        /// Cells of a board.protected_entities record.
        public static List<Cell> EntityCells(Dictionary<string, object> e)
        {
            if (!e.TryGetValue("cells", out var v) || v is null) return new List<Cell>();
            if (v is List<Cell> lc) return lc;
            var outl = new List<Cell>();
            foreach (var c in Json.AsList(v)) outl.Add(Json.AsCell(c));
            return outl;
        }

        internal static HashSet<Cell> Occupancy(World world)
        {
            var occ = new HashSet<Cell>();
            foreach (var f in world.Figures)
                if (f.Alive) foreach (var c in f.Cells()) occ.Add(c);
            return occ;
        }
    }

    /// Min-heap over tuples with IComparable ordering — mirrors Python
    /// heapq on (int, (x, y)) so Dijkstra visit order matches 1:1.
    internal sealed class MinHeap<T> where T : IComparable<T>
    {
        readonly List<T> _h = new List<T>();
        public int Count => _h.Count;
        public void Push(T item) { _h.Add(item); int i = _h.Count - 1; while (i > 0) { int p = (i - 1) / 2; if (_h[i].CompareTo(_h[p]) < 0) { (_h[i], _h[p]) = (_h[p], _h[i]); i = p; } else break; } }
        public T Pop()
        {
            var top = _h[0];
            _h[0] = _h[_h.Count - 1];
            _h.RemoveAt(_h.Count - 1);
            if (_h.Count > 0) { int i = 0, n = _h.Count; while (true) { int l = 2 * i + 1, r = l + 1, s = i; if (l < n && _h[l].CompareTo(_h[s]) < 0) s = l; if (r < n && _h[r].CompareTo(_h[s]) < 0) s = r; if (s == i) break; (_h[i], _h[s]) = (_h[s], _h[i]); i = s; } }
            return top;
        }
    }

    // ---------------------------------------------------------------- counter
    public sealed class VictoryCounter
    {
        public string CounterType;
        public bool MetaBonus;

        static readonly HashSet<string> Valid = new HashSet<string>(Board.CounterTypes);

        public VictoryCounter(string counterType, bool metaBonus = false)
        {
            if (!Valid.Contains(counterType))
                throw new ArgumentException("unknown victory_counter '" + counterType + "'");
            CounterType = counterType;
            MetaBonus = metaBonus;
        }

        /// board.victory_counter > surviveRounds (round budget/stall) >
        /// incapacitateAll (legacy default).
        public static VictoryCounter FromBoard(Board board)
        {
            string ct;
            if (!string.IsNullOrEmpty(board.VictoryCounterName)) ct = board.VictoryCounterName;
            else if (board.RoundLimit != null || board.PostureOverride == "stall") ct = "surviveRounds";
            else ct = "incapacitateAll";
            return new VictoryCounter(ct, board.CampaignMetaBonus);
        }

        public Dictionary<string, object> Progress(World world)
        {
            int healthy = 0, wounded = 0;
            var rebels = new List<Figure>();
            foreach (var f in world.Figures)
            {
                if (f.Team != "rebel" || f.Card.IsMassive) continue;
                rebels.Add(f);
                if (!f.Alive) continue;
                if (f.Vitality >= f.Card.Health) healthy++;
                else wounded++;
            }
            return new Dictionary<string, object>
            {
                { "counter", CounterType },
                { "healthy", healthy },
                { "wounded", wounded },
                { "incapacitated", rebels.Count - healthy - wounded },
            };
        }

        public Dictionary<string, object> PublicFields()
        {
            return new Dictionary<string, object>
            {
                { "victoryCounter", CounterType },
                { "campaignMetaBonus", MetaBonus },
            };
        }

        public Dictionary<string, object> AsDict() => PublicFields();

        /// ΔZaehler per attack — the W tuple of the counter. Python tuple
        /// entries surface as List<object>: boxed ints + the damage component
        /// as Python float(round(ev, 6)) == Math.Round(x, 6, ToEven).
        public List<object> TargetValue(World world, Figure attacker, Figure target)
        {
            int bearer = VictoryHelpers.VictoryBearer(world, target) ? 1 : 0;
            int heal = VictoryHelpers.HealContribution(target);
            double dmg = Math.Round(VictoryHelpers.PrintedAttackEv(world, target), 6, MidpointRounding.ToEven);
            // §3.3.1a profil-Anteil: wird hinter die fuehrenden
            /// Counter-Semantiken eingefuegt, NIE davor (leer ohne Profil).
            var pk = HeroProfiles.ProfileFocusKey(world, target, "eliminate", CounterType);
            switch (CounterType)
            {
                case "incapacitateAll":
                case "capture":
                    {
                        bool killable = target.Vitality <= VictoryHelpers.PrintedDamageEv(attacker);
                        int meta = (MetaBonus && killable && VictoryHelpers.IsWounded(target)) ? 1 : 0;
                        return new List<object>
                        {
                            bearer,
                            killable ? 1 : 0,
                            killable ? -target.Vitality : 0,
                            meta,
                        }.Concat(pk).Concat(new List<object> { heal, dmg }).ToList();
                    }
                case "woundAll":
                    {
                        // woundAll law: healthy BEATS wounded — every hit
                        // counts and counter progress is irreversible. The
                        // §3.3.1a chip value (non-healer/support > healer,
                        // then mobility*rangeMax, then ev) sits BELOW
                        // healthy-first — never overrides it.
                        int healthy = VictoryHelpers.IsWounded(target) ? 0 : 1;
                        return new List<object>
                        {
                            bearer,
                            healthy,
                        }.Concat(pk).Concat(new List<object> { heal, dmg, -target.Vitality }).ToList();
                    }
                default:   // surviveRounds: touch-and-run
                    return new List<object> { bearer, 0 }
                        .Concat(pk)
                        .Concat(new List<object> { heal, dmg }).ToList();
            }
        }
    }

    // ---------------------------------------------------------------- posture
    public sealed class PostureSwitch
    {
        public int RoundNo;
        public string Src;
        public string Dst;
        public string Reason;

        public PostureSwitch(int roundNo, string src, string dst, string reason)
        {
            RoundNo = roundNo; Src = src; Dst = dst; Reason = reason;
        }

        /// Public log line (App hand-off, German, byte-exact; dst capitalized).
        public string Line => "Missionslage: " + Reason + " -> Posture " + Cap(Dst);

        static string Cap(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "round", RoundNo },
                { "from", Src },
                { "to", Dst },
                { "reason", Reason },
            };
        }
    }

    public sealed class MissionVictoryModel
    {
        public int? RoundLimit;
        public List<string> ProtectIds;
        public List<string> DenyIds;
        public string Override;         // board.posture_override or custom JSON
        public string Posture;          // current posture
        public int RoundNo;             // set at round evaluation
        public List<PostureSwitch> Switches = new List<PostureSwitch>();  // real changes only
        public VictoryCounter Counter;  // null = pure GH/eliminate cascade

        bool _baselineLogged;

        static readonly HashSet<string> PostureNames = new HashSet<string>(Board.PostureNames);
        static readonly string[] ProtectEvent = { "entity_death" };
        static readonly HashSet<string> DenyEvents = new HashSet<string>
            { "item_taken", "terminal_activated", "exit_reached", "objective_reached" };

        public MissionVictoryModel(int? roundLimit = null, IEnumerable<string> protectIds = null,
                                   IEnumerable<string> denyIds = null, string @override = null)
        {
            if (@override != null && !PostureNames.Contains(@override))
                throw new ArgumentException("unknown posture override '" + @override + "'");
            RoundLimit = roundLimit;
            ProtectIds = protectIds is null ? new List<string>() : new List<string>(protectIds);
            DenyIds = denyIds is null ? new List<string>() : new List<string>(denyIds);
            Override = @override;
            Posture = "eliminate";   // until the first round evaluation
            RoundNo = 1;
        }

        /// board.round_limit + triggers (entity_death -> protect; item_taken /
        /// terminal_activated / exit_reached / objective_reached -> deny).
        public static MissionVictoryModel FromBoard(Board board)
        {
            var protect = new List<string>();
            var deny = new List<string>();
            foreach (var t in board.Triggers)
            {
                string ev = t["event"];   // KeyError parity: direct index
                if (ev == "entity_death") protect.Add(t["entity"]);
                else if (DenyEvents.Contains(ev)) deny.Add(t["entity"]);
            }
            var m = new MissionVictoryModel(board.RoundLimit, protect, deny, board.PostureOverride);
            if (!string.IsNullOrEmpty(board.VictoryCounterName))
                m.Counter = VictoryCounter.FromBoard(board);
            return m;
        }

        public Dictionary<string, object> PublicFields()
        {
            return new Dictionary<string, object>
            {
                { "roundLimit", RoundLimit },
                { "protectTriggers", new List<string>(ProtectIds) },
                { "denyTriggers", new List<string>(DenyIds) },
                { "postureOverride", Override },
            };
        }

        public Dictionary<string, object> AsDict() => PublicFields();

        /// Derived from the trigger set, never trusted from the import.
        public string DeriveMissionClass()
        {
            if (!string.IsNullOrEmpty(Override)) return Override;
            if (ProtectIds.Count > 0) return "protect";
            if (RoundLimit != null) return "stall";
            if (DenyIds.Count > 0) return "deny";
            return "eliminate";
        }

        public int RoundsRemaining() =>
            RoundLimit is { } rl ? Math.Max(0, rl - RoundNo + 1) : World.Inf;

        /// Stall-relevant while at least one rebel can still reach an open
        /// terminal/item/escape before the round budget runs out.
        public bool TimerRaceLive(World world)
        {
            int rem = RoundsRemaining();
            if (rem >= World.Inf) return true;
            foreach (var f in VictoryHelpers.AliveRebels(world))
                foreach (var (id, cs, left) in VictoryHelpers.OpenTimerObjectives(world))
                    if (VictoryHelpers.ArrivalRounds(world, f, cs, left) <= rem) return true;
            return false;
        }

        List<Figure> ProtectedEntities(World world) =>
            world.AliveOf("npc").FindAll(f => ProtectIds.Contains(f.Id));

        public List<Figure> ThreatenedEntities(World world)
        {
            var outp = new List<Figure>();
            foreach (var e in ProtectedEntities(world))
            {
                var cells = EntityCellsOf(world, e);
                foreach (var r in VictoryHelpers.AliveRebels(world))
                    if (VictoryHelpers.Threatens(world, r, cells)) { outp.Add(e); break; }
            }
            return outp;
        }

        static List<Cell> EntityCellsOf(World world, Figure e) => e.Cells();

        public List<(string Id, List<Cell> Cells, int Left)> DenyObjectives(World world)
        {
            var rt = world.Objectives;
            var outp = new List<(string, List<Cell>, int)>();
            if (rt is null) return outp;
            foreach (var oid in DenyIds)
            {
                var o = rt.ById(oid);
                if (o is { Done: false })
                {
                    int needed = o.Role switch
                    {
                        "terminal" => 2,
                        "item" => 1,
                        "escape" => 0,
                        "guard_zone" => 0,
                        _ => 2,
                    };
                    outp.Add((oid, new List<Cell>(o.Cells), needed - o.Progress));
                }
            }
            return outp;
        }

        /// Ladder per concept 3.1 + timer race (Rundenlimit, M2b.2).
        public (string Posture, string Reason) ComputePosture(int roundNo, World world)
        {
            RoundNo = roundNo;
            if (Override is { } ov) return (ov, "Custom-Override (" + ov + ")");
            var threatened = ThreatenedEntities(world);
            if (threatened.Count > 0)
            {
                var names = new List<string>();
                foreach (var f in threatened) names.Add(f.Id);
                names.Sort(StringComparer.Ordinal);
                return ("protect", "Schutzobjekt " + string.Join(", ", names) + " bedroht");
            }
            if (RoundLimit != null)
            {
                int rem = RoundsRemaining();
                if (rem <= 0) return ("eliminate", "Rundenlimit erreicht");
                if (!TimerRaceLive(world))
                    return ("eliminate", "Timer-Rennen verloren (Rest " + rem + "/" + RoundLimit + ")");
                return ("stall", "Timer-Rennen live (Rest " + rem + "/" + RoundLimit + ")");
            }
            if (DenyObjectives(world).Count > 0) return ("deny", "Verwehrziel noch umkämpft");
            if (DenyIds.Count > 0) return ("eliminate", "Verwehrziel aufgenommen");
            return ("eliminate", "keine Timer-/Schutz-/Verwehrbedingung live");
        }

        /// Posture switches that fired at this round boundary. The baseline
        /// line is returned but NOT kept in .Switches.
        public List<PostureSwitch> Evaluate(int roundNo, World world)
        {
            var (newPosture, reason) = ComputePosture(roundNo, world);
            if (!_baselineLogged)
            {
                _baselineLogged = true;
                Posture = newPosture;
                // baseline src stamp is the literal "-" (public line format)
                return new List<PostureSwitch>
                {
                    new PostureSwitch(roundNo, "-", newPosture, "Ausgangslage: " + reason),
                };
            }
            if (newPosture == Posture) return new List<PostureSwitch>();
            Switches.Add(new PostureSwitch(roundNo, Posture, newPosture, reason));
            Posture = newPosture;
            return Switches.GetRange(Switches.Count - 1, 1);
        }

        List<(string Id, List<Cell> Cells, int Left)> RaceObjectives(World world) =>
            Posture == "deny" ? DenyObjectives(world) : VictoryHelpers.OpenTimerObjectives(world);

        /// Per-(attacker,target) value tuple W — HIGHER = better; the focus
        /// sorts by -W first, then the GH cascade. Python ints + the rounded
        /// damage float surface as List<object>.
        public List<object> PostureValue(World world, Figure attacker, Figure target)
        {
            int INF = World.Inf;
            switch (Posture)
            {
                case "eliminate":
                    {
                        if (Counter is { } c) return c.TargetValue(world, attacker, target);
                        int killable = target.Vitality <= VictoryHelpers.PrintedDamageEv(attacker) ? 1 : 0;
                        double dmg = Math.Round(VictoryHelpers.PrintedAttackEv(world, target), 6, MidpointRounding.ToEven);
                        int heal = VictoryHelpers.HealContribution(target);
                        // attrition rule (§3.3.1a): among equally killable/
                        // wounded targets of the plain hunt, ev hoch zuerst
                        return new List<object> { killable, killable != 0 ? -target.Vitality : 0 }
                            .Concat(HeroProfiles.ProfileFocusKey(world, target, "eliminate", null))
                            .Concat(new List<object> { heal, dmg }).ToList();
                    }
                case "stall":
                    {
                        int rem = RoundsRemaining();
                        int bestA = INF, bestC = INF;
                        foreach (var (id, cs, left) in VictoryHelpers.OpenTimerObjectives(world))
                        {
                            bestA = Math.Min(bestA, VictoryHelpers.ArrivalRounds(world, target, cs, left));
                            bestC = Math.Min(bestC, (int)VictoryHelpers.TerrainCost(world, target.Cells(), cs));
                        }
                        // §3.3.1a protect/stall: Laeuferwert mobility x
                        // rangeMax gewichtet ZWISCHEN Ankunfts- und Terrainterm
                        return new List<object>
                        {
                            bestA <= rem ? 1 : 0,
                            -Math.Min(bestA, 1000000),
                        }
                            .Concat(HeroProfiles.ProfileFocusKey(world, target, "stall", null))
                            .Concat(new List<object> { -Math.Min(bestC, 1000000) }).ToList();
                    }
                case "protect":
                    {
                        int menaced = 0, bestA = INF, bestC = INF;
                        foreach (var e in ProtectedEntities(world))
                        {
                            var cells = e.Cells();
                            if (VictoryHelpers.Threatens(world, target, cells)) menaced = 1;
                            int c = (int)VictoryHelpers.TerrainCost(world, target.Cells(), cells);
                            bestC = Math.Min(bestC, c);
                            if (c < INF) bestA = Math.Min(bestA, CeilDiv(c, Math.Max(1, target.Card.Speed)));
                        }
                        // §3.3.1a: runner value between arrival and terrain
                        return new List<object>
                        {
                            menaced,
                            -Math.Min(bestA, 1000000),
                        }
                            .Concat(HeroProfiles.ProfileFocusKey(world, target, "protect", null))
                            .Concat(new List<object>
                            {
                                -Math.Min(bestC, 1000000),
                                Math.Round(VictoryHelpers.PrintedAttackEv(world, target), 6, MidpointRounding.ToEven),
                            }).ToList();
                    }
                case "deny":
                    {
                        var node = ContestedNode(world, null, target);
                        if (node is null) return new List<object> { 0, 0, 0 };
                        int cost = (int)VictoryHelpers.TerrainCost(world, target.Cells(), new List<Cell> { node.Value });
                        int eta = cost < INF ? CeilDiv(cost, Math.Max(1, target.Card.Speed)) : INF;
                        return new List<object> { cost < INF ? 1 : 0, -Math.Min(eta, 1000000), -Math.Min(cost, 1000000) };
                    }
                default:
                    return new List<object> { 0 };
            }
        }

        static int CeilDiv(int a, int b) => (a + b - 1) / b;   // a>=0, b>=1 == math.ceil(a/b)

        /// The key node between a rebel and the contested objective that the
        /// figure can REACH STRICTLY FASTER than the rebel (body block = legal
        /// movement onto the node itself, concept 3.3.0 Deny + Stall). Pure
        /// over public state; deterministic tie-break: figure cost, then
        /// objective distance, then coordinates. fig=null asks for the race
        /// node itself (Deny W function).
        public Cell? ContestedNode(World world, Figure fig = null, Figure target = null)
        {
            int INF = World.Inf;
            var objs = RaceObjectives(world);
            if (objs.Count == 0) return null;
            var rebels = VictoryHelpers.AliveRebels(world);
            if (target != null)
                rebels = target.Alive && target.Team == "rebel"
                    ? new List<Figure> { target } : new List<Figure>();
            if (rebels.Count == 0) return null;
            var occupants = VictoryHelpers.Occupancy(world);
            (int Mc, int Fc, int X, int Y)? best = null;
            Cell? bestCell = null;
            foreach (var (oid, cells, left) in objs)
            {
                var cellSet = new HashSet<Cell>(cells);
                var fieldObj = VictoryHelpers.TerrainCostField(world.Board, cells);
                foreach (var rebel in rebels)
                {
                    var rStart = rebel.Cells();
                    int rCost = INF;
                    foreach (var c in rStart)
                        if (fieldObj.TryGetValue(c, out var v) && v < rCost) rCost = v;
                    if (rCost >= INF) continue;          // objective not contestable
                    var rField = VictoryHelpers.TerrainCostField(world.Board, rStart);
                    var fField = fig != null ? VictoryHelpers.TerrainCostField(world.Board, fig.Cells()) : null;
                    foreach (var kv in fieldObj)
                    {
                        var c = kv.Key;
                        int fc = kv.Value;
                        if (cellSet.Contains(c) || occupants.Contains(c)) continue;
                        if (world.Board.IsImpassableCell(c)) continue;
                        if (fc >= rCost) continue;       // not between rebel and objective
                        int rc = rField.TryGetValue(c, out var rv) ? rv : INF;
                        if (rc >= INF) continue;
                        int mc = 0;
                        if (fig != null)
                        {
                            mc = fField.TryGetValue(c, out var mv) ? mv : INF;
                            if (mc >= rc || mc > fig.Card.Speed) continue;
                        }
                        var key = (mc, fc, c.X, c.Y);
                        if (best is null || CompareKey(key, best.Value) < 0)
                        {
                            best = key;
                            bestCell = c;
                        }
                    }
                }
            }
            return bestCell;
        }

        static int CompareKey((int, int, int, int) a, (int, int, int, int) b)
        {
            if (a.Item1 != b.Item1) return a.Item1 < b.Item1 ? -1 : 1;
            if (a.Item2 != b.Item2) return a.Item2 < b.Item2 ? -1 : 1;
            if (a.Item3 != b.Item3) return a.Item3 < b.Item3 ? -1 : 1;
            if (a.Item4 != b.Item4) return a.Item4 < b.Item4 ? -1 : 1;
            return 0;
        }

        /// Stall/deny doctrine: attack ONLY while the target presently
        /// obstructs — it could work the race objective this round
        /// (arrival_rounds <= 1).
        public bool TargetIsImmediate(World world, Figure target)
        {
            if (target is null) return false;
            foreach (var (id, cells, left) in RaceObjectives(world))
                if (VictoryHelpers.ArrivalRounds(world, target, cells, left) <= 1) return true;
            return false;
        }
    }
}
