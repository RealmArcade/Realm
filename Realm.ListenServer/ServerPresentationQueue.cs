using System.Numerics;
using Realm.Shared.Network;

namespace Realm.ListenServer;

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
	public const int Everyone = -1;

	public PresentationMessage ToMessage(int sequence)
	{
		return new PresentationMessage
		{
			Sequence = sequence,
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

public class ServerPresentationQueue
{
	private const int DurableCapacity = 512;

	private readonly Queue<ServerPresentationEvent> _events = new();
	private readonly Dictionary<string, ServerPresentationEvent> _durable = new(StringComparer.Ordinal);
	private readonly List<string> _durableOrder = new();

	public int PendingCount => _events.Count;

	public int DurableCount => _durableOrder.Count;

	public void Write(PresentationKind kind, int playerIndex, Vector3 position)
	{
		Write(kind, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty, null, position, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void Write(PresentationKind kind, int playerIndex, int handle, Vector3 position)
	{
		Write(kind, playerIndex, handle, string.Empty, string.Empty, string.Empty, string.Empty, null, position, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

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

	public void Write(in ServerPresentationEvent request)
	{
		_events.Enqueue(request);
		Retain(in request);
	}

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
				or PresentationKind.HudElementVisible or PresentationKind.CameraBoundsSet or PresentationKind.CameraPanned
				or PresentationKind.SelectionChanged => true,
			_ => false
		};
	}

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

	public ServerPresentationEvent[] Drain()
	{
		if (_events.Count == 0) return Array.Empty<ServerPresentationEvent>();

		ServerPresentationEvent[] drained = _events.ToArray();
		_events.Clear();
		return drained;
	}
}
