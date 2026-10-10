using System.Numerics;

namespace Realm.Shared.ModelOptimization;

public struct SmoothedVertexData
{
	public Vector3 Position;
	public Vector3 Normal;
	public Vector2 UV0;
	public Vector2 UV1;
	public Vector4 Joints0;
	public Vector4 Weights0;
	public Vector4 Color0;
	public Vector4 Tangent;
}