using System;

namespace Saga
{
	// TACTICAL AI M12d2: the Imperial-side sibling of CM6 (CampaignModify-
	// ImperialXP) for INFLUENCE. Rules Reference "Influence", verbatim: "This
	// resource is used to purchase Agenda cards during the Imperial Upgrade
	// Stage ... Influence is tracked on the campaign log ... When the
	// Imperial player gains influence, he adds it to the current influence
	// on the campaign log and records this new total." Influence income,
	// like mission XP, is mission CONTENT (mission rewards print it) - CM7
	// reuses the app's own event-action booking hook to move it into the
	// SINGLE truth source, the M12d2 influence ledger on AgendaState
	// (append-only booking log, the app's campaign-log twin). reason is the
	// machine key ("victory" | "loot" | ...), stored verbatim in the
	// influenceLog for auditability; no UI text here.
	public class CampaignModifyInfluence : EventAction
	{
		public int influenceToAdd { get; set; } = 0;
		public string reason { get; set; } = "";

		public CampaignModifyInfluence()
		{

		}
	}
}
