// M12d2 Agenda market - the Imperial Upgrade Stage purchase step for
// agenda cards (FFG Rules Reference, "Imperial Upgrade Stage", verbatim):
//   "To purchase Agenda cards, the Imperial player shuffles the Agenda deck
//    and secretly draws four of them. He may purchase any of these cards by
//    spending influence equal to the cards' costs."
//   "The Imperial player does not show his drawn Agenda cards to the heroes.
//    Any cards he does not purchase are shuffled back into the deck without
//    being revealed."
//
// SECRET MARKET, FAIR PLAY: the 4-card market is face-down AGAINST THE
// REBELS (RR law), seeded ONLY from the campaign seed (never
// UnityEngine.Random) and fully auditable in the campaign store - the same
// "secret mechanic, never a leak" line as M12d's deployment hand. The KI
// reads exactly what a human imperial reads from the same four cards: the
// printed, public card text - no hidden evaluation beyond the M12c synergy
// vocabulary, no peeking at the rest of the deck.
//
// Determinism law: one stage is a PURE function of
//   deckBefore (store order), influence, profile, seed, stageSeq.
// Shuffle = canonical Python shuffle semantics (OrderDeck/AgendaCards.
// Shuffle) on DeterministicRandom(seed ^ Fnv1a64("agenda-market|" + stage)).
// The RR shuffles the deck again before EVERY stage, so the physical return
// order of unpurchased cards is mechanically dead - we still freeze the
// exact post-stage deck list (remainder + not-purchased appended in market
// order) so any replay reproduces it bit-for-bit (own harness fixture).
//
// Purchase policy (same doctrine as M12c class cards): RR allows ANY
// subset ("may purchase any of these cards"); the KI buys greedily by
// printed-text priority while the influence account can pay, highest-ranked
// affordable first. Purchased cards LEAVE the agenda deck permanently: RR
// "Most Agenda cards are immediately read aloud and resolved when
// purchased" and a resolved agenda is discarded out of the campaign ("when
// an Agenda card is discarded, it is returned to the game box"); Ongoing
// cards stay in the Play Area (store rows), Kept Secrets in the reserve -
// none of them rides in the draw pile anymore.
using System;
using System.Collections.Generic;
using System.Linq;

