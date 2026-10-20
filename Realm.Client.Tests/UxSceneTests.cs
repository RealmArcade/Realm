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

namespace Realm.Client.Tests;

[TestSuite]
[RequireGodotRuntime]
public class UxSceneTests
{
	public static MapData[] GetDummyMaps()
	{
		var maps = new MapData[]
		{
			new MapData
			{
				MapId = "castle_td",
				Title = "CASTLE TD",
				Version = "1.0.0",
				Creator = "Realm Builder",
				ThumbnailPath = "res://Assets/UI/moonlit_castle.webp",
				Description = "Defend the last fortress against waves of ancient evil! Includes the sintepres sucitoure unit. Features deep mountain passes, branching paths, unique hero units, and custom tower designs. Can you withstand the boss waves?",
				Screenshots = new string[]
				{
					"res://Assets/UI/moonlit_castle.webp",
					"res://Assets/UI/moonlit_forest.webp",
					"res://Assets/UI/forest_path.webp",
					"res://Assets/UI/snowy_forest_path.webp"
				},
				Features = new string[]
				{
					"Custom Units",
					"Boss Waves",
					"Achievements",
					"Hardcore Mode"
				},
				RatingStars = 4.7f,
				Votes5Star = "14,231 Votes",
				Votes3Star = "1,533 Votes",
				Votes1Star = "3,273 Votes",
				AvgRating = "4.7 / 5.0",
				AvgPlaytime = "42 min",
				PlayerCount = "1.2k Active (Last 24h)",
				CompletionRate = "74%",
				FileSize = "1.8 GB",
				EngineVersion = "Godot Realm Engine v1.0",
				MaxPlayers = "8 Players",
				Genre = "Campaign / TD",
				Awards = new string[]
				{
					"res://Assets/UI/gold_coin.webp",
					"res://Assets/UI/victory_flag.webp",
					"res://Assets/UI/battle_shield.webp",
					"res://Assets/UI/battle_axe.webp"
				}
			}
		};

		foreach (var m in maps)
		{
			m.AvailableVersions = new List<MapData> { m };
		}

		return maps;
	}

	[TestCase]
	public async Task CaptureUxScreenshots()
	{
		ISceneRunner runner = ISceneRunner.Load("res://GameHost.tscn");
		await runner.AwaitMillis(1000);

		string tempDir = Path.Combine(Path.GetTempPath(), "Realm_UX_Screenshots");
		Directory.CreateDirectory(tempDir);

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
				MapData[] dummy = GetDummyMaps();
				MapData mapData = (dummy != null && dummy.Length > 0) ? dummy[0] : new MapData { Title = "Test Map" };
				UIManager.Instance.TransitionToMapDetails(mapData);
			}
			else
			{
				UIManager.Instance.TransitionTo(screen);
			}

			await runner.AwaitMillis(1000);

