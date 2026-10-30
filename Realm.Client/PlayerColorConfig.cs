using System;
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

	/// <summary>
	///     The palette index a colour stands for. The match is made on the eight bits per channel a seat's colour travels
	///     as, because a colour that has come back from the authority no longer equals the palette's own float exactly and
	///     a miss would read as the neutral entry, which is what a colour picker reads to choose the next colour.
	/// </summary>
	public static int GetColorIndex(Color color)
	{
		int red = ToChannelByte(color.R);
		int green = ToChannelByte(color.G);
		int blue = ToChannelByte(color.B);

		for (int index = 0; index < Palette.Length; index++)
		{
			Color entryColor = Palette[index].Color;
			if (entryColor.R8 != red || entryColor.G8 != green || entryColor.B8 != blue) continue;

			return index;
		}

		return NEUTRAL_PLAYER_INDEX;
	}

	private static int ToChannelByte(float channel) => (int)Math.Round(Math.Clamp(channel, 0.0f, 1.0f) * 255.0);

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