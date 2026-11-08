using Godot;

namespace Realm.Client.UI;

/// <summary>
/// The button and input states the lobby screens share. <see cref="UIStyle"/> can already draw each of these plates,
/// but every lobby wrote the same four-line normal/hover/pressed/focus block against the same factory, so the block
/// lives here once. Each applier is named for the control it dresses rather than for the shape it draws.
/// </summary>
public static partial class UIStyle
{
	/// <summary>The tall "host a game" / "start game" plate, with its focus ring cleared.</summary>
	public static void ApplyCustomLobbyStartGameButton(Button button)
	{
		button.AddThemeStyleboxOverride("normal", CreateCustomLobbyStartGameButton(false, false));
		button.AddThemeStyleboxOverride("hover", CreateCustomLobbyStartGameButton(true, false));
		button.AddThemeStyleboxOverride("pressed", CreateCustomLobbyStartGameButton(false, true));
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	/// <summary>The back button that returns from a lobby to the browser.</summary>
	public static void ApplyCustomLobbyBackButton(Button button)
	{
		button.AddThemeStyleboxOverride("normal", CreateCustomLobbyBackButton(false, false));
		button.AddThemeStyleboxOverride("hover", CreateCustomLobbyBackButton(true, false));
		button.AddThemeStyleboxOverride("pressed", CreateCustomLobbyBackButton(false, true));
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	/// <summary>The refresh button in the lobby browser's list header.</summary>
	public static void ApplyCustomLobbyRechargeButton(Button button)
	{
		button.AddThemeStyleboxOverride("normal", CreateCustomLobbyRechargeButton(false, false));
		button.AddThemeStyleboxOverride("hover", CreateCustomLobbyRechargeButton(true, false));
		button.AddThemeStyleboxOverride("pressed", CreateCustomLobbyRechargeButton(false, true));
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	/// <summary>The search field, which draws a different plate while it holds focus.</summary>
	public static void ApplyCustomLobbySearchInput(LineEdit edit)
	{
		edit.AddThemeStyleboxOverride("normal", CreateCustomLobbySearchInput(false));
		edit.AddThemeStyleboxOverride("focus", CreateCustomLobbySearchInput(true));
	}
}
