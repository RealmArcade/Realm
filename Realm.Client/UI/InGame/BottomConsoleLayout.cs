using System;
using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.InGame;

/// <summary>
/// The console's nodes, read through accessors because half of them are created during the HUD's own setup and the
/// layout runs from a resize callback. Passing the nodes themselves would capture whichever existed first.
/// </summary>
public sealed record ConsoleLayoutNodes(
	Control Owner,
	Func<Control?> BottomConsole,
	Func<Control?> ConsoleBackground,
	Func<UnitVitalsBars?> VitalsBars,
	Func<Control?> MinimapFrame,
	Func<Control?> MinimapControls,
	Func<Control?> PortraitFrame,
	Func<Control?> SelectionFrame,
	Func<UnitStatsPanel?> StatsPanel,
	Func<Control?> CommandFrame,
	Func<Control?> ChatPanel,
	Func<Control?> ResourceContainer,
	Func<Control?> ControlGroupsContainer,
	Func<Control?> CustomUIPanel);

/// <summary>
/// Positions the bottom console and everything drawn on it. The artwork is authored at 1920x1080, so every slot is a
/// proportion of the console height and the whole plate is laid out absolutely: anchors are pinned to the top-left and the
/// offsets carry the scaled positions. That is what keeps the minimap, portrait, selection and command slots aligned with
/// the painted frame at any resolution.
/// </summary>
public sealed class BottomConsoleLayout
{
	/// <summary>The console height at the 1920-wide authoring size; everything else scales from this.</summary>
	public const float BaseConsoleHeight = 240f;

	private const float ConsoleArtworkHeight = 240f;
	private const float PortraitSlotLeft = 370f;
	private const float PortraitSlotTop = 75f;
	private const float PortraitSlotRight = 515f;
	private const float PortraitSlotBottom = 195f;
	private const float PortraitNameHeight = 20f;

	public void Apply(ConsoleLayoutNodes nodes)
	{
		if (nodes.BottomConsole() is not { } bottomConsole) return;

		float viewportWidth = nodes.Owner.GetViewportRect().Size.X;
		if (viewportWidth <= 0f) viewportWidth = 1920f;
		float proportionalHeight = viewportWidth * (BaseConsoleHeight / 1920f);

		bottomConsole.OffsetBottom = 0;
		bottomConsole.OffsetTop = -Mathf.Round(proportionalHeight);

		if (nodes.ConsoleBackground() is { Material: ShaderMaterial mat })
		{
			mat.SetShaderParameter("u_console_size", new Vector2(viewportWidth, proportionalHeight));
		}

		nodes.VitalsBars()?.ApplyLayout(proportionalHeight);

		float s = proportionalHeight / 433f;
		float wLeft = 935f * s;
		float wRight = 912f * s;

		Place(nodes.MinimapFrame(), s, 38f, 426f, 61f, 426f);
		Place(nodes.MinimapControls(), s, 438f, 534f, 64f, 424f);

		PlacePortrait(nodes, proportionalHeight);

		if (nodes.SelectionFrame() is { } selectionFrame)
		{
			Pin(selectionFrame);
			selectionFrame.OffsetLeft = Mathf.Round(wLeft + 140f * s);
			selectionFrame.OffsetRight = Mathf.Round(viewportWidth - wRight - 40f * s);
			selectionFrame.OffsetTop = Mathf.Round(165f * s);
			selectionFrame.OffsetBottom = Mathf.Round(415f * s);

			if (nodes.StatsPanel() is { } statsPanel)
			{
				float labelW = Mathf.Max(200f, selectionFrame.OffsetRight - selectionFrame.OffsetLeft - 20f);
				statsPanel.Root.CustomMinimumSize = new Vector2(labelW, 0);
			}
		}

		var inventoryFrame = nodes.Owner.GetNodeOrNull<PanelContainer>("BottomConsole/HBox/InventoryFrame");
		if (inventoryFrame != null)
		{
			Pin(inventoryFrame);
			inventoryFrame.OffsetLeft = Mathf.Round(viewportWidth - (2752f - 1850f) * s);
			inventoryFrame.OffsetRight = Mathf.Round(viewportWidth - (2752f - 2150f) * s);
			inventoryFrame.OffsetTop = Mathf.Round(170f * s);
			inventoryFrame.OffsetBottom = Mathf.Round(425f * s);
		}

		if (nodes.CommandFrame() is { } commandFrame)
		{
			Pin(commandFrame);
			commandFrame.OffsetLeft = Mathf.Round(viewportWidth - (2752f - 2250f) * s);
			commandFrame.OffsetRight = Mathf.Round(viewportWidth - (2752f - 2725f) * s);
			commandFrame.OffsetTop = Mathf.Round(60f * s);
			commandFrame.OffsetBottom = Mathf.Round(415f * s);
		}

		// The chat log sits just above the console rather than inside it, so it tracks the console's top edge.
		if (nodes.ChatPanel() is { } chatPanel)
		{
			const float chatHeight = 240f;
			chatPanel.OffsetBottom = bottomConsole.OffsetTop - 15f;
			chatPanel.OffsetTop = chatPanel.OffsetBottom - chatHeight;
		}
	}

