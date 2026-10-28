using TMPro;
using UnityEngine;

public class Tooltip : MonoBehaviour
{
	public TextMeshProUGUI tmp;

	public void Show( string t )
	{
		string tt = "";
		switch ( t )
		{
			case "MissionInfo":
				tt = DataStore.uiLanguage.uiMainApp.tooltipInfoUC;
				break;
			case "MissionRules":
				tt = DataStore.uiLanguage.uiMainApp.tooltipRulesUC;
				break;
			case "PauseDeploy":
				tt = DataStore.uiLanguage.uiMainApp.tooltipPauseDepUC;
				break;
			case "PauseThreat":
				tt = DataStore.uiLanguage.uiMainApp.tooltipPauseThreatUC;
				break;
			case "OptionalDeploy":
				tt = DataStore.uiLanguage.uiMainApp.tooltipOpDepUC;
				break;
			case "Settings":
				tt = DataStore.uiLanguage.uiMainApp.tooltipSettingsUC;
				break;
			case "Debug":
				tt = DataStore.uiLanguage.uiMainApp.tooltipImpHandUC;
				break;
			case "Activate":
				tt = DataStore.uiLanguage.uiMainApp.tooltipActivateUC;
				break;
			case "Fame":
				tt = DataStore.uiLanguage.uiMainApp.tooltipFameUC;
				break;
			case "MapVisibility":
				tt = DataStore.uiLanguage.sagaMainApp.tooltipHideUIUC;
				break;
			case "MedPac":
				tt = DataStore.uiLanguage.uiMainApp.tooltipMedPacUC;
				break;
			case "Dashboard":
				tt = DataStore.uiLanguage.uiMainApp.tooltipDashboardUC;
				break;
			default:
				tt = "Unknown string code: " + t;
				break;
		}

		gameObject.SetActive( true );
		tmp.text = tt;
	}

	public void Hide()
	{
		gameObject.SetActive( false );
	}

	/// <summary>M12e: free-form tooltip for dynamically built elements
	/// (Imperium panel rows) - the printed content itself, glyph-mapped like
	/// every other in-game text. Unknown-string-code branch above stays the
	/// only path for fixed HUD tooltips.</summary>
	public void ShowText( string text )
	{
		gameObject.SetActive( true );
		tmp.text = Saga.Utils.ReplaceGlyphs( text ?? "" );
	}
}
