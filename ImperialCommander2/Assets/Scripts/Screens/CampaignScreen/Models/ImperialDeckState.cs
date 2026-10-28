using System;
using System.Collections.Generic;

namespace Saga
{
	// TACTICAL AI M12b: the campaign store's Imperial class deck (FFG Rules
	// Reference, campaign setup step 3 "Choose Imperial Class"). Serialized
	// as a plain Newtonsoft object inside SagaCampaign - ADDITIVE only: the
	// campaign formatVersion STAYS 2, older saves simply load this field as
	// null, and null is the model's VALID "no deck" state (non-tactical
	// campaigns never store AI-owned deck data - the UI may only preview the
	// pure choice). Deck choice itself is a pure function of the campaign
	// seed (GUID) + profile: see TacticalAI.Core.ImperialDecks; the choice
	// here is the frozen result incl. the full score tuple (Begruendung).
	// Determinism rule: no UnityEngine.Random, no timestamps - every field is
	// derived from seed + profile or from explicit campaign-booked XP.

	/// <summary>One acquired deck card, import-key id + acquisition order
	/// (basic cards get the leading orders at deck choice time).</summary>
	public class ImperialDeckCardEntry
	{
		public string id;
		public int order;
	}

	/// <summary>One Imperial XP booking (Kontofuehrung zwischen Missionen in
	/// der Imperial Upgrade Stage). seq = append index, balanceAfter =
	/// Kontostand nach dieser Buchung.</summary>
	public class ImperialXpEntry
	{
		public int seq;
		public int delta;
		public string reason;
		public int balanceAfter;
	}

	/// <summary>Persisted score row = Begruendung of the deterministic choice
	/// (mirror of TacticalAI.Core.ImperialDeckScoreRow).</summary>
	public class ImperialDeckScoreEntry
	{
		public string deck;
		public long synergy;
		public long noise;
		public long total;
	}

	/// <summary>One live Attachment placement: card -> the deploy card of the
	/// group it rides on (Rules Reference "Attachment": attached AT
	/// DEPLOYMENT, acts on ALL figures of the group). Returned to the play
	/// area (active=false) when the LAST figure of the group is defeated -
	/// the attachment is NOT consumed and may attach again at FUTURE
	/// deployments.</summary>
	public class ImperialAttachmentEntry
	{
		public int seq;
		public string cardId;
		public string groupCardId;
		public bool active;
	}

	public class ImperialDeckState
	{
		public string deckId;                 // chosen class deck (slug)
		public string seedKey;                // campaign GUID the seed derives from
		public string seedHex;                // 16-hex re-derivation handle
		public string reason;                 // human-readable top row (model text)
		public List<ImperialDeckCardEntry> cards = new List<ImperialDeckCardEntry>();
		public List<ImperialDeckScoreEntry> scores = new List<ImperialDeckScoreEntry>();
		public int xpBalance;                 // Kontostand imperiale XP
		public List<ImperialXpEntry> xpLog = new List<ImperialXpEntry>();
		// M12d: attachment persistence (RR lifecycle lives in the STORE,
		// not in the planner). Append-only; a return flips active to false,
		// a re-attach appends a NEW placement row (history stays auditable,
		// determinism: no timestamps).
		public List<ImperialAttachmentEntry> attachments = new List<ImperialAttachmentEntry>();

		/// <summary>Freeze one Core choice into the store: deck + full ranked
		/// score tuple; the chosen deck's BASIC cards (0 XP) are acquired
		/// immediately (setup step 3), paid cards arrive via bookings.</summary>
		public static ImperialDeckState FromChoice( TacticalAI.Core.ImperialDeckChoice choice,
			string seedKey )
		{
			if ( choice == null ) return null;
			var state = new ImperialDeckState
			{
				deckId = choice.DeckId,
				seedKey = seedKey,
				seedHex = choice.SeedHex,
				reason = choice.Reason,
			};
			foreach ( var s in choice.Scores )
				state.scores.Add( new ImperialDeckScoreEntry
				{
					deck = s.Deck, synergy = s.Synergy, noise = s.Noise, total = s.Total,
				} );
			var table = TacticalAI.Core.ImperialDecks.Load();
			var deck = table != null ? table.Find( choice.DeckId ) : null;
			if ( deck != null )
				foreach ( var basic in deck.BasicCards )
					state.cards.Add( new ImperialDeckCardEntry
					{
						id = basic.Id, order = state.cards.Count,
					} );
			return state;
		}

