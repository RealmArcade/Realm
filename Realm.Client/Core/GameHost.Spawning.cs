using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Realm.Ecs;
using Realm.Ecs.Replication;
using DotRecast.Detour;
using Godot;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = Godot.Environment;
using FileAccess = System.IO.FileAccess;
using Realm.Shared.Abilities;

namespace Realm.Client.Core;
public partial class GameHost
{
	private Entity CreateEcsUnit(string id, string name, float hp, float damage, float range, float armor, float speed, Vector3 pos, PlayerEntity owner)
	{
		UnitMetadata? regMeta = null;
		if (!UnitRegistry.TryGetValue(id, out regMeta)) TryGetUnitOrBuildingMetadata(id, out regMeta);

		bool hasShields = GameContext.World?.GetFieldOrDefault<PlayerUpgrades, bool>(owner.Value, u => u.ShieldsUpgrade) ?? false;
		bool hasWeapons = GameContext.World?.GetFieldOrDefault<PlayerUpgrades, bool>(owner.Value, u => u.WeaponsUpgrade) ?? false;

		var entity = regMeta != null
			? CreateEcsUnitEntityWithMeta(id, name, hp, damage, range, armor, speed, pos, owner, regMeta, hasShields, hasWeapons)
			: GameContext.UnitSpawnService.CreateEcsUnitEntity(id, name, hp, damage, range, armor, speed, 15.0f, false, 1.5f, 8, pos, owner, owner.Value, hasShields, hasWeapons, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, "physical", 0f, 1f, "None", 0f, 0f, 0f, 1f, 0.5f, 0.25f, false, 0, "Ground");

		if (regMeta != null) ApplyUnitAttributes(entity, regMeta);

		OnUnitCreated?.Invoke(GameContext.SimulationService.GetUnitWrapper(entity));
		return entity;
	}

	private Entity CreateEcsUnitEntityWithMeta(string id, string name, float hp, float damage, float range, float armor, float speed, Vector3 pos, PlayerEntity owner, UnitMetadata meta, bool hasShields, bool hasWeapons)
	{
		return GameContext.UnitSpawnService.CreateEcsUnitEntity(
			id, name, hp, damage, range, armor, speed,
			meta.ScanRadius > 0 ? meta.ScanRadius : 15.0f, meta.IsHero,
			meta.AttackCooldown > 0 ? meta.AttackCooldown : 1.5f, GetUnitPathingFlags(meta), pos, owner,
			owner.Value, hasShields, hasWeapons, meta.Targets,
			meta.HpRegen, meta.HpRegenCombatDelay, meta.MaxMana, meta.ManaRegen,
			meta.MagicResistance,
			meta.FlatArmorPenetration, meta.PercentArmorPenetration, meta.DamageVariance,
			string.IsNullOrEmpty(meta.DamageType) ? "physical" : meta.DamageType,
			meta.CritChance, meta.CritMultiplier > 0f ? meta.CritMultiplier : 1f,
			meta.SplashType, meta.SplashInnerRadius, meta.SplashMediumRadius, meta.SplashOuterRadius,
			meta.SplashInnerRatio, meta.SplashMediumRatio, meta.SplashOuterRatio, meta.FriendlyFire,
			meta.PushPriority, !string.IsNullOrEmpty(meta.MovementType) ? meta.MovementType : "Ground",
			meta.FlatMagicPenetration
		);
	}

	private void ApplyUnitAttributes(Entity entity, UnitMetadata meta)
	{
		if (meta.Strength != 0f || meta.Agility != 0f || meta.Vitality != 0f || meta.Intelligence != 0f || meta.Wisdom != 0f || meta.Fortune != 0f)
		{
			World.Set(entity, new UnitAttributes(meta.Strength, meta.Agility, meta.Vitality, meta.Intelligence, meta.Wisdom, meta.Fortune));
			AttributeStatCalculator.RecalculateEntityStats(World, entity);
		}
	}

	private void ApplyGigachadBuffsIfNeeded(Entity entity, int playerIndex)
	{
		if (!GameContext.CheatService.GigachadEnabled) return;
		int localPlayerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		if (localPlayerIndex <= 0) return;
		if (NetworkService.ArePlayerIndicesEnemies(localPlayerIndex, playerIndex)) return;
		if (World.Has<Health>(entity)) World.Set(entity, new Health(9000f, 9000f));
		if (World.Has<Attack>(entity))
		{
			var atk = World.Get<Attack>(entity);
			World.Set(entity, new Attack(9001f, atk.Range, atk.Cooldown, atk.CurrentCooldown));
		}
	}

	private void HandleBuildingSpawn(Entity entity, Unit3D unit3D, string id, Vector3 pos)
	{
		UnitMetadata? meta = TryGetUnitOrBuildingMetadata(id, out var resolved) ? resolved : null;
		if (meta != null && !UnitEntityFactory.BlocksMovement(meta))
		{
			return;
		}

		World.SetOrAdd(entity, new BuildingSpawnOffset(new System.Numerics.Vector3(0f, 0f, 8f)));

		// A screen in a match holds the body the authority stated and works nothing out for itself; only a screen
		// playing no match supplies its own radius.
		if (GameContext.ServerSession != null) return;
		if (World.Has<CollisionRadius>(entity)) return;

		float baseRadius = meta != null
			? UnitEntityFactory.ResolveCollisionRadius(meta)
			: GameplayConstants.DefaultBuildingCollisionRadius;
		World.SetOrAdd(entity, new CollisionRadius(baseRadius));
	}

