using Godot;

public static class UIStyle
{

	public static readonly Color ColorBgDark = new Color(0.08f, 0.08f, 0.1f, 1.0f);       // Deep charcoal/midnight
	public static readonly Color ColorPanelBg = new Color(0.13f, 0.14f, 0.17f, 0.95f);    // Granite slate
	public static readonly Color ColorPanelLight = new Color(0.18f, 0.19f, 0.23f, 0.95f); // Lighter granite
	
	public static readonly Color ColorBronze = new Color(0.6f, 0.5f, 0.35f, 1.0f);        // Bronze border
	public static readonly Color ColorGold = new Color(0.95f, 0.82f, 0.55f, 1.0f);        // Bright gold text
	public static readonly Color ColorGoldDull = new Color(0.75f, 0.67f, 0.5f, 1.0f);      // Dull gold
	
	public static readonly Color ColorCyanGlow = new Color(0.15f, 0.65f, 1.0f, 1.0f);     // Runic cyan glow
	public static readonly Color ColorCyanGlowDim = new Color(0.15f, 0.65f, 1.0f, 0.4f);

	public static readonly Font FontNorseBold = GD.Load<Font>("res://Assets/UI/Norse-Bold.otf");
	public static readonly Font FontCinzelBold = GD.Load<Font>("res://Assets/UI/Cinzel-Bold.ttf");

	private static Texture2D? _emptyBlackTexture;
	public static Texture2D EmptyBlackTexture
	{
		get
		{
			if (_emptyBlackTexture == null)
			{
				var img = Image.CreateEmpty(512, 512, false, Image.Format.Rgba8);
				img.Fill(Colors.Black);
				_emptyBlackTexture = ImageTexture.CreateFromImage(img);
			}
			return _emptyBlackTexture;
		}
	}

	public static StyleBox CreateBgTexture(string path)
	{
		var texture = GD.Load<Texture2D>(path);
		var style = new StyleBoxTexture();
		style.Texture = texture;
		return style;
	}

	public static StyleBox CreateBgGradient()
	{
		return CreateBgTexture("res://Assets/UI/menu_background_with_frame.jpg");
	}

