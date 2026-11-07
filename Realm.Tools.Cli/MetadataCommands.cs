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

internal static class MetadataCommands
{
	private static readonly string[] MetadataAddModes = new[] { "add", "update", "set", "write", "embed" };
	private static readonly string[] MetadataRemoveModes = new[] { "remove", "delete", "clear", "strip" };
	private static readonly string[] MetadataHashModes = new[] { "blake3", "hash" };

	internal static int Execute(MetadataOptions options)
	{
		string mode = options.Mode?.ToLowerInvariant() ?? "read";

		if (MetadataAddModes.Contains(mode))
		{
			return ExecuteMetadataAdd(options);
		}

		if (MetadataRemoveModes.Contains(mode))
		{
			return ExecuteMetadataRemove(options);
		}

		if (MetadataHashModes.Contains(mode))
		{
			return Blake3Command.Execute(new Blake3Options { Input = options.Input, Recursive = options.Recursive });
		}

		return ExecuteMetadataRead(options);
	}

	internal static string FormatFileMetadata(string filePath)
	{
		string? rawMeta = RealmMetadataHelper.ExtractMetadata(filePath);
		JsonObject metaObj;
		if (!string.IsNullOrWhiteSpace(rawMeta))
		{
			try
			{
				metaObj = JsonNode.Parse(rawMeta) as JsonObject ?? new JsonObject();
			}
			catch
			{
				metaObj = new JsonObject();
				metaObj["raw"] = rawMeta;
			}
		}
		else
		{
			metaObj = new JsonObject();
		}

		if (!metaObj.ContainsKey("blake3") || string.IsNullOrWhiteSpace(metaObj["blake3"]?.ToString()))
		{
			string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(filePath);
			metaObj["blake3"] = canonicalBlake3;
		}

		return metaObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
	}

	internal static int ExecuteMetadataRead(MetadataOptions options)
	{
		if (!CliFileSet.EnsurePathExists(options.Input)) return 1;

		if (File.Exists(options.Input))
		{
			string ext = Path.GetExtension(options.Input).ToLowerInvariant();
			if (!RealmMetadataHelper.SupportsMetadata(ext))
			{
				Console.Error.WriteLine($"Error: Unsupported file format '{ext}' for metadata. Supported formats: .rmesh, .raud, .rtex, .ranim, .glb, .ogg");
				return 1;
			}

			string metaToDisplay = FormatFileMetadata(options.Input);
			Console.WriteLine(metaToDisplay);

			if (!string.IsNullOrEmpty(options.Output))
			{
				string? dir = Path.GetDirectoryName(options.Output);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllText(options.Output, metaToDisplay);
			}

			return 0;
		}

		var files = CliFileSet.TraverseFiles(options.Input, options.Recursive, file => RealmMetadataHelper.SupportsMetadata(Path.GetExtension(file).ToLowerInvariant())).ToArray();
		int foundCount = 0;
		foreach (var file in files)
		{
			string metaToDisplay = FormatFileMetadata(file);
			Console.WriteLine($"--- {file} ---");
			Console.WriteLine(metaToDisplay);
			foundCount++;
		}

		Console.WriteLine($"Extracted metadata from {foundCount} file(s).");
		return 0;
	}

	internal static bool PrepareMetadataJsonForFile(string targetPath, ref string inputJsonContent, bool isUpdate, string? explicitAssetType, out string error)
	{
		error = string.Empty;
		string ext = Path.GetExtension(targetPath).ToLowerInvariant();
		try
		{
			if (!TryParseMetadataJson(inputJsonContent, out var inputObj, out error))
			{
				return false;
			}

			if (!ResolveAssetType(explicitAssetType, ext, inputObj, out error))
			{
				return false;
			}

			string? existingMeta = RealmMetadataHelper.ExtractMetadata(targetPath);
			JsonObject finalObj;

			if (isUpdate)
			{
				finalObj = MergeMetadataUpdate(existingMeta, inputObj);
			}
			else
			{
				finalObj = MergeMetadataCreate(existingMeta, inputObj);
			}

			if (!finalObj.ContainsKey("format"))
			{
				finalObj["format"] = ext.TrimStart('.');
			}

			if (!finalObj.ContainsKey("created_utc") || finalObj["created_utc"] == null)
			{
				finalObj["created_utc"] = DateTime.UtcNow.ToString("O");
			}

			string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(targetPath);
			finalObj["blake3"] = canonicalBlake3;

			inputJsonContent = finalObj.ToJsonString();
			return true;
		}
		catch (Exception ex)
		{
			error = $"Invalid JSON metadata: {ex.Message}";
			return false;
		}
	}