	/// <summary>
	/// Applies the user's HUD scale. Each element pivots on the edge that is meant to stay put - the console scales up from
	/// its bottom centre, the chat from its bottom left - so scaling does not move them off the frame they sit in.
	/// </summary>
	public void ApplyHudScale(ConsoleLayoutNodes nodes, float scale)
	{
		if (nodes.ResourceContainer() is { } resourceContainer)
		{
			resourceContainer.PivotOffset = new Vector2(resourceContainer.Size.X / 2f, 0f);
			resourceContainer.Scale = new Vector2(scale, scale);
		}

		if (nodes.BottomConsole() is { } bottomConsole)
		{
			float width = bottomConsole.Size.X > 0 ? bottomConsole.Size.X : nodes.Owner.GetViewportRect().Size.X;
			float height = bottomConsole.Size.Y > 0 ? bottomConsole.Size.Y : BaseConsoleHeight;
			bottomConsole.PivotOffset = new Vector2(width / 2f, height);
			bottomConsole.Scale = new Vector2(scale, scale);
		}

		if (nodes.ChatPanel() is { } chatPanel)
		{
			chatPanel.PivotOffset = new Vector2(0f, chatPanel.Size.Y);
			chatPanel.Scale = new Vector2(scale, scale);
		}

		if (nodes.ControlGroupsContainer() is { } controlGroups)
		{
			controlGroups.PivotOffset = Vector2.Zero;
			controlGroups.Scale = new Vector2(scale, scale);
		}

		if (nodes.CustomUIPanel() is { } customUIPanel)
		{
			customUIPanel.PivotOffset = new Vector2(customUIPanel.Size.X, 0f);
			customUIPanel.Scale = new Vector2(scale, scale);
		}
	}

	/// <summary>
	/// The portrait slot is a hole in the artwork, so it scales from the artwork's own height rather than from the 433-unit
	/// console grid the other slots use.
	/// </summary>
	private static void PlacePortrait(ConsoleLayoutNodes nodes, float proportionalHeight)
	{
		if (nodes.PortraitFrame() is not { } portraitFrame) return;

		float artworkScale = proportionalHeight / ConsoleArtworkHeight;
		Pin(portraitFrame);
		portraitFrame.OffsetLeft = Mathf.Round(PortraitSlotLeft * artworkScale);
		portraitFrame.OffsetRight = Mathf.Round(PortraitSlotRight * artworkScale);
		portraitFrame.OffsetTop = Mathf.Round(PortraitSlotTop * artworkScale);
		portraitFrame.OffsetBottom = Mathf.Round(PortraitSlotBottom * artworkScale);

		var portraitTexture = portraitFrame.GetNodeOrNull<TextureRect>("VBox/PortraitTexture");
		if (portraitTexture == null) return;

		float slotHeight = (PortraitSlotBottom - PortraitSlotTop - PortraitNameHeight) * artworkScale;
		float slotWidth = (PortraitSlotRight - PortraitSlotLeft) * artworkScale;
		float portraitSize = Mathf.Round(Mathf.Min(slotWidth, slotHeight));
		portraitTexture.CustomMinimumSize = new Vector2(portraitSize, portraitSize);
	}

	private static void Place(Control? control, float scale, float left, float right, float top, float bottom)
	{
		if (control == null) return;

		Pin(control);
		control.OffsetLeft = Mathf.Round(left * scale);
		control.OffsetRight = Mathf.Round(right * scale);
		control.OffsetTop = Mathf.Round(top * scale);
		control.OffsetBottom = Mathf.Round(bottom * scale);
	}

	/// <summary>Pins a slot to the console's top-left corner so the offsets are plain pixel positions.</summary>
	private static void Pin(Control control)
	{
		control.AnchorLeft = 0;
		control.AnchorRight = 0;
		control.AnchorTop = 0;
		control.AnchorBottom = 0;
	}
}
