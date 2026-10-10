using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Client;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Client.Core
{
    public partial class GameHost
    {

        private void FocusCameraOnCoordinate(EditorCoordinate coord)
        {
            float centerX = (coord.MinX + coord.MaxX) / 2.0f;
            float centerZ = (coord.MinZ + coord.MaxZ) / 2.0f;
            float centerY = GetTerrainHeightAt(new Vector3(centerX, 0f, centerZ));

            var camera = (MainCamera ?? GetViewport()?.GetCamera3D()) as Realm.Client.CameraControl;
            if (camera != null)
            {
                camera.FocusOnPosition(new Vector3(centerX, centerY, centerZ));
            }
        }
    }
}
