using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;

namespace Realm.Client.Core
{
	public partial class GameHost
	{
		public void StartMapEditorMode()
		{
			GameContext.ReplayPlayback.StopReplay();
			EndJoinedMatch();
			GameContext.SessionState.IsMapEditorMode = true;
			if (GameContext.SessionState.EditorWorldState != null && !string.IsNullOrEmpty(GameContext.SessionState.SavedEditorDirectory))
			{
				GameContext.EditorService.SetActiveMapDirectory(GameContext.SessionState.SavedEditorDirectory);
				GameContext.EditorService.SetCurrentSourceFolder(GameContext.SessionState.SavedEditorSourceFolder);
			}

			GameContext.EditorService.EnsureEditingDirectoryDomain();

			string wsPath = GameContext.EditorService._editingDirectory;
			if (!string.IsNullOrEmpty(wsPath))
			{
				try
				{
					Directory.CreateDirectory(wsPath);
					MapWorkspaceService.SetupWorkspace(wsPath, "MapScript");
				}
				catch (Exception ex)
				{
					GameLog.LogError($"Failed setting up map workspace: {ex.Message}");
				}
			}

			ActiveEditorTool = EditorTool.None;
			EditorHistoryManager.Clear();

			GameContext.SessionState.PendingNewMapOnEditorOpen = false;

			if (GameContext.SessionState.ReturningFromTest)
			{
				RestoreMapEditorStateFromTest();
			}
			else
			{
				ResetWorldAndState();
				ClearMapEntirely();
				GameContext.SessionState.PendingNewMapOnEditorOpen = true;
			}

			CreateBrushIndicator();
			UpdateGridOverlayVisibility();
			InitializeCameraBoundsOverlay();
			UpdateEditorShadows();
			UpdateDayNightVisuals(0.0f);

			if (AllVfx != null)
			{
				foreach (var vfx in AllVfx)
				{
					if (vfx != null && GodotObject.IsInstanceValid(vfx))
					{
						vfx.SetEditorBaseRingVisible(true);
					}
				}
			}
		}

		private void RestoreMapEditorStateFromTest()
		{
			try
			{
				GameContext.SessionState.ReturningFromTest = false;

				MapStateSnapshot? editorState = GameContext.SessionState.EditorWorldState;
				if (editorState == null)
					throw new InvalidOperationException("The editor's world was not kept before the playtest that is ending.");

				GameContext.SessionState.EditorWorldState = null;

				EndJoinedMatch();
				CreateGround();
				MapStateSnapshot.Restore(editorState, updateMapSize: false);
				RestoreEditorDirtyStateAfterTest();

				GameContext.EditorService.Tools.GridMode = GameContext.SessionState.SavedGridMode;
				GameContext.EditorService.Tools.CameraBoundsVisible = GameContext.SessionState.SavedCameraBoundsVisible;
				GameContext.EditorService.Tools.DisableShadows = GameContext.SessionState.SavedDisableShadows;

				var camera = MainCamera;
				if (camera != null)
				{
					camera.Position = GameContext.SessionState.SavedCameraPosition;
					if (GameContext.World is { } restoreWorld && restoreWorld.IsAlive(GameContext.Simulation.WorldEntity) && restoreWorld.Has<CameraState>(GameContext.Simulation.WorldEntity))
					{
						ref var state = ref restoreWorld.Get<CameraState>(GameContext.Simulation.WorldEntity);
						state.TargetHeight = GameContext.SessionState.SavedTargetHeight;
						state.CurrentHeight = GameContext.SessionState.SavedTargetHeight;
						state.TargetYaw = GameContext.SessionState.SavedTargetYaw;
						state.CurrentYaw = GameContext.SessionState.SavedTargetYaw;
						state.TargetPitch = GameContext.SessionState.SavedTargetPitch;
						state.CurrentPitch = GameContext.SessionState.SavedTargetPitch;
						state.IsTopDown = GameContext.SessionState.SavedIsTopDown;
						state.YawSwing = GameContext.SessionState.SavedYawSwing;
						state.PitchSwing = GameContext.SessionState.SavedPitchSwing;
					}
				}

				GameContext.SessionState.EditorSessionResumedAfterTest = true;
			}
			catch (Exception ex)
			{
				GameLog.LogError($"Failed to put the editor's world back after the playtest: {ex.Message}. Resetting to a blank map.");
				GameContext.SessionState.EditorWorldState = null;
				GameContext.SessionState.ReturningFromTest = false;
				ResetWorldAndState();
				ClearMapEntirely();
			}
		}

		private void RestoreEditorDirtyStateAfterTest()
		{
			string editingDirectory = GameContext.EditorService._editingDirectory;

			EditorHasUnsavedChanges = GameContext.SessionState.SavedEditorHasUnsavedChanges;
			GameContext.EditorService.CurrentDirectoryBlake3 = MapWorkspaceService.ComputeDirectoryBlake3Domain(
				editingDirectory,
				MapEditorHUD.IsIgnoredPath);

			if (string.IsNullOrEmpty(editingDirectory)) return;

			string terrainPath = Path.Combine(editingDirectory, "terrain.json");
			GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(terrainPath);
			GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(Path.Combine(editingDirectory, "metadata.json"));
			EditorWorkspaceWatcher.Start(editingDirectory);
		}

