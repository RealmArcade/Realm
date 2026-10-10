using Arch.Core;
using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the target of a pending or active cast action.
/// </summary>
public record struct CastTarget(Vector3 Position, Entity EntityTarget);
