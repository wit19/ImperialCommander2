// M4 public data assets, loaded on the Unity side (concept 4.2 "DataStore":
// "GetDeckCardForRound() loads order_decks.json" + hero lethality priors).
// Both are PLAIN TEXT assets shipped in the build - the printed order cards
// and the curated lethality table are public, reviewable data, never hidden
// modifiers (fairness rule, concept section 2).
//
// Nearest faithful equivalent of the DataStore adapter rows: kept in its own
// TacticalAI file instead of DataStore.cs to keep the legacy file untouched
// (documented deviation in docs/PR_draft.md). Loading is LAZY and only ever
// happens through the TacticalAISettings-gated hooks.
using UnityEngine;
using Tac = TacticalAI.Core;

namespace TacticalAI
{
    public static class TacticalAIAssets
    {
        const string ORDER_DECKS_PATH = "TacticalAI/order_decks";
        const string HERO_LETHALITY_PATH = "Languages/En/tactical_hero_lethality";
        const string HERO_PROFILES_PATH = "Languages/En/tactical_hero_profiles";
        const string HERO_CARDS_PATH = "Languages/En/tactical_hero_cards";
        const string IMPERIAL_DECKS_PATH = "Languages/En/tactical_imperial_decks";
        const string IMPERIAL_CARD_EFFECTS_PATH = "Languages/En/tactical_imperial_card_effects";
        const string CAMPAIGN_ITEMS_PATH = "Languages/En/CampaignData/items";
        const string CAMPAIGN_SKILLS_PATH = "Languages/En/CampaignData/skills";

        static object _orderDecksRoot;
        static object _heroLethalityRoot;
        static object _heroProfilesRoot;
        static object _heroCardsRoot;
        static object _imperialDecksRoot;
        static object _imperialCardEffectsRoot;
        static object _campaignItemsRoot;
        static object _campaignSkillsRoot;
        static bool _orderDecksFailed, _lethalityFailed, _heroProfilesFailed,
                    _heroCardsFailed, _imperialDecksFailed, _imperialCardEffectsFailed,
                    _campaignItemsFailed, _campaignSkillsFailed;

        /// <summary>tacai.order_decks/1 root object (Core Json) or null when
        /// the asset is missing/unparsable - hooks then fall back to the
        /// legacy path with an error log.</summary>
        public static object OrderDecksRoot
        {
            get
            {
                if (_orderDecksRoot == null && !_orderDecksFailed)
                    _orderDecksRoot = LoadJson(ORDER_DECKS_PATH, ref _orderDecksFailed);
                return _orderDecksRoot;
            }
        }

        /// <summary>tacai.hero_lethality/1 root object or null.</summary>
        public static object HeroLethalityRoot
        {
            get
            {
                if (_heroLethalityRoot == null && !_lethalityFailed)
                    _heroLethalityRoot = LoadJson(HERO_LETHALITY_PATH, ref _lethalityFailed);
                return _heroLethalityRoot;
            }
        }

        /// <summary>M3.1 (concept 3.3.1a): tacai.hero_profiles/1 curated
        /// public threat table or null. Loads AND primes the Core doc cache
        /// (Tac.HeroProfiles.PrimeDoc) in one step: ApplyCampaignLoadouts
        /// reads that cache, and the Unity player has no other loader -
        /// priming here means every caller (bridge, cover-range) sees the
        /// same doc the harness loads from file (dev 30 playtest hotfix:
        /// unprimed doc threw 'no doc loaded' on every campaign click).</summary>
        public static object HeroProfilesRoot
        {
            get
            {
                if (_heroProfilesRoot == null && !_heroProfilesFailed)
                {
                    _heroProfilesRoot = LoadJson(HERO_PROFILES_PATH, ref _heroProfilesFailed);
                    if (_heroProfilesRoot is System.Collections.Generic.Dictionary<string, object> d)
                        Tac.HeroProfiles.PrimeDoc(d);
                }
                return _heroProfilesRoot;
            }
        }

        /// <summary>M11b: tacai.hero_cards/1 - the printed hero-card values
        /// of all 21 campaign heroes. Loads AND primes the Core cache (same
        /// seam and the same dev-30 lesson as HeroProfilesRoot: whoever
        /// touches the table first gets the doc, app and harness agree).</summary>
        public static object HeroCardsRoot
        {
            get
            {
                if (_heroCardsRoot == null && !_heroCardsFailed)
                {
                    _heroCardsRoot = LoadJson(HERO_CARDS_PATH, ref _heroCardsFailed);
                    if (_heroCardsRoot is System.Collections.Generic.Dictionary<string, object> d)
                        Tac.HeroCards.PrimeDoc(d);
                }
                return _heroCardsRoot;
            }
        }

        /// <summary>M11b printed card lookup; primes via HeroCardsRoot on
        /// first use, null when the asset is unavailable (rows then keep the
        /// curated fallback - the layer degrades, never throws).</summary>
        public static Tac.HeroCard CardFor(string heroName)
        {
            var root = HeroCardsRoot;    // prime the Core doc cache (dev-30 seam)
            return Tac.HeroCards.For(heroName);
        }

