// Filling the page from the map data: feature rows, the star rating, awards, gameplay stats, the
// tech-info table, and the card wrapper plus hover animation that seat each group.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Distribution;
using FileAccess = Godot.FileAccess;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;


namespace Realm.Client.UI;

public partial class MapDetails
{
	private void PopulateFeatures()
	{
		foreach (Node child in _featuresList.GetChildren())
		{
			child.QueueFree();
		}

		if (_mapData == null) return;

		var featureSet = new HashSet<string>(_mapData.Features ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
		var displayList = new List<string>(_mapData.Features ?? Array.Empty<string>());
		string[] standardFeatures = { "Custom Units", "Custom Buildings", "Custom Abilities", "Custom Weapons", "Custom Upgrades", "Boss Waves", "Achievements", "Hardcore Mode" };

		foreach (var sf in standardFeatures)
		{
			if (!featureSet.Contains(sf) && displayList.Count < 8)
			{
				displayList.Add(sf);
			}
		}

		foreach (var feat in displayList)
		{
			CreateFeatureRow(feat, featureSet.Contains(feat));
		}
	}

	private void CreateFeatureRow(string feat, bool isActive)
	{
		var rowPanel = new PanelContainer();
		rowPanel.Name = "RowPanel_PanelContainer";
		var rowStyle = new StyleBoxFlat();
		rowStyle.BgColor = isActive ? new Color(0.03f, 0.05f, 0.08f, 0.65f) : new Color(0.02f, 0.03f, 0.04f, 0.35f);
		rowStyle.BorderColor = isActive ? new Color(0.15f, 0.65f, 1.0f, 0.5f) : new Color(0.35f, 0.3f, 0.22f, 0.25f);
		rowStyle.SetBorderWidthAll(1);
		rowStyle.SetCornerRadiusAll(6);
		rowStyle.ContentMarginLeft = 12;
		rowStyle.ContentMarginRight = 12;
		rowStyle.ContentMarginTop = 8;
		rowStyle.ContentMarginBottom = 8;
		rowPanel.AddThemeStyleboxOverride("panel", rowStyle);

		var hBox = new HBoxContainer();
		hBox.Name = "HBox_HBoxContainer";
		hBox.AddThemeConstantOverride("separation", 10);
		rowPanel.AddChild(hBox);

		var iconRect = new TextureRect();
		iconRect.Name = "IconRect_TextureRect";
		iconRect.CustomMinimumSize = new Vector2(22, 22);
		iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		iconRect.Texture = LoadTextureSafe(isActive ? "res://Assets/UI/checked_box.webp" : "res://Assets/UI/unchecked_box.webp");
		hBox.AddChild(iconRect);

		var label = new Label();
		label.Name = "Label_Label";
		label.Text = feat;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeColorOverride("font_color", isActive ? new Color(0.92f, 0.95f, 1.0f) : new Color(0.55f, 0.58f, 0.65f));
		label.AddThemeFontSizeOverride("font_size", 13);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		hBox.AddChild(label);

		AttachHoverAnimation(rowPanel, 1.03f,
			isActive ? UIStyle.ColorCyanGlow : UIStyle.ColorGold,
			isActive ? new Color(0.15f, 0.65f, 1.0f, 0.5f) : new Color(0.35f, 0.3f, 0.22f, 0.25f));

		_featuresList.AddChild(rowPanel);
	}

	private void PopulateRatings()
	{
		FillStarsRow(_starsRow1, 5);
		_votesRow1.Text = $"{_mapData.Votes5Star}   {_mapData.AvgRating} Stars";
		_votesRow1.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow1.AddThemeFontSizeOverride("font_size", 12);

		FillStarsRow(_starsRow2, 3);
		_votesRow2.Text = $"{_mapData.Votes3Star}   3.0/5 Stars";
		_votesRow2.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow2.AddThemeFontSizeOverride("font_size", 12);

		FillStarsRow(_starsRow3, 1);
		_votesRow3.Text = $"{_mapData.Votes1Star}   1.0/5 Stars";
		_votesRow3.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow3.AddThemeFontSizeOverride("font_size", 12);
	}

	private void FillStarsRow(HBoxContainer container, int filledStars)
	{
		foreach (Node child in container.GetChildren())
		{
			child.QueueFree();
		}

		for (int i = 0; i < 5; i++)
		{
			var star = new Label();
			star.Name = "Star_Label";
			star.Text = i < filledStars ? "★" : "☆";
			star.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			star.AddThemeFontSizeOverride("font_size", 16);
			container.AddChild(star);
		}
	}

	private void PopulateAwards()
	{
		foreach (Node child in _awardsContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (_mapData?.Awards == null) return;

		foreach (var awardPath in _mapData.Awards)
		{
			var badgePanel = new PanelContainer();
			badgePanel.Name = "BadgePanel_PanelContainer";
			var badgeStyle = new StyleBoxFlat();
			badgeStyle.BgColor = new Color(0.08f, 0.09f, 0.12f, 0.85f);
			badgeStyle.BorderColor = UIStyle.ColorGoldDull;
			badgeStyle.SetBorderWidthAll(1);
			badgeStyle.SetCornerRadiusAll(6);
			badgeStyle.ContentMarginLeft = 8;
			badgeStyle.ContentMarginRight = 8;
			badgeStyle.ContentMarginTop = 8;
			badgeStyle.ContentMarginBottom = 8;
			badgePanel.AddThemeStyleboxOverride("panel", badgeStyle);

			var rect = new TextureRect();
			rect.Name = "Rect_TextureRect";
			rect.CustomMinimumSize = new Vector2(36, 36);
			rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			rect.Texture = LoadTextureSafe(awardPath) ?? LoadTextureSafe("res://Assets/UI/gold_coin.webp");

			badgePanel.AddChild(rect);
			AttachHoverAnimation(badgePanel, 1.10f, UIStyle.ColorGold, UIStyle.ColorGoldDull);
			_awardsContainer.AddChild(badgePanel);
		}
	}

	private void PopulateGameplayStats()
	{
		foreach (Node child in _gameplayStatsContainer.GetChildren())
		{
			child.QueueFree();
		}

		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/clock.webp", $"AVG PLAYTIME: {_mapData.AvgPlaytime}");
		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/alliance_flag.webp", $"PLAYER COUNT: {_mapData.PlayerCount}");
		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/victory_flag.webp", $"COMPLETION RATE: {_mapData.CompletionRate}");
	}

	private void PopulateTechInfo()
	{
		foreach (Node child in _techInfoContainer.GetChildren())
		{
			child.QueueFree();
		}

		AddStatRow(_techInfoContainer, "res://Assets/UI/wood_logs.webp", $"FILE SIZE: {_mapData.FileSize}");
		AddStatRow(_techInfoContainer, "res://Assets/UI/gold_g.webp", $"ENGINE VERSION: {_mapData.EngineVersion}");
		AddStatRow(_techInfoContainer, "res://Assets/UI/battle_shield.webp", $"MAX PLAYERS: {_mapData.MaxPlayers}");
		AddStatRow(_techInfoContainer, "res://Assets/UI/battle_axe.webp", $"GENRE: {_mapData.Genre}");
	}

	private void AddStatRow(VBoxContainer container, string iconPath, string text)
	{
		var rowPanel = new PanelContainer();
		rowPanel.Name = "RowPanel_PanelContainer";
		var rowStyle = new StyleBoxFlat();
		rowStyle.BgColor = new Color(0.02f, 0.03f, 0.04f, 0.5f);
		rowStyle.BorderColor = new Color(0.45f, 0.38f, 0.25f, 0.35f);
		rowStyle.SetBorderWidthAll(1);
		rowStyle.SetCornerRadiusAll(4);
		rowStyle.ContentMarginLeft = 10;
		rowStyle.ContentMarginRight = 10;
		rowStyle.ContentMarginTop = 6;
		rowStyle.ContentMarginBottom = 6;
		rowPanel.AddThemeStyleboxOverride("panel", rowStyle);

		var hBox = new HBoxContainer();
		hBox.Name = "HBox_HBoxContainer";
		hBox.AddThemeConstantOverride("separation", 10);
		rowPanel.AddChild(hBox);

		var icon = new TextureRect();
		icon.Name = "Icon_TextureRect";
		icon.CustomMinimumSize = new Vector2(24, 24);
		icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		icon.Texture = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
		hBox.AddChild(icon);

		string[] parts = text.Split(new char[] { ':' }, 2);
		if (parts.Length == 2)
		{
			var keyLabel = new Label();
			keyLabel.Name = "KeyLabel_Label";
			keyLabel.Text = parts[0].Trim() + ":";
			keyLabel.VerticalAlignment = VerticalAlignment.Center;
			keyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			keyLabel.AddThemeFontSizeOverride("font_size", 12);
			hBox.AddChild(keyLabel);

			var valueLabel = new Label();
			valueLabel.Name = "ValueLabel_Label";
			valueLabel.Text = parts[1].Trim();
			valueLabel.VerticalAlignment = VerticalAlignment.Center;
			valueLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.98f));
			valueLabel.AddThemeFontSizeOverride("font_size", 13);
			valueLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			hBox.AddChild(valueLabel);
		}
		else
		{
			var label = new Label();
			label.Name = "Label_Label";
			label.Text = text;
			label.VerticalAlignment = VerticalAlignment.Center;
			label.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
			label.AddThemeFontSizeOverride("font_size", 13);
			hBox.AddChild(label);
		}

		container.AddChild(rowPanel);
		AttachHoverAnimation(rowPanel, 1.02f, UIStyle.ColorGoldDull, new Color(0.45f, 0.38f, 0.25f, 0.35f));
	}

