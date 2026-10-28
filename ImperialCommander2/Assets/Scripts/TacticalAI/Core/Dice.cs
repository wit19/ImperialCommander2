// Port of sim/dice.py — fair dice / damage model (concept section 4, T2
// damage seam).
//
// FAIRNESS LAW: every input to a dice roll comes from printed card data only -
// the attacker's dice pool is card.attacks (plus printed keyword dice bonuses
// like "+1 {G}"), surge values and defense dice come from the printed
// surge/defense lists (units/surges_core.json, generated verbatim from the IC2
// repo CardData). No bonuseffects.json-style hidden modifier exists anywhere
// in this module.
//
// Die-face tables
// ---------------
// IA prints die faces as icons (the extracted rulebook PDF has no face text),
// so the multiset of faces was solved EXACTLY from published per-die
// probabilities (both sources cited in docs/balance_sim.md):
//
//   * "Mitigate: A Primer On Imperial Assault's Dice", IA Continuity Project
//     (2020-06-29) - per-damage/surge/accuracy probability statements;
//   * imperial-assault.fandom.com/wiki/Dice - per-die summary (highest/lowest
//     damage, min accuracy, average accuracy red 0.0 / yellow 1.0 /
//     green 1.67 / blue 3.17).
//
// The per-face icon LAYOUT on mixed faces is an assumption where sources do
// not pin it down; what is pinned - and what this module uses - is the exact
// face multiset for damage/surge/accuracy. tests/test_dice.py asserts every
// published probability against these tables, so a wrong face set fails.
//
// Simplifications (documented, calibration knobs in docs/balance_sim.md):
//   * surge spending: at most ONE damage surge ability and ONE accuracy surge
//     ability per attack, used greedily (damage first) - matches most printed
//     surge lines like "{B}: +2 {H}"; Blast/Cleave/Pierce/Stun/Recover surge
//     sides are NOT valued (conservative against the imperial side);
//   * keyword bonuses: "+N {H}" adds N damage, "+N Accuracy" adds static
//     accuracy, "+1 {G|R|B|Y}" adds one die of that color (printed pool);
//   * Pierce/evade interactions are ignored (defender-favourable simplification);
//   * White defense "dodge" counts as 2 blocks + cancels the attacker's surge
//     bonus; the heroes' printed "Cunning" synergy is folded into the White die
//     expectation (evade faces -> +1 block) and documented.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TacticalAI.Core
{
    /// Python-formatting helpers shared by the ported modules (exact
    /// f-string semantics for golden-vector byte parity).
    internal static class Py
    {
        /// f"{v:.2f}" — Python formats the EXACT binary value with
        /// round-half-to-even. The decimal cast is exact for these
        /// magnitudes, so Math.Round on decimal reproduces it.
        public static string F2(double v)
        {
            decimal d = (decimal)v;
            d = Math.Round(d, 2, MidpointRounding.ToEven);
            return d.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// Python list repr for a list of strings: ['a', 'b']
        public static string ListRepr(IList<string> items)
        {
            var parts = new string[items.Count];
            for (int i = 0; i < items.Count; i++) parts[i] = "'" + items[i] + "'";
            return "[" + string.Join(", ", parts) + "]";
        }

        /// Python dict repr for {str: int}: {'MA': 6, 'A': 2}
        public static string DictRepr(Dictionary<string, int> d)
        {
            var parts = new List<string>();
            foreach (var kv in d) parts.Add("'" + kv.Key + "': " + kv.Value);
            return "{" + string.Join(", ", parts) + "}";
        }
    }

    public static class Dice
    {
        // -------------------------------------------------------------------
        // printed die faces: (damage, surge, accuracy) pips per face
        // -------------------------------------------------------------------
        public static readonly Dictionary<string, int[][]> AttackDieFaces =
            new Dictionary<string, int[][]>
        {
            // Red: blank / 2d / 2d / 2d+1s / 3d / 3d
            //   P(>=3 dmg)=2/6, P(>=2 dmg)=5/6, P(>=1 surge)=1/6, P(exact 2d)=2/6
            { "Red", new[] { new[] { 0, 0, 0 }, new[] { 2, 0, 0 }, new[] { 2, 0, 0 },
                              new[] { 2, 1, 0 }, new[] { 3, 0, 0 }, new[] { 3, 0, 0 } } },
            // Green: 1d+1s x2 / 2d x2 / 2d+1s / 0d+1s  (accuracy >=1 on every face)
            //   P(>=2 dmg)=1/2, P(1d+1s)=1/3, P(>=1 surge)=1/2, avg acc=1.67
            { "Green", new[] { new[] { 1, 1, 2 }, new[] { 1, 1, 2 }, new[] { 2, 0, 1 },
                               new[] { 2, 0, 2 }, new[] { 2, 0, 2 }, new[] { 0, 1, 1 } } },
            // Blue: 2d x2 / 1d+1s / 0d+1s / blank x2  (accuracy >=2 on every face)
            //   P(>=2 dmg)=1/3, P(1d+1s)=1/6, P(>=1 surge)=1/3, avg acc=3.17
            { "Blue", new[] { new[] { 2, 0, 2 }, new[] { 2, 0, 2 }, new[] { 1, 1, 3 },
                              new[] { 0, 1, 2 }, new[] { 0, 0, 4 }, new[] { 0, 0, 6 } } },
            // Yellow: 2d / 1d+1s / 1d+2s / 1s x2 / blank
            //   P(2 dmg)=1/6, P(>=1d & >=1s)=1/3, P(>=1 surge)=2/3, avg acc=1.0
            { "Yellow", new[] { new[] { 2, 0, 0 }, new[] { 1, 1, 1 }, new[] { 1, 2, 1 },
                                new[] { 0, 1, 2 }, new[] { 0, 1, 1 }, new[] { 0, 0, 0 } } },
            // Grey: IC2-only homebrew attack colour (CardData villains IG-88 x2,
            // General Weiss x3). No printed face table exists anywhere - not in the
            // IA reference rules, not in the IC2 code (the runtime resolves combat
            // manually; grey1-6.prefab are visual only). T3 (M4a) models it with a
            // PINNED proxy = the Blue die: moderate average damage (5/6), no burst,
            // no extra surge - i.e. it never inflates villain pressure above a
            // printed colour (fairness direction required by concept 2B.6).
            // tests/test_t3_decks.py pins these expectations; a printed face source
            // would replace the proxy in one edit.
            { "Grey", new[] { new[] { 2, 0, 2 }, new[] { 2, 0, 2 }, new[] { 1, 1, 3 },
                              new[] { 0, 1, 2 }, new[] { 0, 0, 4 }, new[] { 0, 0, 6 } } },
        };

        // Defense dice: blocks per face; White additionally has one dodge face (index).
        public static readonly Dictionary<string, object[]> DefenseDieFaces =
            new Dictionary<string, object[]>
        {
            // Black: P(3 blocks)=1/6, P(>=2 blocks)=1/2
            { "Black", new object[] { new[] { 0, 0, 1, 2, 2, 3 }, null } },
            // White: one dodge face (counts as 2 blocks here) + 1b+1e x2 (the evade
            // pips feed hero "Cunning": +1 block each, folded into the face values)
            { "White", new object[] { new[] { 0, 0, 1, 1, 2, 2 }, (int)5 } },
        };

        static readonly string[] DieColorsArr = { "Red", "Green", "Blue", "Yellow", "Grey" };

        /// Die colour iteration order = attack-die table key order (Python
        /// DIE_COLORS = tuple(ATTACK_DIE_FACES)).
        public static IEnumerable<string> DieColors { get { return DieColorsArr; } }

        static readonly Dictionary<string, Dictionary<string, Rational>> ExpectCache =
            new Dictionary<string, Dictionary<string, Rational>>();

        static Rational Mean(IEnumerable<int> values)
        {
            long sum = 0; int n = 0;
            foreach (var v in values) { sum += v; n++; }
            return n > 0 ? new Rational(sum, n) : Rational.Zero;
        }

        static Rational GetOrZero(Dictionary<int, Rational> d, int key)
        {
            Rational v;
            return d.TryGetValue(key, out v) ? v : Rational.Zero;
        }

        public static Dictionary<string, Rational> DieExpectations(string color)
        {
            Dictionary<string, Rational> cached;
            if (ExpectCache.TryGetValue(color, out cached)) return cached;
            int[][] faces;
            if (!AttackDieFaces.TryGetValue(color, out faces))
                throw new KeyNotFoundException("unknown die colour " + color);
            int surgeGe1 = 0;
            foreach (var f in faces) if (f[1] > 0) surgeGe1++;
            var res = new Dictionary<string, Rational>
            {
                { "dmg", Mean(Dmg(faces)) },
                { "surge", Mean(Surge(faces)) },
                { "acc", Mean(Acc(faces)) },
                { "p_surge_ge1", new Rational(surgeGe1, 6) },
            };
            ExpectCache[color] = res;
            return res;
        }

        static IEnumerable<int> Dmg(int[][] faces) { foreach (var f in faces) yield return f[0]; }
        static IEnumerable<int> Surge(int[][] faces) { foreach (var f in faces) yield return f[1]; }
        static IEnumerable<int> Acc(int[][] faces) { foreach (var f in faces) yield return f[2]; }

        public static Rational DefenseExpectation(string color)
        {
            object[] entry;
            if (!DefenseDieFaces.TryGetValue(color, out entry))
                throw new KeyNotFoundException("unknown defence die colour " + color);
            var faces = (int[])entry[0];
            var dodge = entry[1] as int?;
            int sum = 0;
            for (int i = 0; i < faces.Length; i++)
                sum += dodge.HasValue && i == dodge.Value ? 2 : faces[i];   // dodge face = 2 blocks
            return new Rational(sum, faces.Length);
        }

        // ---- keyword regexes (printed text only) --------------------------------
        static readonly Regex KwDmg = new Regex(@"\+(\d+)\s*\{H\}");
        static readonly Regex KwAcc = new Regex(@"\+(\d+)\s*Accuracy");
        static readonly Regex KwDie = new Regex(@"\+(\d+)\s*\{(G|R|B|Y)\}");
        static readonly Regex SurgeDmg = new Regex(@"\{B\}:.*?\+(\d+)\s*\{H\}");
        static readonly Regex SurgeAcc = new Regex(@"\{B\}:.*?\+(\d+)\s*Accuracy");
        static readonly Dictionary<string, string> ColorByShort =
            new Dictionary<string, string> { { "G", "Green" }, { "R", "Red" },
                                             { "B", "Blue" }, { "Y", "Yellow" } };

        /// (extra flat damage, extra static accuracy, extra dice colors) —
        /// port of _keyword_bonuses.
        public static void KeywordBonuses(IEnumerable<string> keywords,
                                          out int extraDamage, out int extraAccuracy,
                                          out List<string> extraDice)
        {
            extraDamage = 0; extraAccuracy = 0; extraDice = new List<string>();
            if (keywords == null) return;
            foreach (var kw in keywords)
            {
                var m = KwDmg.Match(kw);
                if (m.Success) extraDamage += int.Parse(m.Groups[1].Value);
                m = KwAcc.Match(kw);
                if (m.Success) extraAccuracy += int.Parse(m.Groups[1].Value);
                m = KwDie.Match(kw);
                if (m.Success)
                {
                    var color = ColorByShort[m.Groups[2].Value];
                    int n = int.Parse(m.Groups[1].Value);
                    for (int i = 0; i < n; i++) extraDice.Add(color);
                }
            }
        }

        /// Exact distribution of total surge pips over a dice pool
        /// (port of _surge_pip_distribution; convolution is commutative, so
        /// pool order never matters).
        public static Dictionary<int, Rational> SurgePipDistribution(IEnumerable<string> colors)
        {
            var dist = new Dictionary<int, Rational> { { 0, Rational.One } };
            foreach (var c in colors)
            {
                var perDie = new Dictionary<int, Rational>();
                foreach (var f in AttackDieFaces[c])
                    perDie[f[1]] = GetOrZero(perDie, f[1]) + Rational.One / 6;
                var nxt = new Dictionary<int, Rational>();
                foreach (var kv1 in dist)
                    foreach (var kv2 in perDie)
                        nxt[kv1.Key + kv2.Key] =
                            GetOrZero(nxt, kv1.Key + kv2.Key) + kv1.Value * kv2.Value;
                dist = nxt;
            }
            return dist;
        }

        public static Rational PrintedEv(DeploymentCard card)
        {
            // Port of order_plan.printed_ev: exact expected damage of the
            // card's printed dice (no surges); unknown colours are skipped
            // (Python catches KeyError per face).
            var ev = Rational.Zero;
            var attacks = card.Attacks ?? Array.Empty<string>();
            foreach (var color in attacks)
            {
                if (!AttackDieFaces.ContainsKey(color)) continue;
                ev += DieExpectations(color)["dmg"];
            }
            return ev;
        }

        /// Exact damage/surge/accuracy gate bundle for one figure's attack —
        /// printed data only. Mirrors the frozen AttackStats dataclass.
        public static AttackStats BuildAttackStats(IEnumerable<string> attacks,
                                                   IEnumerable<string> keywords,
                                                   bool isRanged,
                                                   IEnumerable<string> surgeLines = null,
                                                   IEnumerable<string> defense = null)
        {
            var colors = new List<string>();
            if (attacks != null) foreach (var a in attacks) colors.Add(a);
            KeywordBonuses(keywords, out int kwDmg, out int kwAcc, out var extraDice);
            colors.AddRange(extraDice);
            int surgeDmg = 0, surgeAcc = 0;
            if (surgeLines != null)
                foreach (var s in surgeLines)
                {
                    var m = SurgeDmg.Match(s);
                    if (m.Success) surgeDmg = Math.Max(surgeDmg, int.Parse(m.Groups[1].Value));
                    m = SurgeAcc.Match(s);
                    if (m.Success) surgeAcc = Math.Max(surgeAcc, int.Parse(m.Groups[1].Value));
                }
            var dmgEv = Rational.Zero;
            var accEv = Rational.Zero;
            foreach (var c in colors)
            {
                dmgEv += DieExpectations(c)["dmg"];
                accEv += DieExpectations(c)["acc"];
            }
            // surge spending expectation (exact over the pip distribution):
            // one damage ability, then one accuracy ability, greedy.
            var pipDist = SurgePipDistribution(colors);
            var sDmgEv = Rational.Zero;
            var sAccEv = Rational.Zero;
            foreach (var kv in pipDist)
            {
                int left = kv.Key;
                var prob = kv.Value;
                if (surgeDmg != 0 && left >= 1) { sDmgEv += prob * surgeDmg; left -= 1; }
                if (surgeAcc != 0 && left >= 1) { sAccEv += prob * surgeAcc; }
            }
            var accTotalEv = accEv + kwAcc + sAccEv;
            var blocksEv = Rational.Zero;
            if (defense != null)
                foreach (var d in defense) blocksEv += DefenseExpectation(d);
            return new AttackStats
            {
                Colors = colors,
                FlatDamage = kwDmg,
                StaticAccuracy = kwAcc,
                SurgeDamage = surgeDmg,
                SurgeAccuracy = surgeAcc,
                IsRanged = isRanged,
                DefenseDice = defense == null ? new List<string>() : new List<string>(defense),
                DmgEv = dmgEv,
                AccEv = accTotalEv,
                AccGate = (int)((accTotalEv + Rational.One / 2).Num /
                                (accTotalEv + Rational.One / 2).Den),   // int(Fraction + 1/2)
                SurgeDmgEv = sDmgEv,
                BlocksEv = blocksEv,
            };
        }

        /// <summary>Continuous (untruncated) expected damage of the frozen
        /// pairing/hit formula — M11b threat v2 runs the SAME hit formula the
        /// imperial attack model uses (EV branch of ResolveAttack), only
        /// without the integer round-down. pierce > 0 removes that many
        /// expected block results (printed "Pierce N"). accOk=false (ranged
        /// gate below the distance) pairs to 0 damage. rng-free and
        /// position-free: distance is a plain parameter.</summary>
        public static Rational ExpectedDamage(AttackStats attacker,
                                              AttackStats defender, int distance,
                                              int pierce = 0)
        {
            Rational v, b;
            return ExpectedDamage(attacker, defender, distance, pierce, out v, out b);
        }

        static Rational ExpectedDamage(AttackStats attacker, AttackStats defender,
                                       int distance, int pierce,
                                       out Rational valid, out Rational blocks)
        {
            bool accOk = !attacker.IsRanged || attacker.AccGate >= distance;
            var raw = attacker.AttackEv + attacker.FlatDamage;
            // IA pairing rule (RefGuide HIT icon): every damage pip needs a
            // hit pip; ranged attacks spend hit pips for the distance first.
            var accAvail = attacker.AccEv - (attacker.IsRanged ? distance : 0);
            valid = Rational.Min(raw, Rational.Max(Rational.Zero, accAvail));
            blocks = accOk ? (defender == null ? Rational.Zero : defender.BlocksEv)
                           : Rational.Zero;
            if (pierce > 0) blocks = Rational.Max(Rational.Zero, blocks - pierce);
            return Rational.Max(Rational.Zero, valid - blocks);
        }

        /// Resolve one attack. rng=null -> expected-value mode (deterministic);
        /// rng given -> stochastic mode (rolls printed faces).
        ///
        /// Returns {hit, damage, frac_left, raw, blocks, why}. Ranged attacks
        /// additionally need accuracy >= distance (RefGuide ACCURACY); damage
        /// is 0 on a miss.
        public static Dictionary<string, object> ResolveAttack(AttackStats attacker,
                                                               AttackStats defender,
                                                               int distance, IRandom rng)
        {
            if (rng == null)
            {
                bool accOk = !attacker.IsRanged || attacker.AccGate >= distance;
                var raw = attacker.AttackEv + attacker.FlatDamage;
                Rational valid, blocks;
                var dmg = ExpectedDamage(attacker, defender, distance, 0,
                                         out valid, out blocks);
                // deterministic integer damage: round down; a positive fractional
                // remainder is carried by the caller via frac_left (fair long-run EV)
                long whole = (long)(dmg.Num / dmg.Den);
                var frac = dmg - new Rational(whole);
                return new Dictionary<string, object>
                {
                    { "hit", accOk },
                    { "damage", whole },
                    { "frac_left", frac },
                    { "raw", raw },
                    { "blocks", blocks },
                    { "why", "EV raw " + Py.F2(raw.ToDouble()) + " pair-> "
                             + Py.F2(valid.ToDouble()) + " - blocks "
                             + Py.F2(blocks.ToDouble()) + " over dist " + distance
                             + " (acc gate " + attacker.AccGate + ")" },
                };
            }
            // stochastic mode
            int rolledD = attacker.FlatDamage, rolledS = 0, rolledA = 0;
            foreach (var c in attacker.Colors)
            {
                var face = AttackDieFaces[c][rng.RandBelow(6)];
                rolledD += face[0]; rolledS += face[1]; rolledA += face[2];
            }
            int surgeUsedDmg = 0, surgeUsedAcc = 0;
            if (attacker.SurgeDamage != 0 && rolledS >= 1)
            { rolledD += attacker.SurgeDamage; rolledS -= 1; surgeUsedDmg = attacker.SurgeDamage; }
            if (attacker.SurgeAccuracy != 0 && rolledS >= 1)
            { rolledA += attacker.SurgeAccuracy; rolledS -= 1; surgeUsedAcc = attacker.SurgeAccuracy; }
            if (attacker.IsRanged && rolledA < distance)
                return new Dictionary<string, object>
                {
                    { "hit", false },
                    { "damage", 0L },
                    { "frac_left", Rational.Zero },
                    { "raw", new Rational(rolledD) },
                    { "blocks", Rational.Zero },
                    { "why", "miss: " + rolledA + " accuracy < distance " + distance },
                };
            // IA pairing rule: hit pips cover the distance first, then every
            // damage pip needs one hit pip.
            int blocksI = 0;
            bool dodge = false;
            foreach (var dcol in defender.DefenseDice)
            {
                var entry = DefenseDieFaces[dcol];
                var faces = (int[])entry[0];
                var dodgeIdx = entry[1] as int?;
                int idx = rng.RandBelow(6);
                if (dodgeIdx.HasValue && idx == dodgeIdx.Value) { dodge = true; blocksI += 2; }
                else blocksI += faces[idx];
            }
            if (dodge) { rolledD -= surgeUsedDmg; surgeUsedDmg = 0; }   // dodge cancels the surge ability
            int accAvailR = rolledA - (attacker.IsRanged ? distance : 0);
            int validD = Math.Min(rolledD, Math.Max(0, accAvailR));
            int dmgR = Math.Max(0, validD - blocksI);
            return new Dictionary<string, object>
            {
                { "hit", true },
                { "damage", (long)dmgR },
                { "frac_left", Rational.Zero },
                { "raw", new Rational(rolledD) },
                { "blocks", new Rational(blocksI) },
                { "why", "rolled " + rolledD + " dmg/" + rolledA + " hit, paired "
                         + validD + ", " + blocksI + " blocked" + (dodge ? " (dodge)" : "") },
            };
        }
    }

    /// Port of the frozen AttackStats dataclass (sim/dice.py).
    public sealed class AttackStats
    {
        public List<string> Colors = new List<string>();
        public int FlatDamage;          // "+N {H}" keyword
        public int StaticAccuracy;      // "+N Accuracy" keyword
        public int SurgeDamage;         // best printed "{B}: +N {H}"
        public int SurgeAccuracy;       // best printed "{B}: +N Accuracy"
        public bool IsRanged;
        public List<string> DefenseDice = new List<string>();
        // derived (expected values, exact rationals)
        public Rational DmgEv = Rational.Zero;
        public Rational AccEv = Rational.Zero;
        public int AccGate = 0;         // ranged: max distance the pool hits on average
        public Rational SurgeDmgEv = Rational.Zero;
        public Rational BlocksEv = Rational.Zero;

        /// Expected raw damage INCLUDING surge spending (before defense).
        public Rational AttackEv { get { return DmgEv + SurgeDmgEv; } }
    }
}
