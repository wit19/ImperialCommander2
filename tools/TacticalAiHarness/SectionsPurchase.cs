// M12c Imperial Upgrade Stage purchase section: the deterministic greedy
// purchase rule on the SHIPPED deck table. Expect blocks are authored from
// the documented rule via wiki_data/tools/tacai_purchase_twin.py (see the
// fixture _comment); this section runs them against the C# original.
// Covers: the "buy|" noise vectors, the greedy ledgers (fixed seed + fixed
// profile + fixed XP history -> exact purchase order + final balance, plus
// the replay identity), and the plan snapshot (full ranked score rows = the
// persisted Begruendung). The greedy loop mirrors
// Saga.SagaCampaign.RunImperialUpgradeStage on the same Core primitives -
// if the Saga loop semantics ever change, these vectors must change with
// it (and the twin re-authored), never the other way round.
using System;
using System.Collections.Generic;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        static void SecImperialPurchases()
        {
            var v = LoadJson("imperial_purchases.json");
            if (ImperialDecks.DocOrNull() == null)
                throw new InvalidOperationException("imperial deck asset not primed");
            var table = ImperialDecks.Load();

            // -------------------------------------------------- noise vectors
            foreach (var raw in Json.AsList(v["noise_vectors"]))
            {
                var c = Json.AsDict(raw);
                var key = Json.AsStr(c["key"]);
                var card = Json.AsStr(c["card"]);
                Check("noise[" + card + "@" + key.Substring(0, 8) + "]",
                      Json.AsLong(c["expect"]),
                      ImperialPurchases.NoiseForPurchase(
                          ImperialDecks.DeckSeedFor(key), card));
            }

            // ------------------------------------------------- greedy ledgers
            foreach (var raw in Json.AsList(v["purchase_cases"]))
                PurchaseCase(Json.AsDict(raw), table);

            // ------------------------------------------------- plan snapshot
            PlanSnapshot(Json.AsDict(v["plan_snapshot"]), table);
        }

        // One case: save-shaped profile + fixed XP history -> exact bought
        // order and final balance (run twice; the replay must be identical).
        static void PurchaseCase(Dictionary<string, object> c,
                                 ImperialClassDeckTable table)
        {
            var name = Json.AsStr(c["name"]);
            var save = Json.AsDict(c["campaign_save"]);
            var exp = Json.AsDict(c["expect"]);
            var seed = ImperialDecks.DeckSeedFor(Json.AsStr(save["GUID"]));
            var profile = ImperialCampaignProfile.ProfileFromCampaignSave(save);
            var choice = ImperialDecks.Choose(profile, seed);
            if (choice == null) { Fail(name, "deck choice returned null"); return; }
            Check(name + ".deck", Json.AsStr(exp["deck"]), choice.DeckId);

            List<object> firstRun = null;
            long firstBalance = 0;
            for (int run = 0; run < 2; run++)
            {
                var bought = new List<object>();
                var balance = RunLedger(table, profile, choice.DeckId,
                                        Json.AsList(c["xp_events"]), seed, bought);
                if (run == 0)
                {
                    firstRun = bought; firstBalance = balance;
                    Check(name + ".bought",
                          Json.AsList(exp["bought"]).ConvertAll(x => (object)Json.AsStr(x)),
                          bought);
                    Check(name + ".balance", Json.AsLong(exp["balance"]), balance);
                }
                else
                {
                    Check(name + ".replay_bought_identical", Canonical(firstRun),
                          Canonical(bought));
                    Check(name + ".replay_balance_identical", firstBalance, balance);
                }
            }
        }

        // The C# twin of the twin's simulate(): each XP event lands on the
        // account, then the stage buys greedily. Owned starts at the deck's
        // basic cards (they rode along with the M12b deck choice).
        static long RunLedger(ImperialClassDeckTable table,
                              ImperialCampaignProfile profile, string deckId,
                              List<object> xpEvents, ulong seed,
                              List<object> bought)
        {
            var deck = table.Find(deckId);
            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in deck.BasicCards) owned.Add(b.Id);
            long balance = 0;
            foreach (var rawEv in xpEvents)
            {
                balance += Json.AsLong(rawEv);
                for (int guard = 0; guard < 100; guard++)
                {
                    var plan = ImperialPurchases.Plan(table, profile, deckId,
                                                      owned, balance, seed);
                    if (plan == null || plan.CardId == null) break;
                    if (!owned.Add(plan.CardId)) break;   // duplicate guard
                    balance -= plan.XpCost;
                    bought.Add(plan.CardId);
                }
            }
            return balance;
        }

        // One Plan() call, fully pinned: seed + deck + the complete ranked
        // candidate rows (card/name/cost/synergy/noise/total, total desc then
        // id asc) + the single affordable pick.
        static void PlanSnapshot(Dictionary<string, object> c,
                                 ImperialClassDeckTable table)
        {
            var name = Json.AsStr(c["name"]);
            var save = Json.AsDict(c["campaign_save"]);
            var seed = ImperialDecks.DeckSeedFor(Json.AsStr(save["GUID"]));
            var profile = ImperialCampaignProfile.ProfileFromCampaignSave(save);
            var choice = ImperialDecks.Choose(profile, seed);
            Check(name + ".deck", Json.AsStr(c["deck"]), choice.DeckId);

            var deck = table.Find(choice.DeckId);
            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in deck.BasicCards) owned.Add(b.Id);
            var plan = ImperialPurchases.Plan(table, profile, choice.DeckId,
                                              owned, Json.AsLong(c["xp_balance"]),
                                              seed);
            if (plan == null) { Fail(name, "Plan returned null"); return; }

            Check(name + ".seed", Json.AsStr(c["seed"]), plan.SeedHex);
            Check(name + ".bought", Json.AsStr(c["bought"]),
                  plan.CardId == null ? "" : plan.CardId);
            Check(name + ".cost", Json.AsLong(c["cost"]),
                  plan.CardId == null ? 0L : plan.XpCost);

            var got = new List<object>();
            foreach (var r in plan.Scores) got.Add(r.ToDict());
            Check(name + ".scores", Json.AsList(c["scores"]), got);

            // invariants: total desc, then card id ascending (ordinal);
            // every candidate is a printed non-basic card with an XP cost.
            var rows = plan.Scores;
            for (int i = 1; i < rows.Count; i++)
            {
                var a = rows[i - 1]; var b = rows[i];
                if (a.Total < b.Total)
                    Fail(name + ".rank", "row " + i + " total not descending");
                else if (a.Total == b.Total &&
                         string.CompareOrdinal(a.Card, b.Card) >= 0)
                    Fail(name + ".rank", "row " + i + " tie not id-ascending");
            }
            Check(name + ".candidates_all_paid", true,
                  rows.All(r => r.Cost > 0));
            Check(name + ".candidates_all_basics_absent", true,
                  deck.BasicCards.All(b0 => rows.All(r => r.Card != b0.Id)));
            // candidate completeness: non-basic paid cards minus owned
            Check(name + ".candidate_count",
                  (long)deck.Cards.Count(x => !x.Basic && x.Xp.HasValue &&
                                              !owned.Contains(x.Id)),
                  (long)rows.Count);
        }
    }
}
