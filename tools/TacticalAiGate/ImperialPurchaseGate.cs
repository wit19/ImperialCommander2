// M12c live gate: the SHIPPED tactical_imperial_decks.json must feed the
// purchase engine, and every greedy-ledger invariant must hold on the real
// asset with zero fixtures: deterministic replay (two runs identical), a
// rich stage buys each paid card exactly once, basics are never candidates,
// no buy ever exceeds the balance, exhausted decks stop, and unknown decks
// / empty tables stay null (the 'kein Deck' contract).
using System;
using System.Collections.Generic;
using System.Linq;
using TacticalAI.Core;

public static class ImperialPurchaseGate
{
    static int fails = 0;
    static void Eq(object expected, object actual, string what)
    {
        bool ok = Equals(expected, actual);
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what + " | expected=" + expected + " actual=" + actual);
        if (!ok) fails++;
    }

    static readonly string App =
        "/home/open/work/ia-ai/ImperialCommander2/ImperialCommander2";

    // Greedy stage mirror (same primitives as SagaCampaign.
    // RunImperialUpgradeStage / the harness ledger).
    static long Stage(ImperialClassDeckTable table, ImperialCampaignProfile p,
                      string deckId, HashSet<string> owned, long balance,
                      ulong seed, List<string> bought)
    {
        for (int guard = 0; guard < 100; guard++)
        {
            var plan = ImperialPurchases.Plan(table, p, deckId, owned, balance, seed);
            if (plan == null || plan.CardId == null) break;
            if (plan.XpCost > balance) { fails++; Console.WriteLine("FAIL overpay|" + plan.CardId); break; }
            owned.Add(plan.CardId);
            balance -= plan.XpCost;
            bought.Add(plan.CardId);
        }
        return balance;
    }

    public static int Run()
    {
        var path = System.IO.Path.Combine(App, "Assets", "Resources", "Languages", "En",
                                          "tactical_imperial_decks.json");
        var doc = Json.AsDict(Json.Parse(System.IO.File.ReadAllText(path)));
        ImperialDecks.PrimeDoc(doc);
        var table = ImperialDecks.Load();
        Eq(12, table.Decks.Count, "deck_count");

        var save = Json.AsDict(Json.Parse(@"{
            ""GUID"": ""0f1e2d3c-4b5a-4938-8726-152433221100"",
            ""campaignHeroes"": [{""heroID"":""H1""},{""heroID"":""H2""},{""heroID"":""H4""}],
            ""campaignVillains"": [{""name"":""Scout Trooper""},{""name"":""Assault Trooper""},
              {""name"":""Imperial Probe Droid""},{""name"":""Heavy Weapon Expert""},
              {""name"":""Omblesse Officer""}],
            ""campaignStructure"": [{""missionType"":0},{""missionType"":0},{""missionType"":1,""isAgendaMission"":true},
              {""missionType"":1},{""missionType"":0},{""missionType"":0,""isAgendaMission"":true},
              {""missionType"":5}]}"));
        var profile = ImperialCampaignProfile.ProfileFromCampaignSave(save);
        var seed = ImperialDecks.DeckSeedFor("0f1e2d3c-4b5a-4938-8726-152433221100");
        var deckId = ImperialDecks.Choose(profile, seed).DeckId;
        Eq("hutt_mercenaries", deckId, "deck_of_start_core");
        var deck = table.Find(deckId);

        // noise vectors re-derive bit-identically (two calls)
        var paidIds = deck.Cards.Where(c => !c.Basic && c.Xp.HasValue)
                                .Select(c => c.Id).ToList();
        bool noiseStable = paidIds.All(id =>
            ImperialPurchases.NoiseForPurchase(seed, id) ==
            ImperialPurchases.NoiseForPurchase(seed, id));
        Eq(true, noiseStable, "noise_rederivable");

        // rich stage: buys every paid card exactly once, basics never
        var owned1 = new HashSet<string>(deck.BasicCards.Select(c => c.Id), StringComparer.Ordinal);
        var basics = new HashSet<string>(deck.BasicCards.Select(c => c.Id), StringComparer.Ordinal);
        var bought = new List<string>();
        long balance = Stage(table, profile, deckId, owned1, 99, seed, bought);
        Eq(8, bought.Count, "rich_stage_buys_all_paid");
        Eq(true, paidIds.OrderBy(x => x).SequenceEqual(bought.OrderBy(x => x)), "rich_stage_exactly_once");
        Eq(true, bought.All(b => !basics.Contains(b)), "basics_never_bought");
        Eq(99L - paidIds.Sum(id => deck.Cards.First(c => c.Id == id).Xp.Value), balance, "rich_stage_balance");

        // exhausted deck: a further stage on the full account buys nothing
        var more = new List<string>();
        Stage(table, profile, deckId, owned1, 99, seed, more);
        Eq(0, more.Count, "exhausted_deck_stops");

        // affordability: one XP can never buy a costlier card
        var owned2 = new HashSet<string>(deck.BasicCards.Select(c => c.Id), StringComparer.Ordinal);
        var cheap = new List<string>();
        Stage(table, profile, deckId, owned2, 1, seed, cheap);
        Eq(true, cheap.All(id => deck.Cards.First(c => c.Id == id).Xp.Value <= 1), "never_overpay");

        // determinism: a second run from scratch gives the identical order
        var owned3 = new HashSet<string>(deck.BasicCards.Select(c => c.Id), StringComparer.Ordinal);
        var bought2 = new List<string>();
        Stage(table, profile, deckId, owned3, 99, seed, bought2);
        Eq(string.Join(",", bought), string.Join(",", bought2), "greedy_replay_identical");

        // 'kein Deck' contract: unknown deck and empty table stay null
        Eq(null, ImperialPurchases.Plan(table, profile, "no_such_deck", owned1, 99, seed), "unknown_deck_null");
        Eq(null, ImperialPurchases.Plan(new ImperialClassDeckTable(), profile, deckId, owned1, 99, seed), "empty_table_null");

        Console.WriteLine(fails == 0 ? "ImperialPurchaseGate: ALL PASS"
                                     : "ImperialPurchaseGate: " + fails + " FAILURES");
        if (fails != 0) System.Environment.ExitCode = 1;
        return fails;
    }
}
