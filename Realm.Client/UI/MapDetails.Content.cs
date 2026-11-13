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
		ClearNodeChildren(_featuresList);

		if (_mapData == null) return;

		var features = _mapData.Features ?? Array.Empty<string>();
		if (features.Length == 0)
		{
			var emptyLbl = new Label();
			emptyLbl.Name = "NoFeaturesLbl_Label";
			emptyLbl.Text = TranslationServer.Translate("No tags or features listed.");
			emptyLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			emptyLbl.CustomMinimumSize = new Vector2(180, 0);
			emptyLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			emptyLbl.AddThemeColorOverride("font_color", new Color(0.65f, 0.68f, 0.74f));
			emptyLbl.AddThemeFontSizeOverride("font_size", 13);
			_featuresList.AddChild(emptyLbl);
			return;
		}

		foreach (var feat in features)
		{
			if (!string.IsNullOrWhiteSpace(feat))
			{
				CreateFeatureRow(feat.Trim(), true);
			}
		}
	}

	private void CreateFeatureRow(string feat, bool isActive)
	{
		var rowPanel = new PanelContainer();
		rowPanel.Name = "RowPanel_PanelContainer";
		rowPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCardPlate(
			isActive ? new Color(0.03f, 0.05f, 0.08f, 0.65f) : new Color(0.02f, 0.03f, 0.04f, 0.35f),
			isActive ? new Color(0.15f, 0.65f, 1.0f, 0.5f) : new Color(0.35f, 0.3f, 0.22f, 0.25f),
			1, 6, 12, 12, 8, 8));

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
		if (_mapData == null) return;

		if (_mapData.TotalReviews > 0)
		{
			int filledStars = Math.Clamp((int)Math.Round(_mapData.RatingStars), 0, 5);
			FillStarsRow(_starsRow1, filledStars);
			_votesRow1.Visible = true;
			_votesRow1.Text = $"{_mapData.RatingStars:F1} / 5.0  ({string.Format(TranslationServer.Translate("{0} reviews"), _mapData.TotalReviews)})";
			_votesRow1.AddThemeColorOverride("font_color", new Color(0.90f, 0.90f, 0.95f));
			_votesRow1.AddThemeFontSizeOverride("font_size", 12);

			_starsRow2.Visible = false;
			_votesRow2.Visible = true;
			_votesRow2.Text = string.Format(TranslationServer.Translate("{0} verified positive reviews"), _mapData.VerifiedGoodReviews);
			_votesRow2.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			_votesRow2.AddThemeFontSizeOverride("font_size", 12);

			_starsRow3.Visible = false;
			_votesRow3.Visible = false;
			return;
		}

		bool hasLegacyCustomRating = !string.IsNullOrWhiteSpace(_mapData.Votes5Star) && _mapData.Votes5Star != "0" && _mapData.RatingStars > 0;
		if (hasLegacyCustomRating)
		{
			int filledStars = Math.Clamp((int)Math.Round(_mapData.RatingStars), 0, 5);
			FillStarsRow(_starsRow1, filledStars);
			_votesRow1.Visible = true;
			_votesRow1.Text = $"{_mapData.Votes5Star}  •  {_mapData.AvgRating}";
			_votesRow1.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
			_votesRow1.AddThemeFontSizeOverride("font_size", 12);
			_starsRow2.Visible = false;
			_votesRow2.Visible = false;
			_starsRow3.Visible = false;
			_votesRow3.Visible = false;
			return;
		}

		FillStarsRow(_starsRow1, 0);
		_votesRow1.Visible = true;
		_votesRow1.Text = TranslationServer.Translate("No player reviews yet");
		_votesRow1.AddThemeColorOverride("font_color", new Color(0.70f, 0.72f, 0.78f));
		_votesRow1.AddThemeFontSizeOverride("font_size", 12);
		_starsRow2.Visible = false;
		_votesRow2.Visible = false;
		_starsRow3.Visible = false;
		_votesRow3.Visible = false;
	}

	private void FillStarsRow(HBoxContainer container, int filledStars)
	{
		ClearNodeChildren(container);

		for (int i = 0; i < 5; i++)
		{
			var star = new Label();
			star.Name = "Star_Label";
			star.Text = i < filledStars ? "★" : "☆";
			star.AddThemeColorOverride("font_color", i < filledStars ? UIStyle.ColorGold : new Color(0.45f, 0.45f, 0.52f));
			star.AddThemeFontSizeOverride("font_size", 16);
			container.AddChild(star);
		}
	}

	private void PopulateAwards()
	{
		ClearNodeChildren(_awardsContainer);

		if (_mapData?.Awards == null || _mapData.Awards.Length == 0)
		{
			var emptyLbl = new Label();
			emptyLbl.Name = "NoAwardsLbl_Label";
			emptyLbl.Text = TranslationServer.Translate("No awards earned yet.");
			emptyLbl.AddThemeColorOverride("font_color", new Color(0.65f, 0.68f, 0.74f));
			emptyLbl.AddThemeFontSizeOverride("font_size", 12);
			_awardsContainer.AddChild(emptyLbl);
			return;
		}

		foreach (var awardPath in _mapData.Awards)
		{
			Texture2D? tex = LoadTextureSafe(awardPath);
			if (tex == null)
			{
				continue;
			}

			var badgePanel = new PanelContainer();
			badgePanel.Name = "BadgePanel_PanelContainer";
			badgePanel.TooltipText = GetAwardTooltip(awardPath);
			badgePanel.AddThemeStyleboxOverride("panel",
				UIStyle.CreateCardPlate(new Color(0.08f, 0.09f, 0.12f, 0.85f), UIStyle.ColorGoldDull, 1, 6));

			var rect = new TextureRect();
			rect.Name = "Rect_TextureRect";
			rect.CustomMinimumSize = new Vector2(36, 36);
			rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			rect.Texture = tex;

			badgePanel.AddChild(rect);
			AttachHoverAnimation(badgePanel, 1.10f, UIStyle.ColorGold, UIStyle.ColorGoldDull);
			_awardsContainer.AddChild(badgePanel);
		}

		if (_awardsContainer.GetChildCount() == 0)
		{
			var emptyLbl = new Label();
			emptyLbl.Name = "NoAwardsLbl_Label";
			emptyLbl.Text = TranslationServer.Translate("No awards earned yet.");
			emptyLbl.AddThemeColorOverride("font_color", new Color(0.65f, 0.68f, 0.74f));
			emptyLbl.AddThemeFontSizeOverride("font_size", 12);
			_awardsContainer.AddChild(emptyLbl);
		}
	}

	private void PopulateGameplayStats()
	{
		ClearNodeChildren(_gameplayStatsContainer);

		string playtimeDisplay = string.IsNullOrWhiteSpace(_mapData.AvgPlaytime)
			? TranslationServer.Translate("Not recorded")
			: _mapData.AvgPlaytime;
		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/clock.webp", $"{TranslationServer.Translate("AVG PLAYTIME")}: {playtimeDisplay}");

		string matchesDisplay = _mapData.GamesPlayed > 0
			? $"{_mapData.GamesPlayed:N0}"
			: TranslationServer.Translate("No matches recorded");
		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/alliance_flag.webp", $"{TranslationServer.Translate("MATCHES PLAYED")}: {matchesDisplay}");

		if (_mapData.TotalReviews > 0)
		{
			AddStatRow(_gameplayStatsContainer, "res://Assets/UI/victory_flag.webp", $"{TranslationServer.Translate("VERIFIED POSITIVE")}: {_mapData.VerifiedGoodReviews:N0} / {_mapData.TotalReviews:N0}");
		}
	}

	private void PopulateTechInfo()
	{
		ClearNodeChildren(_techInfoContainer);

		AddStatRow(_techInfoContainer, "res://Assets/UI/wood_logs.webp", $"{TranslationServer.Translate("FILE SIZE")}: {_mapData.FileSize}");

		if (!string.IsNullOrWhiteSpace(_mapData.EngineVersion))
		{
			AddStatRow(_techInfoContainer, "res://Assets/UI/gold_g.webp", $"{TranslationServer.Translate("ENGINE VERSION")}: {_mapData.EngineVersion}");
		}

		if (!string.IsNullOrWhiteSpace(_mapData.MaxPlayers))
		{
			AddStatRow(_techInfoContainer, "res://Assets/UI/battle_shield.webp", $"{TranslationServer.Translate("MAX PLAYERS")}: {_mapData.MaxPlayers}");
		}

		AddStatRow(_techInfoContainer, "res://Assets/UI/battle_axe.webp", $"{TranslationServer.Translate("GENRE")}: {_mapData.Genre}");
	}

	private static string GetAwardTooltip(string awardPath)
	{
		if (awardPath.Contains("victory_flag", StringComparison.OrdinalIgnoreCase))
			return TranslationServer.Translate("Greenlit Map");
		if (awardPath.Contains("gold_coin", StringComparison.OrdinalIgnoreCase))
			return TranslationServer.Translate("Top Rated (4.5+ Stars)");
		if (awardPath.Contains("battle_shield", StringComparison.OrdinalIgnoreCase))
			return TranslationServer.Translate("Community Verified Reviews");
		if (awardPath.Contains("battle_axe", StringComparison.OrdinalIgnoreCase))
			return TranslationServer.Translate("Popular Battleground");
		return TranslationServer.Translate("Community Award");
	}

	private static void ClearNodeChildren(Node container)
	{
		foreach (Node child in container.GetChildren())
		{
			container.RemoveChild(child);
			child.QueueFree();
		}
	}

	private void AddStatRow(VBoxContainer container, string iconPath, string text)
	{
		var rowPanel = new PanelContainer();
		rowPanel.Name = "RowPanel_PanelContainer";
		rowPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateCardPlate(
			new Color(0.02f, 0.03f, 0.04f, 0.5f), new Color(0.45f, 0.38f, 0.25f, 0.35f), 1, 4, 10, 10, 6, 6));

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
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateCardPlate(
			isAccentCard ? new Color(0.06f, 0.07f, 0.10f, 0.8f) : new Color(0.04f, 0.05f, 0.07f, 0.55f),
			isAccentCard ? UIStyle.ColorGold : new Color(0.55f, 0.45f, 0.32f, 0.4f),
			isAccentCard ? 2 : 1, 6, 14, 14, 12, 12));

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