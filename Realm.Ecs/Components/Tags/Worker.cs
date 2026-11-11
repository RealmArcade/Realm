using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag marking a unit as a resource worker: workers body-block the workers of their own player and pass through the
///     workers of every other player, so a gathering crew never jams on itself while rival crews walk through one another.
/// </summary>
[TagDefinition("Worker", "Worker", "Marks a unit as a resource worker for the worker collision rule.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct Worker;
