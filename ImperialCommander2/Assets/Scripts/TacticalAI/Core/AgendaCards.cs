// M12d2 Agenda cards (FFG Rules Reference, "Agenda Cards" / "Building the
// Agenda Deck" / "Imperial Upgrade Stage", verbatim core law):
//   * "During campaign setup, the Imperial player chooses six sets of Agenda
//      cards and shuffles them together to create his Agenda deck."
//   * "Each Agenda set consists of three cards." / "All Agenda cards not
//      chosen during campaign setup are returned to the game box."
//   * "The Imperial player cannot choose an Agenda set if it contains a card
//      with a listed time period range that does not include the time period
//      of the campaign." / "If an Agenda set ... does not have a time period
//      displayed, it can be used in any campaign."
//   * "To purchase Agenda cards, the Imperial player shuffles the Agenda deck
//      and secretly draws four of them. He may purchase any of these cards
//      by spending influence equal to the cards' costs."
//   * "The Imperial player does not show his drawn Agenda cards to the
//      heroes. Any cards he does not purchase are shuffled back into the
//      deck without being revealed."
//   * "Most Agenda cards are immediately read aloud and resolved when
//      purchased. The only exception is if the card instructs the player to
//      'keep this card secret.'"
//   * Influence ("Influence"): tracked on the campaign log, gain/spend both
//      recorded, unspent influence carries over (saving up is legal).
//
// FAIRNESS CANON (controller ruling 28.10., overrides older fairness text):
// the KI follows the reference rules AS THEY ARE. A face-down 4-card market
// and Kept Secret agendas are game mechanics, not cheating - the KI may use
// them exactly like a human imperial. Equal rules, NOT more and NOT less
// knowledge: draws are seeded from the campaign seed (auditable in the
// STORE, invisible in the UI), and Kept Secret content never leaks into any
// rebel-facing view until the card itself demands resolution.
//
// Determinism (same law as M12b/M12c): no UnityEngine.Random, no wall time,
// no order side-channels. Every choice is a pure function of
//   seed    - ImperialDecks.DeckSeedFor(GUID) salted per decision below,
//   profile - the M12b ImperialCampaignProfile,
//   table   - the shipped agenda-card asset.
//
// Time period: the app does NOT model a campaign-level time period (the
// MissionCard.timePeriod field is per-mission display data only - checked in
// M12d2 PRUEFAUFTRAG), so the RR filter is data-driven and VACUOUS by
// default: LegalSets() only excludes a set when the caller supplies an
// explicit campaign time period. Null is honest "app does not know", never
// "everything is legal because I decided so".
using System;
using System.Collections.Generic;
using System.Linq;

namespace TacticalAI.Core
{
    /// <summary>One printed Agenda card (asset fields 1:1).</summary>
    public sealed class AgendaCard
    {
        public string Id;                      // "<set_slug>__<card_slug>"
        public string Name;
        public string SetId;
        public string Source;                  // printed expansion origin
        public string Type;                    // side_mission|forced_mission|ongoing|kept_secret
        public long Cost;                      // printed Influence purchase cost
        public long TimeLo, TimeHi;            // printed range; HasTimePeriod=false when none printed
        public bool HasTimePeriod;
        public string Text;
        public string Flavor;
        public bool NeedsReview;               // M12a honesty flag (NOT weighted)

        public bool IsKeptSecret { get { return Type == AgendaCards.TypeKeptSecret; } }
    }

    /// <summary>One complete 3-card Agenda set (membership authoritative:
    /// the wiki set pages, RR "Each Agenda set consists of three cards").
    /// The set's printed set label is irrelevant during play: "An Agenda's
    /// set is only used for building the deck at the start of a campaign and
    /// has no other game effect."</summary>
    public sealed class AgendaSet
    {
        public string Id;
        public string Name;
        public string Source;
        public List<string> CardIds = new List<string>();   // agenda1..3 order
    }

    /// <summary>Loaded tacai.agenda_cards/1 table.</summary>
    public sealed class AgendaCardTable
    {
        public List<AgendaSet> Sets = new List<AgendaSet>();
        public List<AgendaCard> Cards = new List<AgendaCard>();
        public Dictionary<string, AgendaCard> ById =
            new Dictionary<string, AgendaCard>(StringComparer.Ordinal);
        public Dictionary<string, AgendaSet> SetById =
            new Dictionary<string, AgendaSet>(StringComparer.Ordinal);

        public AgendaCard Find(string id)
        {
            AgendaCard c;
            return ById.TryGetValue(id ?? "", out c) ? c : null;
        }

