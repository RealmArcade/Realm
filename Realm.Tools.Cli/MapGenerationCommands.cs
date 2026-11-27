using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Realm.MapGeneration;

namespace Realm.Tools.Cli;

public static class MapGenerationCommands
{
	private static readonly string[] Commands = { "render", "reconstruct", "compare", "benchmark", "inspect" };

	public static bool IsMapGenerationInvocation(string[] arguments)
	{
		return arguments.Length > 0 && string.Equals(arguments[0], "mapgen", StringComparison.OrdinalIgnoreCase);
	}

	public static int ExecuteFromArguments(string[] arguments)
	{
		if (arguments.Length < 2 || !Commands.Contains(arguments[1].ToLowerInvariant()))
		{
			Console.Error.WriteLine("Usage: mapgen <render|reconstruct|compare|benchmark|inspect> <input> [-o output] [-w width] [-d depth] [-r resolution] [-t template] [-s seed] [--maps a,b] [--map-directory dir] [--report file] [--use-thumbnails]");
			return 1;
		}

		var options = new MapGenerationOptions { Command = arguments[1] };
		var positional = new List<string>();
		for (int index = 2; index < arguments.Length; index++)
		{
			string argument = arguments[index];
			if (argument.StartsWith("-", StringComparison.Ordinal))
			{
				if (index + 1 >= arguments.Length)
				{
					if (string.Equals(argument, "--use-thumbnails", StringComparison.OrdinalIgnoreCase))
					{
						options.UseThumbnails = true;
						continue;
					}

					Console.Error.WriteLine($"Option '{argument}' needs a value.");
					return 1;
				}

				string value = arguments[++index];
				if (!TryApplyOption(options, argument, value))
				{
					Console.Error.WriteLine($"Unknown option '{argument}'.");
					return 1;
				}

				continue;
			}

			positional.Add(argument);
		}

		if (positional.Count > 0)
		{
			options.Input = positional[0];
		}

		return Execute(options);
	}

	private static bool TryApplyOption(MapGenerationOptions options, string argument, string value)
	{
		switch (argument.ToLowerInvariant())
		{
			case "-o":
			case "--output":
				options.Output = value;
				return true;
			case "-w":
			case "--width":
				return TryParseInteger(value, parsed => options.Width = parsed);
			case "-d":
			case "--depth":
				return TryParseInteger(value, parsed => options.Depth = parsed);
			case "-r":
			case "--resolution":
				return TryParseInteger(value, parsed => options.Resolution = parsed);
			case "-t":
			case "--template":
				options.Template = value;
				return true;
			case "-s":
			case "--seed":
				return TryParseInteger(value, parsed => options.Seed = parsed);
			case "-m":
			case "--maps":
				options.Maps = value;
				return true;
			case "--map-directory":
				options.MapDirectory = value;
				return true;
			case "--report":
				options.Report = value;
				return true;
			default:
				return false;
		}
	}

	private static bool TryParseInteger(string value, Action<int> assign)
	{
		if (!int.TryParse(value, out int parsed))
		{
			return false;
		}

		assign(parsed);
		return true;
	}

