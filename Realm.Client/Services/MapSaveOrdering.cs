﻿using System;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     The order the save file writes its contents in: by template id, then by distance from the map's
///     top-left corner, then by the remaining properties. A stable order keeps a map's save file from
///     churning between writes that changed nothing.
/// </summary>
internal static class MapSaveOrdering
{
	private static int CompareDistanceAndPosition(float aPosX, float aPosY, float aPosZ, float aRotY, float bPosX, float bPosY, float bPosZ, float bRotY, float topLeftX, float topLeftZ)
	{
		float distanceA = MathF.Sqrt(MathF.Pow(aPosX - topLeftX, 2) + MathF.Pow(aPosZ - topLeftZ, 2));
		float distanceB = MathF.Sqrt(MathF.Pow(bPosX - topLeftX, 2) + MathF.Pow(bPosZ - topLeftZ, 2));
		int comparison = distanceA.CompareTo(distanceB);
		if (comparison != 0) return comparison;

		comparison = aPosX.CompareTo(bPosX);
		if (comparison != 0) return comparison;
		comparison = aPosZ.CompareTo(bPosZ);
		if (comparison != 0) return comparison;
		comparison = aPosY.CompareTo(bPosY);
		if (comparison != 0) return comparison;
		return aRotY.CompareTo(bRotY);
	}

	private static int CompareBaseProperties(string aTemplateId, string bTemplateId, float aPosX, float aPosY, float aPosZ, float aRotY, float bPosX, float bPosY, float bPosZ, float bRotY, float topLeftX, float topLeftZ)
	{
		int comparison = string.Compare(aTemplateId, bTemplateId, StringComparison.OrdinalIgnoreCase);
		if (comparison != 0) return comparison;
		comparison = string.Compare(aTemplateId, bTemplateId, StringComparison.Ordinal);
		if (comparison != 0) return comparison;

		return CompareDistanceAndPosition(aPosX, aPosY, aPosZ, aRotY, bPosX, bPosY, bPosZ, bRotY, topLeftX, topLeftZ);
	}

	private static int CompareUnits(UnitSaveData a, UnitSaveData b, float topLeftX, float topLeftZ)
	{
		int comparison = CompareBaseProperties(a.TemplateId, b.TemplateId, a.PosX, a.PosY, a.PosZ, a.RotationY, b.PosX, b.PosY, b.PosZ, b.RotationY, topLeftX, topLeftZ);
		if (comparison != 0) return comparison;

		comparison = a.Scale.CompareTo(b.Scale);
		if (comparison != 0) return comparison;
		comparison = a.Player.CompareTo(b.Player);
		if (comparison != 0) return comparison;
		return 0;
	}

	private static int CompareProps(PropSaveData a, PropSaveData b, float topLeftX, float topLeftZ)
	{
		int comparison = CompareBaseProperties(a.TemplateId, b.TemplateId, a.PosX, a.PosY, a.PosZ, a.RotationY, b.PosX, b.PosY, b.PosZ, b.RotationY, topLeftX, topLeftZ);
		if (comparison != 0) return comparison;

		return a.Scale.CompareTo(b.Scale);
	}

	private static int CompareDecals(DecalSaveData a, DecalSaveData b, float topLeftX, float topLeftZ)
	{
		int comparison = CompareBaseProperties(a.TemplateId, b.TemplateId, a.PosX, a.PosY, a.PosZ, a.RotationY, b.PosX, b.PosY, b.PosZ, b.RotationY, topLeftX, topLeftZ);
		if (comparison != 0) return comparison;

		return a.Scale.CompareTo(b.Scale);
	}

	private static int CompareVfx(VfxSaveData a, VfxSaveData b, float topLeftX, float topLeftZ)
	{
		int comparison = string.Compare(a.VfxId, b.VfxId, StringComparison.OrdinalIgnoreCase);
		if (comparison != 0) return comparison;
		comparison = string.Compare(a.VfxId, b.VfxId, StringComparison.Ordinal);
		if (comparison != 0) return comparison;

		comparison = CompareDistanceAndPosition(a.PosX, a.PosY, a.PosZ, a.RotationY, b.PosX, b.PosY, b.PosZ, b.RotationY, topLeftX, topLeftZ);
		if (comparison != 0) return comparison;

		return a.ScaleX.CompareTo(b.ScaleX);
	}

	private static int CompareCoordinates(CoordinateSaveData a, CoordinateSaveData b, float topLeftX, float topLeftZ)
	{
		int comparison = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
		if (comparison != 0) return comparison;
		comparison = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
		if (comparison != 0) return comparison;

		float centerAX = (a.MinX + a.MaxX) * 0.5f;
		float centerAZ = (a.MinZ + a.MaxZ) * 0.5f;
		float centerBX = (b.MinX + b.MaxX) * 0.5f;
		float centerBZ = (b.MinZ + b.MaxZ) * 0.5f;

		float distanceA = MathF.Sqrt(MathF.Pow(centerAX - topLeftX, 2) + MathF.Pow(centerAZ - topLeftZ, 2));
		float distanceB = MathF.Sqrt(MathF.Pow(centerBX - topLeftX, 2) + MathF.Pow(centerBZ - topLeftZ, 2));
		comparison = distanceA.CompareTo(distanceB);
		if (comparison != 0) return comparison;

		comparison = a.MinX.CompareTo(b.MinX);
		if (comparison != 0) return comparison;
		comparison = a.MinZ.CompareTo(b.MinZ);
		if (comparison != 0) return comparison;
		comparison = a.MaxX.CompareTo(b.MaxX);
		if (comparison != 0) return comparison;
		return a.MaxZ.CompareTo(b.MaxZ);
	}

	public static void SortMapSaveData(MapSaveData saveData)
	{
		if (saveData == null) return;

		int width = saveData.Width > 0 ? saveData.Width : 128;
		int depth = saveData.Depth > 0 ? saveData.Depth : 128;
		float topLeftX = -width / 2.0f;
		float topLeftZ = -depth / 2.0f;

		if (saveData.Units != null)
		{
			saveData.Units.Sort((a, b) => CompareUnits(a, b, topLeftX, topLeftZ));
		}

		if (saveData.Props != null)
		{
			saveData.Props.Sort((a, b) => CompareProps(a, b, topLeftX, topLeftZ));
		}

		if (saveData.Decals != null)
		{
			saveData.Decals.Sort((a, b) => CompareDecals(a, b, topLeftX, topLeftZ));
		}

		if (saveData.Vfx != null)
		{
			saveData.Vfx.Sort((a, b) => CompareVfx(a, b, topLeftX, topLeftZ));
		}

		if (saveData.Coordinates != null)
		{
			saveData.Coordinates.Sort((a, b) => CompareCoordinates(a, b, topLeftX, topLeftZ));
		}
	}
}