        public static AgendaCardTable FromDoc(object doc)
        {
            var table = new AgendaCardTable();
            if (doc == null) return table;
            var root = Json.AsDict(doc);
            if (root.ContainsKey("sets") && root["sets"] != null)
                foreach (var rawSet in Json.AsList(root["sets"]))
                {
                    var ds = Json.AsDict(rawSet);
                    var set = new AgendaSet
                    {
                        Id = Json.AsStr(ds["id"]),
                        Name = Json.AsStr(ds["name"]),
                        Source = ds.ContainsKey("source") && ds["source"] != null
                                 ? Json.AsStr(ds["source"]) : "",
                    };
                    if (ds.ContainsKey("cards") && ds["cards"] != null)
                        foreach (var rawId in Json.AsList(ds["cards"]))
                            set.CardIds.Add(Json.AsStr(rawId));
                    table.Sets.Add(set);
                }
            if (root.ContainsKey("cards") && root["cards"] != null)
                foreach (var rawCard in Json.AsList(root["cards"]))
                {
                    var dc = Json.AsDict(rawCard);
                    var card = new AgendaCard
                    {
                        Id = Json.AsStr(dc["id"]),
                        Name = Json.AsStr(dc["name"]),
                        SetId = dc.ContainsKey("set") && dc["set"] != null
                                ? Json.AsStr(dc["set"]) : "",
                        Source = dc.ContainsKey("source") && dc["source"] != null
                                 ? Json.AsStr(dc["source"]) : "",
                        Type = dc.ContainsKey("type") && dc["type"] != null
                               ? Json.AsStr(dc["type"]) : "?",
                        Cost = dc.ContainsKey("cost") && dc["cost"] != null
                               ? Json.AsLong(dc["cost"]) : 1,
                        Text = dc.ContainsKey("text") && dc["text"] != null
                               ? Json.AsStr(dc["text"]) : "",
                        Flavor = dc.ContainsKey("flavor") && dc["flavor"] != null
                                 ? Json.AsStr(dc["flavor"]) : "",
                        NeedsReview = dc.ContainsKey("needs_review")
                                      && Json.AsBool(dc["needs_review"]),
                    };
                    if (dc.ContainsKey("time_period") && dc["time_period"] != null)
                    {
                        var tp = Json.AsList(dc["time_period"]);
                        if (tp.Count >= 2)
                        {
                            card.HasTimePeriod = true;
                            card.TimeLo = Json.AsLong(tp[0]);
                            card.TimeHi = Json.AsLong(tp[1]);
                        }
                    }
                    table.Cards.Add(card);
                    table.ById[card.Id] = card;
                }
            foreach (var s in table.Sets) table.SetById[s.Id] = s;
            return table;
        }
    }

    /// <summary>Frozen 6-set setup choice + the campaign's Agenda deck order
    /// (RR setup: six sets, shuffled together). The full ranked score tuple
    /// rides along so the Begruendung is auditable exactly like M12b's.</summary>
    public sealed class AgendaDeckChoice
    {
        public string SeedHex;
        public List<AgendaSetScoreRow> Scores = new List<AgendaSetScoreRow>();
        public List<string> ChosenSets = new List<string>();   // pick order
        public List<string> DeckOrder = new List<string>();    // card ids, draw order
        public string Reason;

        public Dictionary<string, object> ToDict()
        {
            var rows = new List<object>();
            foreach (var s in Scores) rows.Add(s.ToDict());
            return new Dictionary<string, object>
            {
                { "seed", SeedHex },
                { "chosen_sets", ChosenSets.Cast<object>().ToList() },
                { "deck", DeckOrder.Cast<object>().ToList() },
                { "scores", rows },
                { "reason", Reason ?? "" },
            };
        }
    }

