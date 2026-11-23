using System;
using Godot;
using Realm.Client.Services;
using static Godot.Control;

namespace Realm.Client.UI;

/// <summary>
///     The first screen of a launch that could not reach Steam. It is a choice rather than a dead end: the player retries
///     Steam, continues into a local-only installation that still plays single-player and runs the editor, or quits. It
///     states what is missing before the player decides, because a menu that silently dropped every Steam-backed action would
///     look like a broken build.
/// </summary>
[GlobalClass]
public partial class SteamStartupGate : Control
{
	private Label _reasonLabel = null!;
	private Button _retryButton = null!;
	private Button _continueButton = null!;
	private Button _quitButton = null!;

	/// <summary>Raised once the player has a usable Steam identity, or has chosen to carry on without one.</summary>
	public event Action? Resolved;

	/// <summary>Raised when the player chose to quit instead of continuing.</summary>
	public event Action? QuitRequested;

	public override void _Ready()
	{
		Name = "SteamStartupGate";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		var background = new ColorRect();
		background.Name = "GateBackground_ColorRect";
		background.Color = new Color(0.03f, 0.03f, 0.04f, 1.0f);
		background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(background);

		var center = UIStyle.CreateCenteredOverlay();
		AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "GatePanel_PanelContainer";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(720, 0);
		center.AddChild(panel);

		var column = new VBoxContainer();
		column.Name = "GateColumn_VBoxContainer";
		column.AddThemeConstantOverride("separation", 18);
		panel.AddChild(column);

		var title = new Label();
		title.Name = "GateTitle_Label";
		UIStyle.ApplyTitle(title, TranslationServer.Translate("STEAM IS NOT AVAILABLE"), 26);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(title);

		_reasonLabel = new Label();
		_reasonLabel.Name = "GateReason_Label";
		_reasonLabel.Text = GameContext.Steam.UnavailableExplanation;
		_reasonLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_reasonLabel.CustomMinimumSize = new Vector2(660, 0);
		_reasonLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_reasonLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_reasonLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		_reasonLabel.AddThemeFontSizeOverride("font_size", 14);
		column.AddChild(_reasonLabel);

		var explanation = new Label();
		explanation.Name = "GateExplanation_Label";
		explanation.Text = TranslationServer.Translate("You can still play single-player, open the map editor, load the maps already on this machine and import a map package. Multiplayer, Steam lobbies, Workshop, invites, stats and creator links need Steam.");
		explanation.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		explanation.CustomMinimumSize = new Vector2(660, 0);
		explanation.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		explanation.HorizontalAlignment = HorizontalAlignment.Center;
		explanation.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		explanation.AddThemeFontSizeOverride("font_size", 13);
		column.AddChild(explanation);

		var buttons = new HBoxContainer();
		buttons.Name = "GateButtons_HBoxContainer";
		buttons.AddThemeConstantOverride("separation", 20);
		buttons.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		column.AddChild(buttons);

		_retryButton = UIStyle.CreateBareButton("GateRetry_Button");
		UIStyle.ApplyButtonText(_retryButton, TranslationServer.Translate("RETRY STEAM"), 14);
		UIStyle.ApplyButtonStyleBoxes(_retryButton);
		_retryButton.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_retryButton.Pressed += OnRetryPressed;
		buttons.AddChild(_retryButton);

		_continueButton = UIStyle.CreateBareButton("GateContinue_Button");
		UIStyle.ApplyButtonText(_continueButton, TranslationServer.Translate("CONTINUE WITHOUT STEAM"), 14);
		UIStyle.ApplyButtonStyleBoxes(_continueButton);
		_continueButton.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_continueButton.Pressed += OnContinuePressed;
		buttons.AddChild(_continueButton);

		_quitButton = UIStyle.CreateBareButton("GateQuit_Button");
		UIStyle.ApplyButtonText(_quitButton, TranslationServer.Translate("QUIT"), 14);
		UIStyle.ApplyButtonStyleBoxes(_quitButton);
		_quitButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
		_quitButton.Pressed += () => QuitRequested?.Invoke();
		buttons.AddChild(_quitButton);
	}

	private void OnRetryPressed()
	{
		if (GameContext.Steam.Retry())
		{
			Resolved?.Invoke();
			return;
		}

		_reasonLabel.Text = GameContext.Steam.UnavailableExplanation;
		GameContext.UserInterface?.ShowPopupMessage(
			TranslationServer.Translate("STEAM STILL UNAVAILABLE"),
			GameContext.Steam.UnavailableExplanation);
	}

	private void OnContinuePressed()
	{
		GameContext.Steam.ContinueWithoutSteam();
		Resolved?.Invoke();
	}
}
