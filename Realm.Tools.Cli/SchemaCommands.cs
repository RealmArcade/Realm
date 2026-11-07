using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blake3;
using CommandLine;
using Realm.Shared;
using Realm.Shared.Animation;
using Realm.Shared.Audio;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Terrain;
using Realm.Shared.Textures;

namespace Realm.Tools.Cli;

internal static class SchemaCommands
{
	internal static int ExecuteManifest(GenerateManifestSchemaOptions options)
	{
		string schemaJson = MapManifest.GenerateJsonSchema();
		string outputPath = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : "manifest.schema.json";
		string? outDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}

		File.WriteAllText(outputPath, schemaJson, new UTF8Encoding(false));
		Console.WriteLine($"Generated schema: {outputPath}");
		return 0;
	}

	internal static int ExecuteMetadata(GenerateMetadataSchemaOptions options)
	{
		string schemaJson = MapMetadata.GenerateJsonSchema();
		string outputPath = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : "metadata.schema.json";
		string? outDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}

		File.WriteAllText(outputPath, schemaJson, new UTF8Encoding(false));
		Console.WriteLine($"Generated schema: {outputPath}");
		return 0;
	}

	internal static int ExecuteTerrain(GenerateTerrainSchemaOptions options)
	{
		string schemaJson = MapSaveData.GenerateJsonSchema();
		string outputPath = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : "terrain.schema.json";
		string? outDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}

		File.WriteAllText(outputPath, schemaJson, new UTF8Encoding(false));
		Console.WriteLine($"Generated schema: {outputPath}");
		return 0;
	}

	internal static int ExecuteAll(GenerateSchemasOptions options)
	{
		string baseDir = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : Directory.GetCurrentDirectory();
		if (!Directory.Exists(baseDir))
		{
			Directory.CreateDirectory(baseDir);
		}

		ExecuteManifest(new GenerateManifestSchemaOptions { Output = Path.Combine(baseDir, "manifest.schema.json") });
		ExecuteMetadata(new GenerateMetadataSchemaOptions { Output = Path.Combine(baseDir, "metadata.schema.json") });
		ExecuteTerrain(new GenerateTerrainSchemaOptions { Output = Path.Combine(baseDir, "terrain.schema.json") });
		return 0;
	}
}
