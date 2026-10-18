using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Defines the range in world units within which a unit scans for and acquires attack targets.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct ScanRadius(float Value);