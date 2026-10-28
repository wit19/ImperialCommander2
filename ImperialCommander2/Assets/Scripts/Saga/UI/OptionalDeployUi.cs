namespace Saga
{
	/// <summary>
	/// M13c: null-safe reader for the optional-deployment ui.json keys
	/// (sagaMainApp section, all 11 language files carry them; the gate
	/// TacticalAiGate/OptionalDeployGate.cs pins key presence + {0}/{1}
	/// placeholders across every shipped language). Same fallback
	/// convention as TacticalAISettingsUnity.Fallback: a missing/empty
	/// translation shows the shipped English text, never an empty string.
	/// </summary>
	public static class OptionalDeployUi
	{
		static string Get( string value, string fallback )
		{
			return string.IsNullOrEmpty( value ) ? fallback : value;
		}

		static SagaMainApp L
		{
			get { return DataStore.uiLanguage != null ? DataStore.uiLanguage.sagaMainApp : null; }
		}

		public static string Title        => Get( L?.optionalDeployTitleUC,        "Optional Deployment" );
		public static string PickGroup    => Get( L?.optionalDeployPickGroupUC,    "Choose a group:" );
		public static string PickDp       => Get( L?.optionalDeployPickDpUC,       "Now click a deployment point on the map:" );
		public static string NoGroups     => Get( L?.optionalDeployNoGroupsUC,     "No group available for optional deployment." );
		public static string CostFormat   => Get( L?.optionalDeployCostFormatUC,   "{0} - {1}" );
		public static string DpFormat     => Get( L?.optionalDeployDpFormatUC,     "Deployment point: {0}" );
		public static string Confirm      => Get( L?.optionalDeployConfirmUC,      "Deploy" );
		// Tactical-mode announce template ({0} = group, {1} = DP name).
		public static string AnnounceFormat => Get( L?.optionalTacticalAnnounceUC, "{0} deploys at {1}. Place its figures on the deployment point." );
	}
}
