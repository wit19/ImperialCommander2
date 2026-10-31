// M11c2 (tactical AI, ab S4): the curated SEMANTICS of printed card keywords
// for the pattern (Muster) choice. The card TEXTS landed with M11c-i in the
// language files (Assets/Resources/Languages/{En,De}/DeploymentGroups/
// {enemies,villains}.json, abilities field); keyword definitions live in
// wiki_data/keyword_table.json. This table is the ONLY place that decides
// which printed keywords change the tactical plan - curated on purpose:
//
//   * KEYED BY PRINTED CARD ID (DGxxx), never by ability NAME. The DE files
//     carry German names (Sturm/Stativ/Konzentrierter Beschuss) while the EN
//     files carry Assault/Tripod/Focus Fire; the DGxxx id is the language-
//     independent, deterministic join key (DeploymentCard.Id, catalog
//     units_core.json = same ids).
//   * Source of truth for the assignment: the abilities field of the En
//     enemies/villains language files (M11c-i delivery, dev 33) cross-checked
//     against wiki_data/keyword_table.json.
//   * Fairness law holds: card abilities are PUBLIC table knowledge (cards
//     lie open on the table), so reading them needs no hidden information.
//     The table never filters or invents targets or actions - it only
//     tightens or (structurally, see MultiAttack below) leaves the plan
//     budget as is.
//
// The three tactical halves (scope M11c2, exactly these):
//
//   NoMoveAndAttack  <- Tripod / "Stativ" (E-Web Engineers DG007, DG008):
//       "you cannot voluntarily exit your space if you attack, and you
//        cannot attack if you exit your space". Explicit card rule now that
//        the MultiAttack exception exists: an Assault figure that ALSO
//        prints Tripod (E-Web prints both) may never move and attack in one
//        activation - the attack-half exception never unlocks the move half.
//        ENFORCED (ActionBuilder.PlanActivation, gated ab S4).
//
//   MultiAttack      <- Assault / "Sturm" (DG007, DG008 E-Web; DG046 AT-DP;
//       DG071 IG-88 - every En abilities entry literally named "Assault";
//       Sentry Droids print "{A} Multi-Fire", a DIFFERENT keyword, out of
//       scope): the printed text permits several attack actions per
//       activation. NOT ENFORCED - the (M11-retired) deck schema and the
//       concept-2 fairness law (OrderDeck.ValidateCard: at
//       most ONE attack block per card, Profil-A budget) could not represent
//       a second attack block without amending the fairness law itself, and no
//       threat-budget lever for attack counts exists in ThreatEconomy
//       (it budgets focus bids). Attacks therefore STAY at 1 for these
//       figures; the keyword is carried so a future fairness-law decision
//       (Thomas lane) has the table + wiring point ready.
//
//   FocusFire        <- "Focus Fire" / "Konzentrierter Beschuss" (DG025
//       SC2-M Repulsor Tank): "Perform 2 attacks targeting the same
//       figure." Only meaningful once MultiAttack fires; with attacks at 1
//       the same-target constraint is trivially satisfied. Carried, not
//       enforced (same law limit).
//
// Deliberately PASSIVE (reviewed, no pattern/target change - see the closing
// comment of the M11c2 card): Cower, Defensible, Take Cover, Agile,
// Foresight, Shield, Last Stand, Sentinel, Composite Plating, Professional,
// Fleet, Relentless (reaction strain, no pattern), Mounted, Targeting
// Computer, Scattergun/ACP Scattergun (dice modifiers), and every other
// printed ability not listed above - defensive dice/economy keywords do not
// change Musterwahl or target ranking.
//
// S-GATE: the table is consulted ONLY when the active skill profile is S4 or
// S5 (existing gating plumbing: World.Skill / SkillProfile, concept §6 -
// S1..S3 keep the reference behaviour bit-identical; the harness section
// keyword_rules.json pins both sides). No payload changes anywhere, so no
// existing golden vector moves.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public static class CardKeywords
    {
        // Rules the planner knows (stable ids for harness/pins).
        public const string NoMoveAndAttack = "no_move_and_attack";
        public const string MultiAttack     = "multi_attack";
        public const string FocusFire       = "focus_fire";

        // Skill levels at which the AI reads printed card abilities
        // (Thomas decision 28.10: Faehigkeiten-Play ab S4).
        public static readonly string[] ActiveLevels = { "S4", "S5" };

        // printed card id -> rule ids it carries (curated; sorted by id).
        static readonly Dictionary<string, string[]> ByCard =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                // E-Web Engineers: print Assault AND Tripod - the Tripod
                // half is what binds here (MultiAttack stays schema-capped).
                { "DG007", new[] { NoMoveAndAttack, MultiAttack } },
                { "DG008", new[] { NoMoveAndAttack, MultiAttack } },
                // SC2-M Repulsor Tank: Focus Fire (implies the MultiAttack
                // law limit; carried for the same-target law).
                { "DG025", new[] { FocusFire } },
                // AT-DP: Assault only.
                { "DG046", new[] { MultiAttack } },
                // IG-88: Assault only (Relentless/Arsenal are passive here).
                { "DG071", new[] { MultiAttack } },
            };

        /// All rule ids printed on a card (may be empty; pure lookup).
        public static string[] RulesFor(string cardId)
        {
            string[] rules;
            return cardId != null && ByCard.TryGetValue(cardId, out rules)
                ? rules : Array.Empty<string>();
        }

        /// Card ids carrying one rule, ordinal-sorted (harness coverage pin).
        public static List<string> CardsWith(string rule)
        {
            var outp = new List<string>();
            foreach (var kv in ByCard)
                foreach (var r in kv.Value)
                    if (r == rule) { outp.Add(kv.Key); break; }
            outp.Sort(StringComparer.Ordinal);
            return outp;
        }

        /// Does the ACTIVE skill profile read printed abilities at all?
        /// (null profile = reference/S1..S3 path: never.)
        public static bool SkillReadsCards(SkillProfile profile)
        {
            if (profile == null) return false;
            foreach (var lvl in ActiveLevels)
                if (string.Equals(profile.Level, lvl, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// THE gate: rule active for this card in this world's tactical mode.
        /// S1-S3 (and the reference path without a profile) are bit-identical
        /// to the pre-M11c2 planner.
        public static bool ActiveFor(World world, string cardId, string rule)
        {
            if (!SkillReadsCards(world == null ? null : world.Skill)) return false;
            foreach (var r in RulesFor(cardId))
                if (r == rule) return true;
            return false;
        }
    }
}
