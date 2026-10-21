using System;
using System.Collections.Generic;
using Godot;
using SkiaSharp;
using Realm.Client.Utils;

namespace Realm.Client.UI;

public static class UIStyle
{
	public static readonly Color ColorBgDark = new Color(0.08f, 0.08f, 0.1f, 1.0f); // Deep charcoal/midnight
	public static readonly Color ColorPanelBg = new Color(0.13f, 0.14f, 0.17f, 0.95f); // Granite slate
	public static readonly Color ColorPanelLight = new Color(0.18f, 0.19f, 0.23f, 0.95f); // Lighter granite

	public static readonly Color ColorBronze = new Color(0.6f, 0.5f, 0.35f, 1.0f); // Bronze border
	public static readonly Color ColorGold = new Color(0.95f, 0.82f, 0.55f, 1.0f); // Bright gold text
	public static readonly Color ColorGoldDull = new Color(0.75f, 0.67f, 0.5f, 1.0f); // Dull gold

	public static readonly Color ColorCyanGlow = new Color(0.15f, 0.65f, 1.0f, 1.0f); // Runic cyan glow
	public static readonly Color ColorCyanGlowDim = new Color(0.15f, 0.65f, 1.0f, 0.4f);

	public static readonly Font FontNorseBold = GD.Load<Font>("res://Assets/UI/Norse-Bold.otf");
	public static readonly Font FontAwesome = GD.Load<Font>("res://Assets/UI/fa-solid-900.ttf");

	private static readonly Dictionary<string, Texture2D> _unicodeIconTextureCache = new();
	private static SKTypeface? _fontAwesomeTypeface;

	private static Texture2D? _emptyBlackTexture;

	public static void SetupButton(Godot.Button btn, string text, Action onClick, int fontSize = 13, string tooltip = "")
	{
		btn.Text = Godot.TranslationServer.Translate(text);
		btn.CustomMinimumSize = new Godot.Vector2(0, 32);
		UIStyle.ApplyButtonStyleBoxes(btn);
		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.FocusMode = Godot.Control.FocusModeEnum.None;
		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = Godot.TranslationServer.Translate(tooltip);
		}

		btn.Pressed += () =>
		{
			Realm.Client.UI.UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
	}

	public static (Panel popup, ProgressBar progressBar, Label statusLabel, Button closeBtn) CreateExportUI(Node parent, string destinationPath)
	{
		var popup = new Panel();
		popup.Name = "Popup_Panel";
		popup.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		popup.AddThemeStyleboxOverride("panel", CreateBgGradient());
		popup.ZIndex = 1100;
		parent.AddChild(popup);

		var cardPanel = new Panel();
		cardPanel.Name = "CardPanel_Panel";
		cardPanel.CustomMinimumSize = new Vector2(560, 260);
		cardPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", CreateStonePanel(true));
		popup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(520, 220);
		vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		vbox.AddThemeConstantOverride("separation", 10);
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		ApplyTitle(titleLabel, "📦 " + TranslationServer.Translate("EXPORTING MAP PACKAGE (.RMAP)"), 20);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var descLabel = new Label();
		descLabel.Name = "DescLabel_Label";
		descLabel.Text = string.Format(TranslationServer.Translate("Destination: {0}"), System.IO.Path.GetFileName(destinationPath));
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 13);
		descLabel.AddThemeColorOverride("font_color", ColorGold);
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		var progressBar = new ProgressBar();
		progressBar.Name = "ProgressBar_ProgressBar";
		progressBar.CustomMinimumSize = new Vector2(480, 24);
		progressBar.MinValue = 0;
		progressBar.MaxValue = 100;
		progressBar.Value = 0;
		vbox.AddChild(progressBar);