	public static int Execute(MapGenerationOptions options)
	{
		try
		{
						switch (options.Command.ToLowerInvariant())
			{
				case "render":
					return ExecuteRender(options);
				case "inspect":
					return ExecuteInspect(options);
				case "reconstruct":
					return ExecuteReconstruct(options);
				case "compare":
					return ExecuteCompare(options);
				case "benchmark":
					return ExecuteBenchmark(options);
				default:
					Console.Error.WriteLine($"Unknown mapgen command '{options.Command}'. Expected render, reconstruct, compare, benchmark or inspect.");
					return 1;
			}
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception.Message);
			return 1;
		}
	}

	private static int ExecuteRender(MapGenerationOptions options)
	{
		string folder = options.Input;
		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
		{
			Console.Error.WriteLine($"The map folder '{folder}' does not exist.");
			return 1;
		}

		string output = options.Output ?? Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) + "_minimap.png");
		var renderOptions = new MinimapRenderOptions { Resolution = options.Resolution };
		MinimapRenderer.SaveFolder(folder, output, renderOptions);
		Console.WriteLine($"Rendered minimap to {output}");
		return 0;
	}

	private static int ExecuteReconstruct(MapGenerationOptions options)
	{
		string imagePath = options.Input;
		if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
		{
			Console.Error.WriteLine($"The minimap image '{imagePath}' does not exist.");
			return 1;
		}

		string templateFolder = options.Template ?? Path.Combine(Directory.GetCurrentDirectory(), "MapTemplate");
		if (!Directory.Exists(templateFolder))
		{
			Console.Error.WriteLine($"The template map folder '{templateFolder}' does not exist.");
			return 1;
		}

		string outputFolder = options.Output ?? Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(imagePath));
		ReconstructedTerrain terrain = MapGenerationWorkflow.ReconstructFromImage(imagePath, templateFolder, options.Width, options.Depth, options.Seed);
		string minimapPath = MapGenerationWorkflow.WriteGeneratedFolder(terrain, templateFolder, outputFolder, Path.GetFileNameWithoutExtension(imagePath));
		int waterCount = 0;
		for (int index = 0; index < terrain.WaterModes.Length; index++)
		{
			if (terrain.WaterModes[index] > 0) waterCount++;
		}

		Console.WriteLine($"Reconstructed {terrain.Width}x{terrain.Depth}: {terrain.Props.Count} props, {waterCount} water cells -> {outputFolder}");
		Console.WriteLine($"Generated minimap: {minimapPath}");
		return 0;
	}

	private static int ExecuteCompare(MapGenerationOptions options)
	{
		string originalFolder = options.Input;
		string generatedFolder = options.Output ?? string.Empty;
		if (!Directory.Exists(originalFolder) || !Directory.Exists(generatedFolder))
		{
			Console.Error.WriteLine("compare needs an original map folder and a generated map folder passed with -o.");
			return 1;
		}

		string minimapPath = MapGenerationWorkflow.ResolveMinimap(originalFolder, options.UseThumbnails, options.Resolution, Path.GetTempPath());
		MinimapImage inputImage = MinimapImage.FromFile(minimapPath);
		MapAccuracyReport report = MapGenerationWorkflow.CompareFolders(originalFolder, generatedFolder, Path.GetFileName(minimapPath), inputImage);
		Console.WriteLine(report.ToString());
		foreach (KeyValuePair<PropCategory, PropCategoryScore> pair in report.PropByCategory)
		{
			Console.WriteLine($"  {pair.Key}: P={pair.Value.Precision:P1} R={pair.Value.Recall:P1} F1={pair.Value.F1:P1} ({pair.Value.GeneratedCount}/{pair.Value.OriginalCount})");
		}

		foreach (KeyValuePair<TerrainSurfaceKind, float> pair in report.TextureKindAgreement)
		{
			Console.WriteLine($"  texture {pair.Key}: {pair.Value:P1}");
		}

		return 0;
	}

	private static int ExecuteBenchmark(MapGenerationOptions options)
	{
		string mapDirectory = options.MapDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "..");
		string templateFolder = options.Template ?? Path.Combine(Directory.GetCurrentDirectory(), "MapTemplate");
		string outputDirectory = options.Output ?? Path.Combine(Path.GetTempPath(), "RealmMapGenerationBenchmark");
		Directory.CreateDirectory(outputDirectory);

		string[] names = (options.Maps ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (names.Length == 0)
		{
			Console.Error.WriteLine("benchmark needs --maps with a comma separated list of map folder names.");
			return 1;
		}

		var reports = new List<MapAccuracyReport>();
		foreach (string name in names)
		{
			string originalFolder = Path.Combine(mapDirectory, name);
			if (!Directory.Exists(originalFolder))
			{
				Console.Error.WriteLine($"Skipping '{name}': folder not found at {originalFolder}");
				continue;
			}

			MapFolderTerrain original = MapFolderTerrainReader.Read(originalFolder);
			string minimapPath = MapGenerationWorkflow.ResolveMinimap(originalFolder, options.UseThumbnails, options.Resolution, outputDirectory);
			MinimapImage inputImage = MinimapImage.FromFile(minimapPath);
			ReconstructedTerrain terrain = MapGenerationWorkflow.ReconstructFromImage(minimapPath, templateFolder, original.Width, original.Depth, options.Seed);
			string generatedFolder = Path.Combine(outputDirectory, name);
			if (Directory.Exists(generatedFolder))
			{
				Directory.Delete(generatedFolder, true);
			}

			MapGenerationWorkflow.WriteGeneratedFolder(terrain, templateFolder, generatedFolder, name);
			MapAccuracyReport report = MapGenerationWorkflow.CompareFolders(originalFolder, generatedFolder, Path.GetFileName(minimapPath), inputImage);
			reports.Add(report);
			Console.WriteLine(report.ToString());
		}

		MapAccuracyReport aggregate = MapGenerationWorkflow.Aggregate(reports);
		Console.WriteLine("AGGREGATE " + aggregate.ToString());
		string reportPath = options.Report ?? Path.Combine(outputDirectory, "report.json");
		MapGenerationWorkflow.WriteReport(reportPath, reports, aggregate);
		Console.WriteLine($"Report written to {reportPath}");
		return 0;
	}

	private static int ExecuteInspect(MapGenerationOptions options)
	{
		string folder = options.Input;
		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
		{
			Console.Error.WriteLine($"The map folder '{folder}' does not exist.");
			return 1;
		}

		MapFolderTerrain terrain = MapFolderTerrainReader.Read(folder);
		float minimumHeight = float.MaxValue;
		float maximumHeight = float.MinValue;
		for (int index = 0; index < terrain.VertexHeights.Length; index++)
		{
			minimumHeight = MathF.Min(minimumHeight, terrain.VertexHeights[index]);
			maximumHeight = MathF.Max(maximumHeight, terrain.VertexHeights[index]);
		}

		int wetCells = 0;
		for (int index = 0; index < terrain.WaterModes.Length; index++)
		{
			if (terrain.WaterModes[index] > 0)
			{
				wetCells++;
			}
		}

		Console.WriteLine($"{Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}: {terrain.Width}x{terrain.Depth}, heights {minimumHeight:F2}..{maximumHeight:F2}, water {wetCells}/{terrain.Width * terrain.Depth} cells, props {terrain.Props.Count}, textures {terrain.Textures.Count}");
		PropCatalog catalog = MapFolderTerrainReader.ReadPropCatalog(folder);
		Console.WriteLine($"  catalog: {catalog.Entries.Count} templates, trees {catalog.CountForCategory(PropCategory.Tree)}, rocks {catalog.CountForCategory(PropCategory.Rock)}, structures {catalog.CountForCategory(PropCategory.Structure)}, nature {catalog.CountForCategory(PropCategory.Nature)}");

		var categories = new int[Enum.GetValues<PropCategory>().Length];
		for (int index = 0; index < terrain.Props.Count; index++)
		{
			categories[(int)terrain.Props[index].Category]++;
		}

		Console.WriteLine($"  placed by category: trees {categories[(int)PropCategory.Tree]}, rocks {categories[(int)PropCategory.Rock]}, structures {categories[(int)PropCategory.Structure]}, nature {categories[(int)PropCategory.Nature]}, other {categories[(int)PropCategory.Other]}");
		return 0;
	}
}
