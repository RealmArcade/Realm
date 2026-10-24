using System.Runtime.InteropServices;
using System.Numerics;
using Realm.MapAPI;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents a request to spawn a decal with specified parameters.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct DecalSpawnRequest
	{
		public int StringId;
		public Vector3 Position;
		public float RotationX;
		public float RotationY;
		public float RotationZ;
		public float Scale;

		public DecalSpawnRequest(string decalId, Vector3 position, float rotationY, float scale)
		{
			StringId = StringRegistry.GetOrAdd(decalId);
			Position = position;
			RotationX = 0f;
			RotationY = rotationY;
			RotationZ = 0f;
			Scale = scale;
		}

		public DecalSpawnRequest(string decalId, Vector3 position, Vector3 rotation, float scale)
		{
			StringId = StringRegistry.GetOrAdd(decalId);
			Position = position;
			RotationX = rotation.X;
			RotationY = rotation.Y;
			RotationZ = rotation.Z;
			Scale = scale;
		}

		public string DecalId
		{
			get => StringRegistry.Get(StringId);
			set => StringId = StringRegistry.GetOrAdd(value);
		}
	}
}