	private void WrapSectionInCard(VBoxContainer parent, Label titleLabel, Control contentControl, bool isAccentCard = false)
	{
		if (titleLabel == null || contentControl == null) return;
		if (titleLabel.GetParent() is PanelContainer) return;

		var card = new PanelContainer();
		card.Name = "Card_PanelContainer";
		var cardStyle = new StyleBoxFlat();
		cardStyle.BgColor = isAccentCard
			? new Color(0.06f, 0.07f, 0.10f, 0.8f)
			: new Color(0.04f, 0.05f, 0.07f, 0.55f);
		cardStyle.BorderColor = isAccentCard
			? UIStyle.ColorGold
			: new Color(0.55f, 0.45f, 0.32f, 0.4f);
		cardStyle.SetBorderWidthAll(isAccentCard ? 2 : 1);
		cardStyle.SetCornerRadiusAll(6);
		cardStyle.ContentMarginLeft = 14;
		cardStyle.ContentMarginRight = 14;
		cardStyle.ContentMarginTop = 12;
		cardStyle.ContentMarginBottom = 12;
		card.AddThemeStyleboxOverride("panel", cardStyle);

		var innerVBox = new VBoxContainer();
		innerVBox.Name = "InnerVBox_VBoxContainer";
		innerVBox.AddThemeConstantOverride("separation", 8);

		int insertIndex = titleLabel.GetIndex();

		parent.RemoveChild(titleLabel);
		parent.RemoveChild(contentControl);

		innerVBox.AddChild(titleLabel);
		innerVBox.AddChild(contentControl);
		card.AddChild(innerVBox);

		parent.AddChild(card);
		parent.MoveChild(card, insertIndex);

		AttachHoverAnimation(card, 1.01f, isAccentCard ? UIStyle.ColorGold : UIStyle.ColorBronze, isAccentCard ? UIStyle.ColorGold : new Color(0.55f, 0.45f, 0.32f, 0.4f));
	}

