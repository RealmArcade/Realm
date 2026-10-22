using System;
using System.Collections.Generic;
using Fastenshtein;
using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

public class AssetFilterDropdownState
{
	public LineEdit Txt { get; set; }
	public string CommittedValue { get; set; }
	public bool IsUpdatingTextProgrammatically { get; set; }
	public bool IsPopupOpening { get; set; }
	public bool IsPopupSelectionInProgress { get; set; }
	public bool IsShowingAll { get; set; }
	public bool IncludeAllFolders { get; set; }
	public List<string> CurrentFilteredItems { get; } = new List<string>();
	public Func<bool, List<string>> ItemsProvider { get; set; }
	public Action<string> OnChanged { get; set; }
	public Node DropdownOwner { get; set; }

	public bool IsDropdownVisible => _dropdownCatcher != null && GodotObject.IsInstanceValid(_dropdownCatcher) && _dropdownCatcher.Visible;

	private const float DropdownRowHeight = 20.0f;
	private const float DropdownMaxHeight = 264.0f;
	private const float DropdownMinWidth = 180.0f;
	private const int DropdownZIndex = 1000;

	private readonly List<Button> _itemButtons = new();
	private Control _dropdownCatcher;
	private PanelContainer _dropdownPanel;
	private ScrollContainer _dropdownScroll;
	private VBoxContainer _dropdownItems;
	private string _previousText = string.Empty;

	public void InitializeDropdown(Node owner, string initialText)
	{
		DropdownOwner = owner;
		_previousText = initialText ?? string.Empty;
		EnsureDropdown();
	}

	public void SyncProgrammaticText(string value)
	{
		_previousText = value ?? string.Empty;
	}

