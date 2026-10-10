using CommandLine;
using Realm.Shared;
using Realm.Shared.Animation;
using Realm.Shared.Audio;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Terrain;
using Realm.Shared.Textures;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Realm.Tools.Cli;

public static class Program
{
	public static int Main(string[] args)
	{
		var unhandledArgs = new List<string>(args.Length);
		for (int index = 0; index < args.Length; index++)
		{
			string argument = args[index];
			if (string.Equals(argument, "--eula-accept", StringComparison.OrdinalIgnoreCase))
			{
				_assetAgreementAccepted = true;
				continue;
			}

			unhandledArgs.Add(argument);
		}

		string[] originalArgs = unhandledArgs.ToArray();
		string[] sanitizedArgs = CommandLineArgsHelper.SanitizeArgs(
			originalArgs,
			typeof(MeshConvertOptions),
			typeof(TextureConvertOptions),
			typeof(AudioConvertOptions),
			typeof(FbxToRanimOptions),
			typeof(RanimRenderOptions),
			typeof(MetadataOptions),
			typeof(Blake3Options),
			typeof(MeshPlayerColorCliOptions),
			typeof(RigHumanoidOptions),
			typeof(KeygenOptions),
			typeof(GenerateManifestSchemaOptions),
			typeof(GenerateMetadataSchemaOptions),
			typeof(GenerateTerrainSchemaOptions),
			typeof(GenerateSchemasOptions));

		return Parser.Default.ParseArguments<MeshConvertOptions, TextureConvertOptions, AudioConvertOptions, FbxToRanimOptions, RanimRenderOptions, MetadataOptions, Blake3Options, MeshPlayerColorCliOptions, RigHumanoidOptions, KeygenOptions, GenerateManifestSchemaOptions, GenerateMetadataSchemaOptions, GenerateTerrainSchemaOptions, GenerateSchemasOptions>(sanitizedArgs)
			.WithParsed(options => CommandLineArgsHelper.ApplyBooleanOverrides(options, originalArgs))
			.MapResult(
				(MeshConvertOptions options) => ExecuteMeshConvert(options),
				(TextureConvertOptions options) => ExecuteTextureConvert(options),
				(AudioConvertOptions options) => ExecuteAudioConvert(options),
				(FbxToRanimOptions options) => ExecuteFbxToRanim(options),
				(RanimRenderOptions options) => ExecuteRanimRender(options),
				(MetadataOptions options) => ExecuteMetadata(options),
				(Blake3Options options) => ExecuteBlake3(options),
				(MeshPlayerColorCliOptions options) => ExecuteMeshPlayerColor(options),
				(RigHumanoidOptions options) => ExecuteRigHumanoid(options),
				(KeygenOptions options) => ExecuteKeygen(options),
				(GenerateManifestSchemaOptions options) => ExecuteGenerateManifestSchema(options),
				(GenerateMetadataSchemaOptions options) => ExecuteGenerateMetadataSchema(options),
				(GenerateTerrainSchemaOptions options) => ExecuteGenerateTerrainSchema(options),
				(GenerateSchemasOptions options) => ExecuteGenerateSchemas(options),
				errors => 1);
	}

	private static bool _assetAgreementAccepted = false;

	private static void EnsureAssetAgreementAccepted()
	{
		if (_assetAgreementAccepted) return;

		Console.WriteLine($"{RealmMetadataHelper.AssetAgreementWarning} Y/N");
		string? response = Console.ReadLine()?.Trim();
		if (string.Equals(response, "Y", StringComparison.OrdinalIgnoreCase))
		{
			_assetAgreementAccepted = true;
		}
		else
		{
			Console.WriteLine("Task cancelled.");
			Environment.Exit(0);
		}
	}

	private static bool EnsurePathExists(string inputPath)
	{
		if (File.Exists(inputPath) || Directory.Exists(inputPath))
		{
			return true;
		}

		Console.Error.WriteLine($"Error: Input path does not exist: {inputPath}");
		return false;
	}

	private static IEnumerable<string> TraverseFiles(string inputPath, bool recursive, Func<string, bool>? filter = null)
	{
		if (File.Exists(inputPath))
		{
			if (filter == null || filter(inputPath))
			{
				yield return inputPath;
			}
		}
		else if (Directory.Exists(inputPath))
		{
			var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			foreach (string file in Directory.EnumerateFiles(inputPath, "*.*", searchOption))
			{
				if (filter == null || filter(file))
				{
					yield return file;
				}
			}
		}
	}

	private static string ResolveTargetOutputPath(
		string inputRoot,
		string currentFile,
		string? outputDestination,
		bool inPlace,
		Func<string, string>? extensionResolver = null)
	{
		string targetExtension = extensionResolver != null
			? extensionResolver(currentFile)
			: Path.GetExtension(currentFile);

		if (inPlace)
		{
			return extensionResolver != null
				? Path.ChangeExtension(currentFile, targetExtension)
				: currentFile;
		}

		if (Directory.Exists(inputRoot))
		{
			return ResolveDirectoryTarget(inputRoot, currentFile, outputDestination, targetExtension);
		}

		if (!string.IsNullOrEmpty(outputDestination))
		{
			return ResolveOutputDestination(currentFile, outputDestination, targetExtension, extensionResolver != null);
		}

		return Path.ChangeExtension(currentFile, targetExtension);
	}

