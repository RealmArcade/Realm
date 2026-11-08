using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Network;
using HttpClient = System.Net.Http.HttpClient;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.UI.InGame;
using Realm.Client.Services;
using Realm.Shared.Accounts;

namespace Realm.Client.UI;

public partial class LobbyRoom
{
	private PanelContainer CreatePlayerRow(PlayerInfo p)
	{
		bool isLocalPlayer = CheckIfLocalPlayer(p);
		bool canEditSeat = isLocalPlayer || Lobby.IsHost;

		var panel = CreatePlayerRowPanel();
		var hBox = new HBoxContainer();
		hBox.Name = "HBox_HBoxContainer";
		hBox.AddThemeConstantOverride("separation", RowColumnSeparation);
		panel.AddChild(hBox);

		AddReadyCheckToRow(hBox, p, isLocalPlayer);
		AddTeamOptionToRow(hBox, p, canEditSeat);
		AddColorButtonToRow(hBox, p, canEditSeat);
		AddNameElementToRow(hBox, p, isLocalPlayer);
		AddFactionOptionToRow(hBox, p, canEditSeat);
		AddDifficultyOptionToRow(hBox, p, canEditSeat);
		AddDiagnosticLabelToRow(hBox, p, isLocalPlayer);
		AddBootButtonToRow(hBox, p);

		return panel;
	}

	private static Control CreateColumnCell(string name, int width)
	{
		var cell = new Control();
		cell.Name = name;
		cell.CustomMinimumSize = new Vector2(width, 0);
		cell.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		cell.ClipContents = true;
		return cell;
	}

	private static CenterContainer FillColumnCell(Control cell, Control content)
	{
		var center = new CenterContainer();
		center.Name = "CellCenter";
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		center.MouseFilter = MouseFilterEnum.Ignore;
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

	private PanelContainer CreatePlayerRowPanel()
	{
		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.CustomMinimumSize = new Vector2(0, 46);

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.12f, 0.13f, 0.16f, 0.3f);
		style.BorderColor = new Color(0.2f, 0.2f, 0.25f, 0.2f);
		style.SetBorderWidthAll(1);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 0;
		style.ContentMarginBottom = 0;
		panel.AddThemeStyleboxOverride("panel", style);
		return panel;
	}

	private void AddReadyCheckToRow(HBoxContainer hBox, PlayerInfo p, bool isLocalPlayer)
	{
		var cell = CreateColumnCell("ReadyCell", ReadyColumnWidth);

		if (p.PeerId >= 1 || isLocalPlayer)
		{
			var readyCheck = new CheckBox();
			readyCheck.Name = "ReadyCheck";
			readyCheck.Text = Tr("READY");
			readyCheck.ButtonPressed = p.IsReady;
			readyCheck.ClipText = true;
			readyCheck.AddThemeConstantOverride("icon_max_width", 20);
			UIStyle.ApplyCheckboxStyle(readyCheck);

			if (isLocalPlayer)
			{
				readyCheck.Disabled = false;
				readyCheck.Toggled += (toggled) => OnReadyCheckToggled(toggled, p);
			}
			else
			{
				readyCheck.Disabled = true;
				readyCheck.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.6f, 0.6f));
			}

