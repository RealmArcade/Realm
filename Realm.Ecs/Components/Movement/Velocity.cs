using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Movement
{
	/// <summary>
	///     A component holding the movement velocity vector of an entity.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct Velocity
	{
		public Vector3 Value;

		public Velocity(Vector3 value)
		{
			Value = value;
		}
	}
}