		public void ExitMapEditorMode()
		{
			GameContext.SessionState.IsMapEditorMode = false;
			ActiveEditorTool = EditorTool.None;
			EditorHistoryManager.Clear();
			ClearEditorPreview();
			UpdateEditorShadows();

			if (AllVfx != null)
			{
				foreach (var vfx in AllVfx)
				{
					if (vfx != null && GodotObject.IsInstanceValid(vfx))
					{
						vfx.SetEditorBaseRingVisible(false);
					}
				}
			}

			if (_paintCursor != null)
			{
				_paintCursor.QueueFree();
				_paintCursor = null;
			}

			if (GroundTerrain != null)
			{
				GroundTerrain.SetGridVisible(false);
				GroundTerrain.SetPathingVisible(false);
			}

			if (_cameraBoundsOverlayMesh != null)
			{
				_cameraBoundsOverlayMesh.QueueFree();
				_cameraBoundsOverlayMesh = null;
			}

			HideCoordinateSelectionOutline();
			HideCoordinatePreviewMesh();
			RebuildAllCoordinatePersistentMeshes();

			var groundNode = GetNodeOrNull("Ground");
			if (groundNode != null)
			{
				groundNode.QueueFree();
				RemoveChild(groundNode);
			}

			CreateGround();
		}

		private void CreateBrushIndicator()
		{
			if (_paintCursor != null) return;

			_paintCursorSuppressed = false;
			_paintCursor = new MapEditorPaintCursor();
			_paintCursor.Name = "EditorPaintCursor";
			AddChild(_paintCursor);
			_paintCursor.SetBrushShape(EditorBrushIsSquare);
			_paintCursor.HideCursor();
		}

		public void UpdateBrushMesh()
		{
			_paintCursor?.SetBrushShape(EditorBrushIsSquare);
		}

		public bool IsPaintCursorVisible => _paintCursor != null && GodotObject.IsInstanceValid(_paintCursor) && _paintCursor.Visible;

		public void SetPaintCursorVisible(bool visible)
		{
			if (_paintCursor == null || !GodotObject.IsInstanceValid(_paintCursor)) return;

			_paintCursorSuppressed = !visible;
			if (!visible)
			{
				_paintCursor.HideCursor();
			}
		}

		private void UpdateBrushIndicator(Vector3 position)
		{
			if (_paintCursor == null) return;

			if (_paintCursorSuppressed)
			{
				_paintCursor.HideCursor();
				return;
			}

			bool isVertexTool = IsVertexTool(ActiveEditorTool);
			if (!IsTerrainTool(isVertexTool) || IsMouseOverUI())
			{
				_paintCursor.HideCursor();
				return;
			}

			Vector3 targetPos = GetBrushTargetPosition(position, isVertexTool);
			EditorPaintSurfaceMode? paintMode = ActiveEditorTool == EditorTool.PaintTexture
				? ResolvePaintSurfaceMode()
				: null;

			_paintCursor.ShowAt(targetPos, EditorBrushRadius, paintMode);
		}

		public EditorPaintSurfaceMode ResolvePaintSurfaceMode()
		{
			if (ActiveEditorTool == EditorTool.PaintTexture)
			{
				if (Input.IsKeyPressed(Key.Shift)) return EditorPaintSurfaceMode.GroundOnly;
				if (Input.IsKeyPressed(Key.Ctrl)) return EditorPaintSurfaceMode.CliffOnly;
			}

			return GameContext.MapEditorHud?.GetPaintSurfaceMode() ?? EditorPaintSurfaceMode.GroundAndCliff;
		}

		private bool IsVertexTool(EditorTool tool)
		{
			return tool == EditorTool.Raise ||
			       tool == EditorTool.Lower ||
			       tool == EditorTool.Height ||
			       tool == EditorTool.Smooth ||
			       tool == EditorTool.Plateau ||
			       tool == EditorTool.PaintTexture ||
			       tool == EditorTool.Noise ||
			       tool == EditorTool.Ramp;
		}

		private bool IsTerrainTool(bool isVertexTool)
		{
			return isVertexTool ||
			       ActiveEditorTool == EditorTool.PlacePropClump ||
			       ActiveEditorTool == EditorTool.PaintPathing ||
			       ((ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal) && EditorClumpMode);
		}

		private Vector3 GetBrushTargetPosition(Vector3 position, bool isVertexTool)
		{
			if (GroundTerrain != null)
			{
				if (isVertexTool) return EditorGridSnapping.SnapToVertex(position);
				if (EditorSnapToGrid) return EditorGridSnapping.SnapToGrid(position);
			}

			return position;
		}

	}
}
