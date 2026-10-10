namespace Realm.Ecs.Components.Core
{
	/// <summary>
	/// Represents the uniform collision scale or multiplier for physics calculations on the entity.
	/// </summary>
	public struct CollisionScale
	{
		public float Value;

		public CollisionScale(float value)
		{
			Value = value;
		}
	}
}