			Image image = runner.Scene().GetViewport().GetTexture().GetImage();
			string fileName = $"UX_Screen_{screen}.png";
			string filePath = Path.Combine(tempDir, fileName);
			image.SavePng(filePath);
		}
	}

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

		string configuredOutputDirectory = Environment.GetEnvironmentVariable("REALM_RESIZED_UX_OUTPUT");
		bool writeInPlace = string.IsNullOrWhiteSpace(configuredOutputDirectory);
		string stagingDirectory = Path.Combine(Path.GetTempPath(), "Realm_Resized_UX_Images");
		Directory.CreateDirectory(stagingDirectory);

		if (!writeInPlace)
		{
			Directory.CreateDirectory(configuredOutputDirectory);
		}

		string reportPath = Path.Combine(stagingDirectory, "UX_Image_Resize_Report.txt");

		var usages = new Dictionary<string, ImageRenderUsage>(StringComparer.OrdinalIgnoreCase);
		var nineSliceFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		ISceneRunner runner = ISceneRunner.Load("res://GameHost.tscn");
		await runner.AwaitMillis(1000);

		Node sceneRoot = runner.Scene();
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
				UIManager.Instance.TransitionToMapDetails(mapData);
			}
			else
			{
				UIManager.Instance.TransitionTo(screen);
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

			Node instance = null;
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

			string outputPath = writeInPlace ? ResolveInPlacePath(globalPath) : Path.Combine(configuredOutputDirectory, Path.GetFileNameWithoutExtension(fileName) + ".webp");
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
			$"write mode: {(writeInPlace ? "in-place (Assets/UI)" : configuredOutputDirectory)}",
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

		string writeMode = writeInPlace ? "in-place" : configuredOutputDirectory;
		GD.Print($"[ResizeUxImages] scenes={sceneResourcePaths.Count} resized={resizedCount} skipped={skippedCount} notLoaded={notLoadedImageLines.Count} writeMode={writeMode} encoder={encoder} sourceBytes={sourceBytes} outputBytes={outputBytes} report={reportPath}");
	}

	private static string ResolveInPlacePath(string globalPath)
	{
		if (Path.GetExtension(globalPath).Equals(".webp", StringComparison.OrdinalIgnoreCase))
		{
			return globalPath;
		}

		return Path.ChangeExtension(globalPath, ".webp");
	}

	private static string ResolveGlobalPath(string resourcePath)
	{
		if (string.IsNullOrEmpty(resourcePath))
		{
			return string.Empty;
		}

		if (resourcePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			return ProjectSettings.GlobalizePath(resourcePath);
		}

		return resourcePath;
	}

	private static bool IsUxImagePath(string resourcePath)
	{
		if (string.IsNullOrEmpty(resourcePath))
		{
			return false;
		}

		string extension = Path.GetExtension(resourcePath).ToLowerInvariant();
		foreach (string candidateExtension in UxImageExtensions)
		{
			if (extension == candidateExtension)
			{
				return true;
			}
		}

		return false;
	}

	private static List<string> FindAllSceneResourcePaths()
	{
		var sceneResourcePaths = new List<string>();
		CollectSceneResourcePaths("res://", sceneResourcePaths);
		sceneResourcePaths.Sort(StringComparer.OrdinalIgnoreCase);
		return sceneResourcePaths;
	}

	private static List<string> FindAllUxImageResourcePaths()
	{
		var imageResourcePaths = new List<string>();
		CollectImageResourcePaths("res://Assets/UI", imageResourcePaths);
		imageResourcePaths.Sort(StringComparer.OrdinalIgnoreCase);
		return imageResourcePaths;
	}

	private static void CollectImageResourcePaths(string directoryResourcePath, List<string> imageResourcePaths)
	{
		using DirAccess directory = DirAccess.Open(directoryResourcePath);
		if (directory == null)
		{
			return;
		}

		string directoryPrefix = directoryResourcePath.EndsWith("/", StringComparison.Ordinal) ? directoryResourcePath : directoryResourcePath + "/";

		foreach (string fileName in directory.GetFiles())
		{
			if (IsUxImagePath(fileName))
			{
				imageResourcePaths.Add(directoryPrefix + fileName);
			}
		}

		foreach (string childDirectory in directory.GetDirectories())
		{
			if (childDirectory.StartsWith(".", StringComparison.Ordinal))
			{
				continue;
			}

			CollectImageResourcePaths(directoryPrefix + childDirectory, imageResourcePaths);
		}
	}

	private static void CollectSceneResourcePaths(string directoryResourcePath, List<string> sceneResourcePaths)
	{
		using DirAccess directory = DirAccess.Open(directoryResourcePath);
		if (directory == null)
		{
			return;
		}

		string directoryPrefix = directoryResourcePath.EndsWith("/", StringComparison.Ordinal) ? directoryResourcePath : directoryResourcePath + "/";

		foreach (string fileName in directory.GetFiles())
		{
			if (fileName.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
			{
				sceneResourcePaths.Add(directoryPrefix + fileName);
			}
		}

		foreach (string childDirectory in directory.GetDirectories())
		{
			if (childDirectory.StartsWith(".", StringComparison.Ordinal))
			{
				continue;
			}

			CollectSceneResourcePaths(directoryPrefix + childDirectory, sceneResourcePaths);
		}
	}

	private static void CollectTextureUsages(Node root, Dictionary<string, ImageRenderUsage> usages, HashSet<string> nineSliceFileNames)
	{
		var pendingNodes = new Stack<Node>();
		pendingNodes.Push(root);

		while (pendingNodes.Count > 0)
		{
			Node node = pendingNodes.Pop();

			if (node is Control control)
			{
				CollectControlTextureUsages(control, usages, nineSliceFileNames);
			}

			foreach (Node child in node.GetChildren())
			{
				pendingNodes.Push(child);
			}
		}
	}

	private static void CollectControlTextureUsages(Control control, Dictionary<string, ImageRenderUsage> usages, HashSet<string> nineSliceFileNames)
	{
		bool isLayoutResolved = IsLayoutResolved(control, control.Size);

		if (control is TextureRect textureRect && textureRect.Texture != null)
		{
			RecordTextureUsage(textureRect.Texture, ComputeTextureRectDrawnSize(textureRect), usages, false, nineSliceFileNames);
		}

		if (control is Button button && button.Icon != null)
		{
			Vector2 iconSize = button.Icon.GetSize();
			int iconMaxWidth = button.GetThemeConstant("icon_max_width");
			if (iconMaxWidth > 0 && iconSize.X > iconMaxWidth)
			{
				iconSize *= iconMaxWidth / iconSize.X;
			}
			else if (button.ExpandIcon)
			{
				iconSize = button.Size;
			}

			RecordTextureUsage(button.Icon, iconSize, usages, false, nineSliceFileNames);
		}

		if (!isLayoutResolved)
		{
			return;
		}

		if (control is TextureButton textureButton)
		{
			RecordTextureUsage(textureButton.TextureNormal, control.Size, usages, false, nineSliceFileNames);
			RecordTextureUsage(textureButton.TexturePressed, control.Size, usages, false, nineSliceFileNames);
			RecordTextureUsage(textureButton.TextureHover, control.Size, usages, false, nineSliceFileNames);
			RecordTextureUsage(textureButton.TextureDisabled, control.Size, usages, false, nineSliceFileNames);
		}

		foreach (string themeName in StyleBoxThemeNames)
		{
			StyleBox styleBox = control.GetThemeStylebox(themeName);
			if (styleBox is StyleBoxTexture styleBoxTexture)
			{
				bool hasTextureMargins = Mathf.Max(Mathf.Max(styleBoxTexture.TextureMarginLeft, styleBoxTexture.TextureMarginRight), Mathf.Max(styleBoxTexture.TextureMarginTop, styleBoxTexture.TextureMarginBottom)) > 0.5f;
				RecordTextureUsage(styleBoxTexture.Texture, control.Size, usages, hasTextureMargins, nineSliceFileNames);
			}
		}
	}

	private static bool IsLayoutResolved(Control control, Vector2 drawnSize)
	{
		if (!control.IsVisibleInTree())
		{
			return false;
		}

		if (control.Size.X < 1.0f || control.Size.Y < 1.0f || drawnSize.X < 1.0f || drawnSize.Y < 1.0f)
		{
			return false;
		}

		Vector2 combinedMinimumSize = control.GetCombinedMinimumSize();
		return control.Size.X + 0.5f >= combinedMinimumSize.X && control.Size.Y + 0.5f >= combinedMinimumSize.Y;
	}

	private static Vector2 ComputeTextureRectDrawnSize(TextureRect textureRect)
	{
		Vector2 textureSize = textureRect.Texture.GetSize();
		if (textureRect.ExpandMode != TextureRect.ExpandModeEnum.IgnoreSize)
		{
			return textureRect.IsVisibleInTree() ? textureSize : Vector2.Zero;
		}

		Vector2 availableSize = textureRect.Size;
		if (!IsLayoutResolved(textureRect, availableSize))
		{
			return Vector2.Zero;
		}

		switch (textureRect.StretchMode)
		{
			case TextureRect.StretchModeEnum.Scale:
			case TextureRect.StretchModeEnum.KeepAspectCovered:
				return availableSize;
			case TextureRect.StretchModeEnum.KeepAspect:
			case TextureRect.StretchModeEnum.KeepAspectCentered:
				return FitSizeToAspect(textureSize, availableSize);
			default:
				return textureSize;
		}
	}

	private static Vector2 FitSizeToAspect(Vector2 textureSize, Vector2 availableSize)
	{
		if (textureSize.X < 1.0f || textureSize.Y < 1.0f)
		{
			return availableSize;
		}

		float scale = Mathf.Min(availableSize.X / textureSize.X, availableSize.Y / textureSize.Y);
		return textureSize * scale;
	}

	private static void RecordTextureUsage(Texture2D texture, Vector2 drawnSize, Dictionary<string, ImageRenderUsage> usages, bool hasTextureMargins, HashSet<string> nineSliceFileNames)
	{
		if (texture == null)
		{
			return;
		}

		string resourcePath = ResolveTextureResourcePath(texture);
		if (!IsUxImagePath(resourcePath))
		{
			return;
		}

		string fileName = Path.GetFileName(resourcePath);

		if (hasTextureMargins)
		{
			nineSliceFileNames.Add(fileName);
		}

		int width = Mathf.RoundToInt(drawnSize.X);
		int height = Mathf.RoundToInt(drawnSize.Y);
		if (width < 1 || height < 1)
		{
			return;
		}

		float area = (float)width * height;

		if (usages.TryGetValue(fileName, out ImageRenderUsage existingUsage) && existingUsage.Area >= area)
		{
			return;
		}

		usages[fileName] = new ImageRenderUsage
		{
			ResourcePath = resourcePath,
			Width = width,
			Height = height,
			Area = area
		};
	}

	private static string ResolveTextureResourcePath(Texture2D texture)
	{
		if (TrimmedTextureLoader.TryGetTextureSourcePath(texture, out string recordedPath))
		{
			return recordedPath;
		}

		return texture.ResourcePath;
	}

	private static Rect2I ComputeSourceCropRect(TrimmedTextureLoadRecord record)
	{
		int sourceWidth = Mathf.Max(1, record.SourceWidth);
		int sourceHeight = Mathf.Max(1, record.SourceHeight);
		int scaledWidth = record.ScaledWidth > 0 ? record.ScaledWidth : sourceWidth;
		int scaledHeight = record.ScaledHeight > 0 ? record.ScaledHeight : sourceHeight;

		float scaleX = sourceWidth / (float)scaledWidth;
		float scaleY = sourceHeight / (float)scaledHeight;

		int usedX = Mathf.Clamp(Mathf.RoundToInt(record.UsedRectX * scaleX), 0, sourceWidth - 1);
		int usedY = Mathf.Clamp(Mathf.RoundToInt(record.UsedRectY * scaleY), 0, sourceHeight - 1);
		int usedWidth = Mathf.Clamp(Mathf.RoundToInt(record.UsedWidth * scaleX), 1, sourceWidth - usedX);
		int usedHeight = Mathf.Clamp(Mathf.RoundToInt(record.UsedHeight * scaleY), 1, sourceHeight - usedY);

		return new Rect2I(usedX, usedY, usedWidth, usedHeight);
	}

	private static Vector2I ComputeFitSize(int sourceWidth, int sourceHeight, Vector2I targetSize)
	{
		float scale = Mathf.Min(1.0f, Mathf.Min(targetSize.X / (float)sourceWidth, targetSize.Y / (float)sourceHeight));
		return new Vector2I(Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale)), Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale)));
	}

	private static Image TrimToRect(Image sourceImage, Rect2I cropRect)
	{
		if (cropRect.Position.X == 0 && cropRect.Position.Y == 0 && cropRect.Size.X == sourceImage.GetWidth() && cropRect.Size.Y == sourceImage.GetHeight())
		{
			return sourceImage;
		}

		return sourceImage.GetRegion(cropRect);
	}

	private static Image ScaleToSize(Image sourceImage, Vector2I outputSize)
	{
		if (sourceImage.GetWidth() == outputSize.X && sourceImage.GetHeight() == outputSize.Y)
		{
			return sourceImage;
		}

		sourceImage.Resize(outputSize.X, outputSize.Y, Image.Interpolation.Lanczos);
		return sourceImage;
	}

	private static Error RenderUxImage(string globalPath, Rect2I sourceCropRect, Vector2I outputSize, string outputPath, string cwebpPath)
	{
		Image sourceImage = Image.LoadFromFile(globalPath);
		if (sourceImage == null || sourceImage.IsEmpty())
		{
			return Error.CantOpen;
		}

		Image trimmedImage = null;
		Image outputImage = null;

		try
		{
			trimmedImage = TrimToRect(sourceImage, sourceCropRect);
			outputImage = ScaleToSize(trimmedImage, outputSize);
			return SaveAsWebp(outputImage, outputPath, cwebpPath);
		}
		finally
		{
			if (outputImage != null && !ReferenceEquals(outputImage, trimmedImage) && !ReferenceEquals(outputImage, sourceImage))
			{
				outputImage.Dispose();
			}

			if (trimmedImage != null && !ReferenceEquals(trimmedImage, sourceImage))
			{
				trimmedImage.Dispose();
			}

			sourceImage.Dispose();
		}
	}

	private static Error SaveAsWebp(Image image, string outputPath, string cwebpPath)
	{
		if (!string.IsNullOrEmpty(cwebpPath))
		{
			string tempPngPath = outputPath + ".source.png";
			Error pngError = image.SavePng(tempPngPath);
			if (pngError != Error.Ok)
			{
				return pngError;
			}

			var startInfo = new ProcessStartInfo(cwebpPath);
			startInfo.UseShellExecute = false;
			startInfo.CreateNoWindow = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.RedirectStandardError = true;
			startInfo.ArgumentList.Add("-quiet");
			startInfo.ArgumentList.Add("-q");
			startInfo.ArgumentList.Add("90");
			startInfo.ArgumentList.Add(tempPngPath);
			startInfo.ArgumentList.Add("-o");
			startInfo.ArgumentList.Add(outputPath);

			try
			{
				using Process process = Process.Start(startInfo);
				if (process == null)
				{
					return Error.CantCreate;
				}

				process.WaitForExit();
				return process.ExitCode == 0 ? Error.Ok : Error.Failed;
			}
			finally
			{
				if (File.Exists(tempPngPath))
				{
					File.Delete(tempPngPath);
				}
			}
		}

		return image.SaveWebp(outputPath, true, 0.9f);
	}

	private static string FindCwebpExecutable()
	{
		string configuredPath = Environment.GetEnvironmentVariable("REALM_CWEBP_PATH");
		if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
		{
			return configuredPath;
		}

		string pathVariable = Environment.GetEnvironmentVariable("PATH");
		if (string.IsNullOrEmpty(pathVariable))
		{
			return string.Empty;
		}

		foreach (string directory in pathVariable.Split(Path.PathSeparator))
		{
			string trimmedDirectory = directory.Trim().Trim('"');
			if (trimmedDirectory.Length == 0)
			{
				continue;
			}

			foreach (string executableName in new[] { "cwebp.exe", "cwebp" })
			{
				string candidatePath = Path.Combine(trimmedDirectory, executableName);
				if (File.Exists(candidatePath))
				{
					return candidatePath;
				}
			}
		}

		return string.Empty;
	}
}