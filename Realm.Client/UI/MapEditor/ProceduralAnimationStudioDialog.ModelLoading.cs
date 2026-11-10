// Preview model instancing. A template names an asset; this resolves it to disk, decides whether
// it is a 2D card or a mesh, and seats the result under the preview root with the studio material.

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Terrain;


namespace Realm.Client.UI.MapEditor;

public partial class ProceduralAnimationStudioDialog
{
	private void Setup3DEnvironment()
	{
		_simRoot = new Node3D();
		_simRoot.Name = "SimRoot";
		PreviewSubViewport.AddChild(_simRoot);

		_currentModelRoot = new Node3D();
		_currentModelRoot.Name = "ModelRoot";
		_simRoot.AddChild(_currentModelRoot);

		UpdateCameraTransform();
	}

	private void LoadPreviewTemplate(TemplateItemInfo template)
	{
		if (_currentModelRoot == null) return;
		ClearCurrentModelRoot();

		if (template == null) return;

		if (template.Id.StartsWith("(") || template.Category.Equals("Sample", StringComparison.OrdinalIgnoreCase))
		{
			LoadSampleTemplate(template);
			return;
		}

		Node3D? loadedNode3D = LoadTemplateModel(template);

		if (loadedNode3D != null)
		{
			SetupLoadedModel(loadedNode3D, template);
		}

		UpdateRegionControls();
		UpdateShaderParameters();
	}

	private void ClearCurrentModelRoot()
	{
		foreach (Node child in _currentModelRoot.GetChildren())
		{
			_currentModelRoot.RemoveChild(child);
			child.QueueFree();
		}
	}

	private void LoadSampleTemplate(TemplateItemInfo template)
	{
		var meshInst = new MeshInstance3D();
		meshInst.Name = "MeshInst_MeshInstance3D";
		if (template.Id.Contains("Capsule"))
		{
			meshInst.Mesh = new CapsuleMesh { Radius = 0.5f, Height = 1.8f };
			meshInst.Position = new Vector3(0, 0.9f, 0);
		}
		else
		{
			meshInst.Mesh = new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.6f, Height = 3.0f };
			meshInst.Position = new Vector3(0, 1.5f, 0);
		}

		var mat = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.65f, 0.45f) };
		meshInst.MaterialOverride = mat;
		_currentModelRoot.AddChild(meshInst);
		ModelShaderManager.RefreshShaderMaterialsForNode(_currentModelRoot, false);
		CenterAndFrameNode(_currentModelRoot);
		UpdateShaderParameters();
	}

	private Node3D? LoadTemplateModel(TemplateItemInfo template)
	{
		string assetPath = !string.IsNullOrEmpty(template.ModelPath) ? template.ModelPath : template.Id;
		string visualMode = !string.IsNullOrEmpty(template.VisualMode) ? template.VisualMode : (assetPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? "GroundPlane" : "Mesh");

		bool is2DTexture = string.Equals(visualMode, "GroundPlane", StringComparison.OrdinalIgnoreCase) ||
		                   string.Equals(visualMode, "SlopeAlignedQuad", StringComparison.OrdinalIgnoreCase) ||
		                   assetPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
		                   assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
		                   assetPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

		if (is2DTexture)
		{
			return Load2DTextureModel(assetPath, visualMode);
		}

		return Load3DModel(assetPath);
	}

	private Node3D? Load2DTextureModel(string assetPath, string visualMode)
	{
		Node3D? loadedNode3D = null;
		var loaded = ModelCache.GetModel(assetPath);
		if (loaded is Node3D n)
		{
			loadedNode3D = n;
		}
		else
		{
			loaded?.QueueFree();
			string? resolved = ModelCache.ResolveModelPath(assetPath);
			if (!string.IsNullOrEmpty(resolved))
			{
				loaded = ModelCache.GetModel(resolved);
				if (loaded is Node3D nResolved)
				{
					loadedNode3D = nResolved;
				}
				else
				{
					loaded?.QueueFree();
				}
			}
		}

		if (loadedNode3D != null && string.Equals(visualMode, "SlopeAlignedQuad", StringComparison.OrdinalIgnoreCase))
		{
			loadedNode3D.RotationDegrees = new Vector3(45f, 0f, 0f);
		}

		return loadedNode3D;
	}

	private Node3D? Load3DModel(string assetPath)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string? resolvedDiskPath = ResolveDiskPath(wsPath, assetPath);

		Node3D? loadedNode3D = null;
		if (resolvedDiskPath != null && File.Exists(resolvedDiskPath))
		{
			loadedNode3D = LoadFromDiskPath(resolvedDiskPath, assetPath);
		}
		else if (!string.IsNullOrEmpty(assetPath))
		{
			var loaded = ModelCache.GetModel(assetPath);
			if (loaded is Node3D n)
			{
				loadedNode3D = n;
			}
			else
			{
				loaded?.QueueFree();
			}
		}

		if (loadedNode3D != null)
		{
			AnimationRetargetingService.TryApplyRiggedIdlePose(loadedNode3D, resolvedDiskPath ?? assetPath);
		}

		return loadedNode3D;
	}

	private string? ResolveDiskPath(string wsPath, string assetPath)
	{
		foreach (var sub in new[] { "props", "units", "buildings", "resources", "characters", "items", "projectiles", "attachments" })
		{
			string p = Path.Combine(wsPath, "Assets", "models", sub, assetPath);
			if (File.Exists(p)) return p;
			p = Path.Combine("MapTemplate", "Assets", "models", sub, assetPath);
			if (File.Exists(p)) return p;
		}

		string modelsDir = Path.Combine(wsPath, "Assets", "models");
		if (Directory.Exists(modelsDir))
		{
			var files = Directory.GetFiles(modelsDir, assetPath, SearchOption.AllDirectories);
			if (files.Length > 0 && files[0].EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				return files[0];
			}
		}

		return null;
	}

	private Node3D? LoadFromDiskPath(string resolvedDiskPath, string assetPath)
	{
		var loaded = ModelCache.GetModel(resolvedDiskPath) ?? ModelCache.GetModel(assetPath);
		if (loaded is Node3D n) return n;
		if (loaded != null) loaded.QueueFree();

		var gltfDoc = new GltfDocument();
		var gltfState = new GltfState();
		byte[] rmeshBytes = File.ReadAllBytes(resolvedDiskPath);
		byte[] glbBytes = RmeshFile.GetGlbBytes(rmeshBytes) ?? rmeshBytes;
		if (gltfDoc.AppendFromBuffer(glbBytes, "", gltfState) == Error.Ok)
		{
			var node = gltfDoc.GenerateScene(gltfState);
			if (node is Node3D n3d) return n3d;
			if (node != null) node.QueueFree();
		}

		return null;
	}

	private void SetupLoadedModel(Node3D loadedNode3D, TemplateItemInfo template)
	{
		_currentModelRoot.AddChild(loadedNode3D);
		ModelShaderManager.RefreshShaderMaterialsForNode(loadedNode3D, false);

		if (GameContext.Host != null && !string.IsNullOrEmpty(template.Id))
		{
			float scale = GameContext.Host.GetModelScale(template.Id);
			if (scale > 0.01f)
			{
				loadedNode3D.Scale = new Vector3(scale, scale, scale);
			}

			float yOffset = GameContext.Host.GetModelYOffset(template.Id);
			loadedNode3D.Position = new Vector3(0, yOffset, 0);
		}

		CenterAndFrameNode(loadedNode3D);
	}

}