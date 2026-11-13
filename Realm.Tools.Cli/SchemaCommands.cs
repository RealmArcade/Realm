using System.Text;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Tools.Cli;

public static class SchemaCommands
{
	private static readonly (string FileName, Type ModelType)[] SchemaTargets =
	{
		("manifest.schema.json", typeof(MapManifest)),
		("metadata.schema.json", typeof(MapMetadata)),
		("terrain.schema.json", typeof(MapSaveData))
	};

	public static int ExecuteManifest(GenerateManifestSchemaOptions options)
	{
		string outputPath = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : "manifest.schema.json";
		return WriteSchema(typeof(MapManifest), outputPath);
	}

	public static int ExecuteMetadata(GenerateMetadataSchemaOptions options)
	{
		string outputPath = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : "metadata.schema.json";
		return WriteSchema(typeof(MapMetadata), outputPath);
	}

	public static int ExecuteTerrain(GenerateTerrainSchemaOptions options)
	{
		string outputPath = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : "terrain.schema.json";
		return WriteSchema(typeof(MapSaveData), outputPath);
	}

	public static int ExecuteAll(GenerateSchemasOptions options)
	{
		string baseDir = ResolveBaseDirectory(options.Output);
		EnsureDirectory(baseDir);

		foreach ((string fileName, Type modelType) in SchemaTargets)
		{
			int exitCode = WriteSchema(modelType, Path.Combine(baseDir, fileName));
			if (exitCode != 0)
			{
				return exitCode;
			}
		}

		Console.WriteLine($"Schema vocabulary version: {RealmJsonSchemaExporter.VocabularyVersion}");
		return 0;
	}

	/// <summary>
	///     Verifies that each committed schema still matches what the model generates, so a hand-edited or stale schema
	///     fails the build instead of shipping.
	/// </summary>
	public static int ExecuteVerify(VerifySchemasOptions options)
	{
		string baseDir = ResolveBaseDirectory(options.Output);
		bool failed = false;

		foreach ((string fileName, Type modelType) in SchemaTargets)
		{
			string committedPath = Path.Combine(baseDir, fileName);
			string? drift = RealmJsonSchemaExporter.FindSchemaDrift(modelType, committedPath);
			if (drift is null)
			{
				Console.WriteLine($"Schema up to date: {fileName}");
				continue;
			}

			failed = true;
			Console.Error.WriteLine(drift);
		}

		if (failed)
		{
			Console.Error.WriteLine("Run scripts/generate-schemas.ps1 and commit the regenerated schemas.");
			return 1;
		}

		return 0;
	}

	private static int WriteSchema(Type modelType, string outputPath)
	{
		RealmSchemaDocument document = RealmJsonSchemaExporter.GenerateSchema(modelType);
		string? outDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outDir))
		{
			EnsureDirectory(outDir);
		}

		File.WriteAllText(outputPath, document.Json, new UTF8Encoding(false));
		Console.WriteLine($"Generated schema: {outputPath}");
		ReportMissingDescriptions(Path.GetFileName(outputPath), document);
		return 0;
	}

	private static void ReportMissingDescriptions(string fileName, RealmSchemaDocument document)
	{
		if (document.MissingDescriptions.Count == 0)
		{
			return;
		}

		Console.WriteLine($"{fileName}: {document.MissingDescriptions.Count} declaration(s) carry no description:");
		foreach (string entry in document.MissingDescriptions)
		{
			Console.WriteLine($"  {entry}");
		}
	}

	private static string ResolveBaseDirectory(string? output)
	{
		return !string.IsNullOrWhiteSpace(output) ? output : Directory.GetCurrentDirectory();
	}

	private static void EnsureDirectory(string path)
	{
		if (!Directory.Exists(path))
		{
			Directory.CreateDirectory(path);
		}
	}
}
