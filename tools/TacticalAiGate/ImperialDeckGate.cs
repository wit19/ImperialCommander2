// M12b live gate: the SHIPPED tactical_imperial_decks.json must load into the
// Core table, seed derivation and the full choice must be re-derivable from
// file with zero fixtures, and the load-backfill contract (same seed + same
// profile -> bit-identical choice, twice) must hold on the real asset.
using System;
using System.Collections.Generic;
using System.Linq;
using TacticalAI.Core;

public static class ImperialDeckGate
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

    public static int Run()
    {
        var path = System.IO.Path.Combine(App, "Assets", "Resources", "Languages", "En",
                                          "tactical_imperial_decks.json");
        var doc = Json.AsDict(Json.Parse(System.IO.File.ReadAllText(path)));
        ImperialDecks.PrimeDoc(doc);
        var table = ImperialDecks.Load();

        Eq(12, table.Decks.Count, "deck_count");
        Eq(109, table.CardCount, "card_count");
        Eq("armored_onslaught,hutt_mercenaries,imperial_black_ops," +
           "inspiring_leadership,military_might,nemeses,overwhelming_oppression," +
           "power_of_the_dark_side,precision_training,reactive_defenses," +
           "subversive_tactics,technological_superiority",
           string.Join(",", table.Decks.Select(d => d.Id)), "deck_order");

        // seed derivation from a campaign GUID (mirrors the canon vectors)
        Eq("7074e2f2197e0c4d",
           ImperialDecks.SeedHex(ImperialDecks.DeckSeedFor(
               "0f1e2d3c-4b5a-4938-8726-152433221100")), "seed.core_guid");

        // full save-shaped choice (the canon start_core profile)
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
        var c1 = ImperialDecks.Choose(profile, seed);
        var c2 = ImperialDecks.Choose(profile, seed);   // the backfill second load
        Eq("hutt_mercenaries", c1.DeckId, "core_choice");
        Eq(39L, c1.Scores[0].Total, "core_total");
        Eq(c1.SeedHex, c2.SeedHex, "backfill.seed_identical");
        Eq(c1.DeckId, c2.DeckId, "backfill.deck_identical");
        Eq(string.Join(";", c1.Scores.Select(r => r.Deck + "=" + r.Total)),
           string.Join(";", c2.Scores.Select(r => r.Deck + "=" + r.Total)),
           "backfill.scores_identical");

        // 'kein Deck' is valid: with NO primed doc the loader path returns a
        // null choice (callers keep the null state), never a crash.
        var empty = new ImperialCampaignProfile();
        var noDoc = ImperialDecks.Choose(new ImperialClassDeckTable(), empty, 7UL);
        Eq(null, noDoc, "empty_table_null");

        Console.WriteLine(fails == 0 ? "ImperialDeckGate: ALL PASS"
                                     : "ImperialDeckGate: " + fails + " FAILURES");
        if (fails != 0) System.Environment.ExitCode = 1;
        return fails;
    }
}
