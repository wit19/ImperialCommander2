// M12d2 Agenda section: influence economy + deterministic 4-card market +
// kept-secret anti-leak law. Expect blocks are authored from the documented
// rule via wiki_data/tools/tacai_agenda_twin.py against the SHIPPED asset
// (see the fixture _comment); this section runs them against the C#
// originals - Core (AgendaCards/AgendaMarket) AND the REAL store class
// Saga.AgendaState (linked into this project, no mirror), because the
// anti-leak law lives in the store and must be pinned WHERE it lives.
// The stage loop below mirrors Saga.SagaCampaign.RunAgendaUpgradeStage
// exactly (book -> plan -> TryBuyAgenda -> audit -> replace deck): if the
// Saga loop semantics change, these vectors must change with it, never the
// other way round.
using System;
using System.Collections.Generic;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        static void SecAgenda()
        {
            var v = LoadJson("agenda.json");
            if (AgendaCards.DocOrNull() == null)
                throw new InvalidOperationException("agenda asset not primed");
            var table = AgendaCards.Load();
            if (table == null) { Fail("agenda.load", "table null"); return; }

            AssetStats(Json.AsDict(v["asset_stats"]), table);
            foreach (var raw in Json.AsList(v["setup_cases"]))
                SetupCase(Json.AsDict(raw), table);
            MarketSequence(Json.AsDict(v["market_stages"]), table, hiddenOut: out List<string> richHidden);
            MarketSequence(Json.AsDict(v["market_stages_plain"]), table, hiddenOut: out _);
            BackfillIdentity(Json.AsDict(v["backfill_identity"]), table);
            AntiLeak(Json.AsDict(v["anti_leak"]), table, richHidden);
            StoreRouting(Json.AsDict(v["store_routing"]), table);
            Classification(Json.AsDict(v["classification"]), table);
        }

        // ------------------------------------------------------ asset stats
        static void AssetStats(Dictionary<string, object> st, AgendaCardTable table)
        {
            Check("agenda.set_count", Json.AsLong(st["set_count"]), (long)table.Sets.Count);
            Check("agenda.card_count", Json.AsLong(st["card_count"]), (long)table.Cards.Count);
            var byType = new Dictionary<string, object>();
            var byCost = new Dictionary<string, object>();
            foreach (var c in table.Cards)
            {
                byType.TryGetValue(c.Type, out object t);
                byType[c.Type] = (long)(t ?? 0L) + 1;
                var key = c.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture);
                byCost.TryGetValue(key, out object o);
                byCost[key] = (long)(o ?? 0L) + 1;
            }
            Check("agenda.by_type", Canonical(Json.AsDict(st["by_type"])), Canonical(byType));
            Check("agenda.by_cost", Canonical(Json.AsDict(st["by_cost"])), Canonical(byCost));
        }

        // ---------------------------------------------------- setup (fixture a)
        static void SetupCase(Dictionary<string, object> c, AgendaCardTable table)
        {
            var name = Json.AsStr(c["name"]);
            var save = Json.AsDict(c["campaign_save"]);
            var exp = Json.AsDict(c["expect"]);
            var seed = AgendaCards.AgendaSeedFor(Json.AsStr(save["GUID"]));
            Check(name + ".seed", Json.AsStr(exp["seed"]), ImperialDecks.SeedHex(seed));
            var profile = ImperialCampaignProfile.ProfileFromCampaignSave(save);
            long? tp = c.ContainsKey("campaign_time_period")
                ? Json.AsLong(c["campaign_time_period"]) : (long?)null;

            var choice = AgendaCards.ChooseDeck(table, profile, seed, tp);
            if (choice == null) { Fail(name, "ChooseDeck null"); return; }
            Check(name + ".chosen_sets", Json.AsList(exp["chosen_sets"]),
                  choice.ChosenSets.Cast<object>().ToList());
            Check(name + ".deck", Json.AsList(exp["deck"]),
                  choice.DeckOrder.Cast<object>().ToList());
            Check(name + ".deck_len", 18L, (long)choice.DeckOrder.Count);
            if (exp.ContainsKey("top_totals"))
                Check(name + ".top_totals", Json.AsList(exp["top_totals"]),
                      choice.Scores.Take(8).Select(r => (object)r.Total).ToList());
            if (exp.ContainsKey("illegal_sets"))
            {
                var illegal = choice.Scores.Where(r => !r.Legal)
                                           .Select(r => r.Set).OrderBy(x => x, StringComparer.Ordinal)
                                           .Cast<object>().ToList();
                Check(name + ".illegal_sets", Json.AsList(exp["illegal_sets"]), illegal);
                Check(name + ".chosen_excludes_illegal", "",
                      choice.ChosenSets.Where(s => Json.AsList(exp["illegal_sets"])
                          .Any(x => Json.AsStr(x) == s)).Count() == 0 ? "" : "PRESENT");
            }
            // reproducible: same inputs -> byte-identical choice dict
            var again = AgendaCards.ChooseDeck(table, profile, seed, tp);
            Check(name + ".reproducible", Canonical(choice.ToDict()), Canonical(again.ToDict()));
        }

        // ------------------------------------- market sequence (fixtures b)
        // Mirrors SagaCampaign.RunAgendaUpgradeStage on the REAL store class.
        static void MarketSequence(Dictionary<string, object> block,
                                   AgendaCardTable table,
                                   out List<string> hiddenOut)
        {
            hiddenOut = new List<string>();
            var name = Json.AsStr(block["name"]);
            var save = Json.AsDict(block["campaign_save"]);
            var seed = AgendaCards.AgendaSeedFor(Json.AsStr(save["GUID"]));
            var profile = ImperialCampaignProfile.ProfileFromCampaignSave(save);
            var events = Json.AsList(block["influence_events"]);
            var expStages = Json.AsList(block["expect_stages"]);
            var expFinal = Json.AsDict(block["expect_final"]);

            string traceA = null;
            for (int run = 0; run < 2; run++)
            {
                var choice = AgendaCards.ChooseDeck(table, profile, seed, null);
                var state = Saga.AgendaState.FromChoice(choice, Json.AsStr(save["GUID"]));
                Check(name + ".store_deck", choice.DeckOrder.Cast<object>().ToList(),
                      state.deck.Cast<object>().ToList());

                var trace = new List<object>();
                int buys = 0;
                for (int i = 0; i < events.Count; i++)
                {
                    state.AddInfluence((int)Json.AsLong(events[i]), "fixture:mission");
                    var plan = AgendaMarket.RunStage(table, profile, state.deck,
                        state.influenceBalance, seed, state.stagesRun);
                    if (plan == null) { Fail(name + ".stage" + i, "plan null"); return; }
                    var boughtIds = new List<string>();
                    foreach (var b in plan.Buys)
                    {
                        if (!state.TryBuyAgenda(b.Card, b.Type, (int)b.Cost,
                                (int)plan.Stage, "buy:" + b.Card))
                        { Fail(name + ".stage" + i + ".book", "refused " + b.Card); return; }
                        boughtIds.Add(b.Card);
                        if (b.Type == AgendaCards.TypeKeptSecret && run == 0)
                            hiddenOut.Add(b.Card);
                    }
                    state.AddMarketAudit((int)plan.Stage, plan.Market, boughtIds,
                                         plan.NotPurchased, plan.SeedHex);
                    state.ReplaceDeckAfterStage(plan.DeckAfter);

                    trace.Add(StageDict(plan));
                    if (run == 0)
                        Check(name + ".stage" + i, Canonical(StageDictOfExpect(
                                  Json.AsDict(expStages[i]))), Canonical(StageDict(plan)));
                    buys += plan.Buys.Count;
                }
                if (run == 0)
                {
                    Check(name + ".final_balance", Json.AsLong(expFinal["balance"]),
                          (long)state.influenceBalance);
                    Check(name + ".final_deck", Json.AsList(expFinal["deck_after"]),
                          state.deck.Cast<object>().ToList());
                    Check(name + ".total_buys", Json.AsLong(expFinal["total_buys"]), (long)buys);
                    // the return-mix law: deck only shrank by the bought cards
                    Check(name + ".return_law", 18L - (long)buys, (long)state.deck.Count);
                    traceA = Canonical(trace);
                }
                else
                    Check(name + ".replay_identical", traceA, Canonical(trace));
            }
        }

        // The pinned subset (the fixture does not pin the free-text Reason;
        // SeedHex rides on the setup-pinned seed).
        static Dictionary<string, object> StageDict(AgendaStagePlan p)
        {
            var buys = new List<object>();
            foreach (var b in p.Buys) buys.Add(b.ToDict());
            return new Dictionary<string, object>
            {
                { "stage", p.Stage },
                { "market", p.Market.Cast<object>().ToList() },
                { "buys", buys },
                { "not_purchased", p.NotPurchased.Cast<object>().ToList() },
                { "deck_after", p.DeckAfter.Cast<object>().ToList() },
                { "influence_before", p.InfluenceBefore },
                { "influence_after", p.InfluenceAfter },
            };
        }

        static Dictionary<string, object> StageDictOfExpect(Dictionary<string, object> e)
        {
            var o = new Dictionary<string, object>();
            foreach (var k in new[] { "stage", "market", "buys", "not_purchased",
                                      "deck_after", "influence_before", "influence_after" })
                o[k] = e[k];
            return o;
        }

        // ---------------------------------------- backfill identity (fixture c)
        // The M12b Punkt-4 doctrine: an OLD save (no agenda field) re-derives
        // EXACTLY the deck a new campaign freezes - identity through the
        // store constructor, checked against the pinned deck.
        static void BackfillIdentity(Dictionary<string, object> block, AgendaCardTable table)
        {
            var save = Json.AsDict(block["campaign_save"]);
            var exp = Json.AsDict(block["expect"]);
            var seed = AgendaCards.AgendaSeedFor(Json.AsStr(save["GUID"]));
            var profile = ImperialCampaignProfile.ProfileFromCampaignSave(save);
            var choice = AgendaCards.ChooseDeck(table, profile, seed, null);
            var state = Saga.AgendaState.FromChoice(choice, Json.AsStr(save["GUID"]));
            Check("backfill.seed", Json.AsStr(exp["seed"]), state.seedHex);
            Check("backfill.deck", Json.AsList(exp["deck"]), state.deck.Cast<object>().ToList());
            Check("backfill.chosen6", 6L, (long)state.chosenSets.Count);
            Check("backfill.scores_full", (long)table.Sets.Count, (long)state.setScores.Count);
            var rechoice = Saga.AgendaState.FromChoice(
                AgendaCards.ChooseDeck(table, profile, seed, null), Json.AsStr(save["GUID"]));
            Check("backfill.identity", Canonical(state.deck), Canonical(rechoice.deck));
        }

        // ------------------------------------------ anti-leak (fixture d)
        // The controller canon in code form: SECRET is mechanics, a LEAK is
        // a bug. Unrevealed Kept Secret ids AND printed names must not occur
        // anywhere in the rebel-facing export, while the influence SPEND
        // stays public; the imperial audit view must contain them (the
        // fixture discriminates, it does not trivially pass).
        static void AntiLeak(Dictionary<string, object> block, AgendaCardTable table,
                             List<string> richHidden)
        {
            var exp = Json.AsDict(block["expect"]);
            Check("anti_leak.hidden_buys", Json.AsList(exp["kept_secret_buys"]),
                  richHidden.Cast<object>().ToList());

            var save = Json.AsDict(block["campaign_save"]);
            var seed = AgendaCards.AgendaSeedFor(Json.AsStr(save["GUID"]));
            var profile = ImperialCampaignProfile.ProfileFromCampaignSave(save);
            var events = Json.AsList(block["influence_events"]);
            var choice = AgendaCards.ChooseDeck(table, profile, seed, null);
            var state = Saga.AgendaState.FromChoice(choice, Json.AsStr(save["GUID"]));
            for (int i = 0; i < events.Count; i++)
            {
                state.AddInfluence((int)Json.AsLong(events[i]), "fixture:mission");
                var plan = AgendaMarket.RunStage(table, profile, state.deck,
                    state.influenceBalance, seed, state.stagesRun);
                foreach (var b in plan.Buys)
                    state.TryBuyAgenda(b.Card, b.Type, (int)b.Cost, (int)plan.Stage, "buy:" + b.Card);
                var boughtIds = plan.Buys.Select(b => b.Card).ToList();
                state.AddMarketAudit((int)plan.Stage, plan.Market, boughtIds,
                                     plan.NotPurchased, plan.SeedHex);
                state.ReplaceDeckAfterStage(plan.DeckAfter);
            }

            // 1) every rebel row matches the pinned public contract
            var view = state.RebelView();
            var rows = Json.AsList(exp["buy_rows"]);
            Check("anti_leak.row_count", (long)rows.Count, (long)view.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var e = Json.AsDict(rows[i]);
                var r = view[i];
                Check("anti_leak.row" + i + ".type", Json.AsStr(e["type"]), Json.AsStr(r["type"]));
                Check("anti_leak.row" + i + ".cost", Json.AsLong(e["cost"]), (long)Json.AsLong(r["cost"]));
                Check("anti_leak.row" + i + ".card", Json.AsStr(e["card"]), Json.AsStr(r["card"]));
            }

            // 2) substring scan over the FULL serialized rebel export
            var flat = Canonical(view);
            foreach (var rawForbidden in Json.AsList(exp["forbidden_strings"]))
            {
                var parts = Json.AsStr(rawForbidden).Split('|');
                Check("anti_leak.no_id[" + parts[0] + "]", -1L,
                      (long)(flat.IndexOf(parts[0], StringComparison.Ordinal)));
                Check("anti_leak.no_name[" + parts[1] + "]", -1L,
                      (long)(flat.IndexOf(parts[1], StringComparison.Ordinal)));
            }

            // 3) discrimination: the imperial audit view DOES know them
            var audit = Canonical(state.ImperialAuditView());
            foreach (var id in richHidden)
                Check("anti_leak.audit_has[" + id + "]", true,
                      audit.IndexOf(id, StringComparison.Ordinal) >= 0);

            // 4) reveal law: discard refuses a hidden secret, reveal opens
            //    the row, then the id is legitimately public
            if (richHidden.Count > 0)
            {
                var k = richHidden[0];
                Check("anti_leak.discard_hidden_refused", false, state.DiscardAgenda(k, "test"));
                Check("anti_leak.reveal", true, state.RevealKeptSecret(k));
                var flat2 = Canonical(state.RebelView());
                Check("anti_leak.revealed_now_public", true,
                      flat2.IndexOf(k, StringComparison.Ordinal) >= 0);
            }
        }

        // --------------------------------------- store routing (fixture f)
        // Type routing through the SINGLE booking path, deterministic asset
        // picks (no market luck): ongoing -> Play-Area row; side/forced ->
        // public row only; kept secret -> hidden row. Discard is the only
        // way an ongoing leaves the area.
        static void StoreRouting(Dictionary<string, object> block, AgendaCardTable table)
        {
            var picks = Json.AsList(Json.AsDict(block["expect"])["picks"]).ConvertAll(Json.AsDict);
            var state = new Saga.AgendaState { seedKey = "fixture", seedHex = "0" };
            state.deck.AddRange(picks.Select(p => Json.AsStr(p["card"])));
            state.AddInfluence(20, "fixture:grant");
            foreach (var p in picks)
                Check("routing.buy[" + Json.AsStr(p["card"]) + "]", true,
                      state.TryBuyAgenda(Json.AsStr(p["card"]), Json.AsStr(p["type"]),
                          (int)Json.AsLong(p["cost"]), 0, "fixture:buy"));
            Check("routing.balance_after_spend",
                  20L - picks.Sum(p => Json.AsLong(p["cost"])), (long)state.influenceBalance);
            Check("routing.deck_empty", 0L, (long)state.deck.Count);

            var ongoing = picks.Where(p => Json.AsStr(p["type"]) == AgendaCards.TypeOngoing)
                               .Select(p => Json.AsStr(p["card"])).ToList();
            Check("routing.play_area", ongoing.Cast<object>().ToList(),
                  state.ActiveOngoing().Cast<object>().ToList());

            var view = state.RebelView();
            foreach (var p in picks)
            {
                var id = Json.AsStr(p["card"]);
                var typ = Json.AsStr(p["type"]);
                var row = view.First(r => Json.AsStr(r["type"]) == typ &&
                                          (typ != AgendaCards.TypeKeptSecret ? Json.AsStr(r["card"]) == id : true));
                Check("routing.public[" + id.Substring(0, System.Math.Min(id.Length, 24)) + "].card_field",
                      typ == AgendaCards.TypeKeptSecret ? "" : id, Json.AsStr(row["card"]));
            }
            // purchases appended in pick order: the revealed flags ARE the
            // routing verdict (kept_secret the single false row)
            Check("routing.revealed_flags",
                  picks.Select(p => Json.AsStr(p["type"]) != AgendaCards.TypeKeptSecret)
                       .Cast<object>().ToList(),
                  state.purchases.Select(pu => (object)pu.revealed).ToList());

            if (ongoing.Count > 0)
            {
                Check("routing.discard", true, state.DiscardAgenda(ongoing[0], "fixture:discard"));
                Check("routing.play_area_after", 0L,
                      (long)state.ActiveOngoing().Where(x => x == ongoing[0]).Count());
            }
        }

        // ------------------------------------- classification (fixture g)
        static void Classification(Dictionary<string, object> block, AgendaCardTable table)
        {
            var exp = Json.AsDict(block["expect"]);
            var rows = AgendaCards.ClassificationTable(table);
            Check("classification.rows", Json.AsLong(exp["rows"]), (long)rows.Count);
            Check("classification.a_store", Json.AsLong(exp["a_store"]),
                  (long)rows.Count(r => Json.AsStr(r["bucket"]) == "a_store"));
            Check("classification.b_public", Json.AsLong(exp["b_public"]),
                  (long)rows.Count(r => Json.AsStr(r["bucket"]) == "b_public"));
            // bucket<->type law: ongoing/kept_secret are store-wired, the
            // mission types are public-only (PRUEFAUFTRAG (2) verdict)
            var storeTypes = new[] { AgendaCards.TypeOngoing, AgendaCards.TypeKeptSecret };
            Check("classification.store_types_only", 0L,
                  (long)rows.Count(r =>
                      (Json.AsStr(r["bucket"]) == "a_store") !=
                      storeTypes.Contains(Json.AsStr(r["type"]))));
        }
    }
}
