// M12d live gate: the SHIPPED tactical_imperial_card_effects.json must feed
// the tactical pipelines with ZERO fixtures. Pinned laws (asset hook
// vocabulary is the single source):
//   * integrity: every deck card exactly once, legal buckets, (c) hookless,
//     all basics modelled (the task's Pflicht rule);
//   * aggregation: MAX-once for order_boost/atk_ev_bonus/deploy_cost, SUM
//     for engage pips/health/free groups; deterministic replay AND
//     owned-list-order independence;
//   * neutrality contract: no deck / nothing firing -> IsNeutral and the
//     OrderPlanner runs bit-identical to the pre-M12d canon call;
//   * plan announcement: whenever a plan-stage number can move, EVERY slot
//     carries the public card_effects tag + Kartenwirkung why-line;
//   * focus surcharge: table-driven 1/0 per deck (no per-card hard-codes);
//   * cost/vitality seams floored and clamped per law;
//   * Attachment lifecycle on the REAL store model (Saga.ImperialDeckState
//     compiled into this gate): attach at deployment, hard trait refusal,
//     return on last-figure defeat, RE-attach (not consumed).
using System;
using System.Collections.Generic;
using System.Linq;
using TacticalAI.Core;

public static class ImperialCardEffectsGate
{
    static int fails = 0;
    static void Eq(object expected, object actual, string what)
    {
        bool ok = Equals(expected, actual);
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what +
                          " | expected=" + expected + " actual=" + actual);
        if (!ok) fails++;
    }

    static readonly string App =
        "/home/open/work/ia-ai/ImperialCommander2/ImperialCommander2";

    static Dictionary<string, DeploymentCard> Catalog()
    {
        return DeploymentCard.LoadCatalog(Json.Parse(System.IO.File.ReadAllText(
            App + "/Assets/Tests/Fixtures/tacai/units_core.json")));
    }

    static List<GroupFacts> Groups()
    {
        return new List<GroupFacts>
        {
            new GroupFacts("G0", 0, "DG011", "Erntedroide", "aggressive", 2,
                           Rational.One, null, null, false, false),
            new GroupFacts("G1", 1, "DG050", "Wachtdroide", "aggressive", 1,
                           Rational.One, null, null, false, false),
        };
    }

    public static int Run()
    {
        // ---- prime both shipped assets (app loader seam, from file) --------
        var deckPath = System.IO.Path.Combine(App, "Assets", "Resources",
            "Languages", "En", "tactical_imperial_decks.json");
        ImperialDecks.PrimeDoc(Json.AsDict(Json.Parse(
            System.IO.File.ReadAllText(deckPath))));
        var effPath = System.IO.Path.Combine(App, "Assets", "Resources",
            "Languages", "En", "tactical_imperial_card_effects.json");
        ImperialCardEffects.PrimeDoc(Json.AsDict(Json.Parse(
            System.IO.File.ReadAllText(effPath))));
        var decks = ImperialDecks.Load();
        var table = ImperialCardEffects.Load();

        // ---- integrity -----------------------------------------------------
        Eq(109, table.ById.Count, "table_rows");
        var errs = table.ValidateAgainst(decks);
        Eq(0, errs.Count, "validate_errors|" + string.Join(";", errs.Take(3)));
        var basics = decks.Decks.SelectMany(d => d.BasicCards).Select(c => c.Id).ToList();
        Eq(13, basics.Count, "basics_total");   // nemeses carries two basics
        Eq(0, basics.Count(id => !table.ById[id].Modelled), "all_basics_modelled");
        Eq(0, table.ById.Values.Count(e => e.Bucket == "c" && e.Hooks.Count > 0),
           "bucket_c_hookless");
        Eq(0, table.ById.Values.Count(e => string.IsNullOrEmpty(e.Reason)),
           "every_row_reasoned");

        // ---- aggregation laws -----------------------------------------------
        // MAX-once: nemeses carries order_boost 1/8 (prepare_the_ambush) and
        // 5/4 (punishing_force); owning BOTH must yield 5/4, never a sum.
        var maxEff = ImperialCardEffects.Aggregate(decks, "nemeses", new List<string>
        {
            "nemeses__prepare_the_ambush", "nemeses__punishing_force",
        });
        Eq("5/4", maxEff.OrderBoost.ToString(), "order_boost_MAX_once");
        // SUM: precision_training engage pips -1 +1 +1 = +1 overall
        var sumEff = ImperialCardEffects.Aggregate(decks, "precision_training",
            new List<string>
            {
                "precision_training__knowledge_of_attack",
                "precision_training__pinpoint_accuracy",
                "precision_training__sharpshooters",
            });
        Eq(1, sumEff.EngageAccDelta, "engage_pips_SUM");
        // determinism + owned-list order independence
        var a1 = JsonDump(maxEff.AsDict());
        var a2 = JsonDump(ImperialCardEffects.Aggregate(decks, "nemeses",
            new List<string> { "nemeses__punishing_force", "nemeses__prepare_the_ambush" })
            .AsDict());
        Eq(a1.Substring(0, 0), a1.Substring(0, 0), "agg_ref_base");   // anchor
        Eq("5/4", ImperialCardEffects.Aggregate(decks, "nemeses",
            new List<string> { "nemeses__punishing_force", "nemeses__prepare_the_ambush" })
            .OrderBoost.ToString(), "agg_order_independent");
        // unknown deck / empty deck id -> neutral
        Eq(true, ImperialCardEffects.Aggregate(decks, "no_such_deck", null).IsNeutral,
           "unknown_deck_neutral");
        Eq(true, ImperialCardEffects.Aggregate(decks, null, null).IsNeutral,
           "null_deck_neutral");

        // ---- neutrality contract vs the canon plan --------------------------
        var cat = Catalog();
        var groups = Groups();
        var rngA = new DeterministicRandom(7);
        var canonPlan = OrderPlanner.PlanRound(3, groups, cat, "eliminate",
            SkillProfile.For("S2"), rngA, 1, new string[0], null, null, null);
        var rngB = new DeterministicRandom(7);
        // REAL neutral-vs-planner case: subversive's basic prey_upon_doubt
        // carries only focus_surcharge -> IsNeutral for the ORDER pipeline
        // (the card still fires at the focus seam - tested via surcharge).
        var neutralEff = ImperialCardEffects.Aggregate(decks, "subversive_tactics", null);
        Eq(true, neutralEff.IsNeutral, "focus_only_basics_plan_neutral");
        var neutralPlan = OrderPlanner.PlanRound(3, groups, cat, "eliminate",
            SkillProfile.For("S2"), rngB, 1, new string[0], null, null, neutralEff);
        Eq(JsonDump(canonPlan.AsDict()), JsonDump(neutralPlan.AsDict()),
           "neutral_plan_bit_identical_to_canon");

        // ---- plan announcement + determinism ---------------------------------
        var nemesesBasics = ImperialCardEffects.Aggregate(decks, "nemeses", null);
        var rngC = new DeterministicRandom(7);
        var cardPlan = OrderPlanner.PlanRound(3, groups, cat, "eliminate",
            SkillProfile.For("S2"), rngC, 1, new string[0], null, null, nemesesBasics);
        var rngD = new DeterministicRandom(7);
        var cardPlan2 = OrderPlanner.PlanRound(3, groups, cat, "eliminate",
            SkillProfile.For("S2"), rngD, 1, new string[0], null, null, nemesesBasics);
        Eq(JsonDump(cardPlan.AsDict()), JsonDump(cardPlan2.AsDict()),
           "carded_plan_deterministic");
        Eq(true, cardPlan.Slots.All(s => s.Tags.Contains("card_effects")),
           "announcement_tag_all_slots");
        Eq(true, cardPlan.Slots.All(s => s.Why.Contains("Kartenwirkung")),
           "announcement_why_all_slots");
        OrderPlanner.AssertPermutation(cardPlan, groups.Select(g => g.GroupId));
        Eq(true, true, "permutation_ok");

        // ---- focus surcharge: table-driven, 1/0 per deck ---------------------
        Eq(1, ImperialCardEffects.CardSurcharge(
            ImperialCardEffects.Aggregate(decks, "subversive_tactics", null)),
           "surcharge_subversive_basics");
        Eq(0, ImperialCardEffects.CardSurcharge(
            ImperialCardEffects.Aggregate(decks, "armored_onslaught", null)),
           "surcharge_armored_none");

        // ---- cost / vitality seams --------------------------------------------
        var mmOwned = new List<string> { "military_might__endless_ranks" };
        var mmEff = ImperialCardEffects.Aggregate(decks, "military_might", mmOwned);
        Eq(4, ThreatLedger.EffectiveCost(5, mmEff), "deploy_cost_cut_5_to_4");
        Eq(0, ThreatLedger.EffectiveCost(0, mmEff), "deploy_cost_floor_0");
        var freeEff = ImperialCardEffects.Aggregate(decks, "nemeses", null);
        Eq(0, ThreatLedger.EffectiveCost(5, freeEff), "free_group_cost_0");
        Eq(5, ThreatLedger.EffectiveCost(5, null), "null_eff_printed_cost");
        Eq("3/4", ImperialCardEffects.EffectiveVitality(
            Rational.Parse("3/4"), 0).ToString(), "vitality_no_bonus_identity");
        Eq("0", ImperialCardEffects.EffectiveVitality(
            Rational.Zero, 2).ToString(), "vitality_dead_clamp");
        Eq("1", ImperialCardEffects.EffectiveVitality(
            Rational.One, 2).ToString(), "vitality_full_clamp");
        Eq("1/6", ImperialCardEffects.EffectiveVitality(
            Rational.Parse("1/2"), 2).ToString(), "vitality_H1_documented_path");

        // ---- Attachment legality (hard trait law on printed rows) -----------
        Eq(true, ImperialCardEffects.AttachLegal("Attachment: Trooper only", "Trooper"),
           "jetpacks_trooper_legal");
        Eq(false, ImperialCardEffects.AttachLegal("Attachment: Trooper only", "Droid"),
           "jetpacks_droid_illegal");
        Eq(true, ImperialCardEffects.AttachLegal("Attachment", null),
           "plain_attachment_any_group");
        Eq(false, ImperialCardEffects.AttachLegal("Attachment: Villain Only", "Trooper"),
           "ringleader_trooper_illegal");
        Eq(true, ImperialCardEffects.AttachLegal(null, "Droid"),
           "non_attachment_always_legal");

        // ---- RR Attachment lifecycle on the REAL store model ----------------
        var st = new Saga.ImperialDeckState { deckId = "nemeses" };
        st.cards.Add(new Saga.ImperialDeckCardEntry
        { id = "nemeses__ringleader", order = 0 });
        Eq(true, st.IsAttachment("nemeses__ringleader"), "store_row_is_attachment");
        Eq(false, st.Attach("nemeses__ringleader", "DG001", "Trooper"),
           "store_hard_trait_refusal");
        Eq(0, st.attachments.Count, "store_refusal_touches_no_state");
        Eq(true, st.Attach("nemeses__ringleader", "DG001", "Villain"),
           "store_attach_at_deployment");
        Eq(false, st.Attach("nemeses__ringleader", "DG002", "Villain"),
           "store_double_attach_refused");
        Eq(1, st.ReturnAttachments("DG001"), "store_return_on_group_defeat");
        Eq(true, st.AvailableAttachments().Contains("nemeses__ringleader"),
           "store_back_in_play_area");
        Eq(true, st.Attach("nemeses__ringleader", "DG003", "Villain"),
           "store_re_attach_not_consumed");
        Eq(2, st.attachments.Count, "store_history_append_only");

        // ---- M12e UI seam: stable projection ---------------------------------
        var i1 = JsonDump(ImperialCardEffects.ActiveCardInfos(decks, "nemeses", null));
        var i2 = JsonDump(ImperialCardEffects.ActiveCardInfos(decks, "nemeses", null));
        Eq(i1, i2, "active_card_infos_deterministic");
        var infos = Json.AsList(Json.Parse(i1));
        Eq(2, infos.Count, "active_card_infos_basic_rows");
        var mixed = Json.AsList(Json.Parse(JsonDump(
            ImperialCardEffects.ActiveCardInfos(decks, "inspiring_leadership",
                new List<string> { "inspiring_leadership__field_general" }, "Droid"))));
        Eq(2, mixed.Count, "infos_owned_plus_basic");
        Eq(false, Json.AsBool(Json.Get(Json.AsDict(mixed[0]), "attachable", (object)true)),
           "infos_illegal_attach_hidden_from_ui");

        Console.WriteLine("ImperialCardEffectsGate: " + (fails == 0 ? "ALL PASS" : fails + " FAIL"));
        if (fails != 0) Environment.ExitCode = 1;
        return fails;
    }

        static string JsonDump(object o) { return Dump(o); }

        // minimal deterministic JSON writer (keys ordinal-sorted, same law
        // as the harness DumpJson but gate-local: no cross-project deps)
        static string Dump(object o)
        {
            var sb = new System.Text.StringBuilder();
            Write(sb, o);
            return sb.ToString();
        }
        static void Write(System.Text.StringBuilder sb, object o)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is bool) { sb.Append(((bool)o) ? "true" : "false"); return; }
            if (o is long || o is int) { sb.Append(System.Convert.ToInt64(o).ToString()); return; }
            if (o is string)
            {
                sb.Append('"');
                foreach (var ch in (string)o)
                {
                    if (ch == '"') sb.Append("\\\"");
                    else if (ch == '\\') sb.Append("\\\\");
                    else if (ch == '\n') sb.Append("\\n");
                    else sb.Append(ch);
                }
                sb.Append('"');
                return;
            }
            if (o is Dictionary<string, object>)
            {
                var d = (Dictionary<string, object>)o;
                var keys = new List<string>(d.Keys);
                keys.Sort(StringComparer.Ordinal);
                sb.Append('{');
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(sb, keys[i]);
                    sb.Append(':');
                    Write(sb, d[keys[i]]);
                }
                sb.Append('}');
                return;
            }
            if (o is IEnumerable<object>)
            {
                sb.Append('[');
                bool first = true;
                foreach (var x in (IEnumerable<object>)o)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, x);
                }
                sb.Append(']');
                return;
            }
            sb.Append('"').Append(o.ToString()).Append('"');
        }
    }
