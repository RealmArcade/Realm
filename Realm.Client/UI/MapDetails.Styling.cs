// The published-map page is styled as a whole rather than per widget: one pass wraps the sections,
// applies the panel styleboxes, title metrics and map frame, then dresses the carousel arrows.

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
	private void ApplyStyles()
	{
		ApplyBackgroundStyles();
		ApplyPanelStyles();
		ApplyWrapperStyles();
		ApplyTextStyles();

		UIStyle.ApplyBackButton(_backButton, () =>
		{
			GameScreen target = GameContext.UserInterface?.MapDetailsReturnScreen ?? GameScreen.MapDiscovery;
			GameContext.UserInterface?.TransitionTo(target);
		});

		UIStyle.ApplyButtonStyleBoxes(_downloadButton);
		_downloadButton.AddThemeStyleboxOverride("disabled", UIStyle.CreateButtonPressed());
		_downloadSubtitle.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_downloadSubtitle.AddThemeFontSizeOverride("font_size", 12);

		ApplyMapFrame();
		ApplyCarouselButtons();

		UIStyle.ApplySlider(_carouselSlider);
	}

	private void ApplyBackgroundStyles()
	{
		Texture2D? bgTexture = null;
		string[] bgPaths = { "res://Assets/UI/map_details_bg.webp", "res://Assets/UI/map_discovery_background.webp" };

		foreach (var path in bgPaths)
		{
			if (ResourceLoader.Exists(path))
			{
				bgTexture = TrimmedTextureLoader.LoadTrimmedTexture(path);
				if (bgTexture != null) break;
			}
		}

		if (bgTexture != null)
		{
			var style = new StyleBoxTexture { Texture = bgTexture };
			_bgPanel.AddThemeStyleboxOverride("panel", style);
			_bgPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		}

		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
	}

	private void ApplyPanelStyles()
	{
		ApplyStyleBox(_headerPanel, "res://Assets/UI/map_details_header.webp", 0, 0, 0, 0);
		ApplyStyleBox(_descriptionPanel, "res://Assets/UI/map_details_panel_description.webp", 52, 52, 38, 42);
		ApplyStyleBox(_featuresPanel, "res://Assets/UI/map_details_panel_features.webp", 40, 40, 38, 35);
		ApplyStyleBox(_statsPanel, "res://Assets/UI/map_details_info_panel.webp", 45, 50, 48, 38);
	}

	private void ApplyStyleBox(Control panel, string path, int left, int right, int top, int bottom)
	{
		if (ResourceLoader.Exists(path))
		{
			var style = new StyleBoxTexture();
			style.Texture = TrimmedTextureLoader.LoadTrimmedTexture(path);
			style.ContentMarginLeft = left;
			style.ContentMarginRight = right;
			style.ContentMarginTop = top;
			style.ContentMarginBottom = bottom;
			panel.AddThemeStyleboxOverride("panel", style);
			panel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			panel.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		}
	}

	private void ApplyWrapperStyles()
	{
		var descVBox = _descriptionPanel.GetNode<VBoxContainer>("VBoxContainer");
		descVBox.AddThemeConstantOverride("separation", 16);
		WrapInPanelContainer(descVBox, "res://Assets/UI/map_details_panel_description.webp");

		var featuresVBox = _featuresPanel.GetNode<VBoxContainer>("VBoxContainer");
		if (ResourceLoader.Exists("res://Assets/UI/map_details_panel_features.webp"))
		{
			featuresVBox.AddThemeConstantOverride("separation", 16);
		}

		WrapInPanelContainer(featuresVBox, "res://Assets/UI/map_details_panel_features.webp");

		var statsVBox = _statsPanel.GetNode<VBoxContainer>("VBoxContainer");
		statsVBox.AddThemeConstantOverride("separation", 18);
		WrapInPanelContainer(statsVBox, "res://Assets/UI/map_details_info_panel.webp");
	}

	private void WrapInPanelContainer(Control content, string stylePath)
	{
		var wrapper = new PanelContainer();
		wrapper.Name = "Wrapper_PanelContainer";
		if (ResourceLoader.Exists(stylePath))
		{
			wrapper.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		}
		else
		{
			wrapper.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		}

		content.GetParent().AddChild(wrapper);
		content.GetParent().MoveChild(wrapper, content.GetIndex());
		content.GetParent().RemoveChild(content);
		wrapper.AddChild(content);
	}

	private void ApplyTextStyles()
	{
		ApplyTitleStyle(_descTitle, TranslationServer.Translate("MAP DESCRIPTION"), 15);
		_descText.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.98f));
		_descText.AddThemeFontSizeOverride("font_size", 13);
		_descText.AddThemeConstantOverride("line_spacing", 4);

		ApplyTitleStyle(_featuresTitle, TranslationServer.Translate("MAP FEATURES"), 16);
		ApplyTitleStyle(_statsTitle, TranslationServer.Translate("MAP INFO & STATS"), 20);
		ApplyTitleStyle(_ratingTitle, TranslationServer.Translate("RATINGS & COMMUNITY"), 13);
		ApplyTitleStyle(_awardsTitle, TranslationServer.Translate("AWARDS"), 13);
		ApplyTitleStyle(_gameplayTitle, TranslationServer.Translate("GAMEPLAY STATS"), 13);
		ApplyTitleStyle(_techTitle, TranslationServer.Translate("TECHNICAL INFO"), 13);

		var contentVBox = _statsPanel.GetNodeOrNull<VBoxContainer>("VBoxContainer/ScrollContainer/ContentVBox");
		if (contentVBox != null)
		{
			HideSeparators(contentVBox);
			WrapSectionInCard(contentVBox, _ratingTitle, contentVBox.GetNodeOrNull<GridContainer>("RatingGrid"), isAccentCard: true);
			WrapSectionInCard(contentVBox, _awardsTitle, _awardsContainer);
			WrapSectionInCard(contentVBox, _gameplayTitle, _gameplayStatsContainer);
			WrapSectionInCard(contentVBox, _techTitle, _techInfoContainer);
			contentVBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });
		}
	}

	private void ApplyTitleStyle(Label label, string text, int fontSize)
	{
		label.Text = text;
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		label.AddThemeFontSizeOverride("font_size", fontSize);
	}

	private void HideSeparators(Node container)
	{
		for (int i = 1; i <= 3; i++)
		{
			var sep = container.GetNodeOrNull<HSeparator>($"HSeparator{i}");
			if (sep != null) sep.Visible = false;
		}
	}

	private void ApplyMapFrame()
	{
		Texture2D? mapFrameTex = LoadTextureSafe("res://Assets/UI/map_details_map.webp");

		if (mapFrameTex == null)
		{
			_texLeftPeek.Modulate = new Color(0.3f, 0.3f, 0.3f, 0.7f);
			_texRightPeek.Modulate = new Color(0.3f, 0.3f, 0.3f, 0.7f);
			return;
		}

		_texLeftPeek.Visible = false;
		_texRightPeek.Visible = false;
		_texLeftPeek.Hide();
		_texRightPeek.Hide();

		var carouselPanel = GetNode<Control>("CarouselPanel");
		carouselPanel.GetNodeOrNull<TextureRect>("MapFrameOverlay")?.QueueFree();

		var mapFrame = carouselPanel.GetNodeOrNull<PanelContainer>("MapFramePanel");
		if (mapFrame == null)
		{
			mapFrame = new PanelContainer { Name = "MapFramePanel" };
			carouselPanel.AddChild(mapFrame);
		}

		var frameStyle = new StyleBoxTexture { Texture = mapFrameTex, ContentMarginLeft = 36, ContentMarginRight = 36, ContentMarginTop = 32, ContentMarginBottom = 32 };
		mapFrame.AddThemeStyleboxOverride("panel", frameStyle);
		mapFrame.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		mapFrame.Position = new Vector2(170, 0);
		mapFrame.Size = new Vector2(650, 380);

		if (_texCenter.GetParent() != mapFrame)
		{
			_texCenter.GetParent()?.RemoveChild(_texCenter);
			mapFrame.AddChild(_texCenter);
		}

		_texCenter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_texCenter.SizeFlagsVertical = SizeFlags.ExpandFill;
		_texCenter.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_texCenter.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;

		carouselPanel.MoveChild(mapFrame, 0);
		carouselPanel.MoveChild(_btnLeftArrow, 1);
		carouselPanel.MoveChild(_btnRightArrow, 2);
		if (_carouselSlider != null) carouselPanel.MoveChild(_carouselSlider, 3);

		_btnLeftArrow.Position = new Vector2(130, 160);
		_btnRightArrow.Position = new Vector2(790, 160);
	}

	private void ApplyCarouselButtons()
	{
		Texture2D? arrowTex = LoadTextureSafe("res://Assets/UI/map_details_prev_next.webp");

		if (arrowTex != null)
		{
			ApplyImageCarouselButton(_btnLeftArrow, arrowTex, true);
			ApplyImageCarouselButton(_btnRightArrow, arrowTex, false);

			Texture2D? mapFrameTex = LoadTextureSafe("res://Assets/UI/map_details_map.webp");
			if (mapFrameTex != null)
			{
				_btnLeftArrow.Position = new Vector2(132, 155);
				_btnRightArrow.Position = new Vector2(818, 155);
			}
		}
		else
		{
			ApplyFlatCarouselButton(_btnLeftArrow, "◀");
			ApplyFlatCarouselButton(_btnRightArrow, "▶");
		}
	}

	private void ApplyImageCarouselButton(Button button, Texture2D texture, bool flipH)
	{
		button.Text = "";
		UIStyle.ApplyPlainButtonStates(button);
		button.Size = new Vector2(40, 70);

		var icon = button.GetNodeOrNull<TextureRect>("ArrowIcon");
		if (icon == null)
		{
			icon = new TextureRect { Name = "ArrowIcon", MouseFilter = MouseFilterEnum.Ignore };
			button.AddChild(icon);
		}

		icon.Texture = texture;
		icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		icon.FlipH = flipH;
		icon.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
	}

	private void ApplyFlatCarouselButton(Button button, string text)
	{
		UIStyle.ApplyFlatButton(button);
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(button, text, 24);
	}

}