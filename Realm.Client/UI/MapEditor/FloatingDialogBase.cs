using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Fastenshtein;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Environment = Godot.Environment;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class FloatingDialogBase : PanelContainer
{
	public FloatingDialogBase()
	{
		Name = GetType().Name;
	}

	private static readonly List<FloatingDialogBase> _openDialogs = new();
	public static bool HasAnyDialogOpen => _openDialogs.Count > 0;

	public static bool IsMouseOverAnyDialogOpen(Vector2 mousePos)
	{
		for (int i = _openDialogs.Count - 1; i >= 0; i--)
		{
			var dialog = _openDialogs[i];
			if (dialog != null && GodotObject.IsInstanceValid(dialog) && dialog.IsOpen && dialog.IsVisibleInTree())
			{
				if (dialog.GetGlobalRect().HasPoint(mousePos))
				{
					return true;
				}
			}
		}

		return false;
	}

	public static bool CloseTopmostDialog()
	{
		for (int i = _openDialogs.Count - 1; i >= 0; i--)
		{
			var dialog = _openDialogs[i];
			if (dialog != null && GodotObject.IsInstanceValid(dialog) && dialog.IsOpen)
			{
				dialog.CancelAndClose();
				return true;
			}
			else
			{
				_openDialogs.RemoveAt(i);
			}
		}

		return false;
	}

	public void BringToFront()
	{
		MoveToFront();
		_openDialogs.Remove(this);
		_openDialogs.Add(this);
	}

	/// <summary>
	/// The map editor this dialog was opened from. It is optional: a dialog can be built and
	/// inspected without a live editor, which is how the UI tests exercise them. Actions that
	/// genuinely need the editor go through <see cref="RequireHud"/> so a missing editor is
	/// reported by name instead of as a null dereference somewhere further down.
	/// </summary>
	protected readonly MapEditorHUD? Hud;

	protected MapEditorHUD RequireHud => Hud ?? throw new InvalidOperationException($"{GetType().Name} needs a live map editor for this action.");
	protected VBoxContainer MainVBox = null!;
	protected HBoxContainer HeaderHBox = null!;
	protected Label TitleLabel = null!;
	protected Button CloseButton = null!;
	protected VBoxContainer BodyContainer = null!;
	protected HBoxContainer FooterHBox = null!;
	protected Button CancelButton = null!;
	protected Button ApplyButton = null!;
	protected Button RandomizeAssetsButton = null!;

	private bool _isDragging;
	private Vector2 _dragStartMousePosition;
	private Vector2 _dragStartPosition;

	public bool IsOpen => Visible && GetParent() != null;

	public FloatingDialogBase(MapEditorHUD? hud, string titleText, Vector2 minSize)
	{
		Name = GetType().Name;
		Hud = hud;
		CustomMinimumSize = minSize;
		Visible = false;
		if (hud != null && GetParent() == null)
		{
			hud.AddChild(this);
		}
		AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		GuiInput += (ev) =>
		{
			if (ev is InputEventMouseButton mb && mb.Pressed)
			{
				BringToFront();
			}
		};

		MainVBox = new VBoxContainer();
		MainVBox.Name = "MainVBox_VBoxContainer";
		MainVBox.AddThemeConstantOverride("separation", 10);
		AddChild(MainVBox);

		HeaderHBox = new HBoxContainer();
		HeaderHBox.Name = "HeaderHBox_HBoxContainer";
		HeaderHBox.AddThemeConstantOverride("separation", 8);
		HeaderHBox.GuiInput += OnHeaderGuiInput;
		HeaderHBox.MouseFilter = MouseFilterEnum.Stop;
		HeaderHBox.MouseDefaultCursorShape = CursorShape.Move;
		MainVBox.AddChild(HeaderHBox);

		TitleLabel = new Label();
		TitleLabel.Name = "TitleLabel_Label";
		TitleLabel.Text = titleText;
		TitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		TitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		TitleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		TitleLabel.AddThemeFontSizeOverride("font_size", 15);
		TitleLabel.MouseFilter = MouseFilterEnum.Pass;

		var titleMargin = new MarginContainer();
		titleMargin.Name = "TitleMargin_MarginContainer";
		titleMargin.AddThemeConstantOverride("margin_top", -14);
		titleMargin.AddThemeConstantOverride("margin_bottom", 0);
		titleMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleMargin.AddChild(TitleLabel);
		HeaderHBox.AddChild(titleMargin);

		CloseButton = new Button();
		CloseButton.Name = "CloseButton_Button";
		CloseButton.Set("icon_max_width", 0);
		CloseButton.Text = "✕";
		CloseButton.CustomMinimumSize = new Vector2(26, 26);
		CloseButton.FocusMode = FocusModeEnum.None;
		CloseButton.Pressed += () => CancelAndClose();

		var closeMargin = new MarginContainer();
		closeMargin.Name = "CloseMargin_MarginContainer";
		closeMargin.AddThemeConstantOverride("margin_right", -35);
		closeMargin.AddThemeConstantOverride("margin_left", 10);
		closeMargin.AddChild(CloseButton);
		HeaderHBox.AddChild(closeMargin);

		BodyContainer = new VBoxContainer();
		BodyContainer.Name = "BodyContainer_VBoxContainer";
		BodyContainer.AddThemeConstantOverride("separation", 8);
		BodyContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		MainVBox.AddChild(BodyContainer);

		FooterHBox = new HBoxContainer();
		FooterHBox.Name = "FooterHBox_HBoxContainer";
		FooterHBox.AddThemeConstantOverride("separation", 12);
		FooterHBox.Alignment = BoxContainer.AlignmentMode.End;
		MainVBox.AddChild(FooterHBox);

		CancelButton = new Button();
		CancelButton.Name = "CancelButton_Button";
		CancelButton.Set("icon_max_width", 0);
		CancelButton.Text = TranslationServer.Translate("CANCEL");
		CancelButton.CustomMinimumSize = new Vector2(90, 30);
		CancelButton.FocusMode = FocusModeEnum.None;
		CancelButton.Pressed += () => CancelAndClose();
		FooterHBox.AddChild(CancelButton);

		ApplyButton = new Button();
		ApplyButton.Name = "ApplyButton_Button";
		ApplyButton.Set("icon_max_width", 0);
		ApplyButton.Text = TranslationServer.Translate("APPLY");
		ApplyButton.CustomMinimumSize = new Vector2(90, 30);
		ApplyButton.FocusMode = FocusModeEnum.None;
		ApplyButton.Pressed += () => ApplyAndClose();
		FooterHBox.AddChild(ApplyButton);
	}

	public void SetFooterCloseOnly(string closeText = "CLOSE")
	{
		if (CancelButton != null) CancelButton.Visible = false;
		if (ApplyButton != null) ApplyButton.Visible = false;

		var btnClose = new Button();
		btnClose.Name = "BtnClose_Button";
		btnClose.Set("icon_max_width", 0);
		btnClose.Text = TranslationServer.Translate(closeText);
		btnClose.CustomMinimumSize = new Vector2(90, 30);
		btnClose.FocusMode = FocusModeEnum.None;
		btnClose.Pressed += () => CloseDialog();
		FooterHBox.AddChild(btnClose);
	}

	protected virtual string GetRandomizeTemplateSlug()
	{
		return string.Empty;
	}

	protected virtual void OnAssetsRandomized()
	{
	}

	protected readonly SessionAssetTracker SessionAssetTracker = new();

	public void CaptureBaselineManifestSnapshot()
	{
		SessionAssetTracker.CaptureBaselineManifestSnapshot();
	}

	public virtual void RandomizeAssets()
	{
		string targetSlug = GetRandomizeTemplateSlug();
		if (string.IsNullOrWhiteSpace(targetSlug))
		{
			return;
		}

		if (SessionAssetTracker.BaselineManifestAssets.Count == 0)
		{
			SessionAssetTracker.CaptureBaselineManifestSnapshot();
		}

		if (SessionAssetTracker.InitialRandomizeTemplateJson == null)
		{
			SessionAssetTracker.RandomizeTemplateSlug = targetSlug;
			SessionAssetTracker.InitialRandomizeTemplateJson = GameContext.EditorService.SnapshotTemplateJson(targetSlug);
		}

		GameContext.EditorService.SnapshotTemplateJson(targetSlug);
		GameContext.EditorService.AssignRandomAssetsToTemplate(targetSlug, overwriteExisting: true);

		SessionAssetTracker.RecordImportedAssets();

		OnAssetsRandomized();
	}

	public void PruneUnreferencedSessionAssets(bool revertSnapshot = false)
	{
		SessionAssetTracker.PruneUnreferencedSessionAssets(revertSnapshot);
	}

	public virtual void SetRandomizeAssetsAction(Action? onRandomize = null)
	{
		var actionToWire = onRandomize ?? RandomizeAssets;

		if (RandomizeAssetsButton == null)
		{
			RandomizeAssetsButton = new Button();
			RandomizeAssetsButton.Name = "RandomizeAssetsButton_Button";
			RandomizeAssetsButton.Set("icon_max_width", 0);
			RandomizeAssetsButton.Text = $"{UnicodeIcons.DICE} " + TranslationServer.Translate("Randomize Assets");
			RandomizeAssetsButton.CustomMinimumSize = new Vector2(160, 28);
			RandomizeAssetsButton.FocusMode = FocusModeEnum.None;
			RandomizeAssetsButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			RandomizeAssetsButton.TooltipText = TranslationServer.Translate("Randomly assign matching indexed assets from storage to this template");

			var faFont = Hud?.GetFontAwesomeFont();
			if (faFont != null)
			{
				RandomizeAssetsButton.AddThemeFontOverride("font", faFont);
			}

			RandomizeAssetsButton.AddThemeFontSizeOverride("font_size", 11);
			UIStyle.ApplyButtonStyleBoxes(RandomizeAssetsButton);
			RandomizeAssetsButton.AddThemeColorOverride("font_color", UIStyle.ColorGold);

			BodyContainer.AddChild(RandomizeAssetsButton);
			BodyContainer.MoveChild(RandomizeAssetsButton, 0);
		}

		RandomizeAssetsButton.Visible = true;
		var signals = RandomizeAssetsButton.GetSignalConnectionList("pressed");
		foreach (var sig in signals)
		{
			if (sig.TryGetValue("callable", out var c) && c.VariantType == Variant.Type.Callable)
			{
				RandomizeAssetsButton.Disconnect("pressed", c.AsCallable());
			}
		}

		RandomizeAssetsButton.Pressed += () => actionToWire();
	}

	public void SetFooterApplyOnly(string applyText = "APPLY")
	{
		if (CancelButton != null) CancelButton.Visible = false;
		if (ApplyButton != null)
		{
			ApplyButton.Visible = true;
			ApplyButton.Text = TranslationServer.Translate(applyText);
		}
		FooterHBox.Alignment = BoxContainer.AlignmentMode.Center;
	}

	protected TextureRect BackgroundTextureRect = null!;
	protected MarginContainer DialogMarginContainer = null!;

	public void SetUncompressedPanelTexture(string texturePath, int marginTop = 30, int marginBottom = 40, int marginLeft = 50, int marginRight = 50)
	{
		AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		if (BackgroundTextureRect == null)
		{
			BackgroundTextureRect = new TextureRect();
			BackgroundTextureRect.Name = "BackgroundTextureRect_TextureRect";
			BackgroundTextureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			BackgroundTextureRect.StretchMode = TextureRect.StretchModeEnum.Scale;
			BackgroundTextureRect.SetAnchorsPreset(LayoutPreset.FullRect);
			AddChild(BackgroundTextureRect);
			MoveChild(BackgroundTextureRect, 0);
		}

		var tex = TrimmedTextureLoader.LoadTrimmedTexture(texturePath);
		if (tex != null)
		{
			BackgroundTextureRect.Texture = tex;
		}

		if (DialogMarginContainer == null && MainVBox != null && MainVBox.GetParent() == this)
		{
			RemoveChild(MainVBox);
			DialogMarginContainer = new MarginContainer();
			DialogMarginContainer.Name = "DialogMarginContainer_MarginContainer";
			DialogMarginContainer.SetAnchorsPreset(LayoutPreset.FullRect);
			DialogMarginContainer.AddChild(MainVBox);
			AddChild(DialogMarginContainer);
		}

		if (DialogMarginContainer != null)
		{
			DialogMarginContainer.AddThemeConstantOverride("margin_top", marginTop);
			DialogMarginContainer.AddThemeConstantOverride("margin_bottom", marginBottom);
			DialogMarginContainer.AddThemeConstantOverride("margin_left", marginLeft);
			DialogMarginContainer.AddThemeConstantOverride("margin_right", marginRight);
		}
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		if (what == NotificationPredelete || what == NotificationExitTree)
		{
			_openDialogs.Remove(this);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (!IsOpen) return;

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.Tab)
			{
				CycleFocusToNextInput(keyEvent.ShiftPressed);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void CycleFocusToNextInput(bool reverse)
	{
		var inputs = new List<LineEdit>();
		CollectFocusableInputs(this, inputs);
		if (inputs.Count == 0) return;

		Control currentFocus = GetViewport().GuiGetFocusOwner();
		int currentIndex = -1;
		if (currentFocus is LineEdit currentEdit)
		{
			currentIndex = inputs.IndexOf(currentEdit);
		}

		int nextIndex;
		if (currentIndex == -1)
		{
			nextIndex = reverse ? inputs.Count - 1 : 0;
		}
		else
		{
			nextIndex = reverse ? (currentIndex - 1 + inputs.Count) % inputs.Count : (currentIndex + 1) % inputs.Count;
		}

		inputs[nextIndex].GrabFocus();
		inputs[nextIndex].SelectAll();
	}

	private void CollectFocusableInputs(Node node, List<LineEdit> inputs)
	{
		if (node is LineEdit edit && edit.Visible && edit.IsInsideTree() && edit.Editable)
		{
			inputs.Add(edit);
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			CollectFocusableInputs(node.GetChild(i), inputs);
		}
	}

	public virtual void OpenDialog()
	{
		CaptureBaselineManifestSnapshot();

		if (GetParent() == null && Hud != null)
		{
			Hud.AddChild(this);
		}

		if (RandomizeAssetsButton == null && !string.IsNullOrWhiteSpace(GetRandomizeTemplateSlug()))
		{
			SetRandomizeAssetsAction();
		}

		Visible = true;
		MoveToFront();
		_openDialogs.Remove(this);
		_openDialogs.Add(this);

		Vector2 parentSize = Hud != null ? Hud.GetViewportRect().Size : GetViewportRect().Size;
		float dialogWidth = Mathf.Max(Size.X, CustomMinimumSize.X);
		float dialogHeight = Mathf.Max(Size.Y, CustomMinimumSize.Y);
		Position = new Vector2(
			Mathf.Max(20, (parentSize.X - dialogWidth) * 0.5f),
			Mathf.Max(20, (parentSize.Y - dialogHeight) * 0.4f)
		);
	}

	public virtual void ApplyAndClose()
	{
		CommitPendingInputFocus();
		OnApply();
		PruneUnreferencedSessionAssets(revertSnapshot: false);
		CloseDialog();
	}

	protected void CommitPendingInputFocus()
	{
		try
		{
			var focusOwner = GetViewport()?.GuiGetFocusOwner();
			if (focusOwner != null && IsAncestorOf(focusOwner))
			{
				focusOwner.ReleaseFocus();
			}
		}
		catch
		{
		}

		CommitInputsRecursive(this);
	}

	private void CommitInputsRecursive(Node node)
	{
		if (node == null) return;

		if (node is SpinBox spinBox)
		{
			try
			{
				spinBox.Apply();
				var lineEdit = spinBox.GetLineEdit();
				if (lineEdit != null && double.TryParse(lineEdit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedVal))
				{
					spinBox.Value = Math.Clamp(parsedVal, spinBox.MinValue, spinBox.MaxValue);
				}
			}
			catch
			{
			}
		}
		else if (node is LineEdit lineEdit)
		{
			try
			{
				lineEdit.ReleaseFocus();
			}
			catch
			{
			}
		}

		int count = node.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			CommitInputsRecursive(node.GetChild(i));
		}
	}

	public virtual void CancelAndClose()
	{
		OnCancel();
		PruneUnreferencedSessionAssets(revertSnapshot: true);
		CloseDialog();
	}

	public event Action? DialogClosed;

	public virtual void CloseDialog()
	{
		SessionAssetTracker.Reset();

		_openDialogs.Remove(this);
		Visible = false;

		DialogClosed?.Invoke();
	}

	protected virtual void OnApply()
	{
	}

	protected virtual void OnCancel()
	{
	}

	private void OnHeaderGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			_isDragging = mouseButton.Pressed;
			if (_isDragging)
			{
				BringToFront();
				_dragStartMousePosition = mouseButton.GlobalPosition;
				_dragStartPosition = Position;
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isDragging)
		{
			Vector2 delta = mouseMotion.GlobalPosition - _dragStartMousePosition;
			Vector2 newPosition = _dragStartPosition + delta;
			Vector2 viewportSize = GetViewportRect().Size;
			newPosition.X = Mathf.Clamp(newPosition.X, 10, Mathf.Max(10, viewportSize.X - Size.X - 10));
			newPosition.Y = Mathf.Clamp(newPosition.Y, 10, Mathf.Max(10, viewportSize.Y - Size.Y - 10));
			Position = newPosition;
		}
	}





	public Button AddButton(
		Control parent,
		string text,
		Action onClick,
		string tooltip = "",
		int fontSize = 11,
		Vector2? minSize = null)
	{
		var btn = CreateDialogButton();
		btn.Text = text;
		var font = Hud?.GetFontAwesomeFont();
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.FocusMode = FocusModeEnum.None;
		if (minSize.HasValue)
		{
			btn.CustomMinimumSize = minSize.Value;
		}

		if (!string.IsNullOrEmpty(tooltip))
		{
			btn.TooltipText = TranslationServer.Translate(tooltip);
		}

		btn.Pressed += onClick;
		parent.AddChild(btn);
		return btn;
	}

	public SubViewportContainer Add3DViewportContainer(
		Control parent,
		Vector2 minSize,
		out SubViewport subViewport,
		out Camera3D camera,
		out DirectionalLight3D light)
	{
		var container = new SubViewportContainer();
		container.Name = "ViewportContainer_SubViewportContainer";
		container.CustomMinimumSize = minSize;
		container.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		container.Stretch = true;

		subViewport = new SubViewport();
		subViewport.Name = "SubViewport_SubViewport";
		subViewport.Size = new Vector2I((int)minSize.X, (int)minSize.Y);
		subViewport.TransparentBg = false;
		subViewport.OwnWorld3D = true;

		var world = new World3D();
		var env = new Environment();
		env.BackgroundMode = Environment.BGMode.Color;
		env.BackgroundColor = new Color(0.11f, 0.13f, 0.17f);
		env.AmbientLightSource = Environment.AmbientSource.Color;
		env.AmbientLightColor = new Color(0.60f, 0.60f, 0.65f);
		env.AmbientLightEnergy = 0.75f;
		world.Environment = env;
		subViewport.World3D = world;

		light = new DirectionalLight3D();
		light.Name = "Light_DirectionalLight3D";
		light.RotationDegrees = new Vector3(-30, 30, 0);
		light.LightEnergy = 0.75f;
		subViewport.AddChild(light);

		camera = new Camera3D();
		camera.Name = "Camera_Camera3D";
		camera.Position = new Vector3(0, 1.2f, 2.8f);
		subViewport.AddChild(camera);

		container.AddChild(subViewport);
		parent.AddChild(container);
		return container;
	}

	public VBoxContainer CreateScrollBody(int minHeight = 350)
	{
		var scroll = new ScrollContainer();
		scroll.Name = "Scroll_ScrollContainer";
		scroll.CustomMinimumSize = new Vector2(0, minHeight);
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

		var innerVBox = new VBoxContainer();
		innerVBox.Name = "InnerVBox_VBoxContainer";
		innerVBox.AddThemeConstantOverride("separation", 8);
		innerVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		innerVBox.SizeFlagsVertical = SizeFlags.ExpandFill;

		scroll.AddChild(innerVBox);
		BodyContainer.AddChild(scroll);
		return innerVBox;
	}

	public VBoxContainer CreateContentColumn()
	{
		var contentVBox = new VBoxContainer();
		contentVBox.Name = "ContentVBox_VBoxContainer";
		contentVBox.AddThemeConstantOverride("separation", 10);
		return contentVBox;
	}

	public VBoxContainer CreateExpandingContentColumn()
	{
		VBoxContainer contentVBox = CreateContentColumn();
		contentVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		return contentVBox;
	}

	public VBoxContainer CreateTopControlsColumn()
	{
		var topControlsVBox = new VBoxContainer();
		topControlsVBox.Name = "TopControlsVBox_VBoxContainer";
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);
		return topControlsVBox;
	}

	public Button CreateDialogButton()
	{
		return UIStyle.CreateBareButton("Btn_Button");
	}

	public LineEdit CreateSlugField(string placeholderKey)
	{
		var txtSlug = new LineEdit();
		txtSlug.Name = "TxtSlug_LineEdit";
		txtSlug.PlaceholderText = TranslationServer.Translate(placeholderKey);
		txtSlug.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		txtSlug.AddThemeFontSizeOverride("font_size", 11);
		return txtSlug;
	}
}