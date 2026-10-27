// Public data assets + threat economy: every order deck in the SHIPPED
// order_decks.json must pass ValidateCard, and the ThreatLedger replay must
// stay byte-exact against threat.json (balance conservation, seeded release
// decisions - the same rows the console harness verifies).
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TacticalAI.Core;

namespace TacticalAI.Tests
{
    [Explicit("Requires Test Framework")]
    [TestFixture]
    public class OrderDeckAssetTests
    {
        [Test]
        public void Order_Decks_Asset_Loads_And_Every_Card_Passes_ValidateCard()
        {
            var raw = Json.AsDict(TacaiFixtures.Load("order_decks.json"));
            var decks = Json.AsDict(raw["decks"]);
            Assert.Greater(decks.Count, 0);

            foreach (var kv in TacaiFixtures.OrdKeys(decks))
            {
                var entry = Json.AsDict(kv.Value);
                foreach (var cO in Json.AsList(entry["cards"]))
                {
                    var rec = Json.AsDict(cO);
                    var card = new OrderCard(Json.AsStr(rec["id"]),
                        Json.AsList(rec["blocks"]).Select(Json.AsStr).ToArray());
                    var violations = OrderDeck.ValidateCard(card);
                    Assert.IsEmpty(violations,
                        kv.Key + "/" + card.Id + " violations: " + string.Join(" | ", violations));
                }
            }
        }

        [Test]
        public void OrderDeckManager_Loads_Public_Asset()
        {
            // same load path the Unity bridge uses (TacticalAIAssets -> here: fixture)
            var mgr = OrderDeckManager.Load(TacaiFixtures.Load("order_decks.json"));
            Assert.Greater(mgr.DecksByCard.Count, 0);
            var dg001 = mgr.Deck("DG001");
            Assert.AreEqual(8, dg001.Count, "one deck per printed group is 8 cards");
        }

        [Test]
        public void ValidateCard_Fixture_Rows_ByteExact()
        {
            var v = Json.AsDict(TacaiFixtures.Load("decks.json"));
            int i = 0;
            foreach (var r in Json.AsList(v["validate"]).Select(Json.AsDict))
            {
                var blocks = Json.AsList(r["blocks"]).Select(Json.AsStr).ToArray();
                var card = new OrderCard(Json.AsStr(r["id"]), blocks);
                TacaiFixtures.Check("validate[" + i + "]", r["violations"], OrderDeck.ValidateCard(card));
                i++;
            }
            Assert.Greater(i, 0);
        }
    }

    [Explicit("Requires Test Framework")]
    [TestFixture]
    public class ThreatLedgerTests
    {
        [Test]
        public void ThreatLedger_Replay_Conservation_ByteExact()
        {
            // Replays every threat.json row: per-round income/balance/pick and
            // the final ledger dict. Conservation invariant (earned income -
            // group costs == balance) is pinned by the final dict equality.
            foreach (var eO in TacaiFixtures.LoadList("threat.json").Select(Json.AsDict)
                        .Select((e, i) => (e, i)))
            {
                var e = eO.e; int idx = eO.i;
                int save = Json.AsInt(e["save"]);
                int idle = Json.AsInt(e["idle"]);
                var led = new ThreatLedger(save, idle);
                var res = new List<ReserveGroup>
                {
                    new ReserveGroup("gA", "DG001", 4, "aggressive"),
                    new ReserveGroup("gB", "DG021", 2, "guard"),
                    new ReserveGroup("gC", "DG009", 6, "blocker"),
                    new ReserveGroup("gD", "DG021", 2, "hunter"),
                };
                int alive = 2;
                int earned = 0;
                int k = 0;
                foreach (var s in Json.AsList(e["seq"]).Select(Json.AsDict))
                {
                    int rnd = Json.AsInt(s["round"]);
                    long inc = led.Tick(rnd, alive);
                    earned += (int)inc;
                    var pick = led.ReleaseDecision(rnd, res, alive);
                    if (pick != null)
                    {
                        bool forced = (rnd - led.LastReleaseRound) >= idle;
                        led.Release(rnd, pick, forced);
                        alive += 1;
                    }
                    TacaiFixtures.Check("threat[" + idx + "].seq[" + k + "]", s,
                        new Dictionary<string, object>
                        {
                            { "round", (long)rnd },
                            { "income", inc },
                            { "balance", led.Balance },
                            { "pick", pick == null ? null : (object)pick.GroupId },
                            { "wait", led.WaitPressure(res) },
                        });
                    k++;
                }
                TacaiFixtures.Check("threat[" + idx + "].final", e["final"], led.AsDict());

                // explicit conservation invariant: nothing materialises from
                // nowhere - balance never exceeds what the rounds have earned.
                Assert.LessOrEqual(led.Balance, earned,
                    "threat[" + idx + "]: spent-less balance exceeds total income");
            }
        }
    }
}
