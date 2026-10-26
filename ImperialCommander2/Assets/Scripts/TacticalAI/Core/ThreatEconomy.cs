// Port of sim/threat_economy.py — threat economy (concept 3.9): income
// projection, save-before-spend, ledger.
//
// Ground rules implemented here:
//
// * IC2 dumps the whole threat budget instantly (MissionEventHandler); this
//   module replaces that with a per-round income and a SAVE TARGET: a reserve
//   group is only bought down if the balance still covers `save_rounds` rounds
//   of projected income afterwards (default 2, concept 3.9 "Spare bis >=
//   2-Runden-Puffer").
// * ANTI-EARLY-STALL (Anti-Fruehstau): if nothing has been deployed for
//   `force_after_idle` rounds and the balance could pay the cheapest waiting
//   group, deployment is FORCED - a hoarded meter that never feeds units is the
//   failure mode concept 3.9 names.
// * LEDGER INVARIANT: earned == spent + balance at every tick. Violation
//   raises LedgerViolation.
// * The ledger is PRIVATE data: saving intent must never reach the public log
//   (concept 3.9 "Spar-Absichten nur im internen Ledger", leak-freedom law).
//
// Income model (design choice, documented in docs/balance_sim.md):
//     income(round) = 1 + (alive_imperials // 2)   per mission round
// Reserve group costs are the PRINTED deck-build point costs (`cost` field in
// IC2 CardData/enemies.json, carried verbatim in units/surges_core.json).
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public class LedgerViolation : Exception
    {
        public LedgerViolation(string message) : base(message) { }
    }

    public sealed class ReserveGroup
    {
        public string GroupId;
        public string CardId;
        public int Cost;
        public string Pattern;
        public int? DeployedRound;      // null: still waiting in the reserve

        public ReserveGroup(string groupId, string cardId, int cost, string pattern,
                            int? deployedRound = null)
        {
            GroupId = groupId;
            CardId = cardId;
            Cost = cost;
            Pattern = pattern;
            DeployedRound = deployedRound;
        }
    }

    public sealed class ThreatLedger
    {
        public int SaveRounds = 2;          // theta: Sparziel (Sparziel rounds)
        public int ForceAfterIdle = 2;      // anti-Fruehstau threshold
        public long Earned = 0;
        public long Spent = 0;
        public int LastReleaseRound = 0;
        /// PRIVATE ledger entries - never surface to the public log.
        public readonly List<Dictionary<string, object>> Events =
            new List<Dictionary<string, object>>();

        public ThreatLedger(int saveRounds = 2, int forceAfterIdle = 2)
        {
            SaveRounds = saveRounds;
            ForceAfterIdle = forceAfterIdle;
        }

        // ---------------------------------------------------------------- state
        public long Balance { get { return Earned - Spent; } }

        public long ProjectIncome(int aliveImperials)
        {
            return 1 + aliveImperials / 2;      // alive >= 0: // == /
        }

        // ----------------------------------------------------------------- tick
        public long Tick(int roundNo, int aliveImperials)
        {
            long income = ProjectIncome(aliveImperials);
            Earned += income;
            Check(roundNo, "tick");
            return income;
        }

        // ------------------------------------------------------------ decisions
        /// Pick the next reserve group to bring in THIS round, or null.
        ///
        /// Order: cheapest first (deterministic; ties by group_id).
        /// Save rule: balance - cost >= save_rounds * projected income.
        /// Anti-stall: if idle long enough, force the cheapest affordable one.
        public ReserveGroup ReleaseDecision(int roundNo, List<ReserveGroup> reserves,
                                           int aliveImperials)
        {
            var waiting = Waiting(reserves);
            if (waiting.Count == 0) return null;
            waiting.Sort(ByCostThenGroup);
            int idle = roundNo - LastReleaseRound;
            bool forced = idle >= ForceAfterIdle;
            foreach (var cand in waiting)
            {
                if (Balance < cand.Cost) continue;
                long keep = Balance - cand.Cost;
                if (keep >= SaveRounds * ProjectIncome(aliveImperials) || forced)
                    return cand;
            }
            return null;
        }

        /// theta noise variant (S3 "Sparregeln mit Rauschen", skill/mission
        /// semantics): ONE draw from the caller's skill rng per round with
        /// waiting reserves. Hesitant (u < econ_noise) suppresses the pick -
        /// even one the ledger would force; impulsive (u >= 1 - econ_noise)
        /// buys the cheapest affordable waiting group when the plain save rule
        /// found nothing and no anti-stall force applies. The ledger itself
        /// stays consistent (earned == spent + balance untouched).
        public ReserveGroup ReleaseDecisionWithNoise(int roundNo, List<ReserveGroup> reserves,
                                                    int aliveImperials, SkillProfile skill,
                                                    IRandom rng)
        {
            var cand = ReleaseDecision(roundNo, reserves, aliveImperials);
            var waiting = Waiting(reserves);
            double? uNoise = null;
            if (skill != null && skill.EconNoise > 0.0 && waiting.Count > 0)
                uNoise = rng.NextUnit();
            if (cand == null && uNoise.HasValue && skill != null &&
                uNoise.Value >= 1.0 - skill.EconNoise)
            {
                bool forcedNow = (roundNo - LastReleaseRound) >= ForceAfterIdle;
                if (!forcedNow)
                {
                    var cheap = new List<ReserveGroup>();
                    foreach (var g in waiting)
                        if (Balance >= g.Cost) cheap.Add(g);
                    cheap.Sort(ByCostThenGroup);
                    if (cheap.Count > 0) cand = cheap[0];   // econ_impulsive
                }
            }
            if (cand != null && uNoise.HasValue && skill != null &&
                uNoise.Value < skill.EconNoise)
                cand = null;                                // econ_hesitant
            return cand;
        }

        public void Release(int roundNo, ReserveGroup group, bool forced)
        {
            if (Balance < group.Cost)
                throw new LedgerViolation("release " + group.GroupId + ": cost " + group.Cost +
                                          " > balance " + Balance);
            Spent += group.Cost;
            group.DeployedRound = roundNo;
            LastReleaseRound = roundNo;
            Events.Add(new Dictionary<string, object>
            {
                { "round", (long)roundNo },
                { "release", group.GroupId },
                { "card", group.CardId },
                { "cost", (long)group.Cost },
                { "forced", forced },
                { "balance_after", Balance },
            });
            Check(roundNo, "release");
        }

        // ----------------------------------------------------------------- misc
        /// Real conservation law: every spent point corresponds to a recorded
        /// release event, and balance is exactly earned minus spent.
        void Check(int roundNo, string where)
        {
            long sum = 0;
            foreach (var e in Events) sum += (long)e["cost"];
            if (sum != Spent)
                throw new LedgerViolation(where + "@" + roundNo + ": ledger conservation broken");
            if (Balance < 0)
                throw new LedgerViolation(where + "@" + roundNo + ": negative balance " + Balance);
        }

        /// Groups waiting while we could at least pay the cheapest -> the
        /// Anti-Fruehstau condition is approaching.
        public bool WaitPressure(List<ReserveGroup> reserves)
        {
            var waiting = Waiting(reserves);
            if (waiting.Count == 0) return false;
            int min = int.MaxValue;
            foreach (var g in waiting) if (g.Cost < min) min = g.Cost;
            return Balance >= min;
        }

        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "earned", Earned },
                { "spent", Spent },
                { "balance", Balance },
                { "events", new List<object>(Events) },
            };
        }

        static List<ReserveGroup> Waiting(List<ReserveGroup> reserves)
        {
            var outl = new List<ReserveGroup>();
            foreach (var g in reserves) if (g.DeployedRound == null) outl.Add(g);
            return outl;
        }

        /// sort key (cost, group_id) - Python tuple order, ties fully resolved.
        static int ByCostThenGroup(ReserveGroup a, ReserveGroup b)
        {
            int c = a.Cost.CompareTo(b.Cost);
            if (c != 0) return c;
            return string.CompareOrdinal(a.GroupId, b.GroupId);
        }
    }
}
