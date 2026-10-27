// Port of sim/hero_profiles.py — HeroThreatProfile (concept v3.10/v3.11
// §3.3.1a, M2a.4 Python / M3.1 C#). Curated, PUBLIC hero threat table:
// everything the profile knows is printed on the physical cards or managed
// by the app itself (sagaCampaign/hero.campaignItems) — a human imperial
// player reads exactly the same sheets. The leak invariant stands: the
// profile hangs on the hero IDENTITY, never on hidden state or sight.
//
// Asset: Assets/Resources/Languages/En/tactical_hero_profiles.json
// (schema tacai.hero_profiles/1, same bytes as the sim asset; fairness
// disclaimer travels inside the file). Golden vectors:
// Assets/Tests/Fixtures/tacai/hero_profiles.json + hero_w.json + the
// profile_cases grid in risk.json.
//
// Reference tables (items/skills/heroes) are the app's OWN CampaignData
// files (Languages/<Lang>/CampaignData/{items,skills}.json +
// CardData/heroes.json); only ids count, the language path is swappable
// (Pflichttest 11). The C# loader is a pure function over the parsed docs;
// the Unity adapter (TacticalAIAssets / TacticalAIBridge) hands them in.
//
// Default OFF everywhere: with no store on the World every W tuple stays
// byte-identical to the M2a.1/M2a.2 pins (ProfileFocusKey returns []).
using System;
using System.Collections.Generic;
using System.Linq;

namespace TacticalAI.Core
{
    /// One curated hero profile. Field names follow the asset keys
    /// (evDamagePerRound etc.) exactly; Fractions are Rationals.
    public sealed class HeroThreatProfile
    {
        public string Name;
        public string Role;                 // melee | ranged | support | healer
        public Rational EvDamagePerRound;   // inkl. Loadout-Beitraege
        public int RangeMax;                // Felder (Acc-Gate-Obergrenze)
        public bool IgnoreLos = false;
        public bool SelfHealPossible = false;
        public int Mobility = 2;            // 1 langsam | 2 normal | 3 schnell
        public List<string> Flags = new List<string>();
        public string SourceId = null;
        public string AppHeroId = null;
        public string Knowledge = "kuratiert";   // kuratiert | bestaetigt
        public Rational EquipmentIndex = Rational.Zero;  // tier x cost typ-gewichtet
        public int AbilityIndex = 0;                   // Summe der xpCost
        public List<string> LoadoutItems = new List<string>();
        public List<string> LoadoutSkills = new List<string>();

        /// §3.3.1a Laeuferwert protect/stall: mobility x rangeMax (exakt).
        public int RunnerValue => Mobility * RangeMax;

        /// woundAll-Chip: Nicht-Heiler/Support vor Heiler, dann
        /// mobility x rangeMax, dann evDamagePerRound.
        /// [int (0 healer | 1 non-healer), int runner, Rational ev]
        public List<object> ChipKey()
        {
            return new List<object>
            {
                // FINAL-QA audit #5: SUPPORT ranks first among NON-healers
                // (concept 3.3.1a chip_key); healer still leads overall.
                Role == "healer" ? 0 : Role == "support" ? 2 : 1,
                RunnerValue,
                EvDamagePerRound,
            };
        }

