using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Realm.Ecs;
using Godot;
using Godot.Collections;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Array = System.Array;
using Realm.Shared.Abilities;

namespace Realm.Client.Core;
public partial class GameHost
{
	private void PerformSingleClickSelection(Vector2 clickPos)
	{
		GameLog.LogToConsole($"[GameHost] PerformSingleClickSelection at screen coordinate: {clickPos}");
		ClearSelectedProp();

		var hit = RaycastFromMouse(clickPos);
		Node? collider = null;
		Unit3D? clickedUnit = null;
		if (hit != null && hit.ContainsKey("collider"))
		{
			collider = hit["collider"].As<Node>();
			clickedUnit = FindUnit3DInParentChain(collider);
		}

		clickedUnit ??= FindUnitNearScreenPoint(clickPos);

		if (clickedUnit == null)
		{
			LootItem3D? clickedLootItem = FindLootItemInParentChain(collider);
			if (clickedLootItem == null && TryRaycastTerrainFromMousePosition(clickPos, out Vector3 groundPoint))
			{
				TryFindLootItemAt(groundPoint, out clickedLootItem);
			}

			if (clickedLootItem != null)
			{
				ClearSelection();
				SelectLootItem(clickedLootItem);
				GameContext.InGameHud?.RefreshUI(SelectedUnits);
				return;
			}

			var camera = GetViewport()?.GetCamera3D();
			if (camera != null && TryPickResourceNodeAtScreenPoint(camera, clickPos, out Entity nodeEntity, out string nodePropId))
			{
				ClearSelection();
				SelectResourceNode(nodeEntity, nodePropId);
				GameContext.InGameHud?.RefreshUI(SelectedUnits);
				return;
			}
		}

		if (clickedUnit == null && collider == null)
		{
			ClearSelection();
			GameContext.InGameHud?.RefreshUI(SelectedUnits);
			return;
		}

		if (clickedUnit != null)
		{
			HandleSingleUnitSelection(clickedUnit, clickPos);
		}
		else
		{
			HandleSinglePropSelection(collider);
		}

		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	private void ClearSelectedProp()
	{
		if (SelectedProp != null && GodotObject.IsInstanceValid(SelectedProp))
		{
			SelectedProp.IsSelected = false;
		}

		SelectedProp = null;
		ClearSelectedResourceNode();
		ClearSelectedLootItem();
	}

	private void HandleSingleUnitSelection(Unit3D clickedUnit, Vector2 clickPos)
	{
		if (!clickedUnit.Visible)
		{
			ClearSelection();
			return;
		}

		var filter = GameContext.PerspectiveFilter;

		if (filter.IsSpectating)
		{
			ClearSelection();
			SelectUnit(clickedUnit);
			return;
		}

		if (Input.IsKeyPressed(Key.Ctrl))
		{
			PerformDoubleClickSelection(clickPos);
			return;
		}

		// A unit this screen cannot command is still selectable so its details can be read, whether it belongs to an ally
		// or an enemy. Every order path skips a unit this screen does not own, so the selection stays read-only.
		if (!filter.CanControl(clickedUnit.Player))
		{
			ClearSelection();
			SelectUnit(clickedUnit);
			return;
		}

		bool shiftPressed = Input.IsKeyPressed(Key.Shift);
		bool selectingViewOnlyUnits = SelectedUnits.Count > 0 && !filter.CanControl(SelectedUnits[0].Player);

		if (selectingViewOnlyUnits || !shiftPressed)
		{
			ClearSelection();
			SelectUnit(clickedUnit);
		}
		else
		{
			if (SelectedUnits.Contains(clickedUnit)) DeselectUnit(clickedUnit);
			else SelectUnit(clickedUnit);
		}
	}

	private void HandleSinglePropSelection(Node? collider)
	{
		var clickedProp = FindProp3DInParentChain(collider);
		if (clickedProp != null && clickedProp.Visible && clickedProp.IsResource)
		{
			ClearSelection();
			SelectedProp = clickedProp;
			SelectedProp.IsSelected = true;
		}
		else
		{
			ClearSelection();
		}
	}

	private void PerformBoxSelection(Vector2 start, Vector2 end)
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		var dragRect = CreateDragRect(start, end);
		HandleBoxSelectionState();

		var commandableUnits = new List<Unit3D>();
		var viewOnlyUnits = new List<Unit3D>();
		CollectUnitsInRect(camera, dragRect, commandableUnits, viewOnlyUnits);

		// A box that catches anything of this screen's own is a command in the making, so it takes only what can be
		// commanded. A box that catches nothing of its own is a look: every visible unit it sweeps is selected, whatever
		// player owns it, so allied and enemy units alike can be read. Every order path skips a unit this screen does not
		// own, so the selection stays read-only.
		if (commandableUnits.Count > 0)
		{
			SelectUnits(commandableUnits);
		}
		else if (viewOnlyUnits.Count > 0)
		{
			// A shift-drag that sweeps more units extends what is being read, but it never turns a command into a look:
			// a selection this screen can order around is dropped rather than mixed with units it cannot.
			if (SelectedUnits.Count > 0 && GameContext.PerspectiveFilter.CanControl(SelectedUnits[0].Player))
			{
				ClearSelection();
			}

			SelectUnits(viewOnlyUnits);
		}
		else
		{
			ClearSelection();
			SelectResourcePropInRect(camera, dragRect);
		}

		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	private Rect2 CreateDragRect(Vector2 start, Vector2 end)
	{
		Vector2 min = new Vector2(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
		Vector2 max = new Vector2(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y));
		return new Rect2(min, max - min);
	}

	private void HandleBoxSelectionState()
	{
		bool shiftPressed = Input.IsKeyPressed(Key.Shift);
		bool selectingViewOnlyUnits = SelectedUnits.Count > 0 && !GameContext.PerspectiveFilter.CanControl(SelectedUnits[0].Player);

		if (selectingViewOnlyUnits || !shiftPressed)
		{
			ClearSelection();
		}
	}

	private void CollectUnitsInRect(Camera3D camera, Rect2 dragRect, List<Unit3D> commandableUnits, List<Unit3D> viewOnlyUnits)
	{
		var filter = GameContext.PerspectiveFilter;

		foreach (var unit in AllUnits)
		{
			if (!IsValidVisibleUnit(unit)) continue;

			var screenPos = camera.UnprojectPosition(unit.GlobalPosition);
			if (!dragRect.HasPoint(screenPos)) continue;

			if (filter.IsSpectating)
			{
				if (filter.CanObserve(unit.Player)) commandableUnits.Add(unit);
				continue;
			}

			if (filter.CanControl(unit.Player))
			{
				if (IsSelectableByLocalPlayer(unit)) commandableUnits.Add(unit);
				continue;
			}

			viewOnlyUnits.Add(unit);
		}
	}

	private bool IsValidVisibleUnit([NotNullWhen(true)] Unit3D? unit)
	{
		return unit != null && GodotObject.IsInstanceValid(unit) && unit.Visible;
	}

	private bool IsSelectableByLocalPlayer(Unit3D unit)
	{
		if (unit == null) return false;
		var filter = GameContext.PerspectiveFilter;
		return filter.IsConfigured && filter.CanControl(unit.Player);
	}

	private void SelectUnits(List<Unit3D> units)
	{
		foreach (var unit in units)
		{
			SelectUnit(unit);
		}
	}

	private void SelectResourcePropInRect(Camera3D camera, Rect2 dragRect)
	{
		Entity selectedEntity = Entity.Null;
		string selectedPropId = string.Empty;
		var query = QueryCache.AllPositionAndResourceNodeAndPropIdentityQuery;
		World.Query(in query, (Entity entity, ref Position position, ref PropIdentity identity) =>
		{
			if (selectedEntity != Entity.Null) return;

			var worldPoint = new Vector3(position.Value.X, position.Value.Y + ResourceNodePickHeight, position.Value.Z);
			if (camera.IsPositionBehind(worldPoint)) return;

			var screenPos = camera.UnprojectPosition(worldPoint);
			if (!dragRect.HasPoint(screenPos)) return;

			selectedEntity = entity;
			selectedPropId = identity.PropId;
		});

		if (selectedEntity == Entity.Null) return;

		ClearSelection();
		SelectResourceNode(selectedEntity, selectedPropId);
	}

	private const float ResourceNodePickPixels = 26f;
	private const float ResourceNodePickHeight = 1.5f;

	/// <summary>
	///     Finds the resource node a click landed on. A node is drawn by a shared multi mesh, so it has no collider to
	///     hit; the click is matched against the screen position of every live node instead, and the nearest node within a
	///     small pixel radius is taken. A click on the ground at a node's feet is matched through the same reach the gather
	///     order uses, so clicking either the trunk or the ground beside it selects the node.
	/// </summary>
	/// <param name="camera">The camera the board is seen through.</param>
	/// <param name="screenPosition">The click, in screen coordinates.</param>
	/// <param name="nodeEntity">The node the click landed on, or <see cref="Entity.Null" />.</param>
	/// <param name="propId">The template the node is placed from.</param>
	/// <returns>True when the click found a resource node.</returns>
	private bool TryPickResourceNodeAtScreenPoint(Camera3D camera, Vector2 screenPosition, out Entity nodeEntity, out string propId)
	{
		nodeEntity = Entity.Null;
		propId = string.Empty;
		Entity foundEntity = Entity.Null;
		string foundPropId = string.Empty;
		float closestWorldDistance = float.MaxValue;

		var query = QueryCache.AllPositionAndResourceNodeAndPropIdentityQuery;
		World.Query(in query, (Entity entity, ref Position position, ref PropIdentity identity) =>
		{
			var worldPoint = new Vector3(position.Value.X, position.Value.Y + ResourceNodePickHeight, position.Value.Z);
			if (camera.IsPositionBehind(worldPoint)) return;
			if (camera.UnprojectPosition(worldPoint).DistanceTo(screenPosition) > ResourceNodePickPixels) return;

			float worldDistance = camera.GlobalPosition.DistanceSquaredTo(worldPoint);
			if (worldDistance >= closestWorldDistance) return;

			closestWorldDistance = worldDistance;
			foundEntity = entity;
			foundPropId = identity.PropId;
		});

		if (foundEntity != Entity.Null)
		{
			nodeEntity = foundEntity;
			propId = foundPropId;
			return true;
		}
		if (!TryRaycastTerrainFromMousePosition(screenPosition, out Vector3 groundPoint)) return false;
		if (!TryFindResourceNodeAt(groundPoint, out Entity groundNode)) return false;

		nodeEntity = groundNode;
		propId = World.Has<PropIdentity>(groundNode) ? World.Get<PropIdentity>(groundNode).PropId : string.Empty;
		return nodeEntity != Entity.Null;
	}

	private void UpdateTemporarySelection(Vector2 start, Vector2 end)
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		var dragRect = CreateDragRect(start, end);

		var commandableUnits = new List<Unit3D>();
		var viewOnlyUnits = new List<Unit3D>();
		var resourceProps = new List<Prop3D>();

		CollectUnitsForTemporarySelection(camera, dragRect, commandableUnits, viewOnlyUnits);
		CollectPropsForTemporarySelection(camera, dragRect, resourceProps);

		ApplyTemporarySelectionHighlights(commandableUnits, viewOnlyUnits, resourceProps);
	}

