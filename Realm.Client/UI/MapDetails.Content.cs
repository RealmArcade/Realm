// Filling the page from the map data: Workshop facts, votes, the tech-info table, and the card wrapper plus
// hover animation that seat each group.

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

		// A map's own features were a realm-portal field; what Steam states about the item is drawn in the
		// ratings and tech rows instead, so this panel states the one thing Steam is authoritative about here.
		if (_mapData == null) return;

		CreateFeatureRow(TranslationServer.Translate("Steam Workshop"), _mapData.WorkshopId != 0);
		CreateFeatureRow(TranslationServer.Translate("Community rated"), _mapData.VotesUp + _mapData.VotesDown > 0);
		CreateFeatureRow(TranslationServer.Translate("Local map"), _mapData.IsLocalMap);
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
		foreach (Node child in _starsRow1.GetChildren()) child.QueueFree();
		foreach (Node child in _starsRow2.GetChildren()) child.QueueFree();
		foreach (Node child in _starsRow3.GetChildren()) child.QueueFree();

		_starsRow1.Visible = false;
		_starsRow2.Visible = false;
		_starsRow3.Visible = false;

		long totalVotes = _mapData.VotesUp + _mapData.VotesDown;

		_votesRow1.Text = totalVotes > 0
			? string.Format(TranslationServer.Translate("{0}% positive — {1} votes"), _mapData.PositivePercent, totalVotes)
			: TranslationServer.Translate("No Steam Workshop votes yet");
		_votesRow1.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow1.AddThemeFontSizeOverride("font_size", 12);

		_votesRow2.Text = string.Format(TranslationServer.Translate("{0} up, {1} down"), _mapData.VotesUp, _mapData.VotesDown);
		_votesRow2.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		_votesRow2.AddThemeFontSizeOverride("font_size", 12);

		string badge = MapTrustPolicy.DescribeBadge(_mapData.Trust);
		_votesRow3.Text = badge.Length > 0
			? $"{badge}: {MapTrustPolicy.DescribeReason(_mapData.Trust.Reason)}"
			: TranslationServer.Translate("Trusted by the Steam Workshop");
		_votesRow3.AddThemeColorOverride("font_color", _mapData.Trust.IsTrusted ? UIStyle.ColorGold : UIStyle.ColorGoldDull);
		_votesRow3.AddThemeFontSizeOverride("font_size", 12);
	}

	private void PopulateGameplayStats()
	{
		foreach (Node child in _gameplayStatsContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (_mapData == null) return;

		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/gold_g.webp",
			string.Format(TranslationServer.Translate("POSITIVE: {0}%"), _mapData.PositivePercent));
		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/alliance_flag.webp",
			string.Format(TranslationServer.Translate("VOTES: {0}"), _mapData.VotesUp + _mapData.VotesDown));
		AddStatRow(_gameplayStatsContainer, "res://Assets/UI/clock.webp",
			string.Format(TranslationServer.Translate("LAST UPDATED: {0}"), _mapData.UpdatedOn?.ToString("yyyy-MM-dd") ?? TranslationServer.Translate("unknown")));
	}

	private void PopulateTechInfo()
	{
		foreach (Node child in _techInfoContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (_mapData == null) return;

		AddStatRow(_techInfoContainer, "res://Assets/UI/wood_logs.webp", $"FILE SIZE: {_mapData.FileSize}");
		AddStatRow(_techInfoContainer, "res://Assets/UI/gold_g.webp", $"ENGINE VERSION: {_mapData.EngineVersion}");
		AddStatRow(_techInfoContainer, "res://Assets/UI/battle_shield.webp",
			string.Format(TranslationServer.Translate("WORKSHOP ITEM: {0}"), _mapData.WorkshopId != 0 ? _mapData.WorkshopId.ToString() : TranslationServer.Translate("not published")));
		AddStatRow(_techInfoContainer, "res://Assets/UI/battle_axe.webp", $"VERSION: {_mapData.Version}");
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