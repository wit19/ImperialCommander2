// M13c optional-deployment revival section: the pure fork/law behind the
// revived flow (tactical = AI places + DP marked + announced; legacy = the
// manual DM placement window - Thomas 28.10). OptionalDeploy is
// presentation/branching-only (planning behaviour untouched, Canon vectors
// unmoved) and has no Python sim twin, so the expect blocks are hand-pinned
// ground truth - the same documented route as action_tree.json, not --record.
// The label STRINGS live in ui.json (localization convention, presence is
// pinned gate-side in OptionalDeployGate.cs); only the substitution law is
// pinned here.
using System;
using System.Collections.Generic;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        static void SecOptionalDeploy()
        {
            var v = LoadJson("optional_deploy.json");

            // ---------------------------------------------- mode fork (Scope D)
            foreach (var raw in Json.AsList(v["path_cases"]))
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var got = OptionalDeploy.Path(
                    Json.AsBool(c["enabled"]),
                    Json.AsStr(c["mode"]),
                    Json.AsStr(c["missionAiMode"]));
                Check("path[" + name + "]", Json.AsStr(c["expect"]), got);
            }

            // ------------------------------------------------ group candidates
            foreach (var raw in Json.AsList(v["candidate_cases"]))
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var got = OptionalDeploy.CandidateIds(
                    Json.AsList(c["hand"]).Select(x => Json.AsStr(x)),
                    Json.AsList(c["manual"]).Select(x => Json.AsStr(x)),
                    Json.AsList(c["deployed"]).Select(x => Json.AsStr(x)));
                var exp = Json.AsList(c["expect"]).Select(x => Json.AsStr(x)).ToList();
                Check("candidates[" + name + "].count", (long)exp.Count, (long)got.Count);
                for (int i = 0; i < Math.Min(exp.Count, got.Count); i++)
                    Check("candidates[" + name + "][" + i + "]", exp[i], got[i]);
            }

            // ------------------------------------------------- cost / spend law
            foreach (var raw in Json.AsList(v["cost_cases"]))
            {
                var c = Json.AsDict(raw);
                int cost = (int)Json.AsLong(c["cost"]);
                int mod = (int)Json.AsLong(c["modifier"]);
                var exp = Json.AsDict(c["expect"]);
                Check("cost[" + cost + "+" + mod + "].display",
                    Json.AsLong(exp["display"]), (long)OptionalDeploy.DisplayedCost(cost));
                Check("cost[" + cost + "+" + mod + "].spend",
                    Json.AsLong(exp["spend"]), (long)OptionalDeploy.ThreatSpend(cost, mod));
            }

            // ---------------------------------------------------- confirm gate
            foreach (var raw in Json.AsList(v["confirm_cases"]))
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var got = OptionalDeploy.CanConfirmManual(
                    Json.AsStr(c["group"]),
                    (int)Json.AsLong(c["cost"]),
                    Json.AsBool(c["dpPicked"]),
                    Json.AsBool(c["dpValid"]));
                Check("confirm[" + name + "]", Json.AsBool(c["expect"]), got);
            }

            // -------------------------------------------------- announce line
            foreach (var raw in Json.AsList(v["announce_cases"]))
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var got = OptionalDeploy.AnnounceLine(
                    Json.AsStr(c["template"]),
                    Json.AsStr(c["group"]),
                    Json.AsStr(c["dp"]));
                Check("announce[" + name + "]", Json.AsStr(c["expect"]), got);
            }
        }
    }
}
