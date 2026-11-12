using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using NSec.Cryptography;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.Services.Publishing;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using Key = Godot.Key;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;

using static Godot.Control;
using Realm.Client.Utils;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		private void RotateSelectedObject(float angleDelta)
		{
			if (GameContext.Host == null) return;

			var (success, newRotY) = GameContext.Host.RotateSelectedObject(angleDelta);
			if (success)
			{
				int selectedCount = GameContext.Host.SelectedEditorObjectCount;
				UpdateSelectedObjectInfo();
				ShowFeedback(selectedCount > 1
					? string.Format(TranslationServer.Translate("{0} Objects Rotated to {1}°"), selectedCount, newRotY)
					: string.Format(TranslationServer.Translate("Rotated Object to {0}°"), newRotY));
			}
		}

		private void ScaleSelectedObject(float scaleDelta)
		{
			if (GameContext.Host == null) return;

			var (success, newScaleVal) = GameContext.Host.ScaleSelectedObject(scaleDelta);
			if (success)
			{
				int selectedCount = GameContext.Host.SelectedEditorObjectCount;
				UpdateSelectedObjectInfo();
				ShowFeedback(selectedCount > 1
					? string.Format(TranslationServer.Translate("{0} Objects Scaled to {1:F1}x"), selectedCount, newScaleVal)
					: string.Format(TranslationServer.Translate("Scaled Object to {0:F1}x"), newScaleVal));
			}
		}

		private void ResetScaleSelectedObject()
		{
			if (GameContext.Host == null) return;

			var success = GameContext.Host.ResetScaleSelectedObject();
			if (success)
			{
				int selectedCount = GameContext.Host.SelectedEditorObjectCount;
				UpdateSelectedObjectInfo();
				ShowFeedback(selectedCount > 1
					? string.Format(TranslationServer.Translate("{0} Objects Reset to 1.0x"), selectedCount)
					: TranslationServer.Translate("Reset Object scale to 1.0x"));
			}
		}

		public void LocateSelectedObjectAction()
		{
			if (GameContext.Host == null) return;
			var selected = GameContext.Host.SelectedEditorObject;
			if (GodotObject.IsInstanceValid(selected) && selected is Node3D node3D)
			{
				GameContext.Host.MainCamera?.FocusOnPosition(node3D.Position);
				ShowFeedback(string.Format(TranslationServer.Translate("Focused camera on {0}"), selected.Name));
			}
			else
			{
				ShowFeedback(TranslationServer.Translate("No object selected to locate"));
			}
		}
	}
}
