// Byte-exact golden rows: one Focus tie-break row and one doctrine card-text
// row (the same rows the console harness compares). "Byte-exact" = the
// normalized JSON value of the Core output must equal the fixture row
// element-for-element, keys sorted ordinal - no tolerance, no float fuzz on
// Rational/why strings.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TacticalAI.Core;

namespace TacticalAI.Tests
{
    [Explicit("Requires Test Framework")]
    [TestFixture]
    public class VectorRowTests
    {
        [Test]
        public void Focus_Tiebreak_Row_ByteExact()
        {
            // focus.json row for the tie-break fixture: two rebels tied at
            // 0 movement points -> printed tiebreak_trace + why string.
            var rows = TacaiFixtures.LoadList("focus.json").Select(Json.AsDict)
                .Where(e => Json.AsStr(e["scenario"]) == "counter_bearer")
                .ToList();
            Assert.AreEqual(1, rows.Count);
            var e = rows[0];

            var scenario = Json.AsDict(TacaiFixtures.Scenarios()[Json.AsStr(e["scenario"])]);
            var (board, world) = TacaiFixtures.BuildScenarioWorld(scenario);
            var imps = world.Figures.Where(f => f.Team == "imperial").ToList();
            var pure = Json.AsList(e["pure"]);

            Assert.Greater(pure.Count, 0);
            for (int k = 0; k < imps.Count && k < pure.Count; k++)
                TacaiFixtures.Check("focus[counter_bearer].pure[" + k + "]", pure[k],
                    FocusResolver.ComputeFocus(world, imps[k]).AsDict());

            // explicit tie-break content assertion (human-readable anchor):
            var first = Json.AsDict(pure[0]);
            var trace = Json.AsList(first["tiebreak_trace"]);
            Assert.Greater(trace.Count, 1, "tie-break row must carry >1 candidate");
            Assert.That(Json.AsStr(first["why"]), Does.Contain("tied"));
        }

        [Test]
        public void Doctrine_Card_Text_Row_ByteExact()
        {
            // doctrine.json eliminate_duel round 1: the full per-pattern
            // doctrine card payload (rules, stance, why, refs) - the text the
            // player would see, straight from the golden vector.
            var rows = TacaiFixtures.LoadList("doctrine.json").Select(Json.AsDict)
                .Where(e => Json.AsStr(e["scenario"]) == "eliminate_duel")
                .ToList();
            Assert.AreEqual(1, rows.Count);
            var e = rows[0];

            var scenario = Json.AsDict(TacaiFixtures.Scenarios()[Json.AsStr(e["scenario"])]);
            var (board, world) = TacaiFixtures.BuildScenarioWorld(scenario);
            var vm = MissionVictoryModel.FromBoard(board);
            if (scenario.ContainsKey("victoryCounter"))
                vm.Counter = new VictoryCounter(Json.AsStr(scenario["victoryCounter"]));
            vm.Evaluate(1, world);
            world.Victory = vm;
            var entrances = Json.AsCells(Json.Get(scenario, "entrances", new List<object>()));
            var feed = new WorldFeed(world, entrances);

            var expRound = Json.AsDict(Json.AsList(e["rounds"])[0]);   // round 1
            vm.Evaluate(1, world);
            feed.Advance(1);
            var view = feed.DoctrineView(1, vm);
            TacaiFixtures.Check("doctrine[eliminate_duel].r1.round", expRound["round"], (long)1);
            TacaiFixtures.Check("doctrine[eliminate_duel].r1.appview", expRound["appview"], view.Public());

            var expDocs = Json.AsDict(expRound["doctrines"]);
            var docs = new Dictionary<string, object>();
            foreach (var pat in Patterns.PatternsList)
                docs[pat] = Doctrine.SelectDoctrine(view, pat).AsDict();
            TacaiFixtures.Check("doctrine[eliminate_duel].r1.doctrines", expDocs, docs);
        }
    }
}
