// Port of sim/order_plan.py — OrderPlanner, concept 3.4.1 (v3.8): the round's
// activation order as a tactical plan, replacing the legacy dice-determined
// chance order.
//
// Fairness law (3.4.1): the plan is a PERMUTATION of the living groups -
// every group exactly once, never more. The planner may only use
//
// * printed strength (DeploymentCard dice/HP/speed/size - cards.py),
// * the Einsatzwert from Posture / Pattern / Belief (public posture is
//   public game state per M2a.1; zone beliefs are AppView-sanctioned;
//   hero-ring beliefs from sim.belief.ActivationInference are zone bands,
//   never coordinates),
// * the Instruktions-Ledger (the App as command authority owns the groups'
//   own Soll-Positions - 3.3.3 Nr. 2b).
//
// Strength-efficiency matrix (Staerke-Effizienz-Matrix): printed value x
// Einsatzwert - printed damage EV x remaining vitality (attrition, 3.3.3
// Nr. 2b side effect) x posture-fit of the pattern x proximity of known
// rebels (ring zones / contested zone).
//
// Synergy-chain hard slots (stable moves over the efficiency order, each one
// logged in the order card's why-text):
//     buff_before_receiver            support before its attack receivers
//     blocker_before_attached_attack  blocker before attackers it covers
//     deploy_before_intercept         fresh arrival before its interceptors
//     finisher_last                   highest damage potential activates last
//
// Skill binding (concept 6, NO new RNG pot): the planner consumes the skill
// rng handed in by the caller (make_skill_rng stream) and reuses the T2b
// SkillProfile semantics:
//
//     S1  chance-free but plan-less: Aufstellungsreihenfolge (index), no
//         matrix, no chains - the old-app level, recognisable (§6 table)
//     S2  strength-efficiency order, chains still off
//     S3  chains + rough efficiency with seeded missers (miss_rate swap,
//         exactly two rng draws per round when the miss fires)
//     S4  chains + full efficiency + finisher-last, rare missers
//     S5  full chain optimisation, no missers; tempo law at full weight
//
// Determinism: no wall-clock, no own Random - PlanRound() is a pure function
// of its inputs plus the injected rng draws.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    /// Mirrors Python's AssertionError raised by assert_permutation.
    public class AssertionError : Exception
    {
        public AssertionError(string message) : base(message) { }
    }

    /// Everything the planner may know about one living group (public or
    /// AppView-sanctioned only - never hidden hero coordinates).
    public sealed class GroupFacts
    {
        public string GroupId;
        public int Index;                       // deployment/index order (tiebreak)
        public string CardId;
        public string Name;
        public string Pattern;
        public int NAlive;
        public Rational VitalityFrac;           // remaining vitality / printed total
        public string Zone;                     // current zone id (belief label allowed); null = none
        public int? ArrivedRound;               // null: on board from mission start
        public bool IsHeavy;                    // Large/Massive base (printed size)
        public bool IsSwarm;                    // small + fast doctrine of many bodies

        public GroupFacts(string groupId, int index, string cardId, string name,
                          string pattern, int nAlive, Rational vitalityFrac,
                          string zone = null, int? arrivedRound = null,
                          bool isHeavy = false, bool isSwarm = false)
        {
            GroupId = groupId;
            Index = index;
            CardId = cardId;
            Name = name;
            Pattern = pattern;
            NAlive = nAlive;
            VitalityFrac = vitalityFrac;
            Zone = zone;
            ArrivedRound = arrivedRound;
            IsHeavy = isHeavy;
            IsSwarm = isSwarm;
        }
    }

    public sealed class OrderSlot
    {
        public readonly string GroupId;
        public readonly int Rank;                       // 0-based activation rank
        public readonly List<string> Tags;              // hard-slot tags applied to this group
        public readonly string Why;                     // Begruendungs-String (German, public-safe)

        public OrderSlot(string groupId, int rank, List<string> tags, string why)
        {
            GroupId = groupId;
            Rank = rank;
            Tags = tags ?? new List<string>();
            Why = why;
        }

        public Dictionary<string, object> AsDict()
        {
            var tags = new List<object>();
            foreach (var t in Tags) tags.Add(t);
            return new Dictionary<string, object>
            {
                { "group", GroupId },
                { "rank", (long)Rank },
                { "tags", tags },
                { "why", Why },
            };
        }
    }

    public sealed class OrderPlan
    {
        public readonly int RoundNo;
        public readonly string Level;
        public readonly int IssuedTurn;                 // logical clock (no wall-clock time!)
        public readonly List<OrderSlot> Slots;
        public readonly string MissNote;                // seeded-misser annotation (S3/S4)

        public OrderPlan(int roundNo, string level, int issuedTurn,
                         List<OrderSlot> slots, string missNote = "")
        {
            RoundNo = roundNo;
            Level = level;
            IssuedTurn = issuedTurn;
            Slots = slots ?? new List<OrderSlot>();
            MissNote = missNote ?? "";
        }

        public List<string> GroupIds()
        {
            var outl = new List<string>();
            foreach (var s in Slots) outl.Add(s.GroupId);
            return outl;
        }

        public Dictionary<string, object> AsDict()
        {
            var order = new List<object>();
            foreach (var s in Slots) order.Add(s.GroupId);
            var reasons = new List<object>();
            foreach (var s in Slots) reasons.Add(s.AsDict());
            return new Dictionary<string, object>
            {
                { "round", (long)RoundNo },
                { "level", Level },
                { "ts_round", (long)RoundNo },
                { "ts_turn", (long)IssuedTurn },
                { "order", order },
                { "miss", MissNote },
                { "reasons", reasons },
            };
        }

        /// Leak-audit safe public order card (no banned tokens, only groups
        /// that are already on the board). German string byte-exact against
        /// the order_plan.json "public" rows.
        public string PublicLine(Dictionary<string, string> names)
        {
            var seqParts = new List<string>();
            var whyParts = new List<string>();
            foreach (var s in Slots)
            {
                string n = names != null && names.TryGetValue(s.GroupId, out var v) ? v : s.GroupId;
                seqParts.Add(n);
                whyParts.Add(n + ": " + s.Why);
            }
            return "Reihenfolge-Karte (Stufe " + Level + "): " + string.Join(" > ", seqParts) +
                   " | " + string.Join("; ", whyParts);
        }
    }

    /// GroupFacts + the printed damage EV of its ALIVE figures (exact
    /// Rational - the matrix never touches float dice).
    public sealed class GroupFactsWithEv
    {
        public readonly GroupFacts Facts;
        public readonly Rational PrintedEvTotal;

        public GroupFactsWithEv(GroupFacts facts, Rational printedEvTotal)
        {
            Facts = facts;
            PrintedEvTotal = printedEvTotal;
        }
    }

    public static class OrderPlanner
    {
        /// Posture-fit table (concept 3.4.1 "Einsatzwert aus Posture/Pattern").
        /// Printed strength is multiplied by the doctrine fit of the pattern;
        /// values are curated (documented in docs/balance_sim.md),
        /// deterministic.
        public static readonly Dictionary<string, Dictionary<string, Rational>> PostureFit =
            new Dictionary<string, Dictionary<string, Rational>>
        {
            { "eliminate", new Dictionary<string, Rational> {
                { "aggressive", new Rational(3, 2) }, { "hunter", new Rational(7, 5) },
                { "ambusher", new Rational(11, 10) }, { "support", new Rational(9, 10) },
                { "blocker", new Rational(4, 5) }, { "guard", new Rational(3, 5) } } },
            { "stall", new Dictionary<string, Rational> {
                { "blocker", new Rational(3, 2) }, { "guard", new Rational(7, 5) },
                { "ambusher", new Rational(11, 10) }, { "support", Rational.One },
                { "hunter", new Rational(9, 10) }, { "aggressive", new Rational(4, 5) } } },
            { "protect", new Dictionary<string, Rational> {
                { "guard", new Rational(3, 2) }, { "blocker", new Rational(13, 10) },
                { "support", new Rational(13, 10) }, { "ambusher", new Rational(9, 10) },
                { "hunter", new Rational(4, 5) }, { "aggressive", new Rational(7, 10) } } },
            { "deny", new Dictionary<string, Rational> {
                { "hunter", new Rational(3, 2) }, { "ambusher", new Rational(13, 10) },
                { "aggressive", new Rational(11, 10) }, { "guard", Rational.One },
                { "blocker", new Rational(9, 10) }, { "support", Rational.One } } },
        };
        public static readonly Rational DefaultFit = Rational.One;

        /// Groups whose activation consumes an attack (receiver of buffs,
        /// attacker that a blocker "attaches" to, candidate finisher).
        public static readonly string[] AttackPatterns = { "aggressive", "hunter" };
        /// The order in which the planner names hard slots (deterministic).
        public static readonly string[] HardSlots =
            { "buff_before_receiver", "blocker_before_attached_attack",
              "deploy_before_intercept", "finisher_last" };

        static bool IsAttackPattern(string pattern)
        {
            return pattern == "aggressive" || pattern == "hunter";
        }

        public static GroupFactsWithEv WithEv(GroupFacts facts, DeploymentCard card)
        {
            return new GroupFactsWithEv(facts, Dice.PrintedEv(card) * facts.NAlive);
        }

        /// Staerke-Effizienz-Matrix: printed value x Einsatzwert.
        ///
        /// printed damage EV x posture-fit x attrition weight
        /// (+ ring proximity x1.25, + contested-zone x1.15,
        ///  tempo law: heavy bonus / swarm discount).
        public static Rational Efficiency(GroupFactsWithEv w, string posture,
                                         HashSet<string> ringZones, string contestZone)
        {
            var f = w.Facts;
            var fit = DefaultFit;
            Dictionary<string, Rational> row;
            if (posture != null && PostureFit.TryGetValue(posture, out row))
            {
                Rational v;
                if (row.TryGetValue(f.Pattern, out v)) fit = v;
            }
            // attrition weight: printed strength x remaining vitality (3.3.3 Nr.2b)
            var body = Rational.One / 2 + Rational.One / 2 * f.VitalityFrac;
            var eff = w.PrintedEvTotal * fit * body;
            if (ringZones != null && ringZones.Count > 0 &&
                f.Zone != null && ringZones.Contains(f.Zone))
            {
                // a hero ring was inferred around this group's area: targets are
                // within reach NOW -> shoot before they move again
                eff = eff * new Rational(5, 4);
            }
            if (!string.IsNullOrEmpty(contestZone) && f.Zone == contestZone)
                eff = eff * new Rational(23, 20);
            // tempo law: heavies first when they are not waiting on a blocker;
            // swarms follow the heavy shots (they clear doors/positions)
            if (f.IsHeavy)
                eff = eff + (w.PrintedEvTotal + Rational.One) * new Rational(2, 5);
            if (f.IsSwarm)
                eff = eff - eff * new Rational(1, 8);
            return eff;
        }

        /// Build the round's activation order card.
        ///
        /// groups      : every LIVING imperial group exactly once
        /// cards       : card_id -> DeploymentCard (printed values only)
        /// posture     : current public posture ('eliminate'/'stall'/...)
        /// skill       : SkillProfile or null (reference path; strings resolve
        ///               via SkillProfile.Resolve)
        /// rng         : the caller's skill rng (make_skill_rng stream) - the
        ///               planner never owns a Random of its own
        /// ringZones   : zone ids carrying a hero-ring belief (belief.py output)
        /// contestZone : zone id the doctrine currently contests (public/belief)
        /// counter     : active VictoryCounter (finisher law: woundAll wants
        ///               fresh damage last -> finisher stays last regardless)
        public static OrderPlan PlanRound(int roundNo, List<GroupFacts> groups,
                                          Dictionary<string, DeploymentCard> cards,
                                          string posture, SkillProfile skill, IRandom rng,
                                          int issuedTurn = 0,
                                          IEnumerable<string> ringZones = null,
                                          string contestZone = null,
                                          string counter = null)
        {
            var prof = skill;                       // resolve_skill(skill)
            var level = prof != null ? prof.Level : "ref";
            if (groups == null || groups.Count == 0)
                return new OrderPlan(roundNo, level, issuedTurn, null, "");

            var weighted = new List<GroupFactsWithEv>();
            foreach (var g in groups)
                weighted.Add(WithEv(g, cards[g.CardId]));

            var tags = new Dictionary<string, List<string>>();
            var why = new Dictionary<string, List<string>>();
            foreach (var w in weighted)
            {
                tags[w.Facts.GroupId] = new List<string>();
                why[w.Facts.GroupId] = new List<string>();
            }

            // -- base order ----------------------------------------------------
            var order = new List<GroupFactsWithEv>(weighted);
            if (level == "S1" || prof == null)
            {
                // chance-free but plan-less: Aufstellungsreihenfolge (index order).
                SortStable(order, (a, b) => a.Facts.Index.CompareTo(b.Facts.Index));
                foreach (var w in order)
                    why[w.Facts.GroupId].Add("Standardreihenfolge (Aufstellung)");
            }
            else
            {
                var ringSet = new HashSet<string>();
                if (ringZones != null) foreach (var z in ringZones) ringSet.Add(z == null ? null : z.ToString());
                var effs = new Dictionary<string, Rational>();
                foreach (var w in weighted)
                    effs[w.Facts.GroupId] = Efficiency(w, posture, ringSet, contestZone);
                // key = (-eff, -printed_ev_total, index) — stable tie chain
                SortStable(order, (a, b) =>
                {
                    int c = CompareDescending(effs[a.Facts.GroupId], effs[b.Facts.GroupId]);
                    if (c != 0) return c;
                    c = CompareDescending(a.PrintedEvTotal, b.PrintedEvTotal);
                    if (c != 0) return c;
                    return a.Facts.Index.CompareTo(b.Facts.Index);
                });
                foreach (var w in order)
                {
                    var e = effs[w.Facts.GroupId];
                    why[w.Facts.GroupId].Add(
                        "Staerke-Effizienz " + Py.F2(e.ToDouble()) +
                        " (W " + posture + ", Muster " + w.Facts.Pattern + ")");
                }
            }

            // -- S2 and S1: chains stay off --------------------------------------
            bool useChains = prof != null && (level == "S3" || level == "S4" || level == "S5");
            if (useChains)
            {
                ApplySlotBuff(order, tags, why);
                ApplySlotBlocker(order, tags, why);
                ApplySlotDeploy(order, tags, why, roundNo);
            }
            if (prof != null && (level == "S4" || level == "S5"))
                ApplySlotFinisher(order, tags, why, counter);

            // -- seeded missers (S3/S4): reuse the skill stream, no own pot -----
            string miss = "";
            if (prof != null && (level == "S3" || level == "S4") && rng != null && order.Count > 1)
            {
                double u = rng.NextUnit();
                if (u < prof.MissRate)
                {
                    // Python randrange(1, len) draws _randbelow(len-1) + 1
                    int k = 1 + rng.RandBelow(order.Count - 1);
                    var t = order[k - 1]; order[k - 1] = order[k]; order[k] = t;
                    miss = "Aussetzer (Stufe " + level + "): Rang " + k + "/" + (k + 1) + " vertauscht";
                    string a = order[k - 1].Facts.GroupId;
                    string b = order[k].Facts.GroupId;
                    tags[a].Add("missa_step");
                    tags[b].Add("missa_step");
                }
            }

            var slots = new List<OrderSlot>();
            for (int i = 0; i < order.Count; i++)
            {
                var w = order[i];
                slots.Add(new OrderSlot(w.Facts.GroupId, i, tags[w.Facts.GroupId],
                                        string.Join(" | ", why[w.Facts.GroupId])));
            }
            return new OrderPlan(roundNo, level, issuedTurn, slots, miss);
        }

        /// Python sorted(): stable. List.Sort is unstable — sort index order out.
        static void SortStable<T>(List<T> list, Comparison<T> cmp)
        {
            var keys = new int[list.Count];
            for (int i = 0; i < list.Count; i++) keys[i] = i;
            Array.Sort(keys, (ia, ib) =>
            {
                int c = cmp(list[ia], list[ib]);
                return c != 0 ? c : ia.CompareTo(ib);
            });
            var sorted = new List<T>(list.Count);
            foreach (var i in keys) sorted.Add(list[i]);
            for (int i = 0; i < list.Count; i++) list[i] = sorted[i];
        }

        /// Python tuple key (-a, -b) descending order on Rationals.
        static int CompareDescending(Rational a, Rational b)
        {
            if (a > b) return -1;
            if (a < b) return 1;
            return 0;
        }

        /// Hard fairness gate: the plan is a permutation of the living groups.
        public static void AssertPermutation(OrderPlan plan, IEnumerable<string> livingGroupIds)
        {
            var got = plan.GroupIds();
            var wantList = new List<string>();
            foreach (var g in livingGroupIds) wantList.Add(g == null ? "null" : g.ToString());
            wantList.Sort(StringComparer.Ordinal);
            got.Sort(StringComparer.Ordinal);
            bool same = got.Count == wantList.Count;
            if (same)
                for (int i = 0; i < got.Count; i++)
                    if (got[i] != wantList[i]) { same = false; break; }
            if (!same)
                throw new AssertionError(
                    "order plan is not a permutation of the living groups: " +
                    "plan=" + Py.ListRepr(plan.GroupIds()) +
                    " living=" + Py.ListRepr(wantList));
        }

        // --- hard-slot moves (stable; each keeps the permutation property) ----
        // Identity equality on the GroupFactsWithEv objects mirrors Python's
        // list.index()/remove() on dataclass instances.

        static void MoveBefore(List<GroupFactsWithEv> order, GroupFactsWithEv mover,
                               GroupFactsWithEv anchor, Dictionary<string, List<string>> tags,
                               Dictionary<string, List<string>> why, string tag, string text)
        {
            if (ReferenceEquals(mover, anchor))
                return;                              // a group is never its own anchor
            int iM = IdentityIndexOf(order, mover);
            int iA = IdentityIndexOf(order, anchor);
            if (iM < iA)
                return;
            order.RemoveAt(iM);
            iA = IdentityIndexOf(order, anchor);
            order.Insert(iA, mover);
            if (!tags[mover.Facts.GroupId].Contains(tag))
                tags[mover.Facts.GroupId].Add(tag);
            why[mover.Facts.GroupId].Add(text);
        }

        static int IdentityIndexOf(List<GroupFactsWithEv> list, GroupFactsWithEv item)
        {
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], item)) return i;
            return -1;
        }

        /// buff_before_receiver: every support group acts before the attack
        /// groups it would buff (receiver = first aggressive/hunter in order).
        static void ApplySlotBuff(List<GroupFactsWithEv> order,
                                  Dictionary<string, List<string>> tags,
                                  Dictionary<string, List<string>> why)
        {
            var receivers = Filter(order, w => IsAttackPattern(w.Facts.Pattern));
            if (receivers.Count == 0)
                return;
            var firstReceiver = receivers[0];
            foreach (var w in Filter(order, x => x.Facts.Pattern == "support"))
                MoveBefore(order, w, firstReceiver, tags, why, "buff_before_receiver",
                           "Buff zuerst: Staerkt die Angreifer dieser Runde");
        }

        /// blocker_before_attached_attack: a blocker takes its position before
        /// the attackers in its zone (the attack is 'attached' to its block).
        static void ApplySlotBlocker(List<GroupFactsWithEv> order,
                                     Dictionary<string, List<string>> tags,
                                     Dictionary<string, List<string>> why)
        {
            foreach (var b in Filter(order, x => x.Facts.Pattern == "blocker"))
                foreach (var a in Filter(order, x =>
                             IsAttackPattern(x.Facts.Pattern) && x.Facts.Zone == b.Facts.Zone))
                    MoveBefore(order, b, a, tags, why, "blocker_before_attached_attack",
                               "Blockade vor dem angeh\u00e4ngten Angriff halten");
        }

        /// deploy_before_intercept: a group arriving this round activates
        /// before the hunters that rely on it being on the board.
        static void ApplySlotDeploy(List<GroupFactsWithEv> order,
                                    Dictionary<string, List<string>> tags,
                                    Dictionary<string, List<string>> why, int roundNo)
        {
            var fresh = Filter(order, x => x.Facts.ArrivedRound.HasValue &&
                                           x.Facts.ArrivedRound.Value == roundNo);
            if (fresh.Count == 0)
                return;
            foreach (var h in Filter(order, x => x.Facts.Pattern == "hunter"))
                foreach (var f in fresh)
                    MoveBefore(order, f, h, tags, why, "deploy_before_intercept",
                               "Neue Ankunft vor den Intercepts einsetzen");
        }

        /// finisher_last: the highest damage potential sees the pre-damage and
        /// activates last. The 3.4.1 exception (kill would change the focus of
        /// the next group) is respected: finisher must be an ATTACK group;
        /// woundAll counters want fresh damage anyway, so the last slot stays
        /// correct.
        static void ApplySlotFinisher(List<GroupFactsWithEv> order,
                                      Dictionary<string, List<string>> tags,
                                      Dictionary<string, List<string>> why,
                                      string counter = null)
        {
            if (order.Count < 2)
                return;
            GroupFactsWithEv fin = null;
            Rational best = Rational.Zero;
            bool haveBest = false;
            for (int i = 0; i < order.Count - 1; i++)
            {
                var w = order[i];
                if (!IsAttackPattern(w.Facts.Pattern) || !(w.Facts.VitalityFrac > Rational.Zero))
                    continue;
                var key = w.PrintedEvTotal * w.Facts.VitalityFrac;
                // max(key, -index): ties keep the EARLIER index (Python max
                // keeps the first maximum; -index breaks ties to lower index).
                bool better = !haveBest || key > best ||
                              (key == best && w.Facts.Index < fin.Facts.Index);
                if (better) { best = key; fin = w; haveBest = true; }
            }
            if (fin == null)
                return;
            order.Remove(fin);
            order.Add(fin);
            tags[fin.Facts.GroupId].Add("finisher_last");
            why[fin.Facts.GroupId].Add(
                "Finisher zuletzt: sieht die Vorsaeden und stoest zu" +
                (counter == "woundAll" ? " (Zaehler will frischen Schaden)" : ""));
        }

        static List<GroupFactsWithEv> Filter(List<GroupFactsWithEv> order,
                                             Func<GroupFactsWithEv, bool> pred)
        {
            var outl = new List<GroupFactsWithEv>();
            foreach (var w in order) if (pred(w)) outl.Add(w);
            return outl;
        }
    }
}
