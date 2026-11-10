using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;

namespace Realm.Client.Utils
{
	public static partial class ModelCache
	{
		public static string? ResolveModelPath(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return null;

			if (ResolutionContextChanged()) ForgetResolutionsUnderThePreviousMap();

			if (_resolvedModelPaths.TryGetValue(modelPath, out var cachedPath))
			{
				return string.IsNullOrEmpty(cachedPath) ? null : cachedPath;
			}

			string? resolved = ResolveModelPathInternal(modelPath);

			// Only a path that was found is cached. A path that was not found is answered against whichever map the
			// process is looking at, and the map a lobby pre-warms resolves after the models it names are queued, so
			// caching the miss would leave every mesh of that match looking for a file in the previous map.
			if (!string.IsNullOrEmpty(resolved)) _resolvedModelPaths[modelPath] = resolved;
			return resolved;
		}

		private static string _resolutionMapDirectory = string.Empty;
		private static string _resolutionMapName = string.Empty;

		private static bool ResolutionContextChanged()
		{
			string mapDirectory = ReadCurrentMapDirectory();
			string mapName = GameContext.Simulation.ActiveMapName ?? string.Empty;

			if (string.Equals(mapDirectory, _resolutionMapDirectory, StringComparison.Ordinal)
			    && string.Equals(mapName, _resolutionMapName, StringComparison.Ordinal))
			{
				return false;
			}

			_resolutionMapDirectory = mapDirectory;
			_resolutionMapName = mapName;
			return true;
		}

		/// <summary>
		///     Reads which map the process is resolving against. It is read during shutdown as well, when the scope holding the
		///     storage service has already been disposed, so a closed scope reads as no map rather than as a fault.
		/// </summary>
		private static string ReadCurrentMapDirectory()
		{
			try
			{
				return GameContext.MapStorageService.CurrentMapDirectory ?? string.Empty;
			}
			catch (ObjectDisposedException)
			{
				return string.Empty;
			}
		}

		private static void ForgetResolutionsUnderThePreviousMap()
		{
			_resolvedModelPaths.Clear();

			_modelCacheLock.EnterWriteLock();
			try
			{
				_failedModelPaths.Clear();
			}
			finally
			{
				_modelCacheLock.ExitWriteLock();
			}
		}

		private static string? ResolveModelPathInternal(string modelPath)
		{
			string cleanPath = modelPath.TrimStart('/', '\\');
			string withRmesh = GetPathWithRmeshExtension(cleanPath);

			string? result = TryResolveDirectPath(modelPath);
			if (result != null) return result;

			result = TryResolveResUserPath(modelPath);
			if (result != null) return result;

			return TryResolveAdditionalPaths(cleanPath, withRmesh);
		}

		private static string GetPathWithRmeshExtension(string cleanPath)
		{
			if (cleanPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || cleanPath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
			{
				return Path.ChangeExtension(cleanPath, ".rmesh");
			}

			if (!cleanPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && !cleanPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				return $"{cleanPath}.rmesh";
			}

			return cleanPath;
		}

		private static string? TryResolveAdditionalPaths(string cleanPath, string withRmesh)
		{
			string? result = TryResolveEditingDirectory(cleanPath, withRmesh);
			if (result != null) return result;

			result = TryResolveActiveMapPath(cleanPath, withRmesh);
			if (result != null) return result;

			result = TryResolveCurrentMapPath(cleanPath, withRmesh);
			if (result != null) return result;

			result = TryResolveResUserRoot(cleanPath, withRmesh);
			if (result != null) return result;

			result = TryResolveInBaseLocations(cleanPath, withRmesh);
			if (result != null) return result;

			return TryResolveViaPathUtils(cleanPath, withRmesh);
		}

		private static string? TryResolveDirectPath(string modelPath)
		{
			if ((modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) && File.Exists(modelPath))
			{
				return modelPath;
			}

			string candDirectRmesh = Path.ChangeExtension(modelPath, ".rmesh");
			if (candDirectRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candDirectRmesh))
			{
				return candDirectRmesh;
			}

			string candDirectRtex = Path.ChangeExtension(modelPath, ".rtex");
			if (candDirectRtex.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) && File.Exists(candDirectRtex))
			{
				return candDirectRtex;
			}

