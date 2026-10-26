// Shared world state for the TacticalAI core (see CONTRACT_tacticalai_core.md).
// Pure C#: the Unity adapter (Assets/Scripts/TacticalAI/Unity/) builds a
// World from IC2 runtime state; the console harness builds it from fixtures.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    /// Deterministic randomness seam. The C# app uses DeterministicRandom;
    /// the harness replays the Python-exported draw streams with ScriptedRandom
    /// so shuffle/rank MECHANICS are cross-checked without porting MT19937.
    public interface IRandom
    {
        int RandBelow(int n);   // uniform 0..n-1 (n>=1)
        double NextUnit();      // uniform [0,1)
    }

    /// SplitMix64 -> per-call derived draws; deterministic, seedable per mission.
    public sealed class DeterministicRandom : IRandom
    {
        ulong _state;
        public DeterministicRandom(ulong seed) { _state = seed; }
        ulong Next64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public double NextUnit() => (Next64() >> 11) * (1.0 / 9007199254740992.0);
        public int RandBelow(int n)
        {
            if (n <= 1) return 0;
            // multiply-shift; bias negligible for gameplay-scale n
            return (int)((Next64() >> 32) % (ulong)n);
        }
    }

    /// Replays exported Python streams: RandBelow consumes the recorded
    /// [_randbelow(n) -> value] entries in order (asserting n matches),
    /// NextUnit the recorded random() floats. Used only by the harness.
    public sealed class ScriptedRandom : IRandom
    {
        readonly List<object> _below;
        readonly List<object> _u;
        int _bi, _ui;
        public ScriptedRandom(List<object> below, List<object> u) { _below = below ?? new List<object>(); _u = u ?? new List<object>(); }
        public int RandBelow(int n)
        {
            if (_bi >= _below.Count) throw new InvalidOperationException("ScriptedRandom: below-stream exhausted");
            var entry = Json.AsList(_below[_bi++]);
            int want = Json.AsInt(entry[0]);
            if (want != n) throw new InvalidOperationException("ScriptedRandom: randbelow(" + n + ") but Python drew for " + want);
            return Json.AsInt(entry[1]);
        }
        public double NextUnit()
        {
            if (_ui >= _u.Count) throw new InvalidOperationException("ScriptedRandom: u-stream exhausted");
            return Json.AsDouble(_u[_ui++]);
        }
        public bool Exhausted() => _bi >= _below.Count && _ui >= _u.Count;
    }

    public sealed class World
    {
        public static readonly int Inf = 1 << 30;

        public Board Board;
        public List<Figure> Figures = new List<Figure>();
        public PathGraph Graph;
        public Los Los;
        public ObjectiveRuntime Objectives;
        public MissionVictoryModel Victory;            // null = pure GH cascade
        public HashSet<string> FocusCandidates;        // null = no doctrine filter
        public Func<Figure, double> RoleWeight;        // null => 0
        public SkillProfile Skill;                     // null = reference path
        public IRandom SkillRng;
        public int RoundNo;

        public World(Board board, List<Figure> figures)
        {
            Board = board;
            Figures = figures;
            Los = new Los(board);
            Graph = new PathGraph(board, Occupants);
        }

        public Dictionary<Cell, List<Figure>> Occupants()
        {
            var occ = new Dictionary<Cell, List<Figure>>();
            foreach (var f in Figures)
            {
                if (!f.Alive) continue;
                foreach (var c in SpaceLegality.FigureCells(f))
                {
                    if (!occ.TryGetValue(c, out var list)) { list = new List<Figure>(); occ[c] = list; }
                    list.Add(f);
                }
            }
            return occ;
        }

        public Figure ById(string id)
        {
            foreach (var f in Figures) if (f.Id == id) return f;
            return null;
        }

        public List<Figure> AliveOf(string team)
        {
            var outl = new List<Figure>();
            foreach (var f in Figures) if (f.Alive && f.Team == team) outl.Add(f);
            return outl;
        }
    }
}
