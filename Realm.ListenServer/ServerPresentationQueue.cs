using System.Numerics;

namespace Realm.ListenServer;

/// <summary>
///     What a map script asked a screen to show. The authority decides what happened and which players can see it; a
///     client decides how it looks.
/// </summary>
public enum ServerPresentationKind : byte
{
	PlayerMessage,
	BroadcastMessage,
	PlayerFeedbackText,
	FeedbackText,
	FloatingText,
	MinimapPing,
	TargetIndicator,
	PlayerDefeated,
	PlayerVictory
}

/// <summary>
///     One request from the scripting surface, addressed to a player or to everyone. It carries keys and numbers, never
///     localized text, so a client renders it in its own language.
/// </summary>
public readonly record struct ServerPresentationEvent(
	ServerPresentationKind Kind,
	int PlayerIndex,
	string Key,
	string Text,
	Vector3 Position);

/// <summary>
///     Collects what the scripting surface wants shown, in the order the authority decided it, for the transport to
///     deliver to each client after the snapshot that introduces what the effect belongs to.
/// </summary>
public class ServerPresentationQueue
{
	private readonly Queue<ServerPresentationEvent> _events = new();

	/// <summary>How many requests are waiting to be delivered to clients.</summary>
	public int PendingCount => _events.Count;

	public void Write(ServerPresentationKind kind, int playerIndex, string key, string text, Vector3 position)
	{
		_events.Enqueue(new ServerPresentationEvent(kind, playerIndex, key ?? string.Empty, text ?? string.Empty, position));
	}

	/// <summary>Hands the waiting requests to the caller and leaves the queue empty for the next tick.</summary>
	public ServerPresentationEvent[] Drain()
	{
		if (_events.Count == 0) return Array.Empty<ServerPresentationEvent>();

		ServerPresentationEvent[] drained = _events.ToArray();
		_events.Clear();
		return drained;
	}
}
