using Godot;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		public void ToggleFreeCamera()
		{
			var cam = GameHost.Instance?.MainCamera;
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
			if (_chkFreeCamera != null)
			{
				_chkFreeCamera.SetPressedNoSignal(isFreeCam);
			}
		}

		public void ToggleShadows()
		{
			if (GameHost.Instance != null)
			{
				bool val = !GameContext.EditorService.EditorDisableShadows;
				GameContext.EditorService.EditorDisableShadows = val;
				GameHost.Instance.UpdateEditorShadows();
				UpdateShadowsExternal(GameContext.EditorService.EditorDisableShadows);
				ShowFeedback(GameContext.EditorService.EditorDisableShadows
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
			if (_btnCameraAngle != null)
			{
				_btnCameraAngle.Text = isTopDown
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
				ExpandAccordion(_btnHeaderViewport, _contentViewport, "Viewport & Navigation");
			}
		}
	}
}