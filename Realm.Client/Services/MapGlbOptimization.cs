using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Bringing the .glb models in a map workspace up to the editor's optimization: every model is checked
///     for the optimizer's completion marker, its animations are moved into per-animation files before the
///     mesh is rewritten, and the result replaces the model in place with its metadata hashes updated. The
///     per-file result is cached against the file's write time, so a workspace that has already been
///     optimized costs one read per model.
/// </summary>
internal static class MapGlbOptimization
{
	private static readonly ConcurrentDictionary<string, (DateTime LastWriteTime, bool HasFlag)> _optimizedFlagCache = new(StringComparer.OrdinalIgnoreCase);

	public static void EnsureGlbAssetsOptimized(string workspacePath)
	{
		MapWorkspaceService.NormalizeMetadataTextureEntries(workspacePath);
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] glbFiles = Directory.GetFiles(assetsDir, "*.glb", SearchOption.AllDirectories);
			if (glbFiles.Length == 0) return;

			bool anyReimported = ProcessGlbFiles(glbFiles, workspacePath);

			if (anyReimported)
				ModelCache.Clear();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapGlbOptimization] EnsureGlbAssetsOptimized error: {ex.Message}");
		}
	}

	private static bool ProcessGlbFiles(string[] glbFiles, string workspacePath)
	{
		bool anyReimported = false;
		var optimizer = new GlbOptimizer();
		var options = new OptimizationOptions
		{
			SimplificationRatio = 0.5f,
			AllowedPixelError = 1.5f,
			MaxTextureResolution = 1024,
			ForceReDecimate = false,
			CompressTextures = true
		};

		foreach (string glbPath in glbFiles)
		{
			if (MapWorkspacePaths.IsBuildOutputPath(glbPath)) continue;

			if (!TryGetFileWriteTime(glbPath, out DateTime lastWrite)) continue;

			if (_optimizedFlagCache.TryGetValue(glbPath, out var cached) && cached.LastWriteTime == lastWrite && cached.HasFlag)
				continue;

			anyReimported |= TryOptimizeGlb(glbPath, workspacePath, optimizer, options, lastWrite);
		}

		return anyReimported;
	}

	private static bool TryGetFileWriteTime(string path, out DateTime lastWrite)
	{
		try
		{
			lastWrite = File.GetLastWriteTimeUtc(path);
			return true;
		}
		catch
		{
			lastWrite = default;
			return false;
		}
	}

	private static bool TryOptimizeGlb(string glbPath, string workspacePath, GlbOptimizer optimizer, OptimizationOptions options, DateTime lastWrite)
	{
		byte[] glbBytes;
		try
		{
			glbBytes = File.ReadAllBytes(glbPath);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapGlbOptimization] Failed to read GLB {glbPath}: {ex.Message}");
			return false;
		}

		if (glbBytes == null || glbBytes.Length == 0) return false;

		if (optimizer.IsOptimized(glbBytes))
		{
			_optimizedFlagCache[glbPath] = (lastWrite, true);
			return false;
		}

		string fileName = Path.GetFileName(glbPath);
		GameLog.LogToConsole($"[MapGlbOptimization] GLB asset '{fileName}' is missing realm_optimize_completed extras. Optimizing into workspace...");

		glbBytes = ExtractAndStripAnimations(workspacePath, glbPath, fileName, glbBytes);

		return PerformGlbOptimization(glbPath, workspacePath, fileName, glbBytes, optimizer, options, lastWrite);
	}

	private static bool PerformGlbOptimization(string glbPath, string workspacePath, string fileName, byte[] glbBytes, GlbOptimizer optimizer, OptimizationOptions options, DateTime lastWrite)
	{
		OptimizationResult optResult = default;
		try
		{
			optResult = optimizer.Optimize(glbBytes, options);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapGlbOptimization] Exception ({ex.GetType().Name}) optimizing {fileName}: {ex.Message}");
			_optimizedFlagCache[glbPath] = (lastWrite, true);
			return false;
		}

		if (optResult.Success && optResult.OutputGlbBytes != null && optResult.OutputGlbBytes.Length > 0)
		{
			return SaveOptimizedGlb(workspacePath, glbPath, fileName, optResult);
		}

		_optimizedFlagCache[glbPath] = (lastWrite, true);
		GameLog.LogError($"[MapGlbOptimization] Optimization failed for {fileName}: {optResult.ErrorMessage}");
		return false;
	}

	private static List<string> GetUnoptimizedGlbFiles(string[] glbFiles, GlbOptimizer optimizer)
	{
		var unoptimizedList = new List<string>();
		foreach (string glbPath in glbFiles)
		{
			string normalized = glbPath.Replace("\\", "/");
			if (MapWorkspacePaths.IsBuildOutputPath(normalized)) continue;

			if (!TryReadGlbBytes(glbPath, out var lastWrite, out var bytes)) continue;

			if (_optimizedFlagCache.TryGetValue(glbPath, out var cached) && cached.LastWriteTime == lastWrite && cached.HasFlag) continue;

			if (optimizer.IsOptimized(bytes))
			{
				_optimizedFlagCache[glbPath] = (lastWrite, true);
				continue;
			}

			unoptimizedList.Add(glbPath);
		}

		return unoptimizedList;
	}

	private static bool TryReadGlbBytes(
		string glbPath, out DateTime lastWrite, [NotNullWhen(true)] out byte[]? bytes)
	{
		bytes = null;
		lastWrite = default;
		try
		{
			lastWrite = File.GetLastWriteTimeUtc(glbPath);
			bytes = File.ReadAllBytes(glbPath);
			return bytes != null && bytes.Length > 0;
		}
		catch
		{
			return false;
		}
	}

	private static async Task NotifyModelProgressAsync(Func<int, int, string, Task>? onModelProgress, int index, int total, string fileName)
	{
		if (onModelProgress == null) return;
		try
		{
			await onModelProgress(index + 1, total, fileName);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapGlbOptimization] Progress callback error: {ex.Message}");
		}
	}

	private static byte[] ExtractAndStripAnimations(string workspacePath, string glbPath, string fileName, byte[] glbBytes)
	{
		try
		{
			string animsDir = Path.Combine(workspacePath, "Assets", "animations");
			Directory.CreateDirectory(animsDir);
			var extractedAnims = MixamoAnimationImporter.ExtractAnimationsFromGlb(glbPath);
			foreach (var (animName, animData) in extractedAnims)
			{
				string animFileName = $"{animName.ToLowerInvariant()}.ranim";
				string animFilePath = Path.Combine(animsDir, animFileName);
				if (!File.Exists(animFilePath))
				{
					RealmAnimationSerializer.SaveToFile(animFilePath, animData);
					MapWorkspaceService.UpdateMetadataAnimationHash(workspacePath, animFileName, RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(animFilePath), ".ranim"));
				}
			}

			if (extractedAnims.Count > 0)
			{
				MixamoAnimationImporter.StripAnimationsFromGlb(glbPath, glbPath);
				return File.ReadAllBytes(glbPath);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapGlbOptimization] Animation extraction error for {fileName}: {ex.Message}");
		}

		return glbBytes;
	}

	private static bool TryOptimizeAndSaveGlb(string workspacePath, string glbPath, string fileName, byte[] glbBytes, DateTime lastWrite, GlbOptimizer optimizer, OptimizationOptions options)
	{
		OptimizationResult optResult = default;
		try
		{
			optResult = optimizer.Optimize(glbBytes, options);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapGlbOptimization] Exception optimizing {fileName}: {ex.Message}");
			_optimizedFlagCache[glbPath] = (lastWrite, true);
			return false;
		}

		if (optResult.Success && optResult.OutputGlbBytes != null && optResult.OutputGlbBytes.Length > 0)
		{
			return SaveOptimizedGlb(workspacePath, glbPath, fileName, optResult);
		}
		else
		{
			_optimizedFlagCache[glbPath] = (lastWrite, true);
			GameLog.LogError($"[MapGlbOptimization] Optimization failed for {fileName}: {optResult.ErrorMessage}");
			return false;
		}
	}

	private static bool SaveOptimizedGlb(string workspacePath, string glbPath, string fileName, OptimizationResult optResult)
	{
		try
		{
			var attrs = File.GetAttributes(glbPath);
			if ((attrs & FileAttributes.ReadOnly) != 0)
			{
				File.SetAttributes(glbPath, attrs & ~FileAttributes.ReadOnly);
			}

			if (optResult.OutputGlbBytes is not { Length: > 0 } outputBytes) return false;

			File.Delete(glbPath);
			File.WriteAllBytes(glbPath, outputBytes);

			DateTime newLastWrite = File.GetLastWriteTimeUtc(glbPath);
			_optimizedFlagCache[glbPath] = (newLastWrite, true);

			string newHash = RealmMetadataHelper.ComputeBlake3(outputBytes, ".glb");
			MapWorkspaceService.UpdateMetadataGlbHash(workspacePath, fileName, newHash);

			GameLog.LogToConsole($"[MapGlbOptimization] Successfully optimized {fileName} ({optResult.OriginalSize} -> {optResult.OptimizedSize} bytes).");
			return true;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapGlbOptimization] Failed to write optimized GLB {glbPath}: {ex.Message}");
			return false;
		}
	}

	private static async Task<bool> TryOptimizeSingleGlbAsync(string workspacePath, string glbPath, string fileName, int index, int count, GlbOptimizer optimizer, OptimizationOptions options, Func<int, int, string, Task>? onModelProgress)
	{
		await NotifyModelProgressAsync(onModelProgress, index, count, fileName);

		if (!TryReadGlbBytes(glbPath, out var lastWrite, out var glbBytes))
		{
			GameLog.LogError($"[MapGlbOptimization] Failed to read GLB {glbPath}");
			return false;
		}

		GameLog.LogToConsole($"[MapGlbOptimization] GLB asset '{fileName}' ({index + 1}/{count}) is missing realm_optimize_completed extras. Optimizing into workspace...");

		glbBytes = ExtractAndStripAnimations(workspacePath, glbPath, fileName, glbBytes);

		return TryOptimizeAndSaveGlb(workspacePath, glbPath, fileName, glbBytes, lastWrite, optimizer, options);
	}

	public static async Task EnsureGlbAssetsOptimizedCooperativeAsync(
		string workspacePath,
		Func<int, int, string, Task>? onModelProgress = null)
	{
		MapWorkspaceService.NormalizeMetadataTextureEntries(workspacePath);
		if (string.IsNullOrEmpty(workspacePath)) return;
		if (!Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			await ProcessGlbAssetsAsync(workspacePath, assetsDir, onModelProgress);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapGlbOptimization] EnsureGlbAssetsOptimizedCooperativeAsync error: {ex.Message}");
		}
	}

	private static async Task ProcessGlbAssetsAsync(string workspacePath, string assetsDir, Func<int, int, string, Task>? onModelProgress)
	{
		string[] glbFiles = Directory.GetFiles(assetsDir, "*.glb", SearchOption.AllDirectories);
		if (glbFiles.Length == 0) return;

		var optimizer = new GlbOptimizer();
		var options = new OptimizationOptions
		{
			SimplificationRatio = 0.5f,
			AllowedPixelError = 1.5f,
			MaxTextureResolution = 1024,
			ForceReDecimate = false,
			CompressTextures = true
		};

		var unoptimizedList = GetUnoptimizedGlbFiles(glbFiles, optimizer);
		if (unoptimizedList.Count == 0) return;

		bool anyReimported = false;
		for (int i = 0; i < unoptimizedList.Count; i++)
		{
			string glbPath = unoptimizedList[i];
			string fileName = Path.GetFileName(glbPath);

			if (await TryOptimizeSingleGlbAsync(workspacePath, glbPath, fileName, i, unoptimizedList.Count, optimizer, options, onModelProgress))
			{
				anyReimported = true;
			}
		}

		if (anyReimported)
		{
			ModelCache.Clear();
		}
	}
}
