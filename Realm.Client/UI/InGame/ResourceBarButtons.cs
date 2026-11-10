using Godot;
using Realm.Client.Services;
using System;
using System.Collections.Generic;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The resource bar's buttons: the settings gear the scene provides, and the three the HUD creates beside it - the
///     map's own rules, the match pause and the way out of the match. The gear is configured here because the others
///     anchor themselves to it, and they share the row-insertion dance that keeps them left of it.
/// </summary>
public sealed class ResourceBarButtons
{
	private readonly Button? _settings;
	private readonly Node _dialogHost;
	private readonly Func<List<InstructionsSection>> _sections;

	private InstructionsDialog? _instructionsDialog;

	public ResourceBarButtons(Button? settings, Node dialogHost, Func<List<InstructionsSection>> sections)
	{
		_settings = settings;
		_dialogHost = dialogHost;
		_sections = sections;
	}

	/// <summary>The rules button, absent until <see cref="Build" /> has run.</summary>
	public Button? Instructions { get; private set; }

	/// <summary>The pause button, absent until <see cref="Build" /> has run.</summary>
	public Button? PauseMatch { get; private set; }

	/// <summary>The quit button, absent until <see cref="Build" /> has run.</summary>
	public Button? QuitMatch { get; private set; }

	public void Build()
	{
		if (_settings == null) return;

		SetupSettingsButton();
		SetupInstructionsButton();
		SetupPauseMatchButton();
		SetupQuitMatchButton();
	}

	/// <summary>
	///     The pause button reports the match's state rather than deciding it, so this is called when that state may have
	///     moved. Safe before <see cref="Build" />.
	/// </summary>
	public void RefreshPauseMatch()
	{
		if (PauseMatch == null || !GodotObject.IsInstanceValid(PauseMatch)) return;

		var network = GameContext.NetworkService;
		bool paused = network != null && network.IsPaused;

		PauseMatch.Text = paused ? UnicodeIcons.PLAY : UnicodeIcons.PAUSE;

		bool singlePlayer = GameContext.Host?.IsSinglePlayerMatch == true;
		PauseMatch.TooltipText = paused
			? TranslationServer.Translate("Resume Match")
			: singlePlayer
				? TranslationServer.Translate("Pause Match")
				: string.Format(TranslationServer.Translate("Pause Match - {0} pause(s) left this match"), network?.RemainingPauses ?? 0);

		// The cooldown is deliberately left to the authority: the button that could tell a player it is still recharging is
		// the one it has to press to be told, so only a player with nothing left to spend is stopped here. A match of one
		// spends nothing, so its button is never taken away.
		PauseMatch.Disabled = !paused && !singlePlayer && network != null && network.RemainingPauses <= 0;
	}

