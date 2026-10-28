// Port of sim/action_builder.py (Profil-A 2-action budget planner) plus the
// posture overlay functions of sim/victory.py (posture_motion_order /
// posture_plan / validate_posture_plan — deferred here by Victory.cs).
//
// Fairness invariants (concept 3.3/3.4, 5.1):
//   MP budget = Speed stat; 2 actions per activation; <=1 attack action for
//   non-hero figures; specials only from the printed card, each at most once;
//   the posture layer may only REPLACE move endpoints / SUPPRESS actions —
//   never invent actions, ranges, specials, params or bonuses.
// Golden vectors: Assets/Tests/Fixtures/tacai/action_plan.json.
//
// Byte-exact mirrors of the Python strings (why/reason payloads are the
// fairness-visible output) — do not "tidy" wording.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public sealed class PlannedAction
    {
        public string Type;                 // attack | move | interact | rest | special
        public Dictionary<string, object> Params = new Dictionary<string, object>();
        public string Why = "";

        public PlannedAction(string type, Dictionary<string, object> prms = null, string why = "")
        {
            Type = type;
            if (prms != null) Params = prms;
            Why = why;
        }

        /// Python PlannedAction.as_dict(): states -> [[x, y], orient] lists.
        public Dictionary<string, object> AsDict()
        {
            var p = new Dictionary<string, object>();
            foreach (var kv in Params)
            {
                if (kv.Key == "states" && kv.Value is List<(Cell, int)> states)
                {
                    var outl = new List<object>();
                    foreach (var s in states)
                        outl.Add(new List<object> {
                            new List<object> { (long)s.Item1.X, (long)s.Item1.Y },
                            (long)s.Item2 });
                    p["states"] = outl;
                }
                else p[kv.Key] = kv.Value;
            }
            return new Dictionary<string, object> { { "type", Type }, { "params", p }, { "why", Why } };
        }
    }

    public sealed class ActivationPlan
    {
        public string FigId;
        public FocusDecision Focus;
        public int MpBudget;
        public List<PlannedAction> Actions = new List<PlannedAction>();
        public int MpPlanned;
        public string Why = "";

        public ActivationPlan(string figId, FocusDecision focus, int mpBudget)
        { FigId = figId; Focus = focus; MpBudget = mpBudget; }

        public int AttackActions
        {
            get { int n = 0; foreach (var a in Actions) if (a.Type == "attack") n++; return n; }
        }

        public List<string> Specials
        {
            get
            {
                var outl = new List<string>();
                foreach (var a in Actions)
                    if (a.Type == "special")
                        outl.Add(a.Params.TryGetValue("special", out var s) ? (string)s : "");
                return outl;
            }
        }

        public Dictionary<string, object> AsDict()
        {
            var acts = new List<object>();
            foreach (var a in Actions) acts.Add(a.AsDict());
            return new Dictionary<string, object>
            {
                { "fig", FigId },
                { "mp_budget", (long)MpBudget },
                { "mp_planned", (long)MpPlanned },
                { "focus", Focus != null ? Focus.AsDict() : null },
                { "actions", acts },
                { "why", Why },
            };
        }
    }

    public static class ActionBuilder
    {
        // Keywords granting an ACTIVE special action (mirrors
        // ACTIVE_SPECIAL_KEYWORDS; passive modifiers are inert for the planner).
        public static readonly HashSet<string> ActiveSpecialKeywords =
            new HashSet<string> { "Overwatch", "Multifire", "Push 1", "Move 2" };

        public static List<string> SpecialsFor(DeploymentCard card)
        {
            var hit = new List<string>();
            foreach (var k in card.Keywords)
                if (ActiveSpecialKeywords.Contains(k) && !hit.Contains(k)) hit.Add(k);
            hit.Sort(StringComparer.Ordinal);
            return hit;
        }

        // ------------------------------------------------------------------ helpers

        /// Execution-time attack legality (LOS with figures, at plan-time view).
        static bool ExecOk(World world, Figure fig, Figure target, IEnumerable<Cell> cells)
        {
            var occupied = new HashSet<Cell>();
            foreach (var f in world.Figures)
            {
                if (!f.Alive || ReferenceEquals(f, fig) || ReferenceEquals(f, target)) continue;
                foreach (var c in SpaceLegality.FigureCells(f)) occupied.Add(c);
            }
            return FocusResolver.AttackGeometryOk(world, fig,
                new HashSet<Cell>(SpaceLegality.FigureCells(target)),
                new HashSet<Cell>(cells), true, occupied);
        }

        static HashSet<Cell> CellsOf(Figure fig, Cell anchor, int orient) =>
            new HashSet<Cell>(Geometry.FootprintCells(anchor, fig.Card.MiniSize, orient));

        /// Python world.hazard_cells(): untriggered hazard cells. The frozen
        /// C# World has no triggered-hazards seam (plan time == nothing
        /// triggered yet), so this equals the full hazard cell set.
        public static HashSet<Cell> HazardCells(World world)
        {
            var outp = new HashSet<Cell>();
            foreach (var hz in world.Board.Hazards)
                foreach (var c in (List<Cell>)hz["cells"]) outp.Add(c);
            return outp;
        }

        class AttackStateResult
        {
            public PathResult Res;
            public bool? Avoided;   // null = no hazards on the board
        }

        /// Shortest legal (end-legal + execution-LOS) attack state within
        /// budget. Path priority: hazard-free preferred.
        static AttackStateResult BestAttackState(World world, Figure fig, Figure target, int mp)
        {
            Func<Cell, int, bool> goal = (a, o) =>
                ExecOk(world, fig, target, CellsOf(fig, a, o));
            var hz = HazardCells(world);
            if (hz.Count > 0)
            {
                var res = world.Graph.Shortest(fig, fig.Anchor, fig.Orientation, goal,
                                               "move", mp, hz);
                if (res != null) return new AttackStateResult { Res = res, Avoided = true };
            }
            var plain = world.Graph.Shortest(fig, fig.Anchor, fig.Orientation, goal,
                                             "move", mp, null);
            return new AttackStateResult
            {
                Res = plain,
                Avoided = plain != null && hz.Count > 0 ? (bool?)false : null,
            };
        }

        static List<Figure> EngagedRebels(World world, Figure fig)
        {
            var cells = SpaceLegality.FigureCells(fig);
            var outl = new List<Figure>();
            foreach (var r in world.Figures)
                if (r.Alive && r.Team != fig.Team &&
                    FocusResolver.CountSpaces(world.Board, cells, SpaceLegality.FigureCells(r)) <= 1)
                    outl.Add(r);
            return outl;
        }

        // ------------------------------------------------- Profil-A default policy

        public static ActivationPlan PlanActivation(World world, Figure fig,
                                                    bool vitalityTiebreak = true,
                                                    bool allowDoorInteract = true)
        {
            int mp = fig.Card.Speed;
            var plan = new ActivationPlan(fig.Id, null, mp);
            var focus = FocusResolver.ComputeFocus(world, fig, vitalityTiebreak);
            plan.Focus = focus;
            if (focus == null)
            {
                plan.Why = "no living rebels: activation does nothing";
                return plan;
            }
            var target = focus.Target;

            // M11c2: printed no-move-and-attack rule (Tripod/Stativ cards,
            // curated table CardKeywords) - ACTIVE ONLY ab S4 (S1..S3 and the
            // reference path keep every branch below bit-identical). Such a
            // figure may never combine movement and attack in one activation,
            // not even via the engaged-retreat variant.
            bool noMoveAttack = CardKeywords.ActiveFor(
                world, fig.Card.Id, CardKeywords.NoMoveAndAttack);

            // -- 1. attack from current position if legal (GH: attack if able)
            if (ExecOk(world, fig, target, SpaceLegality.FigureCells(fig)))
            {
                if (!noMoveAttack && TryEngagedRetreat(world, fig, target, mp, plan, null, 0))
                    return plan;
                plan.Actions.Add(new PlannedAction("attack",
                    new Dictionary<string, object> { { "target_id", target.Id } },
                    why: "attack " + target.Id + ": in range and LOS from current position"));
                plan.Why = "attack now. focus: " + focus.Why;
                return plan;
            }

            // -- 2. move to an attack cell and attack
            var best = BestAttackState(world, fig, target, mp);
            var res = best.Res;
            bool? avoided = best.Avoided;
            if (res != null && res.Cost > 0 && !noMoveAttack)
            {
                if (fig.Card.AttackType == "Ranged" && EngagedRebels(world, fig).Count > 0)
                    if (TryEngagedRetreat(world, fig, target, mp, plan, res.States, res.Cost))
                        return plan;
                var states = new List<(Cell, int)> { (fig.Anchor, fig.Orientation) };
                states.AddRange(res.States);
                plan.Actions.Add(new PlannedAction("move",
                    new Dictionary<string, object>
                    {
                        { "states", states },
                        { "mp", (long)res.Cost },
                        { "hazards_avoided", avoided.HasValue ? (object)avoided.Value : null },
                    },
                    why: "move " + res.Cost + " MP to legal attack position against " + target.Id));
                plan.MpPlanned = res.Cost;
                plan.Actions.Add(new PlannedAction("attack",
                    new Dictionary<string, object> { { "target_id", target.Id } },
                    why: "attack " + target.Id + " after repositioning"));
                string note = avoided == true ? " (hazard fields avoided)"
                    : (avoided == false ? " (all shortest paths cross hazards - unavoidable)" : "");
                plan.Why = "move-then-attack. focus: " + focus.Why + note;
                return plan;
            }

            // -- 3. door Interact option: opening makes an attack possible
            if (allowDoorInteract)
            {
                var act = DoorInteractOption(world, fig, target, mp);
                if (act != null)
                {
                    plan.Actions.Add(act);
                    plan.Why = "open door to enable attack. focus: " + focus.Why;
                    return plan;
                }
            }

            // -- 4. unreachable: spend up to MP along the focus path
            var step = ApproachAction(world, fig, target, mp, focus);
            if (step != null)
            {
                plan.Actions.Add(step);
                plan.MpPlanned = Json.AsInt(step.Params["mp"]);
                plan.Why = noMoveAttack && res != null && res.Cost > 0
                    ? "printed no-move-and-attack rule (Stativ/Tripod): the attack would "
                      + "need leaving your space -> advance without attacking, shoot from "
                      + "standstill next activation. focus: " + focus.Why
                    : "cannot attack this activation -> advance toward focus " +
                      "(no movement beyond Speed). focus: " + focus.Why;
            }
            else
            {
                plan.Why = "no legal movement toward " + target.Id + "; stand by. " +
                           "focus: " + focus.Why;
            }
            return plan;
        }

        /// Walk the focus path (path priority active), clamped at MP budget.
        static PlannedAction ApproachAction(World world, Figure fig, Figure target,
                                            int mp, FocusDecision focus)
        {
            var targetCells = new HashSet<Cell>(SpaceLegality.FigureCells(target));
            Func<Cell, int, bool> goal = (a, o) => FocusResolver.AttackGeometryOk(
                world, fig, targetCells, CellsOf(fig, a, o), false, null);
            var hz = HazardCells(world);
            PathResult res = null;
            bool? avoided = null;
            if (hz.Count > 0)
            {
                res = world.Graph.Shortest(fig, fig.Anchor, fig.Orientation, goal,
                                           "focus", null, hz);
                if (res != null) avoided = true;
            }
            if (res == null)
            {
                res = world.Graph.Shortest(fig, fig.Anchor, fig.Orientation, goal,
                                           "focus", null, null);
                if (res != null) avoided = hz.Count > 0 ? (bool?)false : null;
            }
            if (res != null)
            {
                var states = WalkPrefix(world, fig, res, mp);
                if (states.Count > 1)
                {
                    int spent = WalkCost(world, fig, states);
                    return new PlannedAction("move",
                        new Dictionary<string, object>
                        {
                            { "states", states },
                            { "mp", (long)spent },
                            { "purpose", "approach" },
                            { "hazards_avoided", avoided.HasValue ? (object)avoided.Value : null },
                        },
                        why: "advance " + spent + " MP toward " + target.Id +
                             " along focus path (attack still out of reach)");
                }
            }
            // no attack-cell path at all (e.g. walled-off): hill-climb counting-dist
            return HillclimbAction(world, fig, target, mp);
        }

        /// Prefix of the planned state chain (move-mode legality clamped),
        /// cumulative cost <= mp.
        public static List<(Cell, int)> WalkPrefix(World world, Figure fig,
                                                   PathResult res, int mp)
        {
            var states = new List<(Cell, int)> { (fig.Anchor, fig.Orientation) };
            states.AddRange(res.States);
            var outp = new List<(Cell, int)> { states[0] };
            int spent = 0;
            for (int i = 0; i + 1 < states.Count; i++)
            {
                var cur = states[i];
                var nxt = states[i + 1];
                bool stepped = false;
                foreach (var t in world.Graph.Transitions(fig, cur.Item1, cur.Item2, "move", mp - spent))
                {
                    if (t.Anchor == nxt.Item1 && t.Orientation == nxt.Item2 && spent + t.Cost <= mp)
                    {
                        spent += t.Cost;
                        outp.Add(nxt);
                        stepped = true;
                        break;
                    }
                }
                if (!stepped) break;
            }
            return outp;
        }

        static int WalkCost(World world, Figure fig, List<(Cell, int)> states)
        {
            int spent = 0;
            for (int i = 0; i + 1 < states.Count; i++)
                foreach (var t in world.Graph.Transitions(fig, states[i].Item1, states[i].Item2, "move", null))
                    if (t.Anchor == states[i + 1].Item1 && t.Orientation == states[i + 1].Item2)
                    {
                        spent += t.Cost;
                        break;
                    }
            return spent;
        }

        /// Desperate mode: minimize counting-space distance stepwise.
        static PlannedAction HillclimbAction(World world, Figure fig, Figure target, int mp)
        {
            var targetCells = new HashSet<Cell>(SpaceLegality.FigureCells(target));
            var cur = (fig.Anchor, fig.Orientation);
            int spent = 0;
            var path = new List<(Cell, int)> { cur };
            int bestDist = FocusResolver.CountSpaces(world.Board,
                CellsOf(fig, cur.Item1, cur.Item2), targetCells);
            while (spent < mp)
            {
                bool improved = false;
                foreach (var t in world.Graph.Transitions(fig, cur.Item1, cur.Item2, "move", mp - spent))
                {
                    int d = FocusResolver.CountSpaces(world.Board,
                        CellsOf(fig, t.Anchor, t.Orientation), targetCells);
                    if (d < bestDist && spent + t.Cost <= mp)
                    {
                        cur = (t.Anchor, t.Orientation);
                        spent += t.Cost;
                        path.Add(cur);
                        bestDist = d;
                        improved = true;
                        break;
                    }
                }
                if (!improved) break;
            }
            if (path.Count > 1)
                return new PlannedAction("move",
                    new Dictionary<string, object>
                    {
                        { "states", path },
                        { "mp", (long)spent },
                        { "purpose", "approach" },
                        { "hillclimb", true },
                    },
                    why: "no path to attack cell; reduce distance " + bestDist);
            return null;
        }

        /// Ranged figures prefer to break engagement (concept 3.3). Score =
        /// (further from engagers, can still attack focus target, cheaper move).
        static bool TryEngagedRetreat(World world, Figure fig, Figure target, int mp,
                                      ActivationPlan plan, List<(Cell, int)> fallbackStates,
                                      int fallbackMp = 0)
        {
            var engagers = EngagedRebels(world, fig);
            if (engagers.Count == 0 || fig.Card.AttackType != "Ranged") return false;
            var states = world.Graph.Exhaust(fig, fig.Anchor, fig.Orientation, mp, null, "move");
            int curMin = int.MaxValue;
            foreach (var e in engagers)
                curMin = Math.Min(curMin, FocusResolver.CountSpaces(world.Board,
                    SpaceLegality.FigureCells(fig), SpaceLegality.FigureCells(e)));
            (int mind, int canHit, int negCost)? best = null;
            (Cell, int)? bestState = null;
            int bestCost = 0;
            foreach (var kv in states)
            {
                int cost = kv.Value;
                if (cost == 0) continue;
                var cells = CellsOf(fig, kv.Key.Item1, kv.Key.Item2);
                int mind = int.MaxValue;
                foreach (var e in engagers)
                    mind = Math.Min(mind, FocusResolver.CountSpaces(world.Board, cells,
                        SpaceLegality.FigureCells(e)));
                int canHit = ExecOk(world, fig, target, cells) ? 1 : 0;
                var score = (mind, canHit, -cost);
                if (best == null || ScoreGt(score, best.Value))
                {
                    best = score;
                    bestState = kv.Key;
                    bestCost = cost;
                }
            }
            if (best == null) return false;
            int bMind = best.Value.mind;
            bool bCanHit = best.Value.canHit != 0;
            if (bMind <= curMin) return false;   // cornered: stand and shoot
            var pathStates = world.Graph.PathTo(fig, fig.Anchor, fig.Orientation,
                                                bestState.Value, mp, "move");
            if (pathStates == null) return false;
            var full = new List<(Cell, int)> { (fig.Anchor, fig.Orientation) };
            full.AddRange(pathStates);
            var engNames = new List<string>();
            foreach (var e in engagers) engNames.Add(e.Id);
            plan.Actions.Add(new PlannedAction("move",
                new Dictionary<string, object>
                {
                    { "states", full },
                    { "mp", (long)bestCost },
                    { "purpose", "break_engagement" },
                    { "hazards_avoided", null },
                },
                why: "ranged figure engaged by " + string.Join(", ", engNames) + ": " +
                     "back away " + bestCost + " MP (gap " + curMin + "->" + bMind + ")" +
                     (bCanHit ? " while keeping attack" : " (attack given up)")));
            plan.MpPlanned = bestCost;
            if (bCanHit)
                plan.Actions.Add(new PlannedAction("attack",
                    new Dictionary<string, object> { { "target_id", target.Id } },
                    why: "attack " + target.Id + " after backing away"));
            plan.Why = "break engagement then attack. " +
                       (fallbackStates != null
                           ? "fallback move path exists (" + fallbackMp + " MP)" : "") +
                       " focus: " + plan.Focus.Why;
            return true;
        }

        static bool ScoreGt((int, int, int) a, (int, int, int) b)
        {
            if (a.Item1 != b.Item1) return a.Item1 > b.Item1;
            if (a.Item2 != b.Item2) return a.Item2 > b.Item2;
            return a.Item3 > b.Item3;
        }

        /// Adjacent to a closed UNLOCKED door and opening it makes an attack on
        /// the focus target legal from the current position? -> one Interact
        /// action. Actual opening happens on EXECUTION, never at plan time
        /// (the probe is always rolled back).
        static PlannedAction DoorInteractOption(World world, Figure fig, Figure target, int mp)
        {
            var cells = new HashSet<Cell>(SpaceLegality.FigureCells(fig));
            foreach (var kv in world.Board.Doors)
            {
                var edge = kv.Key;
                var door = kv.Value;
                if (door.IsOpen || door.Locked) continue;
                if (!cells.Contains(edge.Item1) && !cells.Contains(edge.Item2)) continue;
                bool nowOk;
                world.Board.OpenDoor(edge.Item1, edge.Item2);
                try
                {
                    nowOk = ExecOk(world, fig, target, cells);
                }
                finally
                {
                    // always roll back: the actual opening happens when the runner
                    // EXECUTES the interact action, not when it is planned
                    door.IsOpen = false;
                }
                if (nowOk)
                    return new PlannedAction("interact",
                        new Dictionary<string, object>
                        {
                            { "door", new List<object> {
                                new List<object> { (long)edge.Item1.X, (long)edge.Item1.Y },
                                new List<object> { (long)edge.Item2.X, (long)edge.Item2.Y } } },
                            { "opens", true },
                        },
                        why: "open door ((" + edge.Item1.X + ", " + edge.Item1.Y + "), (" +
                             edge.Item2.X + ", " + edge.Item2.Y + ")): unlocks a legal attack on " +
                             target.Id);
            }
            return null;
        }

        // ---------------------------------------------------------------- validator

        /// Concept 5.1: invariant violations of a plan (empty == legal).
        public static List<string> ValidatePlan(Figure fig, ActivationPlan plan)
        {
            var v = new List<string>();
            if (plan.MpPlanned > plan.MpBudget || plan.MpPlanned > fig.Card.Speed)
                v.Add("mp_overflow " + plan.MpPlanned + ">" + fig.Card.Speed);
            if (plan.Actions.Count > 2)
                v.Add("actions_overflow " + plan.Actions.Count + ">2");
            if (plan.AttackActions > 1)
                v.Add("attack_actions " + plan.AttackActions + ">1 (non-hero limit)");
            var seenSpecials = new HashSet<string>();
            foreach (var s in plan.Specials)
            {
                if (!fig.Card.Keywords.Contains(s)) v.Add("special_not_on_card " + s);
                if (seenSpecials.Contains(s)) v.Add("special_reused " + s);
                seenSpecials.Add(s);
            }
            foreach (var a in plan.Actions)
            {
                a.Params.TryGetValue("special", out var sp);
                // Python str(None) == "None" in the f-string
                if (a.Type == "special" && !(sp is string && ActiveSpecialKeywords.Contains((string)sp)))
                    v.Add("special_unknown " + (sp == null ? "None" : (string)sp));
                if (a.Type == "interact" &&
                    (!a.Params.TryGetValue("door", out var door) || door == null))
                    v.Add("interact_without_door");
            }
            if (plan.MpPlanned < 0) v.Add("negative_mp");
            return v;
        }

        // ---------------------------------------------- posture -> plan overlay
        // (sim/victory.py: "Bewegungsfesseln verschärfen", never invent)

        /// stall/deny blockade/intercept order: {node, reason} when the posture
        /// commands the figure onto the contested node INSTEAD of chasing the
        /// (not presently obstructing) focus target. null leaves the pattern
        /// plan untouched.
        public static Dictionary<string, object> PostureMotionOrder(World world, Figure fig,
                                                                    FocusDecision focus)
        {
            var vm = world.Victory;
            if (vm == null || fig.Team != "imperial" || focus == null) return null;
            if (vm.Posture != "stall" && vm.Posture != "deny") return null;
            var target = focus.Target;
            if (target == null || !target.Alive || target.Team != "rebel") return null;
            if (vm.TargetIsImmediate(world, target)) return null;
            var node = vm.ContestedNode(world, fig, target);
            if (node == null) return null;
            return new Dictionary<string, object>
            {
                { "node", node.Value },
                { "reason", "Posture " + vm.Posture + ": Blockadeknoten [" +
                            node.Value.X + ", " + node.Value.Y + "] vor Rebelle " +
                            target.Id + " besetzen" },
            };
        }

        /// Wrap the pattern plan: the posture may replace the move ENDPOINT
        /// (the contested node) and thereby suppress attacks on non-obstructing
        /// targets - it never adds actions, ranges, specials or bonuses. A plan
        /// that does not pass ValidatePosturePlan is never returned.
        public static ActivationPlan PosturePlan(World world, Figure fig, ActivationPlan basePlan)
        {
            var order = PostureMotionOrder(world, fig, basePlan.Focus);
            if (order == null) return basePlan;
            var node = (Cell)order["node"];
            var reason = (string)order["reason"];
            var res = world.Graph.Shortest(fig, fig.Anchor, fig.Orientation,
                (a, o) => {
                    foreach (var c in Geometry.FootprintCells(a, fig.Card.MiniSize, o))
                        if (c == node) return true;
                    return false;
                },
                "move", fig.Card.Speed, null);
            if (res == null) return basePlan;
            var plan = new ActivationPlan(fig.Id, basePlan.Focus, fig.Card.Speed);
            plan.Why = (basePlan.Why.Length > 0 ? basePlan.Why + " || " : "") + reason;
            if (res.Cost > 0)
            {
                var states = new List<(Cell, int)> { (fig.Anchor, fig.Orientation) };
                states.AddRange(res.States);
                plan.Actions.Add(new PlannedAction("move",
                    new Dictionary<string, object>
                    {
                        { "states", states },
                        { "mp", (long)res.Cost },
                    },
                    why: reason + " (" + res.Cost + " MP)"));
                plan.MpPlanned = res.Cost;
            }
            if (ValidatePosturePlan(world, fig, basePlan, plan).Count > 0) return basePlan;
            return plan;
        }

        /// Posture<->Pattern hierarchy validator (deliverable 3): the posture
        /// layer may only REPLACE movement endpoints and SUPPRESS actions.
        public static List<string> ValidatePosturePlan(World world, Figure fig,
                                                       ActivationPlan basePlan,
                                                       ActivationPlan plan)
        {
            var v = new List<string>();
            if (ReferenceEquals(plan, basePlan)) return v;
            if (plan.FigId != fig.Id) v.Add("posture layer changed the figure");
            if (!ReferenceEquals(plan.Focus, basePlan.Focus))
            {
                bool same = plan.Focus != null && basePlan.Focus != null &&
                            DeepEquals(plan.Focus.AsDict(), basePlan.Focus.AsDict());
                if (!same) v.Add("posture layer altered the focus decision");
            }
            if (plan.MpPlanned > fig.Card.Speed)
                v.Add("posture layer exceeded the printed MP budget");
            var bc = CountTypes(basePlan.Actions);
            var nc = CountTypes(plan.Actions);
            // The posture may SUPPRESS actions and RE-DIRECT movement, but it
            // may never ADD an attack/interact/special/rest beyond what the
            // pattern offered.
            foreach (var kind in new[] { "attack", "interact", "special", "rest" })
            {
                int b = bc.TryGetValue(kind, out var bv) ? bv : 0;
                int n = nc.TryGetValue(kind, out var nv) ? nv : 0;
                if (n > b) v.Add("posture invented actions: +" + kind + " x" + n);
            }
            var bs = SpecialsOf(basePlan);
            var ns = SpecialsOf(plan);
            if (ns.Count > bs.Count) { v.Add("posture invented specials"); }
            else foreach (var s in ns)
                if (!bs.Contains(s)) { v.Add("posture invented specials"); break; }
            var allowed = new HashSet<string>
                { "target_id", "states", "mp", "purpose", "focus_cost", "door", "special" };
            foreach (var a in plan.Actions)
            {
                var extra = new List<string>();
                foreach (var k in a.Params.Keys)
                    if (!allowed.Contains(k)) extra.Add(k);
                if (extra.Count > 0)
                {
                    extra.Sort(StringComparer.Ordinal);
                    v.Add("posture invented params: " + PyListStr(extra));
                }
                if (a.Type == "attack")
                {
                    a.Params.TryGetValue("target_id", out var tid);
                    var tgt = tid is string s ? world.ById(s) : null;
                    if (tgt == null || tgt.Team == "npc")
                        v.Add("posture invented an illegal attack target");
                }
                if (a.Type == "move")
                {
                    a.Params.TryGetValue("states", out var sv);
                    var states = sv as List<(Cell, int)>;
                    if (states != null && states.Count > 0 &&
                        (states[0].Item1 != fig.Anchor || states[0].Item2 != fig.Orientation))
                        v.Add("posture teleport: move does not start at the " +
                              "figure's current state");
                }
            }
            return v;
        }

        static Dictionary<string, int> CountTypes(List<PlannedAction> actions)
        {
            var d = new Dictionary<string, int>();
            foreach (var a in actions)
                d[a.Type] = d.TryGetValue(a.Type, out var n) ? n + 1 : 1;
            return d;
        }

        static List<string> SpecialsOf(ActivationPlan plan)
        {
            var outl = new List<string>();
            foreach (var a in plan.Actions)
                if (a.Type == "special")
                {
                    a.Params.TryGetValue("special", out var s);
                    outl.Add(s == null ? "" : s.ToString());
                }
            outl.Sort(StringComparer.Ordinal);
            return outl;
        }

        /// Python-shaped list repr for the violation message
        /// ("['a', 'b']" - sorted strings, mirrors sorted(extra) + str(list)).
        static string PyListStr(List<string> items)
        {
            var parts = new List<string>();
            foreach (var s in items) parts.Add("'" + s + "'");
            return "[" + string.Join(", ", parts) + "]";
        }

        /// Deep equality over the Python-shaped value trees that AsDict emits.
        internal static bool DeepEquals(object a, object b)
        {
            if (a is Dictionary<string, object> da && b is Dictionary<string, object> db)
            {
                if (da.Count != db.Count) return false;
                foreach (var kv in da)
                {
                    if (!db.TryGetValue(kv.Key, out var bv)) return false;
                    if (!DeepEquals(kv.Value, bv)) return false;
                }
                return true;
            }
            if (a is IList<object> la && b is IList<object> lb)
            {
                if (la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++)
                    if (!DeepEquals(la[i], lb[i])) return false;
                return true;
            }
            if (a is null || b is null) return a is null && b is null;
            if (a is string || b is string) return Equals(a, b);
            if (a is bool || b is bool) return Equals(a, b);
            if (a is IConvertible && b is IConvertible &&
                !(a is string) && !(b is string) && !(a is bool) && !(b is bool))
                return Convert.ToDouble(a) == Convert.ToDouble(b);
            return Equals(a, b);
        }
    }
}
