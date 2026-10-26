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

        static object _orderDecksRoot;
        static object _heroLethalityRoot;
        static bool _orderDecksFailed, _lethalityFailed;

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
