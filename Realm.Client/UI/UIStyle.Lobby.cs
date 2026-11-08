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

	/// <summary>The plate behind a lobby dropdown, from the version picker to the "select map" button.</summary>
	public static void ApplyCustomLobbyDropdown(Button button)
	{
		button.AddThemeStyleboxOverride("normal", CreateDropdownStyle(false, false));
		button.AddThemeStyleboxOverride("hover", CreateDropdownStyle(true, false));
		button.AddThemeStyleboxOverride("pressed", CreateDropdownStyle(false, true));
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	/// <summary>The search field, which draws a different plate while it holds focus.</summary>
	public static void ApplyCustomLobbySearchInput(LineEdit edit)
	{
		edit.AddThemeStyleboxOverride("normal", CreateCustomLobbySearchInput(false));
		edit.AddThemeStyleboxOverride("focus", CreateCustomLobbySearchInput(true));
	}

	/// <summary>
	/// One lobby in the browser's list. The row and the state the pointer lifts it into are the same plate with the
	/// gold fill and rule swapped in, so they are one factory with a flag rather than two near-identical blocks.
	/// </summary>
	public static StyleBoxFlat CreateLobbyBrowserRowPlate(bool isHovered) => isHovered
		? CreateCardPlate(new Color(0.95f, 0.82f, 0.55f, 0.12f), ColorGold, 1, 0, 12, 24, 0, 0)
		: CreateCardPlate(new Color(0.12f, 0.13f, 0.16f, 0.3f), new Color(0.25f, 0.25f, 0.3f, 0.2f), 1, 0, 12, 24, 0, 0);
}
