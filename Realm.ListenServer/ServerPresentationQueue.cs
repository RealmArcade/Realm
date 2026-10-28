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
/// <remarks>
///     A map declares most of what a screen offers while it sets the match up, which is before the first peer has
///     joined. Those requests describe a card, an icon, a colour or a setting that stays true for the rest of the
///     match, so the latest of each is kept and handed to a peer the moment it joins. Everything else is a moment:
///     a burst, a line of text, a ping. Those are dropped when nobody could see them rather than replayed later.
/// </remarks>
public class ServerPresentationQueue
{
	/// <summary>How many durable requests are kept, so a long match cannot grow this list without limit.</summary>
	private const int DurableCapacity = 512;

	private readonly Queue<ServerPresentationEvent> _events = new();
	private readonly Dictionary<string, ServerPresentationEvent> _durable = new(StringComparer.Ordinal);
	private readonly List<string> _durableOrder = new();

	/// <summary>How many requests are waiting to be delivered to clients.</summary>
	public int PendingCount => _events.Count;

	/// <summary>How many requests are kept for the peers that join later.</summary>
	public int DurableCount => _durableOrder.Count;

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
		Write(new ServerPresentationEvent(
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

	/// <summary>
	///     Queues a request that was already assembled, and keeps it for the peers that join later when it describes
	///     something that stays true for the rest of the match.
	/// </summary>
	public void Write(in ServerPresentationEvent request)
	{
		_events.Enqueue(request);
		Retain(in request);
	}

	/// <summary>
	///     The durable requests a player joining now has to be told, in the order the authority first decided them.
	///     A request addressed to one player is only handed to the peer acting as that player.
	/// </summary>
	public ServerPresentationEvent[] ReplayDurable(int playerSlot)
	{
		if (_durableOrder.Count == 0) return Array.Empty<ServerPresentationEvent>();

		List<ServerPresentationEvent> replay = new(_durableOrder.Count);
		for (int index = 0; index < _durableOrder.Count; index++)
		{
			ServerPresentationEvent request = _durable[_durableOrder[index]];
			if (request.PlayerIndex != ServerPresentationEvent.Everyone && request.PlayerIndex != playerSlot) continue;

			replay.Add(request);
		}

		return replay.Count == 0 ? Array.Empty<ServerPresentationEvent>() : replay.ToArray();
	}

	/// <summary>Whether a request about a subject that outlives the moment it was made in.</summary>
	private static bool IsDurable(PresentationKind kind)
	{
		return kind switch
		{
			PresentationKind.AbilityRegistered or PresentationKind.PlayerAbilityRegistered or PresentationKind.AbilityInstantSet
				or PresentationKind.AbilityIconSet or PresentationKind.AbilityTooltipSet or PresentationKind.AbilityHotkeySet
				or PresentationKind.AbilityGridPositionSet or PresentationKind.ItemTooltipSet or PresentationKind.TooltipNumberSet
				or PresentationKind.TooltipTextSet or PresentationKind.TechLevelSet or PresentationKind.UnitColored
				or PresentationKind.UnitAnimationSet or PresentationKind.UnitHandAttachmentSet or PresentationKind.TimeOfDaySet
				or PresentationKind.DayNightCycleSet or PresentationKind.EnvironmentPresetSet or PresentationKind.WeatherSet
				or PresentationKind.HudElementVisible or PresentationKind.CameraBoundsSet => true,
			_ => false
		};
	}

	/// <summary>
	///     What a request is about. A request about the same subject replaces the one before it, so a card whose cost was
	///     changed three times is described once and a unit that was tinted twice is tinted the last colour.
	/// </summary>
	private static string SubjectOf(in ServerPresentationEvent request)
	{
		return request.Kind switch
		{
			PresentationKind.TooltipNumberSet or PresentationKind.TooltipTextSet => $"{(byte)request.Kind}|{request.PlayerIndex}|{request.Key}|{request.Text}|{request.Text2}",
			PresentationKind.UnitHandAttachmentSet => $"{(byte)request.Kind}|{request.PlayerIndex}|{request.Handle}|{request.Key}",
			PresentationKind.HudElementVisible => $"{(byte)request.Kind}|{request.PlayerIndex}|{request.Key}",
			_ => $"{(byte)request.Kind}|{request.PlayerIndex}|{request.Key}"
		};
	}

	private void Retain(in ServerPresentationEvent request)
	{
		if (!IsDurable(request.Kind)) return;

		string subject = SubjectOf(in request);
		if (!_durable.ContainsKey(subject))
		{
			if (_durableOrder.Count >= DurableCapacity) ForgetOldest();

			_durableOrder.Add(subject);
		}

		_durable[subject] = request;
	}

	private void ForgetOldest()
	{
		if (_durableOrder.Count == 0) return;

		_durable.Remove(_durableOrder[0]);
		_durableOrder.RemoveAt(0);
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
