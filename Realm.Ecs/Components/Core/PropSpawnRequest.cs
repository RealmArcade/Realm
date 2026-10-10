using System.Numerics;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents a request to spawn a prop with specified parameters.
	/// </summary>
	public struct PropSpawnRequest
	{
		public string PropId;
		public Vector3 Position;
		public float RotationY;
		public float Scale;

		public PropSpawnRequest(string propId, Vector3 position, float rotationY, float scale)
		{
			PropId = propId;
			Position = position;
			RotationY = rotationY;
			Scale = scale;
		}
	}
}
