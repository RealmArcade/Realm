using System.Numerics;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	/// Represents a request to spawn a unit with specified parameters.
	/// </summary>
	public struct UnitSpawnRequest
	{
		public string UnitId;
		public Vector3 Position;
		public float RotationY;
		public float Scale;
		public bool IsEnemy;
		public int Player;

		public UnitSpawnRequest(string unitId, Vector3 position, float rotationY, float scale, bool isEnemy, int player = 0)
		{
			UnitId = unitId;
			Position = position;
			RotationY = rotationY;
			Scale = scale;
			IsEnemy = isEnemy;
			Player = player >= 0 ? player : 0;
		}
	}
}