	internal static bool TryParseMetadataJson(string inputJsonContent, out JsonObject inputObj, out string error)
	{
		var parsedNode = JsonNode.Parse(inputJsonContent);
		if (parsedNode is not JsonObject obj)
		{
			inputObj = new JsonObject();
			error = "Metadata must be a valid JSON object.";
			return false;
		}

		inputObj = obj;
		error = string.Empty;
		return true;
	}

	internal static string? GetStringPropertyOrNull(JsonObject inputObj, string propertyName)
	{
		if (inputObj.TryGetPropertyValue(propertyName, out var val))
		{
			return val?.ToString();
		}

		return null;
	}

	internal static string? GetRawAssetType(string? explicitAssetType, JsonObject inputObj)
	{
		if (!string.IsNullOrWhiteSpace(explicitAssetType))
		{
			return explicitAssetType;
		}

		return GetStringPropertyOrNull(inputObj, "asset_type");
	}

	internal static bool ResolveAssetType(string? explicitAssetType, string ext, JsonObject inputObj, out string error)
	{
		error = string.Empty;
		string? rawAssetType = GetRawAssetType(explicitAssetType, inputObj);

		if (string.IsNullOrEmpty(rawAssetType)) return true;

		if (!RealmMetadataHelper.IsValidAssetTypeForExtension(ext, rawAssetType, out string canonical, out var validTypes))
		{
			error = $"Invalid asset_type '{rawAssetType}' for format '{ext}'. Valid asset_type values for {ext} are: {string.Join(", ", validTypes)}.";
			return false;
		}

		inputObj["asset_type"] = canonical;
		return true;
	}

	internal static JsonObject MergeMetadataUpdate(string? existingMeta, JsonObject inputObj)
	{
		JsonObject finalObj;
		if (!string.IsNullOrEmpty(existingMeta))
		{
			try
			{
				finalObj = JsonNode.Parse(existingMeta) as JsonObject ?? new JsonObject();
			}
			catch
			{
				finalObj = new JsonObject();
			}
		}
		else
		{
			finalObj = new JsonObject();
		}

		foreach (var property in inputObj)
		{
			finalObj[property.Key] = property.Value?.DeepClone();
		}

		return finalObj;
	}

	internal static JsonObject MergeMetadataCreate(string? existingMeta, JsonObject inputObj)
	{
		if (string.IsNullOrEmpty(existingMeta)) return inputObj;

		try
		{
			if (JsonNode.Parse(existingMeta) is not JsonObject existingObj) return inputObj;

			CopyPropertyIfNotExists("format", existingObj, inputObj);
			CopyPropertyIfNotExists("is_compressed", existingObj, inputObj);
			CopyPropertyIfNotExists("created_utc", existingObj, inputObj);
		}
		catch
		{
		}

		return inputObj;
	}

	internal static void CopyPropertyIfNotExists(string key, JsonObject source, JsonObject target)
	{
		if (!target.ContainsKey(key) && source.ContainsKey(key))
		{
			target[key] = source[key]?.DeepClone();
		}
	}

	internal static int ExecuteMetadataAdd(MetadataOptions options)
	{
		if (!CliFileSet.EnsurePathExists(options.Input)) return 1;

		if (!PrepareMetadataAddData(options))
		{
			return 1;
		}

		string jsonContent = options.Data ?? "";
		if (File.Exists(options.Data))
		{
			jsonContent = File.ReadAllText(options.Data);
		}

		string mode = options.Mode?.ToLowerInvariant() ?? "add";
		bool isUpdate = mode is "update" or "set";

		if (File.Exists(options.Input))
		{
			return ProcessSingleMetadataAdd(options.Input, jsonContent, isUpdate, options.AssetType);
		}

		return ProcessDirectoryMetadataAdd(options, jsonContent, isUpdate);
	}

