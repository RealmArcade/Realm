using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Array = Godot.Collections.Array;

namespace Realm.Client.Core
{
	public partial class GameHost
	{
		public void UpdateCameraBoundsOverlayVisibility()
		{
			if (_cameraBoundsOverlayMesh == null) return;
			_cameraBoundsOverlayMesh.Visible = GameContext.SessionState.IsMapEditorMode && GameContext.EditorService.Tools.CameraBoundsVisible;
			if (_cameraBoundsOverlayMesh.Visible)
			{
				RebuildCameraBoundsOverlay();
			}
		}

		public void UpdatePathingOverlay()
		{
			bool isPathingTool = ActiveEditorTool == EditorTool.PaintPathing || ActiveEditorTool == EditorTool.FloodFillPathing;
			bool isClipboardTool = ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea;

			bool shouldBeVisible = GameContext.SessionState.IsMapEditorMode && PathingOverlayVisible && (isPathingTool || (isClipboardTool && PasteOptionPathing));

			if (GroundTerrain != null)
			{
				GroundTerrain.SetPathingVisible(shouldBeVisible);
				if (shouldBeVisible)
				{
					RebuildPathingOverlay();
				}
			}
		}

		public void BeginMinimapCapture()
		{
			_wasSelectionHighlightVisible = HideAndCacheNodeVisibility(_selectionHighlightMesh);
			HideAndCacheSymmetryHighlights();
			_wasCameraBoundsVisible = HideAndCacheNodeVisibility(_cameraBoundsOverlayMesh);
			_wasMeasureMeshVisible = HideAndCacheNodeVisibility(_measureMeshInstance);
			_wasSymmetryPivotVisible = HideAndCacheNodeVisibility(_symmetryPivotMarkerMesh);
			_wasCoordinatePreviewVisible = HideAndCacheNodeVisibility(_coordinatePreviewMesh);
			_wasCoordinateOutlineVisible = HideAndCacheNodeVisibility(_coordinateSelectionOutlineMesh);
			_wasScaleSilhouetteVisible = HideAndCacheNodeVisibility(_scaleMapSilhouetteMesh);
			_wasCoverageOverlayVisible = HideAndCacheNodeVisibility(_editorCoverageOverlayRoot);
			HideVfxEditorBaseRings();
		}

		private bool HideAndCacheNodeVisibility(Node3D? node)
		{
			bool wasVisible = false;
			if (node == null || !GodotObject.IsInstanceValid(node)) return wasVisible;

			wasVisible = node.Visible;
			if (wasVisible)
			{
				node.Visible = false;
			}

			return wasVisible;
		}

		private void HideAndCacheNodeVisibility(Node3D? node, out bool wasVisible)
		{
			wasVisible = HideAndCacheNodeVisibility(node);
		}

		private void HideAndCacheSymmetryHighlights()
		{
			_wasSymmetryHighlightsVisible.Clear();
			if (_symmetryHighlightMeshes == null) return;

			foreach (var symMesh in _symmetryHighlightMeshes)
			{
				bool vis = symMesh != null && GodotObject.IsInstanceValid(symMesh) && symMesh.Visible;
				_wasSymmetryHighlightsVisible.Add(vis);
				if (vis && symMesh != null)
				{
					symMesh.Visible = false;
				}
			}
		}

		private void HideVfxEditorBaseRings()
		{
			if (AllVfx == null) return;
			foreach (var vfx in AllVfx)
			{
				if (vfx != null && GodotObject.IsInstanceValid(vfx))
				{
					vfx.SetEditorBaseRingVisible(false);
				}
			}
		}

		public void EndMinimapCapture()
		{
			ShowVfxEditorBaseRings();
			RestoreNodeVisibility(_selectionHighlightMesh, _wasSelectionHighlightVisible);
			RestoreSymmetryHighlights();
			RestoreNodeVisibility(_cameraBoundsOverlayMesh, _wasCameraBoundsVisible);
			RestoreNodeVisibility(_measureMeshInstance, _wasMeasureMeshVisible);
			RestoreNodeVisibility(_symmetryPivotMarkerMesh, _wasSymmetryPivotVisible);
			RestoreNodeVisibility(_coordinatePreviewMesh, _wasCoordinatePreviewVisible);
			RestoreNodeVisibility(_coordinateSelectionOutlineMesh, _wasCoordinateOutlineVisible);
			RestoreNodeVisibility(_scaleMapSilhouetteMesh, _wasScaleSilhouetteVisible);
			RestoreNodeVisibility(_editorCoverageOverlayRoot, _wasCoverageOverlayVisible);
		}

		private void RestoreNodeVisibility(Node3D? node, bool wasVisible)
		{
			if (node != null && GodotObject.IsInstanceValid(node))
			{
				node.Visible = wasVisible;
			}
		}

		private void RestoreSymmetryHighlights()
		{
			if (_symmetryHighlightMeshes == null || _wasSymmetryHighlightsVisible == null) return;

			for (int i = 0; i < _wasSymmetryHighlightsVisible.Count && i < _symmetryHighlightMeshes.Count; i++)
			{
				var symMesh = _symmetryHighlightMeshes[i];
				if (symMesh != null && GodotObject.IsInstanceValid(symMesh))
				{
					symMesh.Visible = _wasSymmetryHighlightsVisible[i];
				}
			}
		}

		private void ShowVfxEditorBaseRings()
		{
			if (AllVfx == null) return;
			foreach (var vfx in AllVfx)
			{
				if (vfx != null && GodotObject.IsInstanceValid(vfx))
				{
					vfx.SetEditorBaseRingVisible(true);
				}
			}
		}
	}
}
