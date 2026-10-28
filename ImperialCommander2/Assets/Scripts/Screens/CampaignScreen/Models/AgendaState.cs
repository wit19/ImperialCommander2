using System;
using System.Collections.Generic;

namespace Saga
{
	// TACTICAL AI M12d2: the Agenda deck in the campaign store (Rules
	// Reference "Agenda Cards" / "Building the Agenda Deck" / "Influence" /
	// "Imperial Upgrade Stage"). One frozen setup choice (6 sets + deck
	// order), one append-only influence account (RR: "Influence is tracked
	// on the campaign log ... gains and spends both recorded ... unspent
	// influence may be spent during future Imperial Upgrade Stages"), and
	// one audit row per upgrade-stage market + per booked purchase.
	//
	// The RR's campaign-log ledger is exactly the M12c XP ledger shape that
	// this file family already ships (ImperialDeckState.xpLog): append-only,
	// seq-numbered, no timestamps - so a replay cannot drift and a human can
	// audit every balanceAfter. The market rows persist the face-down draw
	// ("without being revealed" is a law AGAINST THE REBELS, not against the
	// store - an audit trail that never reaches rebel-facing UIs).
	//
	// KEPT SECRET LEAK LAW (controller canon 28.10.): secret IS mechanics,
	// a LEAK is a bug. A Kept Secret purchase keeps cardId/name/text OUT of
	// every rebel-facing view (RebelView() below, the bridge export, any
	// future log/UI) until RevealKeptSecret() - which only the card's own
	// printed trigger (or the human imperial resolving it) may call. The
	// public fact that ANOTHER agenda was bought - with its printed cost -
	// stays visible: influence spend is public campaign-log data (RR).

	[System.Serializable]
	public class AgendaInfluenceEntry
	{
		public int seq;
		public int delta;            // + gain / - spend
		public string reason;        // machine key, stored verbatim
		public int balanceAfter;
	}

	[System.Serializable]
	public class AgendaPurchaseEntry
	{
		public int seq;
		public int stage;            // 0-based upgrade stage the buy ran in
		public string cardId;        // "" in the rebel view of a hidden buy
		public string type;          // RR type (public knowledge for ALL buys)
		public int cost;             // influence spent (public campaign-log data)
		public bool revealed;        // kept_secret flips to true at resolution
		public string reason;
	}

	[System.Serializable]
	public class AgendaMarketEntry
	{
		public int stage;
		public List<string> market = new List<string>();        // draw order
		public List<string> bought = new List<string>();
		public List<string> notPurchased = new List<string>();  // shuffled back UNrevealed
		public string seedHex;       // re-derivation handle for the audit
	}

	[System.Serializable]
	public class AgendaOngoingEntry
	{
		public int seq;
		public string cardId;
		public bool active;          // RR: stays in play until discarded by an effect
	}

	[System.Serializable]
	public class AgendaSetScoreEntry
	{
		public string set;
		public long synergy;
		public long noise;
		public long total;
		public bool legal;      // time-period filter verdict (RR setup law)
	}

	[System.Serializable]
	public class AgendaState
	{
		public string seedKey;                 // campaign GUID it derives from
		public string seedHex;                 // 16-hex re-derivation handle
		public string reason;                  // model text (top row, not UI)
		public List<string> chosenSets = new List<string>();   // setup pick order
		public List<AgendaSetScoreEntry> setScores
			= new List<AgendaSetScoreEntry>();
		public List<string> deck = new List<string>();         // card ids, draw order
		public int stagesRun;                  // markets already played (stage pointer)
		public int influenceBalance;           // Kontostand Influence (campaign log)
		public List<AgendaInfluenceEntry> influenceLog = new List<AgendaInfluenceEntry>();
		public List<AgendaMarketEntry> markets = new List<AgendaMarketEntry>();
		public List<AgendaPurchaseEntry> purchases = new List<AgendaPurchaseEntry>();
		public List<AgendaOngoingEntry> ongoing = new List<AgendaOngoingEntry>();

		/// <summary>Freeze one Core setup choice into the store (RR setup:
		/// six sets, shuffled = the campaign Agenda deck). Influence starts
		/// at 0 like the M12c XP account - income arrives through CM7
		/// bookings (mission rewards are mission CONTENT, not a rule this
		/// module may invent).</summary>
		public static AgendaState FromChoice( TacticalAI.Core.AgendaDeckChoice choice,
			string seedKey )
		{
			if ( choice == null ) return null;
			var state = new AgendaState
			{
				seedKey = seedKey,
				seedHex = choice.SeedHex,
				reason = choice.Reason,
			};
			state.chosenSets.AddRange( choice.ChosenSets );
			state.deck.AddRange( choice.DeckOrder );
			foreach ( var s in choice.Scores )
				state.setScores.Add( new AgendaSetScoreEntry
				{
					set = s.Set, synergy = s.Synergy, noise = s.Noise,
					total = s.Total, legal = s.Legal,
				} );
			return state;
		}

		/// <summary>Book influence (mission rewards in, agenda spends out as
		/// negative delta). Append-only, auditable - same law as the XP log.</summary>
		public void AddInfluence( int delta, string reason )
		{
			influenceBalance += delta;
			influenceLog.Add( new AgendaInfluenceEntry
			{
				seq = influenceLog.Count, delta = delta, reason = reason,
				balanceAfter = influenceBalance,
			} );
		}

