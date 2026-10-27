using System.Collections.Generic;
using Godot;
using SharedPalette = Realm.Shared.Players.PlayerColorPalette;
using SharedPlayerColorEntry = Realm.Shared.Players.PlayerColorEntry;

namespace Realm.Client;

public static class PlayerColorConfig
{
	public const int NEUTRAL_PLAYER_INDEX = SharedPalette.NeutralPlayerIndex;

	public static readonly PlayerColorEntry[] Palette = BuildPalette();

	private static PlayerColorEntry[] BuildPalette()
	{
		var palette = new PlayerColorEntry[SharedPalette.Count];
		for (int index = 0; index < palette.Length; index++)
		{
			SharedPlayerColorEntry entry = SharedPalette.Palette[index];
			palette[index] = new PlayerColorEntry(entry.Index, entry.Name, new Color(entry.Red, entry.Green, entry.Blue));
		}

		return palette;
	}

	public static readonly List<Color> AvailableColors = new();

	static PlayerColorConfig()
	{
		for (int i = 0; i < Palette.Length; i++)
		{
			AvailableColors.Add(Palette[i].Color);
		}
	}

	public static Color GetColor(int playerIndex)
	{
		if (playerIndex >= 0 && playerIndex < Palette.Length)
		{
			return Palette[playerIndex].Color;
		}

		return Palette[NEUTRAL_PLAYER_INDEX].Color;
	}

	public static string GetPaletteLabel(int playerIndex)
	{
		if (playerIndex < 0 || playerIndex >= Palette.Length)
		{
			playerIndex = NEUTRAL_PLAYER_INDEX;
		}

		var entry = Palette[playerIndex];
		return playerIndex == NEUTRAL_PLAYER_INDEX
			? $"{entry.Index} {entry.Name} (gaia)"
			: $"{entry.Index} {entry.Name}";
	}

	public static string GetName(int playerIndex)
	{
		if (playerIndex >= 0 && playerIndex < Palette.Length)
		{
			return Palette[playerIndex].Name;
		}

		return Palette[NEUTRAL_PLAYER_INDEX].Name;
	}

	public static int GetColorIndex(Color color) => SharedPalette.IndexOfColor(color.R, color.G, color.B);

	public static string GetColorHex(int playerIndex) => SharedPalette.GetColorHex(playerIndex);

	public static Color GetColorForOwner(int? playerIndex)
	{
		if (!playerIndex.HasValue || playerIndex.Value < 0 || playerIndex.Value >= Palette.Length)
		{
			return Palette[NEUTRAL_PLAYER_INDEX].Color;
		}

		return Palette[playerIndex.Value].Color;
	}
}