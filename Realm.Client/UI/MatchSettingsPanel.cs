using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Shared.Simulation;

namespace Realm.Client.UI;

/// <summary>
///     The panel a lobby's host sets the match rules in. It is opened from the lobby room, because hosting a game takes
///     the host straight into its lobby rather than through a confirmation of the settings first. The rows it offers are
///     the ones the core game owns for every map - the shroud, whether a team's players sit together, and whether a late
///     joiner waits before the board is described - followed by every match setting this lobby's map declared for itself.
///     A map that declares nothing offers its host the core settings alone, so the panel never shows a rule the board it is
///     played on does not have.
/// </summary>
[GlobalClass]
public partial class MatchSettingsPanel : Control
{
	private const int CardWidth = 760;
	private const int RowsHeight = 430;
	private const int LabelWidth = 260;
	private const int BodyWidth = CardWidth - 72;

	private readonly Dictionary<int, SpinBox> _counts = new();
	private readonly Dictionary<int, CheckBox> _toggles = new();
	private readonly Dictionary<int, OptionButton> _choices = new();

	private OptionButton? _shroudOption;
	private CheckBox? _teamsTogetherCheck;
	private CheckBox? _spectatorDelayCheck;
	private Label? _messageLabel;

	/// <summary>Raised when the host left the panel.</summary>
	public event Action? Closed;

	public override void _Ready()
	{
		Name = "MatchSettingsPanel";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		ZIndex = 1100;
	}

	/// <summary>Opens the panel on this lobby's current settings.</summary>
	public void Open()
	{
		RebuildLayout();
		RefreshFromLobby();
		Visible = true;
	}

	/// <summary>
	///     Reads the lobby's settings into the controls, so the panel always shows what the match will play. A row the map
	///     declared after the panel was built is added rather than left out.
	/// </summary>
	public void RefreshFromLobby()
	{
		if (GameContext.Lobby is not { } lobby) return;

		if (NeedsRebuild(lobby)) RebuildLayout();

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

		for (int index = 0; index < lobby.MatchSettings.Count; index++)
		{
			LobbyMatchSetting setting = lobby.MatchSettings[index];
			switch (setting.Control)
			{
				case LobbySettingControl.Count when _counts.TryGetValue(setting.SettingId, out SpinBox? spin):
					spin.SetValueNoSignal(setting.Value);
					break;
				case LobbySettingControl.Toggle when _toggles.TryGetValue(setting.SettingId, out CheckBox? check):
					check.SetPressedNoSignal(setting.IsOn);
					break;
				case LobbySettingControl.Choice when _choices.TryGetValue(setting.SettingId, out OptionButton? option):
					if (option.Selected != setting.Value) option.Select(setting.Value);
					break;
			}
		}
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

	/// <summary>
	///     Whether the rows on screen still describe this lobby's map, which is what a map that declared its settings after
	///     the panel was first opened makes false.
	/// </summary>
	private bool NeedsRebuild(LobbyManager lobby)
	{
		if (lobby.MatchSettings.Count != _counts.Count + _toggles.Count + _choices.Count) return true;

		for (int index = 0; index < lobby.MatchSettings.Count; index++)
		{
			int settingId = lobby.MatchSettings[index].SettingId;
			if (_counts.ContainsKey(settingId) || _toggles.ContainsKey(settingId) || _choices.ContainsKey(settingId)) continue;

			return true;
		}

		return false;
	}

	private void RebuildLayout()
	{
		foreach (Node child in GetChildren()) child.QueueFree();
		_counts.Clear();
		_toggles.Clear();
		_choices.Clear();
		_shroudOption = null;
		_teamsTogetherCheck = null;
		_spectatorDelayCheck = null;
		_messageLabel = null;

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
		UIStyle.ApplyTitle(title, Tr("MATCH SETTINGS"), 24);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		header.AddChild(title);

		var subtitle = new Label { Name = "MatchSettingsSubtitle_Label" };
		subtitle.Text = Tr("These settings apply to the next match this lobby starts.");
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

		AddSection(rows, Tr("VISION"), Tr("What each player can see of the board."));
		AddShroudRow(rows);

		AddSection(rows, Tr("MATCH RULES"), Tr("How the match is seated and played once it starts."));
		AddTeamsTogetherRow(rows);
		AddSpectatorDelayRow(rows);

		if (GameContext.Lobby is { MatchSettings.Count: > 0 } lobby)
		{
			AddSection(rows, Tr("MAP RULES"), Tr("The rules this map plays with, which other maps do not have."));

			for (int index = 0; index < lobby.MatchSettings.Count; index++)
			{
				AddDeclaredSettingRow(rows, lobby.MatchSettings[index]);
			}
		}

		return scroller;
	}

	private void AddShroudRow(VBoxContainer rows)
	{
		var row = new HBoxContainer { Name = "MatchSettingsShroud_HBoxContainer" };
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(BuildRowLabel(Tr("Shroud:"), Tr("What each player can see of the board.")));

		var option = BuildChoice("ShroudMode_OptionButton");
		option.AddItem(Tr("None"), (int)MatchShroudMode.None);
		option.AddItem(Tr("Partial"), (int)MatchShroudMode.Partial);
		option.AddItem(Tr("Full"), (int)MatchShroudMode.Full);
		option.TooltipText = Tr("None shows the whole board. Partial keeps every place on the map remembered while hiding what nobody is watching. Full hides the ground itself until one of your units has walked over it.");
		option.ItemSelected += index =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Lobby?.UpdateShroudMode((MatchShroudMode)option.GetItemId((int)index));
		};
		row.AddChild(option);
		_shroudOption = option;

		AddFill(rows, row);
	}

