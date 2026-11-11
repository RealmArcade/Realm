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
	private void UpdateBuildingPreview()
	{
		if (_buildingPreviewMesh == null || !GodotObject.IsInstanceValid(_buildingPreviewMesh)) return;

		var mousePos = GetViewport().GetMousePosition();

		// The probe answers with the point it struck rather than a dictionary of what it struck: this runs every tick a
		// placement is armed, and that dictionary was built and thrown away on all of them.
		if (!TryRaycastTerrainFromMousePosition(mousePos, out Vector3 pos)
		    && !TryRaycastFromMousePosition(mousePos, out pos))
		{
			return;
		}

		pos.Y = GetTerrainHeightAt(pos) + 0.1f;
		_buildingPreviewMesh.GlobalPosition = pos;

		string activeType = ActiveBuildingPlacementType ?? "";
		float clearance = Math.Max(ResolveTemplateBodyRadius(activeType) * 1.5f, 1.5f);
		bool blocked = GameContext.InputService.IsAreaObstructed(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), clearance);

		// The hologram is recoloured only when the answer changes. Applying it built a fresh material for every mesh
		// under the preview, once per tick, whether or not the ground had become legal or illegal since the last one.
		// A preview is created already wearing the legal colour, so a fresh node starts from there.
		if (!ReferenceEquals(_hologramPreviewNode, _buildingPreviewMesh))
		{
			_hologramPreviewNode = _buildingPreviewMesh;
			_hologramPreviewBlocked = false;
		}

		if (_hologramPreviewBlocked == blocked) return;

		_hologramPreviewBlocked = blocked;
		Color targetColor = blocked ? new Color(0.9f, 0.2f, 0.2f) : new Color(0.15f, 0.65f, 1.0f);
		MakeHologramRecursive(_buildingPreviewMesh, targetColor);
	}

	private Node3D? _hologramPreviewNode;
	private bool _hologramPreviewBlocked;

	public void TrainUnitAtBuilding(string unitId)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (GameContext.InGameHud == null) return;
		var meta = UnitRegistry[unitId];

		if (!TryGetCandidateBuilding(out Entity targetBuildingEntity)) return;

		if (!ValidateTrainingConstraints(meta)) return;

		if (!GameContext.InputService.HasSufficientResources(
			    meta,
			    Hud.Gold,
			    Hud.Wood,
			    Hud.Stone
		    ))
		{
			ShowTrainingFeedback("Cannot train unit: Insufficient resources!", new Color(1f, 0.2f, 0.2f), isWarning: true);
			return;
		}

		SubmitOrderCommand("train", new List<int> { GetServerEntityId(targetBuildingEntity) }, Vector3.Zero, 0, unitId);
		DeductResourcesAndNotify(meta);
	}

	private bool TryGetCandidateBuilding(out Entity targetBuildingEntity)
	{
		var candidateBuildingEntities = SelectedUnits.FindAll(u => GameContext.PerspectiveFilter.CanControl(u.Player) && u.IsBuilding && World.IsAlive(u.Entity) && CanProduceUnits(u)).ConvertAll(u => u.Entity);

		var result = GameContext.InputService.TryGetCandidateBuilding(candidateBuildingEntities, out targetBuildingEntity);

		if (result == InputService.TryGetCandidateBuildingResult.NoProducingBuildingSelected)
		{
			ShowTrainingFeedback(TranslationServer.Translate("Cannot train unit: No producing building selected!"), new Color(1f, 0.3f, 0.3f), isWarning: true);
			return false;
		}
		else if (result == InputService.TryGetCandidateBuildingResult.TrainingQueueFull)
		{
			ShowTrainingFeedback(TranslationServer.Translate("Training queue is full! (Max 5)"), new Color(1f, 0.3f, 0.3f), isWarning: true);
			return false;
		}

		return true;
	}

	private bool ValidateTrainingConstraints(UnitMetadata meta)
	{
		int localSlot = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		int curPop = GetCurrentPopulation(localSlot);
		int maxPop = GetMaxPopulation(localSlot);
		var result = GameContext.InputService.ValidateTrainingConstraints(meta, curPop, maxPop);

		if (result == InputService.ValidateTrainingConstraintsResult.PopulationCapReached)
		{
			string msg = string.Format(TranslationServer.Translate("Population cap reached! ({0}/{1})"), curPop, maxPop);
			ShowTrainingFeedback(msg, new Color(1f, 0.3f, 0.3f), isWarning: true);
			return false;
		}

		return true;
	}

	private void DeductResourcesAndNotify(UnitMetadata meta)
	{
		float gold = Hud.Gold;
		float wood = Hud.Wood;
		float stone = Hud.Stone;

		GameContext.InputService.DeductResources(meta, ref gold, ref wood, ref stone);

		Hud.Gold = gold;
		Hud.Wood = wood;
		Hud.Stone = stone;

		int localSlot = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		int curPop = GetCurrentPopulation(localSlot);
		int maxPop = GetMaxPopulation(localSlot);
		string msg = string.Format(TranslationServer.Translate("Queued {0} ({1}/{2} pop)"), meta.Name, curPop, maxPop);
		ShowTrainingFeedback(msg, new Color(0.2f, 0.8f, 1f), isWarning: false);
		Hud.RefreshUI(SelectedUnits);
	}

	private void ShowTrainingFeedback(string message, Color color, bool isWarning)
	{
		Hud.ShowFeedbackText(message, color);
		if (isWarning)
			GameContext.UserInterface?.PlayWarningSound();
		else
			GameContext.UserInterface?.PlayClickSound();
	}

	public void SaveCameraLocation(int slotIndex)
	{
		if (slotIndex < 1 || slotIndex > 4) return;
		var camera = MainCamera;
		if (camera == null) return;

		Vector3 pos = camera.GlobalPosition;
		float zoom = camera is CameraControl camCtrl ? camCtrl.TargetHeight : pos.Y;

		GameContext.InputService.TrySaveCameraSlot(slotIndex, new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), zoom);
		ShowSaveCameraFeedback(slotIndex);
	}

	private void ShowSaveCameraFeedback(int slotIndex)
	{
		GameContext.InGameHud?.ShowFeedbackText($"Camera Location {slotIndex} Saved", new Color(0.5f, 0.8f, 1.0f));
	}

	public void RecallCameraLocation(int slotIndex)
	{
			if (!GameContext.InputService.TryGetCameraState(out var state)) return;

			CameraLocationSlot slot = GameContext.InputService.GetCameraSlotFromState(ref state, slotIndex);
		if (!slot.IsSet) return;

		ApplyCameraSlot(slot);
	}

	private void ApplyCameraSlot(CameraLocationSlot slot)
	{
		var camera = MainCamera;
		if (camera == null) return;

		Vector3 savedPos = new Vector3(slot.Position.X, slot.Position.Y, slot.Position.Z);
		camera.GlobalPosition = savedPos;

		if (camera is CameraControl camCtrl)
		{
			camCtrl.FollowTarget = null;
			camCtrl.TargetHeight = slot.ZoomLevel;
			camCtrl.CurrentHeight = savedPos.Y;
		}
	}

	public void CycleCameraZoom()
	{
		var camCtrl = MainCamera ?? GetViewport()?.GetCamera3D() as CameraControl;
		if (camCtrl != null)
		{
			camCtrl.CycleZoom();
		}
	}

	public void StopSelectedUnits()
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		var targetIds = new List<int>();
		var selectedEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (!GameContext.PerspectiveFilter.CanControl(unit.Player)) continue;
			selectedEntities.Add(unit.Entity);
			targetIds.Add(GetServerEntityId(unit.Entity));
			unit.Velocity = Vector3.Zero;
		}

		SubmitOrderCommand("stop", targetIds, Vector3.Zero, 0, "");
	}

	public void CancelQueuedUnitAt(Entity buildingEntity, int index)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (!World.IsAlive(buildingEntity) || !World.Has<ProductionQueue>(buildingEntity)) return;
		// A building read through a view-only selection shows its queue, but only a building this screen owns answers.
		if (!TryGetUnit3D(buildingEntity, out var building3D) || !GameContext.PerspectiveFilter.CanControl(building3D.Player)) return;

		SubmitOrderCommand("cancel_train", new List<int> { GetServerEntityId(buildingEntity) }, Vector3.Zero, index, "");

		GameContext.UserInterface?.PlayClickSound();
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	private void UpdateProductionQueueAfterCancel(Entity buildingEntity, int index, string? nextUnitId)
	{
		GameContext.InputService.UpdateProductionQueueAfterCancel(buildingEntity, index, nextUnitId, _multiplayerActive && !Multiplayer.IsServer());
	}

	private bool UnitHasAbility(Unit3D unit, string abilityId)
	{
		if (UnitRegistry.TryGetValue(unit.UnitId, out var meta))
		{
			if (meta.Abilities != null)
			{
				return Array.Exists(meta.Abilities, a => a == abilityId);
			}
		}

		return false;
	}

	private float GetPlacementRadius(string placeId, float scale = 1.0f)
	{
		return GameContext.InputService.GetPlacementRadius(placeId, scale);
	}

	private Vector3? FindNearestFreePosition(Vector3 startPos, float checkRadius, float maxSearchDist = 20.0f)
	{
		var res = GameContext.InputService.FindNearestFreePosition(new System.Numerics.Vector3(startPos.X, startPos.Y, startPos.Z), checkRadius, maxSearchDist);
		if (res == null) return null;
		return new Vector3(res.Value.X, res.Value.Y, res.Value.Z);
	}

	private bool IsPositionBlocked(Vector3 pos, float radius, Node3D? ignoreNode = null)
	{
		Entity ignoreEntity = Entity.Null;
		if (ignoreNode is Unit3D u) ignoreEntity = u.Entity;
		else if (ignoreNode is Prop3D p) ignoreEntity = p.Entity;
		return GameContext.InputService.IsPositionBlocked(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), radius, ignoreEntity);
	}

	public Unit3D? SpawnUnitFromProduction(string unitId, System.Numerics.Vector3 position, Entity buildingEntity, bool isFromQueue = false)
	{
		if (!UnitRegistry.TryGetValue(unitId, out var meta)) return null;

		int parentPlayer = ResolveSpawnParentPlayer(buildingEntity);
		Entity playerOwnerEntity = GetPlayerEntityForPlayerIndex(parentPlayer);
		var playerOwner = playerOwnerEntity.AsPlayerEntity(World);

		string? targetModel = meta.ModelPath;
		string modelPath = GetFallbackModelPath(targetModel, false);
		string name = meta.Name;
		var godotPosition = new Vector3(position.X, position.Y, position.Z);

		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, godotPosition, playerOwner);

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, godotPosition, false, parentPlayer, isFromQueue);

		if (meta.Speed > 0f)
		{
			ApplySpawnRallyPointOrMove(entity, buildingEntity, position);
		}

		return unit3D;
	}

	private int ResolveSpawnParentPlayer(Entity buildingEntity)
	{
		return GameContext.UnitSpawnService.ResolveSpawnParentPlayer(buildingEntity);
	}

	private void ApplySpawnRallyPointOrMove(Entity entity, Entity buildingEntity, System.Numerics.Vector3 defaultPosition)
	{
		GameContext.InputService.ApplySpawnRallyPointOrMove(entity, buildingEntity, defaultPosition);
	}

	private bool TryApplyRallyPoint(Entity entity, Entity buildingEntity)
	{
		return GameContext.InputService.TryApplyRallyPoint(entity, buildingEntity);
	}

	private void IssueResumeConstructionCommand(Unit3D targetBuilding, bool shiftHeld)
	{
		if (targetBuilding == null || !World.IsAlive(targetBuilding.Entity)) return;
		if (!World.Has<UnderConstruction>(targetBuilding.Entity)) return;

		var pos = targetBuilding.GlobalPosition;
		var bPos = new System.Numerics.Vector3(pos.X, pos.Y, pos.Z);

		AssignResumeTasksToWorkers(targetBuilding, bPos, shiftHeld);

		if (GameContext.InGameHud != null)
		{
			Hud.ShowFeedbackText($"Resuming construction...", new Color(0.3f, 0.9f, 0.4f));
			Hud.RefreshUI(SelectedUnits);
		}
	}

	private void AssignResumeTasksToWorkers(Unit3D targetBuilding, System.Numerics.Vector3 bPos, bool shiftHeld)
	{
		var workerEntities = new List<Entity>();
		foreach (var unit in SelectedUnits)
		{
			if (unit.IsBuilding || !GameContext.PerspectiveFilter.CanControl(unit.Player) || unit.UnitId != "worker") continue;
			workerEntities.Add(unit.Entity);
		}

		GameContext.InputService.AssignResumeTasksToWorkers(workerEntities, targetBuilding.Entity, targetBuilding.UnitId, bPos, shiftHeld);
	}

	public void HandleMinimapRightClick(Vector3 minimapWorldPos)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (ClearTargetingModesIfActive()) return;
		if (SelectedUnits.Count == 0 || !SelectedUnits.Exists(su => GameContext.PerspectiveFilter.CanControl(su.Player))) return;
		if (TrySetRallyPointForSingleProducer(minimapWorldPos)) return;

		var rayResult = CastMinimapRay(minimapWorldPos);
		bool shiftHeld = Input.IsKeyPressed(Key.Shift);

		if (TryHandleMinimapAttackOrInteract(rayResult, shiftHeld)) return;

		IssueMoveCommand(rayResult.HitPos, shiftHeld);
	}

	private bool TryHandleMinimapAttackOrInteract(MinimapRayResult rayResult, bool shiftHeld)
	{
		bool isClickedEnemy = rayResult.ClickedUnit != null && GameContext.PerspectiveFilter.IsHostileToObserved(rayResult.ClickedUnit.Player);
		if (rayResult.ClickedUnit != null && isClickedEnemy && rayResult.ClickedUnit.Visible)
		{
			IssueAttackCommand(rayResult.ClickedUnit, shiftHeld);
			return true;
		}

		if (rayResult.ClickedUnit != null && !isClickedEnemy && rayResult.ClickedUnit != SelectedUnits.Find(u => GameContext.PerspectiveFilter.CanControl(u.Player)))
		{
			HandleFriendlyUnitRightClick(rayResult.ClickedUnit, shiftHeld);
			return true;
		}

		if (rayResult.ClickedProp != null && rayResult.ClickedProp.Visible && rayResult.ClickedProp.IsResource)
		{
			IssueGatherCommand(rayResult.ClickedProp, shiftHeld);
			return true;
		}

		if (TryIssueGatherCommandAt(rayResult.HitPos, shiftHeld)) return true;

		return false;
	}

	private bool ClearTargetingModesIfActive()
	{
		if (ActiveSpellTargeting != null || ActiveCommandTargeting != null || ActiveBuildingPlacementType != null)
		{
			ActiveSpellTargeting = null;
			ActiveCommandTargeting = null;
			CancelBuildingPlacement();
			Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
			return true;
		}

		return false;
	}

	private bool TrySetRallyPointForSingleProducer(Vector3 minimapWorldPos)
	{
		if (SelectedUnits.Count == 1 && GameContext.PerspectiveFilter.CanControl(SelectedUnits[0].Player) && CanProduceUnits(SelectedUnits[0]))
		{
			SetRallyPoint(SelectedUnits[0], minimapWorldPos);
			return true;
		}

		return false;
	}


	private MinimapRayResult CastMinimapRay(Vector3 minimapWorldPos)
	{
		var result = new MinimapRayResult { HitPos = minimapWorldPos };

		var from = new Vector3(minimapWorldPos.X, 200f, minimapWorldPos.Z);
		var to = new Vector3(minimapWorldPos.X, -100f, minimapWorldPos.Z);

		var rayResult = GameContext.RaycastService.Raycast(from, to);
		if (rayResult != null && rayResult.HasHit)
		{
			result.HitPos = rayResult.Position;
			if (rayResult.Collider != null)
			{
				result.ClickedUnit = FindUnit3DInParentChain(rayResult.Collider);
				result.ClickedProp = FindProp3DInParentChain(rayResult.Collider);
			}
		}

		return result;
	}

	private void HandleFriendlyUnitRightClick(Unit3D clickedUnit, bool shiftHeld)
	{
		if (clickedUnit.IsBuilding && World.Has<UnderConstruction>(clickedUnit.Entity))
		{
			if (SelectedUnits.Exists(u => u.UnitId == "worker"))
			{
				IssueResumeConstructionCommand(clickedUnit, shiftHeld);
				return;
			}
		}

		IssueFollowCommand(clickedUnit, shiftHeld);
	}

	private Vector3 CalculatePlacementPosition(Vector3 hitPos)
	{
		Vector3 spawnPos = hitPos;
		if (EditorSnapToGrid && GroundTerrain != null)
		{
			float quadSize = GroundTerrain.QuadSize;
			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;
			float fx = Mathf.Round(spawnPos.X / quadSize + (width - 1) / 2.0f);
			spawnPos.X = (Mathf.Clamp(fx, 0, width - 1) - (width - 1) / 2.0f) * quadSize;
			float fz = Mathf.Round(spawnPos.Z / quadSize + (depth - 1) / 2.0f);
			spawnPos.Z = (Mathf.Clamp(fz, 0, depth - 1) - (depth - 1) / 2.0f) * quadSize;
		}
		return spawnPos;
	}



		private static bool IsObjectPlacementTool(EditorTool tool)
		{
			return tool == EditorTool.PlaceUnit ||
			       tool == EditorTool.PlaceProp ||
			       tool == EditorTool.PlaceDecal ||
			       tool == EditorTool.PlaceVfx ||
			       tool == EditorTool.PlacePropClump;
		}

		private bool TryGetValidPlacementContext(Vector3 hitPos, out float placementRot, out float scaleVal, out Vector3 spawnPos)
		{
			placementRot = 0f;
			scaleVal = 1f;
			spawnPos = hitPos;

			if (EditorClumpMode) return false;
			if (!GameContext.EditorService.Interaction.HasCachedRandom) GenerateNewRandomPlacementRotationAndScale();
			
			placementRot = (GameContext.EditorService.Tools.RandomRotation && !GameContext.EditorService.Interaction.IsPastingObject) ? GameContext.EditorService.Interaction.CachedRandomRotation : GameContext.EditorService.Tools.PlacementRotation;
			scaleVal = (GameContext.EditorService.Tools.RandomScale && !GameContext.EditorService.Interaction.IsPastingObject) ? GameContext.EditorService.Interaction.CachedRandomScale : EditorPlacementScale;

			var finalPos = TryGetValidPlacementLocation(spawnPos, scaleVal);
			if (finalPos == null) return false;
			
			spawnPos = finalPos.Value;
			return true;
		}

		private Vector3? TryGetValidPlacementLocation(Vector3 spawnPos, float scaleVal)
		{
			spawnPos.Y = GetTerrainHeightAt(spawnPos);
			float radius = GameContext.InputService.GetPlacementRadius(ActivePlaceId, scaleVal);
			var finalPos = FindNearestFreePosition(spawnPos, radius);
			if (finalPos == null)
			{
				if (scaleVal > 1.5f)
				{
					GameContext.MapEditorHud?.ShowFeedbackExternal($"Invalid location: The object (Scale: {scaleVal:F1}x) is too large to fit here.");
				}
				else
				{
					GameContext.MapEditorHud?.ShowFeedbackExternal("invalid location");
				}

				GameContext.UserInterface?.PlayWarningSound();
				GetViewport().SetInputAsHandled();
				return null;
			}

			return finalPos;
		}
}
