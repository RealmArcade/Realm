using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the swatch grid needs from the editor: the two selections a click can make, the two checkboxes that decide
/// which of them a plain click means, the texture loader, and the label refresh that runs after a rebuild. The member
/// keeps the wrapper's name because the localization generator only extracts keys from call sites naming a registered
/// wrapper.
/// </summary>
internal sealed record SwatchGridHooks(
	Action<int, Button> SelectTerrain,
	Action<int> SelectCliff,
	Func<bool> IsCliffApplyChecked,
	Func<bool> IsGroundApplyChecked,
	Func<int, Texture2D?> LoadTexture,
	Action AfterRebuild);

/// <summary>
/// The texture palette in the texture card: one button per slot, thumbnails or flat colours inside them, the cyan and
/// orange selection overlays, and the texture cache behind the thumbnails. The selection itself stays on the editor —
/// this grid only reports which slot was clicked and which slots should look selected.
/// </summary>
internal sealed class TextureSwatchGrid
{
	private const int Columns = 6;
	private const int MaxVisibleBeforeScroll = 36;
	private const float RowHeight = 40f;

	private readonly SwatchGridHooks _hooks;
	private readonly Dictionary<int, Texture2D> _textureCache = new();
	private readonly List<Button> _buttons = new();

	private Control? _grid;
	private ScrollContainer? _scroll;
	private Panel? _highlightPanel;
	private Panel? _cliffHighlightPanel;
	private EditorService? _dataService;

	public TextureSwatchGrid(SwatchGridHooks hooks)
	{
		_hooks = hooks;
	}

	/// <summary>True once the editor service has been replaced, which is the editor's cue to refresh the labels.</summary>
	public bool IsDataStale => !ReferenceEquals(_dataService, GameContext.EditorService);

	/// <summary>
	/// Repopulates the slot data without touching the buttons. The label text reads that data on a timer, so a replaced
	/// editor service only needs the data refreshed there.
	/// </summary>
	public void RefreshData() => RefreshDataFromWorkspace();

	/// <summary>Hands the grid the two nodes the texture card owns, before the first <see cref="Setup"/>.</summary>
	public void Attach(Control grid, ScrollContainer? scroll)
	{
		_grid = grid;
		_scroll = scroll;
	}

	public Button? GetButton(int slotIndex) =>
		slotIndex >= 0 && slotIndex < _buttons.Count ? _buttons[slotIndex] : null;

	public void InvalidateTextureCache() => _textureCache.Clear();

	/// <summary>
	/// Frees the two selection overlays on teardown. They are reparented onto whichever swatch is selected, so they are
	/// only orphaned (and only need freeing) when the swatch that held them is already gone.
	/// </summary>
	public void ReleaseOrphanedHighlights()
	{
		if (GodotObject.IsInstanceValid(_highlightPanel) && _highlightPanel.GetParent() == null)
		{
			_highlightPanel.QueueFree();
		}

		if (GodotObject.IsInstanceValid(_cliffHighlightPanel) && _cliffHighlightPanel.GetParent() == null)
		{
			_cliffHighlightPanel.QueueFree();
		}
	}

	public void Setup(bool connectEvents = false)
	{
		InvalidateTextureCache();

		var slots = RefreshDataFromWorkspace();
		if (slots != null)
		{
			Rebuild(slots, connectEvents);
		}

		_hooks.AfterRebuild();
	}

	/// <summary>
	/// The cyan overlay marks the terrain texture that painting will use, the orange one the cliff texture. Both are
	/// single panels that move onto the selected swatch rather than one panel per button.
	/// </summary>
	public void Highlight(Button? selectedSwatch)
	{
		_highlightPanel = MoveHighlightTo(_highlightPanel, selectedSwatch, "SwatchHighlightPanel", UIStyle.ColorCyanGlow);
	}

	public void HighlightCliff(Button? selectedSwatch)
	{
		_cliffHighlightPanel = MoveHighlightTo(_cliffHighlightPanel, selectedSwatch, "SwatchCliffHighlightPanel", new Color(0.9f, 0.45f, 0.1f));
	}

	private static Panel MoveHighlightTo(Panel? panel, Button? selectedSwatch, string name, Color borderColor)
	{
		if (!GodotObject.IsInstanceValid(panel))
		{
			panel = new Panel();
			panel.Name = name;
			panel.MouseFilter = MouseFilterEnum.Ignore;
			panel.SetAnchorsPreset(LayoutPreset.FullRect);
			panel.GrowHorizontal = GrowDirection.Both;
			panel.GrowVertical = GrowDirection.Both;

			panel.AddThemeStyleboxOverride("panel", UIStyle.CreateBorderPlate(borderColor, 3, null, 4));
		}

		if (selectedSwatch == null)
		{
			panel.Visible = false;
			return panel;
		}

		if (panel.GetParent() != selectedSwatch)
		{
			panel.GetParent()?.RemoveChild(panel);
			selectedSwatch.AddChild(panel);
		}

		panel.Visible = true;
		return panel;
	}

