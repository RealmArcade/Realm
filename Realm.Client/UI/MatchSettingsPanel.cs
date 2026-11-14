using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.Client.UI;

/// <summary>
///     The panel a lobby's host sets the match parameters in. It is the one surface those parameters are edited on, in both
///     places a host meets them: while the lobby is still being created, where the panel is the confirmation the lobby is not
///     published without, and inside a running lobby, where the same panel changes the settings the next match will play. The
///     rows it offers are the ones this lobby's map did not hide, so a host is never shown a setting whose board does not
///     play with it.
/// </summary>
[GlobalClass]
public partial class MatchSettingsPanel : Control
{
	/// <summary>Why the panel is open, which decides the button that finishes it.</summary>
	public enum MatchSettingsMode
	{
		/// <summary>The lobby is already published: the panel edits the settings the next match will play.</summary>
		Edit,

		/// <summary>The lobby is open but not published: finishing the panel is what publishes it.</summary>
		Confirm
	}

	private const int CardWidth = 760;
	private const int RowsHeight = 430;
	private const int LabelWidth = 260;
	private const int BodyWidth = CardWidth - 72;


	private MatchSettingsMode _mode = MatchSettingsMode.Edit;

	private readonly Dictionary<MatchOption, SpinBox> _spins = new();
	private readonly Dictionary<MatchOption, CheckBox> _toggles = new();

	private SpinBox? _playersSpin;
	private OptionButton? _shroudOption;
	private CheckBox? _teamsTogetherCheck;
	private CheckBox? _spectatorDelayCheck;
	private Label? _messageLabel;
	private Button? _primaryButton;
	private Button? _secondaryButton;

	/// <summary>Raised when the host finished the panel in confirm mode, which is the confirmation to publish the lobby.</summary>
	public event Action? Confirmed;

	/// <summary>Raised when the host left the panel without confirming.</summary>
	public event Action? Closed;

	public override void _Ready()
	{
		Name = "MatchSettingsPanel";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		ZIndex = 1100;
	}

	/// <summary>Opens the panel on this lobby's current settings.</summary>
	/// <param name="mode">Whether the panel edits a published lobby or confirms one before it is published.</param>
	public void Open(MatchSettingsMode mode)
	{
		_mode = mode;
		RebuildLayout();
		RefreshFromLobby();
		Visible = true;
	}

	/// <summary>Reads the lobby's settings into the controls, so the panel always shows what the match will play.</summary>
	public void RefreshFromLobby()
	{
		if (GameContext.Lobby is not { } lobby) return;

		if (_playersSpin != null)
		{
			int players = lobby.MatchOptions.Get(MatchOption.MaximumPlayers);
			_playersSpin.SetValueNoSignal(players);
			_playersSpin.MaxValue = LobbyLimits.MaximumSeats;
		}

		foreach (KeyValuePair<MatchOption, SpinBox> entry in _spins)
		{
			entry.Value.SetValueNoSignal(lobby.MatchOptions.Get(entry.Key));
		}

		foreach (KeyValuePair<MatchOption, CheckBox> entry in _toggles)
		{
			entry.Value.SetPressedNoSignal(lobby.MatchOptions.IsEnabled(entry.Key));
		}

		if (_shroudOption != null)
		{
			int shroudMode = (int)lobby.EffectiveShroudMode;
			for (int index = 0; index < _shroudOption.ItemCount; index++)
			{
				if (_shroudOption.GetItemId(index) != shroudMode) continue;
				_shroudOption.Select(index);
				break;
			}
		}

		if (_teamsTogetherCheck != null) _teamsTogetherCheck.SetPressedNoSignal(lobby.TeamsTogether);
		if (_spectatorDelayCheck != null) _spectatorDelayCheck.SetPressedNoSignal(lobby.SpectatorDelay);
	}

	/// <summary>States something in the panel's message line, which is where a refused setting is explained.</summary>
	/// <param name="text">The message to state.</param>
	/// <param name="isError">Whether the message is a refusal rather than a note.</param>
	public void ShowMessage(string text, bool isError)
	{
		if (_messageLabel == null || !GodotObject.IsInstanceValid(_messageLabel)) return;

		_messageLabel.Text = text;
		_messageLabel.AddThemeColorOverride("font_color", isError ? new Color(1f, 0.35f, 0.3f) : new Color(0.75f, 0.78f, 0.85f));
		_messageLabel.Visible = !string.IsNullOrEmpty(text);
	}

