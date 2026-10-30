using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct ScriptConsoleCommand
{
	public string Command;

	public int SelectedUnitEntityId;
}

public static class ScriptConsoleCommandLimits
{
	public const int MaximumCommandLength = 512;

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
