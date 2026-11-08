using Godot;
using Realm.Client.Services;
using System.Collections.Generic;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The row of up to twelve unit buttons under the portrait. Rebuilt wholesale because the scene ships the container
///     empty: the buttons are the HUD's, not the scene's, so every rebuild starts by clearing whatever is there.
/// </summary>
public static class SelectionUnitStrip
{
	/// <summary>The selection frame shows twelve unit slots; a thirteenth selection is reached with the cycle keys.</summary>
	public const int MaxUnits = 12;

	public sealed record Parts(HBoxContainer Container, List<Button> Buttons);

	/// <summary>
	///     Clears and repopulates the strip, returning the container and the buttons in slot order.
	///     <paramref name="root" /> is the HUD, because the container is addressed by its path from there.
	/// </summary>
	public static Parts Rebuild(Node root)
	{
		var container = root.GetNode<HBoxContainer>("BottomConsole/HBox/SelectionFrame/UnitsContainer");

		foreach (Node child in container.GetChildren())
		{
			container.RemoveChild(child);
			child.QueueFree();
		}

		var buttons = new List<Button>(MaxUnits);
		for (int i = 0; i < MaxUnits; i++)
		{
			Button button = CreateSlot(i);
			container.AddChild(button);
			buttons.Add(button);
		}

		return new Parts(container, buttons);
	}

	private static Button CreateSlot(int index)
	{
		var btn = new Button();
		btn.Name = $"Unit{index + 1}";
		btn.CustomMinimumSize = new Vector2(36, 36);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.ExpandIcon = true;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.AddThemeConstantOverride("icon_max_width", 30);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(btn);

		btn.AddChild(CreateHealthBar());
		btn.AddChild(CreateStatusIcon());
		return btn;
	}

	/// <summary>The health sliver along the bottom edge, hidden until the unit has a health worth showing.</summary>
	private static ProgressBar CreateHealthBar()
	{
		var hpBar = new ProgressBar();
		hpBar.Name = "HealthBar";
		hpBar.CustomMinimumSize = new Vector2(36, 4);
		hpBar.ShowPercentage = false;
		hpBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
		hpBar.OffsetTop = -6;
		hpBar.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		hpBar.AddThemeStyleboxOverride("fill", UIStyle.CreateSliderFill());
		hpBar.Visible = false;
		return hpBar;
	}

	/// <summary>The corner glyph that marks a unit as doing something worth noticing.</summary>
	private static Label CreateStatusIcon()
	{
		var statusLbl = new Label();
		statusLbl.Name = "StatusIcon";
		statusLbl.Text = "";
		statusLbl.AddThemeFontSizeOverride("font_size", 10);
		statusLbl.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.2f));
		statusLbl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
		statusLbl.Visible = false;
		return statusLbl;
	}
}
