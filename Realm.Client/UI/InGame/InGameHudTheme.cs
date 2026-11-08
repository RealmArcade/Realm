using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.InGame;

/// <summary>
/// The HUD nodes the theme touches. Values rather than accessors: the pass runs once from _Ready, after every reference
/// has been resolved, so there is nothing to look up late.
/// </summary>
public sealed record HudThemeNodes(
	Control Owner,
	Control ResourceContainer,
	TextureRect? ResourceBackground,
	TextureRect? ConsoleBackground,
	Control? BottomConsole,
	Control? MinimapFrame,
	Control? PortraitFrame,
	Control? SelectionFrame,
	Control? CommandFrame,
	Label FeedbackLabel,
	Label? GoldLabel,
	Label? WoodLabel,
	Label? StoneLabel,
	Label? PopulationLabel,
	Label? ClockLabel,
	Control? MinimapArea,
	Control? CameraIndicator);

/// <summary>
/// The in-match HUD's look: the painted plates over the resource bar and console, the transparent panel styleboxes that let
/// that artwork through, the resource icons and readouts, and the minimap overlay that draws camera and ping markers.
/// </summary>
public sealed class InGameHudTheme
{
	private static Shader? _panelTopShader;
	private static Shader? _panelBottomShader;

	/// <summary>The minimap overlay this pass created, for the redraw requests the minimap controller makes.</summary>
	public MinimapOverlay? MinimapOverlay { get; private set; }

	public void Apply(HudThemeNodes nodes)
	{
		nodes.ResourceContainer.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		ApplyResourcePlate(nodes);
		ApplyConsolePlate(nodes);
		ApplyFrameStyles(nodes);
		ApplyResourceReadouts(nodes);
		ApplyMinimapOverlay(nodes);
	}

	/// <summary>
	/// The resource bar's artwork is drawn by a shader so it can stretch the painted panel without distorting its border,
	/// which is why the texture and the material are both set here.
	/// </summary>
	private static void ApplyResourcePlate(HudThemeNodes nodes)
	{
		if (nodes.ResourceBackground == null) return;

		nodes.ResourceBackground.Texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_PanelTop.webp");

		_panelTopShader ??= GD.Load<Shader>("res://Assets/shaders/hud_panel_top.gdshader");
		var topMaterial = new ShaderMaterial();
		topMaterial.Shader = _panelTopShader;
		topMaterial.SetShaderParameter("u_panel_size", new Vector2(840.0f, 76.0f));
		nodes.ResourceBackground.Material = topMaterial;
	}

	private static void ApplyConsolePlate(HudThemeNodes nodes)
	{
		if (nodes.ConsoleBackground == null) return;

		nodes.ConsoleBackground.Texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_PanelBottom.webp");

		_panelBottomShader ??= GD.Load<Shader>("res://Assets/shaders/hud_panel_bottom.gdshader");
		var consoleMaterial = new ShaderMaterial();
		consoleMaterial.Shader = _panelBottomShader;
		nodes.ConsoleBackground.Material = consoleMaterial;
	}

	/// <summary>
	/// The portraits, minimap and command slots all sit on painted slots, so their own panels are emptied and the artwork
	/// behind them shows through. The portrait gets an opaque backing because it is the only slot with nothing painted
	/// behind it.
	/// </summary>
	private static void ApplyFrameStyles(HudThemeNodes nodes)
	{
		nodes.BottomConsole?.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		nodes.MinimapFrame?.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		if (nodes.PortraitFrame is { } portraitFrame)
		{
			portraitFrame.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
			portraitFrame.ZIndex = -1;

			if (portraitFrame.GetNodeOrNull<ColorRect>("PortraitBacking") == null)
			{
				var portraitBacking = new ColorRect();
				portraitBacking.Name = "PortraitBacking";
				portraitBacking.Color = new Color(0.03f, 0.03f, 0.04f, 1.0f);
				portraitBacking.MouseFilter = Control.MouseFilterEnum.Ignore;
				portraitFrame.AddChild(portraitBacking);
				portraitFrame.MoveChild(portraitBacking, 0);
			}
		}

		if (nodes.SelectionFrame is { } selectionFrame)
		{
			selectionFrame.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
			selectionFrame.ClipContents = true;
		}

		var inventoryFrame = nodes.Owner.GetNodeOrNull<PanelContainer>("BottomConsole/HBox/InventoryFrame");
		inventoryFrame?.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		nodes.CommandFrame?.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		UIStyle.ApplyTitle(nodes.FeedbackLabel, "", 32);
		nodes.FeedbackLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
	}