namespace TacticalAI.Core
{
    /// <summary>One booked buy inside one market stage.</summary>
    public sealed class AgendaBuyRow
    {
        public string Card;            // card id
        public string Name;            // printed name (display handle)
        public string Type;            // the four RR types
        public long Cost;              // printed influence cost
        public bool ReadAloud;         // false only for Kept Secret
        public long Synergy;
        public long Noise;
        public long Total;

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                { "card", Card },
                { "name", Name },
                { "type", Type },
                { "cost", Cost },
                { "read_aloud", ReadAloud },
                { "synergy", Synergy },
                { "noise", Noise },
                { "total", Total },
            };
        }
    }

    /// <summary>One complete, fully re-derivable market stage: draw, buys,
    /// return-mix and the frozen post-stage deck.</summary>
    public sealed class AgendaStagePlan
    {
        public string SeedHex;
        public long Stage;
        public List<string> Market = new List<string>();     // draw order, max 4
        public List<AgendaBuyRow> Buys = new List<AgendaBuyRow>();
        public List<string> NotPurchased = new List<string>();
        public List<string> DeckAfter = new List<string>();  // frozen replay list
        public long InfluenceBefore;
        public long InfluenceAfter;
        public string Reason;

        public Dictionary<string, object> ToDict()
        {
            var buys = new List<object>();
            foreach (var b in Buys) buys.Add(b.ToDict());
            return new Dictionary<string, object>
            {
                { "seed", SeedHex },
                { "stage", Stage },
                { "market", Market.Cast<object>().ToList() },
                { "buys", buys },
                { "not_purchased", NotPurchased.Cast<object>().ToList() },
                { "deck_after", DeckAfter.Cast<object>().ToList() },
                { "influence_before", InfluenceBefore },
                { "influence_after", InfluenceAfter },
                { "reason", Reason ?? "" },
            };
        }
    }

    /// <summary>Pure market planner. The Saga store (Saga.AgendaState) books
    /// the buys through its single TryBuyAgenda path; nothing here writes
    /// state.</summary>
    public static class AgendaMarket
    {
        public const int MarketDraw = 4;   // RR "secretly draws four of them"

        /// <summary>Per-card purchase synergy: same vocabulary, tokens and
        /// weights as the setup set score (AgendaCards.SetSynergy evaluates
        /// exactly this per card, including the kept_secret agenda-mission
        /// slot) - "greedy against the M12b profile" stays one function
        /// family across setup and purchase, like M12b/M12c did.</summary>
        public static long CardSynergy(AgendaCard c, ImperialCampaignProfile profile)
        {
            if (profile == null) profile = new ImperialCampaignProfile();
            long s = 0;
            var fams = new Dictionary<string, int>(StringComparer.Ordinal);
            ImperialDecks.AddFamilyCounts(fams, c.Text);
            int Count(string f)
            {
                int n;
                return fams.TryGetValue(f, out n) ? n : 0;
            }
            long Cap(long v, long cap) { return v < 0 ? 0 : (v > cap ? cap : v); }
            foreach (var f in ImperialDecks.UnitFamilies)
                s += 3 * Cap(profile.VillainFamilyCount(f), 4) * Cap(Count(f), 1);
            s += 2 * Cap(profile.HeroCount, 4) * Cap(Count("HERO"), 1);
            s += 1 * Cap(profile.HeroCount, 4) * Cap(Count("REBEL"), 1);
            s += 2 * Cap(profile.AgendaMissionCount, 4) * Cap(Count("AGENDA"), 1);
            s += 1 * Cap(profile.MissionCount, 8) * Cap(Count("MISSION"), 1);
            if (c.Type == AgendaCards.TypeKeptSecret) s += 2 * Cap(profile.AgendaMissionCount, 4);
            return s;
        }

        /// <summary>Bounded tie jitter 0..3, salt distinct from the setup
        /// noise ("agenda-set|") and the class-card buys ("buy|").</summary>
        public static long NoiseForBuy(ulong seed, string cardId)
        {
            var rng = new DeterministicRandom(seed ^ ImperialDecks.Fnv1a64("agenda-buy|" + cardId));
            return rng.RandBelow(4);
        }

        /// <summary>Run ONE upgrade-stage market on a deck list (no state
        /// write). deckBefore is used as-is; the RR shuffle before the draw
        /// is reproduced here from (seed, stage). Empty deck = honest null
        /// ('kein Agenda-State' contract: callers keep the zero state).</summary>
        public static AgendaStagePlan RunStage(AgendaCardTable table,
                                               ImperialCampaignProfile profile,
                                               List<string> deckBefore,
                                               long influenceBefore,
                                               ulong seed, long stage)
        {
            if (table == null) return null;
            if (deckBefore == null || deckBefore.Count == 0) return null;
            profile = profile ?? new ImperialCampaignProfile();

            var pile = new List<string>(deckBefore);
            var rng = new DeterministicRandom(seed ^ ImperialDecks.Fnv1a64("agenda-market|" + stage));
            AgendaCards.Shuffle(pile, rng);
            var market = pile.Take(MarketDraw).ToList();
            var remainder = pile.Skip(MarketDraw).ToList();

            var plan = new AgendaStagePlan
            {
                SeedHex = ImperialDecks.SeedHex(seed),
                Stage = stage,
                Market = market,
                InfluenceBefore = influenceBefore,
            };

            // rank the market: total desc, id asc (ordinal) - a total order.
            var ranked = new List<AgendaBuyRow>();
            foreach (var id in market)
            {
                var c = table.Find(id);
                if (c == null) continue;   // unknown card id: skipped, never bought
                var syn = CardSynergy(c, profile);
                var noise = NoiseForBuy(seed, c.Id);
                ranked.Add(new AgendaBuyRow
                {
                    Card = c.Id,
                    Name = c.Name,
                    Type = c.Type,
                    Cost = c.Cost,
                    ReadAloud = c.Type != AgendaCards.TypeKeptSecret,
                    Synergy = syn,
                    Noise = noise,
                    Total = syn + noise,
                });
            }
            ranked = ranked.OrderByDescending(r => r.Total)
                           .ThenBy(r => r.Card, StringComparer.Ordinal)
                           .ToList();

            long balance = influenceBefore;
            foreach (var r in ranked)
            {
                if (r.Cost > balance) continue;   // skipped THIS stage, never waited for
                balance -= r.Cost;
                plan.Buys.Add(r);
            }
            var bought = new HashSet<string>(plan.Buys.Select(b => b.Card), StringComparer.Ordinal);
            plan.NotPurchased = market.Where(id => !bought.Contains(id)).ToList();
            plan.InfluenceAfter = balance;
            // frozen replay list: remainder + not-purchased appended (market
            // order). RR shuffles again next stage, so order here is audit
            // order only - but it must reproduce bit-for-bit (fixture).
            plan.DeckAfter = new List<string>(remainder);
            plan.DeckAfter.AddRange(plan.NotPurchased);
            plan.Reason = plan.Buys.Count + " buys, " + plan.NotPurchased.Count
                + " returned, influence " + balance + " (seed " + plan.SeedHex
                + ", stage " + stage + ")";
            return plan;
        }
    }
}
