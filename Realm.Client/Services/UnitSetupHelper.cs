using Realm.Shared.Metadata;
using Arch.Core;
using Godot;

namespace Realm.Client.Services;

public static class UnitSetupHelper
{
    public static Entity CreateEntityFromSnapshot(
        UnitSpawnService spawnService,
        int entityId,
        string unitId,
        Realm.Client.NetworkVector3 position,
        Realm.Client.NetworkVector3 velocity,
        float rotationY,
        float maxHp,
        float currentHp,
        bool isBuilding,
        UnitMetadata meta,
        Entity ownerPlayerEntity,
        bool isEnemy,
        ref Realm.Ecs.Components.Core.NetworkMappingState mapping,
        out string fallbackModelPath)
    {
        var entity = spawnService.CreateEntityFromSnapshot(
            position.ToNumerics(),
            velocity.ToNumerics(),
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
            Realm.Ecs.Common.PlayerEntityExtensions.AsPlayerEntity(ownerPlayerEntity, GameContext.World),
            isEnemy
        );

        mapping.ServerToClientEntityMap[entityId] = entity;
        mapping.ClientToServerEntityMap[entity.Id] = entityId;

        string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : unitId;
        fallbackModelPath = spawnService.GetFallbackModelPath(targetModel, isBuilding);

        return entity;
    }
}
