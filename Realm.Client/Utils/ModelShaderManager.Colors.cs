using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Realm.Client.Utils;

public static partial class ModelShaderManager
{
	public static void RefreshShaderMaterialsForNode(Node rootNode, bool normalizeLuminance = true)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		RefreshShaderMaterialsRecursive(rootNode, normalizeLuminance);
	}

	private static void RefreshMeshInstanceMaterials(MeshInstance3D meshInst, bool normalizeLuminance)
	{
		if (IsExcludedMesh(meshInst)) return;

		int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 1;
		for (int i = 0; i < surfaceCount; i++)
		{
			Material? srcMat = meshInst.Mesh != null ? meshInst.Mesh.SurfaceGetMaterial(i) : null;
			if (srcMat == null) srcMat = meshInst.GetSurfaceOverrideMaterial(i);
			if (srcMat != null)
			{
				var shaderMat = GetOrCreateShaderMaterial(srcMat, normalizeLuminance);
				meshInst.SetSurfaceOverrideMaterial(i, shaderMat);
			}
		}

		if (meshInst.MaterialOverride != null)
		{
			var shaderMat = GetOrCreateShaderMaterial(meshInst.MaterialOverride, normalizeLuminance);
			meshInst.MaterialOverride = shaderMat;
		}
	}

	private static void RefreshShaderMaterialsRecursive(Node node, bool normalizeLuminance)
	{
		if (node is MeshInstance3D meshInst)
		{
			RefreshMeshInstanceMaterials(meshInst, normalizeLuminance);
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				RefreshShaderMaterialsRecursive(childNode, normalizeLuminance);
			}
		}
	}

	public static void SetPlayerColor(Node rootNode, Color playerColor)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetPlayerColorRecursive(rootNode, playerColor);
	}

	private static void SetPlayerColorRecursive(Node node, Color playerColor)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramPlayerColor, playerColor);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && !IsAttachmentNode(childNode))
			{
				SetPlayerColorRecursive(childNode, playerColor);
			}
		}
	}

	public static void SetPlayerColorOnAttachments(Node rootNode, Color playerColor)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetPlayerColorOnAttachmentsRecursive(rootNode, playerColor);
	}

	private static void SetPlayerColorOnAttachmentsRecursive(Node node, Color playerColor)
	{
		foreach (var child in node.GetChildren())
		{
			if (child is not Node childNode) continue;

			if (IsAttachmentNode(childNode))
			{
				SetPlayerColorForSubtree(childNode, playerColor);
				continue;
			}

			SetPlayerColorOnAttachmentsRecursive(childNode, playerColor);
		}
	}

	private static void SetPlayerColorForSubtree(Node node, Color playerColor)
	{
		if (node is GeometryInstance3D geomInst && !IsExcludedMesh(geomInst))
		{
			geomInst.SetInstanceShaderParameter(_paramPlayerColor, playerColor);
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				SetPlayerColorForSubtree(childNode, playerColor);
			}
		}
	}

	public static void SetIgnorePlayerColor(Node rootNode, bool ignorePlayerColor)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		SetIgnorePlayerColorRecursive(rootNode, ignorePlayerColor);
	}

	private static void SetIgnorePlayerColorRecursive(Node node, bool ignorePlayerColor)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramIgnorePlayerColor, ignorePlayerColor ? 1.0f : 0.0f);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && !IsAttachmentNode(childNode))
			{
				SetIgnorePlayerColorRecursive(childNode, ignorePlayerColor);
			}
		}
	}

	public static void SetBrightnessAndTint(Node rootNode, float brightness, Color tint)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return;
		if (MathF.Abs(brightness - 1.0f) < 0.001f && tint == new Color(1.0f, 1.0f, 1.0f)) return;
		SetBrightnessAndTintRecursive(rootNode, brightness, tint);
	}

	private static void SetBrightnessAndTintRecursive(Node node, float brightness, Color tint)
	{
		if (node is GeometryInstance3D geomInst)
		{
			if (!IsExcludedMesh(geomInst))
			{
				geomInst.SetInstanceShaderParameter(_paramModelBrightness, brightness);
				geomInst.SetInstanceShaderParameter(_paramModelColorTint, tint);
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				SetBrightnessAndTintRecursive(childNode, brightness, tint);
			}
		}
	}


	public static void ApplyBrightnessAndTintToAlbedoImage(Image img, float brightness, Color tint)
	{
		if (img == null) return;
		if (MathF.Abs(brightness - 1.0f) <= 0.001f && MathF.Abs(tint.R - 1.0f) <= 0.001f && MathF.Abs(tint.G - 1.0f) <= 0.001f && MathF.Abs(tint.B - 1.0f) <= 0.001f)
		{
			return;
		}

		if (img.GetFormat() != Image.Format.Rgba8)
		{
			img.Convert(Image.Format.Rgba8);
		}

		byte[] data = img.GetData();
		float multR = brightness * tint.R;
		float multG = brightness * tint.G;
		float multB = brightness * tint.B;

		for (int i = 0; i < data.Length; i += 4)
		{
			data[i] = (byte)Math.Clamp((int)MathF.Round(data[i] * multR), 0, 255);
			data[i + 1] = (byte)Math.Clamp((int)MathF.Round(data[i + 1] * multG), 0, 255);
			data[i + 2] = (byte)Math.Clamp((int)MathF.Round(data[i + 2] * multB), 0, 255);
		}

		img.SetData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, data);
	}

	public static void SetHideInShroud(Node node, bool hideInShroud)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return;
		float val = hideInShroud ? 1.0f : 0.0f;
		if (node is GeometryInstance3D geom)
		{
			geom.SetInstanceShaderParameter(_paramHideInShroud, val);
		}

		int childCount = node.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			SetHideInShroud(node.GetChild(i), hideInShroud);
		}
	}

}
