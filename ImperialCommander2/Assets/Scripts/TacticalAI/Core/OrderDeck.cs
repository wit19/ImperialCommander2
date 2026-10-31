// Port of sim/order_deck.py — order-deck mechanics for Profil B.
//
// An order card is a list of action blocks drawn from the fixed vocabulary:
//     move, attack, special, shuffle
//
// FAIRNESS LAW (concept section 2): every generated card must stay within the
// Profil-A action budget - i.e. no card may contain more than one attack block,
// more than one move block or more than one special block (a card demanding
// three attacks is "unfair"). ValidateCard() is the structural gate that the
// C# deck generator reuses (concept 3.1 test point).
//
// Deck mechanics: 8-card deck, draw one card per round, play it, discard; when
// the deck runs out, the discard pile is shuffled back in. A "shuffle" block on
// a played card additionally shuffles the discard pile into the deck after the
// card resolves. All randomness comes from the injected IRandom seam - the
// seeded shuffle replicates Python random.Random(seed).shuffle semantics
// (for i=n-1..1: j = rng.RandBelow(i+1); swap), and the harness replays the
// exported draw streams with ScriptedRandom.
using System;
using System.Collections.Generic;

namespace TacticalAI.Core
{
    public sealed class OrderCard
    {
        public readonly string Id;
        public readonly string[] Blocks;

        public OrderCard(string id, string[] blocks)
        {
            Id = id;
            Blocks = blocks ?? Array.Empty<string>();
        }

        public int CountBlock(string block)
        {
            int n = 0;
            foreach (var b in Blocks) if (b == block) n++;
            return n;
        }

        public bool HasBlock(string block)
        {
            foreach (var b in Blocks) if (b == block) return true;
            return false;
        }

        public Dictionary<string, object> AsDict()
        {
            var blocks = new List<object>();
            foreach (var b in Blocks) blocks.Add(b);
            return new Dictionary<string, object> { { "id", Id }, { "blocks", blocks } };
        }
    }

    public sealed class OrderDeck
    {
        // Rest and interact join the vocabulary with T3 (M4a): they are Profil-A
        // actions (IA ACTIONS p.4), so the fairness law is untouched - the caps
        // below still allow at most ONE action block per kind per card, and the
        // runtime validator (action_builder.validate_plan) remains the final gate.
        public static readonly string[] BlockVocabulary =
            { "move", "attack", "special", "rest", "interact", "shuffle" };

        // --- T3 (M4a) mix builder ---------------------------------------------
        // Building blocks of concept 2 Profil B.2: {Move+Attack} (Vorstoss+Feuer),
        // {Attack} (Feuerstellung), {Move} (Stellung/Reposition), {Rest/Special}.
        public static readonly string[] MixKeys = { "MA", "A", "M", "R", "S" };

        public IRandom Rng;                                   // mirrors self.rng
        public List<OrderCard> DrawPile = new List<OrderCard>();
        public List<OrderCard> Discard = new List<OrderCard>();
        public readonly List<string[]> History = new List<string[]>();  // (card_id, phase) for hashing

        /// Python OrderDeck(cards, seed): validate, then shuffle with the deck's
        /// own seeded RNG. The C# port injects the randomness seam instead
        /// (DeterministicRandom in the app, ScriptedRandom in the harness); the
        /// initial shuffle consumes the stream in Python shuffle semantics.
        public OrderDeck(List<OrderCard> cards, IRandom rng)
        {
            if (cards == null || cards.Count != 8)
                throw new ArgumentException("order deck must have exactly 8 cards, got " +
                                           (cards == null ? 0 : cards.Count));
            foreach (var c in cards)
            {
                var v = ValidateCard(c);
                if (v.Count > 0)
                    throw new ArgumentException("invalid order card: " + ViolationsRepr(v));
            }
            Rng = rng;
            DrawPile = new List<OrderCard>(cards);
            Shuffle(DrawPile);
            Discard = new List<OrderCard>();
        }

        /// Python random.Random.shuffle semantics: for i=n-1..1:
        /// j = rng.RandBelow(i + 1); swap(i, j).
        void Shuffle(List<OrderCard> pile)
        {
            for (int i = pile.Count - 1; i >= 1; i--)
            {
                int j = Rng.RandBelow(i + 1);
                var t = pile[i]; pile[i] = pile[j]; pile[j] = t;
            }
        }

