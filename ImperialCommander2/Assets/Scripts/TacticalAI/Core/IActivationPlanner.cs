// M1 planner interface (concept section 4.1). The Tactical AI mode and the
// legacy behaviour both sit behind this seam; TacticalAISettings chooses the
// implementation per mission (Default = LegacyPlanner — bit-compatible).
//
// Deliberately small: everything the planner needs arrives via the
// read-only snapshots below, so the planner never touches Unity objects.
using System.Collections.Generic;

namespace TacticalAI.Core
{
    /// <summary>Read-only board view handed to planners (concept 4.1).</summary>
    public interface IBoardSnapshot
    {
        Board Board { get; }
        /// <summary>Living figures visible to the planner (imperial side +
        /// whatever the App view exposes; never hidden rebel positions —
        /// AppView discipline, concept 3.3.3).</summary>
        IReadOnlyList<Figure> Figures { get; }
    }

    /// <summary>Printed card data seam (M4a asset). Implementations must
    /// expose ONLY values printed on the deployment/order cards — no stat
    /// buffs, no bonuseffects.json in Tactical AI mode (fairness rule).</summary>
    public interface ICardData
    {
        DeploymentCard Card(string id);
    }

    /// <summary>Per-round activation order + per-activation action plan
    /// output. Implementations: LegacyPlanner (v1 wrapper) and the
    /// TacticalAI planner (Focus -> ActionBuilder -> OrderPlan).</summary>
    public interface IActivationPlanner
    {
        /// <summary>Stable name for logs/UI: "legacy" | "tactical_ai".</summary>
        string ModeName { get; }

        /// <summary>Called once per imperial round: returns group ids in
        /// activation order (OrderPlanner permutation; Legacy: existing
        /// order unchanged).</summary>
        IReadOnlyList<string> PlanRound(IBoardSnapshot board, int roundNo);
    }

    // NOTE: the seeded randomness seam (IRandom) lives in World.cs — the one
    // real definition (RandBelow/NextUnit + Deterministic/Scripted impls).
    // The earlier contract sketch (NextDouble/Fork) was removed here: it
    // duplicated the type name in the same namespace and had zero references.
}
