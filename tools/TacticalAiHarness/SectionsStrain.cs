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
// M11h adds the FUZZY usage prior rows: a hero row carrying "runden" (the
// public completed-activation count) also reports "unscharf" = the capped
// level of StrainPressure.Unscharf(runden, budget, endurance) with
// endurance = the ACTIVE side's printed endurance, or an explicit per-row
// "endurance" override for the cap-law vectors (an override stands in for a
// future card whose buffer exceeds its endurance - today every hero prints
// endurance >= buffer, which the m11h_prior_* cases pin as value-neutral).
// Key pairing is strict: "druck"/"unscharf" appear on a row ONLY when the
// input row carries "markers"/"runden", so every pre-M11h case stays
// byte-identical.
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
                    var card = HeroCards.For(heroName);
                    var budget = StrainPressure.Budget(card, xp);
                    var row = new Dictionary<string, object>
                    {
                        { "hero", heroName },
                        { "budget", budget },
                    };
                    if (h.ContainsKey("markers"))
                    {
                        var markers = Json.AsLong(h["markers"]);
                        var druck = StrainPressure.Pressure((int)markers, budget);
                        row["druck"] = druck.HasValue ? (object)druck.Value.ToString() : null;
                    }
                    if (h.ContainsKey("runden"))
                    {
                        // active side of the sheet drives the printed endurance
                        bool wounded = h.ContainsKey("wounded") && Json.AsBool(h["wounded"]);
                        int? endurance = h.ContainsKey("endurance") && h["endurance"] != null
                            ? (int?)Json.AsLong(h["endurance"])
                            : (card == null ? (int?)null
                               : (int?)(wounded ? card.VerwundeteAusdauer : card.GesundeAusdauer));
                        int? runden = h["runden"] != null
                            ? (int?)Json.AsLong(h["runden"]) : (int?)null;
                        var uns = StrainPressure.Unscharf(runden, budget, endurance);
                        row["unscharf"] = uns.HasValue ? (object)uns.Value.ToString() : null;
                    }
                    got.Add(row);
                }

                if (!c.ContainsKey("expect"))
                {
                    Check(name + ".expect", "present", "missing");
                    continue;
                }
                // Norm() on the expect side too: Json gives ints, Norm(actual)
                // yields longs and Eq(long, int) is type-strict.
                var exp = Norm(Json.AsList(c["expect"]).ConvertStrainRows());
                Check(name + ".budget", exp, got);
            }
        }
    }

    static class StrainBudgetJsonExt   // tiny shim: expect rows -> plain dicts
    {
        public static List<object> ConvertStrainRows(this List<object> rows)
        {
            var outp = new List<object>();
            foreach (var raw in rows)
            {
                var d = Json.AsDict(raw);
                var row = new Dictionary<string, object>
                {
                    { "hero", Json.AsStr(d["hero"]) },
                    { "budget", (int)Json.AsLong(d["budget"]) },
                };
                // M11h strict key pairing: optional keys ride along ONLY when
                // the expect row states them (mirrors the got-row rules).
                if (d.ContainsKey("druck"))
                    row["druck"] = d["druck"] != null ? Json.AsStr(d["druck"]) : null;
                if (d.ContainsKey("unscharf"))
                    row["unscharf"] = d["unscharf"] != null ? Json.AsStr(d["unscharf"]) : null;
                outp.Add(row);
            }
            return outp;
        }
    }
}
