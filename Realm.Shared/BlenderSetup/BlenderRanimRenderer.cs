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
		{
			exportResult.Success = false;
			exportResult.ErrorMessage = "Animation data is null.";
			return exportResult;
		}

		string? temporaryModelFile = null;
		string? temporaryAnimationJsonFile = null;

		try
		{
			BlenderSetup.EnsureSetup(logCallback);

			string resolvedModelPath = string.Empty;

			if (renderOptions.ModelBytes != null && renderOptions.ModelBytes.Length > 0)
			{
				byte[] modelBytes = RmeshFile.IsRmeshBytes(renderOptions.ModelBytes)
					? (RmeshFile.GetGlbBytes(renderOptions.ModelBytes) ?? renderOptions.ModelBytes)
					: renderOptions.ModelBytes;

				temporaryModelFile = Path.Combine(Path.GetTempPath(), $"realm_model_{Guid.NewGuid():N}.glb");
				File.WriteAllBytes(temporaryModelFile, modelBytes);
				resolvedModelPath = temporaryModelFile;
			}
			else if (!string.IsNullOrEmpty(renderOptions.ModelPath) && File.Exists(renderOptions.ModelPath))
			{
				if (renderOptions.ModelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
				{
					byte[] rawBytes = File.ReadAllBytes(renderOptions.ModelPath);
					byte[]? glbBytes = RmeshFile.GetGlbBytes(rawBytes);
					temporaryModelFile = Path.Combine(Path.GetTempPath(), $"realm_model_{Guid.NewGuid():N}.glb");
					File.WriteAllBytes(temporaryModelFile, glbBytes ?? rawBytes);
					resolvedModelPath = temporaryModelFile;
				}
				else
				{
					resolvedModelPath = Path.GetFullPath(renderOptions.ModelPath);
				}
			}
			else
			{
				exportResult.Success = false;
				exportResult.ErrorMessage = "No model file or model bytes specified for 3D animation rendering.";
				return exportResult;
			}

			temporaryAnimationJsonFile = Path.Combine(Path.GetTempPath(), $"realm_anim_{Guid.NewGuid():N}.json");
			string jsonContent = JsonSerializer.Serialize(animationData, new JsonSerializerOptions { WriteIndented = false });
			File.WriteAllText(temporaryAnimationJsonFile, jsonContent, new UTF8Encoding(false));

			string? destinationDirectory = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrEmpty(destinationDirectory) && !Directory.Exists(destinationDirectory))
			{
				Directory.CreateDirectory(destinationDirectory);
			}

			string formatArgument = renderOptions.Format switch
			{
				RanimOutputFormat.Spritesheet => "spritesheet",
				_ => outputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !outputPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "spritesheet" : "webp"
			};

			var processStartInfo = new ProcessStartInfo
			{
				FileName = BlenderSetup.PythonExePath,
				WorkingDirectory = BlenderSetup.NodeDir,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			processStartInfo.ArgumentList.Add(BlenderSetup.RenderScriptPath);
			processStartInfo.ArgumentList.Add("--model");
			processStartInfo.ArgumentList.Add(resolvedModelPath);
			processStartInfo.ArgumentList.Add("--anim");
			processStartInfo.ArgumentList.Add(temporaryAnimationJsonFile);
			processStartInfo.ArgumentList.Add("--output");
			processStartInfo.ArgumentList.Add(Path.GetFullPath(outputPath));
			processStartInfo.ArgumentList.Add("--format");
			processStartInfo.ArgumentList.Add(formatArgument);
			processStartInfo.ArgumentList.Add("--fps");
			processStartInfo.ArgumentList.Add(renderOptions.Fps.ToString(CultureInfo.InvariantCulture));
			processStartInfo.ArgumentList.Add("--width");
			processStartInfo.ArgumentList.Add(renderOptions.Width.ToString());
			processStartInfo.ArgumentList.Add("--height");
			processStartInfo.ArgumentList.Add(renderOptions.Height.ToString());
			processStartInfo.ArgumentList.Add("--scale");
			processStartInfo.ArgumentList.Add(renderOptions.Scale.ToString(CultureInfo.InvariantCulture));
			processStartInfo.ArgumentList.Add("--quality");
			processStartInfo.ArgumentList.Add(Math.Clamp(renderOptions.Quality, 1, 100).ToString(CultureInfo.InvariantCulture));

			if (renderOptions.Lossless)
			{
				processStartInfo.ArgumentList.Add("--lossless");
			}

			if (renderOptions.MaxFrameCount.HasValue && renderOptions.MaxFrameCount.Value > 0)
			{
				processStartInfo.ArgumentList.Add("--max-frames");
				processStartInfo.ArgumentList.Add(renderOptions.MaxFrameCount.Value.ToString());
			}

			if (!renderOptions.DrawBorder)
			{
				processStartInfo.ArgumentList.Add("--no-border");
			}

			if (!renderOptions.DrawShadow)
			{
				processStartInfo.ArgumentList.Add("--no-shadow");
			}

			var standardOutput = new StringBuilder();
			var standardError = new StringBuilder();

			using var process = new Process { StartInfo = processStartInfo };
			process.OutputDataReceived += (_, eventArgs) =>
			{
				if (eventArgs.Data != null)
				{
					standardOutput.AppendLine(eventArgs.Data);
					logCallback?.Invoke(eventArgs.Data);
				}
			};
			process.ErrorDataReceived += (_, eventArgs) =>
			{
				if (eventArgs.Data != null)
				{
					standardError.AppendLine(eventArgs.Data);
					logCallback?.Invoke(eventArgs.Data);
				}
			};

			if (!process.Start())
			{
				exportResult.Success = false;
				exportResult.ErrorMessage = "Failed to launch Blender Python rendering process.";
				return exportResult;
			}

			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			process.WaitForExit();

			if (process.ExitCode != 0 || !File.Exists(outputPath))
			{
				exportResult.Success = false;
				exportResult.ErrorMessage = standardError.Length > 0
					? standardError.ToString()
					: $"Process exited with code {process.ExitCode}";
				return exportResult;
			}

			exportResult.Success = true;
			float duration = animationData.Duration > 0f ? animationData.Duration : 1.0f;
			int totalSourceFrames = (int)MathF.Ceiling(duration * renderOptions.Fps);
			int modulusStep = (renderOptions.MaxFrameCount.HasValue && renderOptions.MaxFrameCount.Value > 0 && totalSourceFrames > renderOptions.MaxFrameCount.Value)
				? (int)MathF.Ceiling((float)totalSourceFrames / renderOptions.MaxFrameCount.Value)
				: 1;
			int estimatedFrames = (int)MathF.Ceiling((float)totalSourceFrames / Math.Max(1, modulusStep));
			exportResult.FrameCount = Math.Max(1, estimatedFrames);

			return exportResult;
		}
		catch (Exception exception)
		{
			exportResult.Success = false;
			exportResult.ErrorMessage = exception.Message;
			return exportResult;
		}
		finally
		{
			if (temporaryModelFile != null)
			{
				try { File.Delete(temporaryModelFile); } catch { }
			}
			if (temporaryAnimationJsonFile != null)
			{
				try { File.Delete(temporaryAnimationJsonFile); } catch { }
			}
		}
	}

	public static RanimRenderResult RenderFrames(
		RealmAnimationData animationData,
		RanimRenderOptions? options = null,
		Action<string>? logCallback = null)
	{
		var renderOptions = options ?? new RanimRenderOptions();
		if (animationData == null)
		{
			return new RanimRenderResult();
		}

		string temporarySpritesheet = Path.Combine(Path.GetTempPath(), $"realm_spritesheet_{Guid.NewGuid():N}.png");
		var spritesheetRenderOptions = new RanimRenderOptions
		{
			Width = renderOptions.Width,
			Height = renderOptions.Height,
			Fps = renderOptions.Fps,
			MaxFrameCount = renderOptions.MaxFrameCount,
			Format = RanimOutputFormat.Spritesheet,
			Scale = renderOptions.Scale,
			DrawBorder = renderOptions.DrawBorder,
			DrawShadow = renderOptions.DrawShadow,
			ModelPath = renderOptions.ModelPath,
			ModelBytes = renderOptions.ModelBytes,
			Quality = renderOptions.Quality,
			Lossless = renderOptions.Lossless
		};

		try
		{
			var exportResult = ExportToFile(animationData, temporarySpritesheet, spritesheetRenderOptions, logCallback: logCallback);
			if (!exportResult.Success || !File.Exists(temporarySpritesheet))
			{
				return new RanimRenderResult();
			}

			using var fullSheet = SKBitmap.Decode(temporarySpritesheet);
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
				int sourceXOffset = frameIndex * renderOptions.Width;
				byte[] pixelBytes = new byte[renderOptions.Width * renderOptions.Height * 4];

				for (int y = 0; y < renderOptions.Height; y++)
				{
					for (int x = 0; x < renderOptions.Width; x++)
					{
						SKColor color = fullSheet.GetPixel(sourceXOffset + x, y);
						int idx = (y * renderOptions.Width + x) * 4;
						pixelBytes[idx] = color.Red;
						pixelBytes[idx + 1] = color.Green;
						pixelBytes[idx + 2] = color.Blue;
						pixelBytes[idx + 3] = color.Alpha;
					}
				}

				float time = (frameIndex / (float)frameCount) * duration;
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
		finally
		{
			if (File.Exists(temporarySpritesheet))
			{
				try { File.Delete(temporarySpritesheet); } catch { }
			}
		}
	}
}
