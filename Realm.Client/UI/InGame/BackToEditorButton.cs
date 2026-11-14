using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The one control that only exists in a mode of its own: the way back to the editor after a test run of a map. It is
///     separate from the HUD's other buttons because it leaves the match rather than commanding anything in it, and the
///     match's own quit button leaves a test run the same way through <see cref="Return" />.
/// </summary>
public static class BackToEditorButton
{
	public static Button Create()
	{
		var button = new Button();
		button.Name = "BtnBackToEditor";
		button.Text = TranslationServer.Translate("Back to Editor");
		button.Set("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxes(button);
		button.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		button.AddThemeFontSizeOverride("font_size", 13);
		button.CustomMinimumSize = new Vector2(160, 40);
		button.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		button.OffsetLeft = 20;
		button.OffsetTop = 20;
		button.Pressed += OnPressed;
		return button;
	}

	private static void OnPressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		Return();
	}

	/// <summary>
	///     Leaves the test run and puts the editor back on screen with the board it was showing, which is the one way out of
	///     a test run: the button that offers it and the match's quit button both end here, so neither can leave the editor
	///     in a state the other does not.
	/// </summary>
	public static void Return()
	{
		GameContext.SessionState.IsTestMode = false;
		GameContext.SessionState.ReturningFromTest = true;
		if (GameContext.Lobby != null)
		{
			GameContext.Lobby.Disconnect();
		}

		GameContext.UserInterface?.TransitionTo(GameScreen.MapEditorHUD);
	}
}
