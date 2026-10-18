using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Meta;

/// <summary>
///     Represents the visual scale multiplier applied to the entity's 3D model.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct ModelScale(float Value);