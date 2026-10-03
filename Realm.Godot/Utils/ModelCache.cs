using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Godot.Utils
{
	public static class ModelCache
	{
		private static readonly Dictionary<string, PackedScene> _cachedScenes = new(StringComparer.OrdinalIgnoreCase);
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _resolvedModelPaths = new(StringComparer.OrdinalIgnoreCase);

		static ModelCache()
		{
			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.RegisterExtension();
		}

		public static string ResolveModelPath(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return null;

			if (_resolvedModelPaths.TryGetValue(modelPath, out var cachedPath))
			{
				return string.IsNullOrEmpty(cachedPath) ? null : cachedPath;
			}

			string resolved = ResolveModelPathInternal(modelPath);
			_resolvedModelPaths[modelPath] = resolved ?? string.Empty;
			return resolved;
		}

		private static string ResolveModelPathInternal(string modelPath)
		{
			string cleanPath = modelPath.TrimStart('/', '\\');
			string withRmesh = cleanPath;
			if (withRmesh.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || withRmesh.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
			{
				withRmesh = System.IO.Path.ChangeExtension(withRmesh, ".rmesh");
			}
			else if (!withRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && !withRmesh.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				withRmesh = $"{withRmesh}.rmesh";
			}

			if ((modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) && System.IO.File.Exists(modelPath))
			{
				return modelPath;
			}

			string candDirectRmesh = System.IO.Path.ChangeExtension(modelPath, ".rmesh");
			if (candDirectRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candDirectRmesh))
			{
				return candDirectRmesh;
			}

			string candDirectRtex = System.IO.Path.ChangeExtension(modelPath, ".rtex");
			if (candDirectRtex.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candDirectRtex))
			{
				return candDirectRtex;
			}

			if (modelPath.StartsWith("res://") || modelPath.StartsWith("user://"))
			{
				string globalized = ProjectSettings.GlobalizePath(modelPath);
				if (globalized.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(globalized))
				{
					return globalized;
				}
				string globalizedRmesh = System.IO.Path.ChangeExtension(globalized, ".rmesh");
				if (globalizedRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(globalizedRmesh))
				{
					return globalizedRmesh;
				}
			}

			string tempWs = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			if (!string.IsNullOrEmpty(tempWs))
			{
				string candTemp = System.IO.Path.Combine(tempWs, cleanPath);
				if (candTemp.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candTemp))
				{
					return candTemp;
				}
				string candTempRmesh = System.IO.Path.Combine(tempWs, withRmesh);
				if (candTempRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candTempRmesh))
				{
					return candTempRmesh;
				}
			}

			string activeMap = GameHost.Instance?.ActiveMapName ?? LobbyManager.Instance?.ActiveMapName;
			if (!string.IsNullOrEmpty(activeMap))
			{
				if (System.IO.Directory.Exists(activeMap))
				{
					string candDirect = System.IO.Path.Combine(activeMap, cleanPath);
					if (candDirect.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candDirect))
					{
						return candDirect;
					}
					string candDirectRmesh2 = System.IO.Path.Combine(activeMap, withRmesh);
					if (candDirectRmesh2.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candDirectRmesh2))
					{
						return candDirectRmesh2;
					}
				}
				string mapDir = ProjectSettings.GlobalizePath($"user://maps/{activeMap}");
				string candMap = System.IO.Path.Combine(mapDir, cleanPath);
				if (candMap.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candMap))
				{
					return candMap;
				}
				string candMapRmesh = System.IO.Path.Combine(mapDir, withRmesh);
				if (candMapRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candMapRmesh))
				{
					return candMapRmesh;
				}
			}

			string currentMapDir = GameHost.Instance?.CurrentMapDirectory;
			if (!string.IsNullOrEmpty(currentMapDir) && System.IO.Directory.Exists(currentMapDir))
			{
				string candCur = System.IO.Path.Combine(currentMapDir, cleanPath);
				if (candCur.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candCur))
				{
					return candCur;
				}
				string candCurRmesh = System.IO.Path.Combine(currentMapDir, withRmesh);
				if (candCurRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candCurRmesh))
				{
					return candCurRmesh;
				}
			}

			string resDir = ProjectSettings.GlobalizePath("res://");
			string candRes = System.IO.Path.Combine(resDir, cleanPath);
			if (candRes.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candRes))
			{
				return candRes;
			}
			string candResRmesh = System.IO.Path.Combine(resDir, withRmesh);
			if (candResRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candResRmesh))
			{
				return candResRmesh;
			}

			string userDir = ProjectSettings.GlobalizePath("user://");
			string candUser = System.IO.Path.Combine(userDir, cleanPath);
			if (candUser.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candUser))
			{
				return candUser;
			}
			string candUserRmesh = System.IO.Path.Combine(userDir, withRmesh);
			if (candUserRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candUserRmesh))
			{
				return candUserRmesh;
			}

			string[] subDirs = new[] { "attachments", "items", "projectiles", "weapons", "props", "resources", "units", "buildings" };

			string?[] baseLocations = new[] { tempWs, activeMap, currentMapDir, resDir, userDir };
			foreach (var loc in baseLocations)
			{
				if (string.IsNullOrEmpty(loc) || !System.IO.Directory.Exists(loc)) continue;
				foreach (var sub in subDirs)
				{
					string candRmesh = System.IO.Path.Combine(loc, "Assets", "models", sub, withRmesh);
					if (candRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candRmesh)) return candRmesh;
				}
				string candDecalDirect = System.IO.Path.Combine(loc, "Assets", "decals", cleanPath);
				if (candDecalDirect.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candDecalDirect)) return candDecalDirect;
				string candDecalRtex = System.IO.Path.Combine(loc, "Assets", "decals", $"{cleanPath}.rtex");
				if (System.IO.File.Exists(candDecalRtex)) return candDecalRtex;

				string candTexDirect = System.IO.Path.Combine(loc, "Assets", "textures", cleanPath);
				if (candTexDirect.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candTexDirect)) return candTexDirect;
				string candTexRtex = System.IO.Path.Combine(loc, "Assets", "textures", $"{cleanPath}.rtex");
				if (System.IO.File.Exists(candTexRtex)) return candTexRtex;
			}

			if (cleanPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				string foundPath = PathUtils.FindPath(cleanPath);
				if (!string.IsNullOrEmpty(foundPath) && foundPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(foundPath)) return foundPath;
			}

			string foundWithRmesh = PathUtils.FindPath(withRmesh);
			if (!string.IsNullOrEmpty(foundWithRmesh) && foundWithRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(foundWithRmesh)) return foundWithRmesh;

			foreach (var sub in subDirs)
			{
				string tPathRmesh = PathUtils.FindPath($"MapTemplate/Assets/models/{sub}/{withRmesh}");
				if (!string.IsNullOrEmpty(tPathRmesh) && tPathRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(tPathRmesh)) return tPathRmesh;
				string rPathRmesh = PathUtils.FindPath($"Assets/models/{sub}/{withRmesh}");
				if (!string.IsNullOrEmpty(rPathRmesh) && rPathRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(rPathRmesh)) return rPathRmesh;
			}

			return null;
		}

		public static Node GetModel(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return null;

			if (_cachedScenes.TryGetValue(modelPath, out var cachedScene) && GodotObject.IsInstanceValid(cachedScene))
			{
				return cachedScene.Instantiate();
			}

			string resolvedPath = ResolveModelPath(modelPath);
			if (string.IsNullOrEmpty(resolvedPath) || !resolvedPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			if (_cachedScenes.TryGetValue(resolvedPath, out var cachedSceneResolved) && GodotObject.IsInstanceValid(cachedSceneResolved))
			{
				return cachedSceneResolved.Instantiate();
			}

			PackedScene scene = LoadPackedScene(resolvedPath);
			if (scene != null)
			{
				_cachedScenes[modelPath] = scene;
				_cachedScenes[resolvedPath] = scene;
				return scene.Instantiate();
			}

			return null;
		}

		private static PackedScene LoadPackedScene(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return null;

			try
			{
				string targetPath = modelPath;
				if (targetPath.StartsWith("res://") || targetPath.StartsWith("user://"))
				{
					targetPath = ProjectSettings.GlobalizePath(targetPath);
				}

				if (!targetPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}

				if (System.IO.File.Exists(targetPath))
				{
					var doc = new GltfDocument();
					var state = new GltfState();
					byte[] rmeshBytes = System.IO.File.ReadAllBytes(targetPath);
					string? chromaKey = null;
					byte[] glbBytes;
					if (Realm.Shared.ModelOptimization.RmeshFile.IsRmeshBytes(rmeshBytes))
					{
						var (meta, glbPayload, _) = Realm.Shared.ModelOptimization.RmeshFile.Parse(rmeshBytes);
						chromaKey = Realm.Shared.Metadata.RealmMetadataHelper.ExtractChromaKeyFromMetadataJson(meta);
						glbBytes = glbPayload;
					}
					else
					{
						glbBytes = rmeshBytes;
					}

					bool despill = GameHost.Instance != null && GameHost.Instance.GetModelDespillPlayerColor(modelPath);
					if (despill)
					{
						glbBytes = Realm.Shared.GlbInMemoryColorPreprocessor.PreprocessGlbInMemory(glbBytes, chromaKey);
					}
					Error err = doc.AppendFromBuffer(glbBytes, "", state);
					if (err == Error.Ok)
					{
						Node generatedNode = doc.GenerateScene(state);
						if (generatedNode != null)
						{
							Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.ProcessImportedScene(state, generatedNode);
							SetOwnerRecursive(generatedNode, generatedNode);
							var packedScene = new PackedScene();
							Error packErr = packedScene.Pack(generatedNode);
							generatedNode.Free();
							if (packErr == Error.Ok)
							{
								return packedScene;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"ModelCache error loading '{modelPath}': {ex.Message}");
			}
			return null;
		}

		private static void SetOwnerRecursive(Node node, Node owner)
		{
			int childCount = node.GetChildCount();
			for (int i = 0; i < childCount; i++)
			{
				Node child = node.GetChild(i);
				child.Owner = owner;
				SetOwnerRecursive(child, owner);
			}
		}

		public static (float MinY, float YOffset) CalculateModelBounds(string modelPath, float scale = 1.0f)
		{
			if (string.IsNullOrEmpty(modelPath)) return (0f, 0f);

			try
			{
				string resolved = ResolveModelPath(modelPath);
				Node node = null;
				if (!string.IsNullOrEmpty(resolved) && resolved.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(resolved))
				{
					var doc = new GltfDocument();
					var state = new GltfState();
					byte[] rmeshBytes = System.IO.File.ReadAllBytes(resolved);
					byte[] glbBytes = Realm.Shared.ModelOptimization.RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
					Error err = doc.AppendFromBuffer(glbBytes, "", state);
					if (err == Error.Ok)
					{
						node = doc.GenerateScene(state);
					}
				}

				if (node == null)
				{
					node = GetModel(modelPath);
				}

				if (node != null)
				{
					float minY = Unit3D.GetMinY(node, Transform3D.Identity);
					node.Free();

					if (float.IsFinite(minY) && Math.Abs(minY) > 0.0001f)
					{
						float yOffset = (float)Math.Round(-minY * scale, 4);
						return (minY, yOffset);
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ModelCache] CalculateModelBounds error for '{modelPath}': {ex.Message}");
			}

			return (0f, 0f);
		}

		public static void Clear()
		{
			_cachedScenes.Clear();
			_resolvedModelPaths.Clear();
		}

		public static void InvalidateModelPath(string modelPath)
		{
			if (!string.IsNullOrEmpty(modelPath))
			{
				_cachedScenes.Remove(modelPath);
				_resolvedModelPaths.TryRemove(modelPath, out _);
			}
		}
	}
}
