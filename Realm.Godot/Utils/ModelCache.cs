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
				return System.IO.Path.ChangeExtension(cleanPath, ".rmesh");
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
			return null;
		}

		private static string TryResolveResUserPath(string modelPath)
		{
			if (!modelPath.StartsWith("res://") && !modelPath.StartsWith("user://")) return null;

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
			return null;
		}

		private static string TryResolveTempWorkspace(string cleanPath, string withRmesh)
		{
			string tempWs = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			if (string.IsNullOrEmpty(tempWs)) return null;

			string candTemp = System.IO.Path.Combine(tempWs, cleanPath);
			if (candTemp.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candTemp)) return candTemp;

			string candTempRmesh = System.IO.Path.Combine(tempWs, withRmesh);
			if (candTempRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candTempRmesh)) return candTempRmesh;
			return null;
		}

		private static string TryResolveActiveMapPath(string cleanPath, string withRmesh)
		{
			string activeMap = GetActiveMapName();
			if (string.IsNullOrEmpty(activeMap)) return null;

			if (System.IO.Directory.Exists(activeMap))
			{
				string candDirect = System.IO.Path.Combine(activeMap, cleanPath);
				if (CheckRmeshExists(candDirect)) return candDirect;

				string candDirectRmesh2 = System.IO.Path.Combine(activeMap, withRmesh);
				if (CheckRmeshExists(candDirectRmesh2)) return candDirectRmesh2;
			}

			string mapDir = ProjectSettings.GlobalizePath($"user://maps/{activeMap}");
			string candMap = System.IO.Path.Combine(mapDir, cleanPath);
			if (CheckRmeshExists(candMap)) return candMap;

			string candMapRmesh = System.IO.Path.Combine(mapDir, withRmesh);
			if (CheckRmeshExists(candMapRmesh)) return candMapRmesh;
			
			return null;
		}

		private static string GetActiveMapName()
		{
			if (GameHost.Instance != null && !string.IsNullOrEmpty(GameHost.Instance.ActiveMapName))
			{
				return GameHost.Instance.ActiveMapName;
			}

			if (LobbyManager.Instance != null && !string.IsNullOrEmpty(LobbyManager.Instance.ActiveMapName))
			{
				return LobbyManager.Instance.ActiveMapName;
			}

			return null;
		}

		private static bool CheckRmeshExists(string path)
		{
			return path.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(path);
		}

		private static string TryResolveCurrentMapPath(string cleanPath, string withRmesh)
		{
			string currentMapDir = GameHost.Instance?.CurrentMapDirectory;
			if (string.IsNullOrEmpty(currentMapDir) || !System.IO.Directory.Exists(currentMapDir)) return null;

			string candCur = System.IO.Path.Combine(currentMapDir, cleanPath);
			if (candCur.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candCur)) return candCur;

			string candCurRmesh = System.IO.Path.Combine(currentMapDir, withRmesh);
			if (candCurRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candCurRmesh)) return candCurRmesh;
			return null;
		}

		private static string TryResolveResUserRoot(string cleanPath, string withRmesh)
		{
			string resDir = ProjectSettings.GlobalizePath("res://");
			string candRes = System.IO.Path.Combine(resDir, cleanPath);
			if (candRes.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candRes)) return candRes;

			string candResRmesh = System.IO.Path.Combine(resDir, withRmesh);
			if (candResRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candResRmesh)) return candResRmesh;

			string userDir = ProjectSettings.GlobalizePath("user://");
			string candUser = System.IO.Path.Combine(userDir, cleanPath);
			if (candUser.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candUser)) return candUser;

			string candUserRmesh = System.IO.Path.Combine(userDir, withRmesh);
			if (candUserRmesh.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(candUserRmesh)) return candUserRmesh;
			return null;
		}

		private static string TryResolveInBaseLocations(string cleanPath, string withRmesh)
		{
			string[] subDirs = new[] { "attachments", "items", "projectiles", "weapons", "props", "resources", "units", "buildings" };
			string tempWs = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
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
			if (GameHost.Instance != null && !string.IsNullOrEmpty(GameHost.Instance.CurrentMapDirectory))
			{
				return GameHost.Instance.CurrentMapDirectory;
			}
			return null;
		}

		private static string TryResolveInLocation(string loc, string cleanPath, string withRmesh, string[] subDirs)
		{
			if (string.IsNullOrEmpty(loc) || !System.IO.Directory.Exists(loc)) return null;

			foreach (var sub in subDirs)
			{
				string candRmesh = System.IO.Path.Combine(loc, "Assets", "models", sub, withRmesh);
				if (CheckRmeshExists(candRmesh)) return candRmesh;
			}

			return CheckLocationDecalsAndTextures(loc, cleanPath);
		}

		private static string CheckLocationDecalsAndTextures(string loc, string cleanPath)
		{
			string candDecalDirect = System.IO.Path.Combine(loc, "Assets", "decals", cleanPath);
			if (CheckRtexExists(candDecalDirect)) return candDecalDirect;
			
			string candDecalRtex = System.IO.Path.Combine(loc, "Assets", "decals", $"{cleanPath}.rtex");
			if (System.IO.File.Exists(candDecalRtex)) return candDecalRtex;

			string candTexDirect = System.IO.Path.Combine(loc, "Assets", "textures", cleanPath);
			if (CheckRtexExists(candTexDirect)) return candTexDirect;
			
			string candTexRtex = System.IO.Path.Combine(loc, "Assets", "textures", $"{cleanPath}.rtex");
			if (System.IO.File.Exists(candTexRtex)) return candTexRtex;

			return null;
		}

		private static bool CheckRtexExists(string path)
		{
			return path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(path);
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
			return !string.IsNullOrEmpty(path) && path.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(path);
		}

		public static Node GetModel(string modelPath)
		{
			if (string.IsNullOrEmpty(modelPath)) return null;

			if (_cachedScenes.TryGetValue(modelPath, out var cachedScene) && GodotObject.IsInstanceValid(cachedScene))
			{
				return cachedScene.Instantiate();
			}

			string resolvedPath = ResolveModelPath(modelPath);
			if (string.IsNullOrEmpty(resolvedPath))
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

				if (targetPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || targetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || targetPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
				{
					return BuildPrimitiveMeshScene(targetPath);
				}

				if (!targetPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(targetPath))
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
			if (err != Error.Ok) return null;

			Node generatedNode = doc.GenerateScene(state);
			if (generatedNode == null) return null;

			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.ProcessImportedScene(state, generatedNode);
			SetOwnerRecursive(generatedNode, generatedNode);
			
			var packedScene = new PackedScene();
			Error packErr = packedScene.Pack(generatedNode);
			generatedNode.Free();
			
			return packErr == Error.Ok ? packedScene : null;
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

			byte[] rtexBytes = System.IO.File.ReadAllBytes(texturePath);
			var (_, layers, _) = Realm.Shared.Textures.RtexFile.Parse(rtexBytes);
			
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

			SetOwnerRecursive(rootNode, rootNode);
			var packedScene = new PackedScene();
			Error packErr = packedScene.Pack(rootNode);
			rootNode.Free();

			return packErr == Error.Ok ? packedScene : null;
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

		private static Node TryGenerateNodeFromRmesh(string resolvedPath)
		{
			if (string.IsNullOrEmpty(resolvedPath) || !resolvedPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(resolvedPath))
			{
				return null;
			}

			var doc = new GltfDocument();
			var state = new GltfState();
			byte[] rmeshBytes = System.IO.File.ReadAllBytes(resolvedPath);
			byte[] glbBytes = Realm.Shared.ModelOptimization.RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
			
			Error err = doc.AppendFromBuffer(glbBytes, "", state);
			if (err == Error.Ok)
			{
				return doc.GenerateScene(state);
			}

			return null;
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
