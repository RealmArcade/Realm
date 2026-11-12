// "Can this model be rigged?" - the check behind the animation-studio buttons: mesh presence, the model
// cache, then the scene graph, each answering for one kind of visual mode.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;


namespace Realm.Client.UI.MapEditor;

public partial class EntityVisualEditDialog
{
	private void UpdateStudioButtonVisibility()
	{
		bool isRigged = IsCurrentModelRigged();
		if (_btnOpenRiggedAnimStudio != null) _btnOpenRiggedAnimStudio.Visible = isRigged;
		if (_btnOpenSocketsAndVfx != null) _btnOpenSocketsAndVfx.Visible = HasMeshVisual();
	}

	private bool HasMeshVisual()
	{
		return string.Equals(_visualMode, "Mesh", StringComparison.OrdinalIgnoreCase);
	}


	private bool IsCurrentModelRigged()
	{
		if (!string.Equals(_visualMode, "Mesh", StringComparison.OrdinalIgnoreCase)) return false;

		string meshPath = ResolveMeshPathForRigging();

		if (!string.IsNullOrEmpty(meshPath) && TryCheckModelCacheForRigging(meshPath, out bool isRigged))
		{
			return isRigged;
		}

		return CheckSceneGraphForRigging();
	}

	private string ResolveMeshPathForRigging()
	{
		string meshPath = !string.IsNullOrEmpty(_modelPath) ? _modelPath : "";
		if (string.IsNullOrEmpty(meshPath) && _currentSelectedObject is Prop3D prop)
		{
			meshPath = prop.ModelAssetPath;
		}

		if (string.IsNullOrEmpty(meshPath) && GameContext.Host != null && !string.IsNullOrEmpty(_originalTemplateID))
		{
			meshPath = GameContext.Host.GetModelAssetKey(_originalTemplateID) ?? "";
		}

		return meshPath;
	}

	private bool TryCheckModelCacheForRigging(string meshPath, out bool isRigged)
	{
		isRigged = false;
		Node? loadedNode = ModelCache.GetModel(meshPath);
		if (loadedNode == null)
		{
			string? resolved = ModelCache.ResolveModelPath(meshPath);
			if (!string.IsNullOrEmpty(resolved)) loadedNode = ModelCache.GetModel(resolved);
		}

		if (loadedNode != null)
		{
			try
			{
				isRigged = SkeletonValidator.FindSkeleton(loadedNode) != null;
				return true;
			}
			finally
			{
				loadedNode.QueueFree();
			}
		}

		return false;
	}

	private bool CheckSceneGraphForRigging()
	{
		if (_currentSelectedObject == null || !GodotObject.IsInstanceValid(_currentSelectedObject)) return false;

		Node modelRoot = _currentSelectedObject;
		if (_currentSelectedObject is Unit3D u && u.ModelNode != null)
		{
			modelRoot = u.ModelNode;
		}
		else if (_currentSelectedObject is Prop3D p)
		{
			modelRoot = p.GetNodeOrNull<Node3D>("VisualModel") ?? _currentSelectedObject;
		}

		return SkeletonValidator.FindSkeleton(modelRoot) != null;
	}
}
