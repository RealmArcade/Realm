using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents a request to spawn a visual effect.
/// </summary>
public record struct VFXRequest(string EffectTypeId, Vector3 Position, Vector3 TargetPosition, float Scale, float Speed, int EntityId = -1);
