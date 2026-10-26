// M3.1 HeroThreatProfile sections: curated-asset loader rows, campaign
// loadout scoring against the app's OWN CampaignData tables, the
// disengage-utility flag rule, the inference replacement and the profile-
// bound W rows (variants none/synth/asset). All semantics ported 1:1 from
// sim/hero_profiles.py via tools/export_csharp_vectors.py.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        // ------------------------------------------------------ languages dir
        // The C# port reads the app's REAL reference tables (CampaignData)
        // from the repo checkout — same bytes the sim copies under
        // sim/data/reference/ (Pflichttest 11: only ids count).
        static string _languagesDir;
        static string LanguagesDir()
        {
            if (_languagesDir != null) return _languagesDir;
            var env = Environment.GetEnvironmentVariable("TACAI_LANGUAGES_DIR");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            {
                _languagesDir = env;
                return _languagesDir;
            }
            var d = new DirectoryInfo(FIX);
            for (int i = 0; i < 12 && d != null; i++, d = d.Parent)
            {
                var cand = Path.Combine(d.FullName, "ImperialCommander2",
                                        "Assets", "Resources", "Languages");
                if (!Directory.Exists(cand))
                    cand = Path.Combine(d.FullName, "Assets", "Resources", "Languages");
                if (Directory.Exists(cand)) { _languagesDir = cand; return cand; }
            }
            throw new FileNotFoundException("Languages dir not found - set TACAI_LANGUAGES_DIR");
        }

        static object LoadLangJson(string lang, string rel)
        {
            var p = Path.Combine(LanguagesDir(), lang, rel);
            // CampaignData files carry trailing commas (legacy JS export) —
            // the sim's _strip_trailing_commas parity.
            var txt = System.Text.RegularExpressions.Regex.Replace(
                File.ReadAllText(p), @",\s*([}\]])", "$1");
            return Json.Parse(txt);
        }

        // ------------------------------------------------- synthetic profiles
        static List<HeroThreatProfile> SyntheticProfiles(Dictionary<string, object> vectors)
        {
            var outp = new List<HeroThreatProfile>();
            foreach (var pO in Json.AsList(vectors["synthetic_profiles"]))
            {
                var p = Json.AsDict(pO);
                outp.Add(new HeroThreatProfile
                {
                    Name = Json.AsStr(p["name"]),
                    Role = Json.AsStr(p["role"]),
                    EvDamagePerRound = HeroProfiles.ParseFrac(p["evDamagePerRound"]),
                    RangeMax = Json.AsInt(p["rangeMax"]),
                    IgnoreLos = Json.AsBool(p["ignoreLos"]),
                    SelfHealPossible = Json.AsBool(p["selfHealPossible"]),
                    Mobility = Json.AsInt(p["mobility"]),
                    Flags = Json.AsList(p["flags"]).ConvertAll(o => Json.AsStr(o)),
                    Knowledge = Json.AsStr(p["knowledge"]),
                });
            }
            return outp;
        }

        static World ProbeWorld(List<HeroThreatProfile> synth)
        {
            // probe world: ProfileFocusKey reads HeroProfiles[figure.Id] only
            var store = synth.ToDictionary(p => p.Name, p => p);
            return new World(null, new List<Figure>()) { HeroProfiles = store };
        }

        // ---------------------------------------------------- hero_profiles
        static void SecHeroProfiles()
        {
            var v = LoadJson("hero_profiles.json");

            // 0) the shipped app asset must be the SAME bytes as the
            //    curated sim asset the vectors were exported against.
            if (v.ContainsKey("asset_sha256") && v["asset_sha256"] != null)
            {
                var assetPath = Path.Combine(LanguagesDir(), "En",
                                             "tactical_hero_profiles.json");
                if (File.Exists(assetPath))
                {
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    var got = BitConverter.ToString(sha.ComputeHash(
                        File.ReadAllBytes(assetPath))).Replace("-", "").ToLowerInvariant();
                    Check("hero.asset_bytes", Json.AsStr(v["asset_sha256"]), got);
                }
                else Fail("hero.asset_bytes", "shipped asset missing: " + assetPath);
            }

            // 1) loader vectors: doc -> profiles (byte-exact rows). The
            //    asset doc is primed in Main from the shipped app asset;
            //    the fixture FILE hero_profiles.json carries the vectors.
            var assetDoc = HeroProfiles.DocOrNull()
                ?? throw new InvalidOperationException("asset doc not primed");
            var profiles = HeroProfiles.ProfilesFromDoc(assetDoc);

            var expect = Json.AsDict(v["profiles"]);
            Check("hero.profile_names",
                  expect.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList<object>(),
                  profiles.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList<object>());
            var probe = ProbeWorld(SyntheticProfiles(v));
            foreach (var kv in OrdKeys(expect))
            {
                if (!profiles.TryGetValue(kv.Key, out var p))
                {
                    Fail("hero.profiles." + kv.Key, "loader has no entry");
                    continue;
                }
                Check("hero.profiles." + kv.Key, kv.Value, p.VectorRow(probe));
            }

            // 2) disengage flag rule (Pflichttest 3/4/5)
            Check("hero.cover_range", v["cover_shelter_max_range"],
                  HeroProfiles.CoverShelterMaxRange());
            foreach (var rO in Json.AsList(v["cover_worthless"]).Select(Json.AsDict).Enumerate("cw"))
            {
                var r = rO.Item2;
                Check("hero.cover_worthless[" + rO.Item1 + "]", r["worthless"],
                      HeroProfiles.CoverIsWorthless(Json.AsBool(r["ignore_los"]),
                                                    Json.AsInt(r["range_max"])));
            }

            // 3) focus_key branch rows (every posture x counter combo)
            var synthList = SyntheticProfiles(v);
            var synthByName = synthList.ToDictionary(p => p.Name, p => p);
            foreach (var rO in Json.AsList(v["focus_keys"]).Select(Json.AsDict).Enumerate("fk"))
            {
                var r = rO.Item2;
                var p = synthByName[Json.AsStr(r["profile"])];
                var w = ProbeWorld(new List<HeroThreatProfile> { p });
                var fig = new Figure { Id = Json.AsStr(r["profile"]) };
                Check("hero.focus_keys[" + rO.Item1 + "]", r["key"],
                      HeroProfiles.ProfileFocusKey(w, fig,
                          Json.AsStr(r["posture"]),
                          r["counter"] == null ? null : Json.AsStr(r["counter"])));
            }

            // 4) inference replacement (lethality prior -> profile)
            var inf = Json.AsDict(v["inference"]);
            var (ranges, lethal) = HeroProfiles.ProfilesToInference(synthByName);
            var gotRanges = ranges.ToDictionary(kv => kv.Key,
                kv => (object)kv.Value.Select(x => (object)(long)x).ToList());
            Check("hero.inference.ranges", inf["ranges"],
                  SortDictObj(gotRanges));
            Check("hero.inference.lethal", inf["lethal"],
                  SortDictObj(lethal.ToDictionary(kv => kv.Key,
                      kv => (object)kv.Value.ToString())));

            // 5) campaign loadouts against the app's OWN CampaignData
            //    (v3.11: same bytes the live app reads; En path swappable)
            var refs = new Dictionary<string, object>
            {
                { "items", LoadLangJson("En", "CampaignData/items.json") },
                { "skills", LoadLangJson("En", "CampaignData/skills.json") },
            };
            var loFixture = Json.AsDict(LoadJson("campaign_loadouts.json"));
            var baseProfiles = HeroProfiles.ProfilesFromDoc(assetDoc);
            var loaded = HeroProfiles.ApplyCampaignLoadouts(
                HeroProfiles.ProfilesFromDoc(assetDoc),
                HeroProfiles.LoadoutRows(loFixture), refs);
            var byId = HeroProfiles.ProfilesByAppId(baseProfiles);
            var loExp = Json.AsList(v["loadouts"]);
            foreach (var rO in loExp.Select(Json.AsDict).Enumerate("lo"))
            {
                var r = rO.Item2; int i = rO.Item1;
                string who = Json.AsStr(r["hero"]);
                // resolution parity with apply_campaign_loadouts: by NAME
                // first (empty when the entry is hero_id-only), else H-id.
                var target = baseProfiles.ContainsKey(who) ? baseProfiles[who]
                    : (byId.ContainsKey(who) ? byId[who] : null);
                if (Json.AsBool(r["skipped"]))
                {
                    // a hero_id-only entry whose NAME key is absent from the
                    // base table is skipped (name lookup, not id lookup)
                    Check("hero.loadouts[" + i + "].skipped", true,
                          !baseProfiles.ContainsKey(who));
                    continue;
                }
                var key = target.Name;
                Check("hero.loadouts[" + i + "].loaded", r["loaded"],
                      loaded[key].VectorRow(probe));
            }
        }

        static Dictionary<string, object> SortDictObj(Dictionary<string, object> d) =>
            d.OrderBy(kv => kv.Key, StringComparer.Ordinal)
             .ToDictionary(kv => kv.Key, kv => kv.Value);

        // ------------------------------------------------------------ hero_w
        static void SecHeroW()
        {
            var v = LoadJsonList("hero_w.json");
            var assetDoc = HeroProfiles.DocOrNull()
                ?? throw new InvalidOperationException("asset doc not primed");
            var baseProfiles = HeroProfiles.ProfilesFromDoc(assetDoc);

            foreach (var eO in v.Select(Json.AsDict).Enumerate("hw"))
            {
                var e = eO.Item2;
                var scenario = Json.AsDict(Scenarios()[Json.AsStr(e["scenario"])]);
                var (board, world) = BuildScenarioWorld(scenario);
                var vm = MissionVictoryModel.FromBoard(board);
                if (scenario.ContainsKey("victoryCounter"))
                    vm.Counter = new VictoryCounter(Json.AsStr(scenario["victoryCounter"]));

                var rebels = world.Figures.Where(f => f.Team == "rebel").ToList();

                // asset store: curated profiles matched by card name
                var assetStore = new Dictionary<string, HeroThreatProfile>();
                foreach (var f in rebels)
                    if (baseProfiles.TryGetValue(f.Card.Name, out var p))
                        assetStore[f.Id] = p;

                // synthetic store: profiles bound in scenario figure order
                var synth = SyntheticProfiles(
                    Json.AsDict(LoadJson("hero_profiles.json")));
                var synthStore = new Dictionary<string, HeroThreatProfile>();
                for (int i = 0; i < rebels.Count && i < synth.Count; i++)
                    synthStore[rebels[i].Id] = synth[i];

                // binding sanity (mirrors the export's "bound" row)
                var bound = Json.AsDict(e["bound"]);
                Check("hero_w." + Json.AsStr(e["scenario"]) + ".bound_asset",
                      bound["asset"],
                      assetStore.Keys.OrderBy(k => k, StringComparer.Ordinal)
                                     .ToList<object>());
                Check("hero_w." + Json.AsStr(e["scenario"]) + ".bound_synth",
                      bound["synthetic"],
                      synth.Take(rebels.Count).Select(p => (object)p.Name).ToList());

                foreach (var varO in Json.AsList(e["variants"]).Select(Json.AsDict))
                {
                    var vr = varO;
                    string variant = Json.AsStr(vr["variant"]);
                    world.HeroProfiles = variant == "none" ? null
                        : variant == "synth" ? synthStore : assetStore;
                    Check("hero_w." + Json.AsStr(e["scenario"]) + "." + variant + ".w",
                          vr["w"], PostureWRows(world, vm));
                    if (vr["counter"] != null)
                        Check("hero_w." + Json.AsStr(e["scenario"]) + "." + variant + ".counter",
                              vr["counter"], CounterWRows(world, vm.Counter));
                    Check("hero_w." + Json.AsStr(e["scenario"]) + "." + variant + ".woundall",
                          vr["woundall"],
                          CounterWRows(world, new VictoryCounter("woundAll")));
                }
                world.HeroProfiles = null;
            }
        }

        static List<object> PostureWRows(World world, MissionVictoryModel vm)
        {
            var rows = new List<object>();
            foreach (var f in world.Figures)
            {
                if (f.Team != "imperial") continue;
                foreach (var r in world.Figures)
                {
                    if (r.Team != "rebel") continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        { "att", f.Id }, { "tgt", r.Id },
                        { "w", vm.PostureValue(world, f, r).Select(WVal).ToList() },
                    });
                }
            }
            return rows;
        }

        static List<object> CounterWRows(World world, VictoryCounter counter)
        {
            var rows = new List<object>();
            foreach (var f in world.Figures)
            {
                if (f.Team != "imperial") continue;
                foreach (var r in world.Figures)
                {
                    if (r.Team != "rebel") continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        { "att", f.Id }, { "tgt", r.Id },
                        { "w", counter.TargetValue(world, f, r).Select(WVal).ToList() },
                    });
                }
            }
            return rows;
        }
    }
}
