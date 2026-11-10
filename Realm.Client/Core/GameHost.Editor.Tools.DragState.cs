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
		private void RecordSelectionGroupDragAction()
		{
			var actions = new List<IEditorAction>();

			for (int i = 0; i < _dragSelectionStartTransforms.Count; i++)
			{
				var start = _dragSelectionStartTransforms[i];
				if (!GodotObject.IsInstanceValid(start.Node)) continue;
				if (start.Node.Position.DistanceTo(start.Position) <= 0.05f) continue;

				if (start.Node is PropEntityProxyNode proxy)
				{
					GameContext.PropMultiMesh?.MarkDirty(proxy.PropId);
				}
				else if (start.Node is Prop3D prop)
				{
					GameContext.PropMultiMesh?.MarkDirty(prop.PropId);
				}

				actions.Add(new ObjectTransformAction(start.Node, start.Position, start.Node.Position, start.Rotation, start.Node.RotationDegrees, start.Scale, start.Node.Scale));
			}

			if (actions.Count == 0) return;

			EditorHistoryManager.RecordAction(new CompositeAction(actions));
			GameContext.MapEditorHud?.ShowFeedbackExternal(string.Format("Moved {0} Objects", actions.Count));
			EditorHasUnsavedChanges = true;
		}

		private void ClearSelectionState()
		{
			if (GameContext.EditorService.Interaction.IsSelectingArea)
			{
				GameContext.EditorService.Interaction.SetIsSelectingArea(false);
			}

			if (GameContext.EditorService.Interaction.IsObjectMarqueeSelecting)
			{
				FinalizeEditorObjectMarquee();
			}
			else if (GameContext.EditorService.Interaction.ObjectMarqueeStart != null)
			{
				GameContext.EditorService.Interaction.ClearObjectMarquee();
				HideEditorObjectMarquee();
			}
		}

		private void FinalizeEditorObjectMarquee()
		{
			var editorService = GameContext.EditorService;
			if (!editorService.Interaction.IsObjectMarqueeSelecting || editorService.Interaction.ObjectMarqueeStart == null || editorService.Interaction.ObjectMarqueeEnd == null)
			{
				editorService.Interaction.ClearObjectMarquee();
				HideEditorObjectMarquee();
				return;
			}

			Vector2I start = editorService.Interaction.ObjectMarqueeStart.Value;
			Vector2I end = editorService.Interaction.ObjectMarqueeEnd.Value;
			int minX = Mathf.Min(start.X, end.X);
			int minZ = Mathf.Min(start.Y, end.Y);
			int maxX = Mathf.Max(start.X, end.X);
			int maxZ = Mathf.Max(start.Y, end.Y);

			bool additive = Input.IsKeyPressed(Key.Shift);
			bool subtractive = Input.IsKeyPressed(Key.Ctrl);
			var selection = new List<Node>();
			if (additive || subtractive)
			{
				selection.AddRange(_editorObjectSelection);
			}

			CollectMarqueeSelection(AllUnits, minX, minZ, maxX, maxZ, subtractive, selection);
			CollectMarqueeSelection(AllProps, minX, minZ, maxX, maxZ, subtractive, selection);
			CollectMarqueeSelection(AllDecals, minX, minZ, maxX, maxZ, subtractive, selection);
			CollectMarqueeSelection(AllVfx, minX, minZ, maxX, maxZ, subtractive, selection);

			SetEditorObjectSelection(selection);
			editorService.Interaction.ClearObjectMarquee();
			HideEditorObjectMarquee();
			GameContext.MapEditorHud?.ShowFeedbackExternal(string.Format("{0} Objects Selected", selection.Count));
		}

		private void CollectMarqueeSelection<T>(List<T> candidates, int minX, int minZ, int maxX, int maxZ, bool subtractive, List<Node> selection) where T : Node
		{
			if (candidates == null) return;

			for (int i = 0; i < candidates.Count; i++)
			{
				T candidate = candidates[i];
				if (candidate == null || !GodotObject.IsInstanceValid(candidate)) continue;
				if (candidate is not Node3D node3D) continue;

				var (cellX, cellZ) = EditorGridSnapping.WorldPosToCellCoords(node3D.Position);
				if (cellX < minX || cellX > maxX || cellZ < minZ || cellZ > maxZ) continue;

				if (subtractive)
				{
					selection.Remove(candidate);
				}
				else if (!selection.Contains(candidate))
				{
					selection.Add(candidate);
				}
			}
		}

		private void ClearTerrainDrawingState()
		{
			if (!GameContext.EditorService.IsDrawingTerrain) return;

			if (GroundTerrain != null && GroundTerrain.Cells != null && GroundTerrain.SplatMap != null && GroundTerrain.PathingCodes != null)
			{
				var action = GameContext.EditorService.TerrainEdit.EndDraw(null, GroundTerrain.SplatMap, GroundTerrain.PathingCodes, GroundTerrain.CliffSplatMap);
				EditorHistoryManager.RecordAction(action);

				if (IsTerrainHeightsTool(ActiveEditorTool))
				{
					UpdatePathingOverlay();
				}

				GameContext.EditorService.FlushTerrainMeshAndPhysics();
				EditorHasUnsavedChanges = true;
			}
			else
			{
				GameContext.EditorService.TerrainEdit.EndDraw(null, null, null, null);
			}
		}

		private bool IsTerrainHeightsTool(EditorTool tool)
		{
			return tool == EditorTool.Raise ||
			       tool == EditorTool.Lower ||
			       tool == EditorTool.Height ||
			       tool == EditorTool.Smooth ||
			       tool == EditorTool.Plateau ||
			       tool == EditorTool.Noise ||
			       tool == EditorTool.PaintPathing;
		}

		private void ClearEditorPreviewState()
		{
			if (_is3DDragOperationActive)
			{
				_is3DDragOperationActive = false;
				_is3DLeftClickDown = false;
				GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(false);
			}

			_paintCursor?.HideCursor();
			ClearEditorPreview();

			ClearEditorDragAndDrawState();
		}

	}
}
