using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Realm.Shared.Services;

namespace Realm.Simulation.Tests;

/// <summary>
///     Finds the folders the melee ruleset is authored in and publishes its script, so a test plays the same map the
///     lobby hands the authority instead of a stand-in written for tests.
/// </summary>
internal static class MeleeTestMaps
{
	private const string MeleeFolderEnvironmentVariable = "REALM_TEST_MAP_MELEE";
	private const string TerrainFolderEnvironmentVariable = "REALM_TEST_MAP_SECLUDED_CREST";
	private const string MeleeFolderName = "MeleeRuleset";
	private const string TerrainFolderName = "ugc-SecludedCrest";
	private const string EntryPointFileName = "WasmEntryPoint.g.cs";

	private static readonly Lazy<string> Melee = new(() => ResolveFolder(MeleeFolderEnvironmentVariable, MeleeFolderName));
	private static readonly Lazy<string> Terrain = new(() => ResolveFolder(TerrainFolderEnvironmentVariable, TerrainFolderName));
	private static readonly Lazy<string> PublishedScript = new(() => PublishScript(Melee.Value));
	private static readonly Lazy<string> Board = new(BuildBoardFolder);
	private static readonly Lazy<string> ObjectiveBoard = new(BuildObjectiveBoardFolder);

	public static string MeleeFolder => Melee.Value;

	public static string TerrainFolder => Terrain.Value;

	public static string ScriptPath => PublishedScript.Value;

	/// <summary>
	///     The flat board the rules tests play on: the ruleset's own metadata on a 128 by 128 terrain that places the four
	///     spawn anchors and the twelve resource nodes the rules read. A ruleset holds no board of its own, so a test that
	///     plays the rules rather than the composed map hands it one here.
	/// </summary>
	public static string BoardFolder => Board.Value;

	/// <summary>
	///     The same flat board with one unit nobody owns standing on it, whose template states a respawn time and a bounty,
	///     kept apart from <see cref="BoardFolder" /> so a test about a neutral monster is the only one that plays with one,
	///     and the other rules tests keep their board clean. It places the creep camp the ruleset itself authors, so the test
	///     reads the bounty and respawn time the shipped content states rather than values chosen for a test.
	/// </summary>
	public static string ObjectiveBoardFolder => ObjectiveBoard.Value;

	private static string BuildBoardFolder()
	{
		string folder = Path.Combine(Path.GetTempPath(), "realm-melee-test-board", Environment.ProcessId.ToString());
		Directory.CreateDirectory(folder);
		File.Copy(Path.Combine(MeleeFolder, "metadata.json"), Path.Combine(folder, "metadata.json"), overwrite: true);
		File.WriteAllText(Path.Combine(folder, "terrain.json"), BoardTerrainJson, new UTF8Encoding(false));
		return folder;
	}

	private static string BuildObjectiveBoardFolder()
	{
		string folder = Path.Combine(Path.GetTempPath(), "realm-melee-test-objective-board", Environment.ProcessId.ToString());
		Directory.CreateDirectory(folder);
		File.Copy(Path.Combine(MeleeFolder, "metadata.json"), Path.Combine(folder, "metadata.json"), overwrite: true);
		File.WriteAllText(Path.Combine(folder, "terrain.json"), ObjectiveTerrainJson, new UTF8Encoding(false));
		return folder;
	}

	private const string ObjectiveTemplateId = "unit/crest_boar";

	private const string ObjectiveUnitsJson = """
		  "Units": [
		    { "TemplateId": "unit/crest_boar", "PosX": 30, "PosY": 0, "PosZ": 30, "RotationY": 0, "Scale": 1, "Player": 0 }
		  ],
""";

	private static readonly string ObjectiveTerrainJson = BoardTerrainJson.Replace("\"Units\": [],", ObjectiveUnitsJson, StringComparison.Ordinal);

