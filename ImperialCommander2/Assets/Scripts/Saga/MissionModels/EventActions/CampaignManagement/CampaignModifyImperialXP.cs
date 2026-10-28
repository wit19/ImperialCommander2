using System;

namespace Saga
{
	// TACTICAL AI M12c: the Imperial-side sibling of CampaignModifyXP (CM1).
	// Mission reward XP is mission CONTENT, not a generic rule (Rules
	// Reference "Experience Points": "players gain experience points (XP) as
	// mission rewards") - the app's existing booking hook for it is exactly
	// this event-action mechanism: outcome-branch events fire CM1 to add the
	// printed hero XP to every CampaignHero. CM6 reuses that hook for the
	// Imperial side: an author (or the future agenda/mission modules, M12d2)
	// books victory bonus, loot XP or hero-kill bonus XP with the value the
	// mission text dictates, into the SINGLE truth source - the M12b XP
	// account on ImperialDeckState (append-only booking log). reason is the
	// machine key ("victory" | "loot" | "hero" | ...), stored verbatim in the
	// xpLog for auditability; no UI text here.
	public class CampaignModifyImperialXP : EventAction
	{
		public int xpToAdd { get; set; } = 0;
		public string reason { get; set; } = "";

		public CampaignModifyImperialXP()
		{

		}
	}
}