	private void RebuildLayout()
	{
		foreach (Node child in GetChildren()) child.QueueFree();
		_spins.Clear();
		_toggles.Clear();
		_playersSpin = null;
		_shroudOption = null;
		_teamsTogetherCheck = null;
		_spectatorDelayCheck = null;
		_messageLabel = null;
		_primaryButton = null;
		_secondaryButton = null;

		var backdrop = new Panel { Name = "MatchSettingsBackdrop_Panel" };
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		backdrop.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(backdrop);

		var center = new CenterContainer { Name = "MatchSettingsCenter_CenterContainer" };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var card = new PanelContainer { Name = "MatchSettingsCard_PanelContainer" };
		card.CustomMinimumSize = new Vector2(CardWidth, 0);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		center.AddChild(card);

		var margin = new MarginContainer { Name = "MatchSettingsMargin_MarginContainer" };
		margin.AddThemeConstantOverride("margin_left", 30);
		margin.AddThemeConstantOverride("margin_right", 30);
		margin.AddThemeConstantOverride("margin_top", 24);
		margin.AddThemeConstantOverride("margin_bottom", 24);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "MatchSettingsColumn_VBoxContainer" };
		column.AddThemeConstantOverride("separation", 12);
		margin.AddChild(column);

		column.AddChild(BuildHeader());
		column.AddChild(BuildRows());
		column.AddChild(BuildMessageLine());
		column.AddChild(BuildFooter());
	}

	private Control BuildHeader()
	{
		var header = new VBoxContainer { Name = "MatchSettingsHeader_VBoxContainer" };
		header.AddThemeConstantOverride("separation", 6);

		var title = new Label { Name = "MatchSettingsTitle_Label" };
		UIStyle.ApplyTitle(title, Tr(_mode == MatchSettingsMode.Confirm ? "CONFIRM MATCH SETTINGS" : "MATCH SETTINGS"), 24);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		header.AddChild(title);

		var subtitle = new Label { Name = "MatchSettingsSubtitle_Label" };
		subtitle.Text = Tr(_mode == MatchSettingsMode.Confirm
			? "Your map states these settings. Change anything you want the next match to play, then create the lobby."
			: "These settings apply to the next match this lobby starts.");
		subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		subtitle.CustomMinimumSize = new Vector2(BodyWidth, 0);
		subtitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		subtitle.HorizontalAlignment = HorizontalAlignment.Center;
		subtitle.AddThemeFontSizeOverride("font_size", 13);
		subtitle.AddThemeColorOverride("font_color", new Color(0.72f, 0.75f, 0.82f));
		header.AddChild(subtitle);

		header.AddChild(BuildSeparator());
		return header;
	}

	private Control BuildRows()
	{
		var scroller = new ScrollContainer { Name = "MatchSettingsScroll_ScrollContainer" };
		scroller.CustomMinimumSize = new Vector2(BodyWidth, RowsHeight);
		scroller.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroller.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

		var rows = new VBoxContainer { Name = "MatchSettingsRows_VBoxContainer" };
		rows.AddThemeConstantOverride("separation", 10);
		rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroller.AddChild(rows);

		AddSection(rows, Tr("PLAYERS"), Tr("How many players this lobby seats."));
		AddPlayersRow(rows);

		AddSection(rows, Tr("BUILDINGS"), Tr("How much of the building deck a match hands to each player."));
		AddCountRow(rows, MatchOption.MaximumBuildingDraftCount, Tr("Building options per draft:"), 24, Tr("How many building options a draft offers at once. Zero offers the number the ruleset states."));
		AddCountRow(rows, MatchOption.MaximumUniqueBuildingInstances, Tr("Copies of one building:"), 24, Tr("How many unique instances of one building a player may own. Zero uses the ruleset's own limit."));
		AddCountRow(rows, MatchOption.MaximumBuildingSupply, Tr("Building supply:"), 24, Tr("How many buildings a player may construct and hold. Zero uses the ruleset's own supply."));

		AddSection(rows, Tr("MULTIPLIERS"), Tr("Percentages applied to every player's capacity and stored production."));
		AddCountRow(rows, MatchOption.CapacityMultiplierPercent, Tr("Capacity multiplier (%):"), MatchOptionSet.MaximumMultiplierPercent, Tr("The percentage every player's capacity ceiling is multiplied by. Zero uses the ruleset's own values."));
		AddCountRow(rows, MatchOption.ProductionMultiplierPercent, Tr("Production multiplier (%):"), MatchOptionSet.MaximumMultiplierPercent, Tr("The percentage every player's stored production mana is multiplied by. Zero uses the ruleset's own values."));

		AddSection(rows, Tr("VISION"), Tr("What each player can see of the board."));
		AddShroudRow(rows);

		AddSection(rows, Tr("MATCH RULES"), Tr("How the match is seated and played once it starts."));
		AddToggleRow(rows, MatchOption.MirroredAutoDraft, Tr("Mirrored draft:"), Tr("Every player draws the same heroes and the same building options, which removes draft asymmetry."));
		AddToggleRow(rows, MatchOption.OpenPoolDrafting, Tr("Open pool drafting:"), Tr("Drafted buildings stay in the deck instead of leaving it, so the same building can be drafted again."));

		var teamsRow = new HBoxContainer { Name = "MatchSettingsTeamsTogether_HBoxContainer" };
		teamsRow.AddThemeConstantOverride("separation", 12);
		var teamsCheck = BuildToggle("TeamsTogetherCheck_CheckBox", Tr("Teams Together"), Tr("Groups each team's players into consecutive player slots, so a side sits together in the match."));
		teamsCheck.Toggled += pressed =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Lobby?.UpdateTeamsTogether(pressed);
		};
		teamsRow.AddChild(teamsCheck);
		_teamsTogetherCheck = teamsCheck;
		AddSettingRow(rows, LobbySetting.TeamsTogether, teamsRow);

		var spectatorRow = new HBoxContainer { Name = "MatchSettingsSpectatorDelay_HBoxContainer" };
		spectatorRow.AddThemeConstantOverride("separation", 12);
		var spectatorCheck = BuildToggle("SpectatorDelayCheck_CheckBox", Tr("Spectator Delay (5m)"), Tr("Players who join a match already running watch for a delay before the board is described to them."));
		spectatorCheck.Toggled += pressed =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Lobby?.UpdateSpectatorDelay(pressed);
		};
		spectatorRow.AddChild(spectatorCheck);
		_spectatorDelayCheck = spectatorCheck;
		AddSettingRow(rows, LobbySetting.SpectatorDelay, spectatorRow);

		var hint = new Label { Name = "MatchSettingsHint_Label" };
		hint.Text = Tr("A zero leaves that setting to the map. Changes reach the next match this lobby starts.");
		hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hint.CustomMinimumSize = new Vector2(BodyWidth, 0);
		hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		hint.AddThemeFontSizeOverride("font_size", 12);
		hint.AddThemeColorOverride("font_color", new Color(0.62f, 0.65f, 0.72f));
		rows.AddChild(hint);

		return scroller;
	}

	private void AddPlayersRow(VBoxContainer rows)
	{
		var row = new HBoxContainer { Name = "MatchSettingsPlayers_HBoxContainer" };
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(BuildRowLabel(Tr("Players:"), Tr("How many players this match seats. Zero seats the number the map states.")));

		_playersSpin = BuildSpin(LobbyLimits.MaximumSeats, Tr("How many players this match seats. Zero seats the number the map states."));
		_playersSpin.ValueChanged += value => RequestPlayerCount((int)value);
		row.AddChild(_playersSpin);

		AddSettingRow(rows, LobbySetting.MaximumPlayers, row);
	}

	private void AddCountRow(VBoxContainer rows, MatchOption option, string label, double maximum, string tooltip)
	{
		var row = new HBoxContainer { Name = $"MatchSettings{option}_HBoxContainer" };
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(BuildRowLabel(label, tooltip));

		SpinBox spin = BuildSpin(maximum, tooltip);
		spin.ValueChanged += value => GameContext.Lobby?.SetMatchOption(option, (int)value);
		row.AddChild(spin);
		_spins[option] = spin;

		AddSettingRow(rows, (LobbySetting)(int)option, row);
	}

	private void AddShroudRow(VBoxContainer rows)
	{
		var row = new HBoxContainer { Name = "MatchSettingsShroud_HBoxContainer" };
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(BuildRowLabel(Tr("Vision:"), Tr("What each player can see of the board.")));

		var option = new OptionButton { Name = "ShroudMode_OptionButton" };
		option.AddThemeConstantOverride("icon_max_width", 20);
		option.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		option.CustomMinimumSize = new Vector2(0, 34);
		option.FocusMode = FocusModeEnum.None;
		UIStyle.ApplyCustomLobbyDropdown(option);
		option.AddThemeColorOverride("font_color", new Color(0.92f, 0.93f, 0.97f));
		option.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		option.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		option.AddItem(Tr("Visible"), (int)MatchShroudMode.Revealed);
		option.AddItem(Tr("Explored"), (int)MatchShroudMode.VisionShroud);
		option.AddItem(Tr("Full"), (int)MatchShroudMode.ExplorationShroud);
		option.TooltipText = Tr("Visible shows the whole board. Explored keeps every place on the map remembered while hiding what nobody is watching. Full hides the ground itself until one of your units has walked over it.");
		option.ItemSelected += index =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Lobby?.SetMatchOption(MatchOption.ShroudMode, (int)option.GetItemId((int)index));
		};
		row.AddChild(option);
		_shroudOption = option;

		AddSettingRow(rows, LobbySetting.ShroudMode, row);
	}

	private void AddToggleRow(VBoxContainer rows, MatchOption option, string label, string tooltip)
	{
		var row = new HBoxContainer { Name = $"MatchSettings{option}_HBoxContainer" };
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(BuildRowLabel(label, tooltip));

		CheckBox check = BuildToggle($"{option}_CheckBox", Tr("On"), tooltip);
		check.Toggled += pressed =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Lobby?.SetMatchOption(option, pressed ? 1 : 0);
		};
		row.AddChild(check);
		_toggles[option] = check;

		AddSettingRow(rows, (LobbySetting)(int)option, row);
	}

	/// <summary>
	///     Adds a row only when this lobby's map did not hide it, which is the whole of the per-setting rule: the panel is a
	///     presentation of the rows the map left visible, and the authority refuses an order that changes a hidden one.
	/// </summary>
	private void AddSettingRow(VBoxContainer rows, LobbySetting setting, Control row)
	{
		bool hidden = GameContext.Lobby?.IsSettingHidden(setting) ?? false;
		row.Visible = !hidden;
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rows.AddChild(row);
	}

	private static void AddSection(VBoxContainer rows, string title, string descriptionText)
	{
		var section = new VBoxContainer { Name = "MatchSettingsSection_VBoxContainer" };
		section.AddThemeConstantOverride("separation", 4);

		var label = new Label { Name = "MatchSettingsSection_Label" };
		label.Text = title.ToUpperInvariant();
		label.AddThemeFontSizeOverride("font_size", 13);
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		section.AddChild(label);

		var description = new Label { Name = "MatchSettingsSectionDescription_Label" };
		description.Text = descriptionText;
		description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		description.CustomMinimumSize = new Vector2(BodyWidth, 0);
		description.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		description.AddThemeFontSizeOverride("font_size", 12);
		description.AddThemeColorOverride("font_color", new Color(0.62f, 0.65f, 0.72f));
		section.AddChild(description);

		section.AddChild(BuildSeparator());
		rows.AddChild(section);
	}

	private static HSeparator BuildSeparator() => new() { Name = "MatchSettingsSeparator_HSeparator" };

	private static Label BuildRowLabel(string text, string tooltip)
	{
		var label = new Label { Name = "MatchSettingsRowLabel_Label" };
		label.Text = text;
		label.TooltipText = tooltip;
		label.CustomMinimumSize = new Vector2(LabelWidth, 0);
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeFontSizeOverride("font_size", 14);
		label.AddThemeColorOverride("font_color", new Color(0.86f, 0.88f, 0.94f));
		return label;
	}

	private static SpinBox BuildSpin(double maximum, string tooltip)
	{
		var spin = new SpinBox { Name = "MatchSettingsSpin_SpinBox" };
		spin.MinValue = 0;
		spin.MaxValue = maximum;
		spin.Step = 1;
		spin.AllowGreater = false;
		spin.AllowLesser = false;
		spin.CustomMinimumSize = new Vector2(0, 34);
		spin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		spin.TooltipText = tooltip;
		return spin;
	}

	private static CheckBox BuildToggle(string nodeName, string text, string tooltip)
	{
		var check = new CheckBox { Name = nodeName };
		check.Text = text;
		check.TooltipText = tooltip;
		check.AddThemeConstantOverride("icon_max_width", 20);
		check.CustomMinimumSize = new Vector2(0, 30);
		UIStyle.ApplyCheckboxStyle(check);
		return check;
	}

	private Control BuildMessageLine()
	{
		_messageLabel = new Label { Name = "MatchSettingsMessage_Label" };
		_messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_messageLabel.CustomMinimumSize = new Vector2(BodyWidth, 0);
		_messageLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_messageLabel.AddThemeFontSizeOverride("font_size", 13);
		_messageLabel.Visible = false;
		return _messageLabel;
	}

	private Control BuildFooter()
	{
		var footer = new HBoxContainer { Name = "MatchSettingsFooter_HBoxContainer" };
		footer.Alignment = BoxContainer.AlignmentMode.Center;
		footer.AddThemeConstantOverride("separation", 20);

		string primaryText = _mode == MatchSettingsMode.Confirm ? Tr("CREATE LOBBY") : Tr("CLOSE");
		_primaryButton = UIStyle.CreateActionButton("MatchSettingsPrimary_Button", primaryText, 15);
		_primaryButton.CustomMinimumSize = new Vector2(210, 42);
		_primaryButton.Pressed += OnPrimaryPressed;
		_primaryButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		footer.AddChild(_primaryButton);

		string secondaryText = _mode == MatchSettingsMode.Confirm ? Tr("BACK") : string.Empty;
		if (secondaryText.Length > 0)
		{
			_secondaryButton = UIStyle.CreateBareButton("MatchSettingsSecondary_Button");
			UIStyle.ApplyButtonStyleBoxes(_secondaryButton);
			UIStyle.ApplyButtonText(_secondaryButton, secondaryText, 15);
			_secondaryButton.CustomMinimumSize = new Vector2(150, 42);
			_secondaryButton.AddThemeColorOverride("font_color", new Color(0.85f, 0.4f, 0.35f));
			_secondaryButton.Pressed += OnSecondaryPressed;
			_secondaryButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
			footer.AddChild(_secondaryButton);
		}

		return footer;
	}

	private void OnPrimaryPressed()
	{
		GameContext.UserInterface?.PlayClickSound();

		if (_mode == MatchSettingsMode.Confirm)
		{
			Confirmed?.Invoke();
			return;
		}

		Closed?.Invoke();
	}

	private void OnSecondaryPressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		Closed?.Invoke();
	}

	/// <summary>
	///     Asks for a new seat count. A count that would drop a player who is already sitting in the lobby is refused here with
	///     the reason, because the authority refuses it too and a spinner that snapped back with no explanation is how a host
	///     ends up pressing it repeatedly.
	/// </summary>
	private void RequestPlayerCount(int players)
	{
		if (GameContext.Lobby is not { } lobby) return;

		if (players > 0 && players < HighestPlayingSlot(lobby.PlayerList))
		{
			ShowMessage(string.Format(Tr("Seat {0} is taken. Remove that player from the lobby before seating fewer players."), HighestPlayingSlot(lobby.PlayerList)), isError: true);
			RefreshFromLobby();
			return;
		}

		ShowMessage(string.Empty, isError: false);
		lobby.SetMatchOption(MatchOption.MaximumPlayers, players);
	}

	private static int HighestPlayingSlot(List<PlayerInfo> players)
	{
		int highest = 0;
		for (int index = 0; index < players.Count; index++)
		{
			if (players[index].IsComputer) continue;
			if (players[index].TeamIndex < 1) continue;
			if (players[index].Slot > highest) highest = players[index].Slot;
		}

		return highest;
	}
}
