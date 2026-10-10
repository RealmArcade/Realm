using Arch.Core;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Tracks the active construction task assigned to a worker unit, referencing the building entity being constructed.
/// </summary>
public struct BuildTask
{
    public Entity BuildingEntity;
    public float TotalBuildTime;
    public float Progress;

    public BuildTask(Entity buildingEntity, float totalBuildTime)
    {
        BuildingEntity = buildingEntity;
        TotalBuildTime = totalBuildTime;
        Progress = 0f;
    }
}
