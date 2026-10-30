using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct ChatLine
{
	public string Sender;

	public string Message;

	public bool AlliesOnly;

	public long TimestampUtcTicks;
}

public static class ChatLineLimits
{
	public const int MaximumMessageLength = 512;
	public const int MaximumSenderLength = 32;

	public static bool IsAcceptable(string? message)
	{
		if (string.IsNullOrWhiteSpace(message)) return false;
		if (message.Length > MaximumMessageLength) return false;

		for (int index = 0; index < message.Length; index++)
		{
			if (char.IsControl(message[index]) && message[index] != '\t') return false;
		}

		return true;
	}
}
