// M6 Overlord focus layer (concept v4.1 3.3 + 3.5, S4/S5 sharpening).
//
// The overlord plays the SAME legal moves as every other skill level - what
// differs is the QUALITY of public-information optimisation. This module is
// that optimisation for target choice, and it consumes EXACTLY two inputs:
//
//   (1) FeedWorldView - the deterministic projection of the passive public
//       world feed (M5): wounded/defeated flags, focus MARKS (aim tokens).
//       No positions, no hidden state, no dice. A human GM sees the very
//       same facts at the table.
//   (2) HeroCard (M11b, tactical_hero_cards.json) - the PRINTED hero card
//       values of all 21 campaign heroes (healthy + wounded side, weapon
//       dice, items via the app campaign state); HeroThreatProfile stays the
//       role/chip vocabulary and the fallback for rows outside the app
//       roster (concept 3.3.1a). Every W component is vector-ported sim
//       truth (hero_profiles.json, hero_w.json).
//
// FAIRNESS CANON (concept 3.3.1/12, hard constraint): ranking only ever
// PERMUTES the candidate set - it never filters, never invents a target,
// never reads anything outside the two inputs above. Every skill level may
// choose any living hero (identical legal action set); overlord merely picks
// the economically best one and can say WHY in public terms:
//
//   key (lexicographic, all exact arithmetic, no floats, no RNG):
//     1. aim mark   - the human's own public designation (FocusChange). All
//                     groups converge on it: focus fire, table-visible.
//     2. counter    - "woundAll": the sim ChipKey verbatim (healthy-first,
//                     healer last) - counter law outranks economics.
//     3. economy    - posture-keyed threat x wound-economy multiplier. On
//                     the M11b hero-card basis: expected damage of the real
//                     printed dice pool (weapon + known loadout items)
//                     against the engaging imperial figure's defense, or the
//                     real-tempo runner value under protect/stall. Cost/
//                     benefit instead of the raw score: a wounded hero still
//                     projects his printed threat until he is out, but costs
//                     only about half the engagement to finish - the
//                     danger-per-shot DENSITY doubles. Rational x2, exact.
//     4. reference  - no curated profile: the S1-S3 public proxy ranking
//                     (wounded, printed attack dice, printed health).
//     5. tiebreak   - squad row (stable, deterministic), then hero id.
//
// Golden vectors: Assets/Tests/Fixtures/tacai/overlord_focus.json (C#-side
// pinned via --record, same precedent as world_feed.json: this layer sits on
// the APP feed, the sim has no feed - its W components stay sim-vectored).
using System;
using System.Collections.Generic;
using System.Linq;

namespace TacticalAI.Core
{
    /// One living hero as the overlord sees him: public facts only.
    public sealed class FocusCandidate
    {
        public string HeroId;
        public int SquadRow;
        public HeroThreatProfile Profile;   // curated table hit or null
        public bool Wounded;                // feed HeroView (public tracker)
        public int Marks;                   // feed aim tokens (FocusChange)
        public int MarkedRound;
        public int AttackDice;              // printed card values (reference parity)
        public int Health;
        // M10b (Thomas revision 27.10): NO hero cell positions - the app
        // cannot see where a hero stands. The objective attribution is built
        // from public events only: objective progress happened, then a hero
        // finished his activation -> he gets the credit (feed vector).
        public bool ObjectiveActor;
        // M11b: his PRINTED hero card (tactical_hero_cards.json, all 21 app
        // heroes). Null only for rows outside the app roster (sim fixture
        // heroes) - those keep the M2a.4 curated fallback.
        public HeroCard Card;

        public bool Marked { get { return Marks > 0; } }
    }

    /// The decision, with its explainable public parts (ui.json localises).
    public sealed class FocusPick
    {
        public string HeroId;
        public bool Marked;
        public bool Wounded;
        public Rational? Threat;            // printed threat BEFORE economy
        public Rational? ThreatEconomic;    // after the wound-economy multiplier
        public string Basis;                // "mark"|"chip"|"card"|"economy"|"proxy"

