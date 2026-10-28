// M11b hero threat v2 (concept wiki_data/CONCEPT_tactical_ai.md section 2,
// board task M11b). The curated asset tactical_hero_cards.json carries the
// PRINTED hero-card values of all 21 campaign heroes - health/endurance/
// speed/defense/attributes healthy AND wounded (the real card back), the
// printed base weapon die pool, the acc-gate range convention and the
// ability/xp/strain numbers (numbers + keyword slugs only: the asset is
// text-free and language-independent; curation tool + cleanroom policy:
// wiki_data/tools/curate_hero_cards.py, wiki_data/LICENSE_DATA.md).
//
// Threat v2 replaces the M2a.4 curation basis in the ORD focus ranking:
//   * eliminate/attrition: expected damage of the hero's REAL dice pool
//     (base weapon, or an owned weapon/attribute-pool item's pool) plus the
//     items' static printed bonuses, against the DEFENSE POOL OF THE
//     IMPERIAL FIGURE BEING TARGETED, through the frozen pairing/hit
//     formula (Dice.ExpectedDamage - the same hit formula the imperial
//     attack model runs; engagement distance = 1, the worst case for the
//     Empire, identical for every candidate - no positions are read).
//   * protect/stall: runner value = mobility tier x range from the REAL
//     printed tempo/range (wounded heroes run on the real wounded speed).
//   * the wounded penalty is therefore REAL card-back data (tempo -1,
//     shrinking attribute dice for attribute-pool weapons), not a prior.
// Chip class and role vocabulary stay exactly as before (M2a.4 table);
// the ability numbers in the asset are consumed in M11d (strain budget).
using System;
using System.Collections.Generic;
using System.Linq;

namespace TacticalAI.Core
{
    /// <summary>One printed hero card (all numbers are printed card values;
    /// xp costs come from the app's own CampaignData/skills.json).</summary>
    public sealed class HeroCard
    {
        public string Name;
        public string AppHeroId;               // H1..H21 (CardData/heroes.json)
        public int GesUnderhalt;               // printed health (healthy side)
        public int GesundeAusdauer;
        public int VerwundetesGesundheit;
        public int VerwundeteAusdauer;
        public int Tempo;                      // printed speed
        public int TempoVerwundet;
        public string Verteidigung;            // "K" black / "W" white
        public string VerteidigungVerwundet;
        public string Staerke;                 // attribute dice letters (B/R/G/Y)
        public string Einsicht;
        public string Tech;
        public string StaerkeVerwundet;        // card back: dice shrink (M11b
        public string EinsichtVerwundet;       // real wound penalty for
        public string TechVerwundet;           // attribute-pool weapons)
        public string AngriffsTyp;             // "P" melee | "O" ranged
        public string BasisAngriffswuerfel;    // e.g. "BR", "GY+Reach"
        public int Reichweite;                 // acc-gate convention (asset doc)
        public List<HeroAbility> Faehigkeiten = new List<HeroAbility>();

        public bool Nahkaempfer { get { return AngriffsTyp == "P"; } }
        public bool HatReach { get { return BasisAngriffswuerfel != null &&
                BasisAngriffswuerfel.EndsWith("+Reach", StringComparison.Ordinal); } }

        /// <summary>The printed base-weapon die letters (Reach suffix stripped).</summary>
        public List<string> BasisWuerfel
        {
            get { return Letters(BasisAngriffswuerfel); }
        }

        /// <summary>Attribute dice for one attribute slug in the CURRENT
        /// (healthy/wounded) state - null-safe.</summary>
        public List<string> AttributWuerfel(string attribut, bool wounded)
        {
            string v;
            switch (attribut)
            {
                case "staerke": v = wounded ? StaerkeVerwundet : Staerke; break;
                case "einsicht": v = wounded ? EinsichtVerwundet : Einsicht; break;
                case "tech": v = wounded ? TechVerwundet : Tech; break;
                default: return null;
            }
            return Letters(v);
        }