	private void CollectUnitsForTemporarySelection(Camera3D camera, Rect2 dragRect, List<Unit3D> commandableUnits, List<Unit3D> viewOnlyUnits)
	{
		foreach (var unit in AllUnits)
		{
			if (!IsValidVisibleUnit(unit)) continue;

			if (dragRect.HasPoint(camera.UnprojectPosition(unit.GlobalPosition)))
			{
				((!GameContext.PerspectiveFilter.CanControl(unit.Player)) ? viewOnlyUnits : commandableUnits).Add(unit);
			}

			unit.SetTemporarySelectionHighlight(false);
		}
	}

	private void CollectPropsForTemporarySelection(Camera3D camera, Rect2 dragRect, List<Prop3D> resourceProps)
	{
		foreach (var prop in AllProps)
		{
			if (prop == null || !GodotObject.IsInstanceValid(prop) || !prop.Visible) continue;

			if (prop.IsResource)
			{
				if (dragRect.HasPoint(camera.UnprojectPosition(prop.GlobalPosition)))
				{
					resourceProps.Add(prop);
				}

				prop.SetTemporarySelectionHighlight(false);
			}
		}
	}

	private void ApplyTemporarySelectionHighlights(List<Unit3D> commandableUnits, List<Unit3D> viewOnlyUnits, List<Prop3D> resourceProps)
	{
		if (commandableUnits.Count > 0)
		{
			foreach (var unit in commandableUnits) unit.SetTemporarySelectionHighlight(true);
		}
		else if (viewOnlyUnits.Count > 0)
		{
			foreach (var unit in viewOnlyUnits) unit.SetTemporarySelectionHighlight(true);
		}
		else if (resourceProps.Count > 0)
		{
			resourceProps[0].SetTemporarySelectionHighlight(true);
		}
	}

