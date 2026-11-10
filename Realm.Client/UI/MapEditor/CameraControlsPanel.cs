using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the camera popup needs from the editor: its node tree, the popup-button styling, the way the popup is
/// anchored to its toolbar button, and the two HUD refreshes the buttons trigger.
/// </summary>
public sealed record CameraControlsHooks(
	Node Owner,
	Action<Button, string, string> StylePopupButton,
	Action<Button, string, Action?, int, string> SetupButton,
	Action OpenPopup,
	Action<bool> UpdateAngleButtonText,
	Action ToggleFreeCamera);

/// <summary>
/// The camera popup behind the viewport toolbar's camera button: rotate 90 degrees, the perspective/top-down
/// toggle, zoom in and out, and the free-camera checkbox. The camera calls themselves are ambient
/// (<see cref="GameContext.Host"/>), so the panel only asks the editor to refresh the angle label and to run the
/// free-camera toggle it already owns.
/// </summary>
public sealed class CameraControlsPanel
{
	private const string ToolbarButtonPath = "LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/BoxViewportToolbar/ViewportIconRow1/BtnToggleCamera";

	public PopupPanel Popup { get; private set; } = null!;
	public Button ToggleButton { get; private set; } = null!;
	public Button RotateButton { get; private set; } = null!;
	public Button AngleButton { get; private set; } = null!;
	public Button ZoomInButton { get; private set; } = null!;
	public Button ZoomOutButton { get; private set; } = null!;
	public CheckBox FreeCameraCheck { get; private set; } = null!;

	public void Build(CameraControlsHooks hooks)
	{
		var camVBox = BuildPopupShell(hooks);

		BuildRotateButton(hooks, camVBox);
		BuildAngleButton(hooks, camVBox);
		BuildZoomButtons(hooks, camVBox);
		BuildFreeCameraToggle(hooks, camVBox);

		Popup.AddChild(camVBox);
		hooks.Owner.AddChild(Popup);

		ToggleButton = hooks.Owner.GetNode<Button>(ToolbarButtonPath);
		ToggleButton.Set("icon_max_width", 0);
		hooks.SetupButton(ToggleButton, UnicodeIcons.CAMERA, () => hooks.OpenPopup(), 12, "Configure camera controls (R / C / + / - / F8)");
	}

	private VBoxContainer BuildPopupShell(CameraControlsHooks hooks)
	{
		Popup = new PopupPanel();
		Popup.Name = "PopupCamera";

		Popup.AddThemeStyleboxOverride("panel", UIStyle.CreateEditorPopupPanel(8));

		var camVBox = new VBoxContainer();
		camVBox.Name = "CamVBox";
		camVBox.AddThemeConstantOverride("separation", 6);
		camVBox.CustomMinimumSize = new Vector2(170, 0);
		return camVBox;
	}

	private void BuildRotateButton(CameraControlsHooks hooks, VBoxContainer camVBox)
	{
		RotateButton = new Button();
		RotateButton.Name = "BtnRotate";
		RotateButton.Set("icon_max_width", 0);
		hooks.StylePopupButton(RotateButton, $"{UnicodeIcons.ROTATE} Rotate 90° (R)", "Rotate camera 90 degrees (R)");
		RotateButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			var camera = GameContext.Host?.MainCamera;
			camera?.Rotate90Degrees();
			Popup.Hide();
		};
		camVBox.AddChild(RotateButton);
	}

	private void BuildAngleButton(CameraControlsHooks hooks, VBoxContainer camVBox)
	{
		AngleButton = new Button();
		AngleButton.Name = "BtnCameraAngle";
		AngleButton.Set("icon_max_width", 0);
		hooks.StylePopupButton(AngleButton, $"{UnicodeIcons.CUBE} Top-Down (C)", "Toggle perspective vs top-down angle (C)");
		AngleButton.Pressed += () =>
		{
			var camera = GameContext.Host?.MainCamera;
			camera?.ToggleTopDown();
			if (camera != null)
			{
				hooks.UpdateAngleButtonText(camera.IsTopDown());
			}
			Popup.Hide();
		};
		camVBox.AddChild(AngleButton);
	}

	private void BuildZoomButtons(CameraControlsHooks hooks, VBoxContainer camVBox)
	{
		ZoomInButton = new Button();
		ZoomInButton.Name = "BtnZoomIn";
		ZoomInButton.Set("icon_max_width", 0);
		hooks.StylePopupButton(ZoomInButton, $"{UnicodeIcons.ZOOM_IN} Zoom In (Mouse_Wheel)", "Zoom camera in (Mouse Wheel)");
		ZoomInButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Host?.MainCamera?.ZoomIn();
			Popup.Hide();
		};
		camVBox.AddChild(ZoomInButton);

		ZoomOutButton = new Button();
		ZoomOutButton.Name = "BtnZoomOut";
		ZoomOutButton.Set("icon_max_width", 0);
		hooks.StylePopupButton(ZoomOutButton, $"{UnicodeIcons.ZOOM_OUT} Zoom Out (Mouse_Wheel)", "Zoom camera out (Mouse Wheel)");
		ZoomOutButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Host?.MainCamera?.ZoomOut();
			Popup.Hide();
		};
		camVBox.AddChild(ZoomOutButton);
	}

	private void BuildFreeCameraToggle(CameraControlsHooks hooks, VBoxContainer camVBox)
	{
		FreeCameraCheck = new CheckBox();
		FreeCameraCheck.Name = "ChkFreeCamera";
		FreeCameraCheck.Text = TranslationServer.Translate("Free Camera (F8)");
		FreeCameraCheck.TooltipText = TranslationServer.Translate("Toggle free camera mode (F8)");
		FreeCameraCheck.FocusMode = Control.FocusModeEnum.None;
		FreeCameraCheck.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyCheckboxStyle(FreeCameraCheck);
		FreeCameraCheck.Toggled += (pressed) =>
		{
			var cam = GameContext.Host?.MainCamera;
			if (cam != null && cam.IsFreeCamera != pressed)
			{
				hooks.ToggleFreeCamera();
			}
			Popup.Hide();
		};
		camVBox.AddChild(FreeCameraCheck);
	}
}