	/// <summary>
	///     The gear is the one button in the bar the scene already provides: it only needs its icon, its tooltip and its
	///     click wired. It is set up before the others so they insert themselves to its left in a configured row.
	/// </summary>
	private void SetupSettingsButton()
	{
		if (_settings == null) return;

		_settings.ExpandIcon = true;
		_settings.Icon = UIStyle.LoadCrispIcon("res://Assets/UI/gear_icon.webp", 128);
		_settings.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		_settings.AddThemeConstantOverride("icon_max_width", 22);
		_settings.TooltipText = TranslationServer.Translate("Settings (F12)");
		UIStyle.ApplyHudButtonStyleboxes(_settings);
		_settings.AddThemeColorOverride("icon_normal_color", UIStyle.ColorGoldDull);
		_settings.AddThemeColorOverride("icon_hover_color", UIStyle.ColorGold);
		_settings.AddThemeColorOverride("icon_pressed_color", UIStyle.ColorCyanGlow);
		_settings.AddThemeColorOverride("icon_focus_color", UIStyle.ColorGoldDull);
		_settings.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.UserInterface?.OpenSettingsOverlay();
		};
	}

	/// <summary>A menu with nothing in it is not a menu: the button only exists once the map has published a page.</summary>
	public void SetInstructionsAvailable(bool available)
	{
		if (Instructions == null || !GodotObject.IsInstanceValid(Instructions)) return;
		Instructions.Visible = available;
	}

	/// <summary>Repaints an open rules dialog in place, so a rule that changes mid-match does not need a reopen.</summary>
	public void RefreshOpenInstructions()
	{
		if (_instructionsDialog == null || !GodotObject.IsInstanceValid(_instructionsDialog) || !_instructionsDialog.Visible) return;
		_instructionsDialog.Refresh(_sections());
	}

	/*
	 *     The entry point to the map's own rules reference. The toolbar only offers it once the map has published a page,
	 *     because a menu with nothing in it is not a menu: the authority decides what there is to read, and this button
	 *     reports whether it has anything to show rather than deciding itself.
	 */
	private void SetupInstructionsButton()
	{
		Instructions = new Button();
		Instructions.Name = "BtnInstructions";
		Instructions.Set("icon_max_width", 0);
		Instructions.Text = UnicodeIcons.HELP;
		Instructions.CustomMinimumSize = new Vector2(32, 32);
		Instructions.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		Instructions.FocusMode = Control.FocusModeEnum.None;
		Instructions.Visible = _sections().Count > 0;
		Font instructionsIconFont = UIStyle.FontAwesome;
		if (instructionsIconFont != null)
		{
			Instructions.AddThemeFontOverride("font", instructionsIconFont);
		}

		StyleIconButton(Instructions);
		Instructions.TooltipText = TranslationServer.Translate("Instructions");
		Instructions.Pressed += OnInstructionsPressed;
		Instructions.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		InsertBeforeSettings(Instructions, _settings!.GetIndex());
	}

	private void OnInstructionsPressed()
	{
		GameContext.UserInterface?.PlayClickSound();

		if (_instructionsDialog != null && GodotObject.IsInstanceValid(_instructionsDialog))
		{
			_instructionsDialog.QueueFree();
		}

		_instructionsDialog = new InstructionsDialog { Name = "InstructionsDialog" };
		_dialogHost.AddChild(_instructionsDialog);
		_instructionsDialog.Open(_sections());
	}

	/*
	 *     The pause control every player has: it asks the match to hold where it is, and while the match is held it asks
	 *     for the resume countdown that belongs to everyone. What a player is still allowed to ask for is the match's
	 *     answer, so the button reports the match's state rather than deciding anything itself.
	 */
	private void SetupPauseMatchButton()
	{
		PauseMatch = new Button();
		PauseMatch.Name = "BtnPauseMatch";
		PauseMatch.Set("icon_max_width", 0);
		PauseMatch.Text = UnicodeIcons.PAUSE;
		PauseMatch.CustomMinimumSize = new Vector2(32, 32);
		PauseMatch.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		PauseMatch.FocusMode = Control.FocusModeEnum.None;
		Font pauseIconFont = UIStyle.FontAwesome;
		if (pauseIconFont != null)
		{
			PauseMatch.AddThemeFontOverride("font", pauseIconFont);
		}

		StyleIconButton(PauseMatch);
		PauseMatch.TooltipText = TranslationServer.Translate("Pause Match");
		PauseMatch.Pressed += OnPauseMatchButtonPressed;
		PauseMatch.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		InsertBeforeSettings(PauseMatch, _settings!.GetIndex());
	}

	/*
	 *     Holds the match for everyone, which is a request the whole lobby cannot undo by accident, so it is asked for
	 *     once before it is sent. Resuming is not: a held match has nothing left to lose by being released. A match with
	 *     nobody else in it holds only the screen pressing the button, so that one is not asked at all.
	 */
	private void OnPauseMatchButtonPressed()
	{
		GameContext.UserInterface?.PlayClickSound();

		if (GameContext.NetworkService?.IsPaused == true || GameContext.Host?.IsSinglePlayerMatch == true)
		{
			GameContext.Host?.TogglePauseRequest();
			return;
		}

		GameContext.UserInterface?.ShowConfirmationDialog(
			TranslationServer.Translate("Pause game for everyone?"),
			() => GameContext.Host?.TogglePauseRequest(),
			confirmText: TranslationServer.Translate("PAUSE"),
			cancelText: TranslationServer.Translate("CANCEL"));
	}

	private void SetupQuitMatchButton()
	{
		QuitMatch = new Button();
		QuitMatch.Name = "BtnQuitMatch";
		QuitMatch.Set("icon_max_width", 0);
		QuitMatch.Text = UnicodeIcons.SIGN_OUT;
		QuitMatch.TooltipText = TranslationServer.Translate("Quit Match");
		QuitMatch.CustomMinimumSize = new Vector2(32, 32);
		QuitMatch.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		QuitMatch.FocusMode = Control.FocusModeEnum.None;
		Font quitIconFont = UIStyle.FontAwesome;
		if (quitIconFont != null)
		{
			QuitMatch.AddThemeFontOverride("font", quitIconFont);
		}

		StyleIconButton(QuitMatch);
		QuitMatch.Pressed += OnQuitMatchPressed;
		QuitMatch.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		InsertBeforeSettings(QuitMatch, _settings!.GetIndex() + 1);
	}

	private void OnQuitMatchPressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		GameContext.UserInterface?.ShowConfirmationDialog(
			TranslationServer.Translate("Are you sure you want to quit the current match and return to the main menu?"),
			QuitMatchToMainMenu,
			confirmText: TranslationServer.Translate("QUIT"),
			cancelText: TranslationServer.Translate("CANCEL"));
	}

	private static void QuitMatchToMainMenu()
	{
		GameContext.SessionState.IsTestMode = false;
		GameContext.Lobby?.Disconnect();
		GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
	}

	/// <summary>The icon-button look the three bar buttons share: glyph font, gold palette, flat plate.</summary>
	private static void StyleIconButton(Button button)
	{
		button.AddThemeFontSizeOverride("font_size", 16);
		button.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		button.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		button.AddThemeColorOverride("font_focus_color", UIStyle.ColorGoldDull);
		UIStyle.ApplyHudButtonStyleboxes(button);
	}

	/// <summary>
	///     Puts the button in the resource row at <paramref name="index" />. Each insert shifts the gear, so callers pass
	///     the position relative to the gear as it stands at that moment, which is what keeps the order stable.
	/// </summary>
	private void InsertBeforeSettings(Control button, int index)
	{
		Node resourceRow = _settings!.GetParent();
		resourceRow.AddChild(button);
		resourceRow.MoveChild(button, index);
	}
}
