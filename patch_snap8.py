import sys

with open('Realm.Client/Services/UnitSpawnService.cs', 'r') as f:
    spawn = f.read()

# Since `UnitSnapshot` and `ReplayUnitSnapshot` are both in `Realm.Client`, we could create an interface `IUnitSnapshot` in `Realm.Client`.
# But wait, is it worth it for 23 lines? 
# An alternative is to just extract a method that takes the primitive variables directly (which is almost what `CreateEntityFromSnapshot` does but it misses the mapping part).
# Let's extract a method in `NetworkMappingState` or `UnitSpawnService` that just does the mapping logic.

method_new = """	public Arch.Core.Entity InstantiateAndMapSnapshot(
		ref Realm.Client.Core.NetworkMappingState mapping, 
		int entityId,
		System.Numerics.Vector3 position,
		System.Numerics.Vector3 velocity,
		float rotationY,
		string unitId,
		int ownerPlayerEntityId,
		float currentHp,
		float maxHp,
		bool isBuilding,
		Arch.Core.Entity ownerPlayerEntity, 
		bool isEnemy, 
		out string fallbackModelPath)
	{
		if (Realm.Client.Core.GameHost.TryGetUnitOrBuildingMetadata(unitId, out var meta))
		{
			var entity = CreateEntityFromSnapshot(
				position,
				velocity,
				rotationY,
				unitId,
				meta.Name,
				maxHp,
				currentHp,
				meta.Damage,
				meta.Range,
				meta.AttackCooldown,
				meta.Armor,
				meta.Speed,
				ownerPlayerEntity.AsPlayerEntity(GameContext.World),
				isEnemy
			);

			mapping.ServerToClientEntityMap[entityId] = entity;
			mapping.ClientToServerEntityMap[entity.Id] = entityId;

			string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : unitId;
			fallbackModelPath = GetFallbackModelPath(targetModel, isBuilding);
			return entity;
		}

		fallbackModelPath = string.Empty;
		return Arch.Core.Entity.Null;
	}
"""
method_old = """	public Arch.Core.Entity InstantiateEntityAndMapSnapshot(ref Realm.Client.Core.NetworkMappingState mapping, Realm.Shared.Distribution.SnapshotState snap, Arch.Core.Entity ownerPlayerEntity, bool isEnemy, out string fallbackModelPath)
	{
		if (Realm.Client.Core.GameHost.TryGetUnitOrBuildingMetadata(snap.UnitId, out var meta))
		{
			var entity = CreateEntityFromSnapshot(
				snap.Position.ToNumerics(),
				snap.Velocity.ToNumerics(),
				snap.RotationY,
				snap.UnitId,
				meta.Name,
				snap.MaxHp,
				snap.CurrentHp,
				meta.Damage,
				meta.Range,
				meta.AttackCooldown,
				meta.Armor,
				meta.Speed,
				ownerPlayerEntity.AsPlayerEntity(GameContext.World),
				isEnemy
			);

			mapping.ServerToClientEntityMap[snap.EntityId] = entity;
			mapping.ClientToServerEntityMap[entity.Id] = snap.EntityId;

			string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : snap.UnitId;
			fallbackModelPath = GetFallbackModelPath(targetModel, snap.IsBuilding);
			return entity;
		}

		fallbackModelPath = string.Empty;
		return Arch.Core.Entity.Null;
	}"""
spawn = spawn.replace(method_old, method_new)
with open('Realm.Client/Services/UnitSpawnService.cs', 'w') as f:
    f.write(spawn)


with open('Realm.Client/Services/NetworkService.cs', 'r') as f:
    net = f.read()

dup_net = """		var entity = GameContext.UnitSpawnService.CreateEntityFromSnapshot(
			snap.Position.ToNumerics(),
			snap.Velocity.ToNumerics(),
			snap.RotationY,
			snap.UnitId,
			meta.Name,
			snap.MaxHp,
			snap.CurrentHp,
			meta.Damage,
			meta.Range,
			meta.AttackCooldown,
			meta.Armor,
			meta.Speed,
			ownerPlayerEntity.AsPlayerEntity(GameContext.World),
			isEnemy
		);

		mapping.ServerToClientEntityMap[snap.EntityId] = entity;
		mapping.ClientToServerEntityMap[entity.Id] = snap.EntityId;

		string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : snap.UnitId;
		modelPath = getFallbackModelPath(targetModel, snap.IsBuilding);
		return entity;"""

net = net.replace("return GameContext.UnitSpawnService.InstantiateEntityAndMapSnapshot(ref mapping, snap, ownerPlayerEntity, isEnemy, out modelPath);", "return GameContext.UnitSpawnService.InstantiateAndMapSnapshot(ref mapping, snap.EntityId, snap.Position.ToNumerics(), snap.Velocity.ToNumerics(), snap.RotationY, snap.UnitId, snap.OwnerPlayerEntityId, snap.CurrentHp, snap.MaxHp, snap.IsBuilding, ownerPlayerEntity, isEnemy, out modelPath);")

with open('Realm.Client/Services/NetworkService.cs', 'w') as f:
    f.write(net)

with open('Realm.Client/Services/ReplayService.cs', 'r') as f:
    rep = f.read()
rep = rep.replace("var entity = GameContext.UnitSpawnService.InstantiateEntityAndMapSnapshot(ref mapping, snap, ownerPlayerEntity, isEnemy, out string fallbackPath);", "var entity = GameContext.UnitSpawnService.InstantiateAndMapSnapshot(ref mapping, snap.EntityId, snap.Position.ToNumerics(), snap.Velocity.ToNumerics(), snap.RotationY, snap.UnitId, snap.OwnerPlayerEntityId, snap.CurrentHp, snap.MaxHp, snap.IsBuilding, ownerPlayerEntity, isEnemy, out string fallbackPath);")
with open('Realm.Client/Services/ReplayService.cs', 'w') as f:
    f.write(rep)

