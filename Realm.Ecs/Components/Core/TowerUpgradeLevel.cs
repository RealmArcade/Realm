using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the upgrade level of a defensive tower structure.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct TowerUpgradeLevel(int Value);