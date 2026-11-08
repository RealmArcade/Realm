using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Realm.Client.Utils;

public static partial class ModelShaderManager
{
	public static ShaderMaterial GetOrCreateShaderMaterial(Material? sourceMaterial, bool normalizeLuminance = true)
	{
		var shader = GetOrCreateShader();

		MaterialParameters p = ExtractMaterialParameters(sourceMaterial);

		Texture2D? albedoTexture = normalizeLuminance ? GetOrCreateNormalizedAlbedoTexture(p.RawAlbedoTexture) : p.RawAlbedoTexture;
		bool hasPlayerMask = CheckHasPlayerMask(p.OrmTexture, sourceMaterial);

		ulong rawAlbedoId = p.RawAlbedoTexture != null ? p.RawAlbedoTexture.GetInstanceId() : 0;
		ulong ormId = p.OrmTexture != null ? p.OrmTexture.GetInstanceId() : 0;
		ulong normalId = p.NormalTexture != null ? p.NormalTexture.GetInstanceId() : 0;
		ulong emissionId = p.EmissionTexture != null ? p.EmissionTexture.GetInstanceId() : 0;

		string key = $"{rawAlbedoId}_{normalizeLuminance}_{ormId}_{hasPlayerMask}_{normalId}_{emissionId}_{p.AlbedoColor.ToHtml()}_{p.EmissionColor.ToHtml()}_{p.EmissionEnergy:F2}_{p.Roughness:F2}_{p.Metallic:F2}_{p.Specular:F2}_{p.Uv1Scale.X:F2}_{p.Uv1Scale.Y:F2}_{p.Uv1Offset.X:F2}_{p.Uv1Offset.Y:F2}_{p.UseAlphaBlend}_{p.UseAlphaScissor}_{p.AlphaScissorThreshold:F2}";

		if (_materialCache.TryGetValue(key, out var cached) && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}

		var material = new ShaderMaterial
		{
			Shader = shader
		};

		ApplyMaterialParametersToShader(material, p, albedoTexture, hasPlayerMask);

		_materialCache[key] = material;
		return material;
	}

	public static bool IsAttachmentNode(Node node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return false;
		if (node.HasMeta("AttachmentId") || node.HasMeta("CleanAttachmentId")) return true;
		string nodeName = node.Name.ToString();
		return nodeName.StartsWith("Att_", StringComparison.OrdinalIgnoreCase)
		       || nodeName.StartsWith("AttVisual_", StringComparison.OrdinalIgnoreCase)
		       || nodeName.StartsWith("BoneAttachment_", StringComparison.OrdinalIgnoreCase)
		       || nodeName.StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsExcludedMesh(GeometryInstance3D geomInst)
	{
		if (geomInst == null) return true;
		string nodeName = geomInst.Name.ToString();
		return nodeName.StartsWith("_selection", StringComparison.OrdinalIgnoreCase)
		       || nodeName.StartsWith("Selection", StringComparison.OrdinalIgnoreCase)
		       || nodeName.StartsWith("_hover", StringComparison.OrdinalIgnoreCase)
		       || nodeName.StartsWith("Hover", StringComparison.OrdinalIgnoreCase)
		       || nodeName.StartsWith("BrushIndicator", StringComparison.OrdinalIgnoreCase)
		       || nodeName.StartsWith("DropShadow", StringComparison.OrdinalIgnoreCase)
		       || nodeName.Contains("SelectionRing", StringComparison.OrdinalIgnoreCase)
		       || nodeName.Contains("HoverRing", StringComparison.OrdinalIgnoreCase);
	}

	public static void ApplyPlayerColorShader(Node rootNode, Color playerColor, bool ignorePlayerColor = false, bool normalizeLuminance = true, bool isUnitOrBuilding = true)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		ApplyPlayerColorShaderRecursive(rootNode, playerColor, ignorePlayerColor, normalizeLuminance, isUnitOrBuilding);
	}

	private static void ApplyPlayerColorShaderToMesh(MeshInstance3D meshInst, Color playerColor, bool ignorePlayerColor, bool normalizeLuminance, bool isUnitOrBuilding)
	{
		if (IsExcludedMesh(meshInst)) return;

		ApplyPlayerColorShaderToSurfaces(meshInst, normalizeLuminance);
		ApplyPlayerColorShaderToOverrideMaterial(meshInst, normalizeLuminance);

		meshInst.SetInstanceShaderParameter(_paramPlayerColor, playerColor);
		meshInst.SetInstanceShaderParameter(_paramIgnorePlayerColor, ignorePlayerColor ? 1.0f : 0.0f);
		meshInst.SetInstanceShaderParameter(_paramUnitAmbientBoost, isUnitOrBuilding ? 0.10f : 0.0f);
		meshInst.SetInstanceShaderParameter(_paramUnitRimIntensity, isUnitOrBuilding ? 0.25f : 0.0f);
	}

	private static void ApplyPlayerColorShaderToSurfaces(MeshInstance3D meshInst, bool normalizeLuminance)
	{
		int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 1;
		for (int i = 0; i < surfaceCount; i++)
		{
			Material? srcMat = meshInst.GetSurfaceOverrideMaterial(i);
			if (srcMat == null && meshInst.Mesh != null)
			{
				srcMat = meshInst.Mesh.SurfaceGetMaterial(i);
			}

			if (srcMat is ShaderMaterial sm && sm.Shader == _sharedShader) continue;

			if (srcMat is BaseMaterial3D || srcMat is ShaderMaterial || srcMat == null)
			{
				var shaderMat = GetOrCreateShaderMaterial(srcMat, normalizeLuminance);
				meshInst.SetSurfaceOverrideMaterial(i, shaderMat);
			}
		}
	}

	private static void ApplyPlayerColorShaderToOverrideMaterial(MeshInstance3D meshInst, bool normalizeLuminance)
	{
		if (meshInst.MaterialOverride == null) return;
		if (meshInst.MaterialOverride is ShaderMaterial smOver && smOver.Shader == _sharedShader) return;

		var shaderMat = GetOrCreateShaderMaterial(meshInst.MaterialOverride, normalizeLuminance);
		meshInst.MaterialOverride = shaderMat;
	}

	private static void ApplyPlayerColorShaderRecursive(Node node, Color playerColor, bool ignorePlayerColor = false, bool normalizeLuminance = true, bool isUnitOrBuilding = true)
	{
		if (node is MeshInstance3D meshInst)
		{
			ApplyPlayerColorShaderToMesh(meshInst, playerColor, ignorePlayerColor, normalizeLuminance, isUnitOrBuilding);
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && !IsAttachmentNode(childNode))
			{
				ApplyPlayerColorShaderRecursive(childNode, playerColor, ignorePlayerColor, normalizeLuminance, isUnitOrBuilding);
			}
		}
	}

	public static void SetUnitReadability(Node rootNode, bool isUnitOrBuilding)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetUnitReadabilityRecursive(rootNode, isUnitOrBuilding ? 0.10f : 0.0f, isUnitOrBuilding ? 0.25f : 0.0f);
	}

	private static void SetUnitReadabilityRecursive(Node node, float ambientBoost, float rimIntensity)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramUnitAmbientBoost, ambientBoost);
				geomInst.SetInstanceShaderParameter(_paramUnitRimIntensity, rimIntensity);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && !IsAttachmentNode(childNode))
			{
				SetUnitReadabilityRecursive(childNode, ambientBoost, rimIntensity);
			}
		}
	}

}