	private const string BoardTerrainJson = """
		{
		  "Width": 128,
		  "Depth": 128,
		  "Units": [],
		  "Props": [
		    { "TemplateId": "resource/melee_gold_mine", "PosX": -44, "PosY": 0, "PosZ": -58, "RotationY": 0, "Scale": 1.6 },
		    { "TemplateId": "resource/melee_gold_mine", "PosX": 44, "PosY": 0, "PosZ": -58, "RotationY": 0, "Scale": 1.6 },
		    { "TemplateId": "resource/melee_gold_mine", "PosX": -44, "PosY": 0, "PosZ": 92, "RotationY": 0, "Scale": 1.6 },
		    { "TemplateId": "resource/melee_gold_mine", "PosX": 44, "PosY": 0, "PosZ": 92, "RotationY": 0, "Scale": 1.6 },
		    { "TemplateId": "resource/melee_gold_mine", "PosX": -20, "PosY": 0, "PosZ": 15, "RotationY": 0, "Scale": 1.6 },
		    { "TemplateId": "resource/melee_gold_mine", "PosX": 20, "PosY": 0, "PosZ": 15, "RotationY": 0, "Scale": 1.6 },
		    { "TemplateId": "resource/melee_woodland", "PosX": -58, "PosY": 0, "PosZ": -44, "RotationY": 0, "Scale": 2.4 },
		    { "TemplateId": "resource/melee_woodland", "PosX": 58, "PosY": 0, "PosZ": -44, "RotationY": 0, "Scale": 2.4 },
		    { "TemplateId": "resource/melee_woodland", "PosX": -58, "PosY": 0, "PosZ": 106, "RotationY": 0, "Scale": 2.4 },
		    { "TemplateId": "resource/melee_woodland", "PosX": 58, "PosY": 0, "PosZ": 106, "RotationY": 0, "Scale": 2.4 },
		    { "TemplateId": "resource/melee_woodland", "PosX": 0, "PosY": 0, "PosZ": -40, "RotationY": 0, "Scale": 2.4 },
		    { "TemplateId": "resource/melee_woodland", "PosX": 0, "PosY": 0, "PosZ": 70, "RotationY": 0, "Scale": 2.4 }
		  ],
		  "Decals": [],
		  "Vfx": [],
		  "Coordinates": [
		    { "Name": "seat_1", "MinX": -78, "MinZ": -78, "MaxX": -38, "MaxZ": -38 },
		    { "Name": "seat_2", "MinX": 38, "MinZ": -78, "MaxX": 78, "MaxZ": -38 },
		    { "Name": "seat_3", "MinX": -78, "MinZ": 72, "MaxX": -38, "MaxZ": 112 },
		    { "Name": "seat_4", "MinX": 38, "MinZ": 72, "MaxX": 78, "MaxZ": 112 }
		  ],
		  "SpawnAnchors": [
		    { "Player": 1, "CoordinateName": "seat_1" },
		    { "Player": 2, "CoordinateName": "seat_2" },
		    { "Player": 3, "CoordinateName": "seat_3" },
		    { "Player": 4, "CoordinateName": "seat_4" }
		  ]
		}
		""";

	/// <summary>
	///     Skips a test when a map folder is absent, because a clone of the engine alone cannot hold a map's sources and a
	///     missing folder is a missing fixture rather than a failing rule.
	/// </summary>
	public static void RequireMaps()
	{
		try
		{
			_ = MeleeFolder;
			_ = TerrainFolder;
		}
		catch (DirectoryNotFoundException exception)
		{
			Assert.Ignore(exception.Message);
		}
	}