	private static string ResolveDirectoryTarget(
		string inputRoot,
		string currentFile,
		string? outputDestination,
		string targetExtension)
	{
		string relativePath = Path.GetRelativePath(inputRoot, currentFile);
		if (!string.IsNullOrEmpty(outputDestination))
		{
			string relativeTarget = Path.ChangeExtension(relativePath, targetExtension);
			return Path.Combine(outputDestination, relativeTarget);
		}
		return Path.ChangeExtension(currentFile, targetExtension);
	}

	private static string ResolveOutputDestination(
		string currentFile,
		string outputDestination,
		string targetExtension,
		bool hasExtensionResolver)
	{
		if (Directory.Exists(outputDestination) ||
			outputDestination.EndsWith(Path.DirectorySeparatorChar) ||
			outputDestination.EndsWith(Path.AltDirectorySeparatorChar))
		{
			string fileName = Path.ChangeExtension(Path.GetFileName(currentFile), targetExtension);
			return Path.Combine(outputDestination, fileName);
		}

		string explicitExt = Path.GetExtension(outputDestination);
		if (!string.IsNullOrEmpty(explicitExt))
		{
			return outputDestination;
		}

		return hasExtensionResolver
			? Path.ChangeExtension(outputDestination, targetExtension)
			: outputDestination;
	}

	private static int ProcessTraversedFiles(
		string inputPath,
		string? outputPath,
		bool recursive,
		bool inPlace,
		Func<string, bool> fileFilter,
		Func<string, string>? extensionResolver,
		Func<string, string, int> processFile,
		Func<string, string, string>? customPathResolver = null,
		string? summaryActionName = null)
	{
		if (!EnsurePathExists(inputPath))
		{
			return 1;
		}

		if (File.Exists(inputPath))
		{
			if (!fileFilter(inputPath))
			{
				string ext = Path.GetExtension(inputPath);
				Console.Error.WriteLine($"Error: Unsupported file format '{ext}' for input '{inputPath}'.");
				return 1;
			}

			string targetPath = customPathResolver != null
				? customPathResolver(inputPath, inputPath)
				: ResolveTargetOutputPath(inputPath, inputPath, outputPath, inPlace, extensionResolver);

			EnsureTargetDirectoryExists(targetPath);

			return processFile(inputPath, targetPath);
		}

		string fullInputDir = Path.GetFullPath(inputPath);
		var matchingFiles = TraverseFiles(fullInputDir, recursive, fileFilter).ToArray();

		if (matchingFiles.Length == 0)
		{
			Console.WriteLine($"No matching files found in: {inputPath}");
			return 0;
		}

		var (successCount, failCount) = ProcessMultipleFiles(matchingFiles, fullInputDir, outputPath, inPlace, extensionResolver, processFile, customPathResolver);

		if (!string.IsNullOrEmpty(summaryActionName))
		{
			Console.WriteLine($"Finished {summaryActionName}. {successCount} succeeded, {failCount} failed.");
		}

		return failCount > 0 ? 1 : 0;
	}

	private static void EnsureTargetDirectoryExists(string targetPath)
	{
		string? outDir = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
	}

	private static (int successCount, int failCount) ProcessMultipleFiles(
		string[] matchingFiles,
		string fullInputDir,
		string? outputPath,
		bool inPlace,
		Func<string, string>? extensionResolver,
		Func<string, string, int> processFile,
		Func<string, string, string>? customPathResolver)
	{
		int successCount = 0;
		int failCount = 0;

		foreach (string file in matchingFiles)
		{
			string targetPath = customPathResolver != null
				? customPathResolver(fullInputDir, file)
				: ResolveTargetOutputPath(fullInputDir, file, outputPath, inPlace, extensionResolver);

			EnsureTargetDirectoryExists(targetPath);

			int exitCode = processFile(file, targetPath);
			if (exitCode == 0)
			{
				successCount++;
			}
			else
			{
				failCount++;
			}
		}

		return (successCount, failCount);
	}