	private void ClearTemporarySelection()
	{
		foreach (var unit in AllUnits)
		{
			if (unit != null && GodotObject.IsInstanceValid(unit))
			{
				unit.SetTemporarySelectionHighlight(false);
			}
		}

		foreach (var prop in AllProps)
		{
			if (prop != null && GodotObject.IsInstanceValid(prop) && prop.IsResource)
			{
				prop.SetTemporarySelectionHighlight(false);
			}
		}
	}

	private void SelectUnit(Unit3D unit)
	{
		if (!SelectedUnits.Contains(unit))
		{
			SelectedUnits.Add(unit);
			unit.IsSelected = true;
			GameContext.AudioService?.PlayUnitSound(unit.UnitId, UnitSoundEvent.Select, unit.GlobalPosition, 0f, AudioPriorityTier.Critical);
			OnUnitSelected?.Invoke((UnitId)unit.Entity.Id);
		}
	}

	private void ClearSelection()
	{
		if (SelectedProp != null && GodotObject.IsInstanceValid(SelectedProp))
		{
			SelectedProp.IsSelected = false;
		}

		SelectedProp = null;
		ClearSelectedResourceNode();
		ClearSelectedLootItem();

		foreach (var u in SelectedUnits)
		{
			u.IsSelected = false;
		}

		SelectedUnits.Clear();
		CycleSelectionIndex = 0;
	}

