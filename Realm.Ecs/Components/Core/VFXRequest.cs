using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents a request to spawn a visual effect.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct VFXRequest
{
	public int StringId;
	public Vector3 Position;
	public Vector3 TargetPosition;
	public float Scale;
	public float Speed;
	public int EntityId;

	public VFXRequest(string effectTypeId, Vector3 position, Vector3 targetPosition, float scale, float speed, int entityId = -1)
	{
		StringId = StringRegistry.GetOrAdd(effectTypeId);
		Position = position;
		TargetPosition = targetPosition;
		Scale = scale;
		Speed = speed;
		EntityId = entityId;
	}

	public string EffectTypeId
	{
		get => StringRegistry.Get(StringId);
		set => StringId = StringRegistry.GetOrAdd(value);
	}
}
