using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Represents the Wood resource.
/// </summary>
[ResourceDefinition("Wood", "Wood", "A building material.", "res://icons/wood.png")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct Wood;