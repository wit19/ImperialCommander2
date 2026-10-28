// M13a live gate: the label classification (unnumbered "Anweisung:" solo vs.
// numbered "Aktion {n}" + tree-lead header) must follow the ACTION count on
// EVERY card of the SHIPPED order_decks.json - zero fixtures. The 28.10.
// playtest finding (Stormtrooper popup showed only "Aktion 1" and players
// waited for a missing "Aktion 2") is grounded here: the deck mix {'A':2,
// 'MA':6} of DG001 prints single-action ['attack'] cards BY DESIGN (Feuer-
// stellung), so the fix is presentation, never a planning change. Checks:
// total card count, the solo/numbered distribution, that every dealt card is
// labelable (1 or 2 actions - never 0, never >2 = Profil-A budget), and the
// DG001 playtest shape 2 solo + 6 numbered.
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
        Eq(132, solo, "solo_labelled_cards");
        Eq(580, numbered, "numbered_labelled_cards");
        Eq(0, other, "every_card_labelable_solo_or_tree");

        // Playtest 28.10 (Thomas, screenshots 1+2): Stormtrooper DG001 carries
        // mix {'A':2,'MA':6} - the ['attack'] cards DG001-7/8 are the popup
        // that showed a lone "Aktion 1"; the move+attack cards stay numbered.
        int s1 = 0, m1 = 0;
        foreach (var raw in Json.AsList(Json.AsDict(decks["DG001"])["cards"]))
        {
            var blocks = OrderDeck.StrArr(Json.AsList(Json.AsDict(raw)["blocks"]));
            int a = OrderDeck.CountActionBlocks(blocks);
            if (a == 1) s1++; else m1++;
        }
        Eq(2, s1, "DG001_stormtrooper_solo_cards");
        Eq(6, m1, "DG001_stormtrooper_numbered_cards");

        Console.WriteLine(fails == 0 ? "ActionTreeGate: ALL PASS"
                                     : "ActionTreeGate: " + fails + " FAILURES");
        if (fails != 0) System.Environment.ExitCode = 1;
        return fails;
    }
}
