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
		public static Node? GetModel(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return null;

			string? resolvedPath = ResolveModelPath(modelPath);
			string canonicalKey = BuildCanonicalKey(resolvedPath ?? modelPath);

			_modelCacheLock.EnterReadLock();
			try
			{
				if (_failedModelPaths.Contains(canonicalKey)) return null;

				if (_cachedScenes.TryGetValue(canonicalKey, out var cachedScene) && GodotObject.IsInstanceValid(cachedScene))
				{
					return cachedScene.Instantiate();
				}
			}
			finally
			{
				_modelCacheLock.ExitReadLock();
			}

			if (string.IsNullOrEmpty(resolvedPath))
			{
				return null;
			}

			PackedScene? scene = LoadPackedScene(resolvedPath);
			_modelCacheLock.EnterWriteLock();
			try
			{
				if (scene != null)
				{
					_cachedScenes[canonicalKey] = scene;
					return scene.Instantiate();
				}

				_failedModelPaths.Add(canonicalKey);
				return null;
			}
			finally
			{
				_modelCacheLock.ExitWriteLock();
			}
		}

		public static void ReleaseModel(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return;

			string? resolvedPath = ResolveModelPath(modelPath);
			string canonicalKey = BuildCanonicalKey(resolvedPath ?? modelPath);

			_modelCacheLock.EnterWriteLock();
			try
			{
				if (_cachedScenes.Remove(canonicalKey, out PackedScene? cachedScene))
				{
					cachedScene.Dispose();
				}

				_failedModelPaths.Remove(canonicalKey);
			}
			finally
			{
				_modelCacheLock.ExitWriteLock();
			}
		}

		public static void PreloadModel(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return;

			string? resolvedPath = ResolveModelPath(modelPath);
			string canonicalKey = BuildCanonicalKey(resolvedPath ?? modelPath);

			_modelCacheLock.EnterReadLock();
			try
			{
				if (_failedModelPaths.Contains(canonicalKey)) return;
				if (_cachedScenes.TryGetValue(canonicalKey, out var cachedScene) && GodotObject.IsInstanceValid(cachedScene)) return;
			}
			finally
			{
				_modelCacheLock.ExitReadLock();
			}

			if (string.IsNullOrEmpty(resolvedPath)) return;

			PackedScene? scene = LoadPackedScene(resolvedPath);
			_modelCacheLock.EnterWriteLock();
			try
			{
				if (scene != null)
				{
					_cachedScenes[canonicalKey] = scene;
				}
				else
				{
					_failedModelPaths.Add(canonicalKey);
				}
			}
			finally
			{
				_modelCacheLock.ExitWriteLock();
			}
		}

		private static string BuildCanonicalKey(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return string.Empty;

			string absolutePath = modelPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
			                      modelPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
				? ProjectSettings.GlobalizePath(modelPath)
				: modelPath;

			try
			{
				return Path.GetFullPath(absolutePath).Replace('\\', '/').TrimEnd('/');
			}
			catch (Exception)
			{
				return absolutePath.Replace('\\', '/').TrimEnd('/');
			}
		}

		public static void Clear()
		{
			_modelCacheLock.EnterWriteLock();
			try
			{
				_cachedScenes.Clear();
				_failedModelPaths.Clear();
				_resolvedModelPaths.Clear();
			}
			finally
			{
				_modelCacheLock.ExitWriteLock();
			}
		}

		public static void InvalidateModelPath(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return;

			_resolvedModelPaths.TryRemove(modelPath, out string? resolvedPath);
			string canonicalKey = BuildCanonicalKey(string.IsNullOrEmpty(resolvedPath) ? modelPath : resolvedPath);

			_modelCacheLock.EnterWriteLock();
			try
			{
				_cachedScenes.Remove(canonicalKey);
				_failedModelPaths.Remove(canonicalKey);
			}
			finally
			{
				_modelCacheLock.ExitWriteLock();
			}
		}
	}
}
