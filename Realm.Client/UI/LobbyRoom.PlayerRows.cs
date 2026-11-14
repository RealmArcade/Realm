using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Accounts;
using Realm.Shared.Network;

namespace Realm.Client.UI;

public partial class LobbyRoom
{
	private const int BootButtonWidth = 60;
	private const int LinkIconWidth = 24;
	private const int BootRowSeparation = 14;

	/// <summary>The width the pencil beside a computer seat's name takes, and the gap that separates it from the name.</summary>
	private const int AiEditButtonWidth = 24;
	private const int AiEditButtonSeparation = 6;

	/// <summary>The option a player slot's dropdown lists as open, which is also the state of a slot a person holds.</summary>
	private const int SlotStateOpenOption = 0;

	/// <summary>The option a player slot's dropdown lists as closed, which is a slot that accepts nobody.</summary>
	private const int SlotStateClosedOption = 1;

	/// <summary>The option a player slot's dropdown lists as AI, which is a slot a computer player holds.</summary>
	private const int SlotStateComputerOption = 2;

	private PanelContainer CreatePlayerRow(int slot, PlayerInfo? player)
	{
		var panel = new PanelContainer { Name = "Panel_PanelContainer" };
		panel.CustomMinimumSize = new Vector2(0, 46);
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateLobbyPlayerRowPlate());

		var hBox = new HBoxContainer { Name = "HBox_HBoxContainer" };
		hBox.AddThemeConstantOverride("separation", RowColumnSeparation);
		panel.AddChild(hBox);

		CreateReadyCell(hBox);
		CreateTeamCell(hBox);
		CreateColorCell(hBox);
		CreateSlotCell(hBox);
		CreateNameCell(hBox);
		CreateFactionCell(hBox);
		CreateBootCell(hBox);

