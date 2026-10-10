using System.Numerics;

namespace Realm.Shared.ModelOptimization;

public readonly struct VertexWeldKey : IEquatable<VertexWeldKey>
{
	public readonly int PosX;
	public readonly int PosY;
	public readonly int PosZ;
	public readonly int NormX;
	public readonly int NormY;
	public readonly int NormZ;
	public readonly int UvX;
	public readonly int UvY;
	public readonly int ExtraHash;

	public VertexWeldKey(
		Vector3 position,
		Vector3 normal,
		Vector2 uv0,
		Vector2 uv1,
		Vector4 joints0,
		Vector4 weights0,
		Vector4 color0)
	{
		PosX = (int)MathF.Round(position.X * 10000.0f);
		PosY = (int)MathF.Round(position.Y * 10000.0f);
		PosZ = (int)MathF.Round(position.Z * 10000.0f);

		NormX = (int)MathF.Round(normal.X * 1000.0f);
		NormY = (int)MathF.Round(normal.Y * 1000.0f);
		NormZ = (int)MathF.Round(normal.Z * 1000.0f);

		UvX = (int)MathF.Round(uv0.X * 10000.0f);
		UvY = (int)MathF.Round(uv0.Y * 10000.0f);

		int u1X = (int)MathF.Round(uv1.X * 10000.0f);
		int u1Y = (int)MathF.Round(uv1.Y * 10000.0f);

		int j0 = (int)joints0.X;
		int j1 = (int)joints0.Y;
		int j2 = (int)joints0.Z;
		int j3 = (int)joints0.W;

		int w0 = (int)MathF.Round(weights0.X * 1000.0f);
		int w1 = (int)MathF.Round(weights0.Y * 1000.0f);
		int w2 = (int)MathF.Round(weights0.Z * 1000.0f);
		int w3 = (int)MathF.Round(weights0.W * 1000.0f);

		int cR = (int)MathF.Round(color0.X * 255.0f);
		int cG = (int)MathF.Round(color0.Y * 255.0f);
		int cB = (int)MathF.Round(color0.Z * 255.0f);
		int cA = (int)MathF.Round(color0.W * 255.0f);

		int skinHash = HashCode.Combine(j0, j1, j2, j3, w0, w1, w2, w3);
		int colHash = HashCode.Combine(cR, cG, cB, cA, u1X, u1Y);
		ExtraHash = HashCode.Combine(skinHash, colHash);
	}

	public bool Equals(VertexWeldKey other)
	{
		return PosX == other.PosX && PosY == other.PosY && PosZ == other.PosZ &&
		       NormX == other.NormX && NormY == other.NormY && NormZ == other.NormZ &&
		       UvX == other.UvX && UvY == other.UvY &&
		       ExtraHash == other.ExtraHash;
	}

	public override bool Equals(object? obj)
	{
		return obj is VertexWeldKey other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(PosX, PosY, PosZ, NormX, NormY, NormZ, UvX, HashCode.Combine(UvY, ExtraHash));
	}
}