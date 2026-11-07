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

internal static class RanimCommands
{
	internal static int ExecuteRender(RanimRenderOptions options)
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

	internal static int ProcessSingleRanimRender(string inputFile, string targetFile, Shared.Animation.RanimRenderOptions renderOptions)
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

	internal static int ExecuteFromFbx(FbxToRanimOptions options)
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

	internal static int ProcessSingleFbxToRanim(string inputFile, string targetFile)
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
}
