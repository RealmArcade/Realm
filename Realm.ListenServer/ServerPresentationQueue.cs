using System.Numerics;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     One request from the scripting surface, addressed to a player or to everyone. It carries keys and numbers rather
///     than handles into the authority's memory, so a client can act on it without knowing anything the authority has
///     not told it. Which fields a request uses depends on its kind: a visual effect reads its type key, position and
///     scale, a camera pan reads the player it is addressed to, the place and the travel time.
/// </summary>
public readonly record struct ServerPresentationEvent(
	PresentationKind Kind,
	int PlayerIndex,
	int Handle,
	string Key,
	string Text,
	string Text2,
	string Text3,
	string[]? Values,
	Vector3 Position,
	Vector3 Secondary,
	Vector3 Color,
	int Count,
	int SecondCount,
	float Value,
	float Radius,
	float Duration,
	bool Flag)
{
	/// <summary>What a request addressed to no player in particular names: every player is meant to see it.</summary>
	public const int Everyone = -1;

	/// <summary>The request in the form that crosses the transport, so a client reads one shape for every kind.</summary>
	public PresentationMessage ToMessage()
	{
		return new PresentationMessage
		{
			Kind = (byte)Kind,
			PlayerIndex = PlayerIndex,
			Handle = Handle,
			Key = Key,
			Text = Text,
			Text2 = Text2,
			Text3 = Text3,
			Values = Values,
			Position = new NetworkVector3(Position.X, Position.Y, Position.Z),
			Secondary = new NetworkVector3(Secondary.X, Secondary.Y, Secondary.Z),
			Color = new NetworkVector3(Color.X, Color.Y, Color.Z),
			Count = Count,
			SecondCount = SecondCount,
			Value = Value,
			Radius = Radius,
			Duration = Duration,
			Flag = Flag
		};
	}
}

/// <summary>
///     Collects what the scripting surface wants shown, in the order the authority decided it, for the transport to
///     deliver to each client after the snapshot that introduces what the effect belongs to.
/// </summary>
public class ServerPresentationQueue
{
	private readonly Queue<ServerPresentationEvent> _events = new();

	/// <summary>How many requests are waiting to be delivered to clients.</summary>
	public int PendingCount => _events.Count;

	/// <summary>Records a request that carries only a kind, a player and a place.</summary>
	public void Write(PresentationKind kind, int playerIndex, Vector3 position)
	{
		Write(kind, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty, null, position, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	/// <summary>Records a request that carries a handle and a place, which is what removing something shown earlier does.</summary>
	public void Write(PresentationKind kind, int playerIndex, int handle, Vector3 position)
	{
		Write(kind, playerIndex, handle, string.Empty, string.Empty, string.Empty, string.Empty, null, position, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	/// <summary>Records a request that carries a player and a line of text.</summary>
	public void WriteText(PresentationKind kind, int playerIndex, string text, Vector3 color)
	{
		Write(kind, playerIndex, 0, string.Empty, text, string.Empty, string.Empty, null, Vector3.Zero, Vector3.Zero, color, 0, 0, 0f, 0f, 0f, false);
	}

	public void Write(
		PresentationKind kind,
		int playerIndex,
		int handle,
		string key,
		string text,
		string text2,
		string text3,
		string[]? values,
		Vector3 position,
		Vector3 secondary,
		Vector3 color,
		int count,
		int secondCount,
		float value,
		float radius,
		float duration,
		bool flag)
	{
		_events.Enqueue(new ServerPresentationEvent(
			kind,
			playerIndex,
			handle,
			key ?? string.Empty,
			text ?? string.Empty,
			text2 ?? string.Empty,
			text3 ?? string.Empty,
			values,
			position,
			secondary,
			color,
			count,
			secondCount,
			value,
			radius,
			duration,
			flag));
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