	private Unit3D SpawnUnit3D(Entity entity, string id, string modelPath, Vector3 pos, bool isBuilding, int player = 0, bool isFromQueue = false, bool executeSpawnShader = false)
	{
		int playerIndex = player >= 0 ? player : 0;

		SetupUnitEntityComponents(entity, id, playerIndex);

		var unit3D = CreateAndSetupUnit3DNode(entity, id, modelPath, pos, isBuilding, playerIndex);

		if (isBuilding) HandleBuildingSpawn(entity, unit3D, id, pos);

		ApplyMapEditorTransforms(unit3D);

		unit3D.Visible = true;
		unit3D.UpdateLodVisibility();

		EntityToUnit3D[entity] = unit3D;
		EntityIdToUnit3D[entity.Id] = unit3D;
		if (!PlayerToUnits.TryGetValue(playerIndex, out var pList))
		{
			pList = new List<Unit3D>();
			PlayerToUnits[playerIndex] = pList;
		}
		pList.Add(unit3D);
		AllUnits.Add(unit3D);

		UpdatePopulationStats(id, playerIndex, isFromQueue);

		if (executeSpawnShader) ExecuteSpawnShader(unit3D, id);

		return unit3D;
	}

	private void SetupUnitEntityComponents(Entity entity, string id, int playerIndex)
	{
		World.SetOrAdd(entity, new UnitOwnerPlayer(playerIndex));
		World.SetOrAdd(entity, new DefinitionId(id));
		ApplyGigachadBuffsIfNeeded(entity, playerIndex);
		SyncPreviousTransform(World, entity);
	}

	private static readonly QueryDescription _positionQuery = new QueryDescription().WithAll<Position>().WithNone<Building, PropIdentity, Dead>();
	private static readonly QueryDescription _rotationYQuery = new QueryDescription().WithAll<RotationY>().WithNone<Building, PropIdentity, Dead>();

	public void CapturePreviousStateSnapshots()
	{
		var world = GameContext.World;
		if (world == null) return;

		foreach (var entity in world.Query(in _positionQuery))
		{
			ref var pos = ref world.Get<Position>(entity);
			world.SetOrAdd(entity, new PreviousPosition(pos.Value));
		}

		foreach (var entity in world.Query(in _rotationYQuery))
		{
			ref var rot = ref world.Get<RotationY>(entity);
			world.SetOrAdd(entity, new PreviousRotationY(rot.Value));
		}
	}

	public static void SyncPreviousTransform(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity)) return;

		if (world.Has<Position>(entity))
		{
			world.SetOrAdd(entity, new PreviousPosition(world.Get<Position>(entity).Value));
		}

		if (world.Has<RotationY>(entity))
		{
			world.SetOrAdd(entity, new PreviousRotationY(world.Get<RotationY>(entity).Value));
		}
	}

	private Unit3D CreateAndSetupUnit3DNode(Entity entity, string id, string modelPath, Vector3 pos, bool isBuilding, int playerIndex)
	{
		var unit3D = new Unit3D { Entity = entity, UnitId = id, IsBuilding = isBuilding, Name = $"{id}_{entity.Id}", Player = playerIndex };

		if (!isBuilding && !GameContext.SessionState.IsMapEditorMode)
		{
		}

		AddChild(unit3D);
		unit3D.Position = pos;
		unit3D.LoadModel(modelPath);
		unit3D.UpdatePlayerColorVisual();

		return unit3D;
	}

	private void ApplyMapEditorTransforms(Unit3D unit3D)
	{
		if (!GameContext.SessionState.IsMapEditorMode) return;

		unit3D.RotationDegrees = new Vector3(0.0f, GameContext.EditorService.Tools.PlacementRotation, 0.0f);
		unit3D.Scale *= EditorPlacementScale;
	}

	private void UpdatePopulationStats(string id, int playerIndex, bool isFromQueue)
	{
		if (_multiplayerActive) return;
		if (!UnitRegistry.TryGetValue(id, out var popMeta)) return;

		if (!isFromQueue && popMeta.PopCost > 0)
		{
			int curPop = GameContext.ResourceEconomyService?.GetPlayerCurrentPopulation(playerIndex) ?? 0;
			GameContext.ResourceEconomyService?.SetPlayerCurrentPopulation(playerIndex, curPop + popMeta.PopCost);
		}
	}

	private void ExecuteSpawnShader(Unit3D unit3D, string id)
	{
		string spawnShader = GetModelSpawnShader(unit3D);
		if (string.IsNullOrEmpty(spawnShader)) spawnShader = GetModelSpawnShader(id);

		unit3D.TryPlaySpawnShader(spawnShader);
	}

}
