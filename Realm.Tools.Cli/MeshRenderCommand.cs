using System.Text;
using System.Text.Json;
using Blake3;
using Realm.Shared;
using Realm.Shared.BlenderSetup;
using Realm.Shared.ModelOptimization;

namespace Realm.Tools.Cli;

public static class MeshRenderCommand
{
	private const string CacheFormatVersion = "mesh_render_v1";

	private static readonly string[] KnownViews = { "front", "three_quarter", "side", "back", "left" };

	public static int Execute(MeshRenderOptions options)
	{
		if (!TryResolveMode(options.Mode, out MeshRenderMode mode, out string modeError))
		{
			Console.Error.WriteLine($"Error: {modeError}");
			return 1;
		}

		if (!TryResolveEngine(options.Engine, out MeshRenderEngine engine, out string engineError))
		{
			Console.Error.WriteLine($"Error: {engineError}");
			return 1;
		}

		string[] views = ResolveViews(options.Views, out string viewsError);
		if (views.Length == 0)
		{
			Console.Error.WriteLine($"Error: {viewsError}");
			return 1;
		}

		if (options.Size < 16)
		{
			Console.Error.WriteLine("Error: --size must be at least 16 pixels.");
			return 1;
		}

		if (string.IsNullOrWhiteSpace(options.Background))
		{
			Console.Error.WriteLine("Error: --background may not be empty. Use #RRGGBB or 'transparent'.");
			return 1;
		}

		if (!CliFileSet.EnsurePathExists(options.Input))
		{
			return 1;
		}

		bool readsRmesh = CliFileSet.TraverseFiles(options.Input, options.Recursive, IsModelFile)
			.Any(file => Path.GetExtension(file).Equals(".rmesh", StringComparison.OrdinalIgnoreCase));

		if (readsRmesh)
		{
			CliAssetAgreement.EnsureAccepted();
		}

		var turnaroundOptions = new MeshTurnaroundOptions
		{
			Mode = mode,
			Size = options.Size,
			Views = views,
			Background = options.Background,
			Engine = engine,
			Samples = Math.Clamp(options.Samples, 1, 512),
			ElevationDegrees = options.Elevation,
			DrawLabels = !options.NoLabels
		};

		return CliFileSet.ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			inPlace: false,
			IsModelFile,
			_ => ".png",
			(inputFile, targetPath) => ProcessSingleMeshRender(inputFile, targetPath, options, turnaroundOptions),
			customPathResolver: mode == MeshRenderMode.Views
				? (inputRoot, file) => ResolveViewsTargetDirectory(inputRoot, file, options.Output)
				: null,
			summaryActionName: "turnaround rendering");
	}

	private static bool IsModelFile(string file)
	{
		string extension = Path.GetExtension(file);
		return extension.Equals(".rmesh", StringComparison.OrdinalIgnoreCase)
		       || extension.Equals(".glb", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryResolveMode(string mode, out MeshRenderMode resolved, out string errorMessage)
	{
		switch (mode.Trim().ToLowerInvariant())
		{
			case "sheet":
				resolved = MeshRenderMode.Sheet;
				errorMessage = string.Empty;
				return true;
			case "views":
				resolved = MeshRenderMode.Views;
				errorMessage = string.Empty;
				return true;
			default:
				resolved = MeshRenderMode.Sheet;
				errorMessage = $"Unknown mode '{mode}'. Use 'sheet' or 'views'.";
				return false;
		}
	}

	private static bool TryResolveEngine(string engine, out MeshRenderEngine resolved, out string errorMessage)
	{
		switch (engine.Trim().ToLowerInvariant())
		{
			case "auto":
				resolved = MeshRenderEngine.Auto;
				errorMessage = string.Empty;
				return true;
			case "eevee":
				resolved = MeshRenderEngine.Eevee;
				errorMessage = string.Empty;
				return true;
			case "workbench":
				resolved = MeshRenderEngine.Workbench;
				errorMessage = string.Empty;
				return true;
			default:
				resolved = MeshRenderEngine.Auto;
				errorMessage = $"Unknown engine '{engine}'. Use 'auto', 'eevee' or 'workbench'.";
				return false;
		}
	}

	private static string[] ResolveViews(string viewArgument, out string errorMessage)
	{
		errorMessage = string.Empty;

		string[] requestedViews = viewArgument
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(view => view.ToLowerInvariant())
			.ToArray();

		if (requestedViews.Length == 0)
		{
			errorMessage = "--views must name at least one view.";
			return Array.Empty<string>();
		}

		string? unknownView = requestedViews.FirstOrDefault(view => !KnownViews.Contains(view));
		if (unknownView != null)
		{
			errorMessage = $"Unknown view '{unknownView}'. Known views: {string.Join(", ", KnownViews)}.";
			return Array.Empty<string>();
		}

		return requestedViews.Distinct().ToArray();
	}

	private static string ResolveViewsTargetDirectory(string inputRoot, string filePath, string? outputRoot)
	{
		string stem = Path.GetFileNameWithoutExtension(filePath);
		bool singleFileInput = File.Exists(inputRoot);
		string root = outputRoot
			?? (singleFileInput ? Path.GetDirectoryName(Path.GetFullPath(filePath))! : inputRoot);

		if (singleFileInput)
		{
			return Path.Combine(root, stem);
		}

		string relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(inputRoot, filePath)) ?? string.Empty;
		return string.IsNullOrEmpty(relativeDirectory)
			? Path.Combine(root, stem)
			: Path.Combine(root, relativeDirectory, stem);
	}

	private static int ProcessSingleMeshRender(string inputFile, string targetPath, MeshRenderOptions options, MeshTurnaroundOptions turnaroundOptions)
	{
		byte[]? glbBytes = LoadGlbBytes(inputFile, out string loadError);
		if (glbBytes == null)
		{
			Console.Error.WriteLine($"Failed to render {inputFile}: {loadError}");
			return 1;
		}

		if (options.Despill)
		{
			glbBytes = GlbInMemoryColorPreprocessor.PreprocessGlbInMemory(glbBytes, options.ChromaKey);
		}

		string stem = Path.GetFileNameWithoutExtension(inputFile);
		string cacheKey = ComputeCacheKey(glbBytes, turnaroundOptions, options.Despill, options.ChromaKey);
		string cacheDirectory = ResolveCacheDirectory(options, cacheKey);
		var expectedOutputs = BlenderMeshRenderer.ResolveExpectedOutputPaths(targetPath, stem, turnaroundOptions);

		IReadOnlyList<string> outputs;
		if (!options.NoCache && TryRestoreFromCache(cacheDirectory, expectedOutputs))
		{
			outputs = expectedOutputs;
			Console.WriteLine($"Rendered from cache ({cacheKey[..12]}): {inputFile} -> {FormatOutputList(outputs)}");
		}
		else
		{
			outputs = RenderNow(inputFile, glbBytes, targetPath, stem, turnaroundOptions, options);
			if (outputs.Count == 0)
			{
				return 1;
			}

			if (!options.NoCache)
			{
				StoreInCache(cacheDirectory, outputs);
			}
		}

		WriteRenderIndex(inputFile, targetPath, stem, turnaroundOptions, outputs);
		return 0;
	}

	private static IReadOnlyList<string> RenderNow(
		string inputFile,
		byte[] glbBytes,
		string targetPath,
		string stem,
		MeshTurnaroundOptions turnaroundOptions,
		MeshRenderOptions options)
	{
		string temporaryGlbPath = Path.Combine(Path.GetTempPath(), $"realm_meshrender_{Guid.NewGuid():N}.glb");

		try
		{
			File.WriteAllBytes(temporaryGlbPath, glbBytes);

			var renderResult = BlenderMeshRenderer.Render(
				temporaryGlbPath,
				targetPath,
				stem,
				turnaroundOptions,
				message => Console.WriteLine($"[Blender] {message}"));

			if (!renderResult.Success)
			{
				Console.Error.WriteLine($"Failed to render {inputFile}: {renderResult.ErrorMessage}");
				return Array.Empty<string>();
			}

			Console.WriteLine($"Successfully rendered turnaround: {inputFile} -> {FormatOutputList(renderResult.OutputPaths)}");
			return renderResult.OutputPaths;
		}
		finally
		{
			if (File.Exists(temporaryGlbPath))
			{
				try
				{
					File.Delete(temporaryGlbPath);
				}
				catch (IOException)
				{
				}
			}
		}
	}

	private static byte[]? LoadGlbBytes(string inputFile, out string errorMessage)
	{
		errorMessage = string.Empty;

		try
		{
			byte[] fileBytes = File.ReadAllBytes(inputFile);

			if (Path.GetExtension(inputFile).Equals(".glb", StringComparison.OrdinalIgnoreCase))
			{
				return fileBytes;
			}

			byte[]? glbBytes = RmeshFile.GetGlbBytes(fileBytes);
			if (glbBytes == null || glbBytes.Length == 0)
			{
				errorMessage = "the .rmesh container holds no GLB payload.";
				return null;
			}

			return glbBytes;
		}
		catch (Exception exception)
		{
			errorMessage = exception.Message;
			return null;
		}
	}

	private static string ComputeCacheKey(byte[] glbBytes, MeshTurnaroundOptions turnaroundOptions, bool despill, string chromaKey)
	{
		string settingsSignature = string.Join(
			"|",
			CacheFormatVersion,
			turnaroundOptions.Mode,
			turnaroundOptions.Size,
			string.Join(",", turnaroundOptions.Views),
			turnaroundOptions.Background,
			turnaroundOptions.Engine,
			turnaroundOptions.Samples,
			turnaroundOptions.ElevationDegrees,
			turnaroundOptions.DrawLabels,
			despill,
			chromaKey);

		using var hasher = Hasher.New();
		hasher.Update(glbBytes);
		hasher.Update(Encoding.UTF8.GetBytes(settingsSignature));
		return hasher.Finalize().ToString();
	}

	private static string ResolveCacheDirectory(MeshRenderOptions options, string cacheKey)
	{
		string cacheRoot = !string.IsNullOrWhiteSpace(options.CacheDir)
			? options.CacheDir
			: Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Realm",
				"asset_review",
				"mesh_render");

		return Path.Combine(cacheRoot, cacheKey);
	}

	private static bool TryRestoreFromCache(string cacheDirectory, IReadOnlyList<string> expectedOutputs)
	{
		if (!Directory.Exists(cacheDirectory))
		{
			return false;
		}

		for (int index = 0; index < expectedOutputs.Count; index++)
		{
			if (!File.Exists(Path.Combine(cacheDirectory, $"{index}.png")))
			{
				return false;
			}
		}

		try
		{
			for (int index = 0; index < expectedOutputs.Count; index++)
			{
				string cachedPath = Path.Combine(cacheDirectory, $"{index}.png");
				CliFileSet.EnsureTargetDirectoryExists(expectedOutputs[index]);
				File.Copy(cachedPath, expectedOutputs[index], overwrite: true);
			}

			return true;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static void StoreInCache(string cacheDirectory, IReadOnlyList<string> outputs)
	{
		try
		{
			Directory.CreateDirectory(cacheDirectory);

			for (int index = 0; index < outputs.Count; index++)
			{
				File.Copy(outputs[index], Path.Combine(cacheDirectory, $"{index}.png"), overwrite: true);
			}
		}
		catch (IOException)
		{
		}
	}

	private static void WriteRenderIndex(
		string inputFile,
		string targetPath,
		string stem,
		MeshTurnaroundOptions turnaroundOptions,
		IReadOnlyList<string> outputs)
	{
		bool sheetMode = turnaroundOptions.Mode == MeshRenderMode.Sheet;

		string indexPath = sheetMode
			? Path.ChangeExtension(targetPath, ".json")
			: Path.Combine(targetPath, $"{stem}.json");

		var index = new MeshRenderIndex
		{
			Source = Path.GetFullPath(inputFile),
			Mode = sheetMode ? "sheet" : "views",
			Size = turnaroundOptions.Size,
			Columns = sheetMode ? turnaroundOptions.Views.Length : 1,
			Views = turnaroundOptions.Views.ToList(),
			Sheet = sheetMode ? Path.GetFullPath(outputs[0]) : null,
			Images = sheetMode
				? new List<MeshRenderIndexImage>()
				: turnaroundOptions.Views.Zip(outputs, (view, path) => new MeshRenderIndexImage
				{
					View = view,
					Path = Path.GetFullPath(path)
				}).ToList()
		};

		try
		{
			CliFileSet.EnsureTargetDirectoryExists(indexPath);
			File.WriteAllText(indexPath, JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (IOException exception)
		{
			Console.Error.WriteLine($"Warning: could not write render index {indexPath}: {exception.Message}");
		}
	}

	private static string FormatOutputList(IReadOnlyList<string> outputs)
	{
		return outputs.Count == 1 ? outputs[0] : string.Join(", ", outputs);
	}

	private class MeshRenderIndex
	{
		public string Source { get; set; } = string.Empty;

		public string Mode { get; set; } = string.Empty;

		public int Size { get; set; }

		public int Columns { get; set; }

		public List<string> Views { get; set; } = new();

		public string? Sheet { get; set; }

		public List<MeshRenderIndexImage> Images { get; set; } = new();
	}

	private class MeshRenderIndexImage
	{
		public string View { get; set; } = string.Empty;

		public string Path { get; set; } = string.Empty;
	}
}