		/// <summary>Book Imperial XP (mission rewards in, purchases out as
		/// negative delta). Bookings are append-only - the log is auditable.</summary>
		public void AddImperialXp( int delta, string reason )
		{
			xpBalance += delta;
			xpLog.Add( new ImperialXpEntry
			{
				seq = xpLog.Count, delta = delta, reason = reason, balanceAfter = xpBalance,
			} );
		}

		/// <summary>Buy one card with Imperial XP: succeeds only with enough
		/// balance; acquires the card and books the spend in ONE step.</summary>
		public bool TryBuyDeckCard( string cardId, int xpCost, string reason )
		{
			if ( string.IsNullOrEmpty( cardId ) || xpCost < 0 || xpBalance < xpCost ) return false;
			foreach ( var c in cards )
				if ( c.id == cardId ) return false;   // already acquired
			AddImperialXp( -xpCost, reason );
			cards.Add( new ImperialDeckCardEntry { id = cardId, order = cards.Count } );
			return true;
		}

		// ------------------------------------------------ M12d attachments
		// RR "Attachment" lifecycle, verbatim law: attach AT DEPLOYMENT onto
		// the deploy card of the group; the effect acts on ALL figures of
		// the group; when the LAST figure of the group is defeated the
		// attachment returns to the Play Area and can be attached again at
		// FUTURE deployments (not consumed). Trait restrictions ("Trooper"
		// etc.) are HARD: a group without the trait cannot receive it.

		/// <summary>Is this acquired card an Attachment print? (table row
		/// carries the attach_lifecycle hook; the deck table supplies the
		/// printed restriction line).</summary>
		public bool IsAttachment( string cardId )
		{
			var table = TacticalAI.Core.ImperialCardEffects.Load();
			if ( table == null || string.IsNullOrEmpty( cardId ) ) return false;
			TacticalAI.Core.CardEffectEntry e;
			return table.ById.TryGetValue( cardId, out e )
				&& e.Hooks.Exists( h => h.Key == "attach_lifecycle" );
		}

		/// <summary>Attach at deployment: card must be acquired, be an
		/// Attachment print, not currently attached, and pass the HARD trait
		/// restriction against the group's printed traits (null = the model
		/// cannot see the group's traits -> refuse: restrictions are hard).
		/// Returns false without touching state when any condition fails.</summary>
		public bool Attach( string cardId, string groupCardId, string groupTraits )
		{
			if ( string.IsNullOrEmpty( cardId ) || string.IsNullOrEmpty( groupCardId ) ) return false;
			bool acquired = false;
			foreach ( var c in cards )
				if ( c.id == cardId ) { acquired = true; break; }
			if ( !acquired || !IsAttachment( cardId ) ) return false;
			foreach ( var a in attachments )
				if ( a.cardId == cardId && a.active ) return false;   // already on a group
			var deckTable = TacticalAI.Core.ImperialDecks.Load();
			var deck = deckTable != null ? deckTable.Find( deckId ) : null;
			string restriction = null;
			if ( deck != null )
				foreach ( var c in deck.Cards )
					if ( c.Id == cardId ) { restriction = c.Restriction; break; }
			if ( !TacticalAI.Core.ImperialCardEffects.AttachLegal( restriction, groupTraits ) )
				return false;   // hard trait restriction
			attachments.Add( new ImperialAttachmentEntry
			{
				seq = attachments.Count, cardId = cardId, groupCardId = groupCardId, active = true,
			} );
			return true;
		}

		/// <summary>The LAST figure of the group was defeated: every active
		/// attachment of that deploy card returns to the Play Area (stays
		/// acquired, re-attachable at future deployments). Returns how many
		/// attachments returned.</summary>
		public int ReturnAttachments( string groupCardId )
		{
			int n = 0;
			foreach ( var a in attachments )
				if ( a.active && a.groupCardId == groupCardId ) { a.active = false; n++; }
			return n;
		}

		/// <summary>Play-Area view for UI/M12e: acquired Attachment prints
		/// currently unattached (re-attachable).</summary>
		public List<string> AvailableAttachments()
		{
			var outl = new List<string>();
			foreach ( var c in cards )
			{
				if ( !IsAttachment( c.id ) ) continue;
				bool on = false;
				foreach ( var a in attachments )
					if ( a.cardId == c.id && a.active ) { on = true; break; }
				if ( !on ) outl.Add( c.id );
			}
			return outl;
		}
	}
}