	private void AddTeamsTogetherRow(VBoxContainer rows)
	{
		var row = new HBoxContainer { Name = "MatchSettingsTeamsTogether_HBoxContainer" };
		row.AddThemeConstantOverride("separation", 12);
		var check = BuildToggle("TeamsTogetherCheck_CheckBox", Tr("Teams Together"), Tr("Groups each team's players into consecutive player slots, so a side sits together in the match."));
		check.Toggled += pressed =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Lobby?.UpdateTeamsTogether(pressed);
		};
		row.AddChild(check);
		_teamsTogetherCheck = check;

		AddFill(rows, row);
	}

	private void AddSpectatorDelayRow(VBoxContainer rows)
	{
		var row = new HBoxContainer { Name = "MatchSettingsSpectatorDelay_HBoxContainer" };
		row.AddThemeConstantOverride("separation", 12);
		var check = BuildToggle("SpectatorDelayCheck_CheckBox", Tr("Spectator Delay (5m)"), Tr("Players who join a match already running watch for a delay before the board is described to them."));
		check.Toggled += pressed =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Lobby?.UpdateSpectatorDelay(pressed);
		};
		row.AddChild(check);
		_spectatorDelayCheck = check;

		AddFill(rows, row);
	}

	/// <summary>
	///     Adds the row one setting the lobby's map declared is edited with, which is drawn from the declaration alone: a
	///     toggle is a checkbox, a choice is the dropdown its options name, and a count is a spinner inside the range the map
	///     stated.
	/// </summary>
	/// <param name="rows">The column the row is added to.</param>
	/// <param name="setting">The setting the map declared.</param>
	private void AddDeclaredSettingRow(VBoxContainer rows, LobbyMatchSetting setting)
	{
		var row = new HBoxContainer { Name = $"MatchSettingsDeclared{setting.SettingId}_HBoxContainer" };
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(BuildRowLabel(setting.Label, setting.Tooltip));

		switch (setting.Control)
		{
			case LobbySettingControl.Toggle:
			{
				CheckBox check = BuildToggle($"Declared{setting.SettingId}_CheckBox", Tr("On"), setting.Tooltip);
				check.Toggled += pressed =>
				{
					GameContext.UserInterface?.PlayClickSound();
					GameContext.Lobby?.SetLobbySetting(setting.SettingId, pressed ? 1 : 0);
				};
				row.AddChild(check);
				_toggles[setting.SettingId] = check;
				break;
			}
			case LobbySettingControl.Choice:
			{
				OptionButton option = BuildChoice($"Declared{setting.SettingId}_OptionButton");
				for (int index = 0; index < setting.Options.Length; index++)
				{
					option.AddItem(setting.Options[index], index);
				}

				option.ItemSelected += index =>
				{
					GameContext.UserInterface?.PlayClickSound();
					GameContext.Lobby?.SetLobbySetting(setting.SettingId, (int)index);
				};
				row.AddChild(option);
				_choices[setting.SettingId] = option;
				break;
			}
			default:
			{
				SpinBox spin = BuildSpin(setting.Maximum, setting.Tooltip);
				spin.MinValue = setting.Minimum;
				spin.ValueChanged += value =>
				{
					GameContext.Lobby?.SetLobbySetting(setting.SettingId, (int)value);
				};
				row.AddChild(spin);
				_counts[setting.SettingId] = spin;
				break;
			}
		}

		AddFill(rows, row);
	}

	private static void AddFill(VBoxContainer rows, Control row)
	{
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

	private static OptionButton BuildChoice(string nodeName)
	{
		var option = new OptionButton { Name = nodeName };
		option.AddThemeConstantOverride("icon_max_width", 20);
		option.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		option.CustomMinimumSize = new Vector2(0, 34);
		option.FocusMode = FocusModeEnum.None;
		UIStyle.ApplyCustomLobbyDropdown(option);
		option.AddThemeColorOverride("font_color", new Color(0.92f, 0.93f, 0.97f));
		option.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		option.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		return option;
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

		Button close = UIStyle.CreateActionButton("MatchSettingsClose_Button", Tr("CLOSE"), 15);
		close.CustomMinimumSize = new Vector2(210, 42);
		close.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			Closed?.Invoke();
		};
		close.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		footer.AddChild(close);

		return footer;
	}
}
