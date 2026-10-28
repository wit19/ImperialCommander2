// M13a activation-popup labelling section: the pure decision behind the
// unnumbered solo line ("Anweisung: {0}") vs. the numbered Wenn-Dann tree
// ("Aktion {n} - {1}" + doctrineTreeLead header). OrderDeck.CountActionBlocks
// is presentation-only (planning behaviour untouched, Canon vectors unmoved)
// and has no sim counterpart, so the expect blocks are hand-pinned ground
// truth - the same documented route as logger_extract.json, not --record.
// The label STRINGS themselves live in ui.json (localization convention) and
// are rendered bridge-side (Unity); only the solo/numbered decision is pinned
// here, plus the live distribution on the SHIPPED asset in ActionTreeGate.
using System;
using System.Collections.Generic;
using System.Linq;
using TacticalAI.Core;

namespace TacticalAiHarness
{
    public static partial class Program
    {
        static void SecActionTree()
        {
            var v = LoadJson("action_tree.json");
            foreach (var raw in Json.AsList(v["cases"]))
            {
                var c = Json.AsDict(raw);
                var name = Json.AsStr(c["name"]);
                var blocks = Json.AsList(c["blocks"]).Select(x => Json.AsStr(x)).ToArray();
                var exp = Json.AsDict(c["expect"]);
                long got = OrderDeck.CountActionBlocks(blocks);
                Check("action_tree[" + name + "].actions", Json.AsLong(exp["actions"]), got);
                Check("action_tree[" + name + "].solo", Json.AsBool(exp["solo"]), got == 1);
                Check("action_tree[" + name + "].numbered", Json.AsBool(exp["numbered"]), got >= 2);
            }
        }
    }
}
