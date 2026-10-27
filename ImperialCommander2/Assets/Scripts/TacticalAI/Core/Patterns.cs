// Port of sim/patterns.py — behaviour patterns (concept 3.5, Schicht E):
// tiebreak overlays and movement shackles ONLY, never legality changes.
//
// The table in concept 3.5 implemented for the grid sim:
//
//   aggressive  pure focus (shortest attack path) - no clamp
//   guard       movement only inside a guard zone around the assigned
//               objective; pursuit starts on threat (hero near the zone or
//               already adjacent to the guard)
//   blocker     holds the key nodes of the rebel's shortest route between the
//               rebels and their objective (doors, choke points): movement may
//               only end in the "path valley" of the heroes' terrain-cost field
//   hunter      prioritises the hero that currently WORKS an objective (holds
//               the objective token - mission.hero_goal); otherwise aggressive
//   support     keeps at least distance 2 from every hero; otherwise moves
//               toward its most wounded ally (keeps that ally in LOS)
//   ambusher    lies low: no movement and no attack while the nearest hero is
//               farther than AMBUSH_RADIUS; once armed, behaves aggressive
//
// Every shackle is a predicate over END STATES of a legal path; the planner
// walks the focus path only as far as the predicate allows (walk truncation),
// which is exactly the "Bewegungsfessel" notion of concept 3.5. Attacks remain
// possible whenever legal (a Guard that is attacked may always hit back).
//
// PORT NOTES (deviations from the task sketch, dictated by the reference):
//   * posture_plan is NOT ported: it lives in sim/action_builder.py, not in
//     patterns.py, and needs the action builder.
//   * candidate_filter / pattern_targets / distance_shackle / PatternText do
//     NOT exist in sim/patterns.py (candidate selection is the doctrine
//     layer's job - sim/doctrine.py); nothing is invented here.
//   * sim/patterns.py reads MissionWorld seams (guard_zone, hero_path_valley,
//     heroes_working_objective, heroes_known). The frozen World class has no
//     such fields, so these enter as explicit parameters/delegates.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    /// sim/mission.py hero_path_valley() tuple: (field, hero_best, slack).
    public sealed class HeroPathValley
    {
        public Dictionary<Cell, int> Field;
        public int HeroBest;
        public int Slack;
    }

    public static class Patterns
    {
        public static readonly string[] PatternsList =
            { "aggressive", "guard", "blocker", "hunter", "support", "ambusher" };

        public const int AmbushRadius = 3;    // hero chebyshev distance that "arms" an ambusher
        public const int GuardLeash = 3;      // hero distance to the zone that triggers pursuit

        public static bool IsPattern(string name)
        {
            var l = new List<string>(PatternsList);
            return l.IndexOf(name) >= 0;       // name in PATTERNS
        }

        static List<Figure> AliveImperials(World world, Figure fig)
        {
            var outl = new List<Figure>();
            foreach (var f in world.Figures)
                if (f.Alive && f.Team == "imperial" && !ReferenceEquals(f, fig)) outl.Add(f);
            return outl;
        }

        // blind mode (concept 3.3.3): patterns may only use heroes the doctrine
        // layer is allowed to know - LOS-visible right now - not board truth.
        static List<Figure> KnownHeroes(World world, Figure fig,
                                        Func<Figure, List<Figure>> heroesKnown)
        {
            if (fig != null && heroesKnown != null) return heroesKnown(fig);
            var outl = new List<Figure>();
            foreach (var h in world.Figures)
                if (h.Alive && h.Team == "rebel") outl.Add(h);
            return outl;
        }

        static int HeroGap(World world, ICollection<Cell> cells, Figure fig,
                           Func<Figure, List<Figure>> heroesKnown)
        {
            var heroes = KnownHeroes(world, fig, heroesKnown);
            if (heroes.Count == 0) return 1 << 30;
            int best = 1 << 30;
            foreach (var h in heroes)
            {
                int g = Geometry.ChebyshevGap(cells, SpaceLegality.FigureCells(h));
                if (g < best) best = g;
            }
            return best;
        }

        // ------------------------------------------------------------------
        // target overrides
        // ------------------------------------------------------------------

        /// Return a target FIGURE to override the focus target, or null to
        /// keep the focus target. Never invents targets - only re-orders live
        /// rebels.
        ///
        /// Posture hierarchy (concept 3.5 note): with an active
        /// MissionVictoryModel the posture W-function has already ranked
        /// targets - a pattern may then only act as tiebreak/shackle, never
        /// overwrite the posture decision.
        public static Figure TargetOverride(World world, Figure fig,
                                            Func<HashSet<string>> heroesWorkingObjective = null,
                                            Func<Figure, List<Figure>> heroesKnown = null)
        {
            string pattern = fig.Pattern;
            if (pattern == "hunter")
            {
                if (world.Victory != null) return null;
                var rebels = KnownHeroes(world, fig, heroesKnown);
                if (rebels.Count == 0) return null;
                var working = heroesWorkingObjective != null
                    ? heroesWorkingObjective() : new HashSet<string>();
                Figure best = null;
                int bestKey0 = 0, bestKey1 = 0;
                foreach (var r in rebels)                  // Python min: first minimum
                {
                    int k0 = working.Contains(r.Id) ? 0 : 1;
                    int k1 = r.Index;
                    if (best == null || k0 < bestKey0 || (k0 == bestKey0 && k1 < bestKey1))
                    {
                        best = r; bestKey0 = k0; bestKey1 = k1;
                    }
                }
                return best;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // movement shackles: predicate over (anchor, orientation) end states
        // ------------------------------------------------------------------

        /// Predicate restricting WHERE this figure's move may END.
        /// null means unrestricted (aggressive / armed ambusher).
        /// guardZone: MissionWorld.guard_zone(); valley:
        /// MissionWorld.hero_path_valley() (null = no valley).
        public static Func<(Cell anchor, int orientation), bool> MoveShackle(
            World world, Figure fig,
            ICollection<Cell> guardZone = null,
            HeroPathValley valley = null,
            Func<Figure, List<Figure>> heroesKnown = null)
        {
            string pattern = fig.Pattern;

            if (pattern == "aggressive" || pattern == "hunter") return null;

            if (pattern == "guard")
            {
                var zone = guardZone ?? new List<Cell>();
                var heroes = KnownHeroes(world, fig, heroesKnown);
                bool threat = false;
                foreach (var h in heroes)
                    if (Geometry.ChebyshevGap(SpaceLegality.FigureCells(h), zone) <= GuardLeash)
                    { threat = true; break; }
                if (threat) return null;               // pursuit authorised (still legal moves only)
                return state => ZoneOk(world, fig, state, zone, radius: 2);
            }

            if (pattern == "blocker")
            {
                if (valley == null) return null;
                return state => ValleyOk(world, fig, state, valley);
            }

            if (pattern == "support")
            {
                return state =>
                {
                    var cells = Footprint(world, fig, state);
                    return HeroGap(world, cells, fig, heroesKnown) >= 2;
                };
            }

            if (pattern == "ambusher")
            {
                if (Armed(world, fig, heroesKnown)) return null;
                // un-armed: hold position entirely (the planner skips movement)
                return state => state.anchor == fig.Anchor && state.orientation == fig.Orientation;
            }

            return null;
        }

        /// Ambushers hold fire until armed; everybody attacks whenever legal.
        public static bool AttackAllowed(World world, Figure fig,
                                         Func<Figure, List<Figure>> heroesKnown = null)
        {
            if (fig.Pattern == "ambusher" && !Armed(world, fig, heroesKnown)) return false;
            return true;
        }

        // ------------------------------------------------------------------
        // support movement nudge: prefer endpoints near the most wounded ally
        // ------------------------------------------------------------------

        /// Sort key for candidate END STATES (lower is better). Base is the
        /// focus/path cost; patterns only append tiebreak terms.
        public static (int, int) EndpointScore(World world, Figure fig,
                                               (Cell anchor, int orientation) state,
                                               int baseCost,
                                               Func<Figure, List<Figure>> heroesKnown = null)
        {
            if (fig.Pattern == "support")
            {
                var allies = AliveImperials(world, fig);
                if (allies.Count > 0)
                {
                    Figure hurt = null;                  // Python min: first minimum
                    foreach (var a in allies)
                        if (hurt == null || a.Vitality < hurt.Vitality
                            || (a.Vitality == hurt.Vitality && a.Index < hurt.Index))
                            hurt = a;
                    int gap = Geometry.ChebyshevGap(Footprint(world, fig, state),
                                                    SpaceLegality.FigureCells(hurt));
                    return (baseCost, gap);
                }
            }
            return (baseCost, 0);
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        static List<Cell> Footprint(World world, Figure fig, (Cell anchor, int orientation) state)
            => Geometry.FootprintCells(state.anchor, fig.Card.MiniSize, state.orientation);

        public static bool ZoneOk(World world, Figure fig, (Cell anchor, int orientation) state,
                                  ICollection<Cell> zone, int radius)
        {
            var cells = Footprint(world, fig, state);
            return Geometry.ChebyshevGap(cells, zone) <= radius + 1;
        }

        /// End state sits in the hero path valley: the terrain cost from every
        /// footprint cell to the objective is not worse than the heroes'
        /// current best value + slack.
        public static bool ValleyOk(World world, Figure fig, (Cell anchor, int orientation) state,
                                    HeroPathValley valley)
        {
            var cells = Footprint(world, fig, state);
            if (cells.Count == 0) return false;
            int worst = int.MinValue;
            foreach (var c in cells)
            {
                int f = valley.Field.TryGetValue(c, out int v) ? v : 1 << 30;
                if (f > worst) worst = f;
            }
            return worst <= valley.HeroBest + valley.Slack;
        }

        public static bool Armed(World world, Figure fig,
                                 Func<Figure, List<Figure>> heroesKnown = null)
        {
            // M9-F6: an open wound token wakes an ambusher too - a hero's
            // second bar may still be full right after the reset
            // (mirror of sim patterns._armed).
            if (fig.Vitality < fig.Card.Health || fig.Wounded)
                return true;   // being hit always wakes an ambusher
            var self = new List<Cell>(SpaceLegality.FigureCells(fig));
            return HeroGap(world, self, fig, heroesKnown) <= AmbushRadius;
        }
    }
}
