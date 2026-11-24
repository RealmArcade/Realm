using System;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI;

/// <summary>
///     The one leaderboard screen. It reads Steam's lifetime-wins board and draws it in the game's own style: the global
///     board or the reader's friends, each row's Steam persona where Steam has one to hand, and the reader's own rank.
///     There is no Realm ranking server behind it and no per-map board, so this screen only ever shows the fixed board the
///     client's own stats feed.
/// </summary>
[GlobalClass]
public partial class SteamLeaderboardDialog : Control
{
	private const int MaximumRowsShown = 25;

	private VBoxContainer _rows = null!;
	private Label _status = null!;
	private Label _ownRank = null!;
	private Button _globalButton = null!;
	private Button _friendsButton = null!;
	private bool _friendsOnly;

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		BuildLayout();
		Refresh();
	}

	private void BuildLayout()
	{
		var backdrop = new Panel { Name = "SteamLeaderboardBackdrop" };
		backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		UIStyle.ApplyScreenBackground(backdrop, "res://Assets/UI/player_profile_bg.webp");
		AddChild(backdrop);

		var center = new CenterContainer { Name = "SteamLeaderboardCenter" };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var card = new PanelContainer { Name = "SteamLeaderboardCard" };
		card.CustomMinimumSize = new Vector2(620, 0);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateArtworkPlate(
			"res://Assets/UI/player_profile_card_bg.webp", 24, 24, 24, 24, 40));
		center.AddChild(card);

		var margin = new MarginContainer { Name = "SteamLeaderboardMargin" };
		margin.AddThemeConstantOverride("margin_left", 28);
		margin.AddThemeConstantOverride("margin_right", 28);
		margin.AddThemeConstantOverride("margin_top", 22);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "SteamLeaderboardColumn" };
		column.AddThemeConstantOverride("separation", 10);
		margin.AddChild(column);

		var title = new Label { Name = "SteamLeaderboardTitle" };
		title.Text = TranslationServer.Translate("LEADERBOARDS");
		UIStyle.ApplyTitle(title, TranslationServer.Translate("LEADERBOARDS"), 22);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(title);

		var subtitle = new Label { Name = "SteamLeaderboardSubtitle" };
		subtitle.Text = TranslationServer.Translate("Lifetime wins, as Steam records them");
		subtitle.HorizontalAlignment = HorizontalAlignment.Center;
		subtitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		subtitle.AddThemeFontSizeOverride("font_size", 12);
		column.AddChild(subtitle);

		var scopeRow = new HBoxContainer { Name = "SteamLeaderboardScopeRow" };
		scopeRow.AddThemeConstantOverride("separation", 8);
		scopeRow.Alignment = BoxContainer.AlignmentMode.Center;
		column.AddChild(scopeRow);

		_globalButton = CreateScopeButton(scopeRow, "GLOBAL", friendsOnly: false);
		_friendsButton = CreateScopeButton(scopeRow, "FRIENDS", friendsOnly: true);

		_rows = new VBoxContainer { Name = "SteamLeaderboardRows" };
		_rows.AddThemeConstantOverride("separation", 4);
		column.AddChild(_rows);

		_status = new Label { Name = "SteamLeaderboardStatus" };
		_status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_status.CustomMinimumSize = new Vector2(540, 0);
		_status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_status.HorizontalAlignment = HorizontalAlignment.Center;
		_status.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_status.AddThemeFontSizeOverride("font_size", 13);
		column.AddChild(_status);

		_ownRank = new Label { Name = "SteamLeaderboardOwnRank" };
		_ownRank.HorizontalAlignment = HorizontalAlignment.Center;
		_ownRank.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_ownRank.AddThemeFontSizeOverride("font_size", 12);
		column.AddChild(_ownRank);

		var back = new Button { Name = "SteamLeaderboardBackButton" };
		UIStyle.ApplyButtonStyleBoxesClearingFocus(back);
		UIStyle.ApplyButtonText(back, TranslationServer.Translate("BACK"), 14);
		back.CustomMinimumSize = new Vector2(0, 42);
		back.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			QueueFree();
		};
		column.AddChild(back);
	}

	private Button CreateScopeButton(Node parent, string labelKey, bool friendsOnly)
	{
		var button = new Button { Name = $"SteamLeaderboard{(friendsOnly ? "Friends" : "Global")}Button" };
		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);
		UIStyle.ApplyButtonText(button, TranslationServer.Translate(labelKey), 13);
		button.CustomMinimumSize = new Vector2(120, 36);
		button.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			if (_friendsOnly == friendsOnly) return;
			_friendsOnly = friendsOnly;
			Refresh();
		};
		parent.AddChild(button);
		return button;
	}

	private void Refresh()
	{
		ClearRows();

		if (!GameContext.Steam.IsAvailable)
		{
			_status.Text = TranslationServer.Translate("Steam leaderboards are unavailable while Steam is not running.");
			_ownRank.Text = string.Empty;
			return;
		}

		_globalButton.Disabled = !_friendsOnly;
		_friendsButton.Disabled = _friendsOnly;
		_status.Text = TranslationServer.Translate("Loading the Steam leaderboard…");
		_ownRank.Text = string.Empty;

		GameContext.Steam.RequestWinsLeaderboard(_friendsOnly, page =>
		{
			if (!GodotObject.IsInstanceValid(this)) return;
			DrawPage(page);
		});
	}

	private void DrawPage(SteamLeaderboardPage? page)
	{
		ClearRows();

		if (page == null)
		{
			_status.Text = TranslationServer.Translate("Steam did not answer, so the leaderboard cannot be shown right now.");
			return;
		}

		SteamLeaderboardPage loaded = page.Value;
		if (loaded.Rows.Length == 0)
		{
			_status.Text = _friendsOnly
				? TranslationServer.Translate("None of your friends has won a counted match yet.")
				: TranslationServer.Translate("No counted matches have been won yet.");
			return;
		}

		_status.Text = _friendsOnly
			? TranslationServer.Translate("Lifetime wins among your Steam friends")
			: TranslationServer.Translate("Lifetime wins among every Realm player on Steam");

		SteamService steam = GameContext.Steam;
		int rows = Math.Min(loaded.Rows.Length, MaximumRowsShown);

		for (int index = 0; index < rows; index++)
		{
			SteamLeaderboardRow row = loaded.Rows[index];
			string persona = row.SteamId == steam.SteamId
				? steam.UserName
				: steam.GetPersonaName(row.SteamId);

			string label = persona.Length > 0
				? string.Format(TranslationServer.Translate("{0}. {1} — {2} wins"), row.Rank, persona, row.Score)
				: string.Format(TranslationServer.Translate("{0}. Steam player {1} — {2} wins"), row.Rank, row.SteamId, row.Score);

			var rowLabel = new Label { Name = $"SteamLeaderboardRow{index}" };
			rowLabel.Text = label;
			rowLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			rowLabel.CustomMinimumSize = new Vector2(540, 0);
			rowLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			rowLabel.AddThemeFontSizeOverride("font_size", 13);
			rowLabel.AddThemeColorOverride("font_color", row.IsLocalPlayer ? UIStyle.ColorGold : UIStyle.ColorGoldDull);
			_rows.AddChild(rowLabel);
		}

		_ownRank.Text = loaded.LocalPlayerRank > 0
			? string.Format(TranslationServer.Translate("Your rank: {0}"), loaded.LocalPlayerRank)
			: TranslationServer.Translate("You are not on this board yet.");
	}

	private void ClearRows()
	{
		foreach (Node child in _rows.GetChildren())
		{
			_rows.RemoveChild(child);
			child.QueueFree();
		}
	}
}
