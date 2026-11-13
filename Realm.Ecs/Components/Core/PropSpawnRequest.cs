using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents a request to spawn a prop with specified parameters.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct PropSpawnRequest
	{
		public int StringId;
		public Vector3 Position;
		public float RotationY;
		public float Scale;
		public int Level;

		public PropSpawnRequest(string propId, Vector3 position, float rotationY, float scale, int level = 0)
		{
			StringId = StringRegistry.GetOrAdd(propId);
			Position = position;
			RotationY = rotationY;
			Scale = scale;
			Level = level;
		}

		public string PropId
		{
			get => StringRegistry.Get(StringId);
			set => StringId = StringRegistry.GetOrAdd(value);
		}
	}
}
