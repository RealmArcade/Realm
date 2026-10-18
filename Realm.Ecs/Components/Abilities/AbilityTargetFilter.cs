using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Abilities;

/// <summary>
///     A component that defines the targeting criteria for an ability.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct AbilityTargetFilter(TargetAlliance Alliances, float SearchRadius);