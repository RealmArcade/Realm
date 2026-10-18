using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Specifies a cost in terms of a specific resource.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct ResourceCost(ResourceId ResourceTypeId, int Amount);