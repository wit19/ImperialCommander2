// M11d: the hero's printed STRAIN BUFFER and the focus ranking's light
// pressure signal when the table has piled markers onto it (concept
// CONCEPT_tactical_ai.md M11d; Thomas: the ranking considers it "lightly",
// never as a primary signal).
//
// What the tables actually hold (M11d research over the shipped data,
// verified 1:1 before this file existed):
//   * Every campaign-purchasable skill in the app's own skills table
//     (CampaignData/skills.json = the asset's xp_kosten, 168/168 rows join
//     with zero cost mismatches) prints strain_cost 0. XP and strain are
//     DISJOINT pools on the 21-hero roster: XP buys the skill tree, strain
//     is paid by the printed card reactions.
//   * Therefore "budget = sum of strain_kosten of the faehigkeiten[] buyable
//     at the current XP" (task formula) is mathematically 0 for every hero -
//     it is not implemented as-is; that would make every single marker
//     over-budget and turn a LIGHT signal into the loudest in the layer.
//   * Faithful reading with zero invented data: the buffer is what the
//     printed reactions cost in total. xp_kosten == null in the asset means
//     "printed on the card, nothing to buy" -> always available; a KNOWN
//     xp_kosten stays gated by the hero's current unspent XP (CampaignHero.
//     xpAmount) exactly like the purchase screen gates it (cost > xpAmount
//     blocks) - so the formula stays correct if a strain-bearing purchasable
//     ability ever enters the tables. Since every known-cost row is strain 0
//     today, the XP term contributes 0 either way and the budget is static.
//     Onar Koma / Tress Hacnua print no strain reactions -> budget 0 ->
//     the guard in Pressure() (markers > 0) keeps them out of the signal.
//   * The asset stores one row per card SLOT and per healthy/wounded side,
//     so a reaction that prints on both sides is listed twice (Diala's
//     Foresight) and truncated wiki exports can even split one reaction in
//     two slug rows (CT-1701 'barrage' + its long export twin). Distinct
//     reactions only: rows whose slug extends another row's slug merge into
//     the shorter one (min strain), then the distinct strains are summed.
//     This is why CT-1701's buffer is 2, not 4.
//
// Deterministic, pure, public data only (fairness canon unchanged).
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    /// <summary>M11d strain buffer + the light focus pressure term.</summary>
    public static class StrainPressure
    {
        /// <summary>The +8 focus-econ surcharge of the M11d rule (cap by
        /// construction: it never scales with the marker overflow - one
        /// marker over the buffer and a hundred cost the same +8).</summary>
        public const int Druck = 8;

        /// <summary>The hero's strain buffer (see header). 0 = no printed
        /// strain reactions OR no card at all (never a pressure signal).
        /// availableXp null (no campaign context) = every KNOWN-cost row
        /// counts as buyable - which is value-neutral today (all known-cost
        /// rows print strain 0; see header).</summary>
        public static int Budget(HeroCard card, int? availableXp)
        {
            if (card == null) return 0;
            var bySlug = new Dictionary<string, int>(StringComparer.Ordinal);
            var slugs = new List<string>();
            var strains = new List<int?>();
            foreach (var ab in card.Faehigkeiten)
            {
                if (!bySlug.ContainsKey(ab.Bezeichnung))
                { bySlug[ab.Bezeichnung] = strains.Count; slugs.Add(ab.Bezeichnung); strains.Add(null); }
                int i = bySlug[ab.Bezeichnung];
                int st = ab.StrainKosten ?? 0;
                strains[i] = strains[i] == null || st < strains[i].Value ? st : strains[i].Value;
            }
            var merged = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < slugs.Count; i++)
            {
                var key = slugs[i];
                for (int j = 0; j < slugs.Count; j++)
                    if (i != j && key.StartsWith(slugs[j], StringComparison.Ordinal)) { key = slugs[j]; break; }
                int st = strains[i] ?? 0;
                if (!merged.ContainsKey(key) || st < merged[key]) merged[key] = st;
            }
            int sum = 0;
            foreach (var kv in merged)
            {
                if (kv.Value <= 0) continue;
                // buyable test: printed (null xp) = always; known cost = fits
                // the hero's current unspent XP pool (purchase-screen law).
                bool buyable = AvailableXpBuyable(card, kv.Key, availableXp);
                if (buyable) sum += kv.Value;
            }
            return sum;
        }

        /// <summary>Buyable-at-XP test for one distinct reaction: printed
        /// rows (xp_kosten null) are free; a known cost must fit the pool.
        /// The longest matching row of a merged slug decides (the export
        /// twin carries the same cost as its parent).</summary>
        static bool AvailableXpBuyable(HeroCard card, string slug, int? availableXp)
        {
            int? cost = null;
            foreach (var ab in card.Faehigkeiten)
                if (ab.Bezeichnung == slug || ab.Bezeichnung.StartsWith(slug, StringComparison.Ordinal))
                    if (ab.XpKosten != null && (cost == null || ab.XpKosten.Value > cost.Value))
                        cost = ab.XpKosten;
            if (cost == null) return true;                    // printed, nothing to buy
            if (availableXp == null) return true;             // no campaign context: see Budget()
            return cost.Value <= availableXp.Value;
        }

        /// <summary>The focus-econ surcharge (task rule, literal):
        /// placed markers >= buffer AND markers > 0 -> +Druck, else 0. The
        /// markers>0 guard keeps a buffer-less hero (budget 0) from counting
        /// as "full" at zero markers. Null anywhere = the source cannot see
        /// strain = no influence (M11d ready-to-wire: the live app does not
        /// track markers yet - the polling seam is documented at the bridge).</summary>
        public static int? Pressure(int? strainMarkers, int bufferBudget)
        {
            if (strainMarkers == null || bufferBudget <= 0) return null;
            if (strainMarkers.Value <= 0) return 0;
            return strainMarkers.Value >= bufferBudget ? (int?)Druck : (int?)0;
        }
    }
}
