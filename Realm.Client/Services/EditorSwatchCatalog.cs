using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace Realm.Client.Services;

/// <summary>
/// The editor's texture swatches: the resolved texture paths, the names shown in the palette and the
/// colours the palette paints each slot with. Resolving a slot to a path on disk belongs here too,
/// because that is the only thing the swatch list is built from.
/// </summary>
public sealed class EditorSwatchCatalog
{
	public List<string> Paths { get; set; } = new List<string>();
	public List<string> DisplayNames { get; set; } = new List<string>();
	public List<Color> Colors { get; set; } = new List<Color>();

	public void Clear()
	{
		Paths.Clear();
		DisplayNames.Clear();
		Colors.Clear();
	}

	public void Populate(SwatchSlotInfo[] slots, string wsPath)
	{
		for (int i = 0; i < TextureSwatchSlots.MaxSlots; i++)
		{
			var slot = slots[i];
			if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
			{
				string resolvedPath = ResolveTexturePath(slot, wsPath);
				DisplayNames.Add(slot.BaseName);
				Paths.Add(resolvedPath);
				Colors.Add(new Color(0.6f, 0.6f, 0.6f));
			}
			else
			{
				DisplayNames.Add($"Slot {i} (Empty)");
				Paths.Add("");
				Colors.Add(new Color(0.2f, 0.2f, 0.2f, 0.5f));
			}
		}
	}

	public string GetName(Color color)
	{
		float epsilon = 0.01f;
		for (int i = 1; i <= Colors.Count; i++)
		{
			Color c = GetColor(i);
			if (Mathf.Abs(color.R - c.R) < epsilon &&
				Mathf.Abs(color.G - c.G) < epsilon &&
				Mathf.Abs(color.B - c.B) < epsilon)
			{
				return (i >= 1 && i <= DisplayNames.Count) ? DisplayNames[i - 1] : "Unknown";
			}
		}

		return "Custom";
	}

	public Color GetColor(int index)
	{
		if (index >= 1 && index <= Colors.Count)
		{
			return Colors[index - 1];
		}

		return new Color(1, 1, 1);
	}

	private static string ResolveTexturePath(SwatchSlotInfo slot, string wsPath)
	{
		string rtexName = GetRtexName(slot);

		string[] pathsToTry =
		{
			Path.Combine(wsPath, "Assets", "textures", rtexName),
			Path.Combine(wsPath, rtexName)
		};

		foreach (var path in pathsToTry)
		{
			if (File.Exists(path)) return path;
		}

		string? found = TryFindRtexPath(rtexName);
		if (!string.IsNullOrEmpty(found)) return found;

		return string.Empty;
	}

	private static string GetRtexName(SwatchSlotInfo slot)
	{
		string rtexName = slot.MetadataNode?.TexturePath ?? slot.FileName ?? (slot.BaseName + ".rtex");
		if (!rtexName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			rtexName += ".rtex";
		}

		return Path.GetFileName(rtexName);
	}

	private static string? TryFindRtexPath(string rtexName)
	{
		string? found = PathUtils.FindPath($"Assets/textures/{rtexName}");
		if (!string.IsNullOrEmpty(found) && File.Exists(found)) return found;

		string? foundTemplate = PathUtils.FindPath($"MapTemplate/Assets/textures/{rtexName}");
		if (!string.IsNullOrEmpty(foundTemplate) && File.Exists(foundTemplate)) return foundTemplate;

		return null;
	}

	internal (string terrainName, string cliffName) GetTextureNames(int terrainIdx, int cliffIdx)
	{
		string terrainName = (terrainIdx >= 0 && terrainIdx < DisplayNames.Count) ? DisplayNames[terrainIdx] : "Unknown";
		string cliffName = (cliffIdx >= 0 && cliffIdx < DisplayNames.Count) ? DisplayNames[cliffIdx] : "Unknown";

		return (terrainName, cliffName);
	}
}
