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
	public static class ModelCache
	{
		private static readonly ReaderWriterLockSlim _modelCacheLock = new();
		private static readonly Dictionary<string, PackedScene> _cachedScenes = new(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> _failedModelPaths = new(StringComparer.OrdinalIgnoreCase);
		private static readonly ConcurrentDictionary<string, string> _resolvedModelPaths = new(StringComparer.OrdinalIgnoreCase);

		static ModelCache()
		{
			GltfDocumentExtensionMsftLod.RegisterExtension();
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
			string withRmesh = GetPathWithRmeshExtension(cleanPath);

			string result = TryResolveDirectPath(modelPath);
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

		private static string TryResolveAdditionalPaths(string cleanPath, string withRmesh)
		{
			string result = TryResolveTempWorkspace(cleanPath, withRmesh);
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

		private static string TryResolveDirectPath(string modelPath)
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

		private static string TryResolveResUserPath(string modelPath)
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

		private static string TryResolveTempWorkspace(string cleanPath, string withRmesh)
		{
			string tempWs = ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);
			if (string.IsNullOrEmpty(tempWs)) return null;

			string candTemp = Path.Combine(tempWs, cleanPath);
			if (candTemp.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candTemp)) return candTemp;

			string candTempRmesh = Path.Combine(tempWs, withRmesh);
			if (candTempRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candTempRmesh)) return candTempRmesh;
			return null;
		}

		private static string TryResolveActiveMapPath(string cleanPath, string withRmesh)
		{
			string activeMap = GetActiveMapName();
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

		private static string GetActiveMapName()
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

		private static string TryResolveCurrentMapPath(string cleanPath, string withRmesh)
		{
			string currentMapDir = GameContext.MapStorageService.CurrentMapDirectory;
			if (string.IsNullOrEmpty(currentMapDir) || !Directory.Exists(currentMapDir)) return null;

			string candCur = Path.Combine(currentMapDir, cleanPath);
			if (candCur.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candCur)) return candCur;

			string candCurRmesh = Path.Combine(currentMapDir, withRmesh);
			if (candCurRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && File.Exists(candCurRmesh)) return candCurRmesh;
			return null;
		}

		private static string TryResolveResUserRoot(string cleanPath, string withRmesh)
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

		private static string TryResolveInBaseLocations(string cleanPath, string withRmesh)
		{
			string[] subDirs = new[] { "attachments", "items", "projectiles", "weapons", "props", "resources", "units", "buildings" };
			string tempWs = ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);
			string activeMap = GetActiveMapName();
			string currentMapDir = GetCurrentMapDirectory();
			string resDir = ProjectSettings.GlobalizePath("res://");
			string userDir = ProjectSettings.GlobalizePath("user://");

			string[] baseLocations = new[] { tempWs, activeMap, currentMapDir, resDir, userDir };
			foreach (var loc in baseLocations)
			{
				string result = TryResolveInLocation(loc, cleanPath, withRmesh, subDirs);
				if (result != null) return result;
			}

			return null;
		}

		private static string GetCurrentMapDirectory()
		{
			if (GameContext.Host != null && !string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory))
			{
				return GameContext.MapStorageService.CurrentMapDirectory;
			}

			return null;
		}

		private static string TryResolveInLocation(string loc, string cleanPath, string withRmesh, string[] subDirs)
		{
			if (string.IsNullOrEmpty(loc) || !Directory.Exists(loc)) return null;

			foreach (var sub in subDirs)
			{
				string candRmesh = Path.Combine(loc, "Assets", "models", sub, withRmesh);
				if (CheckRmeshExists(candRmesh)) return candRmesh;
			}

			return CheckLocationDecalsAndTextures(loc, cleanPath);
		}

		private static string CheckLocationDecalsAndTextures(string loc, string cleanPath)
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

		private static string TryResolveViaPathUtils(string cleanPath, string withRmesh)
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

		public static Node GetModel(string modelPath)
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

			PackedScene scene = LoadPackedScene(resolvedPath);
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

			PackedScene scene = LoadPackedScene(resolvedPath);
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

				if (targetPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || targetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || targetPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
				{
					return BuildPrimitiveMeshScene(targetPath);
				}

				if (!targetPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || !File.Exists(targetPath))
				{
					return null;
				}

				return LoadGltfScene(targetPath, modelPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"ModelCache error loading '{modelPath}': {ex.Message}");
			}

			return null;
		}

		private static PackedScene LoadGltfScene(string targetPath, string modelPath)
		{
			var doc = new GltfDocument();
			var state = new GltfState();
			byte[] rmeshBytes = File.ReadAllBytes(targetPath);
			string? chromaKey = null;
			byte[] glbBytes;

			if (RmeshFile.IsRmeshBytes(rmeshBytes))
			{
				var (meta, glbPayload, _) = RmeshFile.Parse(rmeshBytes);
				chromaKey = RealmMetadataHelper.ExtractChromaKeyFromMetadataJson(meta);
				glbBytes = glbPayload;
			}
			else
			{
				glbBytes = rmeshBytes;
			}

			bool despill = GameContext.Host != null && GameContext.Host.GetModelDespillPlayerColor(modelPath);
			if (despill)
			{
				glbBytes = GlbInMemoryColorPreprocessor.PreprocessGlbInMemory(glbBytes, chromaKey);
			}

			Error err = doc.AppendFromBuffer(glbBytes, "", state);
			if (err != Error.Ok) return null;

			Node generatedNode = null;
			try
			{
				generatedNode = doc.GenerateScene(state);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ModelCache] GenerateScene failed for '{modelPath}': {ex.Message}");
				return null;
			}

			if (generatedNode == null) return null;

			try
			{
				SetOwnerRecursive(generatedNode, generatedNode);

				var packedScene = new PackedScene();
				Error packErr = packedScene.Pack(generatedNode);
				return packErr == Error.Ok ? packedScene : null;
			}
			finally
			{
				generatedNode.Free();
			}
		}

		private static PackedScene BuildPrimitiveMeshScene(string texturePath)
		{
			try
			{
				Texture2D albedoTex = null;
				Texture2D normalTex = null;

				if (texturePath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
				{
					TryLoadRtexTextures(texturePath, out albedoTex, out normalTex);
				}
				else
				{
					albedoTex = TryLoadStandardTexture(texturePath);
				}

				if (albedoTex == null) return null;

				return CreatePrimitiveMeshNode(albedoTex, normalTex);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ModelCache] Error building primitive mesh scene for '{texturePath}': {ex.Message}");
			}

			return null;
		}

		private static void TryLoadRtexTextures(string texturePath, out Texture2D albedoTex, out Texture2D normalTex)
		{
			albedoTex = null;
			normalTex = null;

			byte[] rtexBytes = File.ReadAllBytes(texturePath);
			var (_, layers, _) = RtexFile.Parse(rtexBytes);

			if (layers.Count > 0 && layers[0] != null && layers[0].Length > 0)
			{
				albedoTex = CreateTextureFromLayer(layers[0]);
			}

			if (layers.Count > 1 && layers[1] != null && layers[1].Length > 0)
			{
				normalTex = CreateTextureFromLayer(layers[1]);
			}
		}

		private static Texture2D CreateTextureFromLayer(byte[] layerData)
		{
			var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
			if (img.LoadWebpFromBuffer(layerData) == Error.Ok || img.LoadPngFromBuffer(layerData) == Error.Ok)
			{
				if (!img.HasMipmaps()) img.GenerateMipmaps();
				return ImageTexture.CreateFromImage(img);
			}

			return null;
		}

		private static Texture2D TryLoadStandardTexture(string texturePath)
		{
			var img = Image.LoadFromFile(texturePath);
			if (img == null) return null;

			if (!img.HasMipmaps()) img.GenerateMipmaps();
			return ImageTexture.CreateFromImage(img);
		}

		private static PackedScene CreatePrimitiveMeshNode(Texture2D albedoTex, Texture2D normalTex)
		{
			var rootNode = new Node3D { Name = "VisualModel" };
			var meshInstance = new MeshInstance3D { Name = "Mesh" };

			var planeMesh = new PlaneMesh
			{
				Size = new Vector2(2.0f, 2.0f),
				SubdivideWidth = 4,
				SubdivideDepth = 4,
				Orientation = PlaneMesh.OrientationEnum.Y
			};
			meshInstance.Mesh = planeMesh;

			var mat = new ShaderMaterial
			{
				Shader = ModelShaderManager.GetOrCreateShader()
			};
			mat.SetShaderParameter("texture_albedo", albedoTex);
			mat.SetShaderParameter("use_alpha_blend", true);
			mat.SetShaderParameter("albedo_color", new Color(1.0f, 1.0f, 1.0f, 1.0f));
			mat.SetShaderParameter("roughness_value", 1.0f);
			mat.SetShaderParameter("specular_value", 0.5f);

			if (normalTex != null)
			{
				mat.SetShaderParameter("texture_normal", normalTex);
				mat.SetShaderParameter("has_normal_texture", true);
			}

			meshInstance.MaterialOverride = mat;
			rootNode.AddChild(meshInstance);

			try
			{
				SetOwnerRecursive(rootNode, rootNode);
				var packedScene = new PackedScene();
				Error packErr = packedScene.Pack(rootNode);
				return packErr == Error.Ok ? packedScene : null;
			}
			finally
			{
				rootNode.Free();
			}
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
				Node node = TryGenerateNodeFromRmesh(resolved);

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
				GD.PrintErr($"[ModelCache] CalculateModelBounds error for '{modelPath}': {ex.Message}");
			}

			return (0f, 0f);
		}

		public static Node TryGenerateNodeFromRmeshOrGlbBytes(byte[] rmeshBytes)
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
					GD.PrintErr($"[ModelCache] GenerateScene failed: {ex.Message}");
					return null;
				}
			}

			return null;
		}

		private static Node TryGenerateNodeFromRmesh(string resolvedPath)
		{
			if (string.IsNullOrEmpty(resolvedPath) || !resolvedPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || !File.Exists(resolvedPath))
			{
				return null;
			}

			byte[] rmeshBytes = File.ReadAllBytes(resolvedPath);
			return TryGenerateNodeFromRmeshOrGlbBytes(rmeshBytes);
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