using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandLine;
using Realm.Shared;
using Realm.Shared.Animation;
using Realm.Shared.Audio;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;

namespace Realm.Tools.Cli;

[Verb("mesh_convert", HelpText = "Convert and optimize 3D models (GLB, RMESH, OBJ, FBX) into Realm .rmesh format (or export .rmesh to .glb).")]
public class MeshConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to 3D model file (.glb, .rmesh, .obj, .fbx) or directory containing assets.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Modify files in-place.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option('f', "force", Required = false, Default = false, HelpText = "Force re-optimization even if already optimized.")]
	public bool Force { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for model: Character, Building, Prop, Item.")]
	public string? AssetType { get; set; }

	[Option("chroma_key", Required = false, HelpText = "Chroma key color in hex (e.g. #FF00FF) or 'auto' to automatically detect the dominant vibrant chroma key.")]
	public string? ChromaKey { get; set; }
}

[Verb("texture_convert", HelpText = "Convert textures between standard image formats and .rtex format.")]
public class TextureConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input image / .rtex file or directory.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for textures: Decal, Icon, Noise, Ribbon, Skybox, Spritesheet, Terrain, vfx_radial, vfx_vertical. If omitted, attempts to read type from image metadata.")]
	public string? AssetType { get; set; }

	[Option("columns", Required = false, HelpText = "Number of grid columns for spritesheets or animated decals (default 4 for spritesheets, 1 for decals).")]
	public int? Columns { get; set; }

	[Option("rows", Required = false, HelpText = "Number of grid rows for spritesheets or animated decals (default 4 for spritesheets, 1 for decals).")]
	public int? Rows { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Write output alongside input file.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

[Verb("audio_convert", HelpText = "Convert audio files (mp3, wav, flac, aac, ogg) to .raud format (or extract .raud to .ogg).")]
public class AudioConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input audio file or directory containing audio files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for audio: Music, SoundEffect.")]
	public string? AssetType { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

[Verb("fbx_to_ranim", HelpText = "Convert Mixamo FBX skeletal animation files to .ranim format.")]
public class FbxToRanimOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .fbx file or directory containing .fbx files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination .ranim file or directory.")]
	public string? Output { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

[Verb("ranim_render", HelpText = "Render .ranim skeletal animation files to animated WebP or PNG spritesheet.")]
public class RanimRenderOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .ranim file or directory.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('f', "format", Required = false, Default = "auto", HelpText = "Output format: auto (default), webp, spritesheet.")]
	public string Format { get; set; } = "auto";

	[Option("fps", Required = false, Default = 12.0f, HelpText = "Target frames per second (default 12).")]
	public float Fps { get; set; } = 12.0f;

	[Option("max-frames", Required = false, HelpText = "Maximum frame count (uses modulus to skip intermediate frames).")]
	public int? MaxFrames { get; set; }

	[Option("size", Required = false, Default = 128, HelpText = "Frame width and height in pixels (default 128).")]
	public int Size { get; set; } = 128;

	[Option("scale", Required = false, Default = 1.0f, HelpText = "Model scale factor (default 1.0).")]
	public float Scale { get; set; } = 1.0f;

	[Option('m', "model", Required = false, HelpText = "Optional path to rigged humanoid .rmesh or .glb model to render instead of skeleton.")]
	public string? Model { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option("no-border", Required = false, Default = false, HelpText = "Disable frame border.")]
	public bool NoBorder { get; set; }

	[Option("no-shadow", Required = false, Default = false, HelpText = "Disable floor shadow.")]
	public bool NoShadow { get; set; }

	[Option('q', "quality", Required = false, Default = 95, HelpText = "Encoding quality for WebP output (1-100, default 95).")]
	public int Quality { get; set; } = 95;

	[Option("lossless", Required = false, Default = false, HelpText = "Use lossless compression for WebP spritesheet output.")]
	public bool Lossless { get; set; }
}

[Verb("metadata", HelpText = "Manage embedded Realm metadata (read, add, remove) in .rmesh, .raud, .rtex, .ranim, .glb, or .ogg files.")]
public class MetadataOptions
{
	[Option('m', "mode", Required = false, Default = "read", HelpText = "Operation mode: read (default), add, update, remove.")]
	public string Mode { get; set; } = "read";

	[Option('i', "input", Required = true, HelpText = "Path to asset file or directory containing asset files.")]
	public string Input { get; set; } = string.Empty;

	[Option('d', "data", Required = false, HelpText = "JSON string or path to JSON file containing metadata to embed (for add/update mode).")]
	public string? Data { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type to embed: Character, Building, Prop, Item, Decal, Icon, Noise, Ribbon, Skybox, Spritesheet, Terrain, vfx_radial, vfx_vertical, Animation, Music, SoundEffect.")]
	public string? AssetType { get; set; }

	[Option('o', "output", Required = false, HelpText = "Output destination file to write extracted JSON (for read mode).")]
	public string? Output { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

[Verb("blake3", HelpText = "Calculate canonical BLAKE3 hash of an asset file or directory (with Realm metadata stripped ephemerally in RAM).")]
public class Blake3Options
{
	[Option('i', "input", Required = true, HelpText = "Path to asset file or directory containing asset files.")]
	public string Input { get; set; } = string.Empty;

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option("raw", Required = false, Default = false, HelpText = "Calculate raw BLAKE3 hash without stripping metadata.")]
	public bool Raw { get; set; }
}

[Verb("mesh_player_color", HelpText = "Extract chroma key color from a 3D model, isolate player-color area via face topology, pack mask into Red channel of ORM texture, and re-optimize into .rmesh.")]
public class MeshPlayerColorCliOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to .rmesh or .glb file or directory containing model files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Overwrite the source file directly.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for model: Character, Building, Prop, Item.")]
	public string? AssetType { get; set; }

	[Option("chroma_key", Required = false, Default = "#FF00FF", HelpText = "Chroma key color in hex (e.g. #FF00FF) or 'auto' to automatically detect the dominant vibrant chroma key.")]
	public string ChromaKey { get; set; } = "#FF00FF";

	[Option("auto_correct_chroma_key", Required = false, Default = true, HelpText = "Auto-correct input chroma key to the closest matching color in the texture (true/false).")]
	public bool AutoCorrectChromaKey { get; set; } = true;

	[Option("core-threshold", Required = false, Default = 0.88f, HelpText = "Chromaticity dot-product threshold for high-confidence core texels (default: 0.88).")]
	public float CoreThreshold { get; set; } = 0.88f;

	[Option("fringe-threshold", Required = false, Default = 0.80f, HelpText = "Chromaticity dot-product threshold for fringe/edge expansion (default: 0.80).")]
	public float FringeThreshold { get; set; } = 0.80f;

	[Option("min-cluster-faces", Required = false, Default = 10, HelpText = "Minimum connected 3D face count to keep a cluster (default: 10).")]
	public int MinClusterFaces { get; set; } = 10;

	[Option("dilation-radius", Required = false, Default = 3, HelpText = "UV gutter dilation radius in pixels (default: 3).")]
	public int DilationRadius { get; set; } = 3;

	[Option("crease-angle", Required = false, Default = GlbMeshSmoother.DefaultCreaseAngleDegrees, HelpText = "Crease angle threshold in degrees for smooth vs flat surface partitioning (default: 60.0).")]
	public float CreaseAngleDegrees { get; set; } = GlbMeshSmoother.DefaultCreaseAngleDegrees;
}

[Verb("rig_humanoid", HelpText = "Auto-rig a humanoid 3D model with a Mixamo skeleton using the Make-It-Animatable pipeline and optimize into .rmesh.")]
public class RigHumanoidOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .rmesh or .glb file or directory containing model files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Overwrite the source file directly.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for model: Character, Building, Prop, Item.")]
	public string? AssetType { get; set; }

	[Option("no-fingers", Required = false, Default = true, HelpText = "Model does not have ten separate fingers (default: true).")]
	public bool NoFingers { get; set; } = true;

	[Option("use-normals", Required = false, Default = false, HelpText = "Use normals to improve skinning when limbs are close together (default: false).")]
	public bool UseNormals { get; set; } = false;

	[Option("weight-postprocess", Required = false, Default = true, HelpText = "Apply empirical post-processing to blend weights (default: true).")]
	public bool WeightPostprocess { get; set; } = true;

	[Option("mia-dir", Required = false, HelpText = "Path to the ComfyUI_Make-It-Animatable directory. Falls back to MIA_DIR env var, then searches common relative paths.")]
	public string? MiaDir { get; set; }
}

[Verb("keygen", HelpText = "Generate a cryptographic Ed25519 author key pair for map attribution, asset signing, and admin verification.")]
public class KeygenOptions
{
	[Option('u', "username", Required = false, HelpText = "Display name / username to associate with this key pair.")]
	public string? Username { get; set; }

	[Option('o', "output", Required = false, HelpText = "Output path to write .rkey file. If omitted, saves to default %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Output { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL to register unique username.")]
	public string? Server { get; set; }

	[Option("register", Required = false, Default = false, HelpText = "Register the generated key pair and username with the official registry server.")]
	public bool Register { get; set; }
}

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
			typeof(KeygenOptions));

		return Parser.Default.ParseArguments<MeshConvertOptions, TextureConvertOptions, AudioConvertOptions, FbxToRanimOptions, RanimRenderOptions, MetadataOptions, Blake3Options, MeshPlayerColorCliOptions, RigHumanoidOptions, KeygenOptions>(sanitizedArgs)
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

		bool isDirectoryInput = Directory.Exists(inputRoot);

		if (isDirectoryInput)
		{
			string relativePath = Path.GetRelativePath(inputRoot, currentFile);
			if (!string.IsNullOrEmpty(outputDestination))
			{
				string relativeTarget = Path.ChangeExtension(relativePath, targetExtension);
				return Path.Combine(outputDestination, relativeTarget);
			}

			return Path.ChangeExtension(currentFile, targetExtension);
		}

		if (!string.IsNullOrEmpty(outputDestination))
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

			return extensionResolver != null
				? Path.ChangeExtension(outputDestination, targetExtension)
				: outputDestination;
		}

		return Path.ChangeExtension(currentFile, targetExtension);
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

			string? outDir = Path.GetDirectoryName(targetPath);
			if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

			return processFile(inputPath, targetPath);
		}

		string fullInputDir = Path.GetFullPath(inputPath);
		var matchingFiles = TraverseFiles(fullInputDir, recursive, fileFilter).ToArray();

		if (matchingFiles.Length == 0)
		{
			Console.WriteLine($"No matching files found in: {inputPath}");
			return 0;
		}

		int successCount = 0;
		int failCount = 0;

		foreach (string file in matchingFiles)
		{
			string targetPath = customPathResolver != null
				? customPathResolver(fullInputDir, file)
				: ResolveTargetOutputPath(fullInputDir, file, outputPath, inPlace, extensionResolver);

			string? outDir = Path.GetDirectoryName(targetPath);
			if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

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

		if (!string.IsNullOrEmpty(summaryActionName))
		{
			Console.WriteLine($"Finished {summaryActionName}. {successCount} succeeded, {failCount} failed.");
		}

		return failCount > 0 ? 1 : 0;
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

	private static int ExecuteMetadata(MetadataOptions options)
	{
		string mode = options.Mode?.ToLowerInvariant() ?? "read";

		switch (mode)
		{
			case "add":
			case "update":
			case "set":
			case "write":
			case "embed":
				return ExecuteMetadataAdd(options);

			case "remove":
			case "delete":
			case "clear":
			case "strip":
				return ExecuteMetadataRemove(options);

			case "blake3":
			case "hash":
				return ExecuteBlake3(new Blake3Options { Input = options.Input, Recursive = options.Recursive });

			case "read":
			case "get":
			case "extract":
			case "show":
			default:
				return ExecuteMetadataRead(options);
		}
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
			var parsedNode = JsonNode.Parse(inputJsonContent);
			if (parsedNode is not JsonObject inputObj)
			{
				error = "Metadata must be a valid JSON object.";
				return false;
			}

			string? rawAssetType = !string.IsNullOrWhiteSpace(explicitAssetType)
				? explicitAssetType
				: inputObj["asset_type"]?.ToString()
					?? inputObj["AssetType"]?.ToString()
					?? inputObj["default_asset_type"]?.ToString()
					?? inputObj["type"]?.ToString();

			if (!string.IsNullOrEmpty(rawAssetType))
			{
				if (!RealmMetadataHelper.IsValidAssetTypeForExtension(ext, rawAssetType, out string canonical, out var validTypes))
				{
					error = $"Invalid asset_type '{rawAssetType}' for format '{ext}'. Valid asset_type values for {ext} are: {string.Join(", ", validTypes)}.";
					return false;
				}

				inputObj["asset_type"] = canonical;
			}

			string? existingMeta = RealmMetadataHelper.ExtractMetadata(targetPath);
			JsonObject finalObj;
			if (isUpdate)
			{
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
			}
			else
			{
				finalObj = inputObj;

				if (!string.IsNullOrEmpty(existingMeta))
				{
					try
					{
						var existingObj = JsonNode.Parse(existingMeta) as JsonObject;
						if (existingObj != null)
						{
							if (!finalObj.ContainsKey("format") && existingObj.ContainsKey("format"))
							{
								finalObj["format"] = existingObj["format"]?.DeepClone();
							}
							if (!finalObj.ContainsKey("is_compressed") && existingObj.ContainsKey("is_compressed"))
							{
								finalObj["is_compressed"] = existingObj["is_compressed"]?.DeepClone();
							}
							if (!finalObj.ContainsKey("created_utc") && existingObj.ContainsKey("created_utc"))
							{
								finalObj["created_utc"] = existingObj["created_utc"]?.DeepClone();
							}
						}
					}
					catch { }
				}
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

	private static int ExecuteMetadataAdd(MetadataOptions options)
	{
		if (!EnsurePathExists(options.Input)) return 1;

		if (string.IsNullOrEmpty(options.Data))
		{
			var jsonNode = new JsonObject();
			if (!string.IsNullOrWhiteSpace(options.AssetType))
			{
				jsonNode["asset_type"] = options.AssetType;
			}

			if (jsonNode.Count > 0)
			{
				options.Data = jsonNode.ToJsonString();
			}
			else
			{
				Console.Error.WriteLine("Error: --data (-d) or --type (-t) option is required for add mode.");
				return 1;
			}
		}

		string jsonContent = options.Data;
		if (File.Exists(options.Data))
		{
			jsonContent = File.ReadAllText(options.Data);
		}

		string mode = options.Mode?.ToLowerInvariant() ?? "add";
		bool isUpdate = mode is "update" or "set";

		if (File.Exists(options.Input))
		{
			string processedJson = jsonContent;
			if (!PrepareMetadataJsonForFile(options.Input, ref processedJson, isUpdate, options.AssetType, out string error))
			{
				Console.Error.WriteLine($"Error: {error}");
				return 1;
			}

			bool success = RealmMetadataHelper.AddMetadata(options.Input, processedJson);
			if (success)
			{
				Console.WriteLine($"Successfully added metadata to: {options.Input}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to add metadata to: {options.Input}");
				return 1;
			}
		}

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
			var res = ModelConverter.ExtractGlbFromRmesh(inputFile, targetFile);
			if (res.Success)
			{
				Console.WriteLine($"Successfully extracted GLB: {inputFile} -> {targetFile}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to extract GLB from {inputFile}: {res.ErrorMessage}");
				return 1;
			}
		}
		else if (targetExt.Equals(".glb", StringComparison.OrdinalIgnoreCase) && fileExt == ".glb")
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
		else
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
			else
			{
				Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
				return 1;
			}
		}
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

		string inputExt = Path.GetExtension(inputPath).ToLowerInvariant();
		bool isRmeshInput = inputExt == ".rmesh";

		string? existingMeta = null;

		try
		{
			byte[] sourceGlbBytes;
			if (isRmeshInput)
			{
				byte[] rmeshBytes = File.ReadAllBytes(inputPath);
				var (meta, glbBytes, _) = RmeshFile.Parse(rmeshBytes);
				existingMeta = meta;
				sourceGlbBytes = glbBytes;
			}
			else
			{
				sourceGlbBytes = File.ReadAllBytes(inputPath);
			}

			var (success, processedGlbBytes, errorMessage, maskedFaces, totalFaces, detectedKey) =
				Realm.Shared.GlbPlayerColorProcessor.ProcessBytes(sourceGlbBytes, processorOptions);

			if (!success || processedGlbBytes == null)
			{
				Console.Error.WriteLine($"  Failed player-color processing: {errorMessage}");
				return 1;
			}

			string resolvedChromaKey = detectedKey ?? processorOptions.ChromaKey;
			Console.WriteLine($"  Player-color mask applied (masked faces: {maskedFaces}/{totalFaces}, chroma key: {resolvedChromaKey})");

			string? outDir = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

			string? targetAssetType = !string.IsNullOrWhiteSpace(assetType)
				? assetType
				: null;
			string? targetAuthor = null;
			if (!string.IsNullOrEmpty(existingMeta))
			{
				try
				{
					var node = JsonNode.Parse(existingMeta);
					targetAssetType ??= node?["asset_type"]?.ToString() ?? node?["default_asset_type"]?.ToString();
					targetAuthor = node?["author"]?.ToString();
				}
				catch { }
			}

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

			if (inPlace && !string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(inputPath))
			{
				try { File.Delete(inputPath); } catch { }
			}

			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"  Failed to process {inputPath}: {ex.Message}");
			return 1;
		}
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

		string inputExt = Path.GetExtension(inputPath).ToLowerInvariant();
		string outputExt = Path.GetExtension(targetOutput).ToLowerInvariant();
		bool isRmeshInput = inputExt == ".rmesh";
		bool isRmeshOutput = outputExt == ".rmesh";

		string tempGlbInput = inputPath;
		string? tempExtractedGlb = null;
		string? tempGlbOutput = null;
		string? existingMeta = null;

		try
		{
			if (isRmeshInput)
			{
				byte[] rmeshBytes = File.ReadAllBytes(inputPath);
				var (meta, glbBytes, _) = RmeshFile.Parse(rmeshBytes);
				existingMeta = meta;
				tempExtractedGlb = Path.Combine(Path.GetTempPath(), $"realm_rig_in_{Guid.NewGuid():N}.glb");
				File.WriteAllBytes(tempExtractedGlb, glbBytes);
				tempGlbInput = tempExtractedGlb;
			}

			tempGlbOutput = Path.Combine(Path.GetTempPath(), $"realm_rig_out_{Guid.NewGuid():N}.glb");

			var result = GlbAutoRigger.RigHumanoid(
				tempGlbInput,
				tempGlbOutput,
				new GlbAutoRiggerOptions
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

			byte[] riggedGlb = File.ReadAllBytes(tempGlbOutput);
			string? outDir = Path.GetDirectoryName(targetOutput);
			if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

			if (isRmeshOutput)
			{
				string? targetAssetType = !string.IsNullOrWhiteSpace(options.AssetType)
					? options.AssetType
					: null;
				string? targetAuthor = null;
				if (!string.IsNullOrEmpty(existingMeta))
				{
					try
					{
						var node = JsonNode.Parse(existingMeta);
						targetAssetType ??= node?["asset_type"]?.ToString() ?? node?["default_asset_type"]?.ToString();
						targetAuthor = node?["author"]?.ToString();
					}
					catch { }
				}
				targetAssetType ??= "Character";

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
			}
			else
			{
				File.WriteAllBytes(targetOutput, riggedGlb);
				RealmMetadataHelper.SyncBlake3Metadata(targetOutput);
				Console.WriteLine($"Successfully rigged and saved GLB: {targetOutput}");
			}

			if (options.InPlace && !string.Equals(inputPath, targetOutput, StringComparison.OrdinalIgnoreCase) && File.Exists(inputPath))
			{
				try { File.Delete(inputPath); } catch { }
			}

			return 0;
		}
		finally
		{
			if (tempExtractedGlb != null && File.Exists(tempExtractedGlb)) try { File.Delete(tempExtractedGlb); } catch { }
			if (tempGlbOutput != null && File.Exists(tempGlbOutput)) try { File.Delete(tempGlbOutput); } catch { }
		}
	}

	private static int ExecuteKeygen(KeygenOptions options)
	{
		var (privateKeyBase64, publicKeyBase64) = AuthorSignatureHelper.GenerateKeyPair();

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Cryptographic Ed25519 Key Pair Generated");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Public Key (Base64):  {publicKeyBase64}");
		Console.WriteLine($"Private Key (Base64): {privateKeyBase64}");
		Console.WriteLine();

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

		Console.WriteLine();
		Console.WriteLine("To graduate this key pair to an Admin Key:");
		Console.WriteLine("1. Keep your Private Key secret.");
		Console.WriteLine("2. Add the Public Key to Realm.AdminServer/appsettings.json under 'AdminPublicKeys':");
		Console.WriteLine($"   \"AdminPublicKeys\": [\n     \"{publicKeyBase64}\"\n   ]");
		Console.WriteLine("=================================================");
		return 0;
	}
}
