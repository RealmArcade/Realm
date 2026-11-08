// Extracted from UIStyle.cs (task 7 role split).
using System;
using System.Collections.Generic;
using Godot;
using SkiaSharp;
using Realm.Client.Utils;
using Realm.Client.Services;

namespace Realm.Client.UI;

public static partial class UIStyle
{
	public static StyleBox CreateBgTexture(string path)
	{
		var texture = TrimmedTextureLoader.LoadTrimmedTexture(path);
		var style = new StyleBoxTexture();
		style.Texture = texture;
		return style;
	}

	public static StyleBox CreateBgGradient()
	{
		return CreateBgTexture("res://Assets/UI/menu_background_with_frame.webp");
	}

	public static StyleBox CreateLobbyRoomBg()
	{
		var tex = LoadTextureWithFallback("res://Assets/UI/LobbyRoom_background.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			return style;
		}
		return CreateBgGradient();
	}

	public static StyleBox CreateGameStartingPanel()
	{
		var tex = LoadTextureWithFallback("res://Assets/UI/GameStarting_Panel.webp");
		if (tex == null)
		{
			tex = LoadTextureWithFallback("res://Assets/UI/GameStarting_Panel.webp");
		}
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			return style;
		}
		return CreateStonePanel(true);
	}

	public static StyleBox CreateCustomMatchBg()
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/custom_match_bg.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			return style;
		}

		return CreateBgGradient();
	}

	public static StyleBox CreateCreatorDiscoveryBg()
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/creator_discovery_bg.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			return style;
		}

		return CreateBgGradient();
	}

	public static StyleBox CreateGameOverBg()
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/GameOver_Background.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			return style;
		}
		return CreateBgGradient();
	}

	public static StyleBox CreateGameOverMainPanel()
	{
		var tex = LoadTextureWithFallback("res://Assets/UI/GameOver_main_panel.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;

			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;

			style.ContentMarginLeft = 175;
			style.ContentMarginRight = 170;
			style.ContentMarginTop = 85;
			style.ContentMarginBottom = 70;

			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;

			return style;
		}
		return CreateStonePanel();
	}

	public static StyleBox CreateCreatorDiscoveryPanelStyle()
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/creator_discovery_panel.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;

			style.TextureMarginLeft = 4;
			style.TextureMarginRight = 4;
			style.TextureMarginTop = 4;
			style.TextureMarginBottom = 4;

			style.ContentMarginLeft = 65;
			style.ContentMarginRight = 65;
			style.ContentMarginTop = 50;
			style.ContentMarginBottom = 50;

			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;

			return style;
		}

		return CreateBackdropPanel();
	}

	public static StyleBox CreateCreatorDiscoveryTabButtonStyle(bool isActive, bool isHover = false, bool isPressed = false)
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/creator_discovery_button_option.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;

			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;

			style.ContentMarginLeft = 20;
			style.ContentMarginRight = 20;
			style.ContentMarginTop = 10;
			style.ContentMarginBottom = 10;

			if (isActive)
			{
				style.ModulateColor = isPressed
					? new Color(0.85f, 0.85f, 0.9f)
					: (isHover ? new Color(1.18f, 1.18f, 1.25f) : Colors.White);
			}
			else
			{
				style.ModulateColor = isHover
					? new Color(0.85f, 0.85f, 0.88f, 0.9f)
					: new Color(0.6f, 0.6f, 0.65f, 0.75f);
			}

			return style;
		}

		return isPressed ? CreateButtonPressed() : (isHover ? CreateButtonHover() : CreateButtonNormal());
	}

	public static StyleBox CreateCreatorDiscoveryMapFrameStyle()
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/creator_discovery_map.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;

			style.TextureMarginLeft = 24;
			style.TextureMarginRight = 24;
			style.TextureMarginTop = 8;
			style.TextureMarginBottom = 8;

			style.ContentMarginLeft = 20;
			style.ContentMarginRight = 20;
			style.ContentMarginTop = 14;
			style.ContentMarginBottom = 14;

			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;

			return style;
		}

		return CreateStonePanel(true);
	}

	public static StyleBox CreateAgreementPanel()
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_agreement.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 32;
			style.TextureMarginRight = 32;
			style.TextureMarginTop = 32;
			style.TextureMarginBottom = 32;
			style.ContentMarginLeft = 35;
			style.ContentMarginRight = 35;
			style.ContentMarginTop = 35;
			style.ContentMarginBottom = 35;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
			return style;
		}

		return CreateStonePanel(true);
	}

	public static StyleBox CreateAssetsImporterPanel()
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_assets_importer.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 140;
			style.TextureMarginRight = 140;
			style.TextureMarginTop = 140;
			style.TextureMarginBottom = 140;
			style.ContentMarginLeft = 40;
			style.ContentMarginRight = 40;
			style.ContentMarginTop = 40;
			style.ContentMarginBottom = 40;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
			return style;
		}

		return CreateStonePanel(true);
	}

	public static StyleBox CreateMapEditorManualPanel()
	{
		return CreateTexturedPanel("res://Assets/UI/map_editor_manual.webp", 140, true);
	}

	public static StyleBox CreateConfirmationPanel()
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_test.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 80;
			style.TextureMarginRight = 80;
			style.TextureMarginTop = 80;
			style.TextureMarginBottom = 80;
			style.ContentMarginLeft = 30;
			style.ContentMarginRight = 30;
			style.ContentMarginTop = 30;
			style.ContentMarginBottom = 30;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
			return style;
		}

		return CreateStonePanel(true);
	}

	public static StyleBox CreateMapStorageMainPanelStyle()
	{
		var tex = LoadTextureWithFallback("res://Assets/UI/map_storage_main_panel.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 24;
			style.TextureMarginRight = 24;
			style.TextureMarginTop = 24;
			style.TextureMarginBottom = 24;
			style.ContentMarginLeft = 32;
			style.ContentMarginRight = 32;
			style.ContentMarginTop = 28;
			style.ContentMarginBottom = 28;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
			return style;
		}
		return CreateStonePanel(true);
	}

	public static StyleBox CreateMapStorageFilterPanelStyle()
	{
		var tex = LoadTextureWithFallback("res://Assets/UI/map_storage_filter_panel.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.ContentMarginLeft = 30;
			style.ContentMarginRight = 30;
			style.ContentMarginTop = 20;
			style.ContentMarginBottom = 30;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
			return style;
		}
		return CreateStonePanel(true);
	}

	public static StyleBox CreatePublishPanel()
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_publish.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 140;
			style.TextureMarginRight = 140;
			style.TextureMarginTop = 140;
			style.TextureMarginBottom = 140;
			style.ContentMarginLeft = 45;
			style.ContentMarginRight = 45;
			style.ContentMarginTop = 45;
			style.ContentMarginBottom = 45;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
			return style;
		}
		return CreateStonePanel(true);
	}

		private static StyleBox CreateTexturedPanel(string texturePath, float margin, bool useTrimmedLoader = false)
	{
		Texture2D? tex = useTrimmedLoader ? TrimmedTextureLoader.LoadTrimmedTexture(texturePath) : LoadTextureWithFallback(texturePath);
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = margin;
			style.TextureMarginRight = margin;
			style.TextureMarginTop = margin;
			style.TextureMarginBottom = margin;
			style.ContentMarginLeft = 45;
			style.ContentMarginRight = 45;
			style.ContentMarginTop = 45;
			style.ContentMarginBottom = 45;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
			return style;
		}
		return CreateStonePanel(true);
	}

	public static StyleBox CreateUsabilitySettingsPanel()
	{
		return CreateTexturedPanel("res://Assets/UI/map_editor_usability_settings.webp", 80);
	}

	public static StyleBox CreateRegisterProfilePanel()
	{
		return CreateTexturedPanel("res://Assets/UI/map_editor_register_profile.webp", 80, true);
	}

	public static StyleBox CreatePlacedObjectsPanel()
	{
		return CreateTexturedPanel("res://Assets/UI/map_editor_placed_objects.webp", 80);
	}

	public static StyleBox CreateAuthorSignaturePanel()
	{
		return CreateTexturedPanel("res://Assets/UI/map_editor_author_signature.webp", 80);
	}

	public static StyleBox CreatePublishMapInstructionsPanel()
	{
		return CreateTexturedPanel("res://Assets/UI/map_editor_publish.webp", 140);
	}

	/// <summary>Clears a panel's plate so the artwork behind it shows through instead.</summary>
	public static void ClearPanelBackground(Control? panel)
	{
		if (panel == null) return;

		panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
	}

	/// <summary>
	/// A filled card traced with a border, the shape every list row and section block on a detail screen takes. The
	/// colours and the border width are what change between an ordinary row and an accented one, so they are arguments.
	/// </summary>
	public static StyleBoxFlat CreateCardPlate(Color bgColor, Color borderColor, int borderWidth, int cornerRadius,
		int left = 8, int right = 8, int top = 8, int bottom = 8)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bgColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(borderWidth);
		style.SetCornerRadiusAll(cornerRadius);
		style.ContentMarginLeft = left;
		style.ContentMarginRight = right;
		style.ContentMarginTop = top;
		style.ContentMarginBottom = bottom;
		return style;
	}

	/// <summary>
	/// A transparent plate traced with a border, which is how the in-game HUD marks the selected unit button. The fill
	/// is optional because most of these plates are only an outline over the button's own artwork.
	/// </summary>
	public static StyleBoxFlat CreateBorderPlate(Color borderColor, int borderWidth, Color? bgColor = null)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bgColor ?? new Color(0, 0, 0, 0);
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(borderWidth);
		return style;
	}

	public static StyleBox CreateBackdropPanel()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.06f, 0.07f, 0.09f, 0.82f);
		style.BorderColor = new Color(0.75f, 0.62f, 0.35f, 0.35f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(6);
		style.ContentMarginLeft = 16;
		style.ContentMarginRight = 16;
		style.ContentMarginTop = 12;
		style.ContentMarginBottom = 12;
		return style;
	}

	public static StyleBox CreateHairlineSeparator()
	{
		return new StyleBoxLine
		{
			Color = new Color(ColorGoldDull.R, ColorGoldDull.G, ColorGoldDull.B, 0.3f),
			Thickness = 1
		};
	}

	public static StyleBox CreateStonePanel(bool isLight = false)
	{
		var style = new StyleBoxTexture();
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture(StonePanelTexturePath);

		style.TextureMarginLeft = StonePanelFrameThickness;
		style.TextureMarginRight = StonePanelFrameThickness;
		style.TextureMarginTop = StonePanelFrameThickness;
		style.TextureMarginBottom = StonePanelFrameThickness;

		if (isLight)
		{
			style.ContentMarginLeft = 20;
			style.ContentMarginRight = 20;
			style.ContentMarginTop = 10;
			style.ContentMarginBottom = 10;

			style.ModulateColor = new Color(1.0f, 1.0f, 1.02f, 0.98f);
		}
		else
		{
			style.ContentMarginLeft = 24;
			style.ContentMarginRight = 24;
			style.ContentMarginTop = 14;
			style.ContentMarginBottom = 14;

			style.ModulateColor = new Color(0.82f, 0.82f, 0.86f, 0.98f);
		}

		return style;
	}

	public static StyleBox CreateReplayPanel()
	{
		var texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/replays_panel.webp");
		if (texture != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = texture;

			float marginX = texture.GetWidth() * 0.08f;
			float marginY = texture.GetHeight() * 0.08f;

			style.TextureMarginLeft = marginX;
			style.TextureMarginRight = marginX;
			style.TextureMarginTop = marginY;
			style.TextureMarginBottom = marginY;

			style.ContentMarginLeft = marginX * 1.3f;
			style.ContentMarginRight = marginX * 1.3f;
			style.ContentMarginTop = marginY * 1.3f;
			style.ContentMarginBottom = marginY * 1.3f;

			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;

			return style;
		}

		return CreateStonePanel(false);
	}

	public static StyleBox CreateMatchViewBottomPanel()
	{
		var texture = LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_PanelBottom.webp");
		if (texture == null)
		{
			texture = LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_PanelBottom.webp");
		}
		if (texture != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = texture;
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
			style.ContentMarginLeft = 0;
			style.ContentMarginRight = 0;
			style.ContentMarginTop = 0;
			style.ContentMarginBottom = 0;
			return style;
		}
		return CreateStonePanel(false);
	}

	public static StyleBox CreateHotkeyReferencePanelStyle()
	{
		var texture = LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_HotkeyReference.webp");
		if (texture != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = texture;
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.ContentMarginLeft = 90;
			style.ContentMarginRight = 90;
			style.ContentMarginTop = 88;
			style.ContentMarginBottom = 82;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
			return style;
		}

		return CreateStonePanel(true);
	}

	public static StyleBox CreateLobbyRoomMainPanel()
	{
		var texture = LoadTextureWithFallback("res://Assets/UI/LobbyRoom_MainPanel.webp");
		if (texture != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = texture;

			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;

			style.ContentMarginLeft = 100;
			style.ContentMarginRight = 100;
			style.ContentMarginTop = 18;
			style.ContentMarginBottom = 45;

			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;

			return style;
		}
		return CreateStonePanel(true);
	}

	public static StyleBox CreateLobbyRoomSecondPanel()
	{
		var texture = LoadTextureWithFallback("res://Assets/UI/LobbyRoom_SecondPanel.webp");
		if (texture != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = texture;

			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;

			style.ContentMarginLeft = 95;
			style.ContentMarginRight = 95;
			style.ContentMarginTop = 18;
			style.ContentMarginBottom = 20;

			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;

			return style;
		}
		return CreateStonePanel(true);
	}

	public static StyleBox CreateLobbyRoomChatPanel()
	{
		var texture = LoadTextureWithFallback("res://Assets/UI/LobbyRoom_ChatPanel.webp");
		if (texture != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = texture;

			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;

			style.ContentMarginLeft = 35;
			style.ContentMarginRight = 35;
			style.ContentMarginTop = 24;
			style.ContentMarginBottom = 10;

			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;

			return style;
		}
		return CreateStonePanel(true);
	}

	public static StyleBox CreateCustomMatchCardPanel()
	{
		var style = new StyleBoxEmpty();
		style.ContentMarginLeft = 40;
		style.ContentMarginRight = 40;
		style.ContentMarginTop = 40;
		style.ContentMarginBottom = 40;
		return style;
	}

	public static StyleBox CreatePillarPanel(bool isLeft)
	{
		return new StyleBoxEmpty();
	}

	public static StyleBox CreateHUDPillarPanel(bool isLeft)
	{
		var style = new StyleBoxTexture();
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/hud_stone_pillar.webp");
		style.TextureMarginLeft = 40;
		style.TextureMarginRight = 40;
		style.TextureMarginTop = 150;
		style.TextureMarginBottom = 150;
		return style;
	}

	public static StyleBox CreateEntranceBgTexture()
	{
		var style = new StyleBoxTexture();
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/menu_entrance_background.webp");
		return style;
	}

	public static StyleBox CreateLightStonePanel()
	{
		var style = new StyleBoxTexture();
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/stone_panel_background.webp");
		style.TextureMarginLeft = 60;
		style.TextureMarginRight = 60;
		style.TextureMarginTop = 60;
		style.TextureMarginBottom = 60;
		style.ContentMarginLeft = 32;
		style.ContentMarginRight = 32;
		style.ContentMarginTop = 40;
		style.ContentMarginBottom = 32;
		return style;
	}

	public static StyleBox CreateLightInnerPanel()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.35f, 0.33f, 0.30f, 0.7f);
		style.BorderColor = new Color(0.25f, 0.23f, 0.20f, 0.6f);
		style.BorderWidthTop = 2;
		style.BorderWidthBottom = 1;
		style.BorderWidthLeft = 2;
		style.BorderWidthRight = 1;
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 14;
		style.ContentMarginRight = 14;
		style.ContentMarginTop = 10;
		style.ContentMarginBottom = 10;
		return style;
	}

	public static StyleBox CreateActiveInnerPanel()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.20f, 0.35f, 0.45f, 0.85f);
		style.BorderColor = ColorCyanGlow;
		style.BorderWidthTop = 2;
		style.BorderWidthBottom = 2;
		style.BorderWidthLeft = 2;
		style.BorderWidthRight = 2;
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 14;
		style.ContentMarginRight = 14;
		style.ContentMarginTop = 10;
		style.ContentMarginBottom = 10;
		return style;
	}

	public static StyleBox CreateLightTitleBadge()
	{
		var style = new StyleBoxTexture();
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/title_banner.webp");
		style.TextureMarginLeft = 140;
		style.TextureMarginRight = 140;
		style.TextureMarginTop = 20;
		style.TextureMarginBottom = 20;
		style.ContentMarginLeft = 45;
		style.ContentMarginRight = 45;
		style.ContentMarginTop = 14;
		style.ContentMarginBottom = 14;
		return style;
	}
}
