// M6 overlord-focus vectors: overlord_focus.json pins the DETERMINISTIC
// target ranking of the OverlordFocus layer (aim marks > counter chip law >
// threat economy > wounded > M10a campaign investment > reference proxy >
// stable tie chain) over the SAME curated
// public hero table the reference path uses. Like world_feed.json this is a
// C#-side canonical vector (the sim has no app feed); its W components are
// sim-vectored via hero_profiles.json / hero_w.json. Expect blocks were
// recorded once with `--record`, reviewed, then pinned - never hand-written.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        static void SecOverlordFocus()
        {
            var v = LoadJson("overlord_focus.json");
            var cases = Json.AsList(v["cases"]);
            var assetDoc = HeroProfiles.DocOrNull()
                ?? throw new InvalidOperationException("asset doc not primed");
            var profiles = HeroProfiles.ProfilesFromDoc(assetDoc);
            var recorded = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var raw in cases)
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var posture = Json.AsStr(c["posture"]);
                string counter = null;
                if (c.ContainsKey("counter") && c["counter"] != null)
                    counter = Json.AsStr(c["counter"]);

                List<FocusCandidate> Build()
                {
                    // M10a: a case hero may carry a campaign "loadout"
                    // ({items, skills}); it is merged into the curated/
                    // placeholder profile EXACTLY like the live bridge's
                    // ProfileFor does (ApplyCampaignLoadouts on the app's
                    // own CampaignData items/skills tables).
                    var merged = profiles;
                    var loEntries = new List<object>();
                    foreach (var rawH in Json.AsList(c["heroes"]))
                    {
                        var h = Json.AsDict(rawH);
                        if (h.ContainsKey("loadout") && h["loadout"] != null)
                        {
                            var lo = Json.AsDict(h["loadout"]);
                            loEntries.Add(new Dictionary<string, object>
                            {
                                { "hero", Json.AsStr(h["hero"]) },
                                { "items", Json.Get(lo, "items", null) },
                                { "skills", Json.Get(lo, "skills", null) },
                            });
                        }
                    }
                    if (loEntries.Count > 0)
                    {
                        var refs = new Dictionary<string, object>
                        {
                            { "items", LoadLangJson("En", "CampaignData/items.json") },
                            { "skills", LoadLangJson("En", "CampaignData/skills.json") },
                        };
                        merged = HeroProfiles.ApplyCampaignLoadouts(
                            profiles, loEntries, refs);
                    }

                    var list = new List<FocusCandidate>();
                    foreach (var rawH in Json.AsList(c["heroes"]))
                    {
                        var h = Json.AsDict(rawH);
                        HeroThreatProfile p = null;
                        if (h.ContainsKey("profile") && h["profile"] != null)
                            merged.TryGetValue(Json.AsStr(h["profile"]), out p);
                        list.Add(new FocusCandidate
                        {
                            HeroId = Json.AsStr(h["hero"]),
                            SquadRow = Json.AsInt(h["row"]),
                            Profile = p,
                            Wounded = Json.AsBool(h["wounded"]),
                            Marks = (int)Json.AsLong(h["marks"]),
                            MarkedRound = (int)Json.AsLong(h["marked_round"]),
                            AttackDice = (int)Json.AsLong(h["attack_dice"]),
                            Health = Json.AsInt(h["health"]),
                            // M10b: optional per-hero flag (default false).
                            ObjectiveActor = h.ContainsKey("objective_actor")
                                && Json.AsBool(h["objective_actor"]),
                        });
                    }
                    return list;
                }

                // replay twice: the layer is a pure function (determinism law)
                var d1 = OverlordFocus.Rank(Build(), posture, counter);
                var d2 = OverlordFocus.Rank(Build(), posture, counter);
                Check(name + ".replay",
                      d1.Select(x => (object)x.AsDict()).ToList(),
                      d2.Select(x => (object)x.AsDict()).ToList());

                // fairness canon: ranking is a PERMUTATION of the input,
                // never a filter (checked for every case, always)
                var inIds = Json.AsList(c["heroes"]).Select(Json.AsDict)
                    .Select(h => Json.AsStr(h["hero"])).OrderBy(x => x, StringComparer.Ordinal)
                    .Select(x => (object)x).ToList();
                var outIds = d1.Select(x => x.HeroId).OrderBy(x => x, StringComparer.Ordinal).ToList();
                Check(name + ".permutation", inIds, outIds);

                var got = d1.Select(x => (object)x.AsDict()).ToList();
                if (Record) { recorded[name] = got; continue; }

                if (!c.ContainsKey("expect"))
                {
                    Check(name + ".expect", "present", "missing - run with --record");
                    continue;
                }
                Check(name + ".rank", Json.AsList(c["expect"]), got);
            }
            if (Record)
            {
                var path = Path.Combine(FIX, "overlord_focus_record.json");
                File.WriteAllText(path, DumpJson(recorded));
                Console.WriteLine("   (record) wrote " + path + " for " + recorded.Count + " cases");
            }
        }
    }
}
