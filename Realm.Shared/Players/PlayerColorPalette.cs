using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Realm.Shared.Players;

/// <summary>
///     The color a player slot is drawn in, as red, green and blue fractions between zero and one, so anything that
///     can answer a color question does not have to know how a particular renderer stores one.
/// </summary>
public readonly record struct PlayerColorEntry(int Index, string Name, float Red, float Green, float Blue)
{
	/// <summary>This entry's color as a plain vector, which is what a map script is handed.</summary>
	public Vector3 ToVector() => new(Red, Green, Blue);
}

/// <summary>
///     The colors player slots are drawn in. A map script asks the authority what color a player is, and screens draw
///     the slots they are told about, so the palette lives beside the protocol rather than in either process: a color
///     a script reads and a color a screen shows are then the same answer.
/// </summary>
public static class PlayerColorPalette
{
	/// <summary>The slot that belongs to nobody, which is who the map itself acts as.</summary>
	public const int NeutralPlayerIndex = 0;

	private static readonly PlayerColorEntry[] Entries =
	[
		new PlayerColorEntry(0, "NEUTRAL_TAN", 0.620f, 0.541f, 0.431f),

		new PlayerColorEntry(1, "Crimson", 0.850f, 0.100f, 0.100f),
		new PlayerColorEntry(2, "Blue", 0.100f, 0.400f, 0.900f),
		new PlayerColorEntry(3, "Emerald", 0.100f, 0.750f, 0.200f),
		new PlayerColorEntry(4, "Yellow", 0.950f, 0.850f, 0.100f),
		new PlayerColorEntry(5, "Orange", 0.950f, 0.450f, 0.050f),
		new PlayerColorEntry(6, "Cyan", 0.000f, 0.800f, 0.800f),
		new PlayerColorEntry(7, "Purple", 0.600f, 0.150f, 0.850f),
		new PlayerColorEntry(8, "Magenta", 0.950f, 0.200f, 0.600f),
		new PlayerColorEntry(9, "Lime", 0.650f, 0.850f, 0.100f),
		new PlayerColorEntry(10, "Teal", 0.000f, 0.550f, 0.550f),
		new PlayerColorEntry(11, "Coral", 0.950f, 0.400f, 0.350f),
		new PlayerColorEntry(12, "Slate", 0.400f, 0.450f, 0.550f),

		new PlayerColorEntry(13, "Maroon", 0.550f, 0.050f, 0.200f),
		new PlayerColorEntry(14, "Cobalt", 0.150f, 0.200f, 0.750f),
		new PlayerColorEntry(15, "Forest", 0.150f, 0.450f, 0.150f),
		new PlayerColorEntry(16, "Amber", 0.950f, 0.680f, 0.100f),
		new PlayerColorEntry(17, "Russet", 0.550f, 0.300f, 0.100f),
		new PlayerColorEntry(18, "Azure", 0.000f, 0.650f, 0.950f),
		new PlayerColorEntry(19, "Indigo", 0.400f, 0.100f, 0.550f),
		new PlayerColorEntry(20, "Lavender", 0.750f, 0.550f, 0.950f),
		new PlayerColorEntry(21, "Olive", 0.400f, 0.500f, 0.200f),
		new PlayerColorEntry(22, "Mint", 0.300f, 0.750f, 0.600f),
		new PlayerColorEntry(23, "Ruby", 0.800f, 0.250f, 0.400f),
		new PlayerColorEntry(24, "Silver", 0.850f, 0.880f, 0.900f)
	];

	/// <summary>How many slots the palette covers, which is the highest slot index plus one.</summary>
	public static int Count => Entries.Length;

	/// <summary>Every entry, in slot order.</summary>
	public static IReadOnlyList<PlayerColorEntry> Palette => Entries;

	/// <summary>One slot's entry, or the neutral entry when the slot has no color of its own.</summary>
	public static PlayerColorEntry GetEntry(int playerIndex)
	{
		if (playerIndex < 0 || playerIndex >= Entries.Length) return Entries[NeutralPlayerIndex];

		return Entries[playerIndex];
	}

	/// <summary>One slot's color.</summary>
	public static Vector3 GetColor(int playerIndex) => GetEntry(playerIndex).ToVector();

	/// <summary>The name a slot's color is known by, which is what a lobby and a map both call it.</summary>
	public static string GetName(int playerIndex) => GetEntry(playerIndex).Name;

	/// <summary>One slot's color as six hexadecimal digits, uppercase and without an alpha channel.</summary>
	public static string GetColorHex(int playerIndex)
	{
		if (playerIndex < 0 || playerIndex >= Entries.Length) return "FFFFFF";

		PlayerColorEntry entry = Entries[playerIndex];
		return string.Create(CultureInfo.InvariantCulture, $"{RoundToByte(entry.Red):X2}{RoundToByte(entry.Green):X2}{RoundToByte(entry.Blue):X2}");
	}

	/// <summary>The slot a color belongs to, or the neutral slot when the color is not in the palette.</summary>
	public static int IndexOfColor(Vector3 color) => IndexOfColor(color.X, color.Y, color.Z);

	/// <summary>The slot a color belongs to, or the neutral slot when the color is not in the palette.</summary>
	public static int IndexOfColor(float red, float green, float blue)
	{
		for (int index = 0; index < Entries.Length; index++)
		{
			PlayerColorEntry entry = Entries[index];
			if (!IsClose(entry.Red, red) || !IsClose(entry.Green, green) || !IsClose(entry.Blue, blue)) continue;

			return index;
		}

		return NeutralPlayerIndex;
	}

	private static int RoundToByte(float value) => (int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);

	private static bool IsClose(float left, float right)
	{
		float difference = MathF.Abs(left - right);
		float tolerance = MathF.Max(0.00001f, 0.00001f * MathF.Max(MathF.Abs(left), MathF.Abs(right)));
		return difference < tolerance;
	}
}
