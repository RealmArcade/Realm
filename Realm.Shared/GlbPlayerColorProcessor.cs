using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

public static partial class GlbPlayerColorProcessor
	{
	public static GlbPlayerColorResult ProcessFile(
		string inputPath,
		string outputPath,
		GlbPlayerColorOptions options)
	{
		if (!File.Exists(inputPath))
		{
			return new GlbPlayerColorResult
			{
				Success = false,
				ErrorMessage = $"Input file does not exist: {inputPath}"
			};
		}

		try
		{
			byte[] inputBytes = File.ReadAllBytes(inputPath);
			var (success, outputBytes, errorMessage, maskedFaces, totalFaces, detectedKey) = ProcessBytes(inputBytes, options);

			if (!success)
			{
				return new GlbPlayerColorResult { Success = false, ErrorMessage = errorMessage };
			}

			string? dir = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			File.WriteAllBytes(outputPath, outputBytes!);
			RealmMetadataHelper.SyncBlake3Metadata(outputPath);

			return new GlbPlayerColorResult
			{
				Success = true,
				OutputFilePath = outputPath,
				MaskedFaceCount = maskedFaces,
				TotalFaceCount = totalFaces,
				DetectedChromaKey = detectedKey ?? options.ChromaKey
			};
		}
		catch (Exception ex)
		{
			return new GlbPlayerColorResult { Success = false, ErrorMessage = ex.Message };
		}
	}
}
