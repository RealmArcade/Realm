using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     A line of chat on its way through the match. Chat belongs to the match rather than to the lobby once the match
///     starts: the process that runs the world is the one every screen is connected to, so a line is carried to it and
///     relayed from there instead of being sent peer to peer.
/// </summary>
[MemoryPackable]
public partial struct ChatLine
{
	/// <summary>The name the line is shown under, as the authority resolved it for the player who sent it.</summary>
	public string Sender;

	/// <summary>The text the player typed.</summary>
	public string Message;

	/// <summary>Whether only the sender's allies are shown the line.</summary>
	public bool AlliesOnly;

	/// <summary>When the line was typed, so every screen shows the same time for it.</summary>
	public long TimestampUtcTicks;
}

/// <summary>What a chat line may be, so a peer cannot make the authority relay something no screen can show.</summary>
public static class ChatLineLimits
{
	public const int MaximumMessageLength = 512;
	public const int MaximumSenderLength = 32;

	/// <summary>Whether a line is one the authority will relay.</summary>
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
