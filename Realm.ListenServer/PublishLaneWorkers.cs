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
///     <para>
///         A lane that throws is contained rather than left to end the process: a description is a few hundred bytes and
///         one peer losing one of them costs it a baseline, while an unhandled exception on a background thread takes the
///         whole match down with every other peer in it. The first failure of a fork is reported with the lane it came
///         from and the fork answers that not every peer was described.
///     </para>
///     <para>
///         The join is bounded only in how often it reports: the tick cannot safely continue past a lane still writing the
///         description it owns, so a lane that outlives the report budget is waited for and said out loud on every budget
///         it outlives, rather than blocking the match silently or being abandoned while it writes.
///     </para>
/// </remarks>
public class PublishLaneWorkers : IDisposable
{
	private const int JoinReportMilliseconds = 500;

	private readonly object _gate = new();

	private readonly Thread[] _threads;

	private readonly CountdownEvent _finished;

	private Action<int>? _laneAction;
	private int _lanesToRun;
	private int _generation;
	private bool _disposed;

	private Exception? _forkFailure;
	private int _failedLane = -1;

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

	/// <summary>
	///     Where a lane that failed, or a fork that outlived its report budget, says so. It is written on the tick thread
	///     after the join and on the reporting loop that follows it, never from inside a critical section.
	/// </summary>
	public Action<string>? Report { get; set; }

	public int LaneCount => _threads.Length;

	/// <summary>
	///     Describes the peers of this fork across its lanes and joins them.
	/// </summary>
	/// <returns>
	///     Whether every lane described the peers it was given. A lane that failed leaves the peers it had not reached
	///     without a description for this tick, which is the caller's to answer with a baseline.
	/// </returns>
	public bool DescribeInParallel(int laneCount, Action<int> laneAction)
	{
		int lanes = Math.Min(laneCount, _threads.Length);
		if (lanes <= 1)
		{
			laneAction(0);
			return true;
		}

		_forkFailure = null;
		_failedLane = -1;
		_finished.Reset(lanes);

		// The work is published before the lanes are woken, and waking them is what carries those writes across.
		lock (_gate)
		{
			_laneAction = laneAction;
			_lanesToRun = lanes;
			_generation++;
			Monitor.PulseAll(_gate);
		}

		while (!_finished.Wait(JoinReportMilliseconds))
		{
			Report?.Invoke($"the publish lanes have not joined after {JoinReportMilliseconds} ms on a fork that described {lanes} peer(s); this tick is held until they finish, so no description is being written under it");
		}

		Exception? failure = _forkFailure;
		if (failure == null) return true;

		Report?.Invoke($"publish lane {_failedLane} failed while describing its peers: {failure}");
		_forkFailure = null;
		_failedLane = -1;
		return false;
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
			catch (Exception exception)
			{
				RecordFailure(laneIndex, exception);
			}
			finally
			{
				_finished.Signal();
			}
		}
	}

	/// <summary>
	///     Keeps the first failure of this fork. The lane that raised it stops describing its peers where it was, so the
	///     description it was writing is one no peer is sent, and the report names the lane rather than the last of a
	///     cascade of consequences.
	/// </summary>
	private void RecordFailure(int laneIndex, Exception exception)
	{
		if (Interlocked.CompareExchange(ref _forkFailure, exception, null) != null) return;

		_failedLane = laneIndex;
	}

	public void Dispose()
	{
		if (_disposed) return;

		lock (_gate)
		{
			_disposed = true;
			Monitor.PulseAll(_gate);
		}

		for (int index = 0; index < _threads.Length; index++)
		{
			if (_threads[index].Join(JoinReportMilliseconds)) continue;

			Report?.Invoke($"publish lane {index} did not stop within {JoinReportMilliseconds} ms of the match ending");
		}

		_finished.Dispose();
	}
}
