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
				CliAssetAgreement.Accepted = true;
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
				(MeshConvertOptions options) => MeshConvertCommands.Execute(options),
				(TextureConvertOptions options) => ExecuteTextureConvert(options),
				(AudioConvertOptions options) => ExecuteAudioConvert(options),
				(FbxToRanimOptions options) => ExecuteFbxToRanim(options),
				(RanimRenderOptions options) => ExecuteRanimRender(options),
				(MetadataOptions options) => MetadataCommands.Execute(options),
				(Blake3Options options) => Blake3Command.Execute(options),
				(MeshPlayerColorCliOptions options) => MeshPlayerColorCommand.Execute(options),
				(RigHumanoidOptions options) => ExecuteRigHumanoid(options),
				(KeygenOptions options) => ExecuteKeygen(options),
				(GenerateManifestSchemaOptions options) => ExecuteGenerateManifestSchema(options),
				(GenerateMetadataSchemaOptions options) => ExecuteGenerateMetadataSchema(options),
				(GenerateTerrainSchemaOptions options) => ExecuteGenerateTerrainSchema(options),
				(GenerateSchemasOptions options) => ExecuteGenerateSchemas(options),
				errors => 1);
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

		var renderOptions = new Shared.Animation.RanimRenderOptions
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

		return CliFileSet.ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			inPlace: false,
			file => Path.GetExtension(file).Equals(".ranim", StringComparison.OrdinalIgnoreCase),
			_ => extension,
			(inputFile, targetFile) => ProcessSingleRanimRender(inputFile, targetFile, renderOptions),
			summaryActionName: "rendering animations");
	}

	private static int ProcessSingleRanimRender(string inputFile, string targetFile, Shared.Animation.RanimRenderOptions renderOptions)
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

			return CliFileSet.ProcessTraversedFiles(
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
			CliAssetAgreement.EnsureAccepted();
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
		return CliFileSet.ProcessTraversedFiles(
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
			CliAssetAgreement.EnsureAccepted();
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
		return CliFileSet.ProcessTraversedFiles(
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

		return CliFileSet.ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			options.InPlace,
			file => file.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase),
			file => MeshConvertCommands.ResolveMeshTargetExtension(file, options.Output, options.InPlace),
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
		RmeshIo.EnsureDirectoryForFile(targetOutput);

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

		RmeshIo.TryDeleteInPlaceOriginal(options.InPlace, inputPath, targetOutput);
		return 0;
	}

	private static void TryDeleteFile(string? filePath)
	{
		if (filePath != null && File.Exists(filePath))
		{
			try
			{
				File.Delete(filePath);
			}
			catch (Exception exception)
			{
				Console.Error.WriteLine($"  Warning: could not delete temporary file {filePath}: {exception.Message}");
			}
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
		var (extractedType, targetAuthor) = RmeshIo.ExtractAssetTypeAndAuthor(existingMeta, options.AssetType);
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

		string keyUsername = !string.IsNullOrWhiteSpace(options.Username) ? options.Username.Trim() : string.Empty;

		if (string.IsNullOrWhiteSpace(options.Output) && keyUsername.Length == 0)
		{
			Console.Error.WriteLine("Error: An authorship key is named after its user, so keygen needs --username (or an explicit --output path).");
			return 1;
		}

		string outputPath = !string.IsNullOrWhiteSpace(options.Output)
			? options.Output.Trim()
			: AuthorshipKeyHelper.GetDefaultAuthorshipKeyPath(null, keyUsername);

		string? outDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}

		byte[] rkeyBytes = RkeyFile.Build(keyUsername, publicKeyBase64, privateKeyBase64);
		File.WriteAllBytes(outputPath, rkeyBytes);
		Console.WriteLine($"Saved key container to: {outputPath}");
		Console.WriteLine("Open the map editor and register the key from the publish dialog to lock this name to your Steam account.");

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

	private static int ExecuteGenerateManifestSchema(GenerateManifestSchemaOptions options)
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

	private static int ExecuteGenerateMetadataSchema(GenerateMetadataSchemaOptions options)
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

	private static int ExecuteGenerateTerrainSchema(GenerateTerrainSchemaOptions options)
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