        static readonly Dictionary<string, string> LetterToColor =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "B", "Blue" }, { "R", "Red" }, { "G", "Green" }, { "Y", "Yellow" },
            };

        /// <summary>Asset die letters (B/R/G/Y) -> Core die colour names
        /// (Dice.AttackDieFaces keys). Unknown letters are dropped.</summary>
        internal static List<string> Letters(string s)
        {
            var outp = new List<string>();
            if (string.IsNullOrEmpty(s)) return outp;
            var core = s;
            var plus = core.IndexOf('+');
            if (plus >= 0) core = core.Substring(0, plus);
            foreach (var c in core)
                if (LetterToColor.TryGetValue(c.ToString(), out var color)) outp.Add(color);
            return outp;
        }
    }

    public sealed class HeroAbility
    {
        public string Bezeichnung;             // pure keyword slug (no prose)
        public int? XpKosten;                  // app skills.json cost
        public int? StrainKosten;              // printed strain on the card
    }

    /// <summary>One campaign item's printed ATTACK-relevant effect
    /// (weapon dice / attribute-pool / static accuracy, damage, pierce).
    /// Surge and activated lines stay out (conservative, cf. Dice.cs).</summary>
    public sealed class ItemEffect
    {
        public string Id;                      // app item id (item101..)
        public string Typ;                     // "O" ranged | "P" melee
        public List<string> Wuerfel = new List<string>();
        public string Attribut;                // "staerke"|"einsicht"|"tech"|null
        public int Genauigkeit;
        public int Schaden;
        public int Durchschlag;

        public bool HatAngriffswirkung
        {
            get { return Wuerfel.Count > 0 || Attribut != null ||
                        Genauigkeit != 0 || Schaden != 0 || Durchschlag != 0; }
        }
    }

    /// <summary>Loaded tactical_hero_cards/1 asset.</summary>
    public sealed class HeroCardTable
    {
        readonly Dictionary<string, HeroCard> _byName =
            new Dictionary<string, HeroCard>(StringComparer.Ordinal);
        readonly Dictionary<string, ItemEffect> _items =
            new Dictionary<string, ItemEffect>(StringComparer.Ordinal);

        public int Count { get { return _byName.Count; } }
        public HeroCard For(string name)
        {
            HeroCard c;
            return name != null && _byName.TryGetValue(name, out c) ? c : null;
        }
        public ItemEffect Item(string id)
        {
            ItemEffect e;
            return id != null && _items.TryGetValue(id, out e) ? e : null;
        }

        public static HeroCardTable FromDoc(object root)
        {
            var t = new HeroCardTable();
            var doc = root as Dictionary<string, object>;
            if (doc == null) return t;
            foreach (var raw in Json.AsList(Json.Get(doc, "heroes", new List<object>())))
            {
                var r = Json.AsDict(raw);
                var c = new HeroCard
                {
                    Name = Json.AsStr(r["name"]),
                    AppHeroId = Json.AsStr(Json.Get(r, "appHeroId", null)),
                    GesUnderhalt = Json.AsInt(Json.Get(r, "gesUnderhalt", 0)),
                    GesundeAusdauer = Json.AsInt(Json.Get(r, "gesundeAusdauer", 0)),
                    VerwundetesGesundheit = Json.AsInt(Json.Get(r, "verwundetesGesundheit", 0)),
                    VerwundeteAusdauer = Json.AsInt(Json.Get(r, "verwundeteAusdauer", 0)),
                    Tempo = Json.AsInt(Json.Get(r, "tempo", 0)),
                    TempoVerwundet = Json.AsInt(Json.Get(r, "tempoVerwundet", 0)),
                    Verteidigung = Json.AsStr(Json.Get(r, "verteidigung", null)),
                    VerteidigungVerwundet = Json.AsStr(Json.Get(r, "verteidigungVerwundet", null)),
                    AngriffsTyp = Json.AsStr(Json.Get(r, "angriffsTyp", "O")),
                    BasisAngriffswuerfel = Json.AsStr(Json.Get(r, "basisAngriffswuerfel", "")),
                    Reichweite = Json.AsInt(Json.Get(r, "reichweite", 0)),
                };
                var att = Json.Get(r, "attribute", null);
                if (att != null)
                {
                    var a = Json.AsDict(att);
                    c.Staerke = Json.AsStr(Json.Get(a, "staerke", ""));
                    c.Einsicht = Json.AsStr(Json.Get(a, "einsicht", ""));
                    c.Tech = Json.AsStr(Json.Get(a, "tech", ""));
                }
                var attv = Json.Get(r, "attributeVerwundet", null);
                if (attv != null)
                {
                    var a = Json.AsDict(attv);
                    c.StaerkeVerwundet = Json.AsStr(Json.Get(a, "staerke", ""));
                    c.EinsichtVerwundet = Json.AsStr(Json.Get(a, "einsicht", ""));
                    c.TechVerwundet = Json.AsStr(Json.Get(a, "tech", ""));
                }
                foreach (var rawA in Json.AsList(Json.Get(r, "faehigkeiten", new List<object>())))
                {
                    var ad = Json.AsDict(rawA);
                    c.Faehigkeiten.Add(new HeroAbility
                    {
                        Bezeichnung = Json.AsStr(Json.Get(ad, "bezeichnung", "")),
                        XpKosten = ad.ContainsKey("xp_kosten") && ad["xp_kosten"] != null
                            ? (int?)Json.AsInt(ad["xp_kosten"]) : null,
                        StrainKosten = ad.ContainsKey("strain_kosten") && ad["strain_kosten"] != null
                            ? (int?)Json.AsInt(ad["strain_kosten"]) : null,
                    });
                }
                if (c.Faehigkeiten.Count > 0)
                    c.Faehigkeiten = c.Faehigkeiten
                        .OrderBy(f => f.Bezeichnung, StringComparer.Ordinal).ToList();
                if (!string.IsNullOrEmpty(c.Name)) t._byName[c.Name] = c;
            }
            foreach (var raw in Json.AsList(Json.Get(doc, "items", new List<object>())))
            {
                var r = Json.AsDict(raw);
                var e = new ItemEffect
                {
                    Id = Json.AsStr(Json.Get(r, "id", null)),
                    Typ = Json.AsStr(Json.Get(r, "typ", "O")),
                    Attribut = Json.AsStr(Json.Get(r, "attribut", null)),
                    Genauigkeit = Json.AsInt(Json.Get(r, "genauigkeit", 0)),
                    Schaden = Json.AsInt(Json.Get(r, "schaden", 0)),
                    Durchschlag = Json.AsInt(Json.Get(r, "durchschlag", 0)),
                };
                var w = Json.AsStr(Json.Get(r, "wuerfel", ""));
                e.Wuerfel = HeroCard.Letters(w);
                if (!string.IsNullOrEmpty(e.Id) && e.HatAngriffswirkung) t._items[e.Id] = e;
            }
            return t;
        }
    }

    /// <summary>Doc cache with the same seam as HeroProfiles: the harness and
    /// the live gate PRIME from file; the app loader (TacticalAIAssets)
    /// primes from Resources - whoever runs first wins deterministically.</summary>
    public static class HeroCards
    {
        static Dictionary<string, object> _docCache;
        static HeroCardTable _table;

        public static void PrimeDoc(Dictionary<string, object> doc)
        {
            _docCache = doc;
            _table = null;
        }

        public static Dictionary<string, object> DocOrNull() => _docCache;

        public static HeroCardTable Load()
        {
            if (_table == null) _table = HeroCardTable.FromDoc(_docCache);
            return _table;
        }

        public static HeroCard For(string name) => Load().For(name);
    }

    /// <summary>Hero threat v2 (see file header). Pure function - no state,
    /// no positions, exact rationals.</summary>
    public static class HeroThreatModelV2
    {
        static readonly List<string> NoItems = new List<string>();

        /// <summary>Printed-threat value of one hero in one race, or null
        /// when the posture is not a threat race. card null -> caller falls
        /// back to the M2a.4 curation table (Han/Wookiee sim rows).</summary>
        public static Rational? Threat(HeroCard card, HeroCardTable table,
                                       HeroThreatProfile profile, bool wounded,
                                       string posture, IList<string> defenderDefense)
        {
            if (card == null) return null;
            if (posture == "eliminate" || posture == "attrition")
                return EliminateEv(card, table, profile, wounded, defenderDefense);
            if (posture == "protect" || posture == "stall")
                return RunnerValue(card, table, profile, wounded);
            return null;
        }

        // ------------------------------------------------------- damage race
        static Rational EliminateEv(HeroCard card, HeroCardTable table,
                                    HeroThreatProfile profile, bool wounded,
                                    IList<string> defenderDefense)
        {
            var owned = profile != null && profile.LoadoutItems != null
                ? profile.LoadoutItems : NoItems;
            int pierce = 0;
            var keywords = new List<string>();
            var pools = new List<Tuple<List<string>, bool>>
            {
                Tuple.Create(card.BasisWuerfel, !card.Nahkaempfer),
            };
            foreach (var id in owned)
            {
                var e = table != null ? table.Item(id) : null;
                if (e == null) continue;
                pierce += e.Durchschlag;
                if (e.Genauigkeit > 0) keywords.Add("+" + e.Genauigkeit + " Accuracy");
                if (e.Schaden > 0) keywords.Add("+" + e.Schaden + " {H}");
                bool ranged = e.Typ == "O";
                if (e.Wuerfel.Count > 0)
                    pools.Add(Tuple.Create(new List<string>(e.Wuerfel), ranged));
                if (e.Attribut != null)
                {
                    var attr = card.AttributWuerfel(e.Attribut, wounded);
                    if (attr != null && attr.Count > 0)
                        pools.Add(Tuple.Create(attr, ranged));
                }
            }
            var defender = Dice.BuildAttackStats(null, null, false, null,
                defenderDefense != null ? new List<string>(defenderDefense) : null);
            // the overlord assumes the hero fights with its BEST known
            // printed weapon (rules-true: one weapon per attack, and the
            // Empire may never under-estimate an equipped hero - same
            // direction as the M10a investment tie-break)
            var best = Rational.Zero;
            foreach (var p in pools)
            {
                var stats = Dice.BuildAttackStats(p.Item1, keywords, p.Item2, null,
                    defenderDefense != null ? new List<string>(defenderDefense) : null);
                var ev = Dice.ExpectedDamage(stats, defender, 1, pierce);
                if (ev > best) best = ev;
            }
            return best;
        }

        // ------------------------------------------------------- runner race
        static Rational RunnerValue(HeroCard card, HeroCardTable table,
                                    HeroThreatProfile profile, bool wounded)
        {
            int tier = MobilityTier(wounded ? card.TempoVerwundet : card.Tempo);
            int range = card.Reichweite;
            var owned = profile != null && profile.LoadoutItems != null
                ? profile.LoadoutItems : NoItems;
            foreach (var id in owned)
            {
                var e = table != null ? table.Item(id) : null;
                if (e == null) continue;
                var chars = e.Wuerfel.Count > 0 ? e.Wuerfel
                          : e.Attribut != null ? card.AttributWuerfel(e.Attribut, wounded)
                          : null;
                if (chars == null || chars.Count == 0) continue;
                if (e.Typ != "O") continue;   // melee items never extend the
                var stats = Dice.BuildAttackStats(chars,   // runner's reach
                    e.Genauigkeit > 0 ? new List<string> { "+" + e.Genauigkeit + " Accuracy" } : null,
                    true, null, null);
                if (stats.AccGate > range) range = stats.AccGate;
            }
            return new Rational((long)tier * range);
        }

        /// <summary>Mobility bands exactly as tactical_hero_profiles/1
        /// band_semantics: 1 = speed <= 3, 2 = speed 4, 3 = speed >= 5.</summary>
        public static int MobilityTier(int tempo)
        {
            return tempo <= 3 ? 1 : tempo == 4 ? 2 : 3;
        }
    }
}
