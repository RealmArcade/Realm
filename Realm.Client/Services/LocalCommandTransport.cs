using System;

namespace Realm.Client.Services;

/// <summary>
/// A command that was submitted by a player whose client runs in the same process as the authority.
/// </summary>
public readonly struct PendingLocalCommand
{
	public readonly int SenderPeerId;
	public readonly byte[] Payload;

	public PendingLocalCommand(int senderPeerId, byte[] payload)
	{
		SenderPeerId = senderPeerId;
		Payload = payload;
	}
}

/// <summary>
/// Carries locally submitted commands to the authority inside the same process, replacing the packet hop that a
/// remote peer would use. Every player who is also the authority (single player, offline, or the hosting player of
/// a networked match) submits orders through this queue so their commands follow the same authoritative path.
/// </summary>
public class LocalCommandTransport
{
	private const int QueueCapacity = 256;

	private readonly PendingLocalCommand[] _queue = new PendingLocalCommand[QueueCapacity];
	private int _head;
	private int _tail;
	private int _count;
	private long _submittedCount;
	private long _deliveredCount;

	public bool HasPending => _count > 0;
	public long SubmittedCount => _submittedCount;
	public long DeliveredCount => _deliveredCount;

	public void Submit(int senderPeerId, byte[] payload)
	{
		if (payload == null || payload.Length == 0) return;

		if (_count == QueueCapacity)
		{
			_head = (_head + 1) % QueueCapacity;
			_count--;
		}

		_queue[_tail] = new PendingLocalCommand(senderPeerId, payload);
		_tail = (_tail + 1) % QueueCapacity;
		_count++;
		_submittedCount++;
	}

	public bool TryDequeue(out PendingLocalCommand command)
	{
		if (_count == 0)
		{
			command = default;
			return false;
		}

		command = _queue[_head];
		_queue[_head] = default;
		_head = (_head + 1) % QueueCapacity;
		_count--;
		_deliveredCount++;
		return true;
	}

	public void Clear()
	{
		Array.Clear(_queue);
		_head = 0;
		_tail = 0;
		_count = 0;
		_submittedCount = 0;
		_deliveredCount = 0;
	}
}