	private static void ApplyResourceReadouts(HudThemeNodes nodes)
	{
		var resourceIcons = new (string nodePath, string assetPath)[]
		{
			("ResourceContainer/HBox/GoldBox/GoldIcon", "res://Assets/UI/gold_coin.webp"),
			("ResourceContainer/HBox/WoodBox/WoodIcon", "res://Assets/UI/wood_logs.webp"),
			("ResourceContainer/HBox/StoneBox/StoneIcon", "res://Assets/UI/stone.webp"),
			("ResourceContainer/HBox/SupplyBox/SupplyIcon", "res://Assets/UI/Supplies.webp"),
			("ResourceContainer/HBox/ClockBox/ClockIcon", "res://Assets/UI/clock.webp"),
		};

		foreach (var (nodePath, assetPath) in resourceIcons)
		{
			var iconRect = nodes.Owner.GetNodeOrNull<TextureRect>(nodePath);
			if (iconRect == null) continue;

			iconRect.Texture = UIStyle.LoadCrispIcon(assetPath, 256);
			iconRect.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
			iconRect.CustomMinimumSize = new Vector2(36, 36);
			iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		}

		var resourceHBox = nodes.Owner.GetNodeOrNull<HBoxContainer>("ResourceContainer/HBox");
		resourceHBox?.AddThemeConstantOverride("separation", 32);

		nodes.Owner.GetNode<Control>("ResourceContainer/HBox/GoldBox").TooltipText = TranslationServer.Translate("Gold");
		nodes.Owner.GetNode<Control>("ResourceContainer/HBox/WoodBox").TooltipText = TranslationServer.Translate("Wood");
		nodes.Owner.GetNode<Control>("ResourceContainer/HBox/StoneBox").TooltipText = TranslationServer.Translate("Stone");
		nodes.Owner.GetNode<Control>("ResourceContainer/HBox/SupplyBox").TooltipText = TranslationServer.Translate("Population / Supply");
		nodes.Owner.GetNode<Control>("ResourceContainer/HBox/ClockBox").TooltipText = TranslationServer.Translate("Game Time & Day/Night Cycle");

		var readouts = new[] { nodes.GoldLabel, nodes.WoodLabel, nodes.StoneLabel, nodes.PopulationLabel, nodes.ClockLabel };
		foreach (var label in readouts)
		{
			if (label == null) continue;

			label.AddThemeFontSizeOverride("font_size", 18);
			label.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			label.AddThemeConstantOverride("outline_size", 2);
			label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
			label.VerticalAlignment = VerticalAlignment.Center;
		}

		// Gold is the one readout the player watches most, so it keeps the brighter colour.
		nodes.GoldLabel?.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		nodes.Owner.GetNode<Label>("BottomConsole/HBox/PortraitFrame/VBox/UnitName")
			.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
	}

	/// <summary>
	/// The minimap overlay draws the camera window and pings over the minimap art. It is added last and told to ignore the
	/// mouse so the minimap's own input still works, and the camera indicator is moved to the top of the stack so it is not
	/// hidden by the overlay.
	/// </summary>
	private void ApplyMinimapOverlay(HudThemeNodes nodes)
	{
		if (nodes.MinimapArea is not { } minimapArea) return;

		var minimapBg = MinimapHelper.SetupMinimapBackground(minimapArea);
		if (minimapBg != null)
		{
			minimapBg.Material = null;
		}

		var overlay = new MinimapOverlay();
		overlay.Name = "MinimapOverlay";
		overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		minimapArea.AddChild(overlay);
		overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		MinimapOverlay = overlay;

		if (nodes.CameraIndicator is { } cameraIndicator)
		{
			cameraIndicator.MouseFilter = Control.MouseFilterEnum.Ignore;
			minimapArea.MoveChild(cameraIndicator, minimapArea.GetChildCount() - 1);
		}
	}

	/// <summary>
	/// Tells the resource plate's shader how big it ended up, so the painted border follows the plate instead of assuming
	/// the size it was authored at. The fallbacks are the artwork's own dimensions.
	/// </summary>
	public static void ResizeResourcePlate(TextureRect? background, Control? container)
	{
		if (background?.Material is not ShaderMaterial topMat) return;

		Vector2 size = container?.Size ?? Vector2.Zero;
		float resW = size.X > 0 ? size.X : 840f;
		float resH = size.Y > 0 ? size.Y : 76f;
		topMat.SetShaderParameter("u_panel_size", new Vector2(resW, resH));
	}
}