	public static StyleBox CreateCustomMatchBg()
	{
		var tex = GD.Load<Texture2D>("res://Assets/UI/custom_match_bg.jpg");
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
		var tex = GD.Load<Texture2D>("res://Assets/UI/creator_discovery_bg.jpg");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			return style;
		}
		return CreateBgGradient();
	}

	public static StyleBox CreateCreatorDiscoveryPanelStyle()
	{
		var tex = GD.Load<Texture2D>("res://Assets/UI/creator_discovery_panel.png");
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
		var tex = GD.Load<Texture2D>("res://Assets/UI/creator_discovery_button_option.png");
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
		var tex = GD.Load<Texture2D>("res://Assets/UI/creator_discovery_map.png");
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
		var tex = GD.Load<Texture2D>("res://Assets/UI/map_editor_agreement.png");
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
		var tex = GD.Load<Texture2D>("res://Assets/UI/map_editor_assets_importer.png");
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
		var tex = GD.Load<Texture2D>("res://Assets/UI/map_editor_manual.png");
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

	public static StyleBox CreateConfirmationPanel()
	{
		var tex = GD.Load<Texture2D>("res://Assets/UI/map_editor_test.png");
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
	public static StyleBox CreateBackdropPanel()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.6f);
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		style.ContentMarginLeft = 16;
		style.ContentMarginRight = 16;
		style.ContentMarginTop = 16;
		style.ContentMarginBottom = 16;
		return style;
	}

	public static StyleBox CreateStonePanel(bool isLight = false)
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/stone_slate_panel.jpg");
		
		if (isLight)
		{
			style.TextureMarginLeft = 32;
			style.TextureMarginRight = 32;
			style.TextureMarginTop = 32;
			style.TextureMarginBottom = 32;
			
			style.ContentMarginLeft = 20;
			style.ContentMarginRight = 20;
			style.ContentMarginTop = 10;
			style.ContentMarginBottom = 10;
			
			style.ModulateColor = new Color(0.9f, 0.9f, 0.93f, 0.95f); // Slightly lighter for sub-panels
		}
		else
		{
			style.TextureMarginLeft = 40;
			style.TextureMarginRight = 40;
			style.TextureMarginTop = 40;
			style.TextureMarginBottom = 40;
			
			style.ContentMarginLeft = 24;
			style.ContentMarginRight = 24;
			style.ContentMarginTop = 14;
			style.ContentMarginBottom = 14;
			
			style.ModulateColor = new Color(0.72f, 0.72f, 0.75f, 0.98f); // Darker base frame
		}
		return style;
	}

	public static StyleBox CreateReplayPanel()
	{
		var texture = GD.Load<Texture2D>("res://Assets/UI/replays_panel.png");
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
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/hud_stone_pillar.png");
		style.TextureMarginLeft = 40;
		style.TextureMarginRight = 40;
		style.TextureMarginTop = 150;
		style.TextureMarginBottom = 150;
		return style;
	}


	public static StyleBox CreateButtonNormal()
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/options_menu_button.png");
		style.TextureMarginLeft = 0;
		style.TextureMarginRight = 0;
		style.TextureMarginTop = 0;
		style.TextureMarginBottom = 0;
		
		style.ContentMarginLeft = 20;
		style.ContentMarginRight = 20;
		style.ContentMarginTop = 10;
		style.ContentMarginBottom = 10;
		
		style.ModulateColor = new Color(1.0f, 1.0f, 1.0f);
		return style;
	}

	public static StyleBox CreateButtonHover()
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/options_menu_button.png");
		style.TextureMarginLeft = 0;
		style.TextureMarginRight = 0;
		style.TextureMarginTop = 0;
		style.TextureMarginBottom = 0;
		
		style.ContentMarginLeft = 20;
		style.ContentMarginRight = 20;
		style.ContentMarginTop = 10;
		style.ContentMarginBottom = 10;
		
		style.ModulateColor = new Color(1.12f, 1.10f, 0.96f);
		return style;
	}

	public static StyleBox CreateButtonPressed()
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/options_menu_button.png");
		style.TextureMarginLeft = 0;
		style.TextureMarginRight = 0;
		style.TextureMarginTop = 0;
		style.TextureMarginBottom = 0;
		
		style.ContentMarginLeft = 20;
		style.ContentMarginRight = 20;
		style.ContentMarginTop = 10;
		style.ContentMarginBottom = 10;
		
		style.ModulateColor = new Color(0.85f, 0.82f, 0.75f);
		return style;
	}

	public static StyleBox CreateOptionButtonNormal()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.18f, 0.16f, 0.14f, 0.95f);
		style.BorderColor = new Color(0.42f, 0.36f, 0.26f, 0.85f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateOptionButtonHover()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.28f, 0.24f, 0.18f, 0.98f);
		style.BorderColor = ColorGold;
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateOptionButtonPressed()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.36f, 0.30f, 0.20f, 0.98f);
		style.BorderColor = ColorGold;
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateCustomLobbyStartGameButton(bool isHover = false, bool isPressed = false)
	{
		var tex = GD.Load<Texture2D>("res://Assets/UI/custom_lobby_start_game.png");
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
			
			if (isPressed)
			{
				style.ModulateColor = new Color(0.85f, 0.85f, 0.9f);
			}
			else if (isHover)
			{
				style.ModulateColor = new Color(1.18f, 1.18f, 1.25f);
			}
			else
			{
				style.ModulateColor = Colors.White;
			}
			return style;
		}
		return isPressed ? CreateButtonPressed() : (isHover ? CreateButtonHover() : CreateButtonNormal());
	}

	public static StyleBox CreateCustomLobbyBackButton(bool isHover = false, bool isPressed = false)
	{
		var tex = GD.Load<Texture2D>("res://Assets/UI/custom_lobby_back_button.png");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.ContentMarginLeft = 0;
			style.ContentMarginRight = 0;
			style.ContentMarginTop = 0;
			style.ContentMarginBottom = 0;
			
			if (isPressed)
			{
				style.ModulateColor = new Color(0.85f, 0.85f, 0.9f);
			}
			else if (isHover)
			{
				style.ModulateColor = new Color(1.18f, 1.18f, 1.25f);
			}
			else
			{
				style.ModulateColor = Colors.White;
			}
			return style;
		}
		return isPressed ? CreateButtonPressed() : (isHover ? CreateButtonHover() : CreateButtonNormal());
	}

	public static StyleBox CreateCustomLobbyRechargeButton(bool isHover = false, bool isPressed = false)
	{
		var tex = GD.Load<Texture2D>("res://Assets/UI/custom_lobby_recharge_button.png");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.ContentMarginLeft = 0;
			style.ContentMarginRight = 0;
			style.ContentMarginTop = 0;
			style.ContentMarginBottom = 0;
			
			if (isPressed)
			{
				style.ModulateColor = new Color(0.85f, 0.85f, 0.9f);
			}
			else if (isHover)
			{
				style.ModulateColor = new Color(1.18f, 1.18f, 1.25f);
			}
			else
			{
				style.ModulateColor = Colors.White;
			}
			return style;
		}
		return isPressed ? CreateButtonPressed() : (isHover ? CreateButtonHover() : CreateButtonNormal());
	}


	public static StyleBoxFlat CreateTextInput(bool hasFocus = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.05f, 0.06f, 0.07f, 0.9f);
		style.BorderColor = hasFocus ? ColorCyanGlow : new Color(0.3f, 0.3f, 0.35f);
		style.SetBorderWidthAll(2);
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		return style;
	}

	public static StyleBox CreateCustomLobbySearchInput(bool hasFocus = false)
	{
		var tex = GD.Load<Texture2D>("res://Assets/UI/custom_lobby_search.png");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.ContentMarginLeft = 80;
			style.ContentMarginRight = 100;
			style.ContentMarginTop = 22;
			style.ContentMarginBottom = 22;
			if (hasFocus)
			{
				style.ModulateColor = new Color(1.15f, 1.15f, 1.25f);
			}
			return style;
		}
		return CreateTextInput(hasFocus);
	}


	public static StyleBoxFlat CreateDropdownStyle(bool isHover = false, bool isPressed = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.08f, 0.09f, 0.11f, 0.95f);
		if (isPressed)
		{
			style.BorderColor = ColorCyanGlow;
		}
		else if (isHover)
		{
			style.BorderColor = ColorGold;
		}
		else
		{
			style.BorderColor = new Color(0.45f, 0.38f, 0.3f);
		}
		style.SetBorderWidthAll(1);
		style.ContentMarginLeft = 14;
		style.ContentMarginRight = 28;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		return style;
	}

	public static StyleBoxFlat CreateFlatButtonStyle(bool isHover = false, bool isPressed = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isPressed ? new Color(0.12f, 0.14f, 0.18f, 0.75f) : (isHover ? new Color(0.1f, 0.12f, 0.15f, 0.55f) : new Color(0.06f, 0.08f, 0.1f, 0.35f));
		if (isPressed)
		{
			style.BorderColor = ColorCyanGlow;
		}
		else if (isHover)
		{
			style.BorderColor = ColorGold;
		}
		else
		{
			style.BorderColor = new Color(0.45f, 0.38f, 0.3f, 0.6f);
		}
		style.SetBorderWidthAll(1);
		style.ContentMarginLeft = 6;
		style.ContentMarginRight = 6;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		return style;
	}

	public static StyleBoxFlat CreateHUDButtonStyle(bool isHover = false, bool isPressed = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isPressed ? new Color(0.12f, 0.14f, 0.16f, 0.9f) : (isHover ? new Color(0.18f, 0.2f, 0.24f, 0.8f) : new Color(0.08f, 0.09f, 0.1f, 0.75f));
		style.BorderColor = isPressed ? ColorCyanGlow : (isHover ? ColorGold : new Color(0.35f, 0.3f, 0.25f, 0.8f));
		style.SetBorderWidthAll(2);
		
		style.ContentMarginLeft = 6;
		style.ContentMarginRight = 6;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		return style;
	}

	public static void ApplyCheckboxStyle(CheckBox cb)
	{
		var checkedTex = GD.Load<Texture2D>("res://Assets/UI/checked_box.jpg");
		var uncheckedTex = GD.Load<Texture2D>("res://Assets/UI/unchecked_box.jpg");
		
		cb.AddThemeIconOverride("checked", checkedTex);
		cb.AddThemeIconOverride("unchecked", uncheckedTex);
		cb.AddThemeIconOverride("checked_disabled", checkedTex);
		cb.AddThemeIconOverride("unchecked_disabled", uncheckedTex);
		
		cb.AddThemeColorOverride("font_color", ColorGold);
		cb.AddThemeColorOverride("font_hover_color", ColorGold);
		cb.AddThemeColorOverride("font_pressed_color", ColorGold);
		cb.AddThemeColorOverride("font_hover_pressed_color", ColorGold);
		cb.AddThemeFontSizeOverride("font_size", 14);
		cb.AddThemeConstantOverride("icon_max_width", 20);
	}

	public static StyleBoxFlat CreateSliderTrack()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.05f, 0.05f, 0.07f, 1.0f);
		style.BorderColor = new Color(0.3f, 0.3f, 0.35f, 1.0f);
		style.SetBorderWidthAll(1);
		style.ExpandMarginTop = 2;
		style.ExpandMarginBottom = 2;
		style.CornerRadiusTopLeft = 2;
		style.CornerRadiusTopRight = 2;
		style.CornerRadiusBottomLeft = 2;
		style.CornerRadiusBottomRight = 2;
		return style;
	}

	public static StyleBoxFlat CreateSliderFill()
	{
		var style = new StyleBoxFlat();
		style.BgColor = ColorGoldDull;
		style.ExpandMarginTop = 2;
		style.ExpandMarginBottom = 2;
		style.CornerRadiusTopLeft = 2;
		style.CornerRadiusTopRight = 2;
		style.CornerRadiusBottomLeft = 2;
		style.CornerRadiusBottomRight = 2;
		return style;
	}


	public static void ApplyTitle(Label label, string text, int fontSize = 36)
	{
		label.Text = TranslationServer.Translate(text);
		var font = FontNorseBold ?? FontCinzelBold;
		if (font != null)
		{
			label.AddThemeFontOverride("font", font);
		}
		label.AddThemeColorOverride("font_color", ColorGold);
		label.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.08f, 0.1f));
		label.AddThemeConstantOverride("outline_size", 8);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
	}

	public static void ApplyButtonText(Button button, string text, int fontSize = 18)
	{
		button.Text = TranslationServer.Translate(text);
		var font = FontNorseBold ?? FontCinzelBold;
		if (font != null)
		{
			button.AddThemeFontOverride("font", font);
		}
		button.AddThemeColorOverride("font_color", ColorGoldDull);
		button.AddThemeColorOverride("font_hover_color", ColorGold);
		button.AddThemeColorOverride("font_pressed_color", ColorGold);
		button.AddThemeColorOverride("font_focus_color", ColorGold);
		button.AddThemeFontSizeOverride("font_size", fontSize);
	}

	public static StyleBox CreateEntranceBgTexture()
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/menu_entrance_background.png");
		return style;
	}

	public static StyleBox CreateLightStonePanel()
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/stone_panel_background.png");
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
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
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
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		style.ContentMarginLeft = 14;
		style.ContentMarginRight = 14;
		style.ContentMarginTop = 10;
		style.ContentMarginBottom = 10;
		return style;
	}

	public static StyleBox CreateLightTitleBadge()
	{
		var style = new StyleBoxTexture();
		style.Texture = GD.Load<Texture2D>("res://Assets/UI/title_banner.png");
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

	public static StyleBox CreateLightButtonNormal()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.13f, 0.15f, 0.18f, 1.0f);
		style.BorderColor = new Color(0.36f, 0.39f, 0.44f, 1.0f);
		style.SetBorderWidthAll(3);
		style.CornerRadiusTopLeft = 24;
		style.CornerRadiusTopRight = 24;
		style.CornerRadiusBottomLeft = 24;
		style.CornerRadiusBottomRight = 24;
		style.CornerDetail = 1;
		style.ContentMarginLeft = 32;
		style.ContentMarginRight = 32;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		style.ShadowColor = new Color(0, 0, 0, 0.75f);
		style.ShadowSize = 6;
		style.ShadowOffset = new Vector2(0, 4);
		return style;
	}

	public static StyleBox CreateLightButtonHover()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.18f, 0.21f, 0.26f, 1.0f);
		style.BorderColor = new Color(0.50f, 0.65f, 0.85f, 1.0f);
		style.SetBorderWidthAll(3);
		style.CornerRadiusTopLeft = 24;
		style.CornerRadiusTopRight = 24;
		style.CornerRadiusBottomLeft = 24;
		style.CornerRadiusBottomRight = 24;
		style.CornerDetail = 1;
		style.ContentMarginLeft = 32;
		style.ContentMarginRight = 32;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		style.ShadowColor = new Color(0, 0, 0, 0.85f);
		style.ShadowSize = 8;
		style.ShadowOffset = new Vector2(0, 5);
		return style;
	}

	public static StyleBox CreateLightButtonPressed()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.10f, 0.12f, 0.14f, 1.0f);
		style.BorderColor = new Color(0.20f, 0.75f, 1.0f, 1.0f);
		style.SetBorderWidthAll(3);
		style.CornerRadiusTopLeft = 24;
		style.CornerRadiusTopRight = 24;
		style.CornerRadiusBottomLeft = 24;
		style.CornerRadiusBottomRight = 24;
		style.CornerDetail = 1;
		style.ContentMarginLeft = 32;
		style.ContentMarginRight = 32;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		style.ShadowColor = new Color(0, 0, 0, 0.6f);
		style.ShadowSize = 3;
		style.ShadowOffset = new Vector2(0, 2);
		return style;
	}

	public static StyleBox CreateLightDropdownNormal()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.20f, 0.19f, 0.18f, 1.0f);
		style.BorderColor = new Color(0.40f, 0.36f, 0.30f, 1.0f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 3;
		style.CornerRadiusTopRight = 3;
		style.CornerRadiusBottomLeft = 3;
		style.CornerRadiusBottomRight = 3;
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateLightDropdownHover()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.26f, 0.25f, 0.23f, 1.0f);
		style.BorderColor = new Color(0.58f, 0.48f, 0.32f, 1.0f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 3;
		style.CornerRadiusTopRight = 3;
		style.CornerRadiusBottomLeft = 3;
		style.CornerRadiusBottomRight = 3;
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateLightDropdownPressed()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.16f, 0.15f, 0.14f, 1.0f);
		style.BorderColor = new Color(0.15f, 0.65f, 1.0f, 1.0f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 3;
		style.CornerRadiusTopRight = 3;
		style.CornerRadiusBottomLeft = 3;
		style.CornerRadiusBottomRight = 3;
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateLightSliderTrack()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.28f, 0.28f, 0.32f, 1.0f);
		style.BorderColor = new Color(0.20f, 0.20f, 0.22f, 1.0f);
		style.SetBorderWidthAll(1);
		style.ExpandMarginTop = 2;
		style.ExpandMarginBottom = 2;
		style.CornerRadiusTopLeft = 3;
		style.CornerRadiusTopRight = 3;
		style.CornerRadiusBottomLeft = 3;
		style.CornerRadiusBottomRight = 3;
		return style;
	}

	public static StyleBox CreateLightSliderFill()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.38f, 0.38f, 0.42f, 1.0f);
		style.ExpandMarginTop = 2;
		style.ExpandMarginBottom = 2;
		style.CornerRadiusTopLeft = 3;
		style.CornerRadiusTopRight = 3;
		style.CornerRadiusBottomLeft = 3;
		style.CornerRadiusBottomRight = 3;
		return style;
	}

	public static Texture2D CreateSquareStoneGrabberTexture(bool highlight)
	{
		int size = 20;
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		
		Color border = highlight ? new Color(0.95f, 0.85f, 0.55f) : new Color(0.65f, 0.55f, 0.38f);
		Color topLight = highlight ? new Color(0.75f, 0.70f, 0.60f) : new Color(0.50f, 0.46f, 0.40f);
		Color bottomDark = new Color(0.18f, 0.16f, 0.14f);
		Color fill = highlight ? new Color(0.42f, 0.39f, 0.35f) : new Color(0.32f, 0.30f, 0.27f);
		Color centerGrip = highlight ? new Color(0.95f, 0.85f, 0.55f) : new Color(0.75f, 0.65f, 0.45f);

		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				Color pixelColor = GetSquareStoneGrabberPixelColor(x, y, size, border, topLight, bottomDark, centerGrip, fill);
				img.SetPixel(x, y, pixelColor);
			}
		}
		return ImageTexture.CreateFromImage(img);
	}

	private static Color GetSquareStoneGrabberPixelColor(int x, int y, int size, Color border, Color topLight, Color bottomDark, Color centerGrip, Color fill)
	{
		if (IsGrabberBorder(x, y, size))
			return border;
		if (IsGrabberTopLight(x, y))
			return topLight;
		if (IsGrabberBottomDark(x, y, size))
			return bottomDark;
		if (IsGrabberCenterGrip(x, y, size))
			return centerGrip;
		return fill;
	}

	private static bool IsGrabberBorder(int x, int y, int size)
	{
		return x == 0 || x == size - 1 || y == 0 || y == size - 1;
	}

	private static bool IsGrabberTopLight(int x, int y)
	{
		return x == 1 || y == 1;
	}

	private static bool IsGrabberBottomDark(int x, int y, int size)
	{
		return x == size - 2 || y == size - 2;
	}

	private static bool IsGrabberCenterGrip(int x, int y, int size)
	{
		return x >= size / 2 - 1 && x <= size / 2 + 1 && y >= 5 && y <= size - 6;
	}
}