        /// <summary>M12b: tacai.imperial_decks/1 - the 12 Imperial class
        /// decks. Loads AND primes the Core cache (same seam as HeroCardsRoot:
        /// app and harness agree on the doc whoever touches it first).</summary>
        public static object ImperialDecksRoot
        {
            get
            {
                if (_imperialDecksRoot == null && !_imperialDecksFailed)
                {
                    _imperialDecksRoot = LoadJson(IMPERIAL_DECKS_PATH, ref _imperialDecksFailed);
                    if (_imperialDecksRoot is System.Collections.Generic.Dictionary<string, object> d)
                        Tac.ImperialDecks.PrimeDoc(d);
                }
                return _imperialDecksRoot;
            }
        }

        /// <summary>M12d: tacai.imperial_card_effects/1 - the classification
        /// table (every deck card -> bucket + hooks). Loads AND primes the
        /// Core cache (same seam as ImperialDecksRoot).</summary>
        public static object ImperialCardEffectsRoot
        {
            get
            {
                if (_imperialCardEffectsRoot == null && !_imperialCardEffectsFailed)
                {
                    _imperialCardEffectsRoot = LoadJson(IMPERIAL_CARD_EFFECTS_PATH,
                                                        ref _imperialCardEffectsFailed);
                    if (_imperialCardEffectsRoot is System.Collections.Generic.Dictionary<string, object> d)
                        Tac.ImperialCardEffects.PrimeDoc(d);
                }
                return _imperialCardEffectsRoot;
            }
        }

        /// <summary>App's OWN campaign tables (v3.11: the AI reads what the
        /// app manages - sagaCampaign/hero.campaignItems). Legacy JS export
        /// format: trailing commas must be stripped before Json.Parse.</summary>
        public static object CampaignItemsRoot
        {
            get
            {
                if (_campaignItemsRoot == null && !_campaignItemsFailed)
                    _campaignItemsRoot = LoadCampaignJson(CAMPAIGN_ITEMS_PATH, ref _campaignItemsFailed);
                return _campaignItemsRoot;
            }
        }

        /// <summary>App's OWN campaign skills table (xp-cost investment).</summary>
        public static object CampaignSkillsRoot
        {
            get
            {
                if (_campaignSkillsRoot == null && !_campaignSkillsFailed)
                    _campaignSkillsRoot = LoadCampaignJson(CAMPAIGN_SKILLS_PATH, ref _campaignSkillsFailed);
                return _campaignSkillsRoot;
            }
        }

        /// <summary>M3.1 store seam: builds {figure-or-hero id: profile}
        /// from the curated table + the campaign loadouts of ONE mission
        /// roster. Pure read of app-managed public data (no state, no
        /// hidden info - leak invariant per concept 3.3.1a). Returns null
        /// when the asset is unavailable -> callers keep the reference
        /// path (Default OFF everywhere).</summary>
        public static Tac.HeroThreatProfile ProfileFor(
            string heroName, Saga.CampaignHero hero)
        {
            var doc = HeroProfilesRoot as System.Collections.Generic.Dictionary<string, object>;
            if (doc == null) return null;
            var baseProfiles = Tac.HeroProfiles.ProfilesFromDoc(doc);
            if (!baseProfiles.TryGetValue(heroName ?? "", out var b)) return null;
            var items = CampaignItemsRoot as System.Collections.Generic.List<object>;
            var skills = CampaignSkillsRoot as System.Collections.Generic.List<object>;
            if (items == null || skills == null) return b;   // curated-only
            var loadout = new System.Collections.Generic.Dictionary<string, object>
            {
                { "hero", heroName },
                { "items", hero == null ? new System.Collections.Generic.List<object>()
                    : new System.Collections.Generic.List<object>(
                        hero.campaignItems.ConvertAll(i => (object)i.id)) },
                { "skills", hero == null ? new System.Collections.Generic.List<object>()
                    : new System.Collections.Generic.List<object>(
                        hero.campaignSkills.ConvertAll(s => (object)s.id)) },
            };
            var refs = new System.Collections.Generic.Dictionary<string, object>
            {
                { "items", items },
                { "skills", skills },
            };
            var merged = Tac.HeroProfiles.ApplyCampaignLoadouts(
                baseProfiles,
                new System.Collections.Generic.List<object> { loadout }, refs);
            return merged.TryGetValue(heroName, out var p) ? p : b;
        }

        static object LoadCampaignJson(string resourcePath, ref bool failedFlag)
        {
            var ta = Resources.Load<TextAsset>(resourcePath);
            if (ta == null)
            {
                Debug.LogError($"TacticalAIAssets::missing Resources/{resourcePath}.json");
                failedFlag = true;
                return null;
            }
            try
            {
                // legacy JS export: trailing commas before } / ] (the sim
                // strips them identically in load_reference_tables)
                var txt = System.Text.RegularExpressions.Regex.Replace(
                    ta.text, @",\s*([}\]])", "$1");
                return Tac.Json.Parse(txt);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TacticalAIAssets::cannot parse {resourcePath}: {e.Message}");
                failedFlag = true;
                return null;
            }
        }

        static object LoadJson(string resourcePath, ref bool failedFlag)
        {
            var ta = Resources.Load<TextAsset>(resourcePath);
            if (ta == null)
            {
                Debug.LogError($"TacticalAIAssets::missing Resources/{resourcePath}.json");
                failedFlag = true;
                return null;
            }
            try
            {
                return Tac.Json.Parse(ta.text);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TacticalAIAssets::cannot parse {resourcePath}: {e.Message}");
                failedFlag = true;
                return null;
            }
        }
    }
}