        /// Python as_dict() mirror (str(Fraction) fields as ToString()).
        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "name", Name },
                { "role", Role },
                { "evDamagePerRound", EvDamagePerRound.ToString() },
                { "rangeMax", RangeMax },
                { "ignoreLos", IgnoreLos },
                { "selfHealPossible", SelfHealPossible },
                { "mobility", Mobility },
                { "flags", new List<string>(Flags) },
                { "knowledge", Knowledge },
                { "equipment_index", EquipmentIndex.ToString() },
                { "ability_index", AbilityIndex },
                { "loadout_items", new List<string>(LoadoutItems) },
                { "loadout_skills", new List<string>(LoadoutSkills) },
            };
        }

        /// Vector-row shape used by the M3.1 golden vectors: as_dict +
        /// runner_value + chip_key + woundAll focus_key probed against the
        /// synthetic store world the harness passes in (mirrors the sim
        /// export: profiles outside that store get an EMPTY focus_key).
        public Dictionary<string, object> VectorRow(World probeWorld)
        {
            var d = AsDict();
            d["runner_value"] = RunnerValue;
            d["chip_key"] = ChipKey();
            d["focus_key"] = HeroProfiles.ProfileFocusKey(probeWorld, ProbeFig(this),
                                                          "eliminate", "woundAll");
            return d;
        }

        /// dataclasses.replace(base, ...) mirror — clone with overrides.
        public HeroThreatProfile Clone(Func<HeroThreatProfile, HeroThreatProfile> edit)
        {
            var c = new HeroThreatProfile
            {
                Name = Name, Role = Role, EvDamagePerRound = EvDamagePerRound,
                RangeMax = RangeMax, IgnoreLos = IgnoreLos,
                SelfHealPossible = SelfHealPossible, Mobility = Mobility,
                Flags = new List<string>(Flags), SourceId = SourceId,
                AppHeroId = AppHeroId, Knowledge = Knowledge,
                EquipmentIndex = EquipmentIndex, AbilityIndex = AbilityIndex,
                LoadoutItems = new List<string>(LoadoutItems),
                LoadoutSkills = new List<string>(LoadoutSkills),
            };
            return edit(c);
        }

        static Figure ProbeFig(HeroThreatProfile p)
        {
            // ProfileFocusKey reads store[target.Id] only — the probe
            // figure carries just the id (mirrors the sim's _FakeFig).
            return new Figure { Id = p.Name };
        }
    }

    /// Asset-level access: curated doc + focus-key + cover rule + loadouts.
    public static class HeroProfiles
    {
        /// Asset fallback when no document was loaded (M2a.4 curation).
        public const int DefaultCoverShelterMaxRange = 5;

        // doc cache (RiskPosture checks the shelter range per activation —
        // file I/O once per process; mirrors the sim's _DOC_CACHE)
        static Dictionary<string, object> _docCache;

        public static Dictionary<string, object> LoadDoc(string path)
        {
            var root = Json.AsDict(Json.Parse(System.IO.File.ReadAllText(path)));
            _docCache = root;
            return root;
        }

        /// Prime from an already-parsed root (Unity Resources path).
        public static void PrimeDoc(Dictionary<string, object> doc)
        {
            _docCache = doc;
        }

        /// null when no doc is loaded -> callers use the curated default.
        public static Dictionary<string, object> DocOrNull() => _docCache;

        public static int CoverShelterMaxRange(Dictionary<string, object> doc = null)
        {
            doc = doc ?? _docCache;
            if (doc != null && doc.ContainsKey("cover_shelter_max_range"))
                return Json.AsInt(doc["cover_shelter_max_range"]);
            return DefaultCoverShelterMaxRange;
        }

        /// name -> profile from the curated table (Python profiles_from_doc;
        /// null ev = neutral 0 -> basis-prior-neutral stays visible).
        public static Dictionary<string, HeroThreatProfile> ProfilesFromDoc(
            Dictionary<string, object> doc)
        {
            var outp = new Dictionary<string, HeroThreatProfile>();
            foreach (var hO in Json.AsList(doc["heroes"]))
            {
                var h = Json.AsDict(hO);
                outp[Json.AsStr(h["name"])] = ProfileFromRow(h);
            }
            return outp;
        }

        /// profiles_by_app_id mirror (campaign loadouts resolve by H-Id).
        public static Dictionary<string, HeroThreatProfile> ProfilesByAppId(
            IDictionary<string, HeroThreatProfile> base_)
        {
            var outp = new Dictionary<string, HeroThreatProfile>();
            foreach (var kv in base_)
                if (!string.IsNullOrEmpty(kv.Value.AppHeroId))
                    outp[kv.Value.AppHeroId] = kv.Value;
            return outp;
        }

        /// Neutral curated-prior basis values (asset null = the hero's
        /// printed dice are uncurated; flag basis-prior-neutral).
        public static readonly Rational NeutralEv = Rational.One;   // Fraction(1)
        public const int NeutralRange = 5;
        public const int NeutralMobility = 2;

        static HeroThreatProfile ProfileFromRow(Dictionary<string, object> h)
        {
            var ev = Json.Get(h, "evDamagePerRound", null);
            var rangeMax = Json.Get(h, "rangeMax", null);
            var mobility = Json.Get(h, "mobility", null);
            return new HeroThreatProfile
            {
                Name = Json.AsStr(h["name"]),
                Role = Json.AsStr(Json.Get(h, "role", "melee")),
                EvDamagePerRound = ev == null ? NeutralEv : ParseFrac(ev),
                RangeMax = rangeMax == null ? NeutralRange : Json.AsInt(rangeMax),
                IgnoreLos = AsBoolX(Json.Get(h, "ignoreLos", false)),
                SelfHealPossible = AsBoolX(Json.Get(h, "selfHealPossible", false)),
                Mobility = mobility == null ? NeutralMobility : Json.AsInt(mobility),
                Flags = StrList(Json.Get(h, "flags", null)),
                SourceId = Json.AsStr(Json.Get(h, "source_id", null)),
                AppHeroId = Json.AsStr(Json.Get(h, "app_hero_id", null)),
            };
        }

        /// Python _frac: str "p/q"|"p" or number.
        public static Rational ParseFrac(object v)
        {
            if (v is Rational r) return r;
            if (v is string s) return Rational.Parse(s);
            if (v is long l) return new Rational(l);
            if (v is double d)
                return Rational.Parse(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Rational.Zero;
        }

        static bool AsBoolX(object v) => v is bool b ? b : false;

        // ------------------------------------------------------- focus key
        /// §3.3.1a profile component of the target value W (Python
        /// profile_for + profile_focus_key, branch order verbatim). EMPTY
        /// when world.HeroProfiles has no entry for the target — every W
        /// tuple then stays byte-identical to the M2a.1/M2a.2 pins.
        /// Embedding rule: "posture weight x profile" is lexicographic —
        /// the profile weights WITHIN the counter/kill/arrival semantics,
        /// it never invents a target (victory.py inserts the component
        /// BEHIND the leading values, never before).
        public static List<object> ProfileFocusKey(World world, Figure target,
                                                   string posture, string counter)
        {
            var p = ProfileFor(world, target);
            if (p == null) return new List<object>();
            if (counter == "woundAll") return p.ChipKey();   // healthy-first leads
            if (posture == "protect" || posture == "stall")
                return new List<object> { p.RunnerValue };   // first on target
            if (posture == "attrition" || posture == "eliminate")
                // attrition race: evDamagePerRound hoch zuerst (Eliminate-
                // Jagd = Attritionsfall)
                return new List<object> { p.EvDamagePerRound };
            return new List<object>();                       // deny: node rules
        }

        /// Welt-Seam: world.HeroProfiles = {figure_id: profile}; null ohne
        /// Store/Eintrag (Referenzpfad bleibt byte-identisch).
        public static HeroThreatProfile ProfileFor(World world, Figure target)
        {
            var store = world == null ? null : world.HeroProfiles;
            if (store == null || store.Count == 0) return null;
            if (target == null) return null;
            return store.TryGetValue(target.Id ?? "", out var p) ? p : null;
        }

        // --------------------------------------------------- disengage rule
        /// disengage-Nutzen = 0 gegen ignoreLos / grosse rangeMax
        /// (Pflichttest 3/4): Deckungsspiel wirkt nur gegen Gegner, deren
        /// Reichweite unter der kuratierten Deckungsreichweite
        /// (cover_shelter_max_range) liegt und die LOS-Regeln achten.
        public static bool CoverIsWorthless(bool ignoreLos, int threatRangeMax,
                                            Dictionary<string, object> doc = null)
        {
            if (ignoreLos) return true;
            var limit = CoverShelterMaxRange(doc);
            return threatRangeMax != 0 && threatRangeMax > limit;
        }

        // ----------------------------------------------------- inference
        /// Letalitaetsprior ERSETZEN durch Profil (§3.3.1a): Reichweiten-
        /// ringe [1, rangeMax] + Gewichte aus dem Profil statt des groben
        /// Kurations-Priors.
        public static (Dictionary<string, List<int>> Ranges,
                       Dictionary<string, Rational> Lethal)
            ProfilesToInference(IDictionary<string, HeroThreatProfile> store)
        {
            var ranges = new Dictionary<string, List<int>>();
            var lethal = new Dictionary<string, Rational>();
            foreach (var kv in store)
            {
                ranges[kv.Key] = new List<int> { 1, kv.Value.RangeMax };
                lethal[kv.Key] = kv.Value.EvDamagePerRound;
            }
            return (ranges, lethal);
        }

        // ------------------------------------------------ campaign loadouts
        /// v3.11: the app KNOWS the loadouts (sagaCampaign.campaignItems,
        /// hero.campaignItems/campaignSkills; skill purchase spends
        /// skill.cost from xpAmount). Fixture-format entries (mirrors
        /// fixtures/campaign_loadouts.json, schema tacai.campaign_loadouts/1):
        ///   { "hero": name?, "hero_id": H-Id?, "items": [ids], "skills": [ids] }
        /// refs: ORIGINAL app tables — refs["items"]/["skills"] id->row,
        /// optional refs["heroes"] (CardData/heroes.json, id->row).
        /// Scoring per Thomas' proxies (apply_loadout verbatim): equipment
        /// tier x cost typ-gewichtet (P/S/O-R table in the asset), abilities
        /// xp-cost sum, EV = basis + weapon contribution + xp contribution;
        /// flags teure-faehigkeit (xpCost >= special_xp_cost) and
        /// lastout-unvollstaendig (unknown ids); support item -> selfHeal;
        /// knowledge flips to "bestaetigt". Entries whose hero neither by
        /// name nor by app-H-Id resolves are SKIPPED (deterministic order).
        public static Dictionary<string, HeroThreatProfile> ApplyCampaignLoadouts(
            Dictionary<string, HeroThreatProfile> base_,
            List<object> loadouts,
            Dictionary<string, object> refs)
        {
            var doc = _docCache
                ?? throw new InvalidOperationException(
                    "HeroProfiles.ApplyCampaignLoadouts: no doc loaded");
            var byId = ProfilesByAppId(base_);
            var outp = new Dictionary<string, HeroThreatProfile>(base_);

            // Python: sorted(loadouts, key=lambda e: str(e.get("hero")
            //                                   or e.get("hero_id")))  — None
            // stringifies to "None"; Python sort is stable, .NET OrderBy is too.
            var entries = loadouts.ConvertAll(o => Json.AsDict(o));
            string SortKey(Dictionary<string, object> e)
            {
                var hero = Json.AsStr(Json.Get(e, "hero", null));
                if (!string.IsNullOrEmpty(hero)) return hero;
                var hid = Json.AsStr(Json.Get(e, "hero_id", null));
                if (!string.IsNullOrEmpty(hid)) return hid;
                return "None";                       // str(None)
            }
            foreach (var entry in entries.OrderBy(SortKey, StringComparer.Ordinal))
            {
                string name = Json.AsStr(Json.Get(entry, "hero", null));
                string hid = Json.AsStr(Json.Get(entry, "hero_id", null));
                HeroThreatProfile target;
                if (!string.IsNullOrEmpty(name))
                    target = base_.ContainsKey(name) ? base_[name] : null;
                else
                    target = byId.ContainsKey(hid ?? "") ? byId[hid] : null;
                if (target == null) continue;
                string key = (name != null && !string.IsNullOrEmpty(name))
                    ? name : target.Name;

                var prof = ApplyLoadout(target,
                    Json.Get(entry, "items", null),
                    Json.Get(entry, "skills", null), refs, doc);
                outp[key] = prof;
            }
            return outp;
        }

        /// Python apply_loadout verbatim (additive on the base profile).
        public static HeroThreatProfile ApplyLoadout(
            HeroThreatProfile base_, object itemIdsRaw, object skillIdsRaw,
            Dictionary<string, object> refs, Dictionary<string, object> doc)
        {
            var typeTable = Json.AsDict(doc["type_table"]);
            var weightsRaw = Json.AsDict(doc["type_weights"]);
            var weights = new Dictionary<string, Rational>();
            foreach (var kv in weightsRaw) weights[kv.Key] = ParseFrac(kv.Value);
            var evModel = Json.AsDict(doc["ev_model"]);
            var weaponEvK = ParseFrac(evModel["weapon_ev_per_1000_cost"]);
            var xpEvK = ParseFrac(evModel["xp_point_ev"]);
            int specialXp = Json.AsInt(evModel["special_xp_cost"]);
            bool supportSelfHeal = AsBoolX(
                Json.Get(evModel, "support_item_sets_self_heal", false));

            var itemsIdx = IdIndex(refs, "items");
            var skillsIdx = IdIndex(refs, "skills");

            Rational equip = Rational.Zero;
            Rational weaponEv = Rational.Zero;
            bool hasSupportItem = false;
            bool unknownAny = false;
            foreach (var iid in SortedStrs(itemIdsRaw))
            {
                if (!itemsIdx.TryGetValue(iid, out var raw)) { unknownAny = true; continue; }
                var it = Json.AsDict(raw);
                int tier = Json.AsInt(Json.Get(it, "tier", (long)1));
                int cost = Json.AsInt(Json.Get(it, "cost", (long)0));
                string type = Json.AsStr(Json.Get(it, "type", null));
                string cls = type != null && typeTable.ContainsKey(type)
                    ? Json.AsStr(typeTable[type]) : null;
                var w = cls != null && weights.ContainsKey(cls) ? weights[cls] : Rational.Zero;
                equip += w * new Rational((long)tier * cost);
                if (cls != null && cls.StartsWith("weapon", StringComparison.Ordinal))
                    weaponEv += weaponEvK * new Rational((long)tier * cost);
                else if (cls == "support") hasSupportItem = true;
            }

            long xpSum = 0;
            bool special = false;
            foreach (var sid in SortedStrs(skillIdsRaw))
            {
                if (!skillsIdx.TryGetValue(sid, out var raw)) { unknownAny = true; continue; }
                var sk = Json.AsDict(raw);
                long cost = Json.AsInt(Json.Get(sk, "cost", (long)0));
                xpSum += cost;
                if (cost >= specialXp) special = true;
            }

            return base_.Clone(p =>
            {
                p.EvDamagePerRound = base_.EvDamagePerRound + weaponEv + xpEvK * new Rational(xpSum);
                p.SelfHealPossible = base_.SelfHealPossible
                    || (supportSelfHeal && hasSupportItem);
                var flags = new List<string>(base_.Flags);
                if (special && !flags.Contains("teure-faehigkeit"))
                    flags.Add("teure-faehigkeit");
                if (unknownAny && !flags.Contains("lastout-unvollstaendig"))
                    flags.Add("lastout-unvollstaendig");
                p.Flags = flags;
                p.Knowledge = "bestaetigt";        // Lastout = App-Kampagnen-State
                p.EquipmentIndex = equip;
                p.AbilityIndex = (int)xpSum;
                p.LoadoutItems = SortedStrs(itemIdsRaw);
                p.LoadoutSkills = SortedStrs(skillIdsRaw);
                return p;
            });
        }

        /// load_campaign_loadouts(path)["loadouts"] seam for callers that
        /// hold the raw fixture doc.
        public static List<object> LoadoutRows(Dictionary<string, object> fixtureDoc)
            => Json.AsList(fixtureDoc["loadouts"]);

        // ---------------------------------------------------------- helpers
        static Dictionary<string, object> IdIndex(Dictionary<string, object> refs, string key)
        {
            var outp = new Dictionary<string, object>();
            if (refs == null || !refs.ContainsKey(key)) return outp;
            var doc = refs[key];
            if (doc is List<object> rows)
            {
                foreach (var rowO in rows)
                {
                    var row = Json.AsDict(rowO);
                    if (row.ContainsKey("id")) outp[Json.AsStr(row["id"])] = rowO;
                }
            }
            else
            {
                // already id-indexed map form (load_reference_tables shape)
                foreach (var kv in Json.AsDict(doc)) outp[kv.Key] = kv.Value;
            }
            return outp;
        }

        static List<string> SortedStrs(object v)
        {
            if (v == null) return new List<string>();
            return Json.AsList(v).ConvertAll(o => Json.AsStr(o))
                       .OrderBy(s => s, StringComparer.Ordinal).ToList();
        }

        static List<string> StrList(object v)
        {
            if (v == null) return new List<string>();
            return Json.AsList(v).ConvertAll(o => Json.AsStr(o));
        }
    }
}
