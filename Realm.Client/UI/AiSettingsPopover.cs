using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Network;

namespace Realm.Client.UI;

/// <summary>
///     The card one computer seat's settings are changed in. It is opened from the seat's row, names the seat it edits so a
///     host with several of them knows which one changed, and carries every setting a computer seat has; the difficulty is
///     the only one it offers so far, and each setting applies as it is changed rather than on a confirmation.
/// </summary>
[GlobalClass]
public partial class AiSettingsPopover : Control
{
	private const float PanelWidth = 320.0f;
	private const float MinimumPanelHeight = 210.0f;
	private const float ContentInset = 48.0f;

	private ColorRect _dismissOverlay = null!;
	private PanelContainer _panel = null!;
	private Label _seatNameLabel = null!;
	private OptionButton _difficultySelect = null!;
	private Action<LobbyBotDifficulty>? _onDifficultyChanged;

	/// <inheritdoc />
	public override void _Ready()
	{
		EnsureBuilt();
	}

	private void EnsureBuilt()
	{
		if (_panel != null) return;

		UIStyle.ConfigurePopoverRoot(this, "AiSettingsPopover");

		_dismissOverlay = UIStyle.CreateDismissOverlay(0.45f);
		_dismissOverlay.GuiInput += OnDismissOverlayInput;
		AddChild(_dismissOverlay);

		_panel = new PanelContainer { Name = "AiSettingsPanel_PanelContainer" };
		_panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		AddChild(_panel);

		var vbox = new VBoxContainer { Name = "Content_VBoxContainer" };
		vbox.AddThemeConstantOverride("separation", 10);
		_panel.AddChild(vbox);

		var titleLabel = new Label { Name = "Title_Label" };
		UIStyle.ApplyTitle(titleLabel, "AI SETTINGS", 18);
		vbox.AddChild(titleLabel);

		_seatNameLabel = new Label
		{
			Name = "SeatName_Label",
			ClipText = true,
			HorizontalAlignment = HorizontalAlignment.Center,
			CustomMinimumSize = new Vector2(PanelWidth - ContentInset, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		_seatNameLabel.AddThemeFontSizeOverride("font_size", 13);
		_seatNameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		vbox.AddChild(_seatNameLabel);

		vbox.AddChild(CreateDifficultyRow());

		var doneButton = new Button { Name = "Done_Button", CustomMinimumSize = new Vector2(0, 32) };
		doneButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonText(doneButton, TranslationServer.Translate("DONE"), 14);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(doneButton);
		UIStyle.ApplyGoldHoverColors(doneButton);
		doneButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		doneButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			Close();
		};
		vbox.AddChild(doneButton);
	}

	private HBoxContainer CreateDifficultyRow()
	{
		var row = new HBoxContainer { Name = "Difficulty_Row" };
		row.AddThemeConstantOverride("separation", 10);

		var difficultyLabel = new Label { Name = "Difficulty_Label", VerticalAlignment = VerticalAlignment.Center };
		difficultyLabel.Text = TranslationServer.Translate("DIFFICULTY");
		difficultyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		difficultyLabel.AddThemeFontSizeOverride("font_size", 13);
		row.AddChild(difficultyLabel);

		_difficultySelect = new OptionButton
		{
			Name = "Difficulty_OptionButton",
			CustomMinimumSize = new Vector2(0, 30),
			ClipText = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_difficultySelect);
		_difficultySelect.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_difficultySelect.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_difficultySelect.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		_difficultySelect.AddThemeFontSizeOverride("font_size", 13);
		_difficultySelect.AddItem(DifficultyName(LobbyBotDifficulty.Easy));
		_difficultySelect.AddItem(DifficultyName(LobbyBotDifficulty.Medium));
		_difficultySelect.AddItem(DifficultyName(LobbyBotDifficulty.Hard));
		_difficultySelect.ItemSelected += OnDifficultySelected;
		_difficultySelect.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		row.AddChild(_difficultySelect);

		return row;
	}

	/// <summary>
	///     Every setting the card holds, one line each, which is what the row states when the seat's name is hovered. A
	///     setting added to the card is added here, so the row can never describe an older set of settings than the card.
	/// </summary>
	/// <param name="difficulty">The difficulty the seat plays on.</param>
	/// <returns>The card's values, one per line.</returns>
	public static string BuildSummary(LobbyBotDifficulty difficulty)
	{
		var lines = new List<string>
		{
			string.Format(TranslationServer.Translate("Difficulty: {0}"), DifficultyName(difficulty))
		};

		return string.Join('\n', lines);
	}

	private static string DifficultyName(LobbyBotDifficulty difficulty)
	{
		return difficulty switch
		{
			LobbyBotDifficulty.Easy => TranslationServer.Translate("EASY"),
			LobbyBotDifficulty.Medium => TranslationServer.Translate("MEDIUM"),
			_ => TranslationServer.Translate("HARD")
		};
	}

	private static int DifficultyOptionIndex(LobbyBotDifficulty difficulty)
	{
		return Math.Clamp((int)difficulty - (int)LobbyBotDifficulty.Easy, 0, 2);
	}

	private void OnDifficultySelected(long index)
	{
		GameContext.UserInterface?.PlayClickSound();

		var difficulty = (LobbyBotDifficulty)((int)LobbyBotDifficulty.Easy + (int)index);
		_onDifficultyChanged?.Invoke(difficulty);
	}

	/// <summary>Opens the card for one computer seat, next to the row that asked for it.</summary>
	/// <param name="anchor">The control the card is placed beside.</param>
	/// <param name="seatName">The name the seat is drawn under, which is what the card titles itself with.</param>
	/// <param name="difficulty">The difficulty the seat plays on now.</param>
	/// <param name="onDifficultyChanged">Receives the difficulty the seat is switched to.</param>
	public void ShowFor(Control anchor, string seatName, LobbyBotDifficulty difficulty, Action<LobbyBotDifficulty> onDifficultyChanged)
	{
		EnsureBuilt();

		_onDifficultyChanged = onDifficultyChanged;
		_seatNameLabel.Text = seatName;
		_difficultySelect.Select(DifficultyOptionIndex(LobbyBotDifficultyRules.Clamp((int)difficulty)));
		Visible = true;

		Node? parent = GetParent();
		if (parent != null)
		{
			parent.MoveChild(this, parent.GetChildCount() - 1);
		}

		PositionPanelNear(anchor);
	}

	/// <summary>Closes the card without changing anything further.</summary>
	public void Close()
	{
		Visible = false;
		_onDifficultyChanged = null;
	}

	private void PositionPanelNear(Control? anchor)
	{
		Vector2 panelSize = _panel.GetCombinedMinimumSize();
		panelSize.X = Mathf.Max(PanelWidth, panelSize.X);
		panelSize.Y = Mathf.Max(MinimumPanelHeight, panelSize.Y);

		Vector2 viewportSize = GetViewportRect().Size;
		Vector2 position = anchor != null
			? anchor.GlobalPosition + new Vector2(anchor.Size.X * 0.5f - panelSize.X * 0.5f, anchor.Size.Y + 10.0f)
			: new Vector2(viewportSize.X * 0.5f - panelSize.X * 0.5f, viewportSize.Y * 0.5f - panelSize.Y * 0.5f);

		position.X = Mathf.Clamp(position.X, 16.0f, Mathf.Max(16.0f, viewportSize.X - panelSize.X - 16.0f));
		position.Y = Mathf.Clamp(position.Y, 16.0f, Mathf.Max(16.0f, viewportSize.Y - panelSize.Y - 16.0f));

		_panel.Position = position;
		_panel.Size = panelSize;
	}

	private void OnDismissOverlayInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouseEvent) return;
		if (!mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left) return;

		Close();
		GetViewport().SetInputAsHandled();
	}

	/// <inheritdoc />
	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (!Visible) return;
		if (!@event.IsActionPressed("ui_cancel")) return;

		Close();
		GetViewport().SetInputAsHandled();
	}
}
