using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Realm.Shared.Animation;
using Realm.Shared.ModelOptimization;
using SkiaSharp;

namespace Realm.Shared.BlenderSetup;

public static class BlenderRanimRenderer
{
	public static RanimExportResult ExportToFile(
		RealmAnimationData animationData,
		string outputPath,
		RanimRenderOptions? options = null,
		string inputPath = "",
		Action<string>? logCallback = null)
	{
		var renderOptions = options ?? new RanimRenderOptions();
		var exportResult = new RanimExportResult
		{
			InputPath = inputPath,
			OutputPath = outputPath
		};

		if (animationData == null)
			return FailExport(exportResult, "Animation data is null.");

		string? temporaryModelFile = null;
		string? temporaryAnimationJsonFile = null;

		try
		{
			BlenderSetup.EnsureSetup(logCallback);

			string resolvedModelPath = ResolveModelPath(renderOptions, out temporaryModelFile);

			if (string.IsNullOrEmpty(resolvedModelPath))
				return FailExport(exportResult, "No model file or model bytes specified for 3D animation rendering.");

			temporaryAnimationJsonFile = CreateTemporaryAnimationJson(animationData);
			EnsureDestinationDirectory(outputPath);

			var processStartInfo = BuildProcessStartInfo(resolvedModelPath, temporaryAnimationJsonFile, outputPath, renderOptions);
			return RunRenderProcess(processStartInfo, outputPath, exportResult, animationData, renderOptions, logCallback);
		}
		catch (Exception exception)
		{
			return FailExport(exportResult, exception.Message);
		}
		finally
		{
			CleanupTemporaryFile(temporaryModelFile);
			CleanupTemporaryFile(temporaryAnimationJsonFile);
		}
	}

	private static RanimExportResult FailExport(RanimExportResult result, string message)
	{
		result.Success = false;
		result.ErrorMessage = message;
		return result;
	}

	private static void CleanupTemporaryFile(string? filePath)
	{
		if (string.IsNullOrEmpty(filePath)) return;
		if (!File.Exists(filePath)) return;
		try { File.Delete(filePath); } catch { }
	}

	private static string ResolveModelPath(RanimRenderOptions renderOptions, out string? temporaryModelFile)
	{
		temporaryModelFile = null;

		if (renderOptions.ModelBytes != null && renderOptions.ModelBytes.Length > 0)
			return ResolveFromBytes(renderOptions.ModelBytes, out temporaryModelFile);

		if (!string.IsNullOrEmpty(renderOptions.ModelPath) && File.Exists(renderOptions.ModelPath))
			return ResolveFromFilePath(renderOptions.ModelPath, out temporaryModelFile);

		return string.Empty;
	}

	private static string ResolveFromBytes(byte[] modelBytes, out string temporaryModelFile)
	{
		byte[] finalBytes = RmeshFile.IsRmeshBytes(modelBytes)
			? (RmeshFile.GetGlbBytes(modelBytes) ?? modelBytes)
			: modelBytes;

		temporaryModelFile = Path.Combine(Path.GetTempPath(), $"realm_model_{Guid.NewGuid():N}.glb");
		File.WriteAllBytes(temporaryModelFile, finalBytes);
		return temporaryModelFile;
	}

