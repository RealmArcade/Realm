using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Shared.Accounts;
using Realm.Shared.Network;

namespace Realm.Client.UI;

public partial class LobbyRoom
{
	private const int SlotStateSelectWidth = 84;

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
		CreateNameCell(hBox);
		CreateFactionCell(hBox);
		CreateDifficultyCell(hBox);
		CreateDiagnosticCell(hBox);
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
		UpdateNameCell(hBox, slot, player, isLocalPlayer);
		UpdateFactionCell(hBox, player, canEditSeat);
		UpdateDifficultyCell(hBox, player, canEditSeat);
		UpdateDiagnosticCell(hBox, player, isLocalPlayer);
		UpdateBootCell(hBox, player);

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
	///     Creates the cell that names the player in a slot, and the dropdown the host decides that slot's state with. The
	///     state belongs beside the name because it describes who the row seats: a person, a computer player, or nobody.
	/// </summary>
	/// <param name="hBox">The row's column.</param>
	private void CreateNameCell(HBoxContainer hBox)
	{
		var cell = CreateColumnCell("NameCell", NameColumnWidth);

		var nameRow = new HBoxContainer { Name = "NameRow", MouseFilter = MouseFilterEnum.Ignore };
		nameRow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		nameRow.AddThemeConstantOverride("separation", 6);
		cell.AddChild(nameRow);

		var stateSelect = new OptionButton { Name = "SlotStateSelect", CustomMinimumSize = new Vector2(SlotStateSelectWidth, 30), ClipText = true };
		ApplyTeamOptionStyle(stateSelect);
		stateSelect.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		stateSelect.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		stateSelect.AddThemeFontSizeOverride("font_size", 12);
		stateSelect.AddItem(TranslationServer.Translate("Open"));
		stateSelect.AddItem(TranslationServer.Translate("Closed"));
		stateSelect.AddItem(TranslationServer.Translate("AI"));
		stateSelect.ItemSelected += index => OnSlotStateSelected(index, ReadCellSlot(hBox), stateSelect);
		stateSelect.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		nameRow.AddChild(stateSelect);

		var lblName = new Label { Name = "LblName", ClipText = true, VerticalAlignment = VerticalAlignment.Center };
		lblName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		lblName.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
		lblName.AddThemeFontSizeOverride("font_size", 14);
		lblName.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		nameRow.AddChild(lblName);

		hBox.AddChild(cell);
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

	private void CreateDifficultyCell(HBoxContainer hBox)
	{
		var cell = CreateColumnCell("DifficultyCell", DifficultyColumnWidth);

		var optDifficulty = new OptionButton { Name = "OptDifficulty", CustomMinimumSize = new Vector2(DifficultyColumnWidth, 30), Flat = false, ClipText = true };
		ApplyTeamOptionStyle(optDifficulty);
		optDifficulty.ItemSelected += idx => OnDifficultyOptionSelected(idx, ReadCellSlot(hBox));
		optDifficulty.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		FillColumnCell(cell, optDifficulty);
		hBox.AddChild(cell);
	}

	private static void FillDifficultyOptions(OptionButton optDifficulty, PlayerInfo? p)
	{
		optDifficulty.Clear();
		optDifficulty.AddItem(TranslationServer.Translate("EASY"));
		optDifficulty.AddItem(TranslationServer.Translate("MEDIUM"));
		optDifficulty.AddItem(TranslationServer.Translate("HARD"));
		optDifficulty.Select(p == null ? -1 : GetDifficultyOptionIndex(p));
	}

	private static int GetDifficultyOptionIndex(PlayerInfo p)
	{
		return Math.Clamp((int)p.Difficulty - (int)LobbyBotDifficulty.Easy, 0, 2);
	}

	private void OnDifficultyOptionSelected(long idx, int slot)
	{
		GameContext.UserInterface?.PlayClickSound();

		PlayerInfo? seat = FindSeatInSlot(slot);
		if (seat == null) return;

		var difficulty = (LobbyBotDifficulty)((int)idx + (int)LobbyBotDifficulty.Easy);
		seat.Difficulty = difficulty;
		Lobby.SetComputerDifficulty(seat.PeerId, difficulty);
	}

	private void CreateDiagnosticCell(HBoxContainer hBox)
	{
		var diagLabel = new RichTextLabel { Name = "DiagLabel", BbcodeEnabled = true, ScrollActive = false };
		diagLabel.AutowrapMode = TextServer.AutowrapMode.Off;
		diagLabel.CustomMinimumSize = new Vector2(200, 24);
		diagLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		diagLabel.AddThemeFontSizeOverride("normal_font_size", 13);
		hBox.AddChild(diagLabel);
	}

	private string BuildDiagnosticText(PlayerInfo p, bool isLocalPlayer)
	{
		if (!p.IsHost && !p.IsMapReady)
		{
			int downloadPercent = Math.Clamp(p.MapDownloadPercent, 0, 100);
			return $"  [color=#ffa040]{Tr("downloading...")} {downloadPercent}%[/color]";
		}

		if (!isLocalPlayer && !p.IsHost && !p.IsLoadReady)
		{
			return $"  [color=#ffa040]{Tr("loading...")}[/color]";
		}

		string latencyText = p.Latency == "--" ? Tr("measuring...") : p.Latency;
		string jitterText = p.Jitter == "--" ? Tr("n/a") : p.Jitter;
		string lossText = p.PacketLoss == "--" ? Tr("n/a") : p.PacketLoss;

		string pingColor = GetPingColorCode(p.Latency);
		string jitterColor = GetJitterColorCode(p.Jitter);
		string lossColor = GetLossColorCode(p.PacketLoss);

		return $"  {Tr("Ping")}: [color={pingColor}]{latencyText}[/color] | {Tr("Jitter")}: [color={jitterColor}]{jitterText}[/color] | {Tr("Loss")}: [color={lossColor}]{lossText}[/color]";
	}

	private void CreateBootCell(HBoxContainer hBox)
	{
		var bootBtn = new Button { Name = "BootBtn", Text = Tr("KICK"), CustomMinimumSize = new Vector2(60, 26), SizeFlagsVertical = SizeFlags.ShrinkCenter };
		ApplyBootButtonStyle(bootBtn);
		bootBtn.Pressed += () =>
		{
			PlayerInfo? seat = FindSeatInSlot(ReadCellSlot(hBox));
			if (seat == null) return;

			GameContext.UserInterface?.PlayWarningSound();
			Lobby.BootPlayer(seat.PeerId);
		};
		hBox.AddChild(bootBtn);
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

	private void UpdateNameCell(HBoxContainer hBox, int slot, PlayerInfo? p, bool isLocalPlayer)
	{
		var stateSelect = hBox.GetNodeOrNull<OptionButton>("NameCell/NameRow/SlotStateSelect");
		var lblName = hBox.GetNodeOrNull<Label>("NameCell/NameRow/LblName");
		if (stateSelect == null || lblName == null) return;

		bool canChooseState = Lobby.IsHost && !isLocalPlayer;
		stateSelect.Disabled = !canChooseState;
		stateSelect.TooltipText = canChooseState
			? Tr("Choose whether this player slot is open to a player, closed to everyone, or held by an AI player.")
			: string.Empty;

		int stateIndex = GetSlotStateOptionIndex(slot, p);
		if (stateSelect.Selected != stateIndex) stateSelect.Select(stateIndex);

		lblName.Visible = p != null;
		if (p == null) return;

		string displayName = GetPlayerRowDisplayName(p);
		if (lblName.Text != displayName) lblName.Text = displayName;

		Color nameColor = p.IsDisconnected ? new Color(0.95f, 0.65f, 0.3f) : new Color(0.9f, 0.9f, 0.9f);
		if (lblName.GetThemeColor("font_color") != nameColor) lblName.AddThemeColorOverride("font_color", nameColor);
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

		factionCell.Visible = HasFactions && p != null;
		if (!HasFactions || p == null) return;

		var optFaction = factionCell.GetNodeOrNull<OptionButton>("CellCenter/OptFaction");
		if (optFaction == null) return;

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

	private void UpdateDifficultyCell(HBoxContainer hBox, PlayerInfo? p, bool canEditSeat)
	{
		var difficultyCell = hBox.GetNodeOrNull<Control>("DifficultyCell");
		var optDifficulty = hBox.GetNodeOrNull<OptionButton>("DifficultyCell/CellCenter/OptDifficulty");
		if (difficultyCell == null || optDifficulty == null) return;

		difficultyCell.Visible = p != null && p.IsComputer;
		if (p == null || !p.IsComputer) return;

		int optionIndex = GetDifficultyOptionIndex(p);
		if (optDifficulty.ItemCount != 3)
		{
			FillDifficultyOptions(optDifficulty, p);
		}
		else if (optDifficulty.Selected != optionIndex)
		{
			optDifficulty.Select(optionIndex);
		}

		optDifficulty.Disabled = !canEditSeat;
	}

	private void UpdateDiagnosticCell(HBoxContainer hBox, PlayerInfo? p, bool isLocalPlayer)
	{
		var diagLabel = hBox.GetNodeOrNull<RichTextLabel>("DiagLabel");
		if (diagLabel == null) return;

		diagLabel.Visible = p != null;
		if (p == null) return;

		diagLabel.MouseFilter = p.IsHost ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore;
		diagLabel.TooltipText = p.IsHost ? Tr("Host: Average connection with all players in lobby") : string.Empty;

		string newText = BuildDiagnosticText(p, isLocalPlayer);
		if (diagLabel.Text != newText) diagLabel.Text = newText;
	}

	private void UpdateBootCell(HBoxContainer hBox, PlayerInfo? p)
	{
		var bootBtn = hBox.GetNodeOrNull<Button>("BootBtn");
		if (bootBtn == null) return;

		bootBtn.Visible = p != null && Lobby.IsHost && !Lobby.IsLocalPlayer(p);
	}
}
