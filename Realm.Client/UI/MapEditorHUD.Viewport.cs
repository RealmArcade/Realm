using Godot;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		public void ToggleFreeCamera()
		{
			var cam = GameContext.Host?.MainCamera;
			if (cam != null)
			{
				cam.ToggleFreeCamera();
				UpdateFreeCameraExternal(cam.IsFreeCamera);
				ShowFeedback(cam.IsFreeCamera
					? TranslationServer.Translate("Free Camera: ON (RMB drag to look, WASD/QE to fly, Wheel to zoom)")
					: TranslationServer.Translate("Free Camera: OFF (Clamped to game bounds)"));
			}
		}

		public void UpdateFreeCameraExternal(bool isFreeCam)
		{
			if (_cameraPanel.FreeCameraCheck != null)
			{
				_cameraPanel.FreeCameraCheck.SetPressedNoSignal(isFreeCam);
			}
		}

		public void ToggleShadows()
		{
			if (GameContext.Host != null)
			{
				bool val = !GameContext.EditorService.Tools.DisableShadows;
				GameContext.EditorService.Tools.DisableShadows = val;
				GameContext.Host.UpdateEditorShadows();
				UpdateShadowsExternal(GameContext.EditorService.Tools.DisableShadows);
				ShowFeedback(GameContext.EditorService.Tools.DisableShadows
					? TranslationServer.Translate("Shadows: OFF")
					: TranslationServer.Translate("Shadows: ON"));
			}
		}

		public void UpdateCameraBoundsUI()
		{
			_mapSettingsDialog?.UpdateCameraBoundsUI();
		}

		public void UpdateSelectedSkyboxExternal(string path)
		{
			_mapSettingsDialog?.SelectSkybox(path);
		}

		public void UpdateCameraAngleButtonText(bool isTopDown)
		{
			if (_cameraPanel.AngleButton != null)
			{
				_cameraPanel.AngleButton.Text = isTopDown
					? TranslationServer.Translate($"{UnicodeIcons.CUBE} Tilt (C)")
					: TranslationServer.Translate($"{UnicodeIcons.CUBE} Top-Down (C)");
			}
		}

		private void UpdateSymmetrySubControlsVisibility()
		{
			if (!GameContext.EditorService.TryGetSymmetryVisibilityState(out bool isRotational, out GridOverlayMode gridMode))
				return;

			UpdatePolarSubControlsVisibility(gridMode);
			if (isRotational)
			{
				ExpandAccordion(_viewportPanel.Header, _viewportPanel.Content, "Viewport & Navigation");
			}
		}
	}
}