			FillColumnCell(cell, readyCheck);
		}

		hBox.AddChild(cell);
	}

	private void OnReadyCheckToggled(bool toggled, PlayerInfo p)
	{
		GameContext.UserInterface?.PlayClickSound();
		p.IsReady = toggled;
		Lobby.UpdateReadyState(p.PeerId, toggled);
		SetupStartButton();
	}

	private void AddTeamOptionToRow(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var cell = CreateColumnCell("TeamCell", TeamColumnWidth);

		var optTeam = new OptionButton();
		optTeam.Name = "OptTeam";
		optTeam.CustomMinimumSize = new Vector2(TeamColumnWidth, 30);
		optTeam.Flat = false;
		optTeam.ClipText = true;

		ApplyTeamOptionStyle(optTeam);
		FillTeamOptions(optTeam, p);

		optTeam.ItemSelected += (idx) => OnTeamOptionSelected(idx, p);
		optTeam.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		if (!canEditSeat)
		{
			optTeam.Disabled = true;
			optTeam.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
		}

		FillColumnCell(cell, optTeam);
		hBox.AddChild(cell);
	}

	private static void FillTeamOptions(OptionButton optTeam, PlayerInfo p)
	{
		optTeam.Clear();

		int teamCount = TeamCount;
		for (int teamIndex = 1; teamIndex <= teamCount; teamIndex++)
		{
			optTeam.AddItem(string.Format(TranslationServer.Translate("Team {0}"), teamIndex));
		}

		optTeam.AddItem(TranslationServer.Translate("Spectator"));
		optTeam.Select(GetTeamOptionIndex(p, teamCount));
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

	private void OnTeamOptionSelected(long idx, PlayerInfo p)
	{
		GameContext.UserInterface?.PlayClickSound();

		PlayerInfo? seat = ResolveLiveSeat(p);
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

	private void AddColorButtonToRow(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var cell = CreateColumnCell("ColorCell", ColorColumnWidth);

		var colorBtn = new Button();
		colorBtn.Name = "ColorBtn";
		colorBtn.CustomMinimumSize = new Vector2(24, 24);

		var colorStyle = CreateColorStyle(p.Color);
		colorBtn.AddThemeStyleboxOverride("normal", colorStyle);
		colorBtn.AddThemeStyleboxOverride("hover", colorStyle);
		colorBtn.AddThemeStyleboxOverride("pressed", colorStyle);

		colorBtn.Pressed += () => OpenColorPicker(colorBtn, p);
		colorBtn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		if (!canEditSeat || p.TeamIndex < 1)
		{
			colorBtn.Disabled = true;
		}

		FillColumnCell(cell, colorBtn);
		hBox.AddChild(cell);
	}

	private StyleBoxFlat CreateColorStyle(Color color)
	{
		var colorStyle = new StyleBoxFlat();
		colorStyle.BgColor = color;
		colorStyle.BorderColor = new Color(0.4f, 0.4f, 0.45f);
		colorStyle.SetBorderWidthAll(2);
		colorStyle.SetCornerRadiusAll(4);
		return colorStyle;
	}

	private void OpenColorPicker(Control anchor, PlayerInfo p)
	{
		PlayerInfo? seat = ResolveLiveSeat(p);
		if (seat == null) return;

		if (_colorPickerPopover == null || !GodotObject.IsInstanceValid(_colorPickerPopover))
		{
			_colorPickerPopover = new ColorPickerPopover();
			_colorPickerPopover.Name = "ColorPickerPopover";
			AddChild(_colorPickerPopover);
		}

		_colorPickerPopover.ShowFor(anchor, CollectFreeColorIndices(seat), PlayerColorConfig.GetColorIndex(seat.Color), paletteIndex => OnColorPicked(p, paletteIndex));
	}

	private void OnColorPicked(PlayerInfo p, int paletteIndex)
	{
		PlayerInfo? seat = ResolveLiveSeat(p);
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

	private void AddNameElementToRow(HBoxContainer hBox, PlayerInfo p, bool isLocalPlayer)
	{
		var cell = CreateColumnCell("NameCell", NameColumnWidth);

		if (isLocalPlayer)
		{
			var nameEdit = new LineEdit();
			nameEdit.Name = "NameEdit";
			nameEdit.Text = p.Name;
			nameEdit.CustomMinimumSize = new Vector2(NameColumnWidth, 30);

			nameEdit.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
			nameEdit.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
			nameEdit.AddThemeColorOverride("font_color", new Color(1, 1, 1));
			nameEdit.AddThemeFontSizeOverride("font_size", 14);
			nameEdit.TextSubmitted += (text) =>
			{
				PlayerInfo? seat = ResolveLiveSeat(p);
				if (seat == null) return;

				seat.Name = text;
				Lobby.UpdatePlayerSlot(seat.PeerId, seat.Faction, seat.Team, seat.Color, seat.Name);
			};
			FillColumnCell(cell, nameEdit);
		}
		else
		{
			var lblName = new Label();
			lblName.Name = "LblName";
			lblName.Text = GetPlayerRowDisplayName(p);
			lblName.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			lblName.ClipText = true;
			lblName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			lblName.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
			lblName.AddThemeFontSizeOverride("font_size", 14);
			lblName.VerticalAlignment = VerticalAlignment.Center;
			cell.AddChild(lblName);
		}

		hBox.AddChild(cell);
	}

	private void AddFactionOptionToRow(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var cell = CreateColumnCell("FactionCell", FactionColumnWidth);
		cell.Visible = HasFactions;

		var optFaction = new OptionButton();
		optFaction.Name = "OptFaction";
		optFaction.CustomMinimumSize = new Vector2(FactionColumnWidth, 30);
		optFaction.Flat = false;
		optFaction.ClipText = true;

		ApplyTeamOptionStyle(optFaction);

		if (p.TeamIndex < 1)
		{
			SetupSpectatorFactionOption(optFaction);
		}
		else
		{
			SetupPlayerFactionOption(optFaction, p, canEditSeat);
		}

		FillColumnCell(cell, optFaction);
		hBox.AddChild(cell);
	}

	private void SetupSpectatorFactionOption(OptionButton optFaction)
	{
		optFaction.AddItem(Tr("SPECTATOR"));
		optFaction.Select(0);
		optFaction.Disabled = true;
		optFaction.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
	}

	private void SetupPlayerFactionOption(OptionButton optFaction, PlayerInfo p, bool canEditSeat)
	{
		FillFactionOptions(optFaction, p);

		optFaction.ItemSelected += (idx) =>
		{
			GameContext.UserInterface?.PlayClickSound();

			PlayerInfo? seat = ResolveLiveSeat(p);
			if (seat == null) return;

			IReadOnlyList<string> factions = FactionList;
			if (idx < 0 || idx >= factions.Count) return;

			seat.Faction = factions[(int)idx];
			Lobby.UpdatePlayerSlot(seat.PeerId, seat.Faction, seat.Team, seat.Color, seat.Name);
		};
		optFaction.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		if (!canEditSeat)
		{
			optFaction.Disabled = true;
			optFaction.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
		}
	}

	private static void FillFactionOptions(OptionButton optFaction, PlayerInfo p)
	{
		IReadOnlyList<string> factions = FactionList;

		optFaction.Clear();
		for (int factionIndex = 0; factionIndex < factions.Count; factionIndex++)
		{
			optFaction.AddItem(factions[factionIndex]);
		}

		int selectedIndex = IndexOfFaction(factions, p.Faction);
		if (selectedIndex >= 0) optFaction.Select(selectedIndex);
	}

	private static int IndexOfFaction(IReadOnlyList<string> factions, string faction)
	{
		for (int index = 0; index < factions.Count; index++)
		{
			if (string.Equals(factions[index], faction, StringComparison.OrdinalIgnoreCase)) return index;
		}

		return -1;
	}

	private void AddDifficultyOptionToRow(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var cell = CreateColumnCell("DifficultyCell", DifficultyColumnWidth);

		if (p.IsComputer)
		{
			var optDifficulty = new OptionButton();
			optDifficulty.Name = "OptDifficulty";
			optDifficulty.CustomMinimumSize = new Vector2(DifficultyColumnWidth, 30);
			optDifficulty.Flat = false;
			optDifficulty.ClipText = true;

			ApplyTeamOptionStyle(optDifficulty);
			FillDifficultyOptions(optDifficulty, p);

			optDifficulty.ItemSelected += (idx) => OnDifficultyOptionSelected(idx, p);
			optDifficulty.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

			if (!canEditSeat)
			{
				optDifficulty.Disabled = true;
				optDifficulty.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));
			}

			FillColumnCell(cell, optDifficulty);
		}

		hBox.AddChild(cell);
	}

	private static void FillDifficultyOptions(OptionButton optDifficulty, PlayerInfo p)
	{
		optDifficulty.Clear();
		optDifficulty.AddItem(TranslationServer.Translate("EASY"));
		optDifficulty.AddItem(TranslationServer.Translate("MEDIUM"));
		optDifficulty.AddItem(TranslationServer.Translate("HARD"));
		optDifficulty.Select(GetDifficultyOptionIndex(p));
	}

	private static int GetDifficultyOptionIndex(PlayerInfo p)
	{
		return Math.Clamp((int)p.Difficulty - (int)LobbyBotDifficulty.Easy, 0, 2);
	}

	private void OnDifficultyOptionSelected(long idx, PlayerInfo p)
	{
		GameContext.UserInterface?.PlayClickSound();

		PlayerInfo? seat = ResolveLiveSeat(p);
		if (seat == null) return;

		var difficulty = (LobbyBotDifficulty)((int)idx + (int)LobbyBotDifficulty.Easy);
		seat.Difficulty = difficulty;
		Lobby.SetComputerDifficulty(seat.PeerId, difficulty);
	}

	private void AddDiagnosticLabelToRow(HBoxContainer hBox, PlayerInfo p, bool isLocalPlayer)
	{
		var diagLabel = new RichTextLabel();
		diagLabel.Name = "DiagLabel";
		diagLabel.BbcodeEnabled = true;
		diagLabel.ScrollActive = false;
		diagLabel.AutowrapMode = TextServer.AutowrapMode.Off;
		diagLabel.CustomMinimumSize = new Vector2(200, 24);
		diagLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		diagLabel.AddThemeFontSizeOverride("normal_font_size", 13);

		if (p.IsHost)
		{
			diagLabel.MouseFilter = MouseFilterEnum.Pass;
			diagLabel.TooltipText = Tr("Host: Average connection with all players in lobby");
		}

		diagLabel.Text = BuildDiagnosticText(p, isLocalPlayer);
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

	private void AddBootButtonToRow(HBoxContainer hBox, PlayerInfo p)
	{
		if (!Lobby.IsHost || Lobby.IsLocalPlayer(p)) return;

		var bootBtn = new Button();
		bootBtn.Name = "BootBtn";
		bootBtn.Text = Tr("KICK");
		bootBtn.CustomMinimumSize = new Vector2(60, 26);
		bootBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		ApplyBootButtonStyle(bootBtn);

		bootBtn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayWarningSound();
			Lobby.BootPlayer(p.PeerId);
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

	private void UpdatePlayerRow(PanelContainer row, PlayerInfo p)
	{
		var hBox = row.GetChildCount() > 0 ? row.GetChild(0) as HBoxContainer : null;
		if (hBox == null) return;

		bool isLocalPlayer = CheckIfLocalPlayer(p);
		bool canEditSeat = isLocalPlayer || Lobby.IsHost;

		UpdateRowReadyCheck(hBox, p, isLocalPlayer);
		UpdateRowTeamOption(hBox, p, canEditSeat);
		UpdateRowColorButton(hBox, p, canEditSeat);
		UpdateRowNameElements(hBox, p);
		UpdateRowFactionOption(hBox, p, canEditSeat);
		UpdateRowDifficultyOption(hBox, p, canEditSeat);
		UpdateRowDiagnosticLabel(hBox, p, isLocalPlayer);
	}

	private void UpdateRowReadyCheck(HBoxContainer hBox, PlayerInfo p, bool isLocalPlayer)
	{
		var readyCheck = hBox.GetNodeOrNull<CheckBox>("ReadyCell/CellCenter/ReadyCheck");
		if (readyCheck == null) return;

		if (readyCheck.ButtonPressed != p.IsReady)
		{
			readyCheck.SetPressedNoSignal(p.IsReady);
		}

		readyCheck.Disabled = !isLocalPlayer;
	}

	private void UpdateRowTeamOption(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var optTeam = hBox.GetNodeOrNull<OptionButton>("TeamCell/CellCenter/OptTeam");
		if (optTeam == null) return;

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

	private void UpdateRowColorButton(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var colorBtn = hBox.GetNodeOrNull<Button>("ColorCell/CellCenter/ColorBtn");
		if (colorBtn == null) return;

		colorBtn.Disabled = !canEditSeat || p.TeamIndex < 1;

		if (colorBtn.GetThemeStylebox("normal") is not StyleBoxFlat colorStyle) return;

		if (colorStyle.BgColor == p.Color) return;

		colorStyle.BgColor = p.Color;
		colorBtn.AddThemeStyleboxOverride("normal", colorStyle);
		colorBtn.AddThemeStyleboxOverride("hover", colorStyle);
		colorBtn.AddThemeStyleboxOverride("pressed", colorStyle);
	}

	private void UpdateRowNameElements(HBoxContainer hBox, PlayerInfo p)
	{
		var nameEdit = hBox.GetNodeOrNull<LineEdit>("NameCell/CellCenter/NameEdit");
		if (nameEdit != null && nameEdit.Text != p.Name)
		{
			nameEdit.Text = p.Name;
		}

		var lblName = hBox.GetNodeOrNull<Label>("NameCell/LblName");
		if (lblName == null) return;

		string displayName = GetPlayerRowDisplayName(p);
		if (lblName.Text != displayName)
		{
			lblName.Text = displayName;
		}

		Color nameColor = p.IsDisconnected ? new Color(0.95f, 0.65f, 0.3f) : new Color(0.9f, 0.9f, 0.9f);
		if (lblName.GetThemeColor("font_color") != nameColor)
		{
			lblName.AddThemeColorOverride("font_color", nameColor);
		}
	}

	private static string GetPlayerRowDisplayName(PlayerInfo player)
	{
		string name = player.AccountId == 0 && player.PeerId > 0 ? UnverifiedNameMark.Apply(player.Name) : player.Name;

		return player.IsDisconnected
			? string.Format(TranslationServer.Translate("{0} (reconnecting)"), name)
			: name;
	}

	private void UpdateRowFactionOption(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var factionCell = hBox.GetNodeOrNull<Control>("FactionCell");
		if (factionCell == null) return;

		factionCell.Visible = HasFactions;
		if (!HasFactions) return;

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
		if (optFaction.ItemCount != factions.Count)
		{
			FillFactionOptions(optFaction, p);
		}
		else
		{
			int selectedIndex = IndexOfFaction(factions, p.Faction);
			if (selectedIndex >= 0 && optFaction.Selected != selectedIndex) optFaction.Select(selectedIndex);
		}

		optFaction.Disabled = !canEditSeat;
	}

	private void UpdateRowDifficultyOption(HBoxContainer hBox, PlayerInfo p, bool canEditSeat)
	{
		var optDifficulty = hBox.GetNodeOrNull<OptionButton>("DifficultyCell/CellCenter/OptDifficulty");
		if (optDifficulty == null) return;

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

	private void UpdateRowDiagnosticLabel(HBoxContainer hBox, PlayerInfo p, bool isLocalPlayer)
	{
		var diagLabel = hBox.GetNodeOrNull<RichTextLabel>("DiagLabel");
		if (diagLabel == null) return;

		string newText = BuildDiagnosticText(p, isLocalPlayer);
		if (diagLabel.Text != newText)
		{
			diagLabel.Text = newText;
		}
	}
}