        public OrderCard Draw()
        {
            if (DrawPile.Count == 0)
            {
                if (Discard.Count == 0)
                    throw new InvalidOperationException("deck and discard both empty");
                DrawPile = Discard;
                Discard = new List<OrderCard>();
                Shuffle(DrawPile);
                History.Add(new[] { "__reshuffle__", "auto" });
            }
            var card = DrawPile[0];
            DrawPile.RemoveAt(0);
            History.Add(new[] { card.Id, "draw" });
            return card;
        }

        /// Resolve the card: move to discard; a shuffle block additionally
        /// shuffles all discards back into the deck (after this card is in the
        /// discard pile).
        public void FinishCard(OrderCard card)
        {
            Discard.Add(card);
            History.Add(new[] { card.Id, "finish" });
            if (card.HasBlock("shuffle"))
            {
                DrawPile.AddRange(Discard);
                Discard = new List<OrderCard>();
                Shuffle(DrawPile);
                History.Add(new[] { "__reshuffle__", "block" });
            }
        }

        public Dictionary<string, object> StateDict()
        {
            var draw = new List<object>();
            foreach (var c in DrawPile) draw.Add(c.Id);
            var disc = new List<object>();
            foreach (var c in Discard) disc.Add(c.Id);
            var hist = new List<object>();
            foreach (var h in History) hist.Add(new List<object> { h[0], h[1] });
            return new Dictionary<string, object>
            {
                { "draw", draw },
                { "discard", disc },
                { "history", hist },
            };
        }

        // ------------------------------------------------------------------ gate
        /// Structural fairness gate; returns violations (empty == valid).
        public static List<string> ValidateCard(OrderCard card)
        {
            var v = new List<string>();
            if (card.Blocks.Length == 0)
                v.Add("card " + card.Id + ": empty");
            var unknown = new List<string>();
            foreach (var b in card.Blocks)
            {
                bool known = false;
                foreach (var w in BlockVocabulary) if (w == b) { known = true; break; }
                if (!known) unknown.Add(b);
            }
            if (unknown.Count > 0)
                v.Add("card " + card.Id + ": unknown blocks " + Py.ListRepr(unknown));
            // caps: at most ONE action block per kind per card (Profil A budget)
            foreach (var pair in new[] { new[] { "attack", "1" }, new[] { "move", "1" },
                                         new[] { "special", "1" }, new[] { "rest", "1" },
                                         new[] { "interact", "1" } })
            {
                int n = card.CountBlock(pair[0]);
                if (n > 1)
                    v.Add("card " + card.Id + ": " + n + "x " + pair[0] +
                          " exceeds Profil-A budget (max 1)");
            }
            int actions = card.Blocks.Length - card.CountBlock("shuffle");
            if (actions > 2)
                v.Add("card " + card.Id +
                      ": more than 2 action blocks (Profil-A budget is 2 actions)");
            if (card.CountBlock("shuffle") > 1)
                v.Add("card " + card.Id + ": multiple shuffle blocks");
            return v;
        }

        internal static string ViolationsRepr(List<string> v) { return Py.ListRepr(v); }