	private static string ResolveFolder(string environmentVariable, string folderName)
	{
		string? fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
		if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment)) return Path.GetFullPath(fromEnvironment);

		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory != null)
		{
			string candidate = Path.Combine(directory.FullName, folderName);
			if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
			directory = directory.Parent;
		}

		throw new DirectoryNotFoundException($"The map folder '{folderName}' was not found above {AppContext.BaseDirectory}; set {environmentVariable} to the folder to run the melee ruleset tests.");
	}

	/// <summary>
	///     Publishes the map script of a folder and answers the artifact, so a test compiles the folder the lobby would hand
	///     the authority rather than one written for the test.
	/// </summary>
	/// <param name="mapFolder">The map folder holding the script project.</param>
	/// <returns>The published <c>.wasm</c> path.</returns>
	public static string PublishScript(string mapFolder)
	{
		EnsureEntryPoint(mapFolder);
		EnsureApiLibraries(mapFolder);

		string? projectPath = Directory.EnumerateFiles(mapFolder, "*.csproj").FirstOrDefault();
		if (projectPath == null) throw new FileNotFoundException($"A map script project was not found in {mapFolder}.");

		string assemblyName = Path.GetFileNameWithoutExtension(projectPath);
		string mapScriptPath = Path.Combine(mapFolder, "MapScript.cs");
		string outputDirectory = MapFileService.ResolveScriptOutputDirectory(mapFolder);
		string wasmPath = Path.Combine(outputDirectory, $"{assemblyName}.wasm");

		if (File.Exists(wasmPath) && File.GetLastWriteTimeUtc(wasmPath) >= NewestSourceTime(mapFolder, mapScriptPath)) return wasmPath;

		string wasiSdkPath = ResolveWasiSdkPath();
		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = mapFolder,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		startInfo.ArgumentList.Add("publish");
		startInfo.ArgumentList.Add(Path.GetFileName(projectPath));
		startInfo.ArgumentList.Add("-c");
		startInfo.ArgumentList.Add(MapFileService.ReleaseBuildFlavor);
		startInfo.ArgumentList.Add("-r");
		startInfo.ArgumentList.Add("wasi-wasm");
		startInfo.ArgumentList.Add("-o");
		startInfo.ArgumentList.Add(outputDirectory);
		startInfo.ArgumentList.Add($"-p:WASI_SDK_PATH={wasiSdkPath}");
		startInfo.ArgumentList.Add("-nodeReuse:false");
		startInfo.EnvironmentVariables["WASI_SDK_PATH"] = wasiSdkPath;
		startInfo.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";

		var output = new StringBuilder();
		var error = new StringBuilder();

		using Process process = new() { StartInfo = startInfo };
		process.OutputDataReceived += (_, eventArgs) =>
		{
			if (eventArgs.Data != null) output.AppendLine(eventArgs.Data);
		};
		process.ErrorDataReceived += (_, eventArgs) =>
		{
			if (eventArgs.Data != null) error.AppendLine(eventArgs.Data);
		};

		if (!process.Start()) throw new InvalidOperationException($"Failed to start the WASM build of {mapFolder}.");
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();

		if (process.ExitCode != 0 || !File.Exists(wasmPath))
		{
			throw new InvalidOperationException($"The WASM build of {mapFolder} failed (exit code {process.ExitCode}).\n{output}\n{error}");
		}

		return wasmPath;
	}

	/// <summary>
	///     Copies the entry point the engine generates into the workspace, the way opening the folder in the editor
	///     repairs it. A workspace keeps its own copy, so a map compiled straight from a checkout without this step would
	///     export the events of whichever engine version last wrote that copy and silently miss the ones added since.
	/// </summary>
	/// <param name="mapFolder">The map folder to repair.</param>
	private static void EnsureEntryPoint(string mapFolder)
	{
		string? templatePath = FindTemplateEntryPoint();
		if (templatePath == null) return;

		string targetPath = Path.Combine(mapFolder, EntryPointFileName);
		string templateText = File.ReadAllText(templatePath);
		if (File.Exists(targetPath) && string.Equals(File.ReadAllText(targetPath).Replace("\r\n", "\n"), templateText.Replace("\r\n", "\n"), StringComparison.Ordinal)) return;

		if (File.Exists(targetPath))
		{
			FileAttributes attributes = File.GetAttributes(targetPath);
			if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(targetPath, attributes & ~FileAttributes.ReadOnly);
		}

		File.WriteAllText(targetPath, templateText);
	}

	/// <summary>
	///     Refreshes the API assemblies a map workspace compiles against, which is what opening the folder in the editor
	///     does. A checkout whose <c>lib/</c> predates the engine would otherwise compile the map against an older
	///     <c>IGameAPI</c> than the entry point the engine generates, and fail for a reason that has nothing to do with the
	///     map.
	/// </summary>
	/// <param name="mapFolder">The map folder to repair.</param>
	private static void EnsureApiLibraries(string mapFolder)
	{
		string? templateLibrary = FindTemplatePath("lib");
		if (templateLibrary == null) return;

		string workspaceLibrary = Path.Combine(mapFolder, "lib");
		Directory.CreateDirectory(workspaceLibrary);

		string[] libraryFiles =
		{
			"Realm.MapAPI.dll", "Realm.MapAPI.pdb", "Realm.MapAPI.xml",
			"Realm.Ecs.dll", "Realm.Ecs.pdb", "Realm.Ecs.xml",
			"Realm.EditorAPI.dll", "Realm.EditorAPI.pdb", "Realm.EditorAPI.xml"
		};

		foreach (string libraryFile in libraryFiles)
		{
			string sourcePath = Path.Combine(templateLibrary, libraryFile);
			if (!File.Exists(sourcePath)) continue;

			string targetPath = Path.Combine(workspaceLibrary, libraryFile);
			if (File.Exists(targetPath) && File.ReadAllBytes(targetPath).AsSpan().SequenceEqual(File.ReadAllBytes(sourcePath))) continue;

			if (File.Exists(targetPath))
			{
				FileAttributes attributes = File.GetAttributes(targetPath);
				if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(targetPath, attributes & ~FileAttributes.ReadOnly);
			}

			File.Copy(sourcePath, targetPath, overwrite: true);
		}
	}

	private static string? FindTemplatePath(string relativePath)
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory != null)
		{
			string candidate = Path.Combine(directory.FullName, "MapTemplate", relativePath);
			if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
			directory = directory.Parent;
		}

		return null;
	}

	private static string? FindTemplateEntryPoint()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory != null)
		{
			string candidate = Path.Combine(directory.FullName, "MapTemplate", EntryPointFileName);
			if (File.Exists(candidate)) return Path.GetFullPath(candidate);
			directory = directory.Parent;
		}

		return null;
	}

	/// <summary>
	///     Finds the newest input of the map build: the script itself and the engine libraries it compiles against. A
	///     build that predates either one is rebuilt, so a test never plays a script compiled against an older engine.
	/// </summary>
	private static DateTime NewestSourceTime(string mapFolder, string mapScriptPath)
	{
		DateTime newest = File.Exists(mapScriptPath) ? File.GetLastWriteTimeUtc(mapScriptPath) : DateTime.MinValue;
		string entryPointPath = Path.Combine(mapFolder, EntryPointFileName);
		if (File.Exists(entryPointPath))
		{
			DateTime writtenEntryPoint = File.GetLastWriteTimeUtc(entryPointPath);
			if (writtenEntryPoint > newest) newest = writtenEntryPoint;
		}
		foreach (string filePath in Directory.EnumerateFiles(mapFolder, "*.cs", SearchOption.TopDirectoryOnly))
		{
			DateTime written = File.GetLastWriteTimeUtc(filePath);
			if (written > newest) newest = written;
		}

		string libraryFolder = Path.Combine(mapFolder, "lib");
		if (Directory.Exists(libraryFolder))
		{
			foreach (string filePath in Directory.EnumerateFiles(libraryFolder, "*.dll", SearchOption.TopDirectoryOnly))
			{
				DateTime written = File.GetLastWriteTimeUtc(filePath);
				if (written > newest) newest = written;
			}
		}

		return newest;
	}

	private static string ResolveWasiSdkPath()
	{
		string? fromEnvironment = Environment.GetEnvironmentVariable("WASI_SDK_PATH");
		if (IsWasiSdk(fromEnvironment)) return Path.GetFullPath(fromEnvironment!);

		string appDataSdkParent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "Realm", "wasi_sdk");
		if (Directory.Exists(appDataSdkParent))
		{
			foreach (string candidate in Directory.GetDirectories(appDataSdkParent, "wasi-sdk-*").OrderByDescending(path => path))
			{
				if (IsWasiSdk(candidate)) return Path.GetFullPath(candidate);
			}
		}

		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory != null)
		{
			string candidate = Path.Combine(directory.FullName, "wasi_sdk_embedded");
			if (IsWasiSdk(candidate)) return Path.GetFullPath(candidate);
			directory = directory.Parent;
		}

		throw new DirectoryNotFoundException("The WASI SDK was not found, so the melee map script cannot be published. Set WASI_SDK_PATH to a wasi-sdk folder that contains bin/clang.");
	}

	private static bool IsWasiSdk(string? directory)
	{
		if (string.IsNullOrWhiteSpace(directory)) return false;

		return File.Exists(Path.Combine(directory, "bin", "clang.exe")) || File.Exists(Path.Combine(directory, "bin", "clang"));
	}
}
