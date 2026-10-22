using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity holds its current animation pose.
/// </summary>
[TagDefinition("AnimationsFrozen", "Animations Frozen", "Marks the entity as holding its current animation pose.")]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct AnimationsFrozen;