	internal static bool PrepareMetadataAddData(MetadataOptions options)
	{
		if (!string.IsNullOrEmpty(options.Data)) return true;

		var jsonNode = new JsonObject();
		if (!string.IsNullOrWhiteSpace(options.AssetType))
		{
			jsonNode["asset_type"] = options.AssetType;
		}

		if (jsonNode.Count > 0)
		{
			options.Data = jsonNode.ToJsonString();
			return true;
		}

		Console.Error.WriteLine("Error: --data (-d) or --type (-t) option is required for add mode.");
		return false;
	}

	internal static int ProcessSingleMetadataAdd(string inputPath, string jsonContent, bool isUpdate, string? assetType)
	{
		string processedJson = jsonContent;
		if (!PrepareMetadataJsonForFile(inputPath, ref processedJson, isUpdate, assetType, out string error))
		{
			Console.Error.WriteLine($"Error: {error}");
			return 1;
		}

		bool success = RealmMetadataHelper.AddMetadata(inputPath, processedJson);
		if (success)
		{
			Console.WriteLine($"Successfully added metadata to: {inputPath}");
			return 0;
		}

		Console.Error.WriteLine($"Failed to add metadata to: {inputPath}");
		return 1;
	}

	internal static int ProcessDirectoryMetadataAdd(MetadataOptions options, string jsonContent, bool isUpdate)
	{
		var files = CliFileSet.TraverseFiles(options.Input, options.Recursive, file => RealmMetadataHelper.SupportsMetadata(Path.GetExtension(file).ToLowerInvariant())).ToArray();
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			string fileJson = jsonContent;
			if (!PrepareMetadataJsonForFile(file, ref fileJson, isUpdate, options.AssetType, out string error))
			{
				Console.Error.WriteLine($"Failed to add metadata to {file}: {error}");
				failCount++;
				continue;
			}

			if (RealmMetadataHelper.AddMetadata(file, fileJson))
			{
				Console.WriteLine($"Added metadata to: {file}");
				successCount++;
			}
			else
			{
				Console.Error.WriteLine($"Failed to add metadata to: {file}");
				failCount++;
			}
		}

		Console.WriteLine($"Finished adding metadata. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}

	internal static int ExecuteMetadataRemove(MetadataOptions options)
	{
		if (!CliFileSet.EnsurePathExists(options.Input)) return 1;

		if (File.Exists(options.Input))
		{
			string ext = Path.GetExtension(options.Input).ToLowerInvariant();
			if (!RealmMetadataHelper.SupportsMetadata(ext))
			{
				Console.Error.WriteLine($"Error: Unsupported file format '{ext}' for metadata. Supported formats: .rmesh, .raud, .rtex, .ranim, .glb, .ogg");
				return 1;
			}

			RealmMetadataHelper.RemoveMetadata(options.Input);
			string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(options.Input);
			var metaObj = new JsonObject
			{
				["format"] = ext.TrimStart('.'),
				["blake3"] = canonicalBlake3
			};
			bool success = RealmMetadataHelper.AddMetadata(options.Input, metaObj.ToJsonString());

			if (success)
			{
				Console.WriteLine($"Successfully removed metadata from: {options.Input}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to remove metadata from: {options.Input}");
				return 1;
			}
		}

		var files = CliFileSet.TraverseFiles(options.Input, options.Recursive, file => RealmMetadataHelper.SupportsMetadata(Path.GetExtension(file).ToLowerInvariant())).ToArray();
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			string ext = Path.GetExtension(file).ToLowerInvariant();
			RealmMetadataHelper.RemoveMetadata(file);
			string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(file);
			var metaObj = new JsonObject
			{
				["format"] = ext.TrimStart('.'),
				["blake3"] = canonicalBlake3
			};

			if (RealmMetadataHelper.AddMetadata(file, metaObj.ToJsonString()))
			{
				Console.WriteLine($"Removed metadata from: {file}");
				successCount++;
			}
			else
			{
				Console.Error.WriteLine($"Failed to remove metadata from: {file}");
				failCount++;
			}
		}

		Console.WriteLine($"Finished removing metadata. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}
}
