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
		public void OpenVfxStudioDialog(VfxAttachmentConfig? initialConfig = null, Action<VfxAttachmentConfig>? onApplied = null)
		{
			if (_vfxStudioDialog == null)
			{
				_vfxStudioDialog = new VfxStudioDialog(this);
			}

			_vfxStudioDialog.OpenForConfig(initialConfig, onApplied);
		}

		public void OpenObjectAttachmentDialog(
			string? unitId = null,
			string? attachmentId = null,
			string hand = "RightHand",
			Node3D? sourceModel = null,
			Action<HandAttachmentOrientation?>? onApplied = null)
		{
			if (_objectAttachmentDialog == null)
			{
				_objectAttachmentDialog = new ObjectAttachmentDialog(this);
			}

			_objectAttachmentDialog.OpenForUnitAndAttachment(unitId, attachmentId, hand, sourceModel, onApplied);
		}

		public void RemoveUnitObjectAttachment(string unitId, string socket, string attachmentId, string? parentAttachmentId = null)
		{
			try
			{
				if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return;

				string regKey = GameContext.EditorService.RemoveUnitObjectAttachmentDomain(unitId, socket, attachmentId, parentAttachmentId);

				if (GameContext.Host != null)
				{
					if (GameContext.Host.AllUnits != null)
					{
						foreach (var u in GameContext.Host.AllUnits)
						{
							if (u != null && (u.UnitId == unitId || u.UnitId == regKey || Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase)))
							{
								u.ApplyAllConfiguredAttachments();
							}
						}
					}

					if (GameContext.Host.SelectedEditorObject is Unit3D selUnit)
					{
						if (selUnit.UnitId == unitId || selUnit.UnitId == regKey || Path.GetFileNameWithoutExtension(selUnit.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase))
						{
							selUnit.ApplyAllConfiguredAttachments();
						}
					}
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] RemoveUnitObjectAttachment error: {ex.Message}");
			}
		}

		public void RestoreUnitObjectAttachments(string targetId, UnitObjectAttachments? snapshot)
		{
			try
			{
				if (string.IsNullOrEmpty(targetId)) return;

				string regKey = GameContext.EditorService.RestoreUnitObjectAttachmentsDomain(targetId, snapshot);

				if (GameContext.Host != null)
				{
					if (GameContext.Host.AllUnits != null)
					{
						foreach (var u in GameContext.Host.AllUnits)
						{
							if (u != null && (u.UnitId == targetId || u.UnitId == regKey || Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase)))
							{
								u.ApplyAllConfiguredAttachments();
							}
						}
					}

					if (GameContext.Host.SelectedEditorObject is Unit3D selUnit)
					{
						if (selUnit.UnitId == targetId || selUnit.UnitId == regKey || Path.GetFileNameWithoutExtension(selUnit.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase))
						{
							selUnit.ApplyAllConfiguredAttachments();
						}
					}
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] RestoreUnitObjectAttachments error: {ex.Message}");
			}
		}

		public void RestoreUnitPseudoSockets(string targetId, List<PseudoSocketAnchor>? snapshot)
		{
			try
			{
				if (string.IsNullOrEmpty(targetId)) return;

				string regKey = GameContext.EditorService.RestoreUnitPseudoSocketsDomain(targetId, snapshot);

				if (GameContext.Host != null)
				{
					if (GameContext.Host.AllUnits != null)
					{
						foreach (var u in GameContext.Host.AllUnits)
						{
							if (u != null && (u.UnitId == targetId || u.UnitId == regKey || Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase)))
							{
								u.ApplyAllConfiguredAttachments();
							}
						}
					}

					if (GameContext.Host.SelectedEditorObject is Unit3D selUnit)
					{
						if (selUnit.UnitId == targetId || selUnit.UnitId == regKey || Path.GetFileNameWithoutExtension(selUnit.UnitId ?? "").Equals(Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase))
						{
							selUnit.ApplyAllConfiguredAttachments();
						}
					}
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] RestoreUnitPseudoSockets error: {ex.Message}");
			}
		}

		public void OpenShaderEditorDialog(string shaderKey = "", Action<CustomShaderConfig>? onSaved = null)
		{
			if (_shaderEditorDialog == null)
			{
				_shaderEditorDialog = new ShaderEditorDialog(this);
			}

			_shaderEditorDialog.OpenForShader(shaderKey, onSaved);
		}

		public void OpenProceduralAnimationStudioDialog(ProceduralAnimationConfig? initialConfig = null, Action<ProceduralAnimationConfig>? onApplied = null, string? previewModelKey = null)
		{
			if (_proceduralAnimationStudioDialog == null)
			{
				_proceduralAnimationStudioDialog = new ProceduralAnimationStudioDialog(this);
			}

			_proceduralAnimationStudioDialog.OpenForConfig(initialConfig, onApplied, previewModelKey);
		}

		public void OpenAnimationPreviewDialog(string unitId, string? modelPath = null)
		{
			if (_animationPreviewDialog == null)
			{
				_animationPreviewDialog = new AnimationPreviewDialog(this);
			}

			_animationPreviewDialog.OpenForUnitId(unitId, modelPath);
		}

		// The test button's handler, and the confirmation dialog's callback, both have to be void, and both awaited
		// ProceedToTestMap with no catch: a fault launching the test map was unobserved, so the editor just stayed put.
		private async void TestMapAction()
		{
			try
			{
				await TestMapCoreAsync();
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[MapEditorHUD] Starting the map test failed: {exception}");
			}
		}

		private async Task TestMapCoreAsync()
		{
			if (GameContext.Host == null) return;

			if (GameContext.Host.AllUnits.Count == 0)
			{
				ShowConfirmationDialog(
					"Warning: You have not placed any units, you won't see anything due to Shroud.",
					async () =>
					{
						try
						{
							await ProceedToTestMap();
						}
						catch (Exception exception)
						{
							GameLog.LogError($"[MapEditorHUD] Starting the map test failed: {exception}");
						}
					},
					"Okay",
					"Cancel"
				);
			}
			else
			{
				await ProceedToTestMap();
			}
		}

		public void OpenNoiseTextureDialog(Action<string>? onSaved = null)
		{
			_noiseTextureDialog?.OpenWithCallback(onSaved);
		}

		public void OpenTemplateManagerDialog()
		{
			if (_templateManagerDialog == null)
			{
				_templateManagerDialog = new TemplateManagerDialog(this);
			}

			_templateManagerDialog.OpenDialog();
		}

		public void OpenEditorSettingsDialog()
		{
			_editorSettingsDialog?.OpenDialog();
		}

		public void OpenMapVersionsDialog()
		{
			if (_mapVersionsDialog == null || !GodotObject.IsInstanceValid(_mapVersionsDialog))
			{
				_mapVersionsDialog = new MapVersionsDialog(this);
			}

			_mapVersionsDialog.OpenDialog();
		}

		public void OpenMapHistoryStoresDialog()
		{
			if (_mapHistoryStoresDialog == null || !GodotObject.IsInstanceValid(_mapHistoryStoresDialog))
			{
				_mapHistoryStoresDialog = new MapHistoryStoresDialog(this);
			}

			_mapHistoryStoresDialog.OpenDialog();
		}

		public void NotifyHistoryRestoreApplied(bool versionRecorded)
		{
			_hasChangesSinceLastVersion = !versionRecorded;
			EditorWorkspaceWatcher.UpdateWatchedFileTimestamps();
		}

		public void MarkScriptSourcesChangedByRestore()
		{
			SetWasmConsoleStatus(TranslationServer.Translate("Script sources changed — publish or test the map to compile them again."), UIStyle.ColorCyanGlow);
			ShowFeedback(TranslationServer.Translate("The restored version changed the map script. Publish or test the map to compile it again."));
		}

		public void OpenUtilitiesDialog()
		{
			if (_utilitiesDialog == null)
			{
				_utilitiesDialog = new UtilitiesDialog(this);
			}

			_utilitiesDialog.OpenDialog();
		}
	}
}