		var statusLabel = new Label();
		statusLabel.Name = "StatusLabel_Label";
		statusLabel.Text = TranslationServer.Translate("Preparing export...");
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 13);
		statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(statusLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

		var buttonRow = new HBoxContainer();
		buttonRow.Name = "ButtonRow_HBoxContainer";
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(buttonRow);

		var closeBtn = CreateCloseButton();
		buttonRow.AddChild(closeBtn);

		closeBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			if (GodotObject.IsInstanceValid(popup))
			{
				popup.QueueFree();
			}
		};

		return (popup, progressBar, statusLabel, closeBtn);
	}

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

			// Set TextureMargin to 0 so the entire artwork image renders unified without 9-slice slicing distortion
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;

			// Content margins: increased padding to make the inner information block compact in width and height
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

	public static StyleBox CreateRandomGeneratorPanel()
	{
		return CreateTexturedPanel("res://Assets/UI/map_editor_random_generator.webp", 80);
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

	public static Texture2D LoadTextureWithFallback(string path)
	{
		return TrimmedTextureLoader.LoadTrimmedTexture(path);
	}

	public static Texture2D LoadCrispIcon(string path, int targetSize = 256)
	{
		return TrimmedTextureLoader.LoadTrimmedTexture(path, targetSize);
	}

	private static SKTypeface? GetFontAwesomeTypeface()
	{
		if (_fontAwesomeTypeface != null)
		{
			return _fontAwesomeTypeface;
		}

		try
		{
			string globalPath = ProjectSettings.GlobalizePath("res://Assets/UI/fa-solid-900.ttf");
			if (System.IO.File.Exists(globalPath))
			{
				byte[] bytes = System.IO.File.ReadAllBytes(globalPath);
				using var data = SKData.CreateCopy(bytes);
				_fontAwesomeTypeface = SKTypeface.FromData(data);
			}
			else if (FileAccess.FileExists("res://Assets/UI/fa-solid-900.ttf"))
			{
				byte[] bytes = FileAccess.GetFileAsBytes("res://Assets/UI/fa-solid-900.ttf");
				using var data = SKData.CreateCopy(bytes);
				_fontAwesomeTypeface = SKTypeface.FromData(data);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UIStyle] Failed to load FontAwesome typeface: {ex.Message}");
		}

		return _fontAwesomeTypeface;
	}

	public static Texture2D? GetUnicodeIconTexture(string unicodeIcon, int textureSize = 32, Color? color = null)
	{
		if (string.IsNullOrEmpty(unicodeIcon))
		{
			return null;
		}

		string cacheKey = $"{unicodeIcon}_{textureSize}_{color?.ToHtml() ?? "white"}";
		if (_unicodeIconTextureCache.TryGetValue(cacheKey, out var cachedTexture))
		{
			return cachedTexture;
		}

		var typeface = GetFontAwesomeTypeface();
		if (typeface == null)
		{
			return null;
		}

		try
		{
			float fontSize = textureSize * 0.7f;
			using var font = new SKFont(typeface, fontSize);

			Color resolvedColor = color ?? Colors.White;
			var skColor = new SKColor(
				(byte)MathF.Round(resolvedColor.R * 255f),
				(byte)MathF.Round(resolvedColor.G * 255f),
				(byte)MathF.Round(resolvedColor.B * 255f),
				(byte)MathF.Round(resolvedColor.A * 255f));

			using var paint = new SKPaint
			{
				Color = skColor,
				IsAntialias = true
			};

			font.MeasureText(unicodeIcon, out SKRect bounds, paint);

			using var bitmap = new SKBitmap(textureSize, textureSize, SKColorType.Rgba8888, SKAlphaType.Premul);
			using var canvas = new SKCanvas(bitmap);
			canvas.Clear(SKColors.Transparent);

			float x = (textureSize / 2f) - bounds.MidX;
			float y = (textureSize / 2f) - bounds.MidY;
			canvas.DrawText(unicodeIcon, x, y, SKTextAlign.Left, font, paint);

			byte[] rgba = bitmap.Bytes;
			var image = Image.CreateFromData(textureSize, textureSize, false, Image.Format.Rgba8, rgba);
			image.GenerateMipmaps();
			var texture = ImageTexture.CreateFromImage(image);

			_unicodeIconTextureCache[cacheKey] = texture;
			return texture;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UIStyle] Failed to generate unicode icon texture: {ex.Message}");
			return null;
		}
	}

	public static StyleBox CreateBackdropPanel()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.06f, 0.07f, 0.09f, 0.82f);
		style.BorderColor = new Color(0.75f, 0.62f, 0.35f, 0.35f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 6;
		style.CornerRadiusTopRight = 6;
		style.CornerRadiusBottomLeft = 6;
		style.CornerRadiusBottomRight = 6;
		style.ContentMarginLeft = 16;
		style.ContentMarginRight = 16;
		style.ContentMarginTop = 12;
		style.ContentMarginBottom = 12;
		return style;
	}

	public static StyleBox CreateStonePanel(bool isLight = false)
	{
		var style = new StyleBoxTexture();
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/stone_slate_panel.webp");

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

			// Zero 9-patch slice margins: renders the 2338x1744 custom artwork as a single unified 3D frame
			// Eliminates 100% of pillar distortion, rune slicing, and header squishing
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;

			// Side content margins (100px)
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


	public static StyleBox CreateButtonNormal()
	{
		var style = new StyleBoxTexture();
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/options_menu_button.webp");
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
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/options_menu_button.webp");
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
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/options_menu_button.webp");
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

	public static StyleBox CreateCustomLobbyButtonTexture(string path, int contentMarginX, int contentMarginY, bool isHover, bool isPressed)
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture(path);
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.ContentMarginLeft = contentMarginX;
			style.ContentMarginRight = contentMarginX;
			style.ContentMarginTop = contentMarginY;
			style.ContentMarginBottom = contentMarginY;

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

	public static StyleBox CreateCustomLobbyStartGameButton(bool isHover = false, bool isPressed = false)
	{
		return CreateCustomLobbyButtonTexture("res://Assets/UI/custom_lobby_start_game.webp", 20, 10, isHover, isPressed);
	}

	public static StyleBox CreateCustomLobbyBackButton(bool isHover = false, bool isPressed = false)
	{
		return CreateCustomLobbyButtonTexture("res://Assets/UI/custom_lobby_back_button.webp", 0, 0, isHover, isPressed);
	}

	public static StyleBox CreateCustomLobbyRechargeButton(bool isHover = false, bool isPressed = false)
	{
		return CreateCustomLobbyButtonTexture("res://Assets/UI/custom_lobby_recharge_button.webp", 0, 0, isHover, isPressed);
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
		var tex = LoadTextureWithFallback("res://Assets/UI/custom_lobby_search.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 350;
			style.TextureMarginRight = 200;
			style.TextureMarginTop = 140;
			style.TextureMarginBottom = 140;
			style.ContentMarginLeft = 45;
			style.ContentMarginRight = 25;
			style.ContentMarginTop = 8;
			style.ContentMarginBottom = 8;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
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

	public static StyleBox CreateMinimapButtonStyle(bool isHover = false, bool isPressed = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isPressed
			? new Color(0.08f, 0.12f, 0.18f, 0.95f)
			: (isHover ? new Color(0.16f, 0.18f, 0.22f, 0.92f) : new Color(0.08f, 0.09f, 0.11f, 0.85f));
		style.BorderColor = isPressed
			? ColorCyanGlow
			: (isHover ? ColorGold : new Color(0.45f, 0.38f, 0.28f, 0.75f));
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 3;
		style.CornerRadiusTopRight = 3;
		style.CornerRadiusBottomLeft = 3;
		style.CornerRadiusBottomRight = 3;
		style.ContentMarginLeft = 4;
		style.ContentMarginRight = 4;
		style.ContentMarginTop = 2;
		style.ContentMarginBottom = 2;
		if (isHover)
		{
			style.ShadowColor = new Color(0.95f, 0.82f, 0.55f, 0.25f);
			style.ShadowSize = 2;
		}
		else if (isPressed)
		{
			style.ShadowColor = new Color(0.15f, 0.65f, 1.0f, 0.35f);
			style.ShadowSize = 2;
		}

		return style;
	}

	public static void ApplyCheckboxStyle(CheckBox cb)
	{
		var checkedTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/checked_box.webp");
		var uncheckedTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/unchecked_box.webp");

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


		public static void AddWarningPopupContent(Control vbox, string message, Action onOkPressed)
	{
		var descLabel = new Label();
		descLabel.Name = "DescLabel_Label";
		descLabel.Text = message;
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		descLabel.AddThemeFontSizeOverride("font_size", 14);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var okBtn = new Button();
		okBtn.Name = "OkBtn_Button";
		okBtn.Flat = false;
		okBtn.AddThemeConstantOverride("icon_max_width", 0);
		okBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		okBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		okBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		okBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(okBtn, Godot.TranslationServer.Translate("OK"), 14);
		okBtn.CustomMinimumSize = new Vector2(160, 40);
		okBtn.SizeFlagsHorizontal = Godot.Control.SizeFlags.ShrinkCenter;
		okBtn.Pressed += () =>
		{
			UIManager.Instance.PlayClickSound();
			onOkPressed?.Invoke();
		};
		vbox.AddChild(okBtn);
	}

public static void ApplyTitle(Label label, string text, int fontSize = 36)
	{
		label.Text = TranslationServer.Translate(text);
		var font = FontNorseBold;
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

		public static (HBoxContainer row, ColorPickerButton btn) CreateColorPickerRow(string labelText, Color initialColor, Action<Color> onColorChanged)
		{
			var row = new HBoxContainer();
			row.Name = "RowTint_HBoxContainer";
			row.AddThemeConstantOverride("separation", 8);

			var lbl = new Label();
			lbl.Name = "LblTint_Label";
			lbl.Text = TranslationServer.Translate(labelText);
			lbl.CustomMinimumSize = new Vector2(140, 0);
			lbl.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(lbl);

			var btn = new ColorPickerButton();
			btn.CustomMinimumSize = new Vector2(90, 24);
			btn.Color = initialColor;
			btn.ColorChanged += (newCol) => onColorChanged?.Invoke(newCol);
			row.AddChild(btn);

			return (row, btn);
		}

		public static Button CreateCloseButton()
		{
			var closeBtn = new Button();
			closeBtn.Name = "CloseBtn_Button";
			closeBtn.Flat = false;
			closeBtn.AddThemeConstantOverride("icon_max_width", 0);
			closeBtn.AddThemeStyleboxOverride("normal", CreateButtonNormal());
			closeBtn.AddThemeStyleboxOverride("hover", CreateButtonHover());
			closeBtn.AddThemeStyleboxOverride("pressed", CreateButtonPressed());
			closeBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
			ApplyButtonText(closeBtn, Godot.TranslationServer.Translate("CLOSE"), 14);
			closeBtn.CustomMinimumSize = new Vector2(130, 36);
			closeBtn.Visible = false;
			return closeBtn;
		}

	public static void ApplyButtonStyleBoxes(Button button)
	{
		button.AddThemeStyleboxOverride("normal", CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", CreateButtonPressed());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	public static Texture2D LoadTextureSafe(string path)
	{
		if (string.IsNullOrEmpty(path)) return null;
		if (path.StartsWith("res://") && !ResourceLoader.Exists(path)) return null;
		if (path.StartsWith("user://") && !FileAccess.FileExists(path)) return null;
		
		try
		{
			return GD.Load<Texture2D>(path);
		}
		catch
		{
			return null;
		}
	}



	public static void ApplyButtonText(Button button, string text, int fontSize = 18)
	{
		button.Text = TranslationServer.Translate(text);
		var font = FontNorseBold;
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

	public static void SetupHUDButton(Button btn, string tooltip, bool showHotkey = true)
	{
		btn.CustomMinimumSize = new Vector2(48, 48);
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 42);

		if (showHotkey && tooltip.StartsWith('[') && tooltip.Contains(']'))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 12);
			hotkeyLabel.AddThemeColorOverride("font_color", ColorGold);
			hotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			hotkeyLabel.AddThemeConstantOverride("outline_size", 4);
			hotkeyLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
			hotkeyLabel.OffsetLeft = 4;
			hotkeyLabel.OffsetTop = 3;
			hotkeyLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			btn.AddChild(hotkeyLabel);
		}
	}

	public static void ApplyMapEditorRandomizeButtonStyle(Button btn)
	{
		var mapEdBtnTex = Utils.TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_button.webp");
		if (mapEdBtnTex != null)
		{
			var normalSb = new StyleBoxTexture { Texture = mapEdBtnTex, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
			var hoverSb = new StyleBoxTexture { Texture = mapEdBtnTex, ModulateColor = new Color(1.25f, 1.2f, 1.0f, 1.0f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
			var pressedSb = new StyleBoxTexture { Texture = mapEdBtnTex, ModulateColor = new Color(0.85f, 0.8f, 0.7f, 1.0f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
			btn.AddThemeStyleboxOverride("normal", normalSb);
			btn.AddThemeStyleboxOverride("hover", hoverSb);
			btn.AddThemeStyleboxOverride("pressed", pressedSb);
			btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		}
		else
		{
			btn.AddThemeStyleboxOverride("normal", CreateButtonNormal());
			btn.AddThemeStyleboxOverride("hover", CreateButtonHover());
			btn.AddThemeStyleboxOverride("pressed", CreateButtonPressed());
		}
	}

	public static void ClearCache()
	{
		_unicodeIconTextureCache.Clear();
		_emptyBlackTexture = null;
	}

	public static Button CreateWebAiButton()
	{
		var btnWebAi = new Button();
		btnWebAi.Name = "BtnWebAi_Button";
		btnWebAi.Set("icon_max_width", 16);
		btnWebAi.AddThemeConstantOverride("icon_max_width", 16);
		btnWebAi.ExpandIcon = false;
		btnWebAi.IconAlignment = HorizontalAlignment.Center;
		btnWebAi.VerticalIconAlignment = VerticalAlignment.Center;
		if (ResourceLoader.Exists("res://Assets/UI/globe_icon.webp"))
		{
			btnWebAi.Icon = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/globe_icon.webp");
		}
		else
		{
			btnWebAi.Text = "🌐";
		}

		btnWebAi.TooltipText = TranslationServer.Translate("Generate 3D Model with AI Online");
		btnWebAi.CustomMinimumSize = new Vector2(24, 24);
		btnWebAi.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		btnWebAi.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		btnWebAi.FocusMode = Control.FocusModeEnum.None;
		btnWebAi.Pressed += () => OS.ShellOpen("https://3d.hunyuanglobal.com");
		
		return btnWebAi;
	}
}