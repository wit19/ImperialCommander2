using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Saga
{
	public class ErrorPanel : MonoBehaviour
	{
		public TextMeshProUGUI message;
		public PopupBase popupBase;
		public Text exitText, continueText;
		public GameObject continueButton, exitButton;

		Action closeCallback = null;

		/// <summary>
		/// centered message
		/// </summary>
		public void Show( string m, Action onCloseCallback = null )
		{
			// M13d defensive guards: during a scene teardown LogCallback may
			// reach this panel while its own components are already gone -
			// the error display must never crash on top of the original
			// error (EMPIRE12 Player.log: NRE right here, from
			// SagaController.LogCallback). No double-crash, ever.
			if ( message == null || popupBase == null ) return;

			if ( InputManager.Instance != null )
				InputManager.Instance.PushFocus( gameObject );

			if ( exitText != null ) exitText.text = DataStore.uiLanguage.uiSettings.quit;
			if ( continueText != null ) continueText.text = DataStore.uiLanguage.uiSetup.continueBtn;

			message.text = m;
			closeCallback = onCloseCallback;

			SelectButton();

			popupBase.Show();
		}

		/// <summary>
		/// Centered header, left-aligned message
		/// </summary>
		public void Show( string header, string m, Action onCloseCallback = null )
		{
			// M13d defensive guards - see Show(string) above (observed NRE:
			// GetComponent<Selectable>() returns null once the button's
			// component is destroyed during teardown while the GO still reads
			// activeSelf, and the unguarded .Select() then throws).
			if ( message == null || popupBase == null ) return;

			if ( InputManager.Instance != null )
				InputManager.Instance.PushFocus( gameObject );

			try
			{
				if ( exitText != null ) exitText.text = DataStore.uiLanguage.uiSettings.quit;
				if ( continueText != null ) continueText.text = DataStore.uiLanguage.uiSetup.continueBtn;
			}
			catch ( Exception )
			{
				if ( exitText != null ) exitText.text = "EXIT APP";
				if ( continueText != null ) continueText.text = "CONTINUE";
			}

			message.text = $"<color=yellow>{header}</color>\n\n<align=left>{m}</align>";
			closeCallback = onCloseCallback;

			SelectButton();

			popupBase.Show();
		}

		/// <summary>
		/// Centered header, left-aligned message and stack trace
		/// </summary>
		public void Show( string header, Exception e, Action onCloseCallback = null )
		{
			// M13d defensive guards - see Show(string) above
			if ( message == null || popupBase == null || e == null ) return;

			if ( InputManager.Instance != null )
				InputManager.Instance.PushFocus( gameObject );

			try
			{
				if ( exitText != null ) exitText.text = DataStore.uiLanguage.uiSettings.quit;
				if ( continueText != null ) continueText.text = DataStore.uiLanguage.uiSetup.continueBtn;
			}
			catch ( Exception )
			{
				if ( exitText != null ) exitText.text = "EXIT APP";
				if ( continueText != null ) continueText.text = "CONTINUE";
			}

			message.text = $"<color=yellow>{header}</color>\n\n<align=left><color=orange>{e.Message}</color>\n{( e.StackTrace ?? "" ).Replace( " at ", "\nat " )}</align>";
			closeCallback = onCloseCallback;

			SelectButton();

			popupBase.Show();
		}

		/// <summary>M13d: shared guarded focus-select for all three Show
		/// overloads. Unity's == null also reports destroyed objects, so a
		/// torn-down button (or its Selectable) simply skips the select
		/// instead of NRE-ing the error display itself.</summary>
		void SelectButton()
		{
			GameObject btn = ( continueButton != null && continueButton.gameObject.activeSelf )
				? continueButton : exitButton;
			if ( btn == null ) return;
			Selectable sel = btn.GetComponent<Selectable>();
			if ( sel != null ) sel.Select();
		}

		public void ContinueApp()
		{
			Hide();
		}

		public void Hide()
		{
			InputManager.Instance.PopFocus();
			popupBase.Close( closeCallback );
		}

		public void OnExitApp()
		{
			Application.Quit();
		}
	}
}
