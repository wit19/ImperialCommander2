// M11e (tactical AI, ab S4): the printed {A} SPECIAL ACTIONS of the single
// villain cards (CardData/villains.json DG071-DG090, all size 1) become a real
// third activation option next to move/attack. This lifts the old M6 decision
// ("Sonder-Aktionen bewusst nicht") for the tactical mode from S4 up - now
// that the ability texts exist in the language files (M11c-i) and the curated
// glyph semantics live in wiki_data/keyword_table.json ("g" = glyph; "{A}"
// means: it IS an action). Only abilities whose printed name carries the
// {A} glyph qualify - {B} reactions (Truppenkommando, Bedrohung, Punishment,
// Shocking Palm, Stalk Prey) and trigger abilities (Negotiate) are NOT
// actions and stay out of scope, exactly as the glyph table says.
//
// Same curation law as CardKeywords (M11c2): the table is keyed by PRINTED
// CARD ID (DGxxx, language-independent) with the abilities[] array index for
// the localized UI join (EN and De arrays are index-aligned). Fairness law
// holds: the cards lie OPEN on the table - the option names their own printed
// conditions, the model never filters heroes by hidden information and never
// rolls dice.
//
// THE YARDSTICK (scope: "schlaegt den regulaeren Wuerfel-Angriff im
// ThreatEconomy-Score - gleiche Score-Logik wie Bewegung vs. Angriff heute"):
// the same printed-expectation calibration the ORD decks are calibrated with
// (order_decks.json card_schema "cards_ev" = Dice.PrintedEv, the exact
// rational dice-damage expectation WITHOUT defense/pairing - no RNG). The
// regular attack contributes PrintedEv(card) when it is LEGAL at the
// activation position, 0 when it is not; an option contributes its printed
// fixed expectation:
//   * attack_like  - a fixed damage number read straight off the card text
//       (Machtblitz 3, Machtwuergegriff 2, Invasive 1, Flamethrower 1) or the
//       dice-pool mean of the printed replacement pool (Lichtschwertwurf
//       1R1Y = 8/3). Accuracy bonuses do NOT add damage (reach only) and the
//       non-damage extras (Strain {C}, Geschwaecht, Blutung, self-Focus,
//       Blast splash, area "each other figure") are NOT counted - there is no
//       strain/condition currency in the damage-race model (M11d found the
//       app does not even track hero strain markers), so every option value
//       is a CONSERVATIVE floor of the printed effect.
//   * order_like   - curated pressure signal on the focus target, expressed
//       in the same damage-expected-value unit (see the table comments). It
//       never fires a real interrupt in the game flow - it is a planning
//       signal only, exactly as M11e scope defines.
//   * utility      - value 0: shown when the (public) threat balance allows
//       paying for it; never competes for the single option block against a
//       qualifying attack/order option and never changes target selection.
// The gate is STRICT: the option must be > the legal attack value. A tie
// keeps the plain dice attack in front (harness-pinned with a real tie:
// Kayns pressure 3 vs printed 19/6 - and Palpatines 3 vs 10/3 adjacent).
//
// TWO CALLERS, ONE TABLE, DOCUMENTED DEGRADATION (concept 4.2 deviation
// "the app has no hex world - the human reads the board"):
//   * PickForPosition(world, fig, target, balance): the full law - attack
//     and option legality are checked against the real distance/LOS. Used by
//     the harness today and by the live app once the WorldProvider seam is
//     fed (same seam M5 documents).
//   * PickLive(card, balance): the popup path without a world. The regular
//     attack is assumed legal (PrintedEv, the printed Wenn-Dann blocks make
//     the same assumption - only the human sees positions). An attack_like
//     option may still enter when its printed reach strictly exceeds what
//     the attack can ever cover (melee Vader vs range-4 Machtblitz): in that
//     configuration the model CANNOT declare the attack better - there are
//     board states (hero out of melee reach) where the option is strictly
//     better - and the block text carries the printed condition for the
//     human to evaluate. Without a reach surplus the strict EV gate applies.
//     order_like/utility need no positions at all.
//
// Schema law (same limit M11c2 pinned for MultiAttack/FocusFire): the ORD
// card schema allows ONE attack block per activation, so MULTI-attack {A}
// abilities (Grausamkeit DG072/DG074, Raketensalve DG089) stay UNUSED and
// Wahlloser Beschuss DG080 adds a new dice mechanic + hits adjacent friend
// figures - also unused. The Unused table below pins every printed {A} that
// gets no block, with a machine-readable reason.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    /// One curated villain {A} option (or an unused print, via UnusedEntry).
    public sealed class VillainOption
    {
        public string CardId;
        public string Key;                  // stable id for pins/harness
        public string EffectClass;          // attack_like | order_like | utility
        public string EnName;               // printed EN ability name (with glyph)
        public int AbilityIndex;            // index in the card's abilities[] (UI join)
        public Rational Value = Rational.Zero;   // printed-yardstick EV / pressure
        public int Reach = 1;               // attack_like legality range in spaces
        public bool NeedsLos;               // printed "in line of sight" clause
        public int BalanceNeeded;           // utility: threat balance the Empire must hold
        public string[] Dice;               // attack_like dice variant (else null)
        public string[] OptionKeywords;     // accuracy-only keywords for the gate

        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "key", Key },
                { "card", CardId },
                { "class", EffectClass },
                { "ability_index", (long)AbilityIndex },
                { "value", Value.ToString() },
                { "reach", (long)Reach },
                { "needs_los", NeedsLos },
                { "balance_needed", (long)BalanceNeeded },
                { "dice", Dice == null ? null : new List<object>(Dice) },
            };
        }
    }

    /// The result of one option evaluation (harness-pinnable, pure data).
    public sealed class OptionPick
    {
        public VillainOption Option;        // null = no option block this activation
        public Rational AttackValue;        // printed EV of the regular attack here
        public string Mode;                 // "position" | "live"
        public long Distance = -1;          // echo of what the law saw
        public bool? HasLos;

        public Dictionary<string, object> AsDict()
        {
            return new Dictionary<string, object>
            {
                { "option", Option == null ? null : Option.AsDict() },
                { "attack_value", AttackValue.ToString() },
                { "mode", Mode },
                { "distance", Distance },
                { "los", (object)(HasLos.HasValue ? HasLos.Value : (object)"unknown") },
            };
        }
    }

    public static class VillainOptions
    {
        // Effect classes (scope M11e: EXACTLY these three).
        public const string AttackLike = "attack_like";
        public const string OrderLike  = "order_like";
        public const string Utility    = "utility";

        /// Reach sentinel for printed clauses without a number
        /// ("a target ... in line of sight" - Machtwuergegriff).
        public const int UnboundedReach = 99;

        /// The melee reach of a card without the Reach keyword (printed 1;
        /// Reach would double it - no option carrier prints Reach).
        public const int MeleeReach = 1;

        // ------------------------------------------------------------- table
        // Curated by printed card id; sorted by Key ordinal (the selection
        // tie-break). Source: CardData/villains.json abilities[] (EN names),
        // De array at the same index, wiki_data/keyword_table.json glyph "{A}".
        static readonly VillainOption[] Table = new[]
        {
            new VillainOption
            {
                CardId = "DG072", Key = "dg072_force_choke", EffectClass = AttackLike,
                EnName = "{A} Force Choke", AbilityIndex = 1,
                // "...suffers 2 {H} and 1 {C}": fixed damage 2; the strain pip
                // is not damage currency -> conservative floor 2.
                Value = 2, Reach = UnboundedReach, NeedsLos = true,
            },
            new VillainOption
            {
                CardId = "DG073", Key = "dg073_generals_orders", EffectClass = OrderLike,
                EnName = "{A} General's Orders", AbilityIndex = 0,
                // 2 friendly figures interrupt-move: reposition pressure, no
                // direct damage -> 1.
                Value = 1,
            },
            new VillainOption
            {
                CardId = "DG075", Key = "dg075_firing_squad", EffectClass = OrderLike,
                EnName = "{A} Firing Squad", AbilityIndex = 0,
                // 2 adjacent friendly TROOPER figures interrupt-attack the
                // SAME figure: 2 x a trooper's printed attack - curated to a
                // conservative 3 (troopers must also be adjacent, the model
                // does not verify that - so the floor stays below the raw
                // 2 x Blue+Green mean 26/6).
                Value = 3,
            },
            new VillainOption
            {
                CardId = "DG078", Key = "dg078_bombardment", EffectClass = OrderLike,
                EnName = "{A} Bombardment", AbilityIndex = 1,
                // one adjacent friendly figure interrupt-attacks (Blast):
                // curated floor 2 (trooper mean ~13/6 + blast not counted).
                Value = 2,
            },
            new VillainOption
            {
                CardId = "DG079", Key = "dg079_activate_agent", EffectClass = Utility,
                EnName = "{A} Activate Agent", AbilityIndex = 0,
                // Resolve an optional deployment with -1 cost: only worth a
                // block when the (public) balance can pay for a deployment;
                // 2 = cheapest discounted trooper/spy group line.
                Value = 0, BalanceNeeded = 2,
            },
            new VillainOption
            {
                CardId = "DG081", Key = "dg081_bully", EffectClass = AttackLike,
                EnName = "{A} Bully", AbilityIndex = 0,
                // "suffers 3 {C}": pure strain -> 0 damage currency. Carried
                // so the never-selected law is visible and pinned; with the
                // strict > gate this can never take the block.
                Value = 0, Reach = 3, NeedsLos = false,
            },
            new VillainOption
            {
                CardId = "DG081", Key = "dg081_execution_order", EffectClass = OrderLike,
                EnName = "{A} Execution Order", AbilityIndex = 1,
                // another EXECUTOR figure interrupt-attacks: curated floor 2.
                Value = 2,
            },
            new VillainOption
            {
                CardId = "DG081", Key = "dg081_financial_incentive", EffectClass = Utility,
                EnName = "{A} Financial Incentive", AbilityIndex = 2,
                // friendly figure recovers 2 {H} + Focused: sustain, no
                // pressure, no cost gate -> utility.
                Value = 0, BalanceNeeded = 0,
            },
            new VillainOption
            {
                CardId = "DG081", Key = "dg081_plot", EffectClass = Utility,
                EnName = "{A} Plot", AbilityIndex = 3,
                // "increase the threat by 2": pure threat-economy gain.
                Value = 0, BalanceNeeded = 0,
            },
            new VillainOption
            {
                CardId = "DG082", Key = "dg082_flamethrower", EffectClass = AttackLike,
                EnName = "{A} Flamethrower", AbilityIndex = 0,
                // "each other figure ... suffers 1 {H} and 1 {C} and is
                // Weakened": fixed 1 vs the focus figure; area + friendly
                // exposure + strain/weakened not counted -> floor 1.
                Value = 1, Reach = 2, NeedsLos = false,
            },
            new VillainOption
            {
                CardId = "DG083", Key = "dg083_force_lightning", EffectClass = AttackLike,
                EnName = "{A} Force Lightning", AbilityIndex = 2,
                // "...suffers 3 {H} and is Weakened": fixed 3 (blast and
                // weakened not counted). Printed clause: within 4 + LOS.
                Value = 3, Reach = 4, NeedsLos = true,
            },
            new VillainOption
            {
                CardId = "DG087", Key = "dg087_lightsaber_throw", EffectClass = AttackLike,
                EnName = "{A} Lightsaber Throw", AbilityIndex = 1,
                // "perform a ranged attack with 1 red and 1 yellow die with
                // +3 accuracy": pool mean 2 + 2/3 = 8/3; the accuracy bonus
                // extends reach (gate below), never damage.
                Value = Dice.DieExpectations("Red")["dmg"]
                      + Dice.DieExpectations("Yellow")["dmg"],
                Dice = new[] { "Red", "Yellow" }, OptionKeywords = new[] { "+3 Accuracy" },
                Reach = 4, NeedsLos = true,
            },
            new VillainOption
            {
                CardId = "DG088", Key = "dg088_invasive_procedure", EffectClass = AttackLike,
                EnName = "{A} Invasive Procedure", AbilityIndex = 2,
                // "adjacent figure suffers 1 {H} + 1 {C} + Bleeding, 0-0-0
                // gains Focused": fixed 1 vs the focus figure; extras not
                // counted -> floor 1; printed clause: adjacent.
                Value = 1, Reach = 1, NeedsLos = false,
            },
        };

        /// Printed {A} abilities that get NO option block (scope 2: only
        /// deterministic, dice-free effects). Machine-readable reasons, keyed
        /// by card id + printed EN name - the closing comment expands them.
        public static readonly string[][] Unused = new[]
        {
            new[] { "DG072", "{A} Brutality", "multi_attack_schema_cap" },
            new[] { "DG074", "{A} Brutality", "multi_attack_schema_cap" },
            new[] { "DG080", "{A} Indiscriminate Fire", "new_dice_mechanic" },
            new[] { "DG089", "{A} Missile Salvo", "multi_attack_schema_cap" },
        };

        /// All table options of one card, ordinal-sorted by key (pure lookup).
        public static List<VillainOption> OptionsFor(string cardId)
        {
            var outp = new List<VillainOption>();
            if (cardId == null) return outp;
            foreach (var o in Table)
                if (o.CardId == cardId) outp.Add(o);
            outp.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return outp;
        }

        /// Card ids carrying one effect class, ordinal-sorted (coverage pin).
        public static List<string> CardsWith(string effectClass)
        {
            var outp = new List<string>();
            foreach (var o in Table)
                if (o.EffectClass == effectClass && !outp.Contains(o.CardId)) outp.Add(o.CardId);
            outp.Sort(StringComparer.Ordinal);
            return outp;
        }

        /// THE gate (mirrors CardKeywords.ActiveFor): the active skill
        /// profile reads printed abilities at all (S4/S5) AND the card has a
        /// curated option. S1..S3 and the reference path never see a block.
        public static bool ActiveFor(SkillProfile profile, string cardId)
        {
            if (!CardKeywords.SkillReadsCards(profile)) return false;
            foreach (var o in Table)
                if (o.CardId == cardId) return true;
            return false;
        }

        // ------------------------------------------------------- legality

        /// Max distance the card's printed attack can cover (melee reach or
        /// the ranged accuracy gate of the printed pool + keywords).
        public static int AttackReach(DeploymentCard card)
        {
            if (!IsRanged(card)) return card.HasReach ? 2 : MeleeReach;
            return Dice.BuildAttackStats(card.Attacks, card.Keywords, true)
                       .AccGate;
        }

        static bool IsRanged(DeploymentCard card)
        {
            return string.Equals(card.AttackType, "Ranged", StringComparison.OrdinalIgnoreCase);
        }

        /// The regular dice attack is legal from the activation position.
        /// Simplification (documented): pure reach + LOS - the activation
        /// planner (ActionBuilder) stays the authority over engagement.
        static bool AttackLegal(DeploymentCard card, int distance, bool los)
        {
            if (distance < 1) return false;
            if (IsRanged(card)) return los && distance <= AttackReach(card);
            return distance <= AttackReach(card);
        }

        /// The printed reach of one attack_like option (dice variant: its
        /// own accuracy gate; fixed-damage variant: the printed clause).
        public static int OptionReach(VillainOption o)
        {
            if (o.Dice != null)
                return Dice.BuildAttackStats(o.Dice, o.OptionKeywords, true).AccGate;
            return o.Reach;
        }

        static bool OptionLegal(VillainOption o, int distance, bool los)
        {
            if (o.EffectClass != AttackLike) return true;   // orders/utility are position-free
            if (distance < 1) return false;
            if (distance > OptionReach(o)) return false;
            return !o.NeedsLos || los;
        }

        // ------------------------------------------------------- the law

        /// Shared selection. attackValue = printed EV contribution of the
        /// regular attack at this activation (0 when illegal); "dist" null
        /// selects the documented live degradation (PickLive). Qualifying
        /// attack_like/order_like options must STRICTLY beat attackValue;
        /// in live mode an attack_like option may also enter through the
        /// reach-surplus clause (file header). Utility never competes with a
        /// qualifying attack/order block and only needs the balance.
        static OptionPick Evaluate(DeploymentCard card, Rational attackValue, string mode,
                                   int? dist, bool? los, int? ledgerBalance)
        {
            VillainOption best = null;
            var bestVal = Rational.Zero;
            var bestRank = int.MaxValue;
            foreach (var o in OptionsFor(card.Id))
            {
                int rank; Rational v;
                if (o.EffectClass == Utility)
                {
                    if (!ledgerBalance.HasValue) continue;      // unknown balance -> no show
                    if (ledgerBalance.Value < o.BalanceNeeded) continue;
                    rank = 2; v = Rational.Zero;
                }
                else if (o.EffectClass == OrderLike)
                {
                    rank = 1; v = o.Value;
                    if (!(v > attackValue)) continue;           // strict gate
                }
                else
                {
                    rank = 0; v = o.Value;
                    if (dist.HasValue && !OptionLegal(o, dist.Value, los.Value)) continue;
                    if (v > attackValue) { /* strict gate: plain win */ }
                    else if (mode == "live" && !dist.HasValue && v > 0
                             && OptionReach(o) > AttackReach(card))
                    { /* live reach-surplus clause: attack can never cover this */ }
                    else continue;
                }
                // determinism: highest value wins; ties on the class rank
                // (attack < order < utility), then on the key ordinal.
                if (best == null || v > bestVal
                    || (v == bestVal && (rank < bestRank
                        || (rank == bestRank && string.CompareOrdinal(o.Key, best.Key) < 0))))
                {
                    best = o; bestVal = v; bestRank = rank;
                }
            }
            return new OptionPick
            {
                Option = best, AttackValue = attackValue, Mode = mode,
                Distance = dist ?? -1, HasLos = los,
            };
        }

        /// Full law with real distance/LOS (harness today, live app through
        /// the WorldProvider seam). Caller must gate with ActiveFor first.
        public static OptionPick PickForPosition(World world, Figure fig, Figure target,
                                                 int? ledgerBalance)
        {
            var card = fig.Card;
            if (target == null)
                return new OptionPick { AttackValue = Rational.Zero, Mode = "position",
                                        Distance = -1, HasLos = null, Option = null };
            int dist = FocusResolver.CountSpaces(world.Board, fig.Cells(), target.Cells());
            bool los = world.Los.StaticLos(fig.Cells(), target.Cells());
            var attack = AttackLegal(card, dist, los) ? Dice.PrintedEv(card) : Rational.Zero;
            return Evaluate(card, attack, "position", dist, los, ledgerBalance);
        }

        /// Popup law without a world (live bridge): the attack is assumed
        /// legal, options qualify by the strict EV gate or the documented
        /// reach-surplus clause. Caller gates with ActiveFor first.
        public static OptionPick PickLive(DeploymentCard card, int? ledgerBalance)
        {
            return Evaluate(card, Dice.PrintedEv(card), "live", null, null, ledgerBalance);
        }
    }
}
