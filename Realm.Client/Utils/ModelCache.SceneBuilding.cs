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
		private static PackedScene? LoadPackedScene(string modelPath)
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
				GameLog.LogError($"ModelCache error loading '{modelPath}': {ex.Message}");
			}

			return null;
		}

		private static PackedScene? LoadGltfScene(string targetPath, string modelPath)
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

			Node? generatedNode = null;
			try
			{
				generatedNode = doc.GenerateScene(state);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[ModelCache] GenerateScene failed for '{modelPath}': {ex.Message}");
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

		private static PackedScene? BuildPrimitiveMeshScene(string texturePath)
		{
			try
			{
				Texture2D? albedoTex = null;
				Texture2D? normalTex = null;

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
				GameLog.LogError($"[ModelCache] Error building primitive mesh scene for '{texturePath}': {ex.Message}");
			}

			return null;
		}

		private static void TryLoadRtexTextures(string texturePath, out Texture2D? albedoTex, out Texture2D? normalTex)
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

		private static Texture2D? CreateTextureFromLayer(byte[] layerData)
		{
			var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
			if (img.LoadWebpFromBuffer(layerData) == Error.Ok || img.LoadPngFromBuffer(layerData) == Error.Ok)
			{
				if (!img.HasMipmaps()) img.GenerateMipmaps();
				return ImageTexture.CreateFromImage(img);
			}

			return null;
		}

		private static Texture2D? TryLoadStandardTexture(string texturePath)
		{
			var img = Image.LoadFromFile(texturePath);
			if (img == null) return null;

			if (!img.HasMipmaps()) img.GenerateMipmaps();
			return ImageTexture.CreateFromImage(img);
		}

		private static PackedScene? CreatePrimitiveMeshNode(Texture2D? albedoTex, Texture2D? normalTex)
		{
			if (albedoTex == null) return null;

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

			// The albedo texture is the whole point of a ground plane, so the plane carries it on its own surface as well
			// as on the instance: a material refresh that reaches either level then still has a texture to keep.
			planeMesh.Material = mat;
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
	}
}
