using System.Numerics;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

public static unsafe partial class GlbMeshSmoother
	{
	public const float DefaultCreaseAngleDegrees = 60.0f;

	public static byte[] SmoothMesh(byte[] inputBytes, float creaseAngleDegrees = DefaultCreaseAngleDegrees)
	{
		if (inputBytes == null || inputBytes.Length < 20)
		{
			return inputBytes ?? Array.Empty<byte>();
		}

		if (RmeshFile.IsRmeshBytes(inputBytes))
		{
			var (meta, glbPayload, _) = RmeshFile.Parse(inputBytes);
			byte[] smoothedGlb = SmoothGlbBytes(glbPayload, creaseAngleDegrees);
			return RmeshFile.Build(meta, smoothedGlb);
		}

		return SmoothGlbBytes(inputBytes, creaseAngleDegrees);
	}

	public static bool SmoothMeshFile(string inputPath, string? outputPath = null, float creaseAngleDegrees = DefaultCreaseAngleDegrees)
	{
		if (!File.Exists(inputPath)) return false;

		byte[] inputBytes = File.ReadAllBytes(inputPath);
		byte[] smoothedBytes = SmoothMesh(inputBytes, creaseAngleDegrees);

		string targetPath = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
		string? dir = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}

		File.WriteAllBytes(targetPath, smoothedBytes);
		return true;
	}

	private static byte[] SmoothGlbBytes(byte[] glbBytes, float creaseAngleDegrees)
	{
		var (jsonNode, binChunk, glbVersion) = GlbManifestUtils.ParseGlb(glbBytes);
		if (jsonNode is not JsonObject root || binChunk == null) return glbBytes;

		if (!IsValidGlb(root)) return glbBytes;

		RemoveMsftLodExtension(root, "extensionsUsed");
		RemoveMsftLodExtension(root, "extensionsRequired");
		CleanNodes(root);

		var bufferViews = (JsonArray)root["bufferViews"]!;
		var accessors = (JsonArray)root["accessors"]!;
		var meshes = (JsonArray)root["meshes"]!;
		var buffers = (JsonArray)root["buffers"]!;

		float cosThreshold = MathF.Cos(creaseAngleDegrees * (MathF.PI / 180.0f));
		var retainedBufferViews = GetRetainedBufferViews(root, bufferViews.Count, accessors);

		using var newBinStream = new MemoryStream();
		CopyRetainedBufferViews(bufferViews, retainedBufferViews, binChunk, newBinStream);

		byte[] compactedBinChunk = newBinStream.ToArray();

		for (int m = 0; m < meshes.Count; m++)
		{
			ProcessMesh(meshes[m], accessors, bufferViews, compactedBinChunk, cosThreshold, newBinStream);
		}

		while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);

		if (buffers[0] is JsonObject buf0)
		{
			buf0["byteLength"] = (int)newBinStream.Position;
		}

		byte[] newBin = newBinStream.ToArray();
		return GlbManifestUtils.BuildGlb(root, newBin, glbVersion);
	}

}
