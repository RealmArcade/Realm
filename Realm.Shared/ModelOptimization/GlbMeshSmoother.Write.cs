using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>writing primitives, buffer views and accessors back into the glb.</summary>
public static unsafe partial class GlbMeshSmoother
	{
	private static void WritePrimitiveToBin(
		JsonObject primObj,
		List<SmoothedVertexData> vertices,
		List<uint> indices,
		bool hasUv0,
		bool hasUv1,
		bool hasJoints0,
		bool hasWeights0,
		bool hasColor0,
		bool hasTangents,
		MemoryStream binStream,
		JsonArray bufferViews,
		JsonArray accessors)
	{
		int vertexCount = vertices.Count;
		var attributes = new JsonObject();

		WritePositions(vertices, vertexCount, binStream, bufferViews, accessors, attributes);
		WriteNormals(vertices, vertexCount, binStream, bufferViews, accessors, attributes);

		if (hasUv0) WriteUVs(vertices, vertexCount, binStream, bufferViews, accessors, attributes, 0);
		if (hasUv1) WriteUVs(vertices, vertexCount, binStream, bufferViews, accessors, attributes, 1);

		if (hasJoints0) WriteJoints(vertices, vertexCount, binStream, bufferViews, accessors, attributes);
		if (hasWeights0) WriteWeights(vertices, vertexCount, binStream, bufferViews, accessors, attributes);
		if (hasColor0) WriteColors(vertices, vertexCount, binStream, bufferViews, accessors, attributes);
		if (hasTangents) WriteTangents(vertices, vertexCount, binStream, bufferViews, accessors, attributes);

		primObj["attributes"] = attributes;

		WriteIndices(indices, primObj, binStream, bufferViews, accessors);
	}

	private static void WritePositions(List<SmoothedVertexData> vertices, int vertexCount, MemoryStream binStream, JsonArray bufferViews, JsonArray accessors, JsonObject attributes)
	{
		float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
		float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
		byte[] posBytes = new byte[vertexCount * 12];

		for (int i = 0; i < vertexCount; i++)
		{
			var p = vertices[i].Position;
			minX = MathF.Min(minX, p.X);
			minY = MathF.Min(minY, p.Y);
			minZ = MathF.Min(minZ, p.Z);
			maxX = MathF.Max(maxX, p.X);
			maxY = MathF.Max(maxY, p.Y);
			maxZ = MathF.Max(maxZ, p.Z);

			BitConverter.TryWriteBytes(posBytes.AsSpan(i * 12, 4), p.X);
			BitConverter.TryWriteBytes(posBytes.AsSpan(i * 12 + 4, 4), p.Y);
			BitConverter.TryWriteBytes(posBytes.AsSpan(i * 12 + 8, 4), p.Z);
		}

		int posBvIdx = AppendBufferView(binStream, posBytes, 34962, bufferViews);
		int posAccIdx = AppendAccessor(accessors, posBvIdx, 5126, vertexCount, "VEC3",
			new JsonArray(minX, minY, minZ),
			new JsonArray(maxX, maxY, maxZ));
		attributes["POSITION"] = posAccIdx;
	}

	private static void WriteNormals(List<SmoothedVertexData> vertices, int vertexCount, MemoryStream binStream, JsonArray bufferViews, JsonArray accessors, JsonObject attributes)
	{
		byte[] normBytes = new byte[vertexCount * 12];
		for (int i = 0; i < vertexCount; i++)
		{
			var n = vertices[i].Normal;
			BitConverter.TryWriteBytes(normBytes.AsSpan(i * 12, 4), n.X);
			BitConverter.TryWriteBytes(normBytes.AsSpan(i * 12 + 4, 4), n.Y);
			BitConverter.TryWriteBytes(normBytes.AsSpan(i * 12 + 8, 4), n.Z);
		}

		int normBvIdx = AppendBufferView(binStream, normBytes, 34962, bufferViews);
		int normAccIdx = AppendAccessor(accessors, normBvIdx, 5126, vertexCount, "VEC3");
		attributes["NORMAL"] = normAccIdx;
	}

	private static void WriteUVs(List<SmoothedVertexData> vertices, int vertexCount, MemoryStream binStream, JsonArray bufferViews, JsonArray accessors, JsonObject attributes, int uvIndex)
	{
		byte[] uvBytes = new byte[vertexCount * 8];
		for (int i = 0; i < vertexCount; i++)
		{
			var u = uvIndex == 0 ? vertices[i].UV0 : vertices[i].UV1;
			BitConverter.TryWriteBytes(uvBytes.AsSpan(i * 8, 4), u.X);
			BitConverter.TryWriteBytes(uvBytes.AsSpan(i * 8 + 4, 4), u.Y);
		}

		int uvBvIdx = AppendBufferView(binStream, uvBytes, 34962, bufferViews);
		int uvAccIdx = AppendAccessor(accessors, uvBvIdx, 5126, vertexCount, "VEC2");
		attributes[$"TEXCOORD_{uvIndex}"] = uvAccIdx;
	}

	private static void WriteJoints(List<SmoothedVertexData> vertices, int vertexCount, MemoryStream binStream, JsonArray bufferViews, JsonArray accessors, JsonObject attributes)
	{
		byte[] jointsBytes = new byte[vertexCount * 8];
		for (int i = 0; i < vertexCount; i++)
		{
			var j = vertices[i].Joints0;
			BitConverter.TryWriteBytes(jointsBytes.AsSpan(i * 8, 2), (ushort)Math.Clamp((int)j.X, 0, 65535));
			BitConverter.TryWriteBytes(jointsBytes.AsSpan(i * 8 + 2, 2), (ushort)Math.Clamp((int)j.Y, 0, 65535));
			BitConverter.TryWriteBytes(jointsBytes.AsSpan(i * 8 + 4, 2), (ushort)Math.Clamp((int)j.Z, 0, 65535));
			BitConverter.TryWriteBytes(jointsBytes.AsSpan(i * 8 + 6, 2), (ushort)Math.Clamp((int)j.W, 0, 65535));
		}

		int jBvIdx = AppendBufferView(binStream, jointsBytes, 34962, bufferViews);
		int jAccIdx = AppendAccessor(accessors, jBvIdx, 5123, vertexCount, "VEC4");
		attributes["JOINTS_0"] = jAccIdx;
	}

	private static void WriteWeights(List<SmoothedVertexData> vertices, int vertexCount, MemoryStream binStream, JsonArray bufferViews, JsonArray accessors, JsonObject attributes)
	{
		byte[] wBytes = new byte[vertexCount * 16];
		for (int i = 0; i < vertexCount; i++)
		{
			var w = vertices[i].Weights0;
			BitConverter.TryWriteBytes(wBytes.AsSpan(i * 16, 4), w.X);
			BitConverter.TryWriteBytes(wBytes.AsSpan(i * 16 + 4, 4), w.Y);
			BitConverter.TryWriteBytes(wBytes.AsSpan(i * 16 + 8, 4), w.Z);
			BitConverter.TryWriteBytes(wBytes.AsSpan(i * 16 + 12, 4), w.W);
		}

		int wBvIdx = AppendBufferView(binStream, wBytes, 34962, bufferViews);
		int wAccIdx = AppendAccessor(accessors, wBvIdx, 5126, vertexCount, "VEC4");
		attributes["WEIGHTS_0"] = wAccIdx;
	}

	private static void WriteColors(List<SmoothedVertexData> vertices, int vertexCount, MemoryStream binStream, JsonArray bufferViews, JsonArray accessors, JsonObject attributes)
	{
		byte[] cBytes = new byte[vertexCount * 16];
		for (int i = 0; i < vertexCount; i++)
		{
			var c = vertices[i].Color0;
			BitConverter.TryWriteBytes(cBytes.AsSpan(i * 16, 4), c.X);
			BitConverter.TryWriteBytes(cBytes.AsSpan(i * 16 + 4, 4), c.Y);
			BitConverter.TryWriteBytes(cBytes.AsSpan(i * 16 + 8, 4), c.Z);
			BitConverter.TryWriteBytes(cBytes.AsSpan(i * 16 + 12, 4), c.W);
		}

		int cBvIdx = AppendBufferView(binStream, cBytes, 34962, bufferViews);
		int cAccIdx = AppendAccessor(accessors, cBvIdx, 5126, vertexCount, "VEC4");
		attributes["COLOR_0"] = cAccIdx;
	}

	private static void WriteTangents(List<SmoothedVertexData> vertices, int vertexCount, MemoryStream binStream, JsonArray bufferViews, JsonArray accessors, JsonObject attributes)
	{
		byte[] tanBytes = new byte[vertexCount * 16];
		for (int i = 0; i < vertexCount; i++)
		{
			var t = vertices[i].Tangent;
			BitConverter.TryWriteBytes(tanBytes.AsSpan(i * 16, 4), t.X);
			BitConverter.TryWriteBytes(tanBytes.AsSpan(i * 16 + 4, 4), t.Y);
			BitConverter.TryWriteBytes(tanBytes.AsSpan(i * 16 + 8, 4), t.Z);
			BitConverter.TryWriteBytes(tanBytes.AsSpan(i * 16 + 12, 4), t.W);
		}

		int tanBvIdx = AppendBufferView(binStream, tanBytes, 34962, bufferViews);
		int tanAccIdx = AppendAccessor(accessors, tanBvIdx, 5126, vertexCount, "VEC4");
		attributes["TANGENT"] = tanAccIdx;
	}

	private static void WriteIndices(List<uint> indices, JsonObject primObj, MemoryStream binStream, JsonArray bufferViews, JsonArray accessors)
	{
		uint maxIdx = 0;
		for (int i = 0; i < indices.Count; i++)
		{
			if (indices[i] > maxIdx) maxIdx = indices[i];
		}

		bool useShort = maxIdx <= 65535;
		byte[] indBytes = new byte[indices.Count * (useShort ? 2 : 4)];
		for (int i = 0; i < indices.Count; i++)
		{
			if (useShort)
			{
				BitConverter.TryWriteBytes(indBytes.AsSpan(i * 2, 2), (ushort)indices[i]);
			}
			else
			{
				BitConverter.TryWriteBytes(indBytes.AsSpan(i * 4, 4), indices[i]);
			}
		}

		int indBvIdx = AppendBufferView(binStream, indBytes, 34963, bufferViews);
		int indAccIdx = AppendAccessor(accessors, indBvIdx, useShort ? 5123 : 5125, indices.Count, "SCALAR");
		primObj["indices"] = indAccIdx;
	}

	private static int AppendBufferView(MemoryStream stream, byte[] data, int target, JsonArray bufferViews)
	{
		while ((stream.Position % 4) != 0) stream.WriteByte(0);
		int offset = (int)stream.Position;
		stream.Write(data, 0, data.Length);
		while ((stream.Position % 4) != 0) stream.WriteByte(0);

		int bvIdx = bufferViews.Count;
		var bvObj = new JsonObject
		{
			["buffer"] = 0,
			["byteOffset"] = offset,
			["byteLength"] = data.Length,
			["target"] = target
		};
		bufferViews.Add(bvObj);
		return bvIdx;
	}

	private static int AppendAccessor(
		JsonArray accessors,
		int bufferViewIndex,
		int componentType,
		int count,
		string type,
		JsonArray? min = null,
		JsonArray? max = null)
	{
		int accIdx = accessors.Count;
		var accObj = new JsonObject
		{
			["bufferView"] = bufferViewIndex,
			["byteOffset"] = 0,
			["componentType"] = componentType,
			["count"] = count,
			["type"] = type
		};

		if (min != null) accObj["min"] = min;
		if (max != null) accObj["max"] = max;

		accessors.Add(accObj);
		return accIdx;
	}
}
