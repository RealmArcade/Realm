using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	/// Represents a request to spawn a unit with specified parameters.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct UnitSpawnRequest
	{
		public int StringId;
		public Vector3 Position;
		public float RotationY;
		public float Scale;
		public int Player;
		public int Level;

		public UnitSpawnRequest(string unitId, Vector3 position, float rotationY, float scale, int player = 0, int level = 0)
		{
			StringId = StringRegistry.GetOrAdd(unitId);
			Position = position;
			RotationY = rotationY;
			Scale = scale;
			Player = player >= 0 ? player : 0;
			Level = level;
		}

		public string UnitId
		{
			get => StringRegistry.Get(StringId);
			set => StringId = StringRegistry.GetOrAdd(value);
		}
	}
}
