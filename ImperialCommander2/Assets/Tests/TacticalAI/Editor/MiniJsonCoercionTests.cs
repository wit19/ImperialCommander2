using NUnit.Framework;
using TacticalAI.Core;

namespace TacticalAI.Tests
{
    /// <summary>Regression pins for the first live playtest (27.10): the game's
    /// DataStore feeds boxed int/float/string values into FromRecord, which the
    /// old AsLong/AsDouble rejected ("JSON: not a number" for all 69 cards).
    /// Fixture-JSON parsing never produced those shapes - live game data does.</summary>
    [Explicit("C# unit tests; run via Test Runner or mini-runner")]
    [TestFixture]
    public class MiniJsonCoercionTests
    {
        [Test]
        public void AsLong_Accepts_Int_From_Game_DataStore()
        {
            object boxed = 7; // System.Int32, the exact live failure shape
            Assert.AreEqual(7L, Json.AsLong(boxed));
        }

        [Test]
        public void AsLong_Accepts_Float_And_InvariantString()
        {
            Assert.AreEqual(3L, Json.AsLong(3.9f));
            Assert.AreEqual(12L, Json.AsLong("12"));
        }

        [Test]
        public void AsDouble_Accepts_Int_Float_And_InvariantString()
        {
            Assert.AreEqual(2.0, Json.AsDouble(2), 10e-9);
            Assert.AreEqual(1.5, Json.AsDouble(1.5f), 10e-5);
            Assert.AreEqual(1.25, Json.AsDouble("1.25"), 10e-9);
        }

        [Test]
        public void AsLong_Rejects_NonNumeric_WithTypeHint()
        {
            var ex = Assert.Throws<System.FormatException>(() => Json.AsLong("x,y"));
            StringAssert.Contains("String", ex.Message);
        }

        [Test]
        public void AsList_Accepts_DotNetArrays_From_Game_Adapter()
        {
            // live playtest 27.10 round 2: bridge ToRecord passes string[]
            // (keywords/surges) and ToArray() results (attacks/defense);
            // the strict List<object> cast skipped all 69 catalog cards
            // ("JSON: not an array") and the planner degraded to legacy.
            object kw = new[] { "Big", "Reach" };
            var list = Json.AsList(kw);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("Big", list[0]);

            var sysList = new System.Collections.Generic.List<string> { "a" };
            Assert.AreEqual(1, Json.AsList(sysList).Count);
        }

        [Test]
        public void AsList_Still_Rejects_Scalar_With_TypeHint()
        {
            var ex = Assert.Throws<System.FormatException>(() => Json.AsList(7));
            StringAssert.Contains("Int32", ex.Message);
        }
    }
}