        /// Build the 8 OrderCards of one deployment group from a mix spec.
        ///
        /// mix: {"MA": n, "A": n, "M": n, "R": n} (counts sum to 8; "S" may
        /// appear as a pre-expanded {Rest/Special} entry already holding block
        /// text). shuffle_slots: card indices (0-based, 1-2 of them) carrying
        /// the shuffle icon. specialBlock: block text used for the 4th building
        /// block; only "rest" is legal unless the group prints an active
        /// special (caller checks action_builder.specials_for() - the generator
        /// decides, this function is data-shaping only).
        public static List<OrderCard> DeckFromMix(string cardId, Dictionary<string, int> mix,
                                                 IList<int> shuffleSlots,
                                                 string specialBlock = "rest")
        {
            var blocksPerKey = new Dictionary<string, string[]>
            {
                { "MA", new[] { "move", "attack" } },
                { "A", new[] { "attack" } },
                { "M", new[] { "move" } },
                { "R", new[] { specialBlock } },   // {Rest/Special}: "rest" unless specialBlock says otherwise
                { "S", new[] { "special" } },       // only legal when the group prints an active special
            };
            var seq = new List<List<string>>();
            foreach (var key in MixKeys)
            {
                int n = 0;
                if (mix != null) mix.TryGetValue(key, out n);
                if (n < 0)
                    throw new ArgumentException(cardId + ": negative count for " + key);
                for (int i = 0; i < n; i++) seq.Add(new List<string>(blocksPerKey[key]));
            }
            if (seq.Count != 8)
                throw new ArgumentException(cardId + ": mix sums to " + seq.Count +
                                            ", need exactly 8 cards");
            var cards = new List<OrderCard>();
            for (int i = 0; i < seq.Count; i++)
                cards.Add(new OrderCard(cardId + "-" + (i + 1), seq[i].ToArray()));
            foreach (var idx in shuffleSlots)
            {
                if (!(0 <= idx && idx < 8))
                    throw new ArgumentException(cardId + ": shuffle slot " + idx + " out of range");
                var blocks = new List<string>(cards[idx].Blocks) { "shuffle" };
                cards[idx] = new OrderCard(cards[idx].Id, blocks.ToArray());
            }
            foreach (var c in cards)
            {
                var v = ValidateCard(c);
                if (v.Count > 0)
                    throw new ArgumentException(cardId + ": invalid card in mix " +
                                                Py.DictRepr(mix) + ": " + ViolationsRepr(v));
            }
            return cards;
        }

        /// Load a flat card list (JSON text, trailing commas tolerated) and
        /// validate every card - mirrors load_deck_list(path).
        public static List<OrderCard> LoadDeckList(string jsonText)
        {
            var data = Json.AsList(Json.Parse(jsonText));
            var cards = new List<OrderCard>();
            foreach (var e in data)
            {
                var rec = Json.AsDict(e);
                cards.Add(new OrderCard(Json.AsStr(rec["id"]).ToString(),
                                        StrArr(Json.AsList(rec["blocks"]))));
            }
            foreach (var c in cards)
            {
                var v = ValidateCard(c);
                if (v.Count > 0)
                    throw new ArgumentException("deck file invalid: " + ViolationsRepr(v));
            }
            return cards;
        }

        internal static string[] StrArr(List<object> list)
        {
            var outl = new string[list.Count];
            for (int i = 0; i < list.Count; i++) outl[i] = Json.AsStr(list[i]);
            return outl;
        }

        /// M13a labelling decision (pure presentation helper, ZERO planning
        /// effect): how many NUMBERED ACTION blocks a token list carries.
        /// move/attack/special/rest/interact are the action kinds the
        /// activation popup numbers; "shuffle" is an icon note the bridge
        /// renders WITHOUT a number, unknown tokens pass through verbatim
        /// (never numbered) - exactly the RenderBlocks taxonomy. The popup
        /// labels count==1 plans with the unnumbered solo line (ui.json
        /// actionSoloFormat) and count>=2 plans with "Aktion {n}"; the
        /// former tree-lead line (doctrineTreeLead) was REMOVED in M13c
        /// (Thomas 30.10) - the basic attack rule now rides in the merged
        /// info block. The strings themselves
        /// stay in ui.json (localization convention), only this decision is
        /// Core-pure and pinned by the harness action_tree.json fixture +
        /// the live ActionTreeGate distribution check on the shipped asset.
        public static int CountActionBlocks(string[] blocks)
        {
            if (blocks == null) return 0;
            int n = 0;
            foreach (var b in blocks)
                if (b == "move" || b == "attack" || b == "special" ||
                    b == "rest" || b == "interact") n++;
            return n;
        }
    }

    /// Per-mission deck manager mirroring MissionWorld.deal_round_cards /
    /// reap_unused_cards (concept 3.4.1 v3.8 fairness law): EVERY living
    /// group's order card is dealt at ROUND START in canonical order (figure
    /// index) - never at activation time, so the activation order can never
    /// influence deck contents. Deck state persists per group across rounds.
    public sealed class OrderDeckManager
    {
        /// card_id -> the 8 template OrderCards (from order_decks.json).
        public readonly Dictionary<string, List<OrderCard>> DecksByCard =
            new Dictionary<string, List<OrderCard>>();
        /// group_id -> the live draw/discard engine (persists across rounds).
        public readonly Dictionary<string, OrderDeck> OrderDecks =
            new Dictionary<string, OrderDeck>();
        /// group_id -> the card dealt this round, not yet resolved.
        public readonly Dictionary<string, OrderCard> RoundCards =
            new Dictionary<string, OrderCard>();

