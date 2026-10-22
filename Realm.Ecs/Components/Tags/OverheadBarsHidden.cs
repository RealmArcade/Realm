using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity never draws overhead health and mana bars.
/// </summary>
[TagDefinition("OverheadBarsHidden", "Overhead Bars Hidden", "Marks the entity as never drawing overhead health and mana bars.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct OverheadBarsHidden;
