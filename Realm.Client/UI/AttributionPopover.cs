using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI;

/// <summary>
///     One titled list the attribution dialog shows: what the list is about, what to say while it holds nothing, and the
///     entries themselves.
/// </summary>
/// <param name="Subtitle">The line that introduces the list.</param>
/// <param name="EmptyText">The line shown instead of the list when it holds nothing.</param>
/// <param name="Entries">The entries the list shows.</param>
public readonly record struct AttributionSection(string Subtitle, string EmptyText, IReadOnlyList<string> Entries);

[GlobalClass]
public partial class AttributionPopover : Control
{
	private const float PanelWidth = 440.0f;
	private const float PanelBaseHeight = 168.0f;
	private const float SectionHeadingHeight = 24.0f;
	private const float RowHeight = 30.0f;
	private const float EmptyLineHeight = 22.0f;
	private const float PanelMaximumHeight = 460.0f;

	private ColorRect _dismissOverlay = null!;
	private PanelContainer _panel = null!;
	private Label _titleLabel = null!;
	private ScrollContainer _sectionsScroll = null!;
	private VBoxContainer _sectionsList = null!;
	private Button _closeButton = null!;
	private bool _built;

	private int _sectionCount;
	private int _rowCount;
	private int _emptyLineCount;

	public override void _Ready()
	{
		EnsureBuilt();
	}

