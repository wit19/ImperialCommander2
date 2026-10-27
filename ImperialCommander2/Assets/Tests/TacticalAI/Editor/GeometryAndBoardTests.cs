// Geometry sanity + all board fixtures (concept 4.1 vector set A/B).
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TacticalAI.Core;

namespace TacticalAI.Tests
{
    [Explicit("Requires Test Framework")]
    [TestFixture]
    public class GeometryTests
    {
        [Test]
        public void FootprintSizes_And_Dirs_Match_Golden()
        {
            var v = Json.AsDict(TacaiFixtures.Load("geometry.json"));
            foreach (var kv in TacaiFixtures.OrdKeys(Json.AsDict(v["footprint_sizes"])))
                TacaiFixtures.Check("footprint_sizes." + kv.Key, kv.Value, Geometry.FootprintSizes[kv.Key]);
            TacaiFixtures.Check("dirs", v["dirs"], Geometry.DirVectors.ToList());
        }

        [Test]
        public void Footprint_Cases_Match_Golden()
        {
            var v = Json.AsDict(TacaiFixtures.Load("geometry.json"));
            int i = 0;
            foreach (var cO in Json.AsList(v["cases"]).Select(Json.AsDict))
            {
                var c = cO;
                var anchor = Json.AsCell(c["anchor"]);
                var mini = Json.AsStr(c["mini"]);
                int orient = Json.AsInt(c["orient"]);
                TacaiFixtures.Check("case[" + i + "].cells", c["cells"],
                    Geometry.FootprintCells(anchor, mini, orient));
                TacaiFixtures.Check("case[" + i + "].dims", c["dims"], Geometry.FootprintDims(mini, orient));
                TacaiFixtures.Check("case[" + i + "].is_large", c["is_large"], Geometry.IsLarge(mini));
                i++;
            }
            Assert.Greater(i, 0, "fixture had no cases");
        }

        [Test]
        public void Adjacency_Match_Golden()
        {
            var v = Json.AsDict(TacaiFixtures.Load("geometry.json"));
            int i = 0;
            foreach (var c in Json.AsList(v["adjacency"]).Select(Json.AsDict))
            {
                var a = new HashSet<Cell>(Json.AsCells(c["a"]));
                var b = new HashSet<Cell>(Json.AsCells(c["b"]));
                TacaiFixtures.Check("adjacency[" + i + "].adjacent", c["adjacent"], Geometry.CellsAdjacent(a, b));
                TacaiFixtures.Check("adjacency[" + i + "].gap", c["gap"], Geometry.ChebyshevGap(a, b));
                i++;
            }
        }
    }

    [Explicit("Requires Test Framework")]
    [TestFixture]
    public class BoardLoadTests
    {
        static readonly string[] BaseBoards =
        {
            "open_map_two_entrances", "rancor_corridor", "siege_courtyard", "terminal_two_room",
        };

        [Test]
        public void All_BaseBoards_Load_With_Size_And_Deployment_Points()
        {
            foreach (var name in BaseBoards)
            {
                var b = TacaiFixtures.BoardOf(name);
                Assert.AreEqual(name, b.Name);
                Assert.Greater(b.Width, 0, name + ": width");
                Assert.Greater(b.Height, 0, name + ": height");
                Assert.IsNotEmpty(b.DeploymentPoints, name + ": no deployment points");
                foreach (var dp in b.DeploymentPoints)
                    Assert.IsNotEmpty(dp.Cells, name + "/" + dp.Id + ": empty DP cells");
            }
        }

        [Test]
        public void Scenario_Boards_Load_From_Boards_Dir()
        {
            // every scenario referenced by the vector files must have its board
            foreach (var kv in TacaiFixtures.Scenarios())
            {
                if (!(kv.Value is Dictionary<string, object>)) continue;  // meta entries
                var spec = Json.AsDict(kv.Value);
                if (!spec.ContainsKey("board")) continue;
                var b = TacaiFixtures.BoardOf(Json.AsStr(spec["board"]));
                Assert.Greater(b.Width * b.Height, 0, kv.Key + ": empty board");
            }
        }
    }
}
