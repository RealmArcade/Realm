using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the active player perspective ID being viewed by a live spectator.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct SpectatorPerspective(int Value);