	public void EnsureDropdown()
	{
		if (_dropdownCatcher != null && GodotObject.IsInstanceValid(_dropdownCatcher) && _dropdownCatcher.IsInsideTree())
		{
			return;
		}

		if (DropdownOwner == null || !GodotObject.IsInstanceValid(DropdownOwner))
		{
			return;
		}

		_dropdownCatcher = new Control
		{
			Name = "AssetFilterDropdownCatcher_Control",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop,
			FocusMode = Control.FocusModeEnum.None,
			ZIndex = DropdownZIndex
		};
		_dropdownCatcher.GuiInput += OnDropdownCatcherGuiInput;
		DropdownOwner.AddChild(_dropdownCatcher);
		_dropdownCatcher.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		_dropdownPanel = new PanelContainer
		{
			Name = "AssetFilterDropdownPanel_PanelContainer",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_dropdownPanel.AddThemeStyleboxOverride("panel", CreateDropdownPanelStyle());
		_dropdownCatcher.AddChild(_dropdownPanel);

		var margin = new MarginContainer { Name = "AssetFilterDropdownMargin_MarginContainer" };
		margin.AddThemeConstantOverride("margin_left", 2);
		margin.AddThemeConstantOverride("margin_top", 2);
		margin.AddThemeConstantOverride("margin_right", 2);
		margin.AddThemeConstantOverride("margin_bottom", 2);
		_dropdownPanel.AddChild(margin);

		_dropdownScroll = new ScrollContainer
		{
			Name = "AssetFilterDropdownScroll_ScrollContainer",
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		margin.AddChild(_dropdownScroll);

		_dropdownItems = new VBoxContainer
		{
			Name = "AssetFilterDropdownItems_VBoxContainer",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		_dropdownItems.AddThemeConstantOverride("separation", 0);
		_dropdownScroll.AddChild(_dropdownItems);
	}

	public void FreeDropdown()
	{
		if (_dropdownCatcher != null && GodotObject.IsInstanceValid(_dropdownCatcher))
		{
			_dropdownCatcher.QueueFree();
		}

		_dropdownCatcher = null;
		_dropdownPanel = null;
		_dropdownScroll = null;
		_dropdownItems = null;
		_itemButtons.Clear();
	}

	public void CommitSelection(string selected)
	{
		string safeSelected = selected ?? string.Empty;
		IsUpdatingTextProgrammatically = true;
		try
		{
			Txt.Text = safeSelected;
			Txt.CaretColumn = Txt.Text.Length;
		}
		finally
		{
			IsUpdatingTextProgrammatically = false;
		}

		_previousText = safeSelected;

		if (!string.Equals(CommittedValue, safeSelected, StringComparison.Ordinal))
		{
			CommittedValue = safeSelected;
			OnChanged(safeSelected);
		}
	}

	public void ShowAssetPopup(bool showAll = false)
	{
		EnsureDropdown();
		if (_dropdownPanel == null) return;

		IsShowingAll = showAll;

		string currentText = Txt.Text?.Trim() ?? string.Empty;
		string query = showAll ? string.Empty : currentText;
		var allItems = ItemsProvider(IncludeAllFolders);

		List<string> displayItems;
		if (allItems == null || allItems.Count == 0)
		{
			displayItems = new List<string>();
		}
		else
		{
			displayItems = GetDisplayItems(allItems, query, showAll);
		}

		bool looksLikeDeletion = currentText.Length < _previousText.Length
			&& _previousText.StartsWith(currentText, StringComparison.OrdinalIgnoreCase);
		_previousText = currentText;

		if (!showAll && !looksLikeDeletion && ShouldAutoComplete(displayItems, currentText))
		{
			CommitSelection(displayItems[0]);
			HideDropdown();
			return;
		}

		PopulateItems(displayItems);
		PositionAndShowDropdown();
	}

	public void PositionAndShowDropdown()
	{
		if (!Txt.IsInsideTree() || _dropdownCatcher == null || _dropdownPanel == null) return;

		IsPopupOpening = true;
		try
		{
			Rect2 txtRect = Txt.GetGlobalRect();
			Vector2 viewportSize = Txt.GetViewportRect().Size;

			float panelWidth = Mathf.Max(txtRect.Size.X, DropdownMinWidth);
			int itemCount = Math.Max(CurrentFilteredItems.Count, 1);
			float panelHeight = Mathf.Min(itemCount * DropdownRowHeight + 4.0f, DropdownMaxHeight);

			float panelX = Mathf.Clamp(txtRect.Position.X, 0.0f, Mathf.Max(0.0f, viewportSize.X - panelWidth));
			float panelY = txtRect.Position.Y + txtRect.Size.Y + 2.0f;
			if (panelY + panelHeight > viewportSize.Y)
			{
				panelY = Mathf.Max(0.0f, txtRect.Position.Y - panelHeight - 2.0f);
			}

			_dropdownPanel.Position = new Vector2(panelX, panelY);
			_dropdownPanel.Size = new Vector2(panelWidth, panelHeight);
			_dropdownScroll.ScrollVertical = 0;

			_dropdownCatcher.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			_dropdownCatcher.Visible = true;
			_dropdownCatcher.MoveToFront();
			_dropdownPanel.Visible = true;
		}
		finally
		{
			Callable.From(() => IsPopupOpening = false).CallDeferred();
		}
	}

	public void HideDropdown()
	{
		IsShowingAll = false;

		if (_dropdownPanel != null && GodotObject.IsInstanceValid(_dropdownPanel))
		{
			_dropdownPanel.Visible = false;
		}

		if (_dropdownCatcher != null && GodotObject.IsInstanceValid(_dropdownCatcher))
		{
			_dropdownCatcher.Visible = false;
		}
	}

	private List<string> GetDisplayItems(List<string> allItems, string query, bool showAll)
	{
		if (showAll || string.IsNullOrEmpty(query)) return allItems;

		var lev = new Levenshtein(query.ToLowerInvariant());
		var scoredItems = new List<(string Item, int Score)>();
		for (int i = 0; i < allItems.Count; i++)
		{
			scoredItems.Add((allItems[i], FloatingDialogBase.ComputeMatchScore(allItems[i], query, lev)));
		}

		scoredItems.Sort((a, b) =>
		{
			int cmp = a.Score.CompareTo(b.Score);
			return cmp != 0 ? cmp : string.Compare(a.Item, b.Item, StringComparison.OrdinalIgnoreCase);
		});

		var genuineMatches = new List<string>();
		for (int i = 0; i < scoredItems.Count; i++)
		{
			if (scoredItems[i].Score < 1000) genuineMatches.Add(scoredItems[i].Item);
		}

		if (genuineMatches.Count > 0) return genuineMatches;

		var fallbackItems = new List<string>();
		int takeCount = Math.Min(5, scoredItems.Count);
		for (int i = 0; i < takeCount; i++)
		{
			fallbackItems.Add(scoredItems[i].Item);
		}

		return fallbackItems;
	}

	private static bool ShouldAutoComplete(List<string> displayItems, string query)
	{
		return displayItems.Count == 1
			&& !string.IsNullOrEmpty(query)
			&& displayItems[0].Length > query.Length
			&& displayItems[0].StartsWith(query, StringComparison.OrdinalIgnoreCase);
	}

	private void PopulateItems(List<string> displayItems)
	{
		CurrentFilteredItems.Clear();

		int visibleCount = Math.Max(displayItems.Count, 1);
		for (int i = 0; i < visibleCount; i++)
		{
			Button itemButton = EnsureItemButton(i);
			itemButton.Visible = true;

			if (displayItems.Count == 0)
			{
				itemButton.Text = TranslationServer.Translate("No matching assets found");
				itemButton.TooltipText = string.Empty;
				itemButton.Disabled = true;
			}
			else
			{
				string item = displayItems[i];
				CurrentFilteredItems.Add(item);
				itemButton.Text = item;
				itemButton.TooltipText = item;
				itemButton.Disabled = false;
			}
		}

		for (int i = visibleCount; i < _itemButtons.Count; i++)
		{
			_itemButtons[i].Visible = false;
		}
	}

	private Button EnsureItemButton(int index)
	{
		while (_itemButtons.Count <= index)
		{
			int slotIndex = _itemButtons.Count;
			var itemButton = new Button
			{
				Name = $"AssetFilterDropdownItem{slotIndex}_Button",
				FocusMode = Control.FocusModeEnum.None,
				Alignment = HorizontalAlignment.Left,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
				CustomMinimumSize = new Vector2(0, DropdownRowHeight),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			itemButton.AddThemeStyleboxOverride("normal", CreateDropdownItemStyle(false));
			itemButton.AddThemeStyleboxOverride("hover", CreateDropdownItemStyle(true));
			itemButton.AddThemeStyleboxOverride("pressed", CreateDropdownItemStyle(true));
			itemButton.AddThemeStyleboxOverride("disabled", CreateDropdownItemStyle(false));
			itemButton.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
			itemButton.AddThemeFontSizeOverride("font_size", 11);
			itemButton.Pressed += () => HandleItemPressed(slotIndex);
			_dropdownItems.AddChild(itemButton);
			_itemButtons.Add(itemButton);
		}

		return _itemButtons[index];
	}

	private void HandleItemPressed(int index)
	{
		if (index < 0 || index >= CurrentFilteredItems.Count) return;

		string selected = CurrentFilteredItems[index];
		IsPopupSelectionInProgress = true;
		CommitSelection(selected);
		HideDropdown();
		Callable.From(() => IsPopupSelectionInProgress = false).CallDeferred();
	}

	private void OnDropdownCatcherGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
		{
			HideDropdown();
		}
	}

	public void ResolveClosestMatch()
	{
		var allItems = ItemsProvider(IncludeAllFolders);
		string closest = FloatingDialogBase.FindClosestMatch(Txt.Text, allItems, CommittedValue);
		CommitSelection(closest);
		HideDropdown();
	}

	private static StyleBoxFlat CreateDropdownPanelStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.08f, 0.09f, 0.11f, 0.97f);
		style.BorderColor = new Color(0.45f, 0.38f, 0.3f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.SetContentMarginAll(2);
		return style;
	}

	private static StyleBoxFlat CreateDropdownItemStyle(bool isHover)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isHover ? new Color(0.16f, 0.18f, 0.22f, 0.95f) : new Color(0.0f, 0.0f, 0.0f, 0.0f);
		style.BorderColor = isHover ? UIStyle.ColorGold : new Color(0.0f, 0.0f, 0.0f, 0.0f);
		style.SetBorderWidthAll(isHover ? 1 : 0);
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 5;
		style.ContentMarginRight = 5;
		style.ContentMarginTop = 1;
		style.ContentMarginBottom = 1;
		return style;
	}
}
