// M12c Imperial Upgrade Stage purchases (FFG Rules Reference, "Imperial
// Upgrade Stage", verbatim core: "To purchase Class cards, the Imperial
// player EXAMINES ALL CARDS in his Class deck and may purchase AS MANY AS HE
// WISHES by spending XP equal to the cards' costs ... kept faceup ... for
// the remainder of the campaign."). There is NO market and NO chance element
// in class-card purchasing: the whole chosen deck is always visible, so the
// KI does not need a draw pile - it buys greedily by purchase priority
// against the SAME campaign profile the M12b deck choice scored against, and
// a single purchase step is exactly "buy 1 card, if XP >= price" (the Saga
// layer repeats that step while affordable cards remain - the RR allows any
// number of buys per stage; see the chain doctrine note in the M12c commit).
//
// Determinism rule (same as M12b): no UnityEngine.Random, no wall time, no
// order side-channels. Every number is a pure function of
//   seed      - the campaign deck seed (DeckSeedFor(GUID): mission-
//               independent, so priorities cannot drift between missions),
//   profile   - the M12b ImperialCampaignProfile (heroes, villain families,
//               mission kinds),
//   owned     - the ids already acquired (basic cards ride along from deck
//               choice), and
//   xpBalance - the M12b XP account after mission-end bookings.
//
// SCORE MODEL (same vocabulary, tokens and weights as the M12b deck score -
// just evaluated per card instead of per deck, so "greedy against the M12b
// profile" is literally the same function family):
//   synergy(card) =
//       3 * sum over UNIT families {TROOPER, VEHICLE, DROID, HEAVY WEAPON,
//           MINE, OFFICER, GUARDIAN, COMMANDER}:
//           min(#villains of that family in the campaign, 4)
//         * [card text mentions that family]
//     + 2 * min(heroCount, 4)   * [card mentions HERO]
//     + 1 * min(heroCount, 4)   * [card mentions REBEL]
//     + 2 * min(agendaMissions, 4) * [card mentions AGENDA]
//     + 1 * min(missionCount, 8)   * [card mentions MISSION]
//   noise(card)   = DeterministicRandom(seed ^ Fnv1a64("buy|" + cardId))
//                   .RandBelow(4)              // 0..3: bounded tie jitter
//   total(card)   = synergy + noise
// The deck score counted deck-wide mentions (0..4 cap); a single card can
// only contribute its own mention, so the per-card factor is the mention
// flag - this keeps a card's purchase priority aligned with why its deck
// was chosen in the first place. Candidate rank = total desc, then card id
// ascending (ordinal) - a total order, fully re-derivable.
//
// Basic cards (xp null, 0 XP) are NEVER purchase candidates: they arrive
// free at deck choice (setup step 3 "Choose Imperial Class") and the store
// already lists them as owned. A non-basic card without a printed XP cost
// (data anomaly, absent in the Era-1 corpus) is not buyable with XP and is
// skipped - the gate pins "paid cards == non-basic cards" for every deck.
using System;
using System.Collections.Generic;
using System.Linq;

