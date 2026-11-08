using System;
using System.Collections.Generic;
using Godot;
using SkiaSharp;
using Realm.Client.Utils;
using Realm.Client.Services;

namespace Realm.Client.UI;

public static partial class UIStyle
{
	public static readonly Color ColorBgDark = new Color(0.08f, 0.08f, 0.1f, 1.0f);
	public static readonly Color ColorPanelBg = new Color(0.13f, 0.14f, 0.17f, 0.95f);
	public static readonly Color ColorPanelLight = new Color(0.18f, 0.19f, 0.23f, 0.95f);

	public static readonly Color ColorBronze = new Color(0.6f, 0.5f, 0.35f, 1.0f);
	public static readonly Color ColorGold = new Color(0.95f, 0.82f, 0.55f, 1.0f);
	public static readonly Color ColorGoldDull = new Color(0.75f, 0.67f, 0.5f, 1.0f);

	public static readonly Color ColorCyanGlow = new Color(0.15f, 0.65f, 1.0f, 1.0f);
	public static readonly Color ColorCyanGlowDim = new Color(0.15f, 0.65f, 1.0f, 0.4f);

	public static readonly Font FontNorseBold = GD.Load<Font>("res://Assets/UI/Norse-Bold.otf");
	public static readonly Font FontAwesome = GD.Load<Font>("res://Assets/UI/fa-solid-900.ttf");

	private const string StonePanelTexturePath = "res://Assets/UI/stone_slate_panel.webp";
	private const string ButtonPlateTexturePath = "res://Assets/UI/options_menu_button.webp";

	private const int StonePanelFrameThickness = 28;
	private const int ButtonPlateTipWidth = 18;
	private const int ButtonPlateEdgeHeight = 8;

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
			GameContext.UserInterface?.PlayClickSound();
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

		var progressBar = UIStyle.CreateProgressBar(480f, 24f);
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
			GameContext.UserInterface?.PlayClickSound();
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

	public static Texture2D? LoadTextureWithFallback(string path)
	{
		return TrimmedTextureLoader.LoadTrimmedTexture(path);
	}

	public static Texture2D? LoadCrispIcon(string path, int targetSize = 256)
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
			GameLog.LogError($"[UIStyle] Failed to load FontAwesome typeface: {ex.Message}");
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
			GameLog.LogError($"[UIStyle] Failed to generate unicode icon texture: {ex.Message}");
			return null;
		}
	}




	public static Texture2D? LoadTextureSafe(string path)
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
