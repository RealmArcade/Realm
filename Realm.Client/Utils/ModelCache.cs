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
		private static readonly ReaderWriterLockSlim _modelCacheLock = new();
		private static readonly Dictionary<string, PackedScene> _cachedScenes = new(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> _failedModelPaths = new(StringComparer.OrdinalIgnoreCase);
		private static readonly ConcurrentDictionary<string, string> _resolvedModelPaths = new(StringComparer.OrdinalIgnoreCase);

		static ModelCache()
		{
			GltfDocumentExtensionMsftLod.RegisterExtension();
		}

		/// <summary>
		///     The lift that puts a model's lowest point on the ground at the scale a template draws it. The offset is in world
		///     metres while the model is scaled about its own origin, so a template drawn larger needs a larger lift: measuring
		///     a re-imported asset at the importing category's default scale instead left every model whose scale had been
		///     changed since it was first imported sunk into the terrain.
		/// </summary>
		public static float RestingOffsetFor(float minY, float scale)
		{
			if (!float.IsFinite(minY) || Math.Abs(minY) <= 0.0001f) return 0f;
			return (float)Math.Round(-minY * (scale <= 0.001f ? 1f : scale), 4);
		}

		public static (float MinY, float YOffset) CalculateModelBounds(string modelPath, float scale = 1.0f)
		{
			if (string.IsNullOrEmpty(modelPath)) return (0f, 0f);

			try
			{
				string? resolved = ResolveModelPath(modelPath);
				Node? node = resolved != null ? TryGenerateNodeFromRmesh(resolved) : null;

				if (node == null)
				{
					node = GetModel(modelPath);
				}

				if (node != null)
				{
					try
					{
						float minY = Unit3D.GetMinY(node, Transform3D.Identity);
						if (float.IsFinite(minY) && Math.Abs(minY) > 0.0001f)
						{
							float yOffset = (float)Math.Round(-minY * scale, 4);
							return (minY, yOffset);
						}
					}
					finally
					{
						node.Free();
					}
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[ModelCache] CalculateModelBounds error for '{modelPath}': {ex.Message}");
			}

			return (0f, 0f);
		}

		public static Node? TryGenerateNodeFromRmeshOrGlbBytes(byte[] rmeshBytes)
		{
			var doc = new GltfDocument();
			var state = new GltfState();
			byte[] glbBytes = RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;

			Error err = doc.AppendFromBuffer(glbBytes, "", state);
			if (err == Error.Ok)
			{
				try
				{
					return doc.GenerateScene(state);
				}
				catch (Exception ex)
				{
					GameLog.LogError($"[ModelCache] GenerateScene failed: {ex.Message}");
					return null;
				}
			}

			return null;
		}

		private static Node? TryGenerateNodeFromRmesh(string resolvedPath)
		{
			if (string.IsNullOrEmpty(resolvedPath) || !resolvedPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || !File.Exists(resolvedPath))
			{
				return null;
			}

			byte[] rmeshBytes = File.ReadAllBytes(resolvedPath);
			return TryGenerateNodeFromRmeshOrGlbBytes(rmeshBytes);
		}

	}
}
