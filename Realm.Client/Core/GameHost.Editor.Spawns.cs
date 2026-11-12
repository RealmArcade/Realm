using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Simulation;

namespace Realm.Client.Core;
public partial class GameHost
{
	public Unit3D? SpawnUnitExternal(string unitId, Vector3 position, float rotationY, float scale, int player = 0)
	{
		int playerIndex = player >= 0 ? player : 0;
		bool isBuilding = false;
		if (!UnitRegistry.ContainsKey(unitId) && !BuildingRegistry.ContainsKey(unitId))
		{
			LoadUnitMetadata(!string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : GameContext.EditorService._editingDirectory);
		}

		UnitMetadata? meta;
		if (!UnitRegistry.TryGetValue(unitId, out meta))
		{
			if (BuildingRegistry.TryGetValue(unitId, out meta))
				isBuilding = true;
			else
				return null;
		}

		if (meta == null) return null;

		var playerOwner = GetPlayerEntityForPlayerIndex(playerIndex).AsPlayerEntity(World);

		if (string.IsNullOrEmpty(meta.ModelPath))
		{
			return null;
		}

		string modelPath = GetFallbackModelPath(meta.ModelPath, isBuilding);

		string name = meta.Name;
		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, position, playerOwner);

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, position, isBuilding, playerIndex);
		unit3D.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		unit3D.Scale = Vector3.One * (scale <= 0.001f ? 1.0f : scale);
		unit3D.Visible = true;
		unit3D.UpdateLodVisibility();

		World.SetOrAdd(entity, new CollisionScale(scale));

		World.SetOrAdd(entity, new RotationY(rotationY));

		World.SetOrAdd(entity, new ModelScale(scale));

		return unit3D;
	}

	private bool TryResolvePropExternalId(ref string propId, ref string cleanId)
	{
		bool inProp = CheckIfInRegistry(PropRegistry, propId, cleanId);
		bool inRes = CheckIfInRegistry(ResourceRegistry, propId, cleanId);
		if (inProp || inRes) return true;

		LoadUnitMetadata(!string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : GameContext.EditorService._editingDirectory);
		inProp = CheckIfInRegistry(PropRegistry, propId, cleanId);
		inRes = CheckIfInRegistry(ResourceRegistry, propId, cleanId);
		if (inProp || inRes) return true;

		inProp = TryResolvePropIdByModelPath(ref propId, ref cleanId);
		if (inProp) return true;

		return TryResolveResourceIdByModelPath(ref propId, ref cleanId);
	}

	public Prop3D? SpawnPropExternalWithParams(string propId, Vector3 position, float rotationY, float scale)
	{
		if (string.IsNullOrEmpty(propId)) return null;

		string cleanId = Path.GetFileNameWithoutExtension(propId);
		if (!TryResolvePropExternalId(ref propId, ref cleanId)) return null;

		float defaultAmount = GetDefaultResourceAmount(propId);
		position.Y = GetTerrainHeightAt(position);

		float baseRadius = ResolveTemplateCollisionRadius(propId);

		var entity = GameContext.EditorService.SpawnPropDomain(propId, new System.Numerics.Vector3(position.X, position.Y, position.Z), rotationY, scale, cleanId, defaultAmount, position.Y, baseRadius);

		if (!GameContext.SessionState.IsMapEditorMode && IsStaticPropAsset(propId))
		{
			GameContext.PropMultiMesh?.MarkDirty(propId);
			return null;
		}

		var prop = new Prop3D();
		prop.Name = "Prop_Prop3D";
		prop.Entity = entity;
		prop.PropId = propId;
		prop.Position = position;
		prop.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		prop.Scale = Vector3.One * (scale <= 0.001f ? 1.0f : scale);
		AddChild(prop);
		AllProps.Add(prop);
		GameContext.PropMultiMesh?.MarkDirty(propId);

		EntityToProp3D[entity] = prop;

		return prop;
	}

	/// <summary>
	///     Places an item on the ground as the map's terrain describes it, which is an object a unit can be sent to collect
	///     rather than a prop. The item's own template states the model the drop is drawn with, so the placed node is the
	///     same drop the authority spawns from the same entry.
	/// </summary>
	/// <param name="itemId">The item template the drop holds, by template id or bare name.</param>
	/// <param name="position">Where on the ground the item lies.</param>
	/// <returns>The placed drop, or null when the map declares no such item.</returns>
	public LootItem3D? SpawnItemExternal(string itemId, Vector3 position)
	{
		if (string.IsNullOrWhiteSpace(itemId)) return null;

		string? resolvedId = ResolveItemTemplateId(itemId);
		if (resolvedId == null) return null;

		var lootItem = new LootItem3D();
		lootItem.Name = "PlacedItem_LootItem3D";
		lootItem.Position = position;
		lootItem.Initialize(0, resolvedId, 1, 0f, GroundLootService.DefaultPickupRadius, false, -1);

		AddChild(lootItem);
		AllLootItems.Add(lootItem);
		return lootItem;
	}

	private string? ResolveItemTemplateId(string itemId)
	{
		if (ItemRegistry.ContainsKey(itemId)) return itemId;

		string cleanId = Path.GetFileNameWithoutExtension(itemId);
		if (!string.IsNullOrEmpty(cleanId) && ItemRegistry.ContainsKey(cleanId)) return cleanId;

		LoadUnitMetadata(!string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : GameContext.EditorService._editingDirectory);
		if (ItemRegistry.ContainsKey(itemId)) return itemId;
		if (!string.IsNullOrEmpty(cleanId) && ItemRegistry.ContainsKey(cleanId)) return cleanId;

		return null;
	}

	/// <summary>Removes every placed drop this screen drew, which is how a map load replaces the items the previous one held.</summary>
	public void ClearPlacedLootItems()
	{
		ClearSelectedLootItem();

		for (int index = AllLootItems.Count - 1; index >= 0; index--)
		{
			LootItem3D lootItem = AllLootItems[index];
			if (lootItem != null && GodotObject.IsInstanceValid(lootItem))
			{
				lootItem.IsDespawned = true;
				lootItem.QueueFree();
			}
		}

		AllLootItems.Clear();
	}

	private bool CheckIfInRegistry<T>(Dictionary<StringName, T> registry, string id, string cleanId)
	{
		return registry.ContainsKey(id) || (!string.IsNullOrEmpty(cleanId) && registry.ContainsKey(cleanId));
	}

	private bool TryResolvePropIdByModelPath(ref string propId, ref string cleanId)
	{
		foreach (var kvp in PropRegistry)
		{
			if (string.Equals(kvp.Value.ModelPath, propId, StringComparison.OrdinalIgnoreCase) ||
			    (!string.IsNullOrEmpty(cleanId) && string.Equals(Path.GetFileNameWithoutExtension(kvp.Value.ModelPath), cleanId, StringComparison.OrdinalIgnoreCase)))
			{
				propId = kvp.Key;
				cleanId = Path.GetFileNameWithoutExtension(propId);
				return true;
			}
		}

		return false;
	}

	private bool TryResolveResourceIdByModelPath(ref string propId, ref string cleanId)
	{
		foreach (var kvp in ResourceRegistry)
		{
			if (string.Equals(kvp.Value.ModelPath, propId, StringComparison.OrdinalIgnoreCase) ||
			    (!string.IsNullOrEmpty(cleanId) && string.Equals(Path.GetFileNameWithoutExtension(kvp.Value.ModelPath), cleanId, StringComparison.OrdinalIgnoreCase)))
			{
				propId = kvp.Key;
				cleanId = Path.GetFileNameWithoutExtension(propId);
				return true;
			}
		}

		return false;
	}

	private float GetDefaultResourceAmount(string propId) => DefinitionCatalog.DefaultResourceAmount(propId);

	public ProceduralVfxInstance3D SpawnVfxExternalWithParams(
		string vfxId,
		Vector3 position,
		Vector3 rotationDegrees,
		Vector3 scale,
		float normalOffset = 0f,
		VfxAttachmentConfig? customConfig = null,
		int ownerPlayerIndex = 0)
	{
		var entity = World.Create();
		var config = customConfig != null ? customConfig.Clone() : ResolveVfxConfig(vfxId);

		if (normalOffset != 0f) config.SurfaceNormalOffset = normalOffset;

		Vector3 safeScale = scale;
		if (Mathf.IsZeroApprox(safeScale.X)) safeScale.X = 1f;
		if (Mathf.IsZeroApprox(safeScale.Y)) safeScale.Y = 1f;
		if (Mathf.IsZeroApprox(safeScale.Z)) safeScale.Z = 1f;

		var vfx = new ProceduralVfxInstance3D(config)
		{
			Entity = entity,
			PlayerIndex = ownerPlayerIndex,
			Position = position,
			RotationDegrees = rotationDegrees,
			Scale = safeScale
		};

		vfx.Name = "Vfx_ProceduralVfxInstance3D";
		AddChild(vfx);
		AllVfx.Add(vfx);
		EntityToVfx3D[entity] = vfx;

		World.Add(entity, new Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		World.Add(entity, new RotationY(rotationDegrees.Y));
		World.Add(entity, new ModelScale(safeScale.X));

		return vfx;
	}

	public void RefreshVfxTemplateInstances(string previousTemplateId, VfxAttachmentConfig updatedConfig)
	{
		if (updatedConfig == null || AllVfx == null) return;

		string previousSlug = TemplateIDHelper.ParseTemplateID(previousTemplateId).Slug;
		string updatedSlug = TemplateIDHelper.ParseTemplateID(updatedConfig.VfxId).Slug;

		for (int index = 0; index < AllVfx.Count; index++)
		{
			var instance = AllVfx[index];
			if (instance == null || !GodotObject.IsInstanceValid(instance)) continue;

			string instanceSlug = TemplateIDHelper.ParseTemplateID(instance.VfxId).Slug;
			bool matchesPrevious = !string.IsNullOrEmpty(previousSlug) && string.Equals(instanceSlug, previousSlug, StringComparison.OrdinalIgnoreCase);
			bool matchesUpdated = !string.IsNullOrEmpty(updatedSlug) && string.Equals(instanceSlug, updatedSlug, StringComparison.OrdinalIgnoreCase);
			if (!matchesPrevious && !matchesUpdated) continue;

			instance.UpdateConfig(updatedConfig);
		}
	}

	private VfxAttachmentConfig ResolveVfxConfig(string vfxId)
	{
		string cleanId = vfxId ?? string.Empty;
		if (cleanId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)) cleanId = cleanId.Substring(4);

		if (GameContext.RegistryService.TryGetVfxConfig(vfxId, out var regCfg)) return regCfg.Clone();

		var cfgFromCleanId = TryCreateVfxConfigFromPrimitive(cleanId);
		if (cfgFromCleanId != null) return cfgFromCleanId;

		var cfgFromVfxId = TryCreateVfxConfigFromPrimitive(vfxId);
		if (cfgFromVfxId != null) return cfgFromVfxId;

		return CreateFallbackVfxConfig(vfxId, cleanId);
	}

	private VfxAttachmentConfig? TryCreateVfxConfigFromPrimitive(string? id)
	{
		if (!string.IsNullOrEmpty(id) && Enum.TryParse<VfxPrimitiveType>(id, true, out var primType))
		{
			var cfg = new VfxAttachmentConfig { VfxId = id, PrimitiveType = primType };
			if (primType == VfxPrimitiveType.ParticleSystem) cfg.ParticleConfig = SpellParticleConfig.CreatePreset(id);
			return cfg;
		}

		return null;
	}


	private VfxAttachmentConfig CreateFallbackVfxConfig(string? vfxId, string cleanId)
	{
		var particlePresets = SpellParticleConfig.GetAllPresets();
		if (particlePresets.ContainsKey(cleanId) || cleanId.StartsWith("particle_", StringComparison.OrdinalIgnoreCase))
		{
			return new VfxAttachmentConfig
			{
				VfxId = cleanId,
				Name = cleanId,
				PrimitiveType = VfxPrimitiveType.ParticleSystem,
				ParticleConfig = SpellParticleConfig.CreatePreset(cleanId)
			};
		}

		return new VfxAttachmentConfig
		{
			VfxId = vfxId ?? string.Empty,
			Name = cleanId,
			BaseTexture = cleanId,
			PrimitiveType = VfxPrimitiveType.VortexDisc
		};
	}

	public static Basis CreateAlignedBasis(Vector3 normal)
	{
		Vector3 up = normal.Normalized();
		Vector3 tangent = (Mathf.Abs(up.Dot(Vector3.Up)) > 0.9f) ? Vector3.Right : Vector3.Up;
		Vector3 right = tangent.Cross(up).Normalized();
		Vector3 forward = up.Cross(right).Normalized();
		return new Basis(right, up, forward);
	}


	public void SetUnitPlayerExternal(Unit3D unit, int playerIndex)
	{
		if (GodotObject.IsInstanceValid(unit) && GameContext.World is { } world && world.IsAlive(unit.Entity))
		{
			var playerOwner = GetPlayerEntityForPlayerIndex(playerIndex).AsPlayerEntity(world);
			GameContext.World.Set(unit.Entity, new Owner(playerOwner));

			GameContext.World.SetOrAdd(unit.Entity, new UnitOwnerPlayer(playerIndex));

			if (TryGetUnitOrBuildingMetadata(unit.UnitId, out var meta))
			{
				string name = meta.Name;
				GameContext.World.Set(unit.Entity, new Name(name));
			}

			unit.Player = playerIndex;
			unit.UpdatePlayerColorVisual();
			unit.IsSelected = unit.IsSelected;
		}
	}

	public void SetUnitTeamExternal(Unit3D unit, bool isEnemy)
	{
		int targetPlayer = isEnemy ? 1 : 0;
		if (GameContext.Lobby != null && GameContext.Lobby.PlayerList.Count > 0)
		{
			int localPlayerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
			var enemyPlayer = GameContext.Lobby.PlayerList.Find(p => NetworkService.ArePlayerIndicesEnemies(localPlayerIndex, p.Slot));
			if (isEnemy && enemyPlayer != null)
			{
				targetPlayer = enemyPlayer.Slot;
			}
		}

		SetUnitPlayerExternal(unit, targetPlayer);
	}

}
