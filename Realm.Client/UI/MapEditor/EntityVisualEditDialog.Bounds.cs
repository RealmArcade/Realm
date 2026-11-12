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
	protected override void OnCancel()
	{
		string targetId = !string.IsNullOrEmpty(_originalTemplateID) ? _originalTemplateID : $"{_objectType}/{_slug}";
		if (!string.IsNullOrEmpty(targetId))
		{
			if (_templateExistedInitially)
			{
				GameContext.EditorService.RevertTemplateJson(targetId, _initialTemplateJson);
			}
			else
			{
				GameContext.EditorService.RevertTemplateJson(targetId, null);
			}
		}

		if (GameContext.Host == null || string.IsNullOrEmpty(_originalTemplateID)) return;

		RestoreStaticRegistryMode();

		GameContext.IEditorAPI.SetModelScale(_originalTemplateID, _initialSnapshot.Scale);
		GameContext.IEditorAPI.SetModelYOffset(_originalTemplateID, _initialSnapshot.YOffset);
		GameContext.IEditorAPI.SetModelCollisionCircleRatio(_originalTemplateID, _initialSnapshot.CollisionCircleRatio);
		GameContext.IEditorAPI.SetModelBrightness(_originalTemplateID, _initialSnapshot.Brightness);
		GameContext.IEditorAPI.SetModelColorTint(_originalTemplateID, _initialSnapshot.ColorTint.R, _initialSnapshot.ColorTint.G, _initialSnapshot.ColorTint.B, _initialSnapshot.ColorTint.A);
		GameContext.IEditorAPI.SetModelNormalizeLuminance(_originalTemplateID, _initialSnapshot.NormalizeLuminance);
		GameContext.IEditorAPI.SetModelIgnorePlayerColor(_originalTemplateID, _initialSnapshot.IgnorePlayerColor);
		GameContext.IEditorAPI.SetModelDespillPlayerColor(_originalTemplateID, _initialSnapshot.DespillPlayerColor);
		GameContext.IEditorAPI.SetModelSpawnShader(_originalTemplateID, _initialSnapshot.SpawnShader);
		GameContext.IEditorAPI.SetModelDeathShader(_originalTemplateID, _initialSnapshot.DeathShader);
		GameContext.IEditorAPI.SetModelEnableProceduralAnimation(_originalTemplateID, _initialSnapshot.EnableProceduralAnimation);
		GameContext.IEditorAPI.SetModelProceduralAnimation(_originalTemplateID, _initialSnapshot.ProceduralAnimation);

		Prop3D.InvalidateModelPathCache(_originalTemplateID);
		ModelCache.InvalidateModelPath(_originalTemplateID);
		if (!string.IsNullOrEmpty(_initialSnapshot.ModelPath))
		{
			Prop3D.InvalidateModelPathCache(_initialSnapshot.ModelPath);
			ModelCache.InvalidateModelPath(_initialSnapshot.ModelPath);
		}

		GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);

		if (_currentSelectedObject != null && GodotObject.IsInstanceValid(_currentSelectedObject))
		{
			if (_currentSelectedObject is Unit3D u3d)
			{
				string fallback = GameContext.Host.GetFallbackModelPath(_initialSnapshot.ModelPath, u3d.IsBuilding);
				u3d.LoadModel(fallback);
			}
			else if (_currentSelectedObject is Prop3D p3d)
			{
				p3d.RefreshPropVisual();
			}
		}

		if (GameContext.Host is { } flushHost)
		{
			flushHost.FlushModelYOffsetSave();
			flushHost.FlushModelCollisionCircleSave();
		}
		UpdateStudioButtonVisibility();
	}

	private void RestoreStaticRegistryMode()
	{
		UpdateStaticRegistryModelAndVisualMode(_category, _originalTemplateID, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);

		string cleanId = Path.GetFileNameWithoutExtension(_originalTemplateID);
		if (!string.IsNullOrEmpty(cleanId) && !cleanId.Equals(_originalTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, cleanId, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
		}

		if (!string.IsNullOrEmpty(_slug))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, $"{_objectType}/{_slug}", _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
			UpdateStaticRegistryModelAndVisualMode(_category, _slug, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
		}

		if (_currentSelectedObject is Unit3D unit && !string.IsNullOrEmpty(unit.UnitId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, unit.UnitId, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
		}
		else if (_currentSelectedObject is Prop3D prop && !string.IsNullOrEmpty(prop.PropId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, prop.PropId, _initialSnapshot.ModelPath, _initialSnapshot.VisualMode);
		}
	}


	private static float CalculateModelLocalMinY(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject))
			return 0f;

		Node3D? visualNode = null;
		if (selectedObject is Unit3D unit && unit.ModelNode != null && GodotObject.IsInstanceValid(unit.ModelNode))
		{
			visualNode = unit.ModelNode;
		}
		else if (selectedObject is Node rootNode)
		{
			visualNode = rootNode.GetNodeOrNull<Node3D>("VisualModel") ?? (selectedObject as Node3D);
		}

		if (visualNode == null)
			return 0f;

		float minY = float.MaxValue;
		bool foundMesh = false;

		CollectModelMinY(visualNode, visualNode, ref minY, ref foundMesh);
		return foundMesh ? minY : 0f;
	}


	private static void CollectModelMinY(Node current, Node3D visualNode, ref float minY, ref bool foundMesh)
	{
		if (current is MeshInstance3D meshInst && meshInst.Mesh != null && meshInst.Visible)
		{
			CheckMeshBounds(meshInst, visualNode, ref minY, ref foundMesh);
		}

		foreach (Node child in current.GetChildren())
		{
			if (child is BoneAttachment3D) continue;
			string cName = child.Name.ToString();
			if (cName.StartsWith("PseudoSocket_", StringComparison.OrdinalIgnoreCase)) continue;
			if (cName.StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase)) continue;
			if (cName.StartsWith("Att_", StringComparison.OrdinalIgnoreCase)) continue;
			if (cName.StartsWith("AttVisual_", StringComparison.OrdinalIgnoreCase)) continue;

			CollectModelMinY(child, visualNode, ref minY, ref foundMesh);
		}
	}

	private static void CheckMeshBounds(MeshInstance3D meshInst, Node3D visualNode, ref float minY, ref bool foundMesh)
	{
		Transform3D relXform = Transform3D.Identity;
		Node curr = meshInst;
		while (curr != null && curr != visualNode)
		{
			if (curr is Node3D n3d)
			{
				relXform = n3d.Transform * relXform;
			}

			curr = curr.GetParent();
		}

		if (Mathf.Abs(relXform.Basis.Determinant()) <= 0.0001f) return;

		Aabb mAabb = meshInst.Mesh.GetAabb();
		Vector3[] corners = Realm.Client.Utils.MathUtils.GetAabbCorners(mAabb);
		for (int i = 0; i < 8; i++)
		{
			Vector3 pt = relXform * corners[i];
			if (pt.Y < minY)
			{
				minY = pt.Y;
				foundMesh = true;
			}
		}
	}
}