	private Unit3D? FindUnit3DInParentChain(Node? node)
	{
		while (node != null)
		{
			if (node is Unit3D unit)
			{
				return unit;
			}

			node = node.GetParent();
		}

		return null;
	}

	public Decal? FindDecalInParentChain(Node? node)
	{
		if (!GameContext.SessionState.IsMapEditorMode) return null;
		while (node != null)
		{
			if (node is Decal decal)
			{
				return decal;
			}

			node = node.GetParent();
		}

		return null;
	}

	public ProceduralVfxInstance3D? FindVfxInParentChain(Node? node)
	{
		if (!GameContext.SessionState.IsMapEditorMode) return null;
		while (node != null)
		{
			if (node is ProceduralVfxInstance3D vfx)
			{
				return vfx;
			}

			node = node.GetParent();
		}

		return null;
	}

	public bool TryRaycastTerrainFromMousePosition(Vector2 mousePos, out Vector3 position)
	{
		return TryRaycastFromMousePosition(mousePos, out position, RaycastService.TerrainLayer);
	}

	public bool TryRaycastFromMousePosition(Vector2 mousePos, out Vector3 position, uint collisionMask = uint.MaxValue)
	{
		position = Vector3.Zero;
		var camera = GetViewport()?.GetCamera3D();
		if (camera == null) return false;

		var from = camera.ProjectRayOrigin(mousePos);
		var to = from + camera.ProjectRayNormal(mousePos) * 1000f;

		if (GameContext.RaycastService.TryRaycast(from, to, collisionMask, out position, out _, out _))
		{
			return true;
		}

		return false;
	}

	public Dictionary? RaycastTerrainFromMouse(Vector2 mousePos)
	{
		return RaycastFromMouse(mousePos, RaycastService.TerrainLayer);
	}

	private const float RampDragScreenThreshold = 6f;
	private const float RampClickMaximumMovement = 2f;
	private const ulong RampDragHoldMilliseconds = 350;
	private const float SelectionPickAssistMeters = 0.8f;
	private const float SelectionPickAssistPixels = 28f;

	private bool _rampPressActive;
	private bool _rampPressDragging;
	private bool _rampPressStartedNewRamp;
	private Vector2 _rampPressScreenPosition;
	private ulong _rampPressStartedMilliseconds;

	private void BeginRampInteraction(Vector3 startPosition, Vector2 screenPosition)
	{
		_rampPressActive = true;
		_rampPressDragging = false;
		_rampPressStartedNewRamp = GameContext.EditorService.RampStartPos == null;
		_rampPressScreenPosition = screenPosition;
		_rampPressStartedMilliseconds = Time.GetTicksMsec();

		if (_rampPressStartedNewRamp)
		{
			GameContext.EditorService.SetRampStartPos(startPosition);
			GameContext.MapEditorHud?.ShowFeedbackExternal("Ramp Start Point Set!");
		}

		UpdateRampPreviewVisuals(GameContext.EditorService.RampStartPos ?? startPosition, startPosition);
	}

