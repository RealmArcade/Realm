import sys

with open('Realm.Client/Services/UnitSpawnService.cs', 'r') as f:
    spawn = f.read()

method_new = """	public Arch.Core.Entity InstantiateEntityAndMapSnapshot(
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
spawn = spawn.rstrip()
if spawn.endswith('}'):
    spawn = spawn[:-1] + method_new + "\n}"

with open('Realm.Client/Services/UnitSpawnService.cs', 'w') as f:
    f.write(spawn)

