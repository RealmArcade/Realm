using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     A line a player typed into the map script's console, on its way to the process that runs the script. The map's
///     commands are the map's own to interpret, so the line is carried to the authority rather than acted on where it
///     was typed.
/// </summary>
[MemoryPackable]
public partial struct ScriptConsoleCommand
{
	/// <summary>The line the player typed, without the leading whitespace.</summary>
	public string Command;

	/// <summary>
	///     The unit that was selected when the line was typed, as the authority names it, or zero when nothing was
	///     selected. A command that acts on a unit is answered about the unit its sender meant.
	/// </summary>
	public int SelectedUnitEntityId;
}

public static class ScriptConsoleCommandLimits
{
	public const int MaximumCommandLength = 512;

	/// <summary>Whether a line is one the authority will hand to a map's script.</summary>
	public static bool IsAcceptable(string? command)
	{
		if (string.IsNullOrWhiteSpace(command)) return false;
		if (command.Length > MaximumCommandLength) return false;

		for (int index = 0; index < command.Length; index++)
		{
			if (char.IsControl(command[index]) && command[index] != '\t') return false;
		}

		return true;
	}
}