		ApplyPlayerRow(panel, slot, player);
		return panel;
	}

	private void UpdatePlayerRow(PanelContainer row, int slot, PlayerInfo? player)
	{
		ApplyPlayerRow(row, slot, player);
	}

	private void ApplyPlayerRow(PanelContainer row, int slot, PlayerInfo? player)
	{
		if (!GodotObject.IsInstanceValid(row)) return;

		var hBox = row.GetChildCount() > 0 ? row.GetChild(0) as HBoxContainer : null;
		if (hBox == null) return;

		bool occupied = player != null;
		bool isLocalPlayer = player != null && CheckIfLocalPlayer(player);
		bool canEditSeat = player != null && (isLocalPlayer || Lobby.IsHost);

		row.TooltipText = string.Empty;

		UpdateReadyCell(hBox, player, isLocalPlayer);
		UpdateTeamCell(hBox, player, canEditSeat);
		UpdateColorCell(hBox, player, canEditSeat);
		UpdateSlotCell(hBox, slot, player, isLocalPlayer);
		UpdateNameCell(hBox, player, canEditSeat);
		UpdateFactionCell(hBox, player, canEditSeat);
		UpdateBootCell(hBox, player, isLocalPlayer);

		hBox.Modulate = occupied ? new Color(1f, 1f, 1f) : new Color(1f, 1f, 1f, Lobby.IsSlotOpen(slot) ? 0.72f : 0.55f);
	}

	private static Control CreateColumnCell(string name, int width)
	{
		var cell = new Control { Name = name, CustomMinimumSize = new Vector2(width, 0) };
		cell.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		cell.ClipContents = true;
		return cell;
	}

	private static CenterContainer FillColumnCell(Control cell, Control content)
	{
		var center = new CenterContainer { Name = "CellCenter", MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		cell.AddChild(center);
		center.AddChild(content);
		return center;
	}

	private bool CheckIfLocalPlayer(PlayerInfo p)
	{
		return Lobby.IsLocalPlayer(p);
	}

	private static PlayerInfo? ResolveLiveSeat(PlayerInfo rowSeat)
	{
		return GameContext.Lobby?.PlayerList.Find(candidate => candidate.PeerId == rowSeat.PeerId);
	}

	private PlayerInfo? GetLocalPlayer()
	{
		if (GameContext.Lobby?.PlayerList == null) return null;
		return Lobby.PlayerList.Find(CheckIfLocalPlayer);
	}

	private void CreateReadyCell(HBoxContainer hBox)
	{
		var cell = CreateColumnCell("ReadyCell", ReadyColumnWidth);

		var readyCheck = new CheckBox { Name = "ReadyCheck", Text = Tr("READY"), ClipText = true };
		readyCheck.AddThemeConstantOverride("icon_max_width", 20);
		UIStyle.ApplyCheckboxStyle(readyCheck);
		readyCheck.Toggled += toggled =>
		{
			PlayerInfo? seat = Lobby.PlayerList.Find(candidate => candidate.Slot == ReadCellSlot(hBox));
			if (seat == null) return;

			OnReadyCheckToggled(toggled, seat);
		};

		FillColumnCell(cell, readyCheck);
		hBox.AddChild(cell);
	}

	/// <summary>
	///     Reads the slot a row is drawing, which every cell is named after, so a control's own callback can find the seat
	///     it belongs to without holding a reference to a seat the lobby may have replaced.
	/// </summary>
	/// <param name="hBox">The row's column.</param>
	/// <returns>The player slot the row draws.</returns>
	private static int ReadCellSlot(HBoxContainer hBox)
	{
		var row = hBox.GetParent() as PanelContainer;
		string? rowName = row?.Name;
		if (string.IsNullOrEmpty(rowName)) return 0;

		int separator = rowName.LastIndexOf("Slot", StringComparison.Ordinal);
		if (separator < 0) return 0;

		return int.TryParse(rowName.AsSpan(separator + 4), out int slot) ? slot : 0;
	}

	private static PlayerInfo? FindSeatInSlot(int slot)
	{
		if (slot <= 0) return null;
		return GameContext.Lobby?.PlayerList.Find(candidate => candidate.Slot == slot);
	}

	private void OnReadyCheckToggled(bool toggled, PlayerInfo p)
	{
		GameContext.UserInterface?.PlayClickSound();
		p.IsReady = toggled;
		Lobby.UpdateReadyState(p.PeerId, toggled);
		SetupStartButton();
	}

	private void CreateTeamCell(HBoxContainer hBox)
	{
		var cell = CreateColumnCell("TeamCell", TeamColumnWidth);

		var optTeam = new OptionButton { Name = "OptTeam", CustomMinimumSize = new Vector2(TeamColumnWidth, 30), Flat = false, ClipText = true };
		ApplyTeamOptionStyle(optTeam);
		optTeam.ItemSelected += idx => OnTeamOptionSelected(idx, ReadCellSlot(hBox));
		optTeam.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		FillColumnCell(cell, optTeam);
		hBox.AddChild(cell);
	}

	private static void FillTeamOptions(OptionButton optTeam, PlayerInfo? p)
	{
		optTeam.Clear();

		int teamCount = TeamCount;
		for (int teamIndex = 1; teamIndex <= teamCount; teamIndex++)
		{
			optTeam.AddItem(string.Format(TranslationServer.Translate("Team {0}"), teamIndex));
		}

		optTeam.AddItem(TranslationServer.Translate("Spectator"));
		optTeam.Select(p == null ? -1 : GetTeamOptionIndex(p, teamCount));
	}

	private static int GetTeamOptionIndex(PlayerInfo p, int teamCount)
	{
		if (p.TeamIndex >= 1 && p.TeamIndex <= teamCount) return p.TeamIndex - 1;
		return teamCount;
	}

	private void ApplyTeamOptionStyle(OptionButton optTeam)
	{
		UIStyle.ApplyButtonStyleBoxesClearingFocus(optTeam);
		optTeam.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		optTeam.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		optTeam.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		optTeam.AddThemeFontSizeOverride("font_size", 13);
	}

	private void OnTeamOptionSelected(long idx, int slot)
	{
		GameContext.UserInterface?.PlayClickSound();

		PlayerInfo? seat = FindSeatInSlot(slot);
		if (seat == null) return;

		int teamCount = TeamCount;
		if (idx < teamCount)
		{
			seat.TeamIndex = (int)idx + 1;
		}
		else
		{
			seat.TeamIndex = -1;
			seat.Color = new Color(0.5f, 0.5f, 0.5f);
		}

		Lobby.UpdatePlayerSlot(seat.PeerId, seat.Faction, seat.Team, seat.Color, seat.Name);
	}

	private void CreateColorCell(HBoxContainer hBox)
	{
		var cell = CreateColumnCell("ColorCell", ColorColumnWidth);

		var colorBtn = new Button { Name = "ColorBtn", CustomMinimumSize = new Vector2(24, 24) };
		colorBtn.Pressed += () => OpenColorPicker(colorBtn, ReadCellSlot(hBox));
		colorBtn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		FillColumnCell(cell, colorBtn);
		hBox.AddChild(cell);
	}

	private void OpenColorPicker(Control anchor, int slot)
	{
		PlayerInfo? seat = FindSeatInSlot(slot);
		if (seat == null) return;

		if (_colorPickerPopover == null || !GodotObject.IsInstanceValid(_colorPickerPopover))
		{
			_colorPickerPopover = new ColorPickerPopover();
			_colorPickerPopover.Name = "ColorPickerPopover";
			AddChild(_colorPickerPopover);
		}

		_colorPickerPopover.ShowFor(anchor, CollectFreeColorIndices(seat), PlayerColorConfig.GetColorIndex(seat.Color), paletteIndex => OnColorPicked(seat.PeerId, paletteIndex));
	}

	private void OnColorPicked(int peerId, int paletteIndex)
	{
		PlayerInfo? seat = Lobby.PlayerList.Find(candidate => candidate.PeerId == peerId);
		if (seat == null) return;
		if (paletteIndex <= PlayerColorConfig.NEUTRAL_PLAYER_INDEX || paletteIndex >= PlayerColorConfig.Palette.Length) return;

		GameContext.UserInterface?.PlayClickSound();

		seat.Color = PlayerColorConfig.GetColor(paletteIndex);
		Lobby.UpdatePlayerSlot(seat.PeerId, seat.Faction, seat.Team, seat.Color, seat.Name);
		PopulatePlayersList();
	}

	private static IReadOnlyList<int> CollectFreeColorIndices(PlayerInfo seat)
	{
		var taken = new HashSet<int>();
		foreach (PlayerInfo other in Lobby.PlayerList)
		{
			if (other.PeerId == seat.PeerId) continue;
			if (other.TeamIndex < 1) continue;

			int heldIndex = PlayerColorConfig.GetColorIndex(other.Color);
			if (heldIndex > PlayerColorConfig.NEUTRAL_PLAYER_INDEX) taken.Add(heldIndex);
		}

		var available = new List<int>();
		for (int paletteIndex = PlayerColorConfig.NEUTRAL_PLAYER_INDEX + 1; paletteIndex < PlayerColorConfig.Palette.Length; paletteIndex++)
		{
			if (!taken.Contains(paletteIndex)) available.Add(paletteIndex);
		}

		return available;
	}

	/// <summary>
	///     Creates the cell holding the dropdown the host decides a player slot's state with: open to a player, closed to
	///     everyone, or held by a computer player. It has a column of its own so that neither it nor the name beside it has
	///     to give up width to the other.
	/// </summary>
	/// <param name="hBox">The row's column.</param>
	private void CreateSlotCell(HBoxContainer hBox)
	{
		var cell = CreateColumnCell("SlotCell", SlotColumnWidth);

		var stateSelect = new OptionButton { Name = "SlotStateSelect", CustomMinimumSize = new Vector2(SlotColumnWidth, 30), ClipText = true };
		ApplyTeamOptionStyle(stateSelect);
		stateSelect.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		stateSelect.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		stateSelect.AddThemeFontSizeOverride("font_size", 12);
		stateSelect.AddItem(TranslationServer.Translate("Open"));
		stateSelect.AddItem(TranslationServer.Translate("Closed"));
		stateSelect.AddItem(TranslationServer.Translate("AI"));
		stateSelect.ItemSelected += index => OnSlotStateSelected(index, ReadCellSlot(hBox), stateSelect);
		stateSelect.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		FillColumnCell(cell, stateSelect);
		hBox.AddChild(cell);
	}

	/// <summary>
	///     Creates the cell that names the player in a slot, and the pencil a computer seat's own settings are opened with.
	/// </summary>
	/// <param name="hBox">The row's column.</param>
	private void CreateNameCell(HBoxContainer hBox)
	{
		var cell = CreateColumnCell("NameCell", NameCellWidth);

		var nameRow = new HBoxContainer { Name = "NameRow", MouseFilter = MouseFilterEnum.Ignore };
		nameRow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		nameRow.AddThemeConstantOverride("separation", AiEditButtonSeparation);
		cell.AddChild(nameRow);

		var lblName = new Label
		{
			Name = "LblName",
			ClipText = true,
			VerticalAlignment = VerticalAlignment.Center,
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin
		};
		lblName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		lblName.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
		lblName.AddThemeFontSizeOverride("font_size", 14);
		nameRow.AddChild(lblName);

		nameRow.AddChild(CreateAiEditButton(hBox));

		hBox.AddChild(cell);
	}

	/// <summary>Creates the pencil that opens one computer seat's settings, which only a row that holds one draws.</summary>
	/// <param name="hBox">The row's column.</param>
	/// <returns>The button the seat's settings are opened with.</returns>
	private Button CreateAiEditButton(HBoxContainer hBox)
	{
		var aiEditBtn = new Button
		{
			Name = "AiEditBtn",
			Text = UnicodeIcons.EDIT,
			CustomMinimumSize = new Vector2(AiEditButtonWidth, 24),
			FocusMode = FocusModeEnum.None,
			MouseDefaultCursorShape = CursorShape.PointingHand,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			Visible = false
		};
		aiEditBtn.AddThemeConstantOverride("icon_max_width", 0);
		if (UIStyle.FontAwesome != null) aiEditBtn.AddThemeFontOverride("font", UIStyle.FontAwesome);
		aiEditBtn.AddThemeFontSizeOverride("font_size", 14);
		aiEditBtn.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		UIStyle.ApplyGoldHoverColors(aiEditBtn);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(aiEditBtn);
		aiEditBtn.TooltipText = Tr("Change this AI player's settings");
		aiEditBtn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		aiEditBtn.Pressed += () => OpenAiSettings(aiEditBtn, ReadCellSlot(hBox));

		return aiEditBtn;
	}

	/// <summary>Reads which of the three states a slot is in: held by a computer, held by a person, or empty.</summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="seat">The seat the slot holds, or nothing.</param>
	/// <returns>The option the slot's dropdown shows.</returns>
	private static int GetSlotStateOptionIndex(int slot, PlayerInfo? seat)
	{
		if (seat != null) return seat.IsComputer ? SlotStateComputerOption : SlotStateOpenOption;
		return Lobby.IsSlotOpen(slot) ? SlotStateOpenOption : SlotStateClosedOption;
	}

	/// <summary>
	///     Applies the state the host picked for one player slot. A slot holding somebody is only emptied after the host
	///     confirms it, and the dropdown is put back to the state the lobby still holds until the authority answers.
	/// </summary>
	/// <param name="index">The option the host picked.</param>
	/// <param name="slot">The player slot the row draws.</param>
	/// <param name="stateSelect">The dropdown that raised the change.</param>
	private void OnSlotStateSelected(long index, int slot, OptionButton stateSelect)
	{
		GameContext.UserInterface?.PlayClickSound();

		PlayerInfo? seat = FindSeatInSlot(slot);

		if (index == SlotStateComputerOption)
		{
			RequestComputerSeat(slot, seat);
		}
		else if (index == SlotStateClosedOption)
		{
			RequestSlotClosed(slot, seat);
		}
		else if (seat != null && seat.IsComputer)
		{
			Lobby.BootPlayer(seat.PeerId);
			Lobby.SetSlotOpen(slot, true);
		}
		else
		{
			Lobby.SetSlotOpen(slot, true);
		}

		if (GodotObject.IsInstanceValid(stateSelect)) stateSelect.Select(GetSlotStateOptionIndex(slot, FindSeatInSlot(slot)));
	}

	private void RequestComputerSeat(int slot, PlayerInfo? seat)
	{
		if (seat != null && !seat.IsComputer)
		{
			ConfirmRemovingSeat(seat, () =>
			{
				Lobby.BootPlayer(seat.PeerId);
				SeatComputerInSlot(slot);
			});

			return;
		}

		SeatComputerInSlot(slot);
	}

	private void RequestSlotClosed(int slot, PlayerInfo? seat)
	{
		if (seat == null)
		{
			Lobby.SetSlotOpen(slot, false);
			return;
		}

		ConfirmRemovingSeat(seat, () =>
		{
			Lobby.BootPlayer(seat.PeerId);
			Lobby.SetSlotOpen(slot, false);
		});
	}

	/// <summary>Asks the lobby to seat a computer player in one player slot.</summary>
	/// <param name="slot">The player slot the computer should hold.</param>
	private static void SeatComputerInSlot(int slot)
	{
		Lobby.AddAIBot(slot);
	}

	private static void ConfirmRemovingSeat(PlayerInfo seat, Action onConfirm)
	{
		string message = string.Format(
			TranslationServer.Translate("{0} will be removed from the lobby."),
			GetPlayerRowDisplayName(seat));

		GameContext.UserInterface?.ShowConfirmationDialog(message, onConfirm,
			confirmText: TranslationServer.Translate("REMOVE"),
			cancelText: TranslationServer.Translate("CANCEL"),
			titleOverride: TranslationServer.Translate("REMOVE PLAYER"));
	}

	private void CreateFactionCell(HBoxContainer hBox)
	{
		var cell = CreateColumnCell("FactionCell", FactionColumnWidth);

		var optFaction = new OptionButton { Name = "OptFaction", CustomMinimumSize = new Vector2(FactionColumnWidth, 30), Flat = false, ClipText = true };
		ApplyTeamOptionStyle(optFaction);
		optFaction.ItemSelected += idx => OnFactionOptionSelected(idx, ReadCellSlot(hBox));
		optFaction.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		FillColumnCell(cell, optFaction);
		hBox.AddChild(cell);
	}

	private void OnFactionOptionSelected(long idx, int slot)
	{
		GameContext.UserInterface?.PlayClickSound();

		PlayerInfo? seat = FindSeatInSlot(slot);
		if (seat == null) return;

		IReadOnlyList<string> factions = FactionList;
		if (idx < 0 || idx > factions.Count) return;

		seat.Faction = idx == factions.Count ? LobbyLimits.RandomFactionName : factions[(int)idx];
		Lobby.UpdatePlayerSlot(seat.PeerId, seat.Faction, seat.Team, seat.Color, seat.Name);
	}

	private static void FillFactionOptions(OptionButton optFaction, PlayerInfo? p)
	{
		IReadOnlyList<string> factions = FactionList;

		optFaction.Clear();
		for (int factionIndex = 0; factionIndex < factions.Count; factionIndex++)
		{
			optFaction.AddItem(factions[factionIndex]);
		}

		optFaction.AddItem(TranslationServer.Translate("Random"));

		if (p == null)
		{
			optFaction.Select(-1);
			return;
		}

		int selectedIndex = GetFactionOptionIndex(factions, p.Faction);
		if (selectedIndex >= 0) optFaction.Select(selectedIndex);
	}

	private static int GetFactionOptionIndex(IReadOnlyList<string> factions, string faction)
	{
		if (LobbyLimits.IsRandomFaction(faction)) return factions.Count;

		return IndexOfFaction(factions, faction);
	}

	private static int IndexOfFaction(IReadOnlyList<string> factions, string faction)
	{
		for (int index = 0; index < factions.Count; index++)
		{
			if (string.Equals(factions[index], faction, StringComparison.OrdinalIgnoreCase)) return index;
		}

		return -1;
	}

	/// <summary>
	///     Opens one computer seat's settings next to the pencil of its row, naming the seat so a host with several
	///     computer seats knows which one it is editing.
	/// </summary>
	/// <param name="anchor">The pencil the card is placed beside.</param>
	/// <param name="slot">The player slot whose seat is edited.</param>
	private void OpenAiSettings(Control anchor, int slot)
	{
		PlayerInfo? seat = FindSeatInSlot(slot);
		if (seat == null || !seat.IsComputer) return;

		if (_aiSettingsPopover == null || !GodotObject.IsInstanceValid(_aiSettingsPopover))
		{
			_aiSettingsPopover = new AiSettingsPopover();
			_aiSettingsPopover.Name = "AiSettingsPopover";
			AddChild(_aiSettingsPopover);
		}

		GameContext.UserInterface?.PlayClickSound();
		_aiSettingsPopover.ShowFor(anchor, GetPlayerRowDisplayName(seat), seat.Difficulty, difficulty => OnAiDifficultyChanged(seat.PeerId, difficulty));
	}

	private void OnAiDifficultyChanged(int peerId, LobbyBotDifficulty difficulty)
	{
		PlayerInfo? seat = Lobby.PlayerList.Find(candidate => candidate.PeerId == peerId);
		if (seat == null) return;

		seat.Difficulty = difficulty;
		Lobby.SetComputerDifficulty(peerId, difficulty);
		PopulatePlayersList();
	}

	/// <summary>
	///     Creates the cell the host removes a seat from, and the one glyph that says how good that seat's link is. The
	///     glyph keeps the width the number-and-colour text used to take, follows the kick button, and carries the measured
	///     numbers in its tooltip.
	/// </summary>
	/// <param name="hBox">The row's column.</param>
	private void CreateBootCell(HBoxContainer hBox)
	{
		var cell = CreateColumnCell("BootCell", BootColumnWidth);

		// The cell takes whatever width the row has left, and its contents sit at its far edge, so the actions end where
		// the row does whatever the other columns measure.
		cell.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var bootRow = new HBoxContainer { Name = "BootRow", MouseFilter = MouseFilterEnum.Ignore };
		bootRow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		bootRow.Alignment = BoxContainer.AlignmentMode.End;
		bootRow.AddThemeConstantOverride("separation", BootRowSeparation);
		cell.AddChild(bootRow);

		var buttonSlot = new Control { Name = "BootSlot", CustomMinimumSize = new Vector2(BootButtonWidth, 0), MouseFilter = MouseFilterEnum.Ignore };
		bootRow.AddChild(buttonSlot);

		var bootBtn = new Button { Name = "BootBtn", Text = Tr("KICK"), CustomMinimumSize = new Vector2(BootButtonWidth, 26) };
		ApplyBootButtonStyle(bootBtn);
		bootBtn.SetAnchorsPreset(LayoutPreset.CenterLeft);
		bootBtn.OffsetLeft = 0;
		bootBtn.OffsetRight = BootButtonWidth;
		bootBtn.OffsetTop = -13;
		bootBtn.OffsetBottom = 13;
		bootBtn.Pressed += () =>
		{
			PlayerInfo? seat = FindSeatInSlot(ReadCellSlot(hBox));
			if (seat == null) return;

			GameContext.UserInterface?.PlayWarningSound();
			Lobby.BootPlayer(seat.PeerId);
		};
		buttonSlot.AddChild(bootBtn);

		var linkIcon = new Label
		{
			Name = "LinkIcon",
			Text = UnicodeIcons.WIFI,
			CustomMinimumSize = new Vector2(LinkIconWidth, 0),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = MouseFilterEnum.Stop
		};
		linkIcon.AddThemeFontSizeOverride("font_size", 16);
		if (UIStyle.FontAwesome != null) linkIcon.AddThemeFontOverride("font", UIStyle.FontAwesome);
		bootRow.AddChild(linkIcon);

		hBox.AddChild(cell);
	}

	private void ApplyBootButtonStyle(Button bootBtn)
	{
		var btnNorm = UIStyle.CreateButtonNormal();
		if (btnNorm is StyleBoxFlat flat)
		{
			flat.BgColor = new Color(0.4f, 0.1f, 0.1f, 0.5f);
			flat.BorderColor = new Color(0.8f, 0.2f, 0.2f, 0.6f);
		}

		var btnHover = UIStyle.CreateButtonHover();
		if (btnHover is StyleBoxFlat flatH)
		{
			flatH.BgColor = new Color(0.6f, 0.1f, 0.1f, 0.8f);
			flatH.BorderColor = new Color(1.0f, 0.2f, 0.2f, 0.9f);
		}

		bootBtn.AddThemeStyleboxOverride("normal", btnNorm);
		bootBtn.AddThemeStyleboxOverride("hover", btnHover);
		bootBtn.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.8f));
		bootBtn.AddThemeFontSizeOverride("font_size", 12);
	}

	private void UpdateReadyCell(HBoxContainer hBox, PlayerInfo? p, bool isLocalPlayer)
	{
		var readyCheck = hBox.GetNodeOrNull<CheckBox>("ReadyCell/CellCenter/ReadyCheck");
		if (readyCheck == null) return;

		readyCheck.Visible = p != null && !p.IsComputer;
		readyCheck.Disabled = !isLocalPlayer;
		if (p == null || p.IsComputer) return;

		if (readyCheck.ButtonPressed != p.IsReady) readyCheck.SetPressedNoSignal(p.IsReady);
	}

	private void UpdateTeamCell(HBoxContainer hBox, PlayerInfo? p, bool canEditSeat)
	{
		var optTeam = hBox.GetNodeOrNull<OptionButton>("TeamCell/CellCenter/OptTeam");
		if (optTeam == null) return;

		optTeam.Visible = p != null;
		if (p == null) return;

		int teamCount = TeamCount;
		if (optTeam.ItemCount != teamCount + 1)
		{
			FillTeamOptions(optTeam, p);
		}
		else
		{
			int teamIdx = GetTeamOptionIndex(p, teamCount);
			if (optTeam.Selected != teamIdx) optTeam.Select(teamIdx);
		}

		optTeam.Disabled = !canEditSeat;
	}

	private void UpdateColorCell(HBoxContainer hBox, PlayerInfo? p, bool canEditSeat)
	{
		var colorBtn = hBox.GetNodeOrNull<Button>("ColorCell/CellCenter/ColorBtn");
		if (colorBtn == null) return;

		colorBtn.Visible = p != null;
		if (p == null) return;

		colorBtn.Disabled = !canEditSeat || p.TeamIndex < 1;
		if (colorBtn.GetThemeStylebox("normal") is StyleBoxFlat colorStyle && colorStyle.BgColor == p.Color) return;

		UIStyle.ApplySeatColorSwatch(colorBtn, p.Color);
	}

	private void UpdateSlotCell(HBoxContainer hBox, int slot, PlayerInfo? p, bool isLocalPlayer)
	{
		var stateSelect = hBox.GetNodeOrNull<OptionButton>("SlotCell/CellCenter/SlotStateSelect");
		if (stateSelect == null) return;

		bool canChooseState = Lobby.IsHost && !isLocalPlayer;
		stateSelect.Disabled = !canChooseState;
		stateSelect.TooltipText = canChooseState
			? Tr("Choose whether this player slot is open to a player, closed to everyone, or held by an AI player.")
			: string.Empty;

		int stateIndex = GetSlotStateOptionIndex(slot, p);
		if (stateSelect.Selected != stateIndex) stateSelect.Select(stateIndex);
	}

	/// <summary>
	///     Names the seat a row holds and, for a computer seat, states the settings it plays on in the name's tooltip and
	///     reveals the pencil that edits them.
	/// </summary>
	/// <param name="hBox">The row's column.</param>
	/// <param name="p">The seat the row holds, or nothing.</param>
	/// <param name="canEditSeat">Whether this screen may change that seat's settings.</param>
	private void UpdateNameCell(HBoxContainer hBox, PlayerInfo? p, bool canEditSeat)
	{
		var lblName = hBox.GetNodeOrNull<Label>("NameCell/NameRow/LblName");
		var aiEditBtn = hBox.GetNodeOrNull<Button>("NameCell/NameRow/AiEditBtn");
		if (lblName == null || aiEditBtn == null) return;

		lblName.Visible = p != null;
		aiEditBtn.Visible = p != null && p.IsComputer;
		LocalizedText.BindTooltip(lblName, () => p != null && p.IsComputer ? AiSettingsPopover.BuildSummary(p.Difficulty) : string.Empty);
		if (p == null) return;

		string displayName = GetPlayerRowDisplayName(p);
		if (lblName.Text != displayName) lblName.Text = displayName;
		ApplyNameLabelWidth(lblName, displayName);

		Color nameColor = p.IsDisconnected ? new Color(0.95f, 0.65f, 0.3f) : new Color(0.9f, 0.9f, 0.9f);
		if (lblName.GetThemeColor("font_color") != nameColor) lblName.AddThemeColorOverride("font_color", nameColor);

		aiEditBtn.Disabled = !canEditSeat;
	}

	/// <summary>
	///     Widths the name to the text it holds, as far as the column leaves once the pencil beside it has its room, so the
	///     pencil sits against the end of the name and a name too long for the row is trimmed rather than pushing it out.
	/// </summary>
	/// <param name="lblName">The label the seat is named in.</param>
	/// <param name="displayName">The name the label states.</param>
	private static void ApplyNameLabelWidth(Label lblName, string displayName)
	{
		Font? font = lblName.GetThemeFont("font");
		if (font == null) return;

		int fontSize = lblName.GetThemeFontSize("font_size");
		float textWidth = font.GetStringSize(displayName, HorizontalAlignment.Left, -1, fontSize).X;
		float maximumWidth = Mathf.Max(0, NameCellWidth - AiEditButtonWidth - AiEditButtonSeparation);
		var labelWidth = new Vector2(Mathf.Min(Mathf.Ceil(textWidth) + 2, maximumWidth), 0);
		if (lblName.CustomMinimumSize != labelWidth) lblName.CustomMinimumSize = labelWidth;
	}

	private static string GetPlayerRowDisplayName(PlayerInfo player)
	{
		string name = player.AccountId == 0 && player.PeerId > 0 ? UnverifiedNameMark.Apply(player.Name) : player.Name;

		return player.IsDisconnected
			? string.Format(TranslationServer.Translate("{0} (reconnecting)"), name)
			: name;
	}

	private void UpdateFactionCell(HBoxContainer hBox, PlayerInfo? p, bool canEditSeat)
	{
		var factionCell = hBox.GetNodeOrNull<Control>("FactionCell");
		if (factionCell == null) return;

		factionCell.Visible = HasFactions;

		var optFaction = factionCell.GetNodeOrNull<OptionButton>("CellCenter/OptFaction");
		if (optFaction == null) return;

		optFaction.Visible = p != null;
		if (!HasFactions || p == null) return;

		if (p.TeamIndex < 1)
		{
			if (optFaction.ItemCount != 1)
			{
				optFaction.Clear();
				optFaction.AddItem(TranslationServer.Translate("SPECTATOR"));
			}

			if (optFaction.Selected != 0) optFaction.Select(0);
			optFaction.Disabled = true;
			return;
		}

		IReadOnlyList<string> factions = FactionList;
		if (optFaction.ItemCount != factions.Count + 1)
		{
			FillFactionOptions(optFaction, p);
		}
		else
		{
			int selectedIndex = GetFactionOptionIndex(factions, p.Faction);
			if (selectedIndex >= 0 && optFaction.Selected != selectedIndex) optFaction.Select(selectedIndex);
		}

		optFaction.Disabled = !canEditSeat;
	}

	private void UpdateBootCell(HBoxContainer hBox, PlayerInfo? p, bool isLocalPlayer)
	{
		var bootBtn = hBox.GetNodeOrNull<Button>("BootCell/BootRow/BootSlot/BootBtn");
		var linkIcon = hBox.GetNodeOrNull<Label>("BootCell/BootRow/LinkIcon");
		if (bootBtn == null || linkIcon == null) return;

		bootBtn.Visible = p != null && Lobby.IsHost && !Lobby.IsLocalPlayer(p);

		linkIcon.Visible = p != null;
		if (p == null)
		{
			linkIcon.TooltipText = string.Empty;
			return;
		}

		Color qualityColor = GetLinkQualityColor(p);
		if (linkIcon.GetThemeColor("font_color") != qualityColor) linkIcon.AddThemeColorOverride("font_color", qualityColor);

		linkIcon.TooltipText = BuildLinkStatistics(p, isLocalPlayer);
	}

	/// <summary>Writes the three link measurements a row used to spell out, which its glyph shows as a tooltip.</summary>
	/// <param name="p">The seat the row holds.</param>
	/// <param name="isLocalPlayer">Whether the row is this screen's own seat.</param>
	/// <returns>The measurement text, and what it means for the host's row.</returns>
	private string BuildLinkStatistics(PlayerInfo p, bool isLocalPlayer)
	{
		if (!p.IsHost && !p.IsMapReady)
		{
			int downloadPercent = Math.Clamp(p.MapDownloadPercent, 0, 100);
			return $"{Tr("downloading...")} {downloadPercent}%";
		}

		if (!isLocalPlayer && !p.IsHost && !p.IsLoadReady) return Tr("loading...");

		string latencyText = p.Latency == "--" ? Tr("measuring...") : p.Latency;
		string jitterText = p.Jitter == "--" ? Tr("n/a") : p.Jitter;
		string lossText = p.PacketLoss == "--" ? Tr("n/a") : p.PacketLoss;

		string statistics = $"{Tr("Ping")}: {latencyText} | {Tr("Jitter")}: {jitterText} | {Tr("Loss")}: {lossText}";

		return p.IsHost ? $"{statistics}\n{Tr("Host: Average connection with all players in lobby")}" : statistics;
	}
}
