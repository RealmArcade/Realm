using System;
using System.Threading;

namespace Realm.ListenServer;

/// <summary>
///     The threads a publish describes peers on. They belong to the match rather than being borrowed from the thread
///     pool for each tick, so a tick never waits for the pool to decide to schedule its lanes: the fork is a wake and
///     the join is a count, both of which the tick thread owns.
/// </summary>
/// <remarks>
///     The handoff is a monitor around a per-tick fork, and it is the only synchronization here. The critical section
///     publishes the work and wakes the lanes and holds nothing else - no I/O, no callbacks, no nested lock, and no
///     waiting inside it - while the join is a countdown the tick thread blocks on, which is the bounded fork-join a
///     publish is allowed to spend. A lane index is its thread's index, so a lane never picks up another lane's peers.
/// </remarks>
public class PublishLaneWorkers : IDisposable
{
	private readonly object _gate = new();

	private readonly Thread[] _threads;

	private readonly CountdownEvent _finished;

	private Action<int>? _laneAction;
	private int _lanesToRun;
	private int _generation;
	private bool _disposed;

	public PublishLaneWorkers(int laneCount, string threadNamePrefix)
	{
		_threads = new Thread[laneCount];
		_finished = new CountdownEvent(1);

		for (int index = 0; index < laneCount; index++)
		{
			var thread = new Thread(RunLaneOnOwnedThread)
			{
				IsBackground = true,
				Name = $"{threadNamePrefix} {index}"
			};

			_threads[index] = thread;
			thread.Start(index);
		}
	}

	public int LaneCount => _threads.Length;

	public void DescribeInParallel(int laneCount, Action<int> laneAction)
	{
		int lanes = Math.Min(laneCount, _threads.Length);
		if (lanes <= 1)
		{
			laneAction(0);
			return;
		}

		_finished.Reset(lanes);

		// The work is published before the lanes are woken, and waking them is what carries those writes across.
		lock (_gate)
		{
			_laneAction = laneAction;
			_lanesToRun = lanes;
			_generation++;
			Monitor.PulseAll(_gate);
		}

		_finished.Wait();
	}

	private void RunLaneOnOwnedThread(object? state)
	{
		int laneIndex = (int)state!;
		int describedGeneration = 0;

		while (true)
		{
			Action<int> laneAction;

			lock (_gate)
			{
				while (_generation == describedGeneration && !_disposed) Monitor.Wait(_gate);
				if (_disposed) return;

				describedGeneration = _generation;

				// A publish that needs fewer lanes than this one leaves the rest parked rather than counting them out of a
				// join they were never part of.
				if (laneIndex >= _lanesToRun) continue;

				laneAction = _laneAction!;
			}

			try
			{
				laneAction(laneIndex);
			}
			finally
			{
				_finished.Signal();
			}
		}
	}

	public void Dispose()
	{
		if (_disposed) return;

		lock (_gate)
		{
			_disposed = true;
			Monitor.PulseAll(_gate);
		}

		for (int index = 0; index < _threads.Length; index++) _threads[index].Join();

		_finished.Dispose();
	}
}
