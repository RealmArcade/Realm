using System.Numerics;
using Realm.Ecs;

namespace Realm.Client.Services;

public struct SpawningRequest
{
	public string UnitId;
	public Vector3 Position;
	public Entity BuildingEntity;
	public bool IsFromQueue;
}