	private static string ResolveFromFilePath(string modelPath, out string? temporaryModelFile)
	{
		temporaryModelFile = null;
		if (!modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			return Path.GetFullPath(modelPath);

		byte[] rawBytes = File.ReadAllBytes(modelPath);
		byte[]? glbBytes = RmeshFile.GetGlbBytes(rawBytes);
		temporaryModelFile = Path.Combine(Path.GetTempPath(), $"realm_model_{Guid.NewGuid():N}.glb");
		File.WriteAllBytes(temporaryModelFile, glbBytes ?? rawBytes);
		return temporaryModelFile;
	}

	private static string CreateTemporaryAnimationJson(RealmAnimationData animationData)
	{
		string tempFile = Path.Combine(Path.GetTempPath(), $"realm_anim_{Guid.NewGuid():N}.json");
		string jsonContent = JsonSerializer.Serialize(animationData, new JsonSerializerOptions { WriteIndented = false });
		File.WriteAllText(tempFile, jsonContent, new UTF8Encoding(false));
		return tempFile;
	}

	private static void EnsureDestinationDirectory(string outputPath)
	{
		string? destinationDirectory = Path.GetDirectoryName(outputPath);
		if (string.IsNullOrEmpty(destinationDirectory)) return;
		if (Directory.Exists(destinationDirectory)) return;
		Directory.CreateDirectory(destinationDirectory);
	}

	private static ProcessStartInfo BuildProcessStartInfo(string modelPath, string animJsonPath, string outputPath, RanimRenderOptions options)
	{
		string formatArgument = GetFormatArgument(options.Format, outputPath);

		var startInfo = new ProcessStartInfo
		{
			FileName = BlenderSetup.PythonExePath,
			WorkingDirectory = BlenderSetup.NodeDir,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		startInfo.ArgumentList.Add(BlenderSetup.RenderScriptPath);
		startInfo.ArgumentList.Add("--model");
		startInfo.ArgumentList.Add(modelPath);
		startInfo.ArgumentList.Add("--anim");
		startInfo.ArgumentList.Add(animJsonPath);
		startInfo.ArgumentList.Add("--output");
		startInfo.ArgumentList.Add(Path.GetFullPath(outputPath));
		startInfo.ArgumentList.Add("--format");
		startInfo.ArgumentList.Add(formatArgument);
		startInfo.ArgumentList.Add("--fps");
		startInfo.ArgumentList.Add(options.Fps.ToString(CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add("--width");
		startInfo.ArgumentList.Add(options.Width.ToString());
		startInfo.ArgumentList.Add("--height");
		startInfo.ArgumentList.Add(options.Height.ToString());
		startInfo.ArgumentList.Add("--scale");
		startInfo.ArgumentList.Add(options.Scale.ToString(CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add("--quality");
		startInfo.ArgumentList.Add(Math.Clamp(options.Quality, 1, 100).ToString(CultureInfo.InvariantCulture));

		if (options.Lossless)
			startInfo.ArgumentList.Add("--lossless");

		if (options.MaxFrameCount.HasValue && options.MaxFrameCount.Value > 0)
		{
			startInfo.ArgumentList.Add("--max-frames");
			startInfo.ArgumentList.Add(options.MaxFrameCount.Value.ToString());
		}

		if (!options.DrawBorder)
			startInfo.ArgumentList.Add("--no-border");

		if (!options.DrawShadow)
			startInfo.ArgumentList.Add("--no-shadow");

		return startInfo;
	}

	private static string GetFormatArgument(RanimOutputFormat format, string outputPath)
	{
		if (format == RanimOutputFormat.Spritesheet)
			return "spritesheet";

		bool isPng = outputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
		bool isWebp = outputPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

		return (isPng && !isWebp) ? "spritesheet" : "webp";
	}

	private static RanimExportResult RunRenderProcess(
		ProcessStartInfo startInfo,
		string outputPath,
		RanimExportResult exportResult,
		RealmAnimationData animationData,
		RanimRenderOptions renderOptions,
		Action<string>? logCallback)
	{
		var standardOutput = new StringBuilder();
		var standardError = new StringBuilder();

		using var process = new Process { StartInfo = startInfo };
		process.OutputDataReceived += (_, e) => HandleProcessData(e, standardOutput, logCallback);
		process.ErrorDataReceived += (_, e) => HandleProcessData(e, standardError, logCallback);

		if (!process.Start())
			return FailExport(exportResult, "Failed to launch Blender Python rendering process.");

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();

		if (process.ExitCode != 0 || !File.Exists(outputPath))
		{
			string errorMsg = standardError.Length > 0 ? standardError.ToString() : $"Process exited with code {process.ExitCode}";
			return FailExport(exportResult, errorMsg);
		}

		exportResult.Success = true;
		exportResult.FrameCount = CalculateEstimatedFrames(animationData, renderOptions);
		return exportResult;
	}

	private static void HandleProcessData(DataReceivedEventArgs e, StringBuilder buffer, Action<string>? logCallback)
	{
		if (e.Data == null) return;
		buffer.AppendLine(e.Data);
		logCallback?.Invoke(e.Data);
	}

	private static int CalculateEstimatedFrames(RealmAnimationData animationData, RanimRenderOptions renderOptions)
	{
		float duration = animationData.Duration > 0f ? animationData.Duration : 1.0f;
		int totalSourceFrames = (int)MathF.Ceiling(duration * renderOptions.Fps);

		int modulusStep = 1;
		if (renderOptions.MaxFrameCount.HasValue && renderOptions.MaxFrameCount.Value > 0 && totalSourceFrames > renderOptions.MaxFrameCount.Value)
		{
			modulusStep = (int)MathF.Ceiling((float)totalSourceFrames / renderOptions.MaxFrameCount.Value);
		}

		int estimatedFrames = (int)MathF.Ceiling((float)totalSourceFrames / Math.Max(1, modulusStep));
		return Math.Max(1, estimatedFrames);
	}

	public static RanimRenderResult RenderFrames(
		RealmAnimationData animationData,
		RanimRenderOptions? options = null,
		Action<string>? logCallback = null)
	{
		var renderOptions = options ?? new RanimRenderOptions();
		if (animationData == null) return new RanimRenderResult();

		string temporarySpritesheet = Path.Combine(Path.GetTempPath(), $"realm_spritesheet_{Guid.NewGuid():N}.png");
		var spritesheetRenderOptions = BuildSpritesheetOptions(renderOptions);

		try
		{
			var exportResult = ExportToFile(animationData, temporarySpritesheet, spritesheetRenderOptions, logCallback: logCallback);
			if (!exportResult.Success || !File.Exists(temporarySpritesheet))
				return new RanimRenderResult();

			return ProcessRenderedSpritesheet(temporarySpritesheet, animationData, renderOptions);
		}
		finally
		{
			CleanupTemporaryFile(temporarySpritesheet);
		}
	}

	private static RanimRenderOptions BuildSpritesheetOptions(RanimRenderOptions sourceOptions)
	{
		return new RanimRenderOptions
		{
			Width = sourceOptions.Width,
			Height = sourceOptions.Height,
			Fps = sourceOptions.Fps,
			MaxFrameCount = sourceOptions.MaxFrameCount,
			Format = RanimOutputFormat.Spritesheet,
			Scale = sourceOptions.Scale,
			DrawBorder = sourceOptions.DrawBorder,
			DrawShadow = sourceOptions.DrawShadow,
			ModelPath = sourceOptions.ModelPath,
			ModelBytes = sourceOptions.ModelBytes,
			Quality = sourceOptions.Quality,
			Lossless = sourceOptions.Lossless
		};
	}

	private static RanimRenderResult ProcessRenderedSpritesheet(string spritesheetPath, RealmAnimationData animationData, RanimRenderOptions renderOptions)
	{
		using var fullSheet = SKBitmap.Decode(spritesheetPath);
		int frameCount = Math.Max(1, fullSheet.Width / renderOptions.Width);
		float duration = animationData.Duration > 0f ? animationData.Duration : 1.0f;

		var result = new RanimRenderResult
		{
			Duration = duration,
			TotalSourceFrames = frameCount,
			ModulusStep = 1,
			EffectiveFps = frameCount / duration
		};

		for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
		{
			float time = (frameIndex / (float)frameCount) * duration;
			byte[] pixelBytes = ExtractFramePixels(fullSheet, frameIndex, renderOptions.Width, renderOptions.Height);

			result.Frames.Add(new RanimRenderFrame
			{
				Width = renderOptions.Width,
				Height = renderOptions.Height,
				Time = time,
				RgbaBytes = pixelBytes
			});
		}

		return result;
	}

	private static byte[] ExtractFramePixels(SKBitmap fullSheet, int frameIndex, int width, int height)
	{
		int sourceXOffset = frameIndex * width;
		byte[] pixelBytes = new byte[width * height * 4];

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				SKColor color = fullSheet.GetPixel(sourceXOffset + x, y);
				int idx = (y * width + x) * 4;
				pixelBytes[idx] = color.Red;
				pixelBytes[idx + 1] = color.Green;
				pixelBytes[idx + 2] = color.Blue;
				pixelBytes[idx + 3] = color.Alpha;
			}
		}

		return pixelBytes;
	}
}
