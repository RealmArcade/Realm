using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the ownership of an entity by a player.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct Owner(PlayerEntity PlayerEntity);