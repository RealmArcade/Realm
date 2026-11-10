using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Realm.Client.Utils;

public static partial class ModelShaderManager
{
	public static Image? NormalizeAlbedoImage(Image? sourceImage, float targetLinearLuminance = 0.22f, float minScaleFactor = 0.2f, float maxScaleFactor = 8.0f)
	{
		if (sourceImage == null) return null;

		Image workingImage = PrepareWorkingImage(sourceImage, out Image.Format fmt);
		int w = workingImage.GetWidth();
		int h = workingImage.GetHeight();
		byte[] data = workingImage.GetData();
		int channels = fmt == Image.Format.Rgba8 ? 4 : 3;

		int step = (w * h > 262144) ? 4 : 1;
		int stride = channels * step;

		double totalLinearLuminance = CalculateTotalLinearLuminance(data, stride, channels, false, out long validPixelCount);

		if (validPixelCount == 0)
		{
			totalLinearLuminance = CalculateTotalLinearLuminance(data, stride, channels, true, out validPixelCount);
		}

		if (validPixelCount == 0) return sourceImage;

		float avgLuminance = (float)(totalLinearLuminance / validPixelCount);
		if (avgLuminance <= 0.0001f) return sourceImage;

		float rawScaleFactor = targetLinearLuminance / avgLuminance;
		float scaleFactor = Mathf.Clamp(rawScaleFactor, minScaleFactor, maxScaleFactor);

		if (MathF.Abs(scaleFactor - 1.0f) < 0.01f)
		{
			return sourceImage;
		}

		return ApplyLuminanceScaleFactor(data, channels, scaleFactor, w, h, fmt);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static byte LinearToSrgbByte(float lin)
	{
		int idx = (int)(lin * 65535.0f);
		if ((uint)idx >= 65536)
		{
			return lin <= 0.0f ? (byte)0 : (byte)255;
		}

		return LinearToSrgbLut[idx];
	}

	private static bool EvaluateMaskPresence(byte[] data, int stride)
	{
		int maskCount = 0;
		int unmaskCount = 0;

		for (int i = 0; i < data.Length; i += stride)
		{
			if (data[i] > 32)
			{
				maskCount++;
			}
			else
			{
				unmaskCount++;
			}
		}

		return maskCount > 5 && unmaskCount > 5;
	}

	private static bool UpdatePlayerMaskCache(ulong ormId, bool hasMask)
	{
		_playerMaskCheckCache[ormId] = hasMask;
		return hasMask;
	}

	public static bool CheckHasPlayerMask(Texture2D? ormTexture, Material? sourceMaterial)
	{
		if (ormTexture == null) return false;

		ulong ormId = ormTexture.GetInstanceId();
		if (_playerMaskCheckCache.TryGetValue(ormId, out bool cached))
		{
			return cached;
		}

		Image img = ormTexture.GetImage();
		if (img == null) return UpdatePlayerMaskCache(ormId, false);

		Image workingImg = GetMaskWorkingImage(img, out Image.Format fmt);
		byte[]? data = GetMaskDataFromImage(workingImg, fmt, out int stride);

		if (data == null) return UpdatePlayerMaskCache(ormId, false);

		bool hasMask = EvaluateMaskPresence(data, stride);
		return UpdatePlayerMaskCache(ormId, hasMask);
	}

	private static Image GetMaskWorkingImage(Image img, out Image.Format fmt)
	{
		Image workingImg = (Image)img.Duplicate();
		if (workingImg.IsCompressed()) workingImg.Decompress();
		if (workingImg.HasMipmaps()) workingImg.ClearMipmaps();

		fmt = workingImg.GetFormat();
		if (fmt != Image.Format.Rgba8 && fmt != Image.Format.Rgb8 && fmt != Image.Format.R8)
		{
			workingImg.Convert(Image.Format.Rgba8);
			fmt = Image.Format.Rgba8;
		}

		return workingImg;
	}

	private static byte[]? GetMaskDataFromImage(Image workingImg, Image.Format fmt, out int stride)
	{
		int totalPixels = workingImg.GetWidth() * workingImg.GetHeight();
		if (totalPixels == 0)
		{
			stride = 0;
			return null;
		}

		int channels = fmt == Image.Format.Rgba8 ? 4 : (fmt == Image.Format.Rgb8 ? 3 : 1);
		int step = totalPixels > 262144 ? 4 : 1;
		stride = channels * step;

		return workingImg.GetData();
	}

	public static bool ModelHasPlayerMask(Node rootNode)
	{
		if (rootNode == null || !GodotObject.IsInstanceValid(rootNode)) return false;
		return ModelHasPlayerMaskRecursive(rootNode);
	}

	private static Texture2D? GetOrmTextureFromMaterial(Material? srcMat)
	{
		if (srcMat is OrmMaterial3D ormMat)
		{
			return ormMat.OrmTexture;
		}
		else if (srcMat is BaseMaterial3D baseMat)
		{
			return baseMat.RoughnessTexture ?? baseMat.MetallicTexture;
		}
		else if (srcMat is ShaderMaterial sm)
		{
			var ormVar = sm.GetShaderParameter("texture_orm");
			return ormVar.VariantType != Variant.Type.Nil ? ormVar.As<Texture2D>() : null;
		}

		return null;
	}

	private static bool CheckMeshInstanceForPlayerMask(MeshInstance3D meshInst)
	{
		if (IsExcludedMesh(meshInst)) return false;

		if (CheckSurfaceMaterialForPlayerMask(meshInst)) return true;
		if (CheckOverrideMaterialForPlayerMask(meshInst)) return true;

		return false;
	}

	private static bool CheckSurfaceMaterialForPlayerMask(MeshInstance3D meshInst)
	{
		int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 1;
		for (int i = 0; i < surfaceCount; i++)
		{
			Material? srcMat = meshInst.GetSurfaceOverrideMaterial(i);
			if (srcMat == null && meshInst.Mesh != null)
			{
				srcMat = meshInst.Mesh.SurfaceGetMaterial(i);
			}

			Texture2D? ormTexture = GetOrmTextureFromMaterial(srcMat);
			if (ormTexture != null && CheckHasPlayerMask(ormTexture, srcMat)) return true;
		}

		return false;
	}

	private static bool CheckOverrideMaterialForPlayerMask(MeshInstance3D meshInst)
	{
		if (meshInst.MaterialOverride == null) return false;

		Texture2D? ormTexture = GetOrmTextureFromMaterial(meshInst.MaterialOverride);
		return ormTexture != null && CheckHasPlayerMask(ormTexture, meshInst.MaterialOverride);
	}

	private static bool ModelHasPlayerMaskRecursive(Node node)
	{
		if (node is MeshInstance3D meshInst)
		{
			if (CheckMeshInstanceForPlayerMask(meshInst))
			{
				return true;
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode && !IsAttachmentNode(childNode) && ModelHasPlayerMaskRecursive(childNode))
			{
				return true;
			}
		}

		return false;
	}


}
