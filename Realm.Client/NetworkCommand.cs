using System.Collections.Generic;
using System.Numerics;
using MemoryPack;

namespace Realm.Client;

/// <summary>Command sent from a client to the server representing a player action.</summary>
[MemoryPackable]
public partial struct NetworkCommand
{
	public int CommandId;
	public string CommandType;
	public List<int> UnitEntityIds;
	public NetworkVector3 TargetPosition;
	public int TargetEntityId;
	public string ArgString;
}

public static class NetworkCommandLimits
{
	public const int MaximumPayloadBytes = 8192;
	public const int MaximumCommandTypeLength = 32;
	public const int MaximumArgumentLength = 128;
	public const int MaximumUnitEntityIds = 32;

	public static bool TrySanitize(ref NetworkCommand command, out string rejectionReason)
	{
		rejectionReason = string.Empty;

		if (command.CommandId <= 0)
		{
			rejectionReason = "command id must be positive";
			return false;
		}

		if (!TrySanitizeToken(command.CommandType, MaximumCommandTypeLength, out rejectionReason)) return false;
		if (command.CommandType.Length == 0)
		{
			rejectionReason = "command type is empty";
			return false;
		}

		if (command.ArgString != null)
		{
			if (command.ArgString.Length > MaximumArgumentLength)
			{
				rejectionReason = $"argument is {command.ArgString.Length} characters, above the {MaximumArgumentLength} limit";
				return false;
			}

			for (int index = 0; index < command.ArgString.Length; index++)
			{
				if (char.IsControl(command.ArgString[index]))
				{
					rejectionReason = "argument contains a control character";
					return false;
				}
			}
		}

		if (!float.IsFinite(command.TargetPosition.X) || !float.IsFinite(command.TargetPosition.Y) || !float.IsFinite(command.TargetPosition.Z))
		{
			rejectionReason = "target position is not finite";
			return false;
		}

		if (command.UnitEntityIds == null) return true;

		if (command.UnitEntityIds.Count > MaximumUnitEntityIds)
		{
			rejectionReason = $"command names {command.UnitEntityIds.Count} units, above the {MaximumUnitEntityIds} limit";
			return false;
		}

		for (int index = 0; index < command.UnitEntityIds.Count; index++)
		{
			if (command.UnitEntityIds[index] > 0) continue;
			rejectionReason = "command names a non-positive entity id";
			return false;
		}

		return true;
	}

	private static bool TrySanitizeToken(string? value, int maximumLength, out string rejectionReason)
	{
		rejectionReason = string.Empty;
		if (value == null)
		{
			rejectionReason = "command type is missing";
			return false;
		}

		if (value.Length > maximumLength)
		{
			rejectionReason = $"command type is {value.Length} characters, above the {maximumLength} limit";
			return false;
		}

		for (int index = 0; index < value.Length; index++)
		{
			char character = value[index];
			if (character >= 'a' && character <= 'z') continue;
			if (character >= '0' && character <= '9') continue;
			if (character == '_') continue;
			rejectionReason = $"command type contains '{character}'";
			return false;
		}

		return true;
	}
}
