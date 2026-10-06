using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Godot.Utils;

public readonly struct SwatchSlotInfo
{
	public readonly int SlotIndex;
	public readonly string? BaseName;
	public readonly string? FileName;
	public readonly bool IsFiller;
	public readonly TextureMetadata? MetadataNode;

	public SwatchSlotInfo(int slotIndex, string? baseName, string? fileName, bool isFiller, TextureMetadata? metadataNode)
	{
		SlotIndex = slotIndex;
		BaseName = baseName;
		FileName = fileName;
		IsFiller = isFiller;
		MetadataNode = metadataNode;
	}
}

public static class TextureSwatchSlots
{
	public const int MaxSlots = 256;

	public static HashSet<string> BuildKnownRibbonsCache(MapManifestAssets? allAssets = null, string? mapDir = null)
	{
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		var ribbonDict = allAssets?.GetCategory("Ribbon");
		if (ribbonDict != null)
		{
			foreach (var kvp in ribbonDict)
			{
				set.Add(kvp.Key);
				set.Add(Path.GetFileNameWithoutExtension(kvp.Key));
			}
		}

		if (!string.IsNullOrEmpty(mapDir))
		{
			string diskRibbonsDir = Path.Combine(mapDir, "Assets", "ribbons");
			if (Directory.Exists(diskRibbonsDir))
			{
				try
				{
					foreach (var file in Directory.GetFiles(diskRibbonsDir, "*.rtex"))
					{
						string fname = Path.GetFileName(file);
						set.Add(fname);
						set.Add(Path.GetFileNameWithoutExtension(fname));
					}
				}
				catch { }
			}
		}

		return set;
	}

	public static bool ValidateCategory(string fileName, object? node = null, HashSet<string>? knownRibbons = null)
	{
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return false;
		}

		string normalized = fileName.Replace('\\', '/').ToLowerInvariant();

		if (normalized.Contains("ribbons/") || normalized.Contains("ribbon_textures/") ||
			normalized.Contains("decals/") || normalized.Contains("icons/") ||
			normalized.Contains("skyboxes/") || normalized.Contains("noise/") ||
			normalized.Contains("noise_textures/") || normalized.Contains("vfx/") ||
			normalized.Contains("vfx_spritesheets/"))
		{
			return false;
		}

		string baseName = Path.GetFileNameWithoutExtension(normalized);
		if (baseName.EndsWith("_trail") || baseName.EndsWith("_flare") ||
			baseName.EndsWith("_beam") || baseName.EndsWith("_pulse") ||
			baseName.EndsWith("_streak") || baseName.EndsWith("_ether_trace"))
		{
			return false;
		}

		if (knownRibbons != null)
		{
			if (knownRibbons.Contains(fileName) || knownRibbons.Contains(baseName) || knownRibbons.Contains(baseName + ".rtex"))
			{
				return false;
			}
		}

		return true;
	}

	public static int FirstFreeSlot(bool[] occupiedSlots)
	{
		for (int i = 0; i < MaxSlots; i++)
		{
			if (!occupiedSlots[i])
			{
				return i;
			}
		}
		return -1;
	}

	public static SwatchSlotInfo[] ResolveSlots(Dictionary<string, string>? texturesDict, string mapDir)
	{
		var result = new SwatchSlotInfo[MaxSlots];
		var occupied = new bool[MaxSlots];

		MapManifestAssets? allAssets = null;
		try
		{
			allAssets = Realm.Godot.Utils.MapAssetHelper.LoadAssets(mapDir);
		}
		catch { }

		texturesDict ??= allAssets?.GetCategory("Terrain");

		if (texturesDict == null)
		{
			for (int i = 0; i < MaxSlots; i++)
			{
				result[i] = new SwatchSlotInfo(i, null, null, true, null);
			}
			return result;
		}

		MapMetadata? metadata = null;
		try
		{
			if (!string.IsNullOrEmpty(mapDir))
			{
				metadata = MapFileService.LoadMetadata(mapDir);
			}
		}
		catch { }

		var knownRibbons = BuildKnownRibbonsCache(allAssets, mapDir);

		var candidateItems = new List<(string BaseName, string FileName, int RequestedSlot, TextureMetadata? Node)>();

		foreach (var kvp in texturesDict)
		{
			string fileName = kvp.Key;
			if (!ValidateCategory(fileName, kvp.Value, knownRibbons))
			{
				continue;
			}

			string baseName = Path.GetFileNameWithoutExtension(fileName);
			int requestedSlot = -1;
			TextureMetadata? texMeta = null;

			if (metadata != null && metadata.Textures != null && metadata.Textures.TryGetValue(fileName, out var foundMeta) && foundMeta != null)
			{
				texMeta = foundMeta;
				requestedSlot = foundMeta.SwatchIndex;
			}

			candidateItems.Add((baseName, fileName, requestedSlot, texMeta));
		}

		var pendingReassign = new List<(string BaseName, string FileName, TextureMetadata? Node)>();

		foreach (var item in candidateItems)
		{
			if (item.RequestedSlot >= 0 && item.RequestedSlot < MaxSlots && !occupied[item.RequestedSlot])
			{
				occupied[item.RequestedSlot] = true;
				result[item.RequestedSlot] = new SwatchSlotInfo(item.RequestedSlot, item.BaseName, item.FileName, false, item.Node);
			}
			else
			{
				pendingReassign.Add((item.BaseName, item.FileName, item.Node));
			}
		}

		foreach (var pending in pendingReassign)
		{
			int freeSlot = FirstFreeSlot(occupied);
			if (freeSlot >= 0)
			{
				occupied[freeSlot] = true;
				result[freeSlot] = new SwatchSlotInfo(freeSlot, pending.BaseName, pending.FileName, false, pending.Node);
				GD.Print($"[TextureSwatchSlots] Assigned texture '{pending.FileName}' to free slot {freeSlot}.");
			}
			else
			{
				GD.PrintErr($"[TextureSwatchSlots] Cannot assign texture '{pending.FileName}', maximum {MaxSlots} slots reached.");
			}
		}

		for (int i = 0; i < MaxSlots; i++)
		{
			if (!occupied[i])
			{
				result[i] = new SwatchSlotInfo(i, null, null, true, null);
			}
		}

		return result;
	}
}