        /// Seed -> RNG seam. The app leaves this null (DeterministicRandom);
        /// the harness may inject ScriptedRandom streams per group.
        public Func<int, int, IRandom> RngFactory;   // (deckSeed, figureIndex)

        /// Load tacai.order_decks/1 root: {decks: {card_id: {cards: [{id,
        /// blocks}, ... 8], ...}}}. Validates EVERY deck card through
        /// ValidateCard before accepting anything.
        public static OrderDeckManager Load(object orderDecksJsonRoot)
        {
            var root = Json.AsDict(orderDecksJsonRoot);
            var decks = Json.AsDict(Json.Get(root, "decks", new Dictionary<string, object>()));
            var mgr = new OrderDeckManager();
            foreach (var kv in decks)
            {
                var entry = Json.AsDict(kv.Value);
                var cards = new List<OrderCard>();
                foreach (var c in Json.AsList(entry["cards"]))
                {
                    var rec = Json.AsDict(c);
                    cards.Add(new OrderCard(Json.AsStr(rec["id"]),
                                            OrderDeck.StrArr(Json.AsList(rec["blocks"]))));
                }
                foreach (var c in cards)
                {
                    var v = OrderDeck.ValidateCard(c);
                    if (v.Count > 0)
                        throw new ArgumentException("order deck " + kv.Key +
                                                    " invalid: " + OrderDeck.ViolationsRepr(v));
                }
                mgr.DecksByCard[kv.Key] = cards;
            }
            return mgr;
        }

        /// Template deck for a card id (Python self.decks_by_card[card_id]).
        public List<OrderCard> Deck(string cardId)
        {
            if (!DecksByCard.TryGetValue(cardId, out var d))
                throw new KeyNotFoundException("no order deck for card " + cardId);
            return d;
        }

        IRandom RngFor(int deckSeed, int index)
        {
            if (RngFactory != null) return RngFactory(deckSeed, index);
            return new DeterministicRandom((ulong)(deckSeed + 131 * index));
        }

        /// Deal exactly one card per living group at round start (canonical
        /// index order; each group dealt at most once per round). Returns the
        /// dealt cards in deal order.
        public List<OrderCard> DrawRoundAll(int roundNo, IReadOnlyList<Figure> livingImperialGroups,
                                           int deckSeed)
        {
            var dealt = new List<OrderCard>();
            foreach (var fig in livingImperialGroups)
            {
                var gid = fig.GroupId;
                if (RoundCards.ContainsKey(gid)) continue;
                OrderDeck deck;
                if (!OrderDecks.TryGetValue(gid, out deck))
                {
                    deck = new OrderDeck(Deck(fig.Card.Id), RngFor(deckSeed, fig.Index));
                    OrderDecks[gid] = deck;
                }
                var card = deck.Draw();
                RoundCards[gid] = card;
                dealt.Add(card);
            }
            return dealt;
        }

        /// Activation resolves the dealt card: draw/finish + shuffle-block law.
        public void FinishCard(string groupId)
        {
            if (!RoundCards.TryGetValue(groupId, out var card)) return;
            RoundCards.Remove(groupId);
            OrderDecks[groupId].FinishCard(card);
        }

        /// Cards of groups that fell before activation move to the discard
        /// UNPLAYED: their shuffle block does not resolve (an unplayed card
        /// cannot trigger a reshuffle). Documented M2a.3 delta to T3.
        public void ReapUnused(int roundNo)
        {
            var gids = new List<string>(RoundCards.Keys);
            gids.Sort(StringComparer.Ordinal);
            foreach (var gid in gids)
            {
                var card = RoundCards[gid];
                if (!OrderDecks.TryGetValue(gid, out var deck)) continue;
                deck.Discard.Add(card);
                deck.History.Add(new[] { card.Id, "unused" });
            }
            RoundCards.Clear();
        }
    }
}