	private SwatchSlotInfo[]? RefreshDataFromWorkspace()
	{
		_dataService = GameContext.EditorService;

		try
		{
			GameContext.EditorService.Swatches.Clear();

			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			var metadata = MapFileService.LoadMetadata(wsPath);
			var slots = TextureSwatchSlots.ResolveSlots(metadata?.Textures, wsPath);

			GameContext.EditorService.Swatches.Populate(slots, wsPath);
			return slots;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapEditorHUD] Error in SetupTextureSwatches: {ex.Message}");
			return null;
		}
	}

	private void Rebuild(SwatchSlotInfo[] slots, bool connectEvents)
	{
		if (_grid == null) return;

		EnsureScrollContainerExists();
		ClearGrid();
		CreateButtons(slots, connectEvents);
	}

	private void EnsureScrollContainerExists()
	{
		if (_scroll != null || _grid == null) return;
		if (_grid.GetParent() is not VBoxContainer parentVBox) return;

		int gridIndex = _grid.GetIndex();
		parentVBox.RemoveChild(_grid);
		_scroll = new ScrollContainer();
		_scroll.Name = "ScrollSwatches";
		_scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		_scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_scroll.AddChild(_grid);
		parentVBox.AddChild(_scroll);
		parentVBox.MoveChild(_scroll, gridIndex);
	}

	private void ClearGrid()
	{
		if (_grid is GridContainer gridSwatchesContainer)
		{
			gridSwatchesContainer.Columns = Columns;
		}

		foreach (Node child in _grid!.GetChildren())
		{
			_grid.RemoveChild(child);
			child.QueueFree();
		}

		_buttons.Clear();
	}

	private void CreateButtons(SwatchSlotInfo[] slots, bool connectEvents)
	{
		int visibleCount = 0;
		for (int i = 0; i < TextureSwatchSlots.MaxSlots; i++)
		{
			var slot = slots[i];
			var btn = CreateButton(slot);

			if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
			{
				btn.Visible = true;
				visibleCount++;
				SetupVisuals(btn, slot.SlotIndex);
			}
			else
			{
				btn.Visible = false;
			}

			btn.GuiInput += (@event) => HandleInput(@event, slot.SlotIndex, btn);

			_buttons.Add(btn);
			_grid!.AddChild(btn);
		}

		UpdateScrollHeight(visibleCount);
	}

	private static Button CreateButton(SwatchSlotInfo slot)
	{
		var btn = new Button();
		btn.Name = $"Swatch{slot.SlotIndex + 1}";
		btn.Flat = false;
		btn.ExpandIcon = true;
		btn.FocusMode = FocusModeEnum.None;
		btn.CustomMinimumSize = new Vector2(40, 40);
		btn.Set("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxes(btn);
		return btn;
	}

	private void SetupVisuals(Button btn, int slotIndex)
	{
		Texture2D? tex = GetTexture(slotIndex);
		if (tex != null)
		{
			var texRect = new TextureRect();
			texRect.Name = "TexRect_TextureRect";
			texRect.Texture = tex;
			texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
			texRect.MouseFilter = MouseFilterEnum.Ignore;
			texRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			texRect.GrowHorizontal = GrowDirection.Both;
			texRect.GrowVertical = GrowDirection.Both;
			btn.AddChild(texRect);
		}
		else
		{
			var colorBox = new ColorRect();
			colorBox.Name = "ColorBox_ColorRect";
			colorBox.Color = GameContext.EditorService.Swatches.Colors[slotIndex];
			colorBox.MouseFilter = MouseFilterEnum.Ignore;
			colorBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			colorBox.GrowHorizontal = GrowDirection.Both;
			colorBox.GrowVertical = GrowDirection.Both;
			btn.AddChild(colorBox);
		}

		btn.TooltipText = $"{slotIndex}: {GameContext.EditorService.Swatches.DisplayNames[slotIndex]}";
	}

	/// <summary>
	/// A left click paints with the ground texture unless shift is held or only the cliff checkbox is on, in which case
	/// it picks the cliff texture; a right click always picks the cliff texture.
	/// </summary>
	private void HandleInput(InputEvent @event, int slotIndex, Button btn)
	{
		if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed) return;

		if (mouseEvent.ButtonIndex == MouseButton.Left)
		{
			bool isShift = Input.IsKeyPressed(Godot.Key.Shift);
			bool cliffChecked = _hooks.IsCliffApplyChecked();
			bool groundUnchecked = !_hooks.IsGroundApplyChecked();

			if (isShift || (cliffChecked && groundUnchecked))
			{
				_hooks.SelectCliff(slotIndex);
			}
			else
			{
				_hooks.SelectTerrain(slotIndex, btn);
			}
		}
		else if (mouseEvent.ButtonIndex == MouseButton.Right)
		{
			_hooks.SelectCliff(slotIndex);
		}
	}

	private void UpdateScrollHeight(int visibleCount)
	{
		if (_scroll == null) return;

		int numRows = (int)MathF.Ceiling((float)visibleCount / Columns);
		if (numRows < 1) numRows = 1;
		int maxRows = MaxVisibleBeforeScroll / Columns;
		int displayedRows = Math.Min(numRows, maxRows);

		float vSeparation = 6f;
		if (_grid is GridContainer grid && grid.HasThemeConstantOverride("v_separation"))
		{
			vSeparation = grid.GetThemeConstant("v_separation");
		}

		float targetHeight = displayedRows * RowHeight + Math.Max(0, displayedRows - 1) * vSeparation;

		_scroll.CustomMinimumSize = new Vector2(0, targetHeight);
		_scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_scroll.VerticalScrollMode = visibleCount > MaxVisibleBeforeScroll
			? ScrollContainer.ScrollMode.Auto
			: ScrollContainer.ScrollMode.Disabled;
	}

	private Texture2D? GetTexture(int slotIndex)
	{
		if (_textureCache.TryGetValue(slotIndex, out var cached) && cached != null && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}

		Texture2D? result = _hooks.LoadTexture(slotIndex);
		if (result != null)
		{
			_textureCache[slotIndex] = result;
		}

		return result;
	}
}
