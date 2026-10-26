// M1: behavioural no-op planner wrapper (concept section 4.1/4.2). When
// Tactical AI mode is OFF (the Default), the existing LegacyActivation
// logic in ActivationTargeting.cs runs untouched; LegacyPlanner exists so
// the planner seam can be exercised in tests without changing behaviour.
//
// It deliberately does NOT reimplement legacy targeting: it records the
// order it is handed and returns it verbatim. The hook sites keep their
// original code path as the `else` branch (bit-compatibility rule).
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public sealed class LegacyPlanner : IActivationPlanner
    {
        public string ModeName => "legacy";

        /// <summary>Identity: legacy order passes through unchanged.</summary>
        public IReadOnlyList<string> PlanRound(IBoardSnapshot board, int roundNo)
        {
            var ids = new List<string>();
            if (board != null && board.Figures != null)
                foreach (var f in board.Figures)
                    if (f.Team == "imperial" && f.Alive) ids.Add(f.Id);
            return ids;
        }
    }
}
