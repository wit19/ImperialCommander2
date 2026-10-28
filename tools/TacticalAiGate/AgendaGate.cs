// M12d2 live gate: the SHIPPED tactical_agenda_cards.json must feed the
// agenda engine end to end on the REAL store class (Saga.AgendaState,
// compiled straight from Assets — no mirror) with zero fixtures. Full-
// campaign simulation: book influence, run every upgrade stage until the
// deck is empty or the cap hits; invariants on every stage (market size,
// no overpay, deck conservation across the return-mix, no card bought
// twice), a persistent anti-leak scan (no unrevealed Kept Secret id or
// printed name ever reaches the rebel view, while the audit view DOES
// know it - the check discriminates), and byte-identical replay of two
// full campaigns from the same seed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TacticalAI.Core;

public static class AgendaGate
{
    static int fails = 0;
    static void Eq(object expected, object actual, string what)
    {
        bool ok = Equals(expected, actual);
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what + " | expected=" + expected + " actual=" + actual);
        if (!ok) fails++;
        if (!ok) Environment.ExitCode = 1;
    }

    static readonly string App =
        "/home/open/work/ia-ai/ImperialCommander2/ImperialCommander2";

    // Minimal deterministic serializer for the leak scan (no fixtures,
    // no ordering assumptions - everything is string data here anyway).
    static string Flat(object v)
    {
        var sb = new StringBuilder();
        W(sb, v);
        return sb.ToString();
    }
    static void W(StringBuilder sb, object v)
    {
        if (v == null) { sb.Append("~"); return; }
        if (v is string s) { sb.Append('"').Append(s).Append('"'); return; }
        if (v is bool b) { sb.Append(b ? "1" : "0"); return; }
        if (v is long l) { sb.Append(l); return; }
        if (v is int i) { sb.Append(i); return; }
        if (v is Dictionary<string, object> d)
        {
            sb.Append('{');
            foreach (var k in d.Keys.OrderBy(x => x, StringComparer.Ordinal))
            { sb.Append(k).Append(':'); W(sb, d[k]); sb.Append(','); }
            sb.Append('}');
            return;
        }
        if (v is IList list)
        {
            sb.Append('[');
            foreach (var o in list) { W(sb, o); sb.Append(','); }
            sb.Append(']');
            return;
        }
        sb.Append('"').Append(v).Append('"');
    }

    public static int Run()
    {
        var path = System.IO.Path.Combine(App, "Assets", "Resources", "Languages", "En",
                                          "tactical_agenda_cards.json");
        var doc = Json.AsDict(Json.Parse(System.IO.File.ReadAllText(path)));
        AgendaCards.PrimeDoc(doc);
        var table = AgendaCards.Load();
        Eq(42, table.Sets.Count, "agenda_gate.set_count");
        Eq(126, table.Cards.Count, "agenda_gate.card_count");

        var save = Json.AsDict(Json.Parse(@"{
            ""GUID"": ""77aa55cc-33bb-44dd-9eff-011223344556"",
            ""campaignHeroes"": [{""heroID"":""H1""},{""heroID"":""H2""},{""heroID"":""H3""},
              {""heroID"":""H4""},{""heroID"":""H5""}],
            ""campaignVillains"": [{""name"":""Scout Trooper""},{""name"":""Assault Trooper""},
              {""name"":""Imperial Probe Droid""},{""name"":""Heavy Weapon Expert""},
              {""name"":""Omblesse Officer""}],
            ""campaignStructure"": [{""missionType"":0},{""missionType"":1,""isAgendaMission"":true},
              {""missionType"":3},{""missionType"":1,""isAgendaMission"":true},
              {""missionType"":0},{""missionType"":5}]}"));

        // ---- one full campaign, twice, against the real store class
        string traceA = null, traceB = null;
        for (int run = 0; run < 2; run++)
        {
            var seed = AgendaCards.AgendaSeedFor(save["GUID"].ToString());
            var profile = ImperialCampaignProfile.ProfileFromCampaignSave(save);
            var choice = AgendaCards.ChooseDeck(table, profile, seed, null);
            var state = Saga.AgendaState.FromChoice(choice, save["GUID"].ToString());
            var trace = new StringBuilder();
            var boughtOnce = new HashSet<string>(StringComparer.Ordinal);
            var hiddenKnown = new List<string>();   // kept secrets still unrevealed
            int stages = 0, totalBuys = 0;

            while (state.deck.Count > 0 && stages < 24)
            {
                state.AddInfluence(2, "gate:mission");      // income every mission
                int deckBefore = state.deck.Count;
                long balBefore = state.influenceBalance;
                var plan = AgendaMarket.RunStage(table, profile, state.deck,
                    state.influenceBalance, seed, state.stagesRun);
                if (plan == null) break;

                Eq(true, plan.Market.Count <= Math.Min(4, deckBefore), "gate" + run + ".market_size" + stages);
                long spent = plan.Buys.Sum(b => b.Cost);
                Eq(true, spent <= balBefore, "gate" + run + ".no_overpay" + stages);

                var boughtIds = new List<string>();
                foreach (var b in plan.Buys)
                {
                    Eq(true, boughtOnce.Add(b.Card), "gate" + run + ".no_double_buy." + b.Card);
                    if (!state.TryBuyAgenda(b.Card, b.Type, (int)b.Cost, (int)plan.Stage, "buy:" + b.Card))
                    { Eq(true, false, "gate" + run + ".book." + b.Card); break; }
                    boughtIds.Add(b.Card);
                    if (b.Type == AgendaCards.TypeKeptSecret) hiddenKnown.Add(b.Card);
                    if (b.Type == AgendaCards.TypeKeptSecret)
                    {
                        // the printed name must ALSO be unknown to the scan
                        var card = table.Find(b.Card);
                        hiddenKnown.Add(card == null ? b.Name : card.Name);
                    }
                }
                state.AddMarketAudit((int)plan.Stage, plan.Market, boughtIds,
                                     plan.NotPurchased, plan.SeedHex);
                state.ReplaceDeckAfterStage(plan.DeckAfter);

                // deck conservation: after = before minus exactly the buys
                Eq(deckBefore - boughtIds.Count, state.deck.Count, "gate" + run + ".conservation" + stages);
                Eq(0, state.deck.Intersect(boughtOnce).Count(), "gate" + run + ".bought_leave_deck" + stages);
                trace.Append(Flat(plan.ToDict())).Append('|');

                // ---- persistent anti-leak scan after EVERY stage
                var rebelFlat = Flat(state.RebelView());
                foreach (var h in hiddenKnown)
                    Eq(-1, rebelFlat.IndexOf(h, StringComparison.Ordinal),
                       "gate" + run + ".no_leak[" + h.Substring(0, Math.Min(h.Length, 20)) + "]@" + stages);
                var auditFlat = Flat(state.ImperialAuditView());
                foreach (var b in boughtIds)
                    Eq(true, auditFlat.IndexOf(b, StringComparison.Ordinal) >= 0,
                       "gate" + run + ".audit_knows@" + stages);

                totalBuys += boughtIds.Count;
                stages++;
            }
            trace.Append("#").Append(totalBuys).Append('|').Append(state.influenceBalance)
                 .Append('|').Append(state.deck.Count);
            if (run == 0)
            {
                traceA = trace.ToString();
                Eq(true, totalBuys > 0, "gate0.buys_happened");
                Eq(true, stages > 1, "gate0.multiple_stages");
                Eq(true, state.deck.Count < 18, "gate0.deck_shrank");
            }
            else traceB = trace.ToString();
        }
        Eq(traceA == traceB, true, "gate.replay_identical");
        Console.WriteLine("agenda_gate: full-campaign sim done, fails=" + fails);
        return fails;
    }
}