namespace TacticalAI.Core
{
    /// <summary>One ranked purchase-priority row for one candidate card.
    /// The full ranked list is the persisted Begruendung (as in M12b).</summary>
    public sealed class ImperialCardScoreRow
    {
        public string Card;          // card id ("<deck>__<card>")
        public string Name;          // printed card name (display handle)
        public long Cost;            // printed XP cost (> 0 for candidates)
        public long Synergy;
        public long Noise;
        public long Total;

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                { "card", Card },
                { "name", Name },
                { "cost", Cost },
                { "synergy", Synergy },
                { "noise", Noise },
                { "total", Total },
            };
        }
    }

    /// <summary>One deterministic purchase step: the ranked candidate list +
    /// the single card actually bought (null = nothing affordable / nothing
    /// left). CardName + XpCost ride along so the caller can book and log
    /// without a second table lookup.</summary>
    public sealed class ImperialPurchaseChoice
    {
        public string SeedHex;
        public string CardId;        // bought card id or null
        public string CardName;      // printed name of the bought card
        public long XpCost;          // cost of the bought card
        public List<ImperialCardScoreRow> Scores = new List<ImperialCardScoreRow>();
        public string Reason;        // model text (not a UI string)

        public Dictionary<string, object> ToDict()
        {
            var rows = new List<object>();
            foreach (var s in Scores) rows.Add(s.ToDict());
            return new Dictionary<string, object>
            {
                { "seed", SeedHex },
                { "bought", CardId == null ? "" : CardId },
                { "cost", CardId == null ? 0L : XpCost },
                { "scores", rows },
            };
        }
    }

    /// <summary>Pure purchase planner over the primed M12b deck table. The
    /// Saga store (Saga.ImperialDeckState) books the picked card through its
    /// single TryBuyDeckCard path; nothing in this file writes state.</summary>
    public static class ImperialPurchases
    {
        /// <summary>Family counts of ONE card: abilities text + restriction
        /// + italic keyword (card NAMES are excluded - same rule as the M12b
        /// DeckFamilyCounts, applied per card).</summary>
        public static Dictionary<string, int> CardFamilyCounts(ImperialClassCard card)
        {
            var acc = new Dictionary<string, int>(StringComparer.Ordinal);
            if (card == null) return acc;
            foreach (var t in card.AbilityTexts) ImperialDecks.AddFamilyCounts(acc, t);
            ImperialDecks.AddFamilyCounts(acc, card.Restriction);
            ImperialDecks.AddFamilyCounts(acc, card.Keyword);
            return acc;
        }

        /// <summary>Card-level synergy against the M12b campaign profile
        /// (identical vocabulary + weights as ImperialDecks.SynergyScore).</summary>
        public static long CardSynergy(Dictionary<string, int> cardFamilies,
                                       ImperialCampaignProfile profile)
        {
            if (profile == null) profile = new ImperialCampaignProfile();
            long s = 0;
            int famCount(string f)
            {
                int n;
                return cardFamilies.TryGetValue(f, out n) ? n : 0;
            }
            // per-card mention factor is the flag itself (1 mention is the
            // cap; the deck-level 0..4 cap covers it), profile side keeps
            // the M12b caps exactly.
            foreach (var f in ImperialDecks.UnitFamilies)
                s += 3 * Cap(profile.VillainFamilyCount(f), 4) * Cap(famCount(f), 1);
            s += 2 * Cap(profile.HeroCount, 4) * Cap(famCount("HERO"), 1);
            s += 1 * Cap(profile.HeroCount, 4) * Cap(famCount("REBEL"), 1);
            s += 2 * Cap(profile.AgendaMissionCount, 4) * Cap(famCount("AGENDA"), 1);
            s += 1 * Cap(profile.MissionCount, 8) * Cap(famCount("MISSION"), 1);
            return s;
        }

        static long Cap(long v, long cap) { return v < 0 ? 0 : (v > cap ? cap : v); }

        /// <summary>Bounded tie jitter 0..3 for one card, re-derivable from
        /// the seed. Namespace salt "buy|" keeps purchase noise distinct from
        /// the deck-choice noise ("noise|") over the same seed.</summary>
        public static long NoiseForPurchase(ulong seed, string cardId)
        {
            var rng = new DeterministicRandom(seed ^ ImperialDecks.Fnv1a64("buy|" + cardId));
            return rng.RandBelow(4);
        }

        /// <summary>The candidates of one deck: printed non-basic cards with
        /// a printed XP cost the KI does not own yet.</summary>
        public static List<ImperialClassCard> Candidates(ImperialClassDeck deck,
                                                         ICollection<string> ownedIds)
        {
            var outp = new List<ImperialClassCard>();
            if (deck == null) return outp;
            foreach (var c in deck.Cards)
            {
                if (c.Basic || !c.Xp.HasValue) continue;   // basic/anomaly
                if (ownedIds != null && ownedIds.Contains(c.Id)) continue;
                outp.Add(c);
            }
            return outp;
        }

        /// <summary>ONE deterministic purchase step. Ranks every remaining
        /// candidate of the chosen deck (total desc, id asc) and buys the
        /// BEST AFFORDABLE one - higher-ranked but unaffordable cards are
        /// skipped for this step, never waited for (the greedy reads the
        /// account as it is). Returns null when the deck is unknown/no table;
        /// CardId null when nothing is affordable or the deck is exhausted.
        /// Pure: books nothing, callers own the store.</summary>
        public static ImperialPurchaseChoice Plan(ImperialClassDeckTable table,
                                                  ImperialCampaignProfile profile,
                                                  string deckId,
                                                  ICollection<string> ownedIds,
                                                  long xpBalance,
                                                  ulong seed)
        {
            var deck = table == null ? null : table.Find(deckId);
            if (deck == null) return null;
            profile = profile ?? new ImperialCampaignProfile();

            var rows = new List<ImperialCardScoreRow>();
            foreach (var c in Candidates(deck, ownedIds))
            {
                var syn = CardSynergy(CardFamilyCounts(c), profile);
                var noise = NoiseForPurchase(seed, c.Id);
                rows.Add(new ImperialCardScoreRow
                {
                    Card = c.Id,
                    Name = c.Name,
                    Cost = c.Xp.Value,
                    Synergy = syn,
                    Noise = noise,
                    Total = syn + noise,
                });
            }
            rows = rows.OrderByDescending(r => r.Total)
                       .ThenBy(r => r.Card, StringComparer.Ordinal)
                       .ToList();

            var choice = new ImperialPurchaseChoice
            {
                SeedHex = ImperialDecks.SeedHex(seed),
                Scores = rows,
            };
            var buy = rows.FirstOrDefault(r => r.Cost <= xpBalance);
            if (buy != null)
            {
                choice.CardId = buy.Card;
                choice.CardName = buy.Name;
                choice.XpCost = buy.Cost;
                choice.Reason = buy.Name + " (" + buy.Cost + " XP): synergy="
                    + buy.Synergy + " noise=" + buy.Noise + " total=" + buy.Total
                    + " (seed " + choice.SeedHex + ")";
            }
            return choice;
        }

        /// <summary>Table-free overload on the primed doc cache (mirrors
        /// ImperialDecks.Choose(profile, seed)).</summary>
        public static ImperialPurchaseChoice Plan(ImperialCampaignProfile profile,
                                                  string deckId,
                                                  ICollection<string> ownedIds,
                                                  long xpBalance,
                                                  ulong seed)
        {
            return Plan(ImperialDecks.Load(), profile, deckId, ownedIds, xpBalance, seed);
        }
    }
}