			return null;
		}

		private static string? TryResolveResUserPath(string modelPath)
		{
			if (!modelPath.StartsWith("res://") && !modelPath.StartsWith("user://")) return null;

			string globalized = ProjectSettings.GlobalizePath(modelPath);
			if (globalized.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(globalized))
			{
				return globalized;
			}

			string globalizedRmesh = Path.ChangeExtension(globalized, ".rmesh");
			if (globalizedRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(globalizedRmesh))
			{
				return globalizedRmesh;
			}

			return null;
		}

		private static string? TryResolveEditingDirectory(string cleanPath, string withRmesh)
		{
			string editingDirectory = GameContext.EditorService._editingDirectory;
			if (string.IsNullOrEmpty(editingDirectory)) return null;

			string candTemp = Path.Combine(editingDirectory, cleanPath);
			if (candTemp.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candTemp)) return candTemp;

			string candTempRmesh = Path.Combine(editingDirectory, withRmesh);
			if (candTempRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candTempRmesh)) return candTempRmesh;
			return null;
		}

		private static string? TryResolveActiveMapPath(string cleanPath, string withRmesh)
		{
			string? activeMap = GetActiveMapName();
			if (string.IsNullOrEmpty(activeMap)) return null;

			if (Directory.Exists(activeMap))
			{
				string candDirect = Path.Combine(activeMap, cleanPath);
				if (CheckRmeshExists(candDirect)) return candDirect;

				string candDirectRmesh2 = Path.Combine(activeMap, withRmesh);
				if (CheckRmeshExists(candDirectRmesh2)) return candDirectRmesh2;
			}

			string mapDir = ProjectSettings.GlobalizePath($"user://maps/{activeMap}");
			string candMap = Path.Combine(mapDir, cleanPath);
			if (CheckRmeshExists(candMap)) return candMap;

			string candMapRmesh = Path.Combine(mapDir, withRmesh);
			if (CheckRmeshExists(candMapRmesh)) return candMapRmesh;

			return null;
		}

		private static string? GetActiveMapName()
		{
			if (GameContext.Host != null && !string.IsNullOrEmpty(GameContext.Simulation.ActiveMapName))
			{
				return GameContext.Simulation.ActiveMapName;
			}

			if (GameContext.Lobby != null && !string.IsNullOrEmpty(GameContext.Lobby.ActiveMapName))
			{
				return GameContext.Lobby.ActiveMapName;
			}

			return null;
		}

		private static bool CheckRmeshExists(string path)
		{
			return path.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
		}

		private static string? TryResolveCurrentMapPath(string cleanPath, string withRmesh)
		{
			string currentMapDir = GameContext.MapStorageService.CurrentMapDirectory;
			if (string.IsNullOrEmpty(currentMapDir) || !Directory.Exists(currentMapDir)) return null;

			string candCur = Path.Combine(currentMapDir, cleanPath);
			if (candCur.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candCur)) return candCur;

			string candCurRmesh = Path.Combine(currentMapDir, withRmesh);
			if (candCurRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candCurRmesh)) return candCurRmesh;
			return null;
		}

		private static string? TryResolveResUserRoot(string cleanPath, string withRmesh)
		{
			string resDir = ProjectSettings.GlobalizePath("res://");
			string candRes = Path.Combine(resDir, cleanPath);
			if (candRes.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candRes)) return candRes;

			string candResRmesh = Path.Combine(resDir, withRmesh);
			if (candResRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candResRmesh)) return candResRmesh;

			string userDir = ProjectSettings.GlobalizePath("user://");
			string candUser = Path.Combine(userDir, cleanPath);
			if (candUser.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candUser)) return candUser;

			string candUserRmesh = Path.Combine(userDir, withRmesh);
			if (candUserRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candUserRmesh)) return candUserRmesh;
			return null;
		}

		private static string? TryResolveInBaseLocations(string cleanPath, string withRmesh)
		{
			string[] subDirs = new[] { "attachments", "items", "projectiles", "weapons", "props", "resources", "units", "buildings" };
			string editingDirectory = GameContext.EditorService._editingDirectory;
			string? activeMap = GetActiveMapName();
			string? currentMapDir = GetCurrentMapDirectory();
			string resDir = ProjectSettings.GlobalizePath("res://");
			string userDir = ProjectSettings.GlobalizePath("user://");

			string?[] baseLocations = new[] { editingDirectory, activeMap, currentMapDir, resDir, userDir };
			foreach (var loc in baseLocations)
			{
				if (string.IsNullOrEmpty(loc)) continue;

				string? result = TryResolveInLocation(loc, cleanPath, withRmesh, subDirs);
				if (result != null) return result;
			}

			return null;
		}

		private static string? GetCurrentMapDirectory()
		{
			if (GameContext.Host != null && !string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory))
			{
				return GameContext.MapStorageService.CurrentMapDirectory;
			}

			return null;
		}

		private static string? TryResolveInLocation(string loc, string cleanPath, string withRmesh, string[] subDirs)
		{
			if (string.IsNullOrEmpty(loc) || !Directory.Exists(loc)) return null;

			foreach (var sub in subDirs)
			{
				string candRmesh = Path.Combine(loc, "Assets", "models", sub, withRmesh);
				if (CheckRmeshExists(candRmesh)) return candRmesh;
			}

			return CheckLocationDecalsAndTextures(loc, cleanPath);
		}

		private static string? CheckLocationDecalsAndTextures(string loc, string cleanPath)
		{
			string candDecalDirect = Path.Combine(loc, "Assets", "decals", cleanPath);
			if (CheckRtexExists(candDecalDirect)) return candDecalDirect;

			string candDecalRtex = Path.Combine(loc, "Assets", "decals", $"{cleanPath}.rtex");
			if (File.Exists(candDecalRtex)) return candDecalRtex;

			string candTexDirect = Path.Combine(loc, "Assets", "textures", cleanPath);
			if (CheckRtexExists(candTexDirect)) return candTexDirect;

			string candTexRtex = Path.Combine(loc, "Assets", "textures", $"{cleanPath}.rtex");
			if (File.Exists(candTexRtex)) return candTexRtex;

			return null;
		}

		private static bool CheckRtexExists(string path)
		{
			return path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
		}

		private static string? TryResolveViaPathUtils(string cleanPath, string withRmesh)
		{
			if (cleanPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				string foundPath = PathUtils.FindPath(cleanPath);
				if (ValidateRmeshPath(foundPath)) return foundPath;
			}

			string foundWithRmesh = PathUtils.FindPath(withRmesh);
			if (ValidateRmeshPath(foundWithRmesh)) return foundWithRmesh;

			string[] subDirs = new[] { "attachments", "items", "projectiles", "weapons", "props", "resources", "units", "buildings" };
			foreach (var sub in subDirs)
			{
				string tPathRmesh = PathUtils.FindPath($"MapTemplate/Assets/models/{sub}/{withRmesh}");
				if (ValidateRmeshPath(tPathRmesh)) return tPathRmesh;

				string rPathRmesh = PathUtils.FindPath($"Assets/models/{sub}/{withRmesh}");
				if (ValidateRmeshPath(rPathRmesh)) return rPathRmesh;
			}

			return null;
		}

		private static bool ValidateRmeshPath(string path)
		{
			return !string.IsNullOrEmpty(path) && path.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
		}
	}
}
