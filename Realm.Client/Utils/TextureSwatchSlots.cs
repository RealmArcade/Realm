using Godot;
using Realm.Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace Realm.Client.Utils;

public static class TextureSwatchSlots
{
	public const int MaxSlots = 256;

	public static HashSet<string> BuildKnownRibbonsCache(MapMetadata? metadata = null, string? mapDir = null)
	{
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		metadata = TryLoadMetadata(metadata, mapDir);

		if (metadata?.Ribbons != null)
		{
			foreach (var kvp in metadata.Ribbons)
			{
				set.Add(kvp.Key);
				set.Add(Path.GetFileNameWithoutExtension(kvp.Key));
			}
		}

		AddDiskRibbons(set, mapDir);

		return set;
	}

	private static MapMetadata? TryLoadMetadata(MapMetadata? metadata, string? mapDir)
	{
		if (metadata != null || string.IsNullOrEmpty(mapDir)) return metadata;
		try
		{
			return MapFileService.LoadMetadata(mapDir);
		}
		catch { return null; }
	}

	private static void AddDiskRibbons(HashSet<string> set, string? mapDir)
	{
		if (string.IsNullOrEmpty(mapDir)) return;

		string diskRibbonsDir = Path.Combine(mapDir, "Assets", "ribbons");
		if (!Directory.Exists(diskRibbonsDir)) return;

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

	public static bool ValidateCategory(string fileName, object? node = null, HashSet<string>? knownRibbons = null)
	{
		if (string.IsNullOrWhiteSpace(fileName)) return false;

		string normalized = fileName.Replace('\\', '/').ToLowerInvariant();

		if (IsInvalidCategory(normalized)) return false;

		string baseName = Path.GetFileNameWithoutExtension(normalized);
		if (HasInvalidSuffix(baseName)) return false;

		if (IsKnownRibbon(fileName, baseName, knownRibbons)) return false;

		return true;
	}

	private static bool IsInvalidCategory(string normalized)
	{
		return normalized.Contains("ribbons/") || normalized.Contains("ribbon_textures/") ||
			normalized.Contains("decals/") || normalized.Contains("icons/") ||
			normalized.Contains("skyboxes/") || normalized.Contains("noise/") ||
			normalized.Contains("noise_textures/") || normalized.Contains("vfx/") ||
			normalized.Contains("vfx_spritesheets/");
	}

	private static bool HasInvalidSuffix(string baseName)
	{
		return baseName.EndsWith("_trail") || baseName.EndsWith("_flare") ||
			baseName.EndsWith("_beam") || baseName.EndsWith("_pulse") ||
			baseName.EndsWith("_streak") || baseName.EndsWith("_ether_trace");
	}

	private static bool IsKnownRibbon(string fileName, string baseName, HashSet<string>? knownRibbons)
	{
		if (knownRibbons == null) return false;
		return knownRibbons.Contains(fileName) || knownRibbons.Contains(baseName) || knownRibbons.Contains(baseName + ".rtex");
	}

	public static int FirstFreeSlot(bool[] occupiedSlots)
	{
		for (int i = 0; i < MaxSlots; i++)
		{
			if (!occupiedSlots[i]) return i;
		}
		return -1;
	}

	public static SwatchSlotInfo[] ResolveSlots(Dictionary<string, TextureMetadata>? texturesDict, string mapDir)
	{
		var result = new SwatchSlotInfo[MaxSlots];
		var occupied = new bool[MaxSlots];

		var metadata = TryLoadMetadata(null, mapDir);
		if (texturesDict == null && metadata != null)
		{
			texturesDict = metadata.Textures;
		}

		if (texturesDict == null)
		{
			return FillEmptySlots(result, occupied);
		}

		var knownRibbons = BuildKnownRibbonsCache(metadata, mapDir);

		var candidateItems = GetCandidateItems(texturesDict, knownRibbons);
		var pendingReassign = AssignRequestedSlots(candidateItems, occupied, result);

		AssignPendingSlots(pendingReassign, occupied, result);

		return FillEmptySlots(result, occupied);
	}

	private static SwatchSlotInfo[] FillEmptySlots(SwatchSlotInfo[] result, bool[] occupied)
	{
		for (int i = 0; i < MaxSlots; i++)
		{
			if (!occupied[i])
			{
				result[i] = new SwatchSlotInfo(i, null, null, true, null);
			}
		}
		return result;
	}

	private static List<(string BaseName, string FileName, int RequestedSlot, TextureMetadata? Node)> GetCandidateItems(Dictionary<string, TextureMetadata> texturesDict, HashSet<string> knownRibbons)
	{
		var candidateItems = new List<(string BaseName, string FileName, int RequestedSlot, TextureMetadata? Node)>();
		foreach (var kvp in texturesDict)
		{
			if (!ValidateCategory(kvp.Key, kvp.Value, knownRibbons)) continue;

			string baseName = Path.GetFileNameWithoutExtension(kvp.Key);
			int requestedSlot = kvp.Value?.SwatchIndex ?? -1;
			string fileName = GetCandidateFileName(kvp.Key, baseName, kvp.Value);

			candidateItems.Add((baseName, fileName, requestedSlot, kvp.Value));
		}
		return candidateItems;
	}

	private static string GetCandidateFileName(string key, string baseName, TextureMetadata? texMeta)
	{
		string fileName = !string.IsNullOrWhiteSpace(texMeta?.TexturePath)
			? Path.GetFileName(texMeta.TexturePath)
			: (key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(key) : $"{baseName}.rtex");

		if (!fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			fileName += ".rtex";
		}
		return fileName;
	}

	private static List<(string BaseName, string FileName, TextureMetadata? Node)> AssignRequestedSlots(
		List<(string BaseName, string FileName, int RequestedSlot, TextureMetadata? Node)> candidateItems,
		bool[] occupied,
		SwatchSlotInfo[] result)
	{
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
		return pendingReassign;
	}

	private static void AssignPendingSlots(
		List<(string BaseName, string FileName, TextureMetadata? Node)> pendingReassign,
		bool[] occupied,
		SwatchSlotInfo[] result)
	{
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
	}
}
