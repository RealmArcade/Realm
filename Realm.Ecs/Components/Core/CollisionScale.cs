using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core
{
	/// <summary>
	/// Represents the uniform collision scale or multiplier for physics calculations on the entity.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct CollisionScale
	{
		public float Value;

		public CollisionScale(float value)
		{
			Value = value;
		}
	}
}