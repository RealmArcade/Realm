using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the target of a pending or active cast action.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct CastTarget(Vector3 Position, Entity EntityTarget);