	private void AttachHoverAnimation(Control node, float targetScale = 1.03f, Color? hoverBorderColor = null, Color? defaultBorderColor = null)
	{
		if (node == null) return;

		node.MouseEntered += () =>
		{
			node.PivotOffset = node.Size / 2.0f;
			var tween = node.CreateTween().SetParallel(true);
			tween.TweenProperty(node, "scale", new Vector2(targetScale, targetScale), 0.12);

			if (hoverBorderColor.HasValue && node.HasThemeStyleboxOverride("panel"))
			{
				var sb = node.GetThemeStylebox("panel") as StyleBoxFlat;
				if (sb != null)
				{
					tween.TweenProperty(sb, "border_color", hoverBorderColor.Value, 0.12);
				}
			}

			GameContext.UserInterface?.PlayHoverSound();
		};

		node.MouseExited += () =>
		{
			var tween = node.CreateTween().SetParallel(true);
			tween.TweenProperty(node, "scale", Vector2.One, 0.12);

			if (defaultBorderColor.HasValue && node.HasThemeStyleboxOverride("panel"))
			{
				var sb = node.GetThemeStylebox("panel") as StyleBoxFlat;
				if (sb != null)
				{
					tween.TweenProperty(sb, "border_color", defaultBorderColor.Value, 0.12);
				}
			}
		};
	}

	// Called from the download button's handler, so it stays void. The awaited work moved into a Task-returning
	// method: the download had no catch at all, so a fault from the registry client abandoned _isDownloading = true
	// and left the button disabled with nothing in the log to explain it.
}