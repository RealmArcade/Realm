using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

public class DropdownSuggestionList
{
	public Node Owner { get; set; }
	public Control Anchor { get; set; }
	public int MaxVisibleRows { get; set; } = 13;
	public float RowHeight { get; set; } = 20.0f;
	public float MinWidth { get; set; } = 180.0f;

	public bool IsVisible => _catcher != null && GodotObject.IsInstanceValid(_catcher) && _catcher.Visible;

	public event Action<int, string>? ItemSelected;
	public event Action? Dismissed;

	private const int OverlayZIndex = 1000;

	private readonly List<Button> _rowButtons = new();
	private readonly List<string> _shownItems = new();
	private Control? _catcher;
	private PanelContainer? _panel;
	private ScrollContainer? _scroll;
	private VBoxContainer? _rows;

	public DropdownSuggestionList(Node owner, Control anchor)
	{
		Owner = owner;
		Anchor = anchor;
	}

	public void ShowItems(IReadOnlyList<string> items)
	{
		ShowItems(items, TranslationServer.Translate("No matching assets found"));
	}

	public void ShowItems(IReadOnlyList<string> items, string emptyText)
	{
		EnsureOverlay();
		if (_panel == null) return;

		PopulateRows(items, emptyText);
		PositionAndShow();
	}

	public void Hide()
	{
		if (_panel != null && GodotObject.IsInstanceValid(_panel))
		{
			_panel.Visible = false;
		}

		if (_catcher != null && GodotObject.IsInstanceValid(_catcher))
		{
			_catcher.Visible = false;
		}
	}

	public void Free()
	{
		if (_catcher != null && GodotObject.IsInstanceValid(_catcher))
		{
			_catcher.QueueFree();
		}

		_catcher = null;
		_panel = null;
		_scroll = null;
		_rows = null;
		_rowButtons.Clear();
		_shownItems.Clear();
	}

	private void EnsureOverlay()
	{
		if (_catcher != null && GodotObject.IsInstanceValid(_catcher) && _catcher.IsInsideTree())
		{
			return;
		}

		if (Owner == null || !GodotObject.IsInstanceValid(Owner))
		{
			return;
		}

		_catcher = new Control
		{
			Name = "DropdownSuggestionCatcher_Control",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop,
			FocusMode = Control.FocusModeEnum.None,
			ZIndex = OverlayZIndex
		};
		_catcher.GuiInput += OnCatcherGuiInput;
		Owner.AddChild(_catcher);
		_catcher.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		_panel = new PanelContainer
		{
			Name = "DropdownSuggestionPanel_PanelContainer",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_panel.AddThemeStyleboxOverride("panel",
			UIStyle.CreateCardPlate(new Color(0.08f, 0.09f, 0.11f, 0.97f), new Color(0.45f, 0.38f, 0.3f), 1, 4, 2, 2, 2, 2));
		_catcher.AddChild(_panel);

		var margin = new MarginContainer { Name = "DropdownSuggestionMargin_MarginContainer" };
		margin.AddThemeConstantOverride("margin_left", 2);
		margin.AddThemeConstantOverride("margin_top", 2);
		margin.AddThemeConstantOverride("margin_right", 2);
		margin.AddThemeConstantOverride("margin_bottom", 2);
		_panel.AddChild(margin);

		_scroll = new ScrollContainer
		{
			Name = "DropdownSuggestionScroll_ScrollContainer",
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		margin.AddChild(_scroll);

		_rows = new VBoxContainer
		{
			Name = "DropdownSuggestionRows_VBoxContainer",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		_rows.AddThemeConstantOverride("separation", 0);
		_scroll.AddChild(_rows);
	}

	private void PopulateRows(IReadOnlyList<string> items, string emptyText)
	{
		_shownItems.Clear();

		if (items != null)
		{
			for (int i = 0; i < items.Count; i++)
			{
				_shownItems.Add(items[i]);
			}
		}

		int visibleCount = Math.Max(_shownItems.Count, 1);
		for (int i = 0; i < visibleCount; i++)
		{
			Button rowButton = EnsureRowButton(i);
			rowButton.Visible = true;

			if (_shownItems.Count == 0)
			{
				rowButton.Text = emptyText ?? string.Empty;
				rowButton.TooltipText = string.Empty;
				rowButton.Disabled = true;
			}
			else
			{
				string item = _shownItems[i];
				rowButton.Text = item;
				rowButton.TooltipText = item;
				rowButton.Disabled = false;
			}
		}

		for (int i = visibleCount; i < _rowButtons.Count; i++)
		{
			_rowButtons[i].Visible = false;
		}
	}

	private Button EnsureRowButton(int index)
	{
		if (_rows == null) throw new InvalidOperationException("[DropdownSuggestionList] Cannot build a row before the overlay exists.");

		while (_rowButtons.Count <= index)
		{
			int slotIndex = _rowButtons.Count;
			var rowButton = new Button
			{
				Name = $"DropdownSuggestionRow{slotIndex}_Button",
				FocusMode = Control.FocusModeEnum.None,
				Alignment = HorizontalAlignment.Left,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
				CustomMinimumSize = new Vector2(0, RowHeight),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				MouseDefaultCursorShape = Control.CursorShape.PointingHand
			};
			UIStyle.ApplyEditorSuggestionRow(rowButton);
			rowButton.AddThemeFontSizeOverride("font_size", 11);
			rowButton.Pressed += () => HandleRowPressed(slotIndex);
			_rows.AddChild(rowButton);
			_rowButtons.Add(rowButton);
		}

		return _rowButtons[index];
	}

	private void PositionAndShow()
	{
		if (Anchor == null || !Anchor.IsInsideTree() || _catcher == null || _panel == null || _scroll == null) return;

		Rect2 anchorRect = Anchor.GetGlobalRect();
		Vector2 viewportSize = Anchor.GetViewportRect().Size;

		float panelWidth = Mathf.Max(anchorRect.Size.X, MinWidth);
		int rowCount = Math.Max(_shownItems.Count, 1);
		float panelHeight = Mathf.Min(rowCount * RowHeight + 4.0f, MaxVisibleRows * RowHeight + 4.0f);

		float panelX = Mathf.Clamp(anchorRect.Position.X, 0.0f, Mathf.Max(0.0f, viewportSize.X - panelWidth));
		float panelY = anchorRect.Position.Y + anchorRect.Size.Y + 2.0f;
		if (panelY + panelHeight > viewportSize.Y)
		{
			panelY = Mathf.Max(0.0f, anchorRect.Position.Y - panelHeight - 2.0f);
		}

		_panel.Position = new Vector2(panelX, panelY);
		_panel.Size = new Vector2(panelWidth, panelHeight);
		_scroll.ScrollVertical = 0;

		_catcher.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_catcher.Visible = true;
		_catcher.MoveToFront();
		_panel.Visible = true;
	}

	private void HandleRowPressed(int index)
	{
		if (index < 0 || index >= _shownItems.Count) return;

		string selected = _shownItems[index];
		Hide();
		ItemSelected?.Invoke(index, selected);
	}

	private void OnCatcherGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
		{
			Hide();
			Dismissed?.Invoke();
		}
	}

}