        /// Comparable vector row (harness/NUnit pin), exact string fields.
        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "hero", HeroId },
                { "marked", Marked },
                { "wounded", Wounded },
                { "threat", Threat.HasValue ? Threat.Value.ToString() : null },
                { "threat_econ", ThreatEconomic.HasValue ? ThreatEconomic.Value.ToString() : null },
                { "basis", Basis },
            };
        }
    }

    public static class OverlordFocus
    {
        /// Wound economy: the engagement to FINISH a wounded hero costs about
        /// half (the table tracker says so - public), while his printed threat
        /// keeps projecting until he is out. Danger-per-shot density x2, exact
        /// rational - NEVER a hidden stat bonus (fairness canon).
        public static readonly Rational WoundEconomy = new Rational(2);

        public static Rational? ThreatBase(HeroThreatProfile p, string posture)
        {
            if (p == null) return null;
            if (posture == "protect" || posture == "stall")
                return new Rational(p.RunnerValue);       // mission-denial value
            if (posture == "attrition" || posture == "eliminate")
                return p.EvDamagePerRound;                // damage race
            return null;                                  // deny: node rules
        }

        public static Rational? ThreatEconomic(HeroThreatProfile p, string posture, bool wounded)
        {
            var t = ThreatBase(p, posture);
            if (!t.HasValue) return null;
            // Wound economy applies where REMOVING the threat is the goal
            // (damage race): finishing a wounded hero costs about half, so his
            // printed threat carries double finishing efficiency. Under
            // protect/stall the value is the runner's mission-denial threat -
            // being wounded makes him LESS of a denier, not more, so the
            // value stays the printed one (wounded still breaks ties, key 4).
            bool race = posture != "protect" && posture != "stall";
            return wounded && race ? t.Value * WoundEconomy : t;
        }

        /// Rank ALL living candidates (permutation only - fairness test pins
        /// that the id-multiset is input-equal for every skill level).
        /// posture/counter come from the public feed derivation; counter
        /// "woundAll" applies the sim ChipKey (profile.py port, vector truth).
        /// M11b: defenderDefense = the defense dice of the IMPERIAL FIGURE
        /// choosing its target (its printed deployment-card defense - public
        /// card data, identical against every candidate in one call; null ->
        /// no figure engaged yet, e.g. the round-level explain projection).
        public static List<FocusPick> Rank(IEnumerable<FocusCandidate> candidates,
                                               string posture, string counter,
                                               IList<string> defenderDefense = null)
        {
            var list = candidates == null ? new List<FocusCandidate>()
                                           : candidates.ToList();
            bool woundAll = counter == "woundAll";
            var table = HeroCards.Load();
            var rows = list.Select(c => new Row
            {
                C = c,
                Threat = ThreatOf(c, posture, defenderDefense, table),
                Econ = EconOf(c, posture, defenderDefense, table),
                Chip = woundAll && c.Profile != null ? c.Profile.ChipKey() : null,
            }).ToList();

            // M10b OBJECTIVE ACTOR: the hero credited with the last
            // objective progress raises his threat by EXACTLY +1 - "die
            // Bedrohung erhöht sich leicht", exact integer, public-event-
            // derived, never a hidden stat. (M11b: applies on the single
            // hero-card basis directly - the M10b purity guard, which used
            // to gate this to pure curation rounds, is gone.)
            foreach (var r in rows)
                if (r.C.ObjectiveActor && r.Econ.HasValue)
                {
                    r.Threat = r.Threat.Value + Rational.One;
                    r.Econ = r.Econ.Value + Rational.One;
                }

            var ranked = StableSort(rows, Compare);

            return ranked.Select(x => new FocusPick
            {
                HeroId = x.C.HeroId,
                Marked = x.C.Marked,
                Wounded = x.C.Wounded,
                Threat = x.Threat,
                ThreatEconomic = x.Econ,
                Basis = x.C.Marked ? "mark"
                        : (x.Chip != null ? "chip"
                        : (x.Econ != null ? (x.C.Card != null ? "card" : "economy")
                                          : "proxy")),
            }).ToList();
        }

        /// <summary>M11b threat v2 for one candidate: printed hero card
        /// first (all 21 app heroes), M2a.4 curated profile as fallback for
        /// rows outside the app roster.</summary>
        static Rational? ThreatOf(FocusCandidate c, string posture,
                                  IList<string> defenderDefense, HeroCardTable table)
        {
            if (c.Card != null)
                return HeroThreatModelV2.Threat(c.Card, table, c.Profile,
                                                c.Wounded, posture, defenderDefense);
            return ThreatBase(c.Profile, posture);
        }

        static Rational? EconOf(FocusCandidate c, string posture,
                                IList<string> defenderDefense, HeroCardTable table)
        {
            var t = ThreatOf(c, posture, defenderDefense, table);
            if (!t.HasValue) return null;
            // same wound-economy law as ThreatEconomic: doubling applies in
            // the REMOVING races only; on card rows under protect/stall the
            // wounded penalty is already REAL (card-back tempo, M11b).
            bool race = posture != "protect" && posture != "stall";
            return c.Wounded && race ? t.Value * WoundEconomy : t;
        }

        sealed class Row
        {
            public FocusCandidate C;
            public Rational? Threat;
            public Rational? Econ;
            public List<object> Chip;
        }

        /// Lexicographic compare of two rows, DESC (a before b => -1).
        static int Compare(Row a, Row b)
        {
            // 1. aim mark (public designation): marked first, then the most
            //    recently marked (the CURRENT designation, not an old one).
            if (a.C.Marked != b.C.Marked) return a.C.Marked ? -1 : 1;
            if (a.C.Marked)
            {
                int mr = b.C.MarkedRound.CompareTo(a.C.MarkedRound);
                if (mr != 0) return mr;
            }
            // 2. counter law (woundAll): sim ChipKey verbatim, higher first.
            //    M10b: an objective actor OUTRANKS the static role doctrine
            //    below - a hero who just did real mission damage is the live
            //    threat, ahead of any printed-role preference (only the
            //    human's aim mark, key 1, still overrides him).
            if (a.C.ObjectiveActor != b.C.ObjectiveActor)
                return a.C.ObjectiveActor ? -1 : 1;
            if (a.Chip != null || b.Chip != null)
            {
                int ck = CompareChip(a.Chip, b.Chip);
                if (ck != 0) return ck;
            }
            // 3. threat economy: posture-keyed printed value x wound economy,
            //    exact rationals. No-profile rows carry null -> they rank
            //    BELOW profiled rows on this key (unknown is not better).
            //    M10b OBJECTIVE ACTOR (pure curated rounds): the +1 above
            //    already moved him up this key; the tie-break fired at key 2.
            if (a.Econ != null || b.Econ != null)
            {
                int e = CompareEcon(a.Econ, b.Econ);
                if (e != 0) return e;
            }
            // 4. reference parity (no curated profile): wounded first, then
            //    M10a CAMPAIGN INVESTMENT (Thomas playtest 27.10): four
            //    expansion heroes share the neutral prior 10, so keys 1-3
            //    tie and the old chain fell straight to squad row - a
            //    predictable top-to-bottom kill queue. Items+skills from the
            //    campaign table are PUBLIC board data the Imperial player
            //    reads off the hero sheets: the better-equipped hero is
            //    objectively the bigger threat, so EquipmentIndex (weighted
            //    tier x cost) then AbilityIndex (xp sum) break the tie BEFORE
            //    any row order. Wounded keeps priority (finisher doctrine).
            if (a.C.Wounded != b.C.Wounded) return a.C.Wounded ? -1 : 1;
            var eqA = a.C.Profile != null ? a.C.Profile.EquipmentIndex : Rational.Zero;
            var eqB = b.C.Profile != null ? b.C.Profile.EquipmentIndex : Rational.Zero;
            int eq = eqB.CompareTo(eqA);                                   // desc
            if (eq != 0) return eq;
            int ab = (b.C.Profile != null ? b.C.Profile.AbilityIndex : 0)
                   .CompareTo(a.C.Profile != null ? a.C.Profile.AbilityIndex : 0);
            if (ab != 0) return ab;                                       // desc
            int ad = b.C.AttackDice.CompareTo(a.C.AttackDice);
            if (ad != 0) return ad;
            int hp = b.C.Health.CompareTo(a.C.Health);
            if (hp != 0) return hp;
            // 5. stable tie-chain: squad row left-to-right, then id.
            int row = a.C.SquadRow.CompareTo(b.C.SquadRow);
            if (row != 0) return row;
            return string.CompareOrdinal(a.C.HeroId ?? "", b.C.HeroId ?? "");
        }

        /// ChipKey = [int class, int runner, Rational ev] (HeroProfiles.cs
        /// port of the sim chip_key). Higher leads; missing chip loses.
        static int CompareChip(List<object> a, List<object> b)
        {
            if (a == null) return 1;
            if (b == null) return -1;
            for (int i = 0; i < a.Count && i < b.Count; i++)
            {
                object x = a[i], y = b[i];
                int c;
                if (x is Rational && y is Rational)
                    c = ((Rational)y).CompareTo((Rational)x);          // desc
                else if (x is Rational)
                    c = -1;                                            // number leads
                else if (y is Rational)
                    c = 1;
                else
                    c = Convert.ToInt32(y, System.Globalization.CultureInfo.InvariantCulture)
                      .CompareTo(Convert.ToInt32(x, System.Globalization.CultureInfo.InvariantCulture));
                if (c != 0) return c;
            }
            return b.Count.CompareTo(a.Count);
        }

        static int CompareEcon(Rational? a, Rational? b)
        {
            if (!a.HasValue) return 1;
            if (!b.HasValue) return -1;
            return b.Value.CompareTo(a.Value);                         // desc
        }

        /// Stable insertion sort (candidate sets are hero-sized; avoids the
        /// List.Sort instability question entirely - fairness demands the
        /// tie-chain in Compare to decide, nothing else).
        static List<T> StableSort<T>(List<T> input, Func<T, T, int> cmp)
        {
            var outp = new List<T>();
            foreach (var item in input)
            {
                int lo = 0, hi = outp.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (cmp(outp[mid], item) <= 0) lo = mid + 1;
                    else hi = mid;
                }
                outp.Insert(lo, item);
            }
            return outp;
        }
    }
}
