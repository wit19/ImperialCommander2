namespace Saga
{
	public class SagaSetupOptions
	{
		/*
    difficulty
    adaptive difficulty
    hero selection
    ally selection
    initial threat level / additional threat
    earned villains
    ignored groups
*/
		public Difficulty difficulty;
		public bool useAdaptiveDifficulty;
		public int threatLevel;
		public int addtlThreat;
		public ProjectItem projectItem;
		public bool isTutorial;
		public bool isDebugging;//testing a mission from the command line should not save state
		public int tutorialIndex;
		public bool isRoundLimitDisabled;

		// TACTICAL AI (concept 4.2.7): per-mission planner level,
		// "legacy" (default = untouched original behaviour) | "reference" |
		// "overlord". Only consulted when TacticalAI.TacticalAISettings.Enabled
		// is true; persisted with the session state like every other option.
		public string aiMode = "legacy";

		/// <summary>
		/// Cycles the per-mission AI mode (setup screen button). Returns the
		/// display text. No-op display while the global Tactical AI switch is
		/// OFF: the label then stays LEGACY and hooks run the original code.
		/// </summary>
		public string ToggleAiMode()
		{
			if ( !TacticalAI.TacticalAISettings.Enabled )
				return "LEGACY";

			if ( aiMode == "legacy" ) aiMode = "reference";
			else if ( aiMode == "reference" ) aiMode = "overlord";
			else aiMode = "legacy";

			return aiMode.ToUpperInvariant();
		}

		public SagaSetupOptions()
		{
			Reset();
		}

		public void Reset()
		{
			projectItem = null;
			difficulty = Difficulty.Medium;
			useAdaptiveDifficulty = false;
			threatLevel = 2;
			addtlThreat = 0;
			isTutorial = false;
			isDebugging = false;
			tutorialIndex = 0;
			isRoundLimitDisabled = false;
			aiMode = "legacy";
		}

		public string ToggleDifficulty()
		{
			if ( difficulty == Difficulty.Easy )
				difficulty = Difficulty.Medium;
			else if ( difficulty == Difficulty.Medium )
				difficulty = Difficulty.Hard;
			else
				difficulty = Difficulty.Easy;

			if ( difficulty == Difficulty.Easy )
				return DataStore.uiLanguage.uiSetup.easy.ToUpper();
			else if ( difficulty == Difficulty.Medium )
				return DataStore.uiLanguage.uiSetup.normal.ToUpper();
			else
				return DataStore.uiLanguage.uiSetup.hard.ToUpper();
		}
	}
}
