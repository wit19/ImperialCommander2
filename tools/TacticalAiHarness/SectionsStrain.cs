// M11d strain-buffer vectors: strain_budget.json pins StrainPressure.Budget
// + Pressure DIRECTLY against the SHIPPED tactical_hero_cards.json (like the
// M11b card asset, never a fixture copy). These are FORMULA vectors: the
// Expect numbers state the rule (sum of the distinct printed reaction strains
// - prefix-merged export twins, XP gate on known-cost rows) and were derived
// from the asset by hand BEFORE the code existed, so a regression in the
// Core code fails here instead of being re-recorded into agreement.
// (The record/--record discipline governs MODEL outputs like overlord_focus
// expect blocks; a pure-input formula vector is authored, like threat.json's
// synthetic cases.)
using System;
using System.Collections.Generic;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        static void SecStrainBudget()
        {
            var v = LoadJson("strain_budget.json");
            if (HeroCards.DocOrNull() == null)
                throw new InvalidOperationException("hero-card asset not primed");

            foreach (var raw in Json.AsList(v["cases"]))
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                int? xp = c.ContainsKey("xp_amount") && c["xp_amount"] != null
                    ? (int?)Json.AsLong(c["xp_amount"]) : null;

                var got = new List<object>();
                foreach (var rawH in Json.AsList(c["heroes"]))
                {
                    var h = Json.AsDict(rawH);
                    var heroName = Json.AsStr(h["hero"]);
                    var markers = Json.AsLong(h["markers"]);
                    var card = HeroCards.For(heroName);
                    var budget = StrainPressure.Budget(card, xp);
                    var druck = StrainPressure.Pressure((int)markers, budget);
                    got.Add(new Dictionary<string, object>
                    {
                        { "hero", heroName },
                        { "budget", budget },
                        { "druck", druck.HasValue ? (object)druck.Value.ToString() : null },
                    });
                }

                if (!c.ContainsKey("expect"))
                {
                    Check(name + ".expect", "present", "missing");
                    continue;
                }
                // Norm() on the expect side too: Json gives ints, Norm(actual)
                // yields longs and Eq(long, int) is type-strict.
                var exp = Norm(Json.AsList(c["expect"]).ConvertList());
                Check(name + ".budget", exp, got);
            }
        }
    }

    static class StrainBudgetJsonExt   // tiny shim: expect rows -> plain dicts
    {
        public static List<object> ConvertList(this List<object> rows)
        {
            var outp = new List<object>();
            foreach (var raw in rows)
            {
                var d = Json.AsDict(raw);
                outp.Add(new Dictionary<string, object>
                {
                    { "hero", Json.AsStr(d["hero"]) },
                    { "budget", (int)Json.AsLong(d["budget"]) },
                    { "druck", d.ContainsKey("druck") && d["druck"] != null
                        ? Json.AsStr(d["druck"]) : null },
                });
            }
            return outp;
        }
    }
}
