using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Represents the Food resource.
/// </summary>
[ResourceDefinition("Food", "Food", "Sustains units.", "res://icons/food.png")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct Food;