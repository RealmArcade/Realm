using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core;

/// <summary>
///     Stores the calculated horizontal collision radius of an obstacle for navigation mesh baking.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct CollisionRadius(float Value);