	private void UpdateRampHoverPreview(Vector2 screenPosition)
	{
		if (ActiveEditorTool != EditorTool.Ramp) return;

		Vector3? startPosition = GameContext.EditorService.RampStartPos;
		if (startPosition == null) return;

		var hoverHit = RaycastTerrainFromMouse(screenPosition);
		if (hoverHit == null || !hoverHit.ContainsKey("position")) return;

		UpdateRampPreviewVisuals(startPosition.Value, SnapEditorHitToGrid(hoverHit["position"].AsVector3()));
	}

	private void FinishRampInteraction(Vector2 releaseScreenPosition)
	{
		ulong heldMilliseconds = Time.GetTicksMsec() - _rampPressStartedMilliseconds;
		float movedPixels = releaseScreenPosition.DistanceTo(_rampPressScreenPosition);
		bool dragIntent = _rampPressDragging
		                  || movedPixels > RampDragScreenThreshold
		                  || (heldMilliseconds >= RampDragHoldMilliseconds && movedPixels > RampClickMaximumMovement);

		bool startedNewRamp = _rampPressStartedNewRamp;
		_rampPressActive = false;
		_rampPressDragging = false;
		_rampPressStartedNewRamp = false;

		Vector3? startPosition = GameContext.EditorService.RampStartPos;
		if (startPosition == null) return;
		if (!dragIntent && startedNewRamp) return;

		var releaseHit = RaycastTerrainFromMouse(releaseScreenPosition);
		if (releaseHit == null || !releaseHit.ContainsKey("position")) return;

		Vector3 endPosition = SnapEditorHitToGrid(releaseHit["position"].AsVector3());
		if (CommitRamp(startPosition.Value, endPosition)) GameContext.MapEditorHud?.ShowFeedbackExternal("Ramp Created!");

		GameContext.EditorService.SetRampStartPos(null);
		ClearRampPreviewVisuals();
	}

	private Vector3 SnapEditorHitToGrid(Vector3 hitPosition)
	{
		if (!EditorSnapToGrid || GroundTerrain == null) return hitPosition;

		float quadSize = GroundTerrain.QuadSize;
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float gridX = Mathf.Round(hitPosition.X / quadSize + (width - 1) / 2.0f);
		hitPosition.X = (Mathf.Clamp(gridX, 0, width - 1) - (width - 1) / 2.0f) * quadSize;
		float gridZ = Mathf.Round(hitPosition.Z / quadSize + (depth - 1) / 2.0f);
		hitPosition.Z = (Mathf.Clamp(gridZ, 0, depth - 1) - (depth - 1) / 2.0f) * quadSize;
		return hitPosition;
	}

	private Unit3D? FindUnitNearScreenPoint(Vector2 screenPosition)
	{
		var camera = GetViewport()?.GetCamera3D();
		if (camera == null) return null;

		Vector3 from = camera.ProjectRayOrigin(screenPosition);
		Vector3 direction = camera.ProjectRayNormal(screenPosition);
		var tolerantHit = GameContext.RaycastService.RaycastUnits(from, direction, 1000f, SelectionPickAssistMeters);
		if (tolerantHit == null || !tolerantHit.HasHit) return null;

		var candidate = FindUnit3DInParentChain(tolerantHit.Collider);
		if (candidate == null) return null;

		Vector3 worldPoint = candidate.GlobalPosition + new Vector3(0f, 0.5f, 0f);
		if (camera.IsPositionBehind(worldPoint)) return null;

		return camera.UnprojectPosition(worldPoint).DistanceTo(screenPosition) <= SelectionPickAssistPixels ? candidate : null;
	}

	public Dictionary? RaycastFromMouse(Vector2 mousePos, uint collisionMask = uint.MaxValue)
	{
		var camera = GetViewport()?.GetCamera3D();
		if (camera == null) return null;

		var from = camera.ProjectRayOrigin(mousePos);
		var to = from + camera.ProjectRayNormal(mousePos) * 1000f;

		var result = GameContext.RaycastService.Raycast(from, to, collisionMask);
		if (result == null || !result.HasHit) return null;

		return result.ToGodotDictionary();
	}
}
