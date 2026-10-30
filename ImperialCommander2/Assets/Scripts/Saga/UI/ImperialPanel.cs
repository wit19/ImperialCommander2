// M12e (Imperium-UI): persistent play-area panel that mirrors the Imperial
// side of a campaign for the rebel players at the shared screen. It shows the
// class deck the tactical AI (or the human Imperiator) chose at campaign
// setup, the FREE basic card that lies face-up from that moment on (Rules
// Reference, setup step 3: "kept faceup ... for the remainder of the
// campaign"), and every card bought in an Imperial Upgrade Stage with its XP
// price. Each row's tooltip is the 1:1 printed card text from the shipped
// deck asset (token notation as in game, glyph-mapped via Tooltip.ShowText).
// Cards the M12d table classifies as engine-detail (bucket c) are shown
// honestly as "active - no AI lever": the game does not hide rules the model
// does not simulate.
//
// Built at runtime on top of the existing HUD (M11f precedent: no prefab or
// scene edits, everything derived from wired siblings). It paints UNDER all
// HUD siblings (first-sibling rule), so opened windows cover it by itself;
// only the settings dialog, outside that sibling stack, hides it explicitly.
// Read-only: no buttons here do anything, so the row Graphics set
// raycastTarget=false - the board underneath stays clickable; the tooltip
// handlers ride whatever pointer events still reach the rows.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TacticalAI;
using TacticalAI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Saga
{
	/// <summary>M12e: shared, fail-safe access to the campaign-side Imperial
	/// display data (deck table, deck state, open agenda) for the play-area
	/// panel, the Einsatzkarte deck line and the campaign screen section.
	/// NEVER throws: a display layer may break cosmetics, never a game
	/// scene (fail-open doctrine from TacticalAIBridge).</summary>
	public static class ImperialDisplay
	{
		public sealed class Row
		{
			public string CardId;
			public string Name;
			public bool Basic;
			public int Xp;                       // printed price (0 for basic)
			public bool Modelled;                // false = bucket (c): "no AI lever"
			public bool Attachment;              // printed Attachment card
		}

		/// <summary>Panel/badge data for the current campaign state, or null
		/// when there is nothing to show (feature off, no campaign, no deck).
		/// CardCount = 1 basic + acquired (deduped against the basic).</summary>
		public sealed class Info
		{
			public string DeckName;
			public int CardCount;
			public int XpBalance;
			public string OpenAgenda;            // null unless an OPEN agenda is active
			public readonly List<Row> Rows = new List<Row>();   // basic first, then purchase order
		}

		public static bool FeatureActive( SagaCampaign campaign )
		{
			try
			{
				return TacticalAISettings.Enabled && campaign != null && campaign.imperialDeck != null
					&& TacticalAIAssets.ImperialDecksRoot != null;
			}
			catch { return false; }
		}

		public static Info Build( SagaCampaign campaign )
		{
			try
			{
				if ( !FeatureActive( campaign ) ) return null;
				var decks = ImperialDecks.Load();
				var deck = decks != null ? decks.Find( campaign.imperialDeck.deckId ) : null;
				if ( deck == null ) return null;

				var effects = ImperialCardEffects.Load();
				var info = new Info
				{
					DeckName = deck.Name,
					XpBalance = campaign.imperialDeck.xpBalance,
					OpenAgenda = OpenAgendaLine( campaign ),
				};

				// store order = basic cards first (FromChoice), then purchase
				// order - exactly the acquisition timeline of the play area.
				var entries = campaign.imperialDeck.cards ?? new List<ImperialDeckCardEntry>();
				foreach ( var entry in entries.OrderBy( e => e.order ) )
				{
					var c = deck.Cards.FirstOrDefault( x => x.Id == entry.id );
					if ( c == null ) continue;   // unknown id: honest gap, never a phantom row
					info.Rows.Add( MakeRow( c, effects, c.Basic ? 0 : (int)( c.Xp ?? 0 ), c.Basic ) );
				}
				info.CardCount = info.Rows.Count;
				return info;
			}
			catch ( Exception e )
			{
				Debug.LogWarning( "ImperialDisplay::Build failed -> panel hidden: " + e.Message );
				return null;
			}
		}

		static Row MakeRow( ImperialClassCard c, CardEffectTable effects, int xp, bool basic )
		{
			bool modelled = true;
			if ( effects != null && effects.ById.TryGetValue( c.Id, out var e ) ) modelled = e.Modelled;
			bool attach = ( c.Restriction ?? "" ).IndexOf( "ttachment", StringComparison.OrdinalIgnoreCase ) >= 0;
			return new Row { CardId = c.Id, Name = c.Name, Basic = basic, Xp = xp, Modelled = modelled, Attachment = attach };
		}

		/// <summary>Anti-leak seam: only ACTIVE ONGOING agendas lie face-up in
		/// the play area (AgendaState.ActiveOngoing - the designated M12e
		/// display seam). Kept Secrets never appear here; their purchase rows
		/// are content-free until the card itself reveals them.</summary>
		static string OpenAgendaLine( SagaCampaign campaign )
		{
			try
			{
				var ag = campaign.agenda;
				if ( ag == null ) return null;
				var active = ag.ActiveOngoing();
				if ( active == null || active.Count == 0 ) return null;
				var table = AgendaCards.Load();
				var names = active
					.Select( id => table != null && table.Find( id ) != null ? table.Find( id ).Name : null )
					.Where( n => !string.IsNullOrEmpty( n ) )
					.ToArray();
				if ( names.Length == 0 ) return null;
				return TacticalAISettingsUnity.AgendaOpenLineText( "ongoing",
					string.Join( " \u00b7 ", names ) );
			}
			catch { return null; }
		}

		/// <summary>The 1:1 printed card text for a tooltip: keyword/restriction
		/// line in italics, then the ability blocks, token notation untouched
		/// (Tooltip.ShowText glyph-maps like every other in-game text).</summary>
		public static string CardTooltip( ImperialClassCard c )
		{
			if ( c == null ) return "";
			var sb = new StringBuilder();
			sb.Append( c.Name );
			var restr = new List<string>();
			if ( !string.IsNullOrEmpty( c.Keyword ) ) restr.Add( c.Keyword );
			if ( !string.IsNullOrEmpty( c.Restriction )
				&& ( string.IsNullOrEmpty( c.Keyword ) || !c.Restriction.Contains( c.Keyword ) ) )
				restr.Add( c.Restriction );
			if ( restr.Count > 0 ) sb.Append( "\n<i>" ).Append( string.Join( " \u00b7 ", restr.ToArray() ) ).Append( "</i>" );
			foreach ( var t in c.AbilityTexts )
				if ( !string.IsNullOrWhiteSpace( t ) ) sb.Append( "\n" ).Append( t.Trim() );
			return sb.ToString();
		}

		public static ImperialClassCard FindCard( string cardId )
		{
			try
			{
				var decks = ImperialDecks.Load();
				if ( decks == null ) return null;
				foreach ( var d in decks.Decks )
					foreach ( var c in d.Cards )
						if ( c.Id == cardId ) return c;
				return null;
			}
			catch { return null; }
		}
	}

	/// <summary>Hover helper for dynamically built rows: shows free tooltip
	/// text (card print) without touching the fixed-code Tooltip.Show path.
	/// Needs a Graphic on the same GameObject for raycasts (dev 23 lesson) -
	/// the owner configures it.</summary>
	public class ImperialTooltipHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		public string Text;

		// M13d (EMPIRE12 Player.log): GlowEngine.FindUnityObject<T> ends in
		// `return results[0]` (GlowEngine.cs:595) and THROWS
		// ArgumentOutOfRangeException on the empty scene list during a scene
		// teardown - the null-check below it can never fire. OnDisable runs
		// exactly then, so it must work off the hover-cached reference only
		// (Unity's == null also reports destroyed objects as null).
		Tooltip cachedTooltip;

		public void OnPointerEnter( PointerEventData eventData )
		{
			cachedTooltip = GlowEngine.FindUnityObject<Tooltip>();
			if ( cachedTooltip != null && !string.IsNullOrEmpty( Text ) ) cachedTooltip.ShowText( Text );
		}

		public void OnPointerExit( PointerEventData eventData )
		{
			if ( cachedTooltip != null ) cachedTooltip.Hide();
		}

		void OnDisable()
		{
			// safety net: hidden rows must never leave the tooltip stranded -
			// cached reference ONLY, never FindUnityObject during teardown
			if ( cachedTooltip != null ) cachedTooltip.Hide();
		}
	}

	/// <summary>The panel itself. Lives on a runtime GameObject under the
	/// mission Canvas; refreshes on a slow poll because the deck state is
	/// written outside the UI (upgrade stage, attachment lifecycle).</summary>
	public class ImperialPanel : MonoBehaviour
	{
		const float REFRESH_SECONDS = 0.5f;
		const float ROW_WIDTH = 320f;
		// M13r (Thomas: "da steht kein Imperium Text"): the briefing text
		// box covers the bottom-left corner above y~240 (verified pixel-wise
		// on his mission screenshot - the icon-strip band y=0..230 is free).
		// Capping the panel inside that band makes the header+rows readable
		// WHILE the briefing is open; overflow beyond the cap is masked.
		const float MAX_HEIGHT = 200f;

		GameObject root, contentRoot;
		bool geometryLogged;
		TextMeshProUGUI headerText;
		SagaCampaign campaign;
		ImperialDisplay.Info info;
		string lastSignature = "";
		float nextRefresh;

		void Start()
		{
			try
			{
				if ( !TacticalAISettings.Enabled ) return;
				if ( DataStore.gameType != GameType.Saga ) return;
				if ( RunningCampaign.sagaCampaignGUID == Guid.Empty ) return;

				ImperialDisplay.Build( RunningCampaign.sagaCampaign );   // cheap no-op probe
				campaign = RunningCampaign.sagaCampaign
					?? SagaCampaign.LoadCampaignState( RunningCampaign.sagaCampaignGUID );
				if ( !ImperialDisplay.FeatureActive( campaign ) ) return;

				BuildPanel();
				Debug.Log( "ImperialPanel::installed (campaign " + RunningCampaign.sagaCampaignGUID + ")" );
			}
			catch ( Exception e )
			{
				Debug.LogWarning( "ImperialPanel::Start failed -> no panel: " + e.Message );
			}
		}

		void BuildPanel()
		{
			var tooltip = GlowEngine.FindUnityObject<Tooltip>();
			var canvas = tooltip != null ? tooltip.transform.parent : GameObject.Find( "Canvas" )?.transform;
			if ( canvas == null ) return;

			root = new GameObject( "tacaiImperialPanel", typeof( RectTransform ) );
			root.transform.SetParent( canvas, false );
			var rt = root.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2( 0f, 0f );       // bottom-left corner, RIGHT of the HUD icon row
			rt.anchorMax = new Vector2( 0f, 0f );
			rt.pivot = new Vector2( 0f, 0f );
			// The panel only ever exists in tactical campaigns, where the HUD's
			// bottom-left icon row (rules / medpac-or-"?" / settings) would bury
			// it (Thomas playtest 29.10.: class-card and agenda rows were
			// completely covered). M13p: the measurement runs through
			// LeftEdgeClear() and is REPEATED on every refresh, so a not-yet
			// measurable slot at Start self-heals within 0.5 s.
			rt.anchoredPosition = new Vector2( LeftEdgeClear(), 12f );
			rt.sizeDelta = new Vector2( ROW_WIDTH, 60f );

			var bg = root.AddComponent<Image>();
			bg.color = new Color( 0.02f, 0.10f, 0.09f, 0.88f );
			bg.raycastTarget = false;
			// M13r: with the height capped at MAX_HEIGHT a long deck must not
			// paint its overflow up into the briefing box area
			root.AddComponent<UnityEngine.UI.RectMask2D>();
			root.transform.SetAsFirstSibling();                   // HUD siblings stay on top (M11f lesson)

			headerText = MakeText( "header", root.transform, 13, 15f, 0f, ROW_WIDTH - 20f );
			headerText.fontStyle = FontStyles.Bold;
			headerText.color = new Color( 0.55f, 1f, 0.9f, 1f );

			contentRoot = new GameObject( "cards", typeof( RectTransform ) );
			contentRoot.transform.SetParent( root.transform, false );
			var crt = contentRoot.GetComponent<RectTransform>();
			crt.anchorMin = new Vector2( 0f, 1f );
			crt.anchorMax = new Vector2( 0f, 1f );
			crt.pivot = new Vector2( 0f, 1f );
			crt.anchoredPosition = new Vector2( 10f, -22f );
			crt.sizeDelta = new Vector2( ROW_WIDTH - 20f, 0f );

			Refresh( true );
		}

		void Update()
		{
			if ( root == null ) return;
			if ( Time.unscaledTime < nextRefresh ) return;
			nextRefresh = Time.unscaledTime + REFRESH_SECONDS;
			Refresh( false );
		}

		void Refresh( bool force )
		{
			try
			{
				// M13p (Thomas: "Text ist immer noch weg"): the old rule hid
				// the panel while HeroDashboard.gameObject was active - but
				// the Saga.unity ground truth shows the HeroDashboard ROOT is
				// permanently active (it is the persistent hero bar; only its
				// misionInfo/fame/log child windows open and close). The rule
				// therefore blocked the panel FOREVER. Overlay safety is
				// already given: the panel is SetAsFirstSibling, so any opened
				// window paints over it anyway - only the settings dialog,
				// which lives outside this sibling stack, still blocks.
				bool blocked = IsSettingsOpen();
				var infoNow = ImperialDisplay.Build( campaign );
				bool show = !blocked && infoNow != null;
				root.SetActive( show );
				if ( !show ) return;
				info = infoNow;

				// re-measure the HUD row's right edge every refresh: the
				// medpac slot is hidden in tactical mode and the "?" button
				// sits in it - panel starts right of that, self-healing
				var rt0 = root.GetComponent<RectTransform>();
				var leftNow = LeftEdgeClear();
				rt0.anchoredPosition = new Vector2( leftNow, rt0.anchoredPosition.y );
				if ( !geometryLogged )
				{
					// one breadcrumb so the next Player.log can prove WHERE
					// the panel landed and whether the slot measurement worked
					geometryLogged = true;
					Debug.Log( $"ImperialPanel::geometry left={leftNow:F0} parent={rt0.parent.name} h={rt0.sizeDelta.y:F0}" );
				}

				var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
				string title = tl != null && !string.IsNullOrEmpty( tl.impPanelTitle ) ? tl.impPanelTitle : "IMPERIUM";
				string deckLine = TacticalAISettingsUnity.ImperialDeckLineText( info.DeckName, info.CardCount ) ?? info.DeckName;
				string level = "S" + TacticalAISettings.RankOfToken( TacticalAISettings.EffectiveSkill( "" ) );
				headerText.text = Utils.ReplaceGlyphs( title + " \u00b7 " + level + " \u00b7 " + deckLine
					+ ( string.IsNullOrEmpty( info.OpenAgenda ) ? "" : "\n" + info.OpenAgenda ) );
				// agenda line makes the header two rows tall - give the card
				// list the measured room instead of a fixed 22px assumption
				{
					var crt0 = contentRoot.GetComponent<RectTransform>();
					float headH = Mathf.Max( 22f, headerText.GetPreferredValues( headerText.text ).y + 4f );
					crt0.anchoredPosition = new Vector2( 10f, -headH );
				}

				string signature = string.Join( "|", info.Rows.Select( r => r.CardId + ( r.Modelled ? "" : "!" ) ).ToArray() )
					+ "#" + info.Rows.Count;
				if ( !force && signature == lastSignature ) return;
				lastSignature = signature;

				for ( int i = contentRoot.transform.childCount - 1; i >= 0; i-- )
					Destroy( contentRoot.transform.GetChild( i ).gameObject );

				float y = 0f;
				foreach ( var row in info.Rows )
					y += AddCardRow( row, y ) + 4f;

				var rt = root.GetComponent<RectTransform>();
				rt.sizeDelta = new Vector2( ROW_WIDTH, Mathf.Min( MAX_HEIGHT, 30f + y + 6f ) );
			}
			catch ( Exception e )
			{
				Debug.LogWarning( "ImperialPanel::Refresh failed (panel stays as is): " + e.Message );
			}
		}

		bool IsSettingsOpen()
		{
			var sp = GlowEngine.FindUnityObject<SettingsPanel>();
			return sp != null && sp.gameObject.activeInHierarchy;
		}

		/// <summary>Left start of the panel: clear of the bottom-left HUD
		/// slot row. Measured from 'medpac button base' (the "?" button takes
		/// over exactly that rect in tactical mode) through world space, so
		/// it is correct across canvases; falls back to the canvas corner.
		/// Re-evaluated every refresh (M13p) - a zero-measurement at Start
		/// can no longer stick.</summary>
		float LeftEdgeClear()
		{
			var rt = root != null ? root.GetComponent<RectTransform>() : null;
			var parent = rt != null ? rt.parent as RectTransform : null;
			if ( parent == null ) return 12f;
			var medGo = GameObject.Find( "medpac button base" );
			var medRT = medGo != null ? medGo.transform as RectTransform : null;
			if ( medRT == null || medRT.rect.width <= 0f ) return 12f;
			var slotRight = parent.InverseTransformPoint(
				medRT.TransformPoint( new Vector3( medRT.rect.xMax, medRT.rect.center.y, 0f ) ) );
			return slotRight.x + 24f;
		}

		/// <summary>One printed-card row: name + XP/basic tag + honest
		/// "no AI lever" marker; hover shows the 1:1 card text. Returns the
		/// measured row height (manual stacking - no host layout group to
		/// fight, dev 23 lesson).</summary>
		float AddCardRow( ImperialDisplay.Row row, float yTop )
		{
			var tl = DataStore.uiLanguage != null ? DataStore.uiLanguage.uiTacticalAI : null;
			string head = row.Name;
			string tag = row.Basic
				? Fmt( tl != null ? tl.impBasicTag : null, "Basic \u00b7 free" )
				: Fmt( tl != null ? tl.impXpTag : null, "{0} XP", row.Xp );
			string line = head + "  <color=#FFB454>" + tag + "</color>";
			if ( !row.Modelled )
				line += "\n<color=#8FE3C8><i>" + ( tl != null ? tl.impNoModelTag : "active \u2014 no AI lever" ) + "</i></color>";

			var card = ImperialDisplay.FindCard( row.CardId );
			string tip = ImperialDisplay.CardTooltip( card );
			if ( row.Attachment && tl != null && !string.IsNullOrEmpty( tl.impAttachTip ) )
				tip += "\n<i>" + tl.impAttachTip + "</i>";
			else if ( !row.Modelled && tl != null && !string.IsNullOrEmpty( tl.impNoModelTip ) )
				tip += "\n<i>" + tl.impNoModelTip + "</i>";

			var go = new GameObject( "card-" + row.CardId, typeof( RectTransform ) );
			go.transform.SetParent( contentRoot.transform, false );
			var t = MakeText( "t", go.transform, 12, 40f, 0f, contentRoot.GetComponent<RectTransform>().sizeDelta.x );
			t.text = Utils.ReplaceGlyphs( line );
			var pref = t.GetPreferredValues( line );
			float h = Mathf.Max( 18f, pref.y );
			var tr = t.GetComponent<RectTransform>();
			tr.anchoredPosition = new Vector2( 0f, -yTop - h );   // grow downward (pivot bottom-left)
			tr.sizeDelta = new Vector2( contentRoot.GetComponent<RectTransform>().sizeDelta.x, h );
			t.raycastTarget = false;                              // keep the board clickable

			// hover: an invisible, non-blocking Graphic on the row GO is what
			// the pointer handlers ride (dev 23: Button needs own Graphic).
			var hit = go.AddComponent<Image>();
			hit.color = new Color( 0f, 0f, 0f, 0f );
			hit.raycastTarget = true;
			var hov = go.AddComponent<ImperialTooltipHover>();
			hov.Text = tip;

			var grt = go.GetComponent<RectTransform>();
			grt.anchorMin = new Vector2( 0f, 1f );
			grt.anchorMax = new Vector2( 0f, 1f );
			grt.pivot = new Vector2( 0f, 1f );
			grt.anchoredPosition = new Vector2( 0f, -yTop );
			grt.sizeDelta = new Vector2( contentRoot.GetComponent<RectTransform>().sizeDelta.x, h );
			return h;
		}

		static string Fmt( string fmt, string fallback, params object[] args )
		{
			if ( string.IsNullOrEmpty( fmt ) ) fmt = fallback;
			return string.Format( System.Globalization.CultureInfo.InvariantCulture, fmt, args );
		}

		static TextMeshProUGUI MakeText( string name, Transform parent, int fontSize, float height, float x, float width )
		{
			var go = new GameObject( name, typeof( RectTransform ) );
			go.transform.SetParent( parent, false );
			var rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2( 0f, 1f );
			rt.anchorMax = new Vector2( 0f, 1f );
			rt.pivot = new Vector2( 0f, 1f );
			rt.anchoredPosition = new Vector2( x, -0f );
			rt.sizeDelta = new Vector2( width, height );
			var t = go.AddComponent<TextMeshProUGUI>();
			t.font = TMP_Settings.defaultFontAsset;
			t.fontSize = fontSize;
			t.color = Color.white;
			t.alignment = TextAlignmentOptions.MidlineLeft;
			t.enableWordWrapping = true;
			t.overflowMode = TextOverflowModes.Overflow;
			return t;
		}
	}
}
