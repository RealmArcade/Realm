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
	public static ProgressBar CreateProgressBar(float width, float height)
	{
		var progressBar = new ProgressBar();
		progressBar.Name = "ProgressBar_ProgressBar";
		progressBar.CustomMinimumSize = new Vector2(width, height);
		progressBar.MinValue = 0;
		progressBar.MaxValue = 100;
		progressBar.Value = 0;
		return progressBar;
	}

	public static VBoxContainer CreateInfoColumn()
	{
		var infoVBox = new VBoxContainer();
		infoVBox.Name = "InfoVBox_VBoxContainer";
		infoVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		return infoVBox;
	}

	/// <summary>
	/// Configures a popover root: hidden until shown, full-rect and blocking input
	/// so a click outside the card dismisses it instead of reaching the screen below.
	/// </summary>
	public static void ConfigurePopoverRoot(Control root, string nodeName)
	{
		root.Name = nodeName;
		root.Visible = false;
		root.MouseFilter = Control.MouseFilterEnum.Stop;
		root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}

	public static GridContainer CreateGrid(int columns)
	{
		var grid = new GridContainer();
		grid.Name = "Grid_GridContainer";
		grid.Columns = columns;
		return grid;
	}

	/// <summary>
	/// Binds the three-panel chrome (background plus both pillars) that the lobby,
	/// map and menu scenes all declare with the same node names.
	/// </summary>
	public static (Panel Background, Panel LeftPillar, Panel RightPillar) BindStandardChrome(Node root)
	{
		return (root.GetNode<Panel>("Background"),
			root.GetNode<Panel>("LeftPillar"),
			root.GetNode<Panel>("RightPillar"));
	}

	public static ScrollContainer CreateScrollingBody()
	{
		var scroll = new ScrollContainer();
		scroll.Name = "Scroll_ScrollContainer";
		scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		return scroll;
	}

	/// <summary>
	/// Gives a scroll container's vertical bar the console treatment: a bronze grabber over a dark track, brightening to
	/// gold on hover and cyan while held. Width, corner rounding and grabber tint are the parts that vary between panels.
	/// </summary>
	public static void ApplyScrollBar(VScrollBar? vScroll, float width = 4f, int cornerRadius = 2, Color? grabberColor = null, float trackAlpha = 0.3f)
	{
		if (vScroll == null) return;

		vScroll.CustomMinimumSize = new Vector2(width, 0);

		var grabber = new StyleBoxFlat { BgColor = grabberColor ?? new Color(0.6f, 0.5f, 0.35f, 0.7f) };
		grabber.SetCornerRadiusAll(cornerRadius);
		vScroll.AddThemeStyleboxOverride("grabber", grabber);

		var grabberHighlight = new StyleBoxFlat { BgColor = ColorGold };
		grabberHighlight.SetCornerRadiusAll(cornerRadius);
		vScroll.AddThemeStyleboxOverride("grabber_highlight", grabberHighlight);

		var grabberPressed = new StyleBoxFlat { BgColor = ColorCyanGlow };
		grabberPressed.SetCornerRadiusAll(cornerRadius);
		vScroll.AddThemeStyleboxOverride("grabber_pressed", grabberPressed);

		var track = new StyleBoxFlat { BgColor = new Color(0.04f, 0.04f, 0.06f, trackAlpha) };
		track.SetCornerRadiusAll(cornerRadius);
		vScroll.AddThemeStyleboxOverride("scroll", track);
	}

	public static CenterContainer CreateCenteredOverlay()
	{
		var center = new CenterContainer();
		center.Name = "Center_CenterContainer";
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		return center;
	}

	public static HBoxContainer CreateRowContainer()
	{
		var row = new HBoxContainer();
		row.Name = "Row_HBoxContainer";
		row.AddThemeConstantOverride("separation", 6);
		return row;
	}

	public static void ConfigureModalBackdrop(Control backdrop)
	{
		backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
	}

	public static void ConfigureModalContentLayer(Control contentLayer)
	{
		contentLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		contentLayer.MouseFilter = Control.MouseFilterEnum.Ignore;
	}

	public static ColorRect CreateDismissOverlay(float alpha)
	{
		var overlay = new ColorRect();
		overlay.Name = "DismissOverlay_ColorRect";
		overlay.Color = new Color(0.0f, 0.0f, 0.0f, alpha);
		overlay.MouseFilter = Control.MouseFilterEnum.Stop;
		overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		return overlay;
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
		ApplyButtonStyleBoxesClearingFocus(okBtn);
		UIStyle.ApplyButtonText(okBtn, Godot.TranslationServer.Translate("OK"), 14);
		okBtn.CustomMinimumSize = new Vector2(160, 40);
		okBtn.SizeFlagsHorizontal = Godot.Control.SizeFlags.ShrinkCenter;
		okBtn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			onOkPressed?.Invoke();
		};
		vbox.AddChild(okBtn);
	}
}
