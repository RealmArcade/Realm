using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>reading single floats, tex coords and component sizes.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private static float ReadSingle(byte[] binBytes, int offset)
	{
		return BinaryPrimitives.ReadSingleLittleEndian(binBytes.AsSpan(offset, 4));
	}

	private static float ReadTexCoordComponent(byte[] binBytes, int offset, TexCoordStream texCoords)
	{
		switch (texCoords.ComponentType)
		{
			case 5126:
				return ReadSingle(binBytes, offset);
			case 5123:
				ushort unsignedShortValue = BinaryPrimitives.ReadUInt16LittleEndian(binBytes.AsSpan(offset, 2));
				return texCoords.Normalized ? unsignedShortValue / 65535f : unsignedShortValue;
			case 5122:
				short shortValue = BinaryPrimitives.ReadInt16LittleEndian(binBytes.AsSpan(offset, 2));
				return texCoords.Normalized ? MathF.Max(shortValue / 32767f, -1f) : shortValue;
			default:
				byte byteValue = binBytes[offset];
				return texCoords.Normalized ? byteValue / 255f : byteValue;
		}
	}

	private static int GetComponentByteSize(int componentType)
	{
		return componentType switch
		{
			5120 or 5121 => 1,
			5122 or 5123 => 2,
			_ => 4
		};
	}
}
