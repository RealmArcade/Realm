using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Ecs.Components.Core;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the overlay menu needs from the editor: the viewport panel that owns the popup and its grid button, and the polar
/// configuration row, which the viewport panel builds but the editor keeps because the symmetry pass also shows it.
/// </summary>
public sealed record OverlayMenuHooks(
	Node Owner,
	Func<ViewportPanel?> Viewport,
	Func<Control?> PolarConfigRow);

/// <summary>
/// The overlay (grid / polar / camera bounds / wireframe) menu and the readouts on its button. The popup's items are
/// indexed by position, so the four update paths all go through one checked-setter rather than four copies of the
/// item-index dance.
/// </summary>
public sealed class MapEditorOverlayMenu
{
	private const int GridItem = 0;
	private const int PolarItem = 1;
	private const int CameraBoundsItem = 2;
	private const int WireframeItem = 3;

	private readonly OverlayMenuHooks _hooks;

	public MapEditorOverlayMenu(OverlayMenuHooks hooks) => _hooks = hooks;

	/// <summary>
	/// Rebuilds the checkmarks from the live editor state before showing, so a shortcut or the symmetry pass that changed
	/// a mode while the popup was closed is reflected when it opens.
	/// </summary>
	public void Open()
	{
		var viewport = _hooks.Viewport();
		if (viewport?.OverlayPopup == null || GameContext.Host == null || viewport.GridToggleButton == null) return;

		var tools = GameContext.EditorService.Tools;
		SetChecked(GridItem, tools.GridMode is GridOverlayMode.Grid or GridOverlayMode.Both);
		SetChecked(PolarItem, tools.GridMode is GridOverlayMode.Polar or GridOverlayMode.Both);
		SetChecked(CameraBoundsItem, tools.CameraBoundsVisible);
		SetChecked(WireframeItem, _hooks.Owner.GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe);

		var globalRect = viewport.GridToggleButton.GetGlobalRect();
		viewport.OverlayPopup.Position = new Vector2I(
			(int)globalRect.Position.X,
			(int)(globalRect.Position.Y + globalRect.Size.Y + 2));
		viewport.OverlayPopup.Popup();
	}

	/// <summary>
	/// Called by the game host when the grid mode changes anywhere else. The button keeps its glyph; only the checkmarks
	/// and the polar row follow.
	/// </summary>
	public void UpdateExternal(GridOverlayMode mode)
	{
		var viewport = _hooks.Viewport();
		if (viewport?.GridToggleButton != null)
		{
			viewport.GridToggleButton.Text = UnicodeIcons.BORDER_ALL;
			viewport.GridToggleButton.TooltipText = TranslationServer.Translate("Overlay");
		}

		SetChecked(GridItem, mode is GridOverlayMode.Grid or GridOverlayMode.Both);
		SetChecked(PolarItem, mode is GridOverlayMode.Polar or GridOverlayMode.Both);

		UpdatePolarSubControls(mode);
	}

	/// <summary>
	/// The polar options are only meaningful while the polar grid is drawn, and the pivot reset is also useful with any
	/// symmetry mode because that is what re-centres the mirrored selection.
	/// </summary>
	public void UpdatePolarSubControls(GridOverlayMode mode)
	{
		var viewport = _hooks.Viewport();
		MirrorMode mirrorMode = GameContext.Host?.EditorMirrorMode ?? MirrorMode.None;
		bool isPolar = GameContext.EditorService.IsPolarGridActive(mode, mirrorMode);
		bool showResetPivot = isPolar || mirrorMode != MirrorMode.None;

		if (viewport?.RingSpacingOption != null) viewport.RingSpacingOption.Visible = isPolar;
		if (viewport?.RadialStepOption != null) viewport.RadialStepOption.Visible = isPolar;
		if (viewport?.ResetPivotButton != null) viewport.ResetPivotButton.Visible = showResetPivot;

		if (_hooks.PolarConfigRow() is { } row)
		{
			row.Visible = showResetPivot;
		}
	}

	public void SetCameraBoundsChecked(bool visible) => SetChecked(CameraBoundsItem, visible);

	public void SetWireframeChecked(bool enabled) => SetChecked(WireframeItem, enabled);

	private void SetChecked(int itemIndexInList, bool isChecked)
	{
		var popup = _hooks.Viewport()?.OverlayPopup;
		if (popup == null) return;

		int idx = popup.GetItemIndex(itemIndexInList);
		if (idx >= 0) popup.SetItemChecked(idx, isChecked);
	}
}
