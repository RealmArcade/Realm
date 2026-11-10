using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Realm.Client.UI;
using Realm.Client.Utils;
using Environment = System.Environment;
using Realm.Client.Services;
namespace Realm.Client.Tests;

/// <summary>resizing the ux images through the scene tree.</summary>
public partial class UxSceneTests
	{
	private static readonly string[] StyleBoxThemeNames = { "panel", "normal", "hover", "pressed", "focus", "disabled" };

	private static readonly string[] UxImageExtensions = { ".webp", ".jpg", ".jpeg", ".png", ".svg" };

	private struct ImageRenderUsage
	{
		public string ResourcePath;
		public int Width;
		public int Height;
		public float Area;
	}

	[TestCase]
	public async Task ResizeUxImages()
	{
		TrimmedTextureLoader.LogUsedRect = true;

		string? configuredOutputDirectory = Environment.GetEnvironmentVariable("REALM_RESIZED_UX_OUTPUT");
		bool writeInPlace = string.IsNullOrWhiteSpace(configuredOutputDirectory);
		string outputDirectory = configuredOutputDirectory ?? string.Empty;
		string stagingDirectory = Path.Combine(Path.GetTempPath(), "Realm_Resized_UX_Images");
		Directory.CreateDirectory(stagingDirectory);

		if (!writeInPlace)
		{
			Directory.CreateDirectory(outputDirectory);
		}

		string reportPath = Path.Combine(stagingDirectory, "UX_Image_Resize_Report.txt");

		var usages = new Dictionary<string, ImageRenderUsage>(StringComparer.OrdinalIgnoreCase);
		var nineSliceFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		ISceneRunner runner = ISceneRunner.Load("res://GameHost.tscn");
		await runner.AwaitMillis(1000);

		Node sceneRoot = runner.Scene() ?? throw new InvalidOperationException("The running scene was not available to measure UI textures.");
		Node probeParent = sceneRoot.GetParent() ?? sceneRoot.GetTree().Root;

		var probeRoot = new Control();
		probeRoot.Name = "UxImageResizeProbeRoot";
		probeRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		probeRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
		probeParent.AddChild(probeRoot);
		await runner.AwaitMillis(250);

		GameScreen[] screens = new GameScreen[]
		{
			GameScreen.MainMenu,
			GameScreen.LobbyBrowser,
			GameScreen.LobbyCreate,
			GameScreen.LobbyRoom,
			GameScreen.Settings,
			GameScreen.MapDiscovery,
			GameScreen.MapDetails,
			GameScreen.MapEditorHUD,
			GameScreen.ReplayList,
			GameScreen.GameOver
		};

		foreach (GameScreen screen in screens)
		{
			if (screen == GameScreen.MapDetails)
			{
				MapData[] dummyMaps = GetDummyMaps();
				MapData mapData = (dummyMaps != null && dummyMaps.Length > 0) ? dummyMaps[0] : new MapData { Title = "Test Map" };
				GameContext.UserInterface?.TransitionToMapDetails(mapData);
			}
			else
			{
				GameContext.UserInterface?.TransitionTo(screen);
			}

			await runner.AwaitMillis(750);
			CollectTextureUsages(sceneRoot, usages, nineSliceFileNames);
		}

		List<string> sceneResourcePaths = FindAllSceneResourcePaths();
		var sceneReportLines = new List<string>();

		foreach (string sceneResourcePath in sceneResourcePaths)
		{
			if (sceneResourcePath == "res://GameHost.tscn" || sceneResourcePath == "res://UI/UIManager.tscn")
			{
				CollectTextureUsages(sceneRoot, usages, nineSliceFileNames);
				sceneReportLines.Add($"{sceneResourcePath}\tmeasured-from-live-tree");
				continue;
			}

			Node? instance = null;
			try
			{
				PackedScene packedScene = GD.Load<PackedScene>(sceneResourcePath);
				if (packedScene == null)
				{
					sceneReportLines.Add($"{sceneResourcePath}\tload-failed");
					continue;
				}

				instance = packedScene.Instantiate();
			}
			catch (Exception exception)
			{
				sceneReportLines.Add($"{sceneResourcePath}\tinstantiate-failed: {exception.Message}");
				continue;
			}

			if (instance == null)
			{
				sceneReportLines.Add($"{sceneResourcePath}\tinstantiate-null");
				continue;
			}

			instance.Name = "UxImageResizeProbe_" + Path.GetFileNameWithoutExtension(sceneResourcePath);
			probeRoot.AddChild(instance);
			await runner.AwaitMillis(400);
			CollectTextureUsages(instance, usages, nineSliceFileNames);
			probeRoot.RemoveChild(instance);
			instance.QueueFree();
			await runner.AwaitMillis(50);
			sceneReportLines.Add($"{sceneResourcePath}\tmeasured");
		}

		var groupedRecords = new Dictionary<string, TrimmedTextureLoadRecord>(StringComparer.OrdinalIgnoreCase);

		foreach (TrimmedTextureLoadRecord record in TrimmedTextureLoader.LoadRecords)
		{
			if (!IsUxImagePath(record.ResourcePath))
			{
				continue;
			}

			string groupFileName = Path.GetFileName(record.ResourcePath);
			if (groupedRecords.TryGetValue(groupFileName, out TrimmedTextureLoadRecord existingRecord) && existingRecord.ResultWidth * existingRecord.ResultHeight >= record.ResultWidth * record.ResultHeight)
			{
				continue;
			}

			groupedRecords[groupFileName] = record;
		}

		var uniqueRecords = new List<TrimmedTextureLoadRecord>(groupedRecords.Values);

		uniqueRecords.Sort((left, right) => string.Compare(left.ResourcePath, right.ResourcePath, StringComparison.OrdinalIgnoreCase));

		string cwebpPath = FindCwebpExecutable();
		string encoder = string.IsNullOrEmpty(cwebpPath) ? "Godot-Image.SaveWebp" : cwebpPath;

		var imageReportLines = new List<string>();
		int resizedCount = 0;
		int skippedCount = 0;
		int stagedIndex = 0;
		long sourceBytes = 0;
		long outputBytes = 0;

		foreach (TrimmedTextureLoadRecord record in uniqueRecords)
		{
			string fileName = Path.GetFileName(record.ResourcePath);
			string extension = Path.GetExtension(record.ResourcePath).ToLowerInvariant();
			string globalPath = ResolveGlobalPath(record.ResourcePath);

			if (extension == ".svg")
			{
				skippedCount++;
				imageReportLines.Add($"{fileName}\tsource={record.SourceWidth}x{record.SourceHeight}\tskipped=svg-vector-source");
				continue;
			}

			if (string.IsNullOrEmpty(globalPath) || !File.Exists(globalPath))
			{
				skippedCount++;
				imageReportLines.Add($"{fileName}\tsource={record.SourceWidth}x{record.SourceHeight}\tskipped=file-missing");
				continue;
			}

			if (nineSliceFileNames.Contains(fileName))
			{
				skippedCount++;
				imageReportLines.Add($"{fileName}\tsource={record.SourceWidth}x{record.SourceHeight}\tskipped=nine-slice-margins (pixel-based TextureMargin* in code would desync)");
				continue;
			}

			bool hasUsage = usages.TryGetValue(fileName, out ImageRenderUsage usage);
			Rect2I sourceCropRect = ComputeSourceCropRect(record);
			Vector2I targetSize = hasUsage ? new Vector2I(usage.Width, usage.Height) : sourceCropRect.Size;
			Vector2I outputSize = ComputeFitSize(sourceCropRect.Size.X, sourceCropRect.Size.Y, targetSize);
			string status = hasUsage ? "rendered" : "no-render-usage";

			string outputPath = writeInPlace ? ResolveInPlacePath(globalPath) : Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(fileName) + ".webp");
			string stagedPath = Path.Combine(stagingDirectory, $"staged_{stagedIndex++}.webp");

			Error saveError;
			try
			{
				saveError = RenderUxImage(globalPath, sourceCropRect, outputSize, stagedPath, cwebpPath);
			}
			catch (Exception exception)
			{
				skippedCount++;
				imageReportLines.Add($"{fileName}\tsource={record.SourceWidth}x{record.SourceHeight}\tskipped={exception.Message}");
				continue;
			}

			if (saveError != Error.Ok)
			{
				skippedCount++;
				imageReportLines.Add($"{fileName}\tsource={record.SourceWidth}x{record.SourceHeight}\tskipped=encode-{saveError}");
				continue;
			}

			long sourceFileBytes = new FileInfo(globalPath).Length;
			long outputFileBytes = new FileInfo(stagedPath).Length;
			bool dimensionsReduced = outputSize.X < sourceCropRect.Size.X || outputSize.Y < sourceCropRect.Size.Y;
			if (!dimensionsReduced && outputFileBytes >= sourceFileBytes)
			{
				File.Delete(stagedPath);
				skippedCount++;
				imageReportLines.Add($"{fileName}\tsource={record.SourceWidth}x{record.SourceHeight}\tskipped=no-size-gain (source={sourceFileBytes} bytes, webp={outputFileBytes} bytes)");
				continue;
			}

			File.Move(stagedPath, outputPath, true);

			resizedCount++;
			sourceBytes += sourceFileBytes;
			outputBytes += outputFileBytes;

			string renderSize = hasUsage ? $"{usage.Width}x{usage.Height}" : "none";
			imageReportLines.Add($"{fileName}\tsource={record.SourceWidth}x{record.SourceHeight}\tusedRect={record.UsedRectX},{record.UsedRectY} {record.UsedWidth}x{record.UsedHeight}@{record.ScaledWidth}x{record.ScaledHeight}\ttrimmed={sourceCropRect.Size.X}x{sourceCropRect.Size.Y}\trender={renderSize}\ttarget={targetSize.X}x{targetSize.Y}\toutput={outputSize.X}x{outputSize.Y}\t{status}\tsourceBytes={sourceFileBytes}\tbytes={outputFileBytes}\twritten={ProjectSettings.LocalizePath(outputPath)}");
		}

		var loadedBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string recordedFileName in groupedRecords.Keys)
		{
			loadedBaseNames.Add(Path.GetFileNameWithoutExtension(recordedFileName));
		}

		var notLoadedImageLines = new List<string>();
		foreach (string imageResourcePath in FindAllUxImageResourcePaths())
		{
			string imageFileName = Path.GetFileName(imageResourcePath);
			if (!loadedBaseNames.Contains(Path.GetFileNameWithoutExtension(imageFileName)))
			{
				notLoadedImageLines.Add(imageFileName);
			}
		}

		var reportLines = new List<string>
		{
			$"UX image resize report ({DateTime.Now:yyyy-MM-dd HH:mm:ss})",
			$"write mode: {(writeInPlace ? "in-place (Assets/UI)" : outputDirectory)}",
			$"cwebp encoder: {encoder}",
			$"scene count: {sceneResourcePaths.Count}",
			$"images resized: {resizedCount}, images skipped: {skippedCount}, images not loaded: {notLoadedImageLines.Count}",
			$"source bytes: {sourceBytes}, output bytes: {outputBytes}",
			string.Empty,
			"[scenes]"
		};
		reportLines.AddRange(sceneReportLines);
		reportLines.Add(string.Empty);
		reportLines.Add("[images]");
		reportLines.AddRange(imageReportLines);
		reportLines.Add(string.Empty);
		reportLines.Add("[not-loaded]");
		reportLines.AddRange(notLoadedImageLines);
		File.WriteAllLines(reportPath, reportLines);

		foreach (string sceneReportLine in sceneReportLines)
		{
			GD.Print($"[ResizeUxImages][scene] {sceneReportLine}");
		}

		foreach (string imageReportLine in imageReportLines)
		{
			GD.Print($"[ResizeUxImages][image] {imageReportLine}");
		}

		string writeMode = writeInPlace ? "in-place" : outputDirectory;
		GD.Print($"[ResizeUxImages] scenes={sceneResourcePaths.Count} resized={resizedCount} skipped={skippedCount} notLoaded={notLoadedImageLines.Count} writeMode={writeMode} encoder={encoder} sourceBytes={sourceBytes} outputBytes={outputBytes} report={reportPath}");
	}
}
