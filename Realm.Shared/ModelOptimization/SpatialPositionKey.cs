using System.Numerics;

namespace Realm.Shared.ModelOptimization;

public readonly struct SpatialPositionKey : IEquatable<SpatialPositionKey>
{
	public readonly int X;
	public readonly int Y;
	public readonly int Z;

	public SpatialPositionKey(Vector3 position)
	{
		X = (int)MathF.Round(position.X * 10000.0f);
		Y = (int)MathF.Round(position.Y * 10000.0f);
		Z = (int)MathF.Round(position.Z * 10000.0f);
	}

	public bool Equals(SpatialPositionKey other)
	{
		return X == other.X && Y == other.Y && Z == other.Z;
	}

	public override bool Equals(object? obj)
	{
		return obj is SpatialPositionKey other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(X, Y, Z);
	}
}