	private void EnsureBuilt()
	{
		if (_built) return;
		_built = true;

		UIStyle.ConfigurePopoverRoot(this, "AttributionPopover");

		_dismissOverlay = UIStyle.CreateDismissOverlay(0.55f);
		_dismissOverlay.GuiInput += OnDismissOverlayInput;
		AddChild(_dismissOverlay);

		_panel = new PanelContainer();
		_panel.Name = "AttributionPanel_PanelContainer";
		_panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
		AddChild(_panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Content_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 8);
		_panel.AddChild(vbox);

		_titleLabel = new Label();
		_titleLabel.Name = "Title_Label";
		_titleLabel.Text = LocalizationManager.Translate("ATTRIBUTION");
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_titleLabel.AddThemeFontSizeOverride("font_size", 16);
		vbox.AddChild(_titleLabel);

		_sectionsScroll = new ScrollContainer();
		_sectionsScroll.Name = "SectionScroll_ScrollContainer";
		_sectionsScroll.CustomMinimumSize = new Vector2(0, 80);
		_sectionsScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		_sectionsScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		vbox.AddChild(_sectionsScroll);

		_sectionsList = new VBoxContainer();
		_sectionsList.Name = "SectionList_VBoxContainer";
		_sectionsList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_sectionsList.AddThemeConstantOverride("separation", 4);
		_sectionsScroll.AddChild(_sectionsList);

		_closeButton = new Button();
		_closeButton.Name = "Close_Button";
		_closeButton.Text = LocalizationManager.Translate("CLOSE");
		_closeButton.Set("icon_max_width", 0);
		_closeButton.CustomMinimumSize = new Vector2(120, 28);
		_closeButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_closeButton.FocusMode = FocusModeEnum.None;
		_closeButton.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_closeButton);
		_closeButton.Pressed += Close;
		vbox.AddChild(_closeButton);
	}

	public void ShowFor(Control anchor, IReadOnlyList<string> sourceMapNames)
	{
		ShowFor(anchor, new[]
		{
			new AttributionSection(
				LocalizationManager.Translate("This map contains content imported from:"),
				LocalizationManager.Translate("No imported content has been attributed to this map."),
				sourceMapNames)
		});
	}

	public void ShowFor(Control anchor, IReadOnlyList<AttributionSection> sections)
	{
		EnsureBuilt();
		_titleLabel.Text = LocalizationManager.Translate("ATTRIBUTION");
		Populate(sections);
		Visible = true;

		Node? parent = GetParent();
		if (parent != null)
		{
			parent.MoveChild(this, parent.GetChildCount() - 1);
		}

		PositionPanelNear(anchor);
	}

	public void Close()
	{
		Visible = false;
	}

	private void Populate(IReadOnlyList<AttributionSection> sections)
	{
		foreach (Node child in _sectionsList.GetChildren())
		{
			_sectionsList.RemoveChild(child);
			child.QueueFree();
		}

		_sectionCount = 0;
		_rowCount = 0;
		_emptyLineCount = 0;

		if (sections != null)
		{
			foreach (AttributionSection section in sections) AppendSection(section);
		}

		_sectionsScroll.Visible = _sectionCount > 0;
	}

	private void AppendSection(AttributionSection section)
	{
		var heading = new Label();
		heading.Name = "SectionHeading_Label";
		heading.Text = section.Subtitle;
		heading.AutowrapMode = TextServer.AutowrapMode.Word;
		heading.CustomMinimumSize = new Vector2(PanelWidth - 32, 0);
		heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		heading.AddThemeColorOverride("font_color", new Color(0.78f, 0.8f, 0.86f));
		heading.AddThemeFontSizeOverride("font_size", 11);
		_sectionsList.AddChild(heading);

		_sectionCount++;

		IReadOnlyList<string> entries = MapAttributionLookup.Clean(section.Entries);
		if (entries.Count == 0)
		{
			var emptyLabel = new Label();
			emptyLabel.Name = "SectionEmpty_Label";
			emptyLabel.Text = section.EmptyText;
			emptyLabel.AutowrapMode = TextServer.AutowrapMode.Word;
			emptyLabel.CustomMinimumSize = new Vector2(PanelWidth - 32, 0);
			emptyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			emptyLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.72f, 0.78f));
			emptyLabel.AddThemeFontSizeOverride("font_size", 11);
			_sectionsList.AddChild(emptyLabel);
			_emptyLineCount++;
			return;
		}

		foreach (string entry in entries)
		{
			_sectionsList.AddChild(CreateEntryRow(entry));
			_rowCount++;
		}
	}

	private static PanelContainer CreateEntryRow(string entry)
	{
		var row = new PanelContainer();
		row.Name = "AttributionEntry_PanelContainer";
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var rowStyle = UIStyle.CreateCardPlate(new Color(0.04f, 0.05f, 0.07f, 0.75f),
			new Color(0.45f, 0.38f, 0.25f, 0.35f), 1, 4, 10, 10, 5, 5);
		row.AddThemeStyleboxOverride("panel", rowStyle);

		var label = new Label();
		label.Name = "AttributionEntry_Label";
		label.Text = entry;
		label.AutowrapMode = TextServer.AutowrapMode.Word;
		label.CustomMinimumSize = new Vector2(PanelWidth - 52, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.98f));
		label.AddThemeFontSizeOverride("font_size", 12);
		row.AddChild(label);

		return row;
	}

	private void PositionPanelNear(Control anchor)
	{
		float contentHeight = _sectionCount * SectionHeadingHeight + _rowCount * RowHeight + _emptyLineCount * EmptyLineHeight;
		float panelHeight = Mathf.Clamp(PanelBaseHeight + contentHeight, PanelBaseHeight, PanelMaximumHeight);
		Vector2 viewportSize = GetViewportRect().Size;

		Vector2 position = anchor != null
			? anchor.GlobalPosition + new Vector2(anchor.Size.X * 0.5f - PanelWidth * 0.5f, anchor.Size.Y + 10.0f)
			: new Vector2(viewportSize.X * 0.5f - PanelWidth * 0.5f, viewportSize.Y * 0.5f - panelHeight * 0.5f);

		position.X = Mathf.Clamp(position.X, 16.0f, Mathf.Max(16.0f, viewportSize.X - PanelWidth - 16.0f));
		position.Y = Mathf.Clamp(position.Y, 16.0f, Mathf.Max(16.0f, viewportSize.Y - panelHeight - 16.0f));

		_panel.Position = position;
		_panel.Size = new Vector2(PanelWidth, panelHeight);
	}

	private void OnDismissOverlayInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouseEvent) return;
		if (!mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left) return;

		Close();
		GetViewport().SetInputAsHandled();
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (!Visible) return;
		if (!@event.IsActionPressed("ui_cancel")) return;

		Close();
		GetViewport().SetInputAsHandled();
	}

	private static StyleBoxFlat CreatePanelStyle() => UIStyle.CreatePopoverPanelPlate();
}
