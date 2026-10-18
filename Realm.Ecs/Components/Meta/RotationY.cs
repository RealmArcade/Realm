using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Meta
{
	/// <summary>
	///     Represents the visual rotation around the Y axis in degrees.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public record struct RotationY(float Value);
}