		/// <summary>Buy one agenda card: succeeds only with enough balance.
		/// Books the spend, appends the purchase row, routes the card per RR
		/// type (ongoing -> Play Area row; kept_secret -> hidden reserve;
		/// side/forced -> announced and left to the table, discarded for the
		/// campaign after execution), and REMOVES it from the draw deck (no
		/// bought card ever rides in the pile again). One step, one append -
		/// the single booking path, like TryBuyDeckCard.</summary>
		public bool TryBuyAgenda( string cardId, string type, int cost, int stage, string reason )
		{
			if ( string.IsNullOrEmpty( cardId ) || cost < 0 || influenceBalance < cost ) return false;
			if ( deck.IndexOf( cardId ) < 0 ) return false;   // not drawn/present
			AddInfluence( -cost, "buy:" + cardId );
			purchases.Add( new AgendaPurchaseEntry
			{
				seq = purchases.Count, stage = stage, cardId = cardId,
				type = type ?? "", cost = cost,
				revealed = type != TacticalAI.Core.AgendaCards.TypeKeptSecret,
				reason = reason ?? "",
			} );
			if ( type == TacticalAI.Core.AgendaCards.TypeOngoing )
				ongoing.Add( new AgendaOngoingEntry { seq = ongoing.Count, cardId = cardId, active = true } );
			deck.Remove( cardId );
			return true;
		}

		/// <summary>One stage's audit row (frozen market incl. the returned
		/// cards). Deck pointer bookkeeping stays in the caller (SagaCampaign
		/// owns the deck mutation via TryBuyAgenda + ReplaceDeckAfterStage).</summary>
		public void AddMarketAudit( int stage, List<string> market, List<string> bought,
			List<string> notPurchased, string seedHex )
		{
			var m = new AgendaMarketEntry { stage = stage, seedHex = seedHex };
			if ( market != null ) m.market.AddRange( market );
			if ( bought != null ) m.bought.AddRange( bought );
			if ( notPurchased != null ) m.notPurchased.AddRange( notPurchased );
			markets.Add( m );
			if ( stage + 1 > stagesRun ) stagesRun = stage + 1;
		}

		/// <summary>Frozen post-stage deck list (remainder + returned, the
		/// Core plan's DeckAfter). RR shuffles again next stage, so this is
		/// replay order, and it must reproduce bit-for-bit.</summary>
		public void ReplaceDeckAfterStage( List<string> deckAfter )
		{
			deck.Clear();
			if ( deckAfter != null ) deck.AddRange( deckAfter );
		}

		/// <summary>RR: an Ongoing card "remains in play until discarded by
		/// another effect" - the ONLY way it leaves the Play Area. Also the
		/// exit for a resolved Kept Secret (it leaves with its effect).</summary>
		public bool DiscardAgenda( string cardId, string reason )
		{
			foreach ( var o in ongoing )
				if ( o.cardId == cardId && o.active ) { o.active = false; return true; }
			foreach ( var k in purchases )
				if ( k.cardId == cardId && k.type == TacticalAI.Core.AgendaCards.TypeKeptSecret
					&& k.revealed ) return true;
			return false;
		}

		/// <summary>A Kept Secret resolves exactly when the printed card
		/// demands it ("He resolves the effect at a later point in the
		/// campaign as instructed on the card") - from that moment its
		/// content is public (it is read aloud / executed on the table).
		/// Callers: the card's own trigger or the human imperial; NEVER a
		/// UI peek.</summary>
		public bool RevealKeptSecret( string cardId )
		{
			foreach ( var k in purchases )
				if ( k.cardId == cardId && k.type == TacticalAI.Core.AgendaCards.TypeKeptSecret
					&& !k.revealed ) { k.revealed = true; return true; }
			return false;
		}

		/// <summary>Play-Area view (RR: Ongoing lies faceup in the Imperial
		/// play area - public knowledge; M12e display seam).</summary>
		public List<string> ActiveOngoing()
		{
			var outl = new List<string>();
			foreach ( var o in ongoing )
				if ( o.active ) outl.Add( o.cardId );
			return outl;
		}

		/// <summary>REBEL-FACING export (anti-leak law, own harness fixture):
		/// every purchase shows its RR type and printed cost (public
		/// campaign-log facts); an unrevealed Kept Secret is a content-free
		/// "secret" row - id, name and text NEVER appear here. Revealed
		/// secrets and all other types export normally.</summary>
		public List<Dictionary<string, object>> RebelView()
		{
			var outl = new List<Dictionary<string, object>>();
			foreach ( var p in purchases )
			{
				bool hidden = p.type == TacticalAI.Core.AgendaCards.TypeKeptSecret && !p.revealed;
				var row = new Dictionary<string, object>
				{
					{ "seq", p.seq },
					{ "stage", p.stage },
					{ "type", p.type },
					{ "cost", p.cost },
					{ "card", hidden ? "" : ( p.cardId ?? "" ) },
					{ "revealed", hidden ? false : true },
				};
				outl.Add( row );
			}
			foreach ( var id in ActiveOngoing() )
				outl.Add( new Dictionary<string, object>
				{
					{ "ongoing", id },
				} );
			return outl;
		}

		/// <summary>Imperial-side audit view (store/UI for the human
		/// imperial + M12e setup proposals, scope 6): full rows, incl. the
		/// still-hidden Kept Secrets and the market history. Feeding this
		/// into any rebel-facing surface is the leak the fixture forbids.</summary>
		public List<Dictionary<string, object>> ImperialAuditView()
		{
			var outl = new List<Dictionary<string, object>>();
			foreach ( var p in purchases )
				outl.Add( new Dictionary<string, object>
				{
					{ "seq", p.seq }, { "stage", p.stage }, { "card", p.cardId },
					{ "type", p.type }, { "cost", p.cost }, { "revealed", p.revealed },
				} );
			return outl;
		}
	}
}
