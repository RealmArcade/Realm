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
	public void EnterCommandTargeting(string mode)
	{
		ActiveCommandTargeting = mode;
		ActiveSpellTargeting = null;
		Input.SetDefaultCursorShape(Input.CursorShape.Cross);

		if (GameContext.InGameHud != null)
		{
			if (mode == "attack")
			{
				Hud.ShowFeedbackText("Attack Command: Click enemy to attack, or ground to Attack-Move", new Color(0.9f, 0.4f, 0.1f));
			}
			else if (mode == "move")
			{
				Hud.ShowFeedbackText("Move Command: Click ground to move", new Color(0.2f, 0.9f, 0.3f));
			}
			else if (mode == "patrol")
			{
				Hud.ShowFeedbackText("Patrol Command: Click ground to set patrol endpoint", new Color(0.7f, 0.4f, 1.0f));
			}
			else if (mode == "rally")
			{
				Hud.ShowFeedbackText("Rally Command: Click ground to set building Rally Point", new Color(1.0f, 0.85f, 0.5f));
			}
		}
	}

	public void IssueMoveCommand(Vector3 targetPos, bool isQueued = false)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (SelectedUnits.Count == 0) return;

		ViewShowMoveCommandFeedback(targetPos, isQueued);

		var selectedEntities = new List<Entity>();
		var targetIds = new List<int>();
		ViewCollectMoveCommandTargets(selectedEntities, targetIds);

		int commandId = SubmitOrderCommand(isQueued ? "move_queued" : "move", targetIds, targetPos, 0, "");
		if (!isQueued) PreviewRouteForOrder(commandId, targetIds, targetPos);

		ViewPlayMoveCommandSound(selectedEntities);
	}

	private void ViewShowMoveCommandFeedback(Vector3 targetPos, bool isQueued)
	{
		if (isQueued)
		{
			SpawnTargetIndicator(targetPos, new Color(0.2f, 0.7f, 1.0f));
			GameContext.InGameHud?.ShowFeedbackText(TranslationServer.Translate("Command: Queued Move (Shift+Click)"), new Color(0.2f, 0.7f, 1.0f));
		}
		else
		{
			SpawnTargetIndicator(targetPos, new Color(0.22f, 0.54f, 0.26f));
			GameContext.InGameHud?.ShowFeedbackText(TranslationServer.Translate("Command: Move to position"), new Color(0.22f, 0.54f, 0.26f));
		}
	}

	private void ViewCollectMoveCommandTargets(List<Entity> selectedEntities, List<int> targetIds)
	{
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || !GameContext.PerspectiveFilter.CanControl(unit.Player)) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}
	}

	private void ViewPlayMoveCommandSound(List<Entity> selectedEntities)
	{
		if (selectedEntities.Count > 0 && SelectedUnits.Count > 0)
		{
			var primaryUnit = SelectedUnits[0];
			GameContext.AudioService?.PlayUnitSound(primaryUnit.UnitId, UnitSoundEvent.MoveOrder, primaryUnit.GlobalPosition, 0f, AudioPriorityTier.Critical);
		}
	}

	public void IssueAttackCommand(Unit3D target, bool isQueued = false)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (SelectedUnits.Count == 0) return;

		ViewShowAttackCommandFeedback(target);

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		ViewCollectAttackCommandTargets(selectedEntities, targetIds);

		SubmitOrderCommand(isQueued ? "attack_queued" : "attack", targetIds, target.GlobalPosition, GetServerEntityId(target.Entity), "");

		ViewPlayAttackCommandSound(selectedEntities);
	}

	private void ViewShowAttackCommandFeedback(Unit3D target)
	{
		SpawnTargetIndicator(target.GlobalPosition, new Color(0.9f, 0.1f, 0.1f));

		if (GameContext.InGameHud != null)
		{
			// The unit the order names is shown by the name the map gave it, not by its template identifier: a player
			// who right-clicks a camp reads "Crest Boar", never "UNIT/CREST_BOAR".
			string targetName = GameHost.TryGetUnitOrBuildingMetadata(target.TemplateID, out UnitMetadata? targetMetadata) && !string.IsNullOrEmpty(targetMetadata.Name)
				? targetMetadata.Name
				: target.UnitId;
			Hud.ShowFeedbackText(string.Format(TranslationServer.Translate("Command: Attack {0}"), targetName), new Color(0.9f, 0.2f, 0.2f));
		}
	}

	private void ViewCollectAttackCommandTargets(List<Entity> selectedEntities, List<int> targetIds)
	{
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || !GameContext.PerspectiveFilter.CanControl(unit.Player)) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}
	}

	private void ViewPlayAttackCommandSound(List<Entity> selectedEntities)
	{
		if (selectedEntities.Count > 0 && SelectedUnits.Count > 0)
		{
			var primaryUnit = SelectedUnits[0];
			GameContext.AudioService?.PlayUnitSound(primaryUnit.UnitId, UnitSoundEvent.AttackOrder, primaryUnit.GlobalPosition, 0f, AudioPriorityTier.Critical);
		}
	}

	public void IssueFollowCommand(Unit3D target, bool isQueued = false)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(target.GlobalPosition, new Color(0.2f, 0.6f, 1.0f));
		ShowFollowCommandFeedback(target);

		var selectedEntities = new List<Entity>();
		var targetIds = new List<int>();
		CollectFollowCommandTargets(target, selectedEntities, targetIds);

		SubmitFollowCommand(target, targetIds, isQueued);
	}

	private void ShowFollowCommandFeedback(Unit3D target)
	{
		if (GameContext.InGameHud == null) return;

		Hud.ShowFeedbackText($"Command: Follow {target.UnitId.ToUpper()}", new Color(0.2f, 0.6f, 1.0f));
	}

	private void CollectFollowCommandTargets(Unit3D target, List<Entity> selectedEntities, List<int> targetIds)
	{
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || !GameContext.PerspectiveFilter.CanControl(unit.Player) || unit.Entity == target.Entity) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}
	}

	private void SubmitFollowCommand(Unit3D target, List<int> targetIds, bool isQueued)
	{
		SubmitOrderCommand(isQueued ? "follow_queued" : "follow", targetIds, target.GlobalPosition, GetServerEntityId(target.Entity), "");
	}

	private void SelectAllBuildings()
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		ClearSelection();
		var entities = GameContext.InputService.GetBuildingEntities(GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex));
		int count = 0;
		foreach (var ent in entities)
		{
			var unit = AllUnits.Find(u => u.Entity == ent);
			if (unit != null)
			{
				SelectUnit(unit);
				count++;
			}
		}

		GameContext.InGameHud?.RefreshUI(SelectedUnits);
		GameContext.InGameHud?.ShowFeedbackText($"Selected {count} Buildings", new Color(0.9f, 0.7f, 0.2f));
	}

	public void CycleSelectionFocus(bool reverse = false)
	{
		if (SelectedUnits.Count <= 1) return;
		var unitIds = new List<string>();
		foreach (var u in SelectedUnits)
		{
			unitIds.Add(u.UnitId);
		}

		int index = GameContext.InputService.CycleSelectionFocus(GameContext.Simulation.WorldEntity, unitIds, reverse);
		var focusUnit = SelectedUnits[index];

		var camera = GetViewport().GetCamera3D();
		if (camera is CameraControl cameraControl)
		{
			cameraControl.FocusOnPosition(focusUnit.GlobalPosition);
		}

		GameContext.InGameHud?.ShowFeedbackText($"Focused: {focusUnit.UnitId.ToUpper()} ({index + 1}/{SelectedUnits.Count})", new Color(0.5f, 1.0f, 0.5f));
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	private void CycleThroughBuildings()
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		Entity buildingEntity = GameContext.InputService.CycleThroughBuildings(GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex));
		if (buildingEntity == Entity.Null) return;

		var building = AllUnits.Find(u => u.Entity == buildingEntity);
		if (building == null) return;

		var camera = GetViewport().GetCamera3D();
		if (camera != null)
			camera.GlobalPosition = new Vector3(building.GlobalPosition.X, camera.GlobalPosition.Y, building.GlobalPosition.Z);
		SelectOnlyUnit(building);
		GameContext.InGameHud?.ShowFeedbackText($"Jumped to: {building.UnitId.ToUpper()}", new Color(0.9f, 0.8f, 0.3f));
	}

	private void KillUnitDeferred(Unit3D? unit)
	{
		if (unit != null && AllUnits.Contains(unit))
			KillUnit(unit);
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	public void SelectAllIdleUnits()
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		ClearSelection();
		var entities = GameContext.InputService.GetIdleUnitEntities(GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex));
		int selectedCount = 0;
		foreach (var ent in entities)
		{
			var unit = AllUnits.Find(u => u.Entity == ent);
			if (unit != null)
			{
				SelectUnit(unit);
				selectedCount++;
			}
		}

		GameContext.InGameHud?.RefreshUI(SelectedUnits);
		if (GameContext.InGameHud != null)
		{
			Hud.ShowFeedbackText($"Selected {selectedCount} Idle Units", new Color(0.5f, 1.0f, 0.5f));
		}
	}

	public void SelectAllMilitaryUnits()
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		ClearSelection();
		var entities = GameContext.InputService.GetMilitaryUnitEntities(GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex));
		int selectedCount = 0;
		foreach (var ent in entities)
		{
			var unit = AllUnits.Find(u => u.Entity == ent);
			if (unit != null)
			{
				SelectUnit(unit);
				selectedCount++;
			}
		}

		GameContext.InGameHud?.RefreshUI(SelectedUnits);
		if (GameContext.InGameHud != null)
		{
			Hud.ShowFeedbackText($"Selected All Army ({selectedCount} Units)", new Color(0.5f, 1.0f, 0.5f));
		}
	}

	private void PerformDoubleClickSelection(Vector2 clickPos)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;

		var hit = RaycastFromMouse(clickPos);
		Unit3D? clickedUnit = null;
		if (hit != null && hit.ContainsKey("collider"))
		{
			clickedUnit = FindUnit3DInParentChain(hit["collider"].As<Node>());
		}

		clickedUnit ??= FindUnitNearScreenPoint(clickPos);
		if (!IsValidVisibleUnit(clickedUnit)) return;

		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;

		SelectSimilarVisibleUnits(clickedUnit, camera, GetViewport().GetVisibleRect());
	}

	private void SelectSimilarVisibleUnits(Unit3D clickedUnit, Camera3D camera, Rect2 viewportRect)
	{
		int clickedPlayer = clickedUnit.Player;
		string type = clickedUnit.UnitId;
		var visibleUnits = AllUnits.FindAll(u => IsValidVisibleUnit(u) && !u.IsBuilding && u.Player == clickedPlayer && u.UnitId == type && viewportRect.HasPoint(camera.UnprojectPosition(u.GlobalPosition)));

		// The template selection belongs to the clicked unit's player, so collapsing the current selection first keeps a
		// read-only selection from mixing with units this screen can command.
		ClearSelection();

		foreach (var u in visibleUnits)
		{
			SelectUnit(u);
		}

		GameContext.InGameHud?.RefreshUI(SelectedUnits);
		GameContext.InGameHud?.ShowFeedbackText($"Selected {visibleUnits.Count} {type.ToUpper()}(s)", new Color(0.5f, 1.0f, 0.5f));
	}

	public void CenterCameraOnPrimaryBuilding()
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		Unit3D? primaryBuilding = null;
		Unit3D? anyOwnedBuilding = null;
		foreach (var unit in AllUnits)
		{
			if (GameContext.PerspectiveFilter.CanControl(unit.Player) && unit.IsBuilding)
			{
				if (GameContext.RegistryService.IsPrimaryBuilding(unit.UnitId))
				{
					primaryBuilding = unit;
					break;
				}
				anyOwnedBuilding ??= unit;
			}
		}

		var target = primaryBuilding ?? anyOwnedBuilding;
		if (target != null)
		{
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				camera.GlobalPosition = new Vector3(target.GlobalPosition.X, target.GlobalPosition.Y, target.GlobalPosition.Z + 25f);
			}
		}
	}

	public void CenterCameraOnSelectedOrBase()
	{
		if (SelectedUnits.Count > 0)
		{
			var unit = SelectedUnits[0];
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				camera.GlobalPosition = new Vector3(unit.GlobalPosition.X, unit.GlobalPosition.Y, unit.GlobalPosition.Z + 25f);
			}
		}
		else
		{
			CenterCameraOnPrimaryBuilding();
		}
	}

	public void TriggerCopyFromUI()
	{
		if (ActiveEditorTool == EditorTool.SelectArea)
		{
			PerformCopyArea();
			return;
		}

		if (ActiveEditorTool != EditorTool.SelectMove || !GodotObject.IsInstanceValid(SelectedEditorObject))
		{
			GameContext.MapEditorHud?.ShowFeedbackExternal("Nothing to Copy (select an object or area first)");
			return;
		}

		if (SelectedEditorObject is Unit3D unit)
		{
			CopyEditorUnit(unit);
			return;
		}

		if (SelectedEditorObject is PropEntityProxyNode propProxy)
		{
			GameContext.EditorService.Clipboard.CopyProp(propProxy.PropId, propProxy.RotationDegrees.Y, propProxy.Scale.X);
			GameContext.MapEditorHud?.ShowFeedbackExternal($"Copied Prop: {propProxy.PropId.ToUpper()}");
			return;
		}

		if (SelectedEditorObject is Prop3D prop)
		{
			CopyEditorProp(prop);
			return;
		}

		if (SelectedEditorObject is Decal decal)
		{
			CopyEditorDecal(decal);
			return;
		}
	}

	private void CopyEditorUnit(Unit3D unit)
	{
		GameContext.EditorService.Clipboard.SetCopiedObject(new CopiedObjectTemplate
		{
			Type = CopiedObjectKind.Unit,
			Id = unit.UnitId,
			Rotation = unit.RotationDegrees.Y,
			Scale = unit.Scale.X,
			Player = unit.Player
		});
		GameContext.MapEditorHud?.ShowFeedbackExternal($"Copied Unit: {unit.UnitId.ToUpper()}");
	}

	private void CopyEditorProp(Prop3D prop)
	{
		GameContext.EditorService.Clipboard.CopyProp(prop.PropId, prop.RotationDegrees.Y, prop.Scale.X);
		GameContext.MapEditorHud?.ShowFeedbackExternal($"Copied Prop: {prop.PropId.ToUpper()}");
	}

	private void CopyEditorDecal(Decal decal)
	{
		string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
		GameContext.EditorService.Clipboard.CopyDecal(decalId, decal.RotationDegrees.Y, decal.Scale.X);
		GameContext.MapEditorHud?.ShowFeedbackExternal($"Copied Decal: {decalId.ToUpper()}");
	}

	public void TriggerPasteFromUI()
	{
		if (TryActivateAreaPasteMode()) return;

		var copiedObjOpt = GameContext.EditorService.Clipboard.GetCopiedObject();
		if (copiedObjOpt == null)
		{
			GameContext.MapEditorHud?.ShowFeedbackExternal("Nothing to Paste (copy an object or area first)");
			return;
		}

		var copiedObj = copiedObjOpt.Value;
		ApplyCopiedObjectState(copiedObj);
		GameContext.MapEditorHud?.ShowFeedbackExternal($"Paste Mode Active - Placing {copiedObj.Id.ToUpper()}");
	}

	private bool TryActivateAreaPasteMode()
	{
		if ((ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea) && GameContext.EditorService.Clipboard.HasCopiedArea)
		{
			ActiveEditorTool = EditorTool.PasteArea;
			GameContext.MapEditorHud?.SelectToolFromHotkey(EditorTool.PasteArea);
			GameContext.MapEditorHud?.ShowFeedbackExternal("Paste Mode Active - Click to paste");
			return true;
		}

		return false;
	}

	private void ApplyCopiedObjectState(CopiedObjectTemplate copiedObj)
	{
		GameContext.EditorService.ApplyCopiedObjectStateDomain(copiedObj, out var newTool);
		ActivePlaceId = copiedObj.Id;
		EditorPlacementScale = copiedObj.Scale;
		ActiveEditorTool = newTool;
		GameContext.MapEditorHud?.SelectToolFromHotkey(newTool);
	}

	private void AssignControlGroup(int index)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		var groupUnits = new List<Unit3D>();
		foreach (var u in SelectedUnits)
		{
			if (GameContext.PerspectiveFilter.CanControl(u.Player))
			{
				groupUnits.Add(u);
			}
		}

		_controlGroups[index] = groupUnits;
		if (GameContext.InGameHud != null)
		{
			Hud.ShowFeedbackText($"Assigned {groupUnits.Count} units to Control Group {index}", new Color(0.5f, 0.8f, 1.0f));
			Hud.RefreshUI(SelectedUnits);
		}
	}

	public void RecallControlGroup(int index)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		var group = _controlGroups[index];
		if (group == null || group.Count == 0) return;

		group.RemoveAll(u => !GodotObject.IsInstanceValid(u) || !AllUnits.Contains(u));
		if (group.Count == 0) return;

		ClearSelection();
		foreach (var u in group)
		{
			SelectUnit(u);
		}

		GameContext.InGameHud?.RefreshUI(SelectedUnits);

		double now = Time.GetTicksMsec() / 1000.0;
		if (now - _lastGroupPressTime[index] < 0.3)
		{
			Vector3 sumPos = Vector3.Zero;
			foreach (var u in group)
			{
				sumPos += u.GlobalPosition;
			}

			Vector3 avgPos = sumPos / group.Count;
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				camera.GlobalPosition = new Vector3(avgPos.X, camera.GlobalPosition.Y, avgPos.Z + 25f);
			}
		}

		_lastGroupPressTime[index] = now;
	}

	public void ClearTargetingModes()
	{
		GameContext.InputService.ClearTargetingModes();
		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
	}
}
