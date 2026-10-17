using System.Numerics;
using Arch.Core;

namespace Realm.Client.Services;

public struct SpawningRequest
{
	public string UnitId;
	public Vector3 Position;
	public Entity BuildingEntity;
	public bool IsFromQueue;
}