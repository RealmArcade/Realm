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
		private void ProcessMapEditorTick(float fDelta)
		{
			GameContext.EditorService.Clump.TickCooldown(fDelta);

			var mousePos = GetViewport().GetMousePosition();
			var terrainHit = RaycastTerrainFromMouse(mousePos);
			Vector3 hitPos = Vector3.Zero;
			bool hasHit = false;

			if (terrainHit != null && terrainHit.ContainsKey("position"))
			{
				hitPos = terrainHit["position"].AsVector3();
				hasHit = true;
			}
			else
			{
				var camera = GetViewport().GetCamera3D();
				if (camera != null)
				{
					var from = camera.ProjectRayOrigin(mousePos);
					var normal = camera.ProjectRayNormal(mousePos);
					if (Mathf.Abs(normal.Y) > 0.0001f)
					{
						float t = (0.0f - from.Y) / normal.Y;
						hitPos = from + normal * t;
						hasHit = true;
					}
				}
			}

			if (hasHit)
			{
				UpdateBrushIndicator(hitPos);
				UpdateEditorPreview(hitPos);
				HandleMapEditorTerrainHit(hitPos);
				HandleMapEditorMouseHover(mousePos);
				HandleMapEditorClickEvents(hitPos, mousePos, fDelta);
			}
			else
			{
				ClearEditorPreviewState();
			}

			ProcessMapEditorPhysics(fDelta);
		}

		private void HandleMapEditorTerrainHit(Vector3 hitPos)
		{
			if (GroundTerrain == null) return;

			if (ProcessObjectMarqueeTool(hitPos)) return;
			if (ProcessAreaSelectionTool(hitPos)) return;
			if (ProcessCoordinateTool(hitPos)) return;
			if (ProcessPasteTool(hitPos)) return;
			if (ProcessMeasureTool(hitPos)) return;
		}

		private bool ProcessObjectMarqueeTool(Vector3 hitPos)
		{
			var editorService = GameContext.EditorService;
			if (ActiveEditorTool != EditorTool.SelectMove) return false;
			if (!editorService.Interaction.IsObjectMarqueeSelecting || editorService.Interaction.ObjectMarqueeStart == null) return false;

			var (cellX, cellZ) = EditorGridSnapping.WorldPosToCellCoords(hitPos);
			editorService.Interaction.SetObjectMarquee(editorService.Interaction.ObjectMarqueeStart, new Vector2I(cellX, cellZ), true);
			UpdateEditorObjectMarqueeVisual();
			return true;
		}

		private bool ProcessAreaSelectionTool(Vector3 hitPos)
		{
			if (ActiveEditorTool == EditorTool.SelectArea && GameContext.EditorService.Interaction.IsSelectingArea && GameContext.EditorService.Interaction.SelectionStart != null)
			{
				HandleSelectAreaHit(hitPos);
				return true;
			}

			return false;
		}

		private bool ProcessCoordinateTool(Vector3 hitPos)
		{
			if (ActiveEditorTool == EditorTool.DrawCoordinate && GameContext.EditorService.Interaction.IsSelectingArea && GameContext.EditorService.Interaction.SelectionStart != null)
			{
				HandleDrawCoordinateHit(hitPos);
				return true;
			}

			return false;
		}

		private bool ProcessPasteTool(Vector3 hitPos)
		{
			if (ActiveEditorTool == EditorTool.PasteArea && GameContext.EditorService.Clipboard.HasCopiedArea)
			{
				HandlePasteAreaHit(hitPos);
				return true;
			}

			return false;
		}

		private bool ProcessMeasureTool(Vector3 hitPos)
		{
			if (ActiveEditorTool == EditorTool.Measure && EditorTapeMeasureActive && GameContext.EditorService.Tools.TapeMeasureStart.HasValue)
			{
				HandleMeasureHit(hitPos);
				return true;
			}

			return false;
		}

		private void HandleSelectAreaHit(Vector3 hitPos)
		{
			var (cx, cz) = EditorGridSnapping.WorldPosToCellCoords(hitPos);
			GameContext.EditorService.Interaction.SetSelectionEnd(new Vector2I(cx, cz));
			int minX = Mathf.Min(GameContext.EditorService.Interaction.SelectionStart!.Value.X, cx);
			int minZ = Mathf.Min(GameContext.EditorService.Interaction.SelectionStart!.Value.Y, cz);
			int maxX = Mathf.Max(GameContext.EditorService.Interaction.SelectionStart!.Value.X, cx);
			int maxZ = Mathf.Max(GameContext.EditorService.Interaction.SelectionStart!.Value.Y, cz);
			CreateSelectionHighlight();
			RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
		}

		private void HandleDrawCoordinateHit(Vector3 hitPos)
		{
			var (rcx, rcz) = EditorGridSnapping.WorldPosToCellCoords(hitPos);
			GameContext.EditorService.Interaction.SetSelectionEnd(new Vector2I(rcx, rcz));
			int rMinX = Mathf.Min(GameContext.EditorService.Interaction.SelectionStart!.Value.X, rcx);
			int rMinZ = Mathf.Min(GameContext.EditorService.Interaction.SelectionStart!.Value.Y, rcz);
			int rMaxX = Mathf.Max(GameContext.EditorService.Interaction.SelectionStart!.Value.X, rcx);
			int rMaxZ = Mathf.Max(GameContext.EditorService.Interaction.SelectionStart!.Value.Y, rcz);
			UpdateCoordinatePreviewMesh(rMinX, rMinZ, rMaxX, rMaxZ);
		}

		private void HandlePasteAreaHit(Vector3 hitPos)
		{
			var (cx, cz) = EditorGridSnapping.WorldPosToCellCoords(hitPos);
			var (startX, startZ, targetWidth, targetDepth) = EditorGridSnapping.GetAnchoredPasteBounds(GameContext.EditorService.Clipboard.CopiedArea, cx, cz, GameContext.EditorService.Tools.PasteRotation, GameContext.EditorService.Tools.PasteReflectionMode);

			UpdatePasteSelectionHighlights(startX, startZ, targetWidth, targetDepth);

			int srcAnchorX = GameContext.EditorService.Clipboard.CopiedAreaSourceMinX + GameContext.EditorService.Clipboard.CopiedAreaAnchorX;
			int srcAnchorZ = GameContext.EditorService.Clipboard.CopiedAreaSourceMinZ + GameContext.EditorService.Clipboard.CopiedAreaAnchorZ;
			int deltaX = cx - srcAnchorX;
			int deltaZ = cz - srcAnchorZ;

			var ground = RequireGroundTerrain();
			float quadSize = ground.QuadSize;
			float halfW = ground.Width / 2.0f;
			float halfD = ground.Depth / 2.0f;
			Vector2 targetWorld2D = new Vector2((cx - halfW) * quadSize, (cz - halfD) * quadSize);
			Vector2 pivotWorld2D = GameContext.EditorService.Symmetry.Pivot;
			Vector2 diffFromPivot = targetWorld2D - pivotWorld2D;
			float distFromPivot = diffFromPivot.Length();
			float angleFromPivot = Mathf.RadToDeg(Mathf.Atan2(diffFromPivot.Y, diffFromPivot.X));
			if (angleFromPivot < 0) angleFromPivot += 360.0f;

			GameContext.MapEditorHud?.UpdatePasteTelemetry(deltaX, deltaZ, distFromPivot / quadSize, distFromPivot, angleFromPivot);
		}

		private void HandleMeasureHit(Vector3 hitPos)
		{
			EditorTapeMeasureEnd = hitPos;
			UpdateMeasureVisuals(GameContext.EditorService.Tools.TapeMeasureStart!.Value, hitPos);

			Vector3 delta = hitPos - GameContext.EditorService.Tools.TapeMeasureStart!.Value;
			float quadSize = RequireGroundTerrain().QuadSize;
			float dx = delta.X / quadSize;
			float dz = delta.Z / quadSize;
			float dy = delta.Y;
			float eucTiles = Mathf.Sqrt(dx * dx + dz * dz);
			float eucWorld = delta.Length();
			float manhattanTiles = Mathf.Abs(dx) + Mathf.Abs(dz);
			float angleDeg = Mathf.RadToDeg(Mathf.Atan2(delta.Z, delta.X));
			if (angleDeg < 0) angleDeg += 360.0f;
			float slopePct = eucTiles > 0.001f ? (Mathf.Abs(dy) / (eucTiles * quadSize)) * 100.0f : 0.0f;

			GameContext.MapEditorHud?.UpdateMeasureTelemetry(eucTiles, eucWorld, manhattanTiles, dx, dz, dy, angleDeg, slopePct);
		}

		private void HandleMapEditorMouseHover(Vector2 mousePos)
		{
			bool canHover = (ActiveEditorTool == EditorTool.SelectMove && !_isDraggingObject && !GameContext.EditorService.Interaction.IsObjectMarqueeSelecting) ||
			                ActiveEditorTool == EditorTool.DeleteObject ||
			                ActiveEditorTool == EditorTool.Eyedropper;

			Node? newHovered = null;
			if (canHover && !IsMouseOverUI())
			{
				newHovered = ResolveHoveredEditorObject(mousePos);
			}

			if (_hoveredEditorObject != newHovered)
			{
				SetHoverState(_hoveredEditorObject, false);
				_hoveredEditorObject = newHovered;
				SetHoverState(_hoveredEditorObject, true);
			}
		}

		private Node? ResolveHoveredEditorObject(Vector2 mousePos)
		{
			var objectHit = RaycastFromMouse(mousePos);
			var collider = (objectHit != null && objectHit.ContainsKey("collider")) ? objectHit["collider"].As<Node>() : null;

			Node? newHovered = null;
			if (collider != null)
			{
				newHovered = FindUnit3DInParentChain(collider);
				if (newHovered == null) newHovered = collider as PropEntityProxyNode;
				if (newHovered == null) newHovered = FindProp3DInParentChain(collider);
			}

			if (newHovered == null) newHovered = FindDecal3DInParentChain(collider);
			if (newHovered == null) newHovered = FindVfxInParentChain(collider);

			return newHovered;
		}

		private void SetHoverState(Node? node, bool isHovered)
		{
			if (GodotObject.IsInstanceValid(node))
			{
				if (node is Unit3D u) u.IsHovered = isHovered;
				else if (node is PropEntityProxyNode propProxy) SetHoveredPropEntity(isHovered ? propProxy.Entity : Entity.Null, isHovered ? propProxy.PropId : "");
				else if (node is Prop3D p) p.IsHovered = isHovered;
				else if (node is Decal d) UpdateDecalHoverRing(d, isHovered);
				else if (node is ProceduralVfxInstance3D v) v.IsHovered = isHovered;
			}
			else if (!isHovered)
			{
				SetHoveredPropEntity(Entity.Null, "");
			}
		}

		private void HandleMapEditorClickEvents(Vector3 hitPos, Vector2 mousePos, float fDelta)
		{
			if (Input.IsMouseButtonPressed(MouseButton.Left) && !_leftClickInitiatedOverUI && !FloatingDialogBase.HasAnyDialogOpen)
			{
				TryStartDragOperation(mousePos);

				if (!IsMouseOverUI())
				{
					TryHandleClumpSpawn(hitPos);
					TryHandleObjectDragging(hitPos, mousePos);
					TryApplyTerrainEditing(hitPos, fDelta);
				}
			}
			else
			{
				ClearEditorDragAndDrawState();
			}
		}

		private void TryStartDragOperation(Vector2 mousePos)
		{
			if (_is3DLeftClickDown && !_is3DDragOperationActive && mousePos.DistanceTo(_leftClick3DStartPos) > 3.0f)
			{
				_is3DDragOperationActive = true;
				GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(true);
			}
		}

		private void TryHandleClumpSpawn(Vector3 hitPos)
		{
			if ((ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal) && EditorClumpMode)
			{
				if (!GameContext.EditorService.Clump.IsDrawing)
				{
					GameContext.EditorService.Clump.Begin();
				}

				if (GameContext.EditorService.Clump.CanSpawn)
				{
					ApplyGeneralClumpSpawn(hitPos);
					GameContext.EditorService.Clump.SetCooldown(0.15f);
				}
			}
		}

		private void TryHandleObjectDragging(Vector3 hitPos, Vector2 mousePos)
		{
			if (ActiveEditorTool == EditorTool.SelectMove && _isDraggingObject && GodotObject.IsInstanceValid(SelectedEditorObject))
			{
				float mouseDistPx = mousePos.DistanceTo(GameContext.EditorService.Tools.DragStartMousePos);
				if (!GameContext.EditorService.Tools.DragObjectHasMoved && mouseDistPx > 4.0f)
				{
					GameContext.EditorService.Tools.DragObjectHasMoved = true;
					if (!_is3DDragOperationActive)
					{
						_is3DDragOperationActive = true;
						GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(true);
					}
				}

				if (GameContext.EditorService.Tools.DragObjectHasMoved)
				{
					UpdateDraggedObjectPosition(hitPos);
				}
			}
		}

		private void BeginEditorObjectDrag(Vector3 terrainHitPos, Vector2 mousePos)
		{
			var primary = SelectedEditorObject as Node3D;
			if (!GodotObject.IsInstanceValid(primary)) return;

			_isDraggingObject = true;
			_dragObjectStartPos = primary.Position;
			_dragObjectStartRot = primary.RotationDegrees;
			_dragObjectStartScale = primary.Scale;
			GameContext.EditorService.Tools.DragStartMousePos = mousePos;
			GameContext.EditorService.Tools.DragStartGroundPos = terrainHitPos;
			GameContext.EditorService.Tools.DragObjectStartHitPos = terrainHitPos;
			GameContext.EditorService.Tools.DragObjectHasMoved = false;

			_dragSelectionStartTransforms.Clear();
			var selection = _editorObjectSelection;
			for (int i = 0; i < selection.Count; i++)
			{
				if (selection[i] is Node3D node3D && GodotObject.IsInstanceValid(node3D))
				{
					_dragSelectionStartTransforms.Add((node3D, node3D.Position, node3D.RotationDegrees, node3D.Scale));
				}
			}

			_isDraggingSelectionGroup = _dragSelectionStartTransforms.Count > 1;
		}

		private void TryApplyTerrainEditing(Vector3 hitPos, float fDelta)
		{
			bool isTerrainTool = IsTerrainEditingTool(ActiveEditorTool);

			bool firstClick = false;
			if (isTerrainTool && !GameContext.EditorService.IsDrawingTerrain && GroundTerrain != null)
			{
				firstClick = true;
				float targetBlockHeight = ActiveEditorTool == EditorTool.Height ? EditorExactHeight : EditorBlockLevelHeight;
				GameContext.EditorService.TerrainEdit.BeginDraw(hitPos, ActiveEditorTool, EditorBlockMode, targetBlockHeight, null, GroundTerrain.SplatMap, GroundTerrain.PathingCodes, GroundTerrain.CliffSplatMap);
			}

			GameContext.EditorService.ApplyActiveTerrainToolAt(hitPos.X, hitPos.Y, hitPos.Z, fDelta, firstClick);
		}

		private bool IsTerrainEditingTool(EditorTool tool)
		{
			return tool == EditorTool.Raise ||
			       tool == EditorTool.Lower ||
			       tool == EditorTool.Height ||
			       tool == EditorTool.Smooth ||
			       tool == EditorTool.Plateau ||
			       tool == EditorTool.PaintTexture ||
			       tool == EditorTool.Noise ||
			       tool == EditorTool.PaintPathing;
		}

		private void UpdateDraggedObjectPosition(Vector3 hitPos)
		{
			var node3D = SelectedEditorObject as Node3D;
			if (!GodotObject.IsInstanceValid(node3D)) return;

			if (_isDraggingSelectionGroup)
			{
				UpdateDraggedSelectionPositions(hitPos);
				return;
			}

			Vector3 delta = hitPos - GameContext.EditorService.Tools.DragStartGroundPos;
			Vector3 dragPos = _dragObjectStartPos + delta;
			if (EditorSnapToGrid && GroundTerrain != null)
			{
				dragPos = EditorGridSnapping.SnapToGrid(dragPos);
			}

			float authoredYOffset = _dragObjectStartPos.Y - GameContext.EditorService.GetTerrainHeightAt(_dragObjectStartPos);
			dragPos.Y = GameContext.EditorService.GetTerrainHeightAt(dragPos) + (Mathf.Abs(authoredYOffset) < 0.05f ? 0f : authoredYOffset);
			node3D.Position = dragPos;
			ApplyDraggedNodePosition(node3D, dragPos);

			GameContext.MapEditorHud?.UpdateSelectedObjectInfo();
		}

		private void UpdateDraggedSelectionPositions(Vector3 hitPos)
		{
			Vector3 delta = hitPos - GameContext.EditorService.Tools.DragStartGroundPos;
			Vector3 dragPos = _dragObjectStartPos + delta;
			if (EditorSnapToGrid && GroundTerrain != null)
			{
				dragPos = EditorGridSnapping.SnapToGrid(dragPos);
			}

			Vector3 offset = dragPos - _dragObjectStartPos;

			for (int i = 0; i < _dragSelectionStartTransforms.Count; i++)
			{
				var start = _dragSelectionStartTransforms[i];
				if (!GodotObject.IsInstanceValid(start.Node)) continue;

				Vector3 newPosition = start.Position + offset;
				start.Node.Position = newPosition;
				ApplyDraggedNodePosition(start.Node, newPosition);
			}

			GameContext.MapEditorHud?.UpdateSelectedObjectInfo();
		}

		private void ApplyDraggedNodePosition(Node3D node3D, Vector3 dragPos)
		{
			if (node3D is Unit3D unit)
			{
				UpdateDraggedUnitECS(unit, dragPos);
			}
			else if (node3D is PropEntityProxyNode propProxy)
			{
				UpdateDraggedPropProxyECS(propProxy, dragPos);
			}
			else if (node3D is Prop3D prop)
			{
				UpdateDraggedPropECS(prop, dragPos);
			}
			else if (node3D is Decal3D decal3D)
			{
				UpdateDraggedDecalECS(decal3D, dragPos);
			}
			else if (node3D is ProceduralVfxInstance3D vfxInst)
			{
				UpdateDraggedVfxECS(vfxInst, dragPos);
			}
		}

		private void UpdateDraggedUnitECS(Unit3D unit, Vector3 dragPos)
		{
			if (World.IsAlive(unit.Entity))
			{
				World.Set(unit.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
				SyncPreviousTransform(World, unit.Entity);
				UpdateEditorCoverageOverlay();
			}
		}

		private void UpdateDraggedPropProxyECS(PropEntityProxyNode propProxy, Vector3 dragPos)
		{
			if (GameContext.World is { } propWorld && propWorld.IsAlive(propProxy.Entity))
			{
				propWorld.Set(propProxy.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
				GameContext.PropMultiMesh?.UpdateInstanceTransform(propProxy.Entity, dragPos, propProxy.RotationDegrees.Y, propProxy.Scale.X);
				UpdateSelectedPropRingPosition(dragPos);
			}
		}

		private void UpdateDraggedPropECS(Prop3D prop, Vector3 dragPos)
		{
			if (World.IsAlive(prop.Entity))
			{
				World.Set(prop.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
				SyncPreviousTransform(World, prop.Entity);
			}

			GameContext.PropMultiMesh?.MarkDirty(prop.PropId);
		}

		private void UpdateDraggedDecalECS(Decal3D decal3D, Vector3 dragPos)
		{
			if (World.IsAlive(decal3D.Entity))
			{
				World.Set(decal3D.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
				SyncPreviousTransform(World, decal3D.Entity);
			}
		}

		private void UpdateDraggedVfxECS(ProceduralVfxInstance3D vfxInst, Vector3 dragPos)
		{
			if (World.IsAlive(vfxInst.Entity))
			{
				World.Set(vfxInst.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
				SyncPreviousTransform(World, vfxInst.Entity);
			}
		}

		private void ClearEditorDragAndDrawState()
		{
			Clear3DDragOperationState();
			ClearClumpSessionState();

			GameContext.EditorService.TerrainEdit.ResetDrawState();

			ClearDraggingState();
			ClearSelectionState();
			ClearTerrainDrawingState();
		}

		private void Clear3DDragOperationState()
		{
			if (_is3DDragOperationActive)
			{
				_is3DDragOperationActive = false;
				_is3DLeftClickDown = false;
				GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(false);
			}
		}

		private void ClearClumpSessionState()
		{
			if (GameContext.EditorService.Clump.IsDrawing)
			{
				var composite = GameContext.EditorService.Clump.End();
				if (composite != null)
				{
					EditorHistoryManager.RecordAction(composite);
					EditorHasUnsavedChanges = true;
				}
			}
		}

		private void ClearDraggingState()
		{
			if (_isDraggingObject)
			{
				_isDraggingObject = false;

				if (_isDraggingSelectionGroup)
				{
					RecordSelectionGroupDragAction();
					_isDraggingSelectionGroup = false;
					_dragSelectionStartTransforms.Clear();
					return;
				}

				if (GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					if (SelectedEditorObject is not Node3D node3D) return;
					if (SelectedEditorObject is PropEntityProxyNode propProxy)
					{
						GameContext.PropMultiMesh?.MarkDirty(propProxy.PropId);
					}
					else if (SelectedEditorObject is Prop3D prop && SelectedEditorObject is not Unit3D)
					{
						GameContext.PropMultiMesh?.MarkDirty(prop.PropId);
					}

					if (node3D.Position.DistanceTo(_dragObjectStartPos) > 0.05f)
					{
						var action = new ObjectTransformAction(node3D, _dragObjectStartPos, node3D.Position, _dragObjectStartRot, node3D.RotationDegrees, _dragObjectStartScale, node3D.Scale);
						EditorHistoryManager.RecordAction(action);
						GameContext.MapEditorHud?.ShowFeedbackExternal("Moved Object");
						EditorHasUnsavedChanges = true;
					}
				}
			}
		}

	}
}