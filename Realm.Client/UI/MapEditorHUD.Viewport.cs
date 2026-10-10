using Godot;
using NSec.Cryptography;
using Realm.EditorAPI;
using Realm.Client.Services;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using PasteReflection = Realm.Ecs.Components.Core.PasteReflection;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;

namespace Realm.Client.UI
{
    public partial class MapEditorHUD
    {

        public void ToggleFreeCamera()
        {
            var cam = Realm.Client.Core.GameHost.Instance?.MainCamera as Realm.Client.CameraControl;
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
            if (Realm.Client.Core.GameHost.Instance != null)
            {
                Realm.Client.Core.GameHost.Instance.EditorDisableShadows = !Realm.Client.Core.GameHost.Instance.EditorDisableShadows;
                Realm.Client.Core.GameHost.Instance.UpdateEditorShadows();
                UpdateShadowsExternal(Realm.Client.Core.GameHost.Instance.EditorDisableShadows);
                ShowFeedback(Realm.Client.Core.GameHost.Instance.EditorDisableShadows
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
            if (Realm.Client.Core.GameHost.Instance == null) return;
            var mode = Realm.Client.Core.GameHost.Instance.EditorMirrorMode;
            bool isRotational = mode == MirrorMode.Rotational;
            UpdatePolarSubControlsVisibility(Realm.Client.Core.GameHost.Instance.EditorGridMode);
            if (isRotational)
            {
                ExpandAccordion(_btnHeaderViewport, _contentViewport, "Viewport & Navigation");
            }
        }
    }
}