	private static int ExecuteRanimRender(RanimRenderOptions options)
	{
		RanimOutputFormat outputFormat = RanimOutputFormat.Webp;
		string formatLower = options.Format.Trim().ToLowerInvariant();

		if (formatLower == "webp")
		{
			outputFormat = RanimOutputFormat.Webp;
		}
		else if (formatLower == "spritesheet" || formatLower == "png")
		{
			outputFormat = RanimOutputFormat.Spritesheet;
		}
		else if (formatLower == "auto" && !string.IsNullOrEmpty(options.Output))
		{
			if (options.Output.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				outputFormat = RanimOutputFormat.Spritesheet;
			}
			else
			{
				outputFormat = RanimOutputFormat.Webp;
			}
		}

		var renderOptions = new Realm.Shared.Animation.RanimRenderOptions
		{
			Width = options.Size,
			Height = options.Size,
			Fps = options.Fps,
			MaxFrameCount = options.MaxFrames,
			Format = outputFormat,
			Scale = options.Scale,
			DrawBorder = !options.NoBorder,
			DrawShadow = !options.NoShadow,
			ModelPath = options.Model,
			Quality = options.Quality,
			Lossless = options.Lossless
		};

		string extension = outputFormat switch
		{
			RanimOutputFormat.Spritesheet => ".png",
			_ => ".webp"
		};

		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			inPlace: false,
			file => Path.GetExtension(file).Equals(".ranim", StringComparison.OrdinalIgnoreCase),
			_ => extension,
			(inputFile, targetFile) => ProcessSingleRanimRender(inputFile, targetFile, renderOptions),
			summaryActionName: "rendering animations");
	}

	private static int ProcessSingleRanimRender(string inputFile, string targetFile, Realm.Shared.Animation.RanimRenderOptions renderOptions)
	{
		var result = RanimRenderer.ExportFile(inputFile, targetFile, renderOptions);
		if (result.Success)
		{
			Console.WriteLine($"Successfully rendered ({result.FrameCount} frames): {inputFile} -> {targetFile}");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"Failed to render {inputFile}: {result.ErrorMessage}");
			return 1;
		}
	}

	private static readonly string[] MetadataAddModes = new[] { "add", "update", "set", "write", "embed" };
	private static readonly string[] MetadataRemoveModes = new[] { "remove", "delete", "clear", "strip" };
	private static readonly string[] MetadataHashModes = new[] { "blake3", "hash" };

	private static int ExecuteMetadata(MetadataOptions options)
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
			return ExecuteBlake3(new Blake3Options { Input = options.Input, Recursive = options.Recursive });
		}

		return ExecuteMetadataRead(options);
	}

	private static string FormatFileMetadata(string filePath)
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

	private static int ExecuteMetadataRead(MetadataOptions options)
	{
		if (!EnsurePathExists(options.Input)) return 1;

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

		var files = TraverseFiles(options.Input, options.Recursive, file => RealmMetadataHelper.SupportsMetadata(Path.GetExtension(file).ToLowerInvariant())).ToArray();
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

	private static bool PrepareMetadataJsonForFile(string targetPath, ref string inputJsonContent, bool isUpdate, string? explicitAssetType, out string error)
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

	private static bool TryParseMetadataJson(string inputJsonContent, out JsonObject inputObj, out string error)
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

	private static string? GetStringPropertyOrNull(JsonObject inputObj, string propertyName)
	{
		if (inputObj.TryGetPropertyValue(propertyName, out var val))
		{
			return val?.ToString();
		}
		return null;
	}

	private static string? GetRawAssetType(string? explicitAssetType, JsonObject inputObj)
	{
		if (!string.IsNullOrWhiteSpace(explicitAssetType))
		{
			return explicitAssetType;
		}

		string? type = GetStringPropertyOrNull(inputObj, "asset_type");
		if (type != null) return type;

		type = GetStringPropertyOrNull(inputObj, "AssetType");
		if (type != null) return type;

		type = GetStringPropertyOrNull(inputObj, "default_asset_type");
		if (type != null) return type;

		return GetStringPropertyOrNull(inputObj, "type");
	}

	private static bool ResolveAssetType(string? explicitAssetType, string ext, JsonObject inputObj, out string error)
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

	private static JsonObject MergeMetadataUpdate(string? existingMeta, JsonObject inputObj)
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

	private static JsonObject MergeMetadataCreate(string? existingMeta, JsonObject inputObj)
	{
		if (string.IsNullOrEmpty(existingMeta)) return inputObj;

		try
		{
			if (JsonNode.Parse(existingMeta) is not JsonObject existingObj) return inputObj;

			CopyPropertyIfNotExists("format", existingObj, inputObj);
			CopyPropertyIfNotExists("is_compressed", existingObj, inputObj);
			CopyPropertyIfNotExists("created_utc", existingObj, inputObj);
		}
		catch { }
		
		return inputObj;
	}

	private static void CopyPropertyIfNotExists(string key, JsonObject source, JsonObject target)
	{
		if (!target.ContainsKey(key) && source.ContainsKey(key))
		{
			target[key] = source[key]?.DeepClone();
		}
	}

	private static int ExecuteMetadataAdd(MetadataOptions options)
	{
		if (!EnsurePathExists(options.Input)) return 1;

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

	private static bool PrepareMetadataAddData(MetadataOptions options)
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

	private static int ProcessSingleMetadataAdd(string inputPath, string jsonContent, bool isUpdate, string? assetType)
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

	private static int ProcessDirectoryMetadataAdd(MetadataOptions options, string jsonContent, bool isUpdate)
	{
		var files = TraverseFiles(options.Input, options.Recursive, file => RealmMetadataHelper.SupportsMetadata(Path.GetExtension(file).ToLowerInvariant())).ToArray();
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

	private static int ExecuteMetadataRemove(MetadataOptions options)
	{
		if (!EnsurePathExists(options.Input)) return 1;

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

		var files = TraverseFiles(options.Input, options.Recursive, file => RealmMetadataHelper.SupportsMetadata(Path.GetExtension(file).ToLowerInvariant())).ToArray();
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

	private static int ExecuteTextureConvert(TextureConvertOptions options)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(options.AssetType))
			{
				if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rtex", options.AssetType, out string canonical, out var validTypes))
				{
					Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for textures. Valid asset_type values for textures (.rtex/.png) are: {string.Join(", ", validTypes)}.");
					return 1;
				}
				options.AssetType = canonical;
			}

			return ProcessTraversedFiles(
				options.Input,
				options.Output,
				options.Recursive,
				options.InPlace,
				ImageFormatConverter.IsImageFile,
				file => Path.GetExtension(file).Equals(".rtex", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".rtex",
				(inputFile, targetFile) => ProcessSingleTextureConvert(inputFile, targetFile, options),
				summaryActionName: "texture conversion");
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error: {ex.Message}");
			return 1;
		}
	}

	private static int ProcessSingleTextureConvert(string inputFile, string targetFile, TextureConvertOptions options)
	{
		string fileExt = Path.GetExtension(inputFile).ToLowerInvariant();

		if (fileExt == ".rtex" && !Path.GetExtension(targetFile).Equals(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			EnsureAssetAgreementAccepted();
		}

		var res = TextureConverter.ConvertTextureFile(
			inputFile,
			targetFile,
			options.AssetType,
			options.Columns,
			options.Rows);

		if (res.Success)
		{
			if (Path.GetExtension(targetFile).Equals(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				RealmMetadataHelper.SyncBlake3Metadata(targetFile);
			}
			Console.WriteLine($"Successfully converted: {inputFile} -> {targetFile}");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
			return 1;
		}
	}

	private static int ExecuteAudioConvert(AudioConvertOptions options)
	{
		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			inPlace: false,
			AudioConverter.IsAudioFile,
			file => Path.GetExtension(file).Equals(".raud", StringComparison.OrdinalIgnoreCase) ? ".ogg" : ".raud",
			(inputFile, targetFile) => ProcessSingleAudioConvert(inputFile, targetFile, options),
			summaryActionName: "audio conversion");
	}

	private static int ProcessSingleAudioConvert(string inputFile, string targetFile, AudioConvertOptions options)
	{
		string fileExt = Path.GetExtension(inputFile).ToLowerInvariant();

		if (fileExt == ".raud" && !Path.GetExtension(targetFile).Equals(".raud", StringComparison.OrdinalIgnoreCase))
		{
			EnsureAssetAgreementAccepted();
		}

		var res = AudioConverter.ConvertAudioFile(
			inputFile,
			targetFile,
			options.AssetType);

		if (res.Success)
		{
			if (Path.GetExtension(targetFile).Equals(".raud", StringComparison.OrdinalIgnoreCase))
			{
				RealmMetadataHelper.SyncBlake3Metadata(targetFile);
			}
			Console.WriteLine($"Successfully converted: {inputFile} -> {targetFile}");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
			return 1;
		}
	}

	private static int ExecuteFbxToRanim(FbxToRanimOptions options)
	{
		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			inPlace: false,
			file => Path.GetExtension(file).Equals(".fbx", StringComparison.OrdinalIgnoreCase),
			_ => ".ranim",
			(inputFile, targetFile) => ProcessSingleFbxToRanim(inputFile, targetFile),
			summaryActionName: "FBX conversion");
	}

	private static int ProcessSingleFbxToRanim(string inputFile, string targetFile)
	{
		var res = MixamoFbxConverter.ConvertFbxFile(inputFile, targetFile);
		if (res.Success)
		{
			if (File.Exists(res.OutputPath) && Path.GetExtension(res.OutputPath).Equals(".ranim", StringComparison.OrdinalIgnoreCase))
			{
				RealmMetadataHelper.SyncBlake3Metadata(res.OutputPath);
			}
			else if (Directory.Exists(targetFile))
			{
				foreach (var ranimFile in Directory.GetFiles(targetFile, "*.ranim"))
				{
					RealmMetadataHelper.SyncBlake3Metadata(ranimFile);
				}
			}
			Console.WriteLine($"Successfully converted: {inputFile} -> {res.OutputPath} ({string.Join(", ", res.ConvertedAnimationNames)})");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
			return 1;
		}
	}

	private static int ExecuteMeshConvert(MeshConvertOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.AssetType))
		{
			if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rmesh", options.AssetType, out string canonical, out var validTypes))
			{
				Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for 3D model. Valid asset_type values for .rmesh/.glb are: {string.Join(", ", validTypes)}.");
				return 1;
			}
			options.AssetType = canonical;
		}

		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			options.InPlace,
			ModelConverter.IsModelFile,
			file => options.InPlace ? Path.GetExtension(file) : (Path.GetExtension(file).Equals(".rmesh", StringComparison.OrdinalIgnoreCase) ? ".glb" : ".rmesh"),
			(inputFile, targetFile) => ProcessSingleMeshConvert(inputFile, targetFile, options),
			summaryActionName: "model conversion");
	}

	private static int ProcessSingleMeshConvert(string inputFile, string targetFile, MeshConvertOptions options)
	{
		string fileExt = Path.GetExtension(inputFile).ToLowerInvariant();
		string targetExt = Path.GetExtension(targetFile).ToLowerInvariant();

		if (fileExt == ".rmesh" && !targetExt.Equals(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			EnsureAssetAgreementAccepted();
		}

		if (targetExt.Equals(".glb", StringComparison.OrdinalIgnoreCase) && fileExt == ".rmesh")
		{
			return ExtractGlbFromRmesh(inputFile, targetFile);
		}
		
		if (targetExt.Equals(".glb", StringComparison.OrdinalIgnoreCase) && fileExt == ".glb")
		{
			return OptimizeGlb(inputFile, targetFile, options);
		}

		return ExecuteModelToRmeshConversion(inputFile, targetFile, options);
	}

	private static int ExtractGlbFromRmesh(string inputFile, string targetFile)
	{
		var res = ModelConverter.ExtractGlbFromRmesh(inputFile, targetFile);
		if (res.Success)
		{
			Console.WriteLine($"Successfully extracted GLB: {inputFile} -> {targetFile}");
			return 0;
		}
		
		Console.Error.WriteLine($"Failed to extract GLB from {inputFile}: {res.ErrorMessage}");
		return 1;
	}

	private static int OptimizeGlb(string inputFile, string targetFile, MeshConvertOptions options)
	{
		try
		{
			byte[] inputGlbBytes = File.ReadAllBytes(inputFile);
			byte[] outputGlbBytes;
			if (!options.Force && GlbManifestUtils.HasOptimizationFlag(inputGlbBytes))
			{
				outputGlbBytes = inputGlbBytes;
			}
			else
			{
				byte[] unoptimized = GlbManifestUtils.StripOptimizationMetadata(inputGlbBytes).UnoptimizedBytes;
				var opt = ModelConverter.GetAutomaticOptimizationOptions(options.AssetType, options.Force);
				var optimizer = new GlbOptimizer();
				var optResult = optimizer.Optimize(unoptimized, opt);
				outputGlbBytes = optResult.Success && optResult.OutputGlbBytes != null ? optResult.OutputGlbBytes : unoptimized;
			}

			string? outDir = Path.GetDirectoryName(targetFile);
			if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

			File.WriteAllBytes(targetFile, outputGlbBytes);
			RealmMetadataHelper.SyncBlake3Metadata(targetFile);
			Console.WriteLine($"Successfully converted model: {inputFile} -> {targetFile} (Size: {outputGlbBytes.Length} bytes)");
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to convert {inputFile}: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteModelToRmeshConversion(string inputFile, string targetFile, MeshConvertOptions options)
	{
		var res = ModelConverter.ConvertToRmesh(
			inputFile,
			targetFile,
			options.AssetType,
			options.Force,
			chromaKey: options.ChromaKey);

		if (res.Success)
		{
			Console.WriteLine($"Successfully converted model: {inputFile} -> {targetFile} (Size: {res.OptimizedSize} bytes, TeamColor: {res.SupportsTeamColor})");
			return 0;
		}
		
		Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
		return 1;
	}

	private static int ExecuteBlake3(Blake3Options options)
	{
		if (!EnsurePathExists(options.Input)) return 1;

		var files = TraverseFiles(options.Input, options.Recursive).ToArray();
		if (files.Length == 0)
		{
			Console.WriteLine($"No files found in: {options.Input}");
			return 0;
		}

		foreach (var file in files)
		{
			byte[] bytes = File.ReadAllBytes(file);
			string hash = options.Raw
				? Blake3.Hasher.Hash(bytes).ToString()
				: RealmMetadataHelper.ComputeBlake3(bytes, file);
			Console.WriteLine($"{hash}  {file}");
		}
		return 0;
	}

	private static string ResolveMeshTargetExtension(string inputPath, string? explicitOutput, bool inPlace)
	{
		if (inPlace)
		{
			return Path.GetExtension(inputPath);
		}
		if (!string.IsNullOrEmpty(explicitOutput))
		{
			string outExt = Path.GetExtension(explicitOutput);
			if (!string.IsNullOrEmpty(outExt))
			{
				return outExt;
			}
		}
		string inExt = Path.GetExtension(inputPath);
		return string.Equals(inExt, ".glb", StringComparison.OrdinalIgnoreCase) ? ".glb" : ".rmesh";
	}

	private static int ExecuteMeshPlayerColor(MeshPlayerColorCliOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.AssetType))
		{
			if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rmesh", options.AssetType, out string canonical, out var validTypes))
			{
				Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for 3D model. Valid asset_type values for .rmesh/.glb are: {string.Join(", ", validTypes)}.");
				return 1;
			}
			options.AssetType = canonical;
		}

		var processorOptions = new Realm.Shared.GlbPlayerColorOptions
		{
			ChromaKey = options.ChromaKey,
			AutoCorrectChromaKey = options.AutoCorrectChromaKey,
			CoreThreshold = options.CoreThreshold,
			FringeThreshold = options.FringeThreshold,
			MinClusterFaces = options.MinClusterFaces,
			DilationRadius = options.DilationRadius,
			CreaseAngleDegrees = options.CreaseAngleDegrees
		};

		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			options.InPlace,
			file => file.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase),
			_ => ".rmesh",
			(inputFile, targetFile) => ProcessSingleMeshPlayerColor(inputFile, targetFile, processorOptions, options.AssetType, options.InPlace),
			customPathResolver: (inputRoot, currentFile) =>
			{
				if (Directory.Exists(inputRoot))
				{
					if (options.InPlace || string.IsNullOrEmpty(options.Output))
					{
						return ResolveMeshPlayerColorOutputPath(currentFile, null, options.InPlace);
					}

					string relativePath = Path.GetRelativePath(inputRoot, currentFile);
					string relativeTarget = Path.ChangeExtension(relativePath, ".rmesh");
					return Path.Combine(options.Output, relativeTarget);
				}

				return ResolveMeshPlayerColorOutputPath(currentFile, options.Output, options.InPlace);
			},
			summaryActionName: "mesh player color processing");
	}

	private static int ProcessSingleMeshPlayerColor(
		string inputPath,
		string outputPath,
		Realm.Shared.GlbPlayerColorOptions processorOptions,
		string? assetType = null,
		bool inPlace = false)
	{
		if (!outputPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			outputPath = Path.ChangeExtension(outputPath, ".rmesh");
		}

		Console.WriteLine($"Processing: {inputPath} -> {outputPath}");

		try
		{
			var (sourceGlbBytes, existingMeta) = ExtractInputBytesAndMetadata(inputPath);

			var (success, processedGlbBytes, errorMessage, maskedFaces, totalFaces, detectedKey) =
				Realm.Shared.GlbPlayerColorProcessor.ProcessBytes(sourceGlbBytes, processorOptions);

			if (!success || processedGlbBytes == null)
			{
				Console.Error.WriteLine($"  Failed player-color processing: {errorMessage}");
				return 1;
			}

			string resolvedChromaKey = detectedKey ?? processorOptions.ChromaKey;
			Console.WriteLine($"  Player-color mask applied (masked faces: {maskedFaces}/{totalFaces}, chroma key: {resolvedChromaKey})");

			EnsureDirectoryForFile(outputPath);

			var (targetAssetType, targetAuthor) = ExtractAssetTypeAndAuthor(existingMeta, assetType);

			return RepackAndSaveRmesh(inputPath, outputPath, processedGlbBytes, targetAssetType, targetAuthor, resolvedChromaKey, existingMeta, inPlace);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"  Failed to process {inputPath}: {ex.Message}");
			return 1;
		}
	}

	private static int RepackAndSaveRmesh(string inputPath, string outputPath, byte[] processedGlbBytes, string? targetAssetType, string? targetAuthor, string resolvedChromaKey, string? existingMeta, bool inPlace)
	{
		var convResult = ModelConverter.ConvertToRmesh(
			processedGlbBytes,
			inputPath,
			targetAssetType,
			force: false,
			author: targetAuthor,
			chromaKey: resolvedChromaKey,
			existingMetadataJson: existingMeta);

		if (!convResult.Success || convResult.OutputBytes == null)
		{
			Console.Error.WriteLine($"  Failed to repack into RMESH: {convResult.ErrorMessage}");
			return 1;
		}

		File.WriteAllBytes(outputPath, convResult.OutputBytes);
		Console.WriteLine($"  Successfully saved RMESH: {outputPath} ({convResult.OptimizedSize} bytes)");

		TryDeleteInPlaceOriginal(inPlace, inputPath, outputPath);

		return 0;
	}

	private static void EnsureDirectoryForFile(string filePath)
	{
		string? outDir = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
	}

	private static void TryDeleteInPlaceOriginal(bool inPlace, string inputPath, string outputPath)
	{
		if (inPlace && !string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(inputPath))
		{
			try { File.Delete(inputPath); } catch { }
		}
	}

	private static (byte[] glbBytes, string? metadata) ExtractInputBytesAndMetadata(string inputPath)
	{
		string inputExt = Path.GetExtension(inputPath).ToLowerInvariant();
		bool isRmeshInput = inputExt == ".rmesh";

		if (isRmeshInput)
		{
			byte[] rmeshBytes = File.ReadAllBytes(inputPath);
			var (meta, glbBytes, _) = RmeshFile.Parse(rmeshBytes);
			return (glbBytes, meta);
		}

		return (File.ReadAllBytes(inputPath), null);
	}

	private static JsonNode? TryParseMeta(string? existingMeta)
	{
		if (string.IsNullOrEmpty(existingMeta)) return null;

		try
		{
			return JsonNode.Parse(existingMeta);
		}
		catch
		{
			return null;
		}
	}

	private static void UpdateAssetTypeAndAuthor(JsonNode node, ref string? targetAssetType, ref string? targetAuthor)
	{
		if (node is not JsonObject) return;
		targetAssetType ??= node["asset_type"]?.ToString();
		targetAssetType ??= node["default_asset_type"]?.ToString();
		targetAuthor ??= node["author"]?.ToString();
	}

	private static (string? assetType, string? author) ExtractAssetTypeAndAuthor(string? existingMeta, string? explicitAssetType)
	{
		string? targetAssetType = string.IsNullOrWhiteSpace(explicitAssetType) ? null : explicitAssetType;
		string? targetAuthor = null;
		
		JsonNode? node = TryParseMeta(existingMeta);
		if (node != null)
		{
			UpdateAssetTypeAndAuthor(node, ref targetAssetType, ref targetAuthor);
		}

		return (targetAssetType, targetAuthor);
	}

	private static string ResolveMeshPlayerColorOutputPath(string inputPath, string? explicitOutput, bool inPlace)
	{
		if (inPlace) return Path.ChangeExtension(inputPath, ".rmesh");
		if (!string.IsNullOrEmpty(explicitOutput))
		{
			if (Directory.Exists(explicitOutput) ||
			    explicitOutput.EndsWith(Path.DirectorySeparatorChar) ||
			    explicitOutput.EndsWith(Path.AltDirectorySeparatorChar))
			{
				string nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
				return Path.Combine(explicitOutput, $"{nameWithoutExt}_masked.rmesh");
			}
			return Path.ChangeExtension(explicitOutput, ".rmesh");
		}
		string dir = Path.GetDirectoryName(inputPath) ?? string.Empty;
		string nameWithoutExtDefault = Path.GetFileNameWithoutExtension(inputPath);
		return Path.Combine(dir, $"{nameWithoutExtDefault}_masked.rmesh");
	}

	private static int ExecuteRigHumanoid(RigHumanoidOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.AssetType))
		{
			if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rmesh", options.AssetType, out string canonical, out var validTypes))
			{
				Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for humanoid rig. Valid asset_type values for .rmesh/.glb are: {string.Join(", ", validTypes)}.");
				return 1;
			}
			options.AssetType = canonical;
		}

		if (!string.IsNullOrWhiteSpace(options.MiaDir))
		{
			Environment.SetEnvironmentVariable("MIA_DIR", options.MiaDir);
		}

		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			options.InPlace,
			file => file.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase),
			file => ResolveMeshTargetExtension(file, options.Output, options.InPlace),
			(inputFile, targetFile) => ProcessSingleRigHumanoid(inputFile, targetFile, options),
			summaryActionName: "humanoid rigging");
	}

	private static int ProcessSingleRigHumanoid(string inputPath, string targetOutput, RigHumanoidOptions options)
	{
		Console.WriteLine($"Processing humanoid rig: {inputPath} -> {targetOutput}");

		string tempGlbOutput = Path.Combine(Path.GetTempPath(), $"realm_rig_out_{Guid.NewGuid():N}.glb");
		string? tempExtractedGlb = null;
		
		try
		{
			bool isRmeshInput = Path.GetExtension(inputPath).Equals(".rmesh", StringComparison.OrdinalIgnoreCase);
			string tempGlbInput = inputPath;
			string? existingMeta = null;
			
			if (isRmeshInput)
			{
				(tempExtractedGlb, existingMeta) = ExtractRigInputBytes(inputPath);
				tempGlbInput = tempExtractedGlb;
			}

			var result = GlbAutoRigger.RigHumanoid(tempGlbInput, tempGlbOutput, new GlbAutoRiggerOptions
			{
				NoFingers = options.NoFingers,
				UseNormals = options.UseNormals,
				WeightPostprocess = options.WeightPostprocess
			});

			if (!result.Success || !File.Exists(tempGlbOutput))
			{
				Console.Error.WriteLine($"Error rigging {inputPath}: {result.ErrorMessage}");
				return 1;
			}

			return ProcessRiggedOutput(inputPath, targetOutput, tempGlbOutput, existingMeta, options);
		}
		finally
		{
			TryDeleteFile(tempExtractedGlb);
			TryDeleteFile(tempGlbOutput);
		}
	}

	private static int ProcessRiggedOutput(string inputPath, string targetOutput, string tempGlbOutput, string? existingMeta, RigHumanoidOptions options)
	{
		byte[] riggedGlb = File.ReadAllBytes(tempGlbOutput);
		EnsureDirectoryForFile(targetOutput);

		bool isRmeshOutput = Path.GetExtension(targetOutput).Equals(".rmesh", StringComparison.OrdinalIgnoreCase);

		if (isRmeshOutput)
		{
			int rmeshResult = SaveRiggedRmesh(riggedGlb, inputPath, targetOutput, options, existingMeta);
			if (rmeshResult != 0) return rmeshResult;
		}
		else
		{
			File.WriteAllBytes(targetOutput, riggedGlb);
			RealmMetadataHelper.SyncBlake3Metadata(targetOutput);
			Console.WriteLine($"Successfully rigged and saved GLB: {targetOutput}");
		}

		TryDeleteInPlaceOriginal(options.InPlace, inputPath, targetOutput);
		return 0;
	}

	private static void TryDeleteFile(string? filePath)
	{
		if (filePath != null && File.Exists(filePath))
		{
			try { File.Delete(filePath); } catch { }
		}
	}

	private static (string tempExtractedGlb, string? existingMeta) ExtractRigInputBytes(string inputPath)
	{
		byte[] rmeshBytes = File.ReadAllBytes(inputPath);
		var (meta, glbBytes, _) = RmeshFile.Parse(rmeshBytes);
		string tempExtractedGlb = Path.Combine(Path.GetTempPath(), $"realm_rig_in_{Guid.NewGuid():N}.glb");
		File.WriteAllBytes(tempExtractedGlb, glbBytes);
		
		return (tempExtractedGlb, meta);
	}

	private static int SaveRiggedRmesh(byte[] riggedGlb, string inputPath, string targetOutput, RigHumanoidOptions options, string? existingMeta)
	{
		var (extractedType, targetAuthor) = ExtractAssetTypeAndAuthor(existingMeta, options.AssetType);
		string targetAssetType = extractedType ?? "Character";

		var convRes = ModelConverter.ConvertToRmesh(
			riggedGlb,
			inputPath,
			targetAssetType,
			force: true,
			author: targetAuthor,
			existingMetadataJson: existingMeta);

		if (!convRes.Success || convRes.OutputBytes == null)
		{
			Console.Error.WriteLine($"Failed to pack rigged model to RMESH: {convRes.ErrorMessage}");
			return 1;
		}

		File.WriteAllBytes(targetOutput, convRes.OutputBytes);
		Console.WriteLine($"Successfully rigged and saved RMESH: {targetOutput}");
		return 0;
	}

	private static int ExecuteKeygen(KeygenOptions options)
	{
		var (privateKeyBase64, publicKeyBase64) = AuthorSignatureHelper.GenerateKeyPair();

		PrintKeygenHeader(publicKeyBase64, privateKeyBase64);

		string outputPath = !string.IsNullOrWhiteSpace(options.Output)
			? options.Output.Trim()
			: AuthorshipKeyHelper.GetDefaultKeyPath();

		string? outDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}
		
		string keyUsername = !string.IsNullOrWhiteSpace(options.Username) ? options.Username.Trim() : string.Empty;
		byte[] rkeyBytes = RkeyFile.Build(keyUsername, publicKeyBase64, privateKeyBase64);
		File.WriteAllBytes(outputPath, rkeyBytes);
		Console.WriteLine($"Saved key container to: {outputPath}");

		if (options.Register || !string.IsNullOrWhiteSpace(options.Username))
		{
			RegisterKeygenUser(options, publicKeyBase64, privateKeyBase64);
		}

		Console.WriteLine();
		Console.WriteLine("To graduate this key pair to an Admin Key:");
		Console.WriteLine("1. Keep your Private Key secret.");
		Console.WriteLine("2. Add the Public Key to Realm.AdminServer/appsettings.json under 'AdminPublicKeys':");
		Console.WriteLine($"   \"AdminPublicKeys\": [\n     \"{publicKeyBase64}\"\n   ]");
		Console.WriteLine("=================================================");
		return 0;
	}

	private static void PrintKeygenHeader(string publicKeyBase64, string privateKeyBase64)
	{
		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Cryptographic Ed25519 Key Pair Generated");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Public Key (Base64):  {publicKeyBase64}");
		Console.WriteLine($"Private Key (Base64): {privateKeyBase64}");
		Console.WriteLine();
	}

	private static void RegisterKeygenUser(KeygenOptions options, string publicKeyBase64, string privateKeyBase64)
	{
		string serverUrl = !string.IsNullOrWhiteSpace(options.Server) ? options.Server : ServersConfigHelper.GetDefaultServerUrl();
		string username = !string.IsNullOrWhiteSpace(options.Username) ? options.Username.Trim() : "Creator_" + Guid.NewGuid().ToString("N")[..6];
		string payload = $"{username}:{publicKeyBase64}";
		string signature = AuthorSignatureHelper.SignMessage(privateKeyBase64, payload);

		Console.WriteLine($"Registering username '{username}' with {serverUrl}...");
		try
		{
			using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			var regPayload = new
			{
				Username = username,
				PublicKey = publicKeyBase64,
				Signature = signature
			};
			var content = new System.Net.Http.StringContent(JsonSerializer.Serialize(regPayload), System.Text.Encoding.UTF8, "application/json");
			var response = httpClient.PostAsync($"{serverUrl.TrimEnd('/')}/api/creators/register", content).GetAwaiter().GetResult();
			string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine($"[Success] Username '{username}' locked to public key on registry server.");
			}
			else
			{
				Console.Error.WriteLine($"[Failed] Registration returned HTTP {(int)response.StatusCode}: {responseText}");
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Error] Could not connect to server to register username: {ex.Message}");
		}
	}

	private static int ExecuteGenerateManifestSchema(GenerateManifestSchemaOptions options)
	{
		string schemaJson = MapManifest.GenerateJsonSchema();
		string outputPath = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : "manifest.schema.json";
		string? outDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}
		File.WriteAllText(outputPath, schemaJson, new System.Text.UTF8Encoding(false));
		Console.WriteLine($"Generated schema: {outputPath}");
		return 0;
	}

	private static int ExecuteGenerateMetadataSchema(GenerateMetadataSchemaOptions options)
	{
		string schemaJson = MapMetadata.GenerateJsonSchema();
		string outputPath = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : "metadata.schema.json";
		string? outDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}
		File.WriteAllText(outputPath, schemaJson, new System.Text.UTF8Encoding(false));
		Console.WriteLine($"Generated schema: {outputPath}");
		return 0;
	}

	private static int ExecuteGenerateTerrainSchema(GenerateTerrainSchemaOptions options)
	{
		string schemaJson = MapSaveData.GenerateJsonSchema();
		string outputPath = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : "terrain.schema.json";
		string? outDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}
		File.WriteAllText(outputPath, schemaJson, new System.Text.UTF8Encoding(false));
		Console.WriteLine($"Generated schema: {outputPath}");
		return 0;
	}

	private static int ExecuteGenerateSchemas(GenerateSchemasOptions options)
	{
		string baseDir = !string.IsNullOrWhiteSpace(options.Output) ? options.Output : Directory.GetCurrentDirectory();
		if (!Directory.Exists(baseDir))
		{
			Directory.CreateDirectory(baseDir);
		}

		ExecuteGenerateManifestSchema(new GenerateManifestSchemaOptions { Output = Path.Combine(baseDir, "manifest.schema.json") });
		ExecuteGenerateMetadataSchema(new GenerateMetadataSchemaOptions { Output = Path.Combine(baseDir, "metadata.schema.json") });
		ExecuteGenerateTerrainSchema(new GenerateTerrainSchemaOptions { Output = Path.Combine(baseDir, "terrain.schema.json") });
		return 0;
	}
}
