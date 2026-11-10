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
	public void EnterBuildingPlacement(string type)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		ActiveBuildingPlacementType = type;

		if (_buildingPreviewMesh != null && GodotObject.IsInstanceValid(_buildingPreviewMesh))
		{
			_buildingPreviewMesh.QueueFree();
		}
		_buildingPreviewMesh = null;

		bool isBuilding = false;
		if (TryGetUnitOrBuildingMetadata(type, out var meta))
		{
			if (BuildingRegistry != null && (BuildingRegistry.ContainsKey(type) || (meta.TemplateID != null && BuildingRegistry.ContainsKey(meta.TemplateID))))
			{
				isBuilding = true;
			}
		}
		else if (BuildingRegistry != null && BuildingRegistry.TryGetValue(type, out meta))
		{
			isBuilding = true;
		}

		Node3D? previewNode = null;
		if (meta != null && meta.TemplateID != null && !string.IsNullOrEmpty(meta.ModelPath))
		{
			string modelPath = GetFallbackModelPath(meta.ModelPath, isBuilding);
			var previewUnit = new Unit3D
			{
				UnitId = type,
				IsBuilding = isBuilding,
				Player = GameContext.PerspectiveFilter.ControlledPlayerIndex,
				IsPreview = true
			};
			AddChild(previewUnit);
			previewUnit.LoadModel(modelPath);
			previewUnit.Scale = Vector3.One;
			MakeHologramRecursive(previewUnit, new Color(0.15f, 0.65f, 1.0f));
			previewNode = previewUnit;
		}
		else
		{
			var mesh = new MeshInstance3D();
			mesh.Name = "Mesh_MeshInstance3D";
			float radius = ResolveTemplateBodyRadius(type);
			float side = radius * 2f;
			var box = new BoxMesh
			{
				Size = new Vector3(side, 2f, side)
			};
			mesh.Mesh = box;
			AddChild(mesh);
			MakeHologramRecursive(mesh, new Color(0.15f, 0.65f, 1.0f));
			previewNode = mesh;
		}

		_buildingPreviewMesh = previewNode;

		if (GameContext.InGameHud != null)
		{
			string displayName = meta != null && !string.IsNullOrEmpty(meta.Name) ? meta.Name : type.ToUpper();
			Hud.ShowFeedbackText($"Place {displayName}: Click on map to construct", new Color(0.15f, 0.65f, 1.0f));
		}
	}

	public void CancelBuildingPlacement()
	{
		ActiveBuildingPlacementType = null;
		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}

		if (GameContext.InGameHud != null)
		{
			Hud.ShowFeedbackText("Building Placement Cancelled", new Color(0.8f, 0.8f, 0.8f));
		}

		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
	}

	/// <summary>
	///     The unit that should carry out a placement order: one of this player's selection when it can build the type,
	///     otherwise the player's own builder nearest the spot. A placement is ordered from a builder's build menu, so the
	///     order must not be lost when that builder is not selected the frame the click lands.
	/// </summary>
	private Unit3D? FindWorkerForPlacement(string type, Vector3 position)
	{
		Unit3D? selectedWorker = SelectedUnits.Find(u => CanUnitConstructType(u, type));
		if (selectedWorker != null && World.IsAlive(selectedWorker.Entity)) return selectedWorker;

		int playerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		if (!PlayerToUnits.TryGetValue(playerIndex, out List<Unit3D>? ownedUnits)) return null;

		Unit3D? nearestWorker = null;
		float nearestDistanceSquared = float.MaxValue;
		for (int index = 0; index < ownedUnits.Count; index++)
		{
			Unit3D candidate = ownedUnits[index];
			if (!GodotObject.IsInstanceValid(candidate)) continue;
			if (!World.IsAlive(candidate.Entity)) continue;
			if (!CanUnitConstructType(candidate, type)) continue;

			float distanceSquared = candidate.Position.DistanceSquaredTo(position);
			if (distanceSquared >= nearestDistanceSquared) continue;

			nearestDistanceSquared = distanceSquared;
			nearestWorker = candidate;
		}

		return nearestWorker;
	}

	private bool CanUnitConstructType(Unit3D u, string type)
	{
		if (u == null || u.IsBuilding || !GameContext.PerspectiveFilter.CanControl(u.Player)) return false;
		if (u.UnitId == "worker" || u.UnitId == "unit/builder" || u.UnitId == "builder") return true;
		if (TryGetUnitOrBuildingMetadata(u.UnitId, out var uMeta) && uMeta.BuildOptions != null)
		{
			foreach (var opt in uMeta.BuildOptions)
			{
				if (string.Equals(opt, type, StringComparison.OrdinalIgnoreCase)) return true;
			}
		}
		return false;
	}

	private void ExecuteBuildingPlacement(string type, Vector3 position)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		bool shiftHeld = Input.IsKeyPressed(Key.Shift);
		if (!TryGetUnitOrBuildingMetadata(type, out var meta))
		{
			ClearBuildingPlacementState();
			return;
		}

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			// The worker is resolved before the order is announced or paid for: an order the screen cannot name a worker for
			// is one the match will refuse, and telling the player it is constructing something it never asked for is worse
			// than saying nothing.
			Unit3D? placementWorker = FindWorkerForPlacement(type, position);
			int placementWorkerServerId = placementWorker != null ? GetServerEntityId(placementWorker.Entity) : -1;
			if (placementWorkerServerId <= 0)
			{
				GameContext.InGameHud?.ShowFeedbackText(TranslationServer.Translate("Cannot construct: no builder is available"), new Color(1.0f, 0.2f, 0.2f));
				GameContext.UserInterface?.PlayWarningSound();
				ClearBuildingPlacementState();
				return;
			}

			if (GameContext.InGameHud != null)
			{
				DeductResourcesForBuilding(meta);
				Hud.ShowFeedbackText($"Constructing {meta.Name}...", new Color(0.3f, 0.9f, 0.4f));
			}

			SubmitRemoteCommand("build", new List<int> { placementWorkerServerId }, position, 0, type);

			if (!shiftHeld) ClearBuildingPlacementState();
			return;
		}

		if (GameContext.InGameHud == null) return;

		float clearance = Math.Max(ResolveTemplateBodyRadius(type) * 1.5f, 1.5f);
		if (!GameContext.InputService.TryPlaceBuilding(new System.Numerics.Vector3(position.X, position.Y, position.Z), clearance))
		{
			ViewHandleObstructedPlacement();
			return;
		}

		if (!GameContext.InputService.HasSufficientResources(
			    meta,
			    Hud.Gold,
			    Hud.Wood,
			    Hud.Stone
		    ))
		{
			Hud.ShowFeedbackText("Cannot construct: Insufficient resources!", new Color(1.0f, 0.2f, 0.2f));
			GameContext.UserInterface?.PlayWarningSound();
		}
		else
		{
			DeductResourcesForBuilding(meta);
			var buildingPos = new System.Numerics.Vector3(position.X, position.Y, position.Z);
			Unit3D? firstWorker = SelectedUnits.Find(u => CanUnitConstructType(u, type));
			if (firstWorker != null && World.IsAlive(firstWorker.Entity))
			{
				OnUnitOrdered?.Invoke((UnitId)firstWorker.Entity.Id, type, buildingPos);
				AssignWorkerTasks(type, buildingPos, meta, shiftHeld, firstWorker);
			}
			else
			{
				SpawnInstantBuilding(type, position, meta);
			}

			GameContext.UserInterface?.PlayClickSound();
		}

		if (!shiftHeld) ClearBuildingPlacementState();
	}

	private void ViewHandleObstructedPlacement()
	{
		Hud.ShowFeedbackText("Cannot construct: Area is obstructed!", new Color(1.0f, 0.2f, 0.2f));
		GameContext.UserInterface?.PlayWarningSound();
		ClearBuildingPlacementState();
	}

	private void DeductResourcesForBuilding(UnitMetadata meta)
	{
		GameContext.InputService.DeductResourcesForBuilding(
			meta,
			amount => Hud.Gold -= amount,
			amount => Hud.Wood -= amount,
			amount => Hud.Stone -= amount);
	}

	private void AssignWorkerTasks(string type, System.Numerics.Vector3 buildingPos, UnitMetadata meta, bool shiftHeld, Unit3D firstWorker)
	{
		AssignFirstWorkerTask(type, buildingPos, meta, shiftHeld, firstWorker);
		AssignAdditionalWorkerTasks(type, buildingPos, shiftHeld, firstWorker);
	}

	private void AssignFirstWorkerTask(string type, System.Numerics.Vector3 buildingPos, UnitMetadata meta, bool shiftHeld, Unit3D firstWorker)
	{
		bool wasQueued = GameContext.InputService.AssignFirstWorkerTask(
			firstWorker.Entity, type, buildingPos, shiftHeld,
			(entity, t, pos) => AssignBuildTaskToWorker(entity, t, pos));

		if (wasQueued)
		{
			Hud.ShowFeedbackText($"Queued: Construct {meta.Name}", new Color(0.5f, 0.8f, 1.0f));
		}
		else
		{
			Hud.ShowFeedbackText($"Constructing: {meta.Name}", new Color(0.3f, 0.9f, 0.4f));
		}
	}

	private void AssignAdditionalWorkerTasks(string type, System.Numerics.Vector3 buildingPos, bool shiftHeld, Unit3D firstWorker)
	{
		foreach (var unit in SelectedUnits)
		{
			ProcessWorkerTaskAssignment(unit, firstWorker, type, buildingPos, shiftHeld);
		}
	}

	private void ProcessWorkerTaskAssignment(Unit3D unit, Unit3D firstWorker, string type, System.Numerics.Vector3 buildingPos, bool shiftHeld)
	{
		GameContext.InputService.ProcessWorkerTaskAssignment(
			unit.Entity, firstWorker.Entity, unit.IsBuilding, unit.UnitId, type, buildingPos, shiftHeld);
	}


	private void SpawnInstantBuilding(string type, Vector3 position, UnitMetadata meta)
	{
		var bldEntity = GameContext.InputService.CreateInstantBuildingEcsUnit(type, meta, new System.Numerics.Vector3(position.X, position.Y, position.Z), GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex));
		string modelPath = GetFallbackModelPath(meta.ModelPath, true);
		SpawnUnit3D(bldEntity, type, modelPath, position, true, GameContext.PerspectiveFilter.ControlledPlayerIndex);
		GameContext.InGameHud?.ShowFeedbackText($"Constructed: {meta.Name}", new Color(0.3f, 0.9f, 0.4f));
	}

	private void ClearBuildingPlacementState()
	{
		ActiveBuildingPlacementType = null;
		if (_buildingPreviewMesh != null)
		{
			_buildingPreviewMesh.QueueFree();
			_buildingPreviewMesh = null;
		}

		GameContext.InGameHud?.ExitBuildSubMenu();
	}

	public void CancelLastQueuedUnit(Entity buildingEntity)
	{
		if (World.IsAlive(buildingEntity) && World.Has<ProductionQueue>(buildingEntity))
		{
			var prod = World.Get<ProductionQueue>(buildingEntity);
			if (prod.Count > 0)
			{
				CancelQueuedUnitAt(buildingEntity, prod.Count - 1);
			}
		}
	}

	public bool CanProduceUnits(Unit3D unit)
	{
		if (unit == null || !unit.IsBuilding) return false;

		if (!BuildingRegistry.TryGetValue(unit.UnitId, out var meta)) return false;

		return HasValidBuildOption(meta) || HasProductionAbility(meta);
	}

	private bool HasValidBuildOption(UnitMetadata meta)
	{
		if (meta.BuildOptions == null) return false;

		foreach (var opt in meta.BuildOptions)
		{
			if (UnitRegistry.ContainsKey(opt)) return true;
		}

		return false;
	}

	private bool HasProductionAbility(UnitMetadata meta)
	{
		if (meta.Abilities == null) return false;

		foreach (var ab in meta.Abilities)
		{
			if (ab.Contains("spawn") || ab.Contains("train")) return true;
		}

		return false;
	}

	public void SetRallyPoint(Unit3D building, Vector3 position)
	{
		bool queue = Input.IsKeyPressed(Key.Shift);
		SpawnTargetIndicator(position, new Color(0.9f, 0.7f, 0.2f));
		if (GameContext.InGameHud != null)
		{
			if (queue)
			{
				Hud.ShowFeedbackText($"Queued Rally Point set to {position.X:F0}, {position.Z:F0}", new Color(0.9f, 0.7f, 0.2f));
			}
			else
			{
				Hud.ShowFeedbackText($"Rally Point set to {position.X:F0}, {position.Z:F0}", new Color(0.9f, 0.7f, 0.2f));
			}
		}

		GameContext.InputService.SetRallyPoint(building.Entity, new System.Numerics.Vector3(position.X, position.Y, position.Z), queue);
	}

	public void DeselectUnit(Unit3D unit)
	{
		if (SelectedUnits.Remove(unit))
		{
			unit.IsSelected = false;
		}
	}

	public void SelectOnlyUnit(Unit3D unit)
	{
		ClearSelection();
		SelectUnit(unit);
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	public void BuyUpgrade(string upgradeId, int playerIndex = 0)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (playerIndex <= 0) throw new ArgumentOutOfRangeException(nameof(playerIndex), playerIndex, "An explicit player index is required.");
		if (playerIndex <= 0) return;
		if (string.IsNullOrWhiteSpace(upgradeId)) return;

		SubmitOrderCommand("research", new List<int>(), Vector3.Zero, 0, upgradeId, requiresUnits: false);
	}

	public void IssueAttackMoveCommand(Vector3 targetPos)
	{
		IssueAttackMoveCommand(targetPos, false);
	}

	public void HoldSelectedUnits()
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (SelectedUnits.Count == 0) return;

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || !GameContext.PerspectiveFilter.CanControl(unit.Player)) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
			unit.Velocity = Vector3.Zero;
		}

		SubmitOrderCommand("hold", targetIds, Vector3.Zero, 0, "");
	}

	public void IssuePatrolCommand(Vector3 targetPos, bool isQueued = false)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (SelectedUnits.Count == 0) return;

		SpawnTargetIndicator(targetPos, new Color(0.6f, 0.3f, 1.0f));
		GameContext.InGameHud?.ShowFeedbackText(TranslationServer.Translate("Command: Patrol Route Set"), new Color(0.7f, 0.4f, 1.0f));

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || !GameContext.PerspectiveFilter.CanControl(unit.Player)) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}

		int commandId = SubmitOrderCommand(isQueued ? "patrol_queued" : "patrol", targetIds, targetPos, 0, "");
		if (!isQueued) PreviewRouteForOrder(commandId, targetIds, targetPos);
	}

	public void IssueAttackMoveCommand(Vector3 targetPos, bool isQueued = false)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (SelectedUnits.Count == 0) return;

		if (isQueued)
		{
			SpawnTargetIndicator(targetPos, new Color(0.2f, 0.7f, 1.0f));
			GameContext.InGameHud?.ShowFeedbackText(TranslationServer.Translate("Command: Queued Attack-Move (Shift+Click)"), new Color(0.2f, 0.7f, 1.0f));
		}
		else
		{
			SpawnTargetIndicator(targetPos, new Color(0.9f, 0.5f, 0.1f));
			GameContext.InGameHud?.ShowFeedbackText(TranslationServer.Translate("Command: Attack-Move to position"), new Color(0.9f, 0.5f, 0.1f));
		}

		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || !GameContext.PerspectiveFilter.CanControl(unit.Player)) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
		}

		int commandId = SubmitOrderCommand(isQueued ? "attack_move_queued" : "attack_move", targetIds, targetPos, 0, "");
		if (!isQueued) PreviewRouteForOrder(commandId, targetIds, targetPos);
	}
}
