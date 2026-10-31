// M13a live gate: the label classification (unnumbered "Anweisung:" solo vs.
// numbered "Aktion {n}" + tree-lead header) must follow the ACTION count on
// EVERY card of the SHIPPED order_decks.json - zero fixtures. M10 update
// (Thomas 31.10.2026, "Wir spielen nach Referenz Regeln!"): the RR deck law
// makes every deck the uniform 8x[move,attack] reference deck - every normal
// activation shows BOTH actions. The only legal single-action cards left are
// the three pure-attack cards of DG007 (E-Web Engineer): its printed Stativ/
// Tripod rule makes movement and attack mutually exclusive, so a one-action
// activation is the printed rule there (exemption per Thomas' ruling).
// Checks: total card count, the solo/numbered distribution, that every dealt
// card is labelable (1 or 2 actions - never 0, never >2 = Profil-A budget),
// zero solos outside the Stativ exemption, and the DG007 exemption shape 3
// solo + 5 numbered.
using System;
using System.Collections.Generic;
using TacticalAI.Core;

public static class ActionTreeGate
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
        var path = System.IO.Path.Combine(App, "Assets", "Resources", "TacticalAI",
                                          "order_decks.json");
        var root = Json.AsDict(Json.Parse(System.IO.File.ReadAllText(path)));
        var decks = Json.AsDict(Json.Get(root, "decks", new Dictionary<string, object>()));

        int total = 0, solo = 0, numbered = 0, other = 0;
        foreach (var kv in decks)
            foreach (var raw in Json.AsList(Json.AsDict(kv.Value)["cards"]))
            {
                var rec = Json.AsDict(raw);
                var blocks = OrderDeck.StrArr(Json.AsList(rec["blocks"]));
                int a = OrderDeck.CountActionBlocks(blocks);
                total++;
                if (a == 1) solo++;
                else if (a == 2) numbered++;
                else other++;
            }
        Eq(712, total, "order_cards_total");
        Eq(3, solo, "solo_labelled_cards");
        Eq(709, numbered, "numbered_labelled_cards");
        Eq(0, other, "every_card_labelable_solo_or_tree");

        // RR deck law (Thomas 31.10.2026): Stormtrooper DG001 - the deck of
        // the 28.10 playtest finding - now shows BOTH actions on every card;
        // zero single-action cards. The Stativ exemption: DG007 (E-Web
        // Engineer, printed Tripod rule) keeps 3 pure-attack + 5 move+attack
        // cards - one action per activation IS its printed rule.
        int s1 = 0, m1 = 0;
        foreach (var raw in Json.AsList(Json.AsDict(decks["DG001"])["cards"]))
        {
            var blocks = OrderDeck.StrArr(Json.AsList(Json.AsDict(raw)["blocks"]));
            int a = OrderDeck.CountActionBlocks(blocks);
            if (a == 1) s1++; else m1++;
        }
        Eq(0, s1, "DG001_stormtrooper_solo_cards_RR_law");
        Eq(8, m1, "DG001_stormtrooper_numbered_cards_RR_law");
        int s7 = 0, m7 = 0;
        foreach (var raw in Json.AsList(Json.AsDict(decks["DG007"])["cards"]))
        {
            var blocks = OrderDeck.StrArr(Json.AsList(Json.AsDict(raw)["blocks"]));
            int a = OrderDeck.CountActionBlocks(blocks);
            if (a == 1) s7++; else m7++;
        }
        Eq(3, s7, "DG007_eweb_stativ_solo_cards_printed_rule");
        Eq(5, m7, "DG007_eweb_stativ_numbered_cards");

        Console.WriteLine(fails == 0 ? "ActionTreeGate: ALL PASS"
                                     : "ActionTreeGate: " + fails + " FAILURES");
        if (fails != 0) System.Environment.ExitCode = 1;
        return fails;
    }
}
