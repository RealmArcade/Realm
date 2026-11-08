using System;
using System.Collections.Generic;
using Godot;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     Holds frames the session has received but not yet handed to the world, split by what a later frame can stand in
///     for. Descriptions (snapshots and presentation batches) wait in a ring whose old entries may be dropped; everything
///     else waits on a queue that is drained in full and in order.
/// </summary>
internal sealed class SessionFrameQueue
{
	internal readonly struct QueuedFrame
	{
		public QueuedFrame(NetworkFrameKind kind, byte[] payload, ulong arrivalMilliseconds)
		{
			Kind = kind;
			Payload = payload;
			ArrivalMilliseconds = arrivalMilliseconds;
		}

		public NetworkFrameKind Kind { get; }
		public byte[] Payload { get; }

		public ulong ArrivalMilliseconds { get; }
	}

	/// <summary>
	///     How many descriptions the ring holds before the oldest is overwritten. A screen with this many waiting has
	///     stopped reading rather than met a link that is late, and the ones at its front describe a world the ones behind
	///     them already supersede.
	/// </summary>
	private const int MaximumQueuedDescriptions = 32;

	/// <summary>
	///     How many descriptions may wait to be applied before the older ones are dropped. A queue this deep is a screen
	///     that stopped reading for a fifth of a second, and the descriptions at its front describe a world the ones behind
	///     them already supersede.
	/// </summary>
	private const int MaximumQueuedDescriptionsBeforeDiscard = 6;

	/// <summary>
	///     Descriptions waiting to be applied, oldest first. They live in a ring because the oldest is dropped whenever a
	///     screen falls behind, and shifting the rest of a list to do it was work paid on the tick that could least afford
	///     it.
	/// </summary>
	private readonly QueuedFrame[] _descriptionQueue = new QueuedFrame[MaximumQueuedDescriptions];
	private int _descriptionHead;
	private int _descriptionCount;

	/// <summary>
	///     Frames the match cannot proceed without, in the order they arrived. Nothing waiting here is ever dropped: it
	///     came over the stream that guarantees delivery, and discarding it would throw away the only copy of something
	///     this screen needs.
	/// </summary>
	private readonly Queue<QueuedFrame> _reliableQueue = new();

	private bool _descriptionsWereDropped;

	public int ReliableCount => _reliableQueue.Count;

	public void Clear()
	{
		Array.Clear(_descriptionQueue);
		_descriptionHead = 0;
		_descriptionCount = 0;
		_reliableQueue.Clear();
		_descriptionsWereDropped = false;
	}

	public void Enqueue(NetworkFrameKind kind, byte[] payload)
	{
		ulong arrivalMilliseconds = kind == NetworkFrameKind.LatencyProbeEcho ? Time.GetTicksMsec() : 0UL;
		var frame = new QueuedFrame(kind, payload, arrivalMilliseconds);

		if (kind is not (NetworkFrameKind.Snapshot or NetworkFrameKind.Presentation))
		{
			_reliableQueue.Enqueue(frame);
			return;
		}

		if (_descriptionCount == _descriptionQueue.Length)
		{
			_descriptionQueue[_descriptionHead] = default;
			_descriptionHead = (_descriptionHead + 1) % _descriptionQueue.Length;
			_descriptionCount--;
			_descriptionsWereDropped = true;
		}

		_descriptionQueue[(_descriptionHead + _descriptionCount) % _descriptionQueue.Length] = frame;
		_descriptionCount++;
	}

	public bool TryDequeueReliable(out QueuedFrame frame)
	{
		if (_reliableQueue.Count == 0)
		{
			frame = default;
			return false;
		}

		frame = _reliableQueue.Dequeue();
		return true;
	}

	public bool TryPeekDescription(out QueuedFrame frame)
	{
		if (_descriptionCount == 0)
		{
			frame = default;
			return false;
		}

		frame = _descriptionQueue[_descriptionHead];
		return true;
	}

	public void PopDescription()
	{
		if (_descriptionCount == 0) return;

		_descriptionQueue[_descriptionHead] = default;
		_descriptionHead = (_descriptionHead + 1) % _descriptionQueue.Length;
		_descriptionCount--;
	}

	/// <summary>
	///     Keeps only the newest descriptions waiting to be applied. Nothing is repaired here: the hole the drops leave is
	///     what tells the apply path which run of descriptions this screen is missing, and it asks for exactly that run.
	/// </summary>
	public void DiscardSuperseded()
	{
		int queuedDescriptions = 0;
		for (int offset = 0; offset < _descriptionCount; offset++)
		{
			if (_descriptionQueue[(_descriptionHead + offset) % _descriptionQueue.Length].Kind == NetworkFrameKind.Snapshot) queuedDescriptions++;
		}

		if (queuedDescriptions <= MaximumQueuedDescriptionsBeforeDiscard) return;

		int toDiscard = queuedDescriptions - MaximumQueuedDescriptionsBeforeDiscard;
		while (toDiscard > 0 && _descriptionCount > 0)
		{
			QueuedFrame oldest = _descriptionQueue[_descriptionHead];
			_descriptionQueue[_descriptionHead] = default;
			_descriptionHead = (_descriptionHead + 1) % _descriptionQueue.Length;
			_descriptionCount--;

			if (oldest.Kind == NetworkFrameKind.Snapshot) toDiscard--;
		}
	}

	/// <summary>
	///     Reports whether a description was dropped since the last call, and clears the mark. A screen that lost one has
	///     a hole only asking for the world again can close.
	/// </summary>
	public bool TakeDroppedDescriptionMark()
	{
		if (!_descriptionsWereDropped) return false;

		_descriptionsWereDropped = false;
		return true;
	}
}