    /// <summary>One ranked setup row for one set.</summary>
    public sealed class AgendaSetScoreRow
    {
        public string Set;
        public long Synergy;
        public long Noise;
        public long Total;
        public bool Legal;              // time-period filter verdict (see LegalSets)

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                { "set", Set },
                { "synergy", Synergy },
                { "noise", Noise },
                { "total", Total },
                { "legal", Legal },
            };
        }
    }

    /// <summary>Pure setup planner over the primed agenda-card asset. The
    /// Saga store (Saga.AgendaState) freezes the choice; nothing here writes
    /// state.</summary>
    public static class AgendaCards
    {
        public const string TypeSideMission = "side_mission";
        public const string TypeForcedMission = "forced_mission";
        public const string TypeOngoing = "ongoing";
        public const string TypeKeptSecret = "kept_secret";

        public const int SetsPerDeck = 6;      // RR "chooses six sets"

        static object _doc;

        /// <summary>Prime the shipped asset doc (same seam as HeroCards /
        /// ImperialDecks: app loader and harness pin the SAME asset).</summary>
        public static void PrimeDoc(object doc) { _doc = doc; }
        public static object DocOrNull() { return _doc; }

        public static AgendaCardTable Load()
        {
            return _doc == null ? null : AgendaCardTable.FromDoc(_doc);
        }

        /// <summary>Setup seed, salted from the same campaign GUID that
        /// feeds ImperialDecks.DeckSeedFor - distinct salt keeps the agenda
        /// deck order independent from the class-deck choice.</summary>
        public static ulong AgendaSeedFor(string campaignGuid)
        {
            var h = ImperialDecks.Fnv1a64((campaignGuid ?? "") + "|agenda-deck");
            return h == 0 ? 0xA5A5A5A5A5A5A5A5UL : h;
        }

        /// <summary>A set is legal when NO card of it prints a time period
        /// range excluding the campaign's number (RR "cannot choose an
        /// Agenda set if it contains a card with a listed time period range
        /// that does not include the time period of the campaign"; printed-
        /// none = legal everywhere). campaignTimePeriod null = the app does
        /// not model one -> the filter cannot see any number and stays
        /// vacuous (documented M12d2 finding).</summary>
        public static bool SetLegal(AgendaSet set, AgendaCardTable table,
                                    long? campaignTimePeriod)
        {
            if (!campaignTimePeriod.HasValue) return true;
            foreach (var id in set.CardIds)
            {
                var c = table.Find(id);
                if (c == null || !c.HasTimePeriod) continue;
                if (campaignTimePeriod.Value < c.TimeLo || campaignTimePeriod.Value > c.TimeHi)
                    return false;
            }
            return true;
        }

        /// <summary>Set synergy: sum over the set's three cards of the M12c
        /// per-card synergy vocabulary (same families, weights and caps as
        /// ImperialPurchases.CardSynergy - evaluated over the agenda card's
        /// printed text, names excluded, same rule as the deck model).
        /// Type value slots (public knowledge, RR "Types of Agenda Cards"):
        /// kept_secret gets +2 per card scaled by the campaign's agenda
        /// mission count (delayed pressure fits agenda-heavy campaigns);
        /// every other type stays effect-neutral - the model NEVER pretends
        /// to know which cards are mechanically strong beyond printed,
        /// public text.</summary>
        public static long SetSynergy(AgendaSet set, AgendaCardTable table,
                                      ImperialCampaignProfile profile)
        {
            if (profile == null) profile = new ImperialCampaignProfile();
            long s = 0;
            foreach (var id in set.CardIds)
            {
                var c = table.Find(id);
                if (c == null) continue;
                var fams = new Dictionary<string, int>(StringComparer.Ordinal);
                ImperialDecks.AddFamilyCounts(fams, c.Text);
                foreach (var f in ImperialDecks.UnitFamilies)
                    s += 3 * Cap(profile.VillainFamilyCount(f), 4) * Cap(Count(fams, f), 1);
                s += 2 * Cap(profile.HeroCount, 4) * Cap(Count(fams, "HERO"), 1);
                s += 1 * Cap(profile.HeroCount, 4) * Cap(Count(fams, "REBEL"), 1);
                s += 2 * Cap(profile.AgendaMissionCount, 4) * Cap(Count(fams, "AGENDA"), 1);
                s += 1 * Cap(profile.MissionCount, 8) * Cap(Count(fams, "MISSION"), 1);
                if (c.Type == TypeKeptSecret) s += 2 * Cap(profile.AgendaMissionCount, 4);
            }
            return s;
        }

        static int Count(Dictionary<string, int> fams, string f)
        {
            int n;
            return fams.TryGetValue(f, out n) ? n : 0;
        }

        static long Cap(long v, long cap) { return v < 0 ? 0 : (v > cap ? cap : v); }

        /// <summary>Bounded tie jitter 0..3 for one set, salt distinct from
        /// deck choice ("noise|") and purchase ("buy|").</summary>
        public static long NoiseForSet(ulong seed, string setId)
        {
            var rng = new DeterministicRandom(seed ^ ImperialDecks.Fnv1a64("agenda-set|" + setId));
            return rng.RandBelow(4);
        }

        /// <summary>The deterministic setup choice: rank every legal set
        /// (total desc, id asc), take six; the 18 cards (set order = chosen
        /// order, card order = agenda1..3) are then shuffled with Python
        /// OrderDeck shuffle semantics on the same seeded RNG - that list IS
        /// the campaign Agenda deck and is frozen into the store. Illegal
        /// sets appear in the score tuple flagged legal=false but are never
        /// chosen (full audit trail, same as M12b's ranked tuple).</summary>
        public static AgendaDeckChoice ChooseDeck(ImperialCampaignProfile profile,
                                                  ulong seed, long? campaignTimePeriod)
        {
            return ChooseDeck(Load(), profile, seed, campaignTimePeriod);
        }

        public static AgendaDeckChoice ChooseDeck(AgendaCardTable table,
                                                  ImperialCampaignProfile profile,
                                                  ulong seed, long? campaignTimePeriod)
        {
            if (table == null || table.Sets.Count == 0) return null;
            profile = profile ?? new ImperialCampaignProfile();

            var rows = new List<AgendaSetScoreRow>();
            foreach (var set in table.Sets)
            {
                var syn = SetSynergy(set, table, profile);
                var noise = NoiseForSet(seed, set.Id);
                rows.Add(new AgendaSetScoreRow
                {
                    Set = set.Id,
                    Synergy = syn,
                    Noise = noise,
                    Total = syn + noise,
                    Legal = SetLegal(set, table, campaignTimePeriod),
                });
            }
            var chosen = rows.Where(r => r.Legal)
                             .OrderByDescending(r => r.Total)
                             .ThenBy(r => r.Set, StringComparer.Ordinal)
                             .Take(SetsPerDeck)
                             .Select(r => r.Set)
                             .ToList();
            rows = rows.OrderByDescending(r => r.Total)
                       .ThenBy(r => r.Set, StringComparer.Ordinal)
                       .ToList();

            var choice = new AgendaDeckChoice
            {
                SeedHex = ImperialDecks.SeedHex(seed),
                Scores = rows,
                ChosenSets = chosen,
            };
            // stack in chosen-set order (agenda1..3), then ONE shuffle = the
            // RR setup shuffle. Salt names the step; replay-safe.
            foreach (var setId in chosen)
            {
                var set = table.SetById[setId];
                foreach (var cid in set.CardIds) choice.DeckOrder.Add(cid);
            }
            var rng = new DeterministicRandom(seed ^ ImperialDecks.Fnv1a64("agenda-shuffle|setup"));
            Shuffle(choice.DeckOrder, rng);
            var first = rows.First(r => r.Set == chosen[0]);
            choice.Reason = "6 sets: " + string.Join(",", chosen.ToArray())
                + "; top: " + first.Set + " synergy=" + first.Synergy
                + " noise=" + first.Noise + " total=" + first.Total
                + " (seed " + choice.SeedHex + ")";
            return choice;
        }

        /// <summary>Canonical shuffle: for i=n-1..1: j = RandBelow(i+1), swap
        /// (Python random.shuffle semantics, same as OrderDeck.Shuffle).</summary>
        public static void Shuffle(List<string> list, DeterministicRandom rng)
        {
            for (int i = list.Count - 1; i >= 1; i--)
            {
                int j = rng.RandBelow(i + 1);
                var t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        // ------------------------------------------- classification (scope 5)
        // M12d-style table: (a) wired into the model, (b) public information.
        // PRUEFAUFTRAG (2) verdict (see the M12d2 closing comment): the app
        // has NO agenda-shaped trigger for Side/Forced missions - the
        // campaign-list insertion path is UI-only (CampaignManager.
        // AddForcedMission) and the event-system path (CampaignSetNextMission
        // -> SagaCampaign.SetNextStoryMission) only REWRITES the next Story/
        // Finale slot of a live session, which is the wrong RR semantics for
        // agenda missions ("forced missions ... do not have corresponding
        // Mission cards"). So NO agenda card effect is executed by the model;
        // Side/Forced become the public read-aloud announcement, Ongoing the
        // persisted face-up Play-Area row, Kept Secret the hidden reserve.
        // The classification below is the per-card record of that verdict.

        /// <summary>Shipped classification table (126 rows): one row per
        /// card, bucket + hooks. Pure function of the asset - the harness
        /// pins it.</summary>
        public static List<Dictionary<string, object>> ClassificationTable(AgendaCardTable table)
        {
            var outp = new List<Dictionary<string, object>>();
            if (table == null) return outp;
            foreach (var c in table.Cards)
            {
                string bucket, hook;
                switch (c.Type)
                {
                    case TypeOngoing:
                        bucket = "a_store"; hook = "store_persistence+display"; break;
                    case TypeKeptSecret:
                        bucket = "a_store"; hook = "hidden_reserve+anti_leak_view"; break;
                    case TypeSideMission:
                    case TypeForcedMission:
                        bucket = "b_public"; hook = "announce_public_no_engine_effect"; break;
                    default:
                        bucket = "b_public"; hook = "announce_public_no_engine_effect"; break;
                }
                outp.Add(new Dictionary<string, object>
                {
                    { "card", c.Id },
                    { "type", c.Type },
                    { "bucket", bucket },
                    { "hook", hook },
                });
            }
            return outp;
        }
    }
}
