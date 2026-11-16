using System;
using System.Collections.Generic;
using System.Text;
using Realm.Shared.Network.Relay;

namespace Realm.ListenServerHost.Network;

/// <summary>
///     The authority's side of every relayed connection: one loopback entry per joining player, each standing in front of
///     the game port as that player's own address. ENet therefore seats a relayed player exactly like a direct one and
///     keeps the same peer for the whole match, while the entry behind it changes which volunteer carries the datagrams
///     without ENet ever reconnecting.
/// </summary>
public sealed class RelayPeerDemux : IDisposable
{
	/// <summary>The most relayed connections one match keeps open at once.</summary>
	public const int MaximumEntries = 64;

	/// <summary>
	///     How long an entry must have been open before its silence is taken as the end of its connection. A player who drops
	///     and rejoins arrives with a new join and therefore a new entry, so an entry that has answered nothing for this long
	///     is a thread, a socket and a probe loop the entry keeps for somebody who is no longer here.
	/// </summary>
	public const int DeadEntryLifetimeMilliseconds = 30_000;

	/// <summary>
	///     How long an entry that carried nothing at all is kept. An entry is opened when the lobby server hands a join its
	///     volunteers, which is before the joining screen has tried anything, and that screen only reaches the entry once the
	///     lobby's own address has failed: its whole direct budget can pass before a single datagram arrives here. An entry
	///     therefore waits far longer than dead silence is waited out, because a connection that has not started yet and one
	///     that has gone are only told apart by the join still being on its way.
	/// </summary>
	public const int WaitingEntryLifetimeMilliseconds = 120_000;

	private readonly List<OpenEntry> _entries = new();
	private readonly int _gamePort;
	private bool _disposed;

	/// <summary>Creates the demux in front of one game port.</summary>
	/// <param name="gamePort">The port this match's ENet server listens on.</param>
	public RelayPeerDemux(int gamePort)
	{
		_gamePort = gamePort;
	}

	/// <summary>How many relayed connections this match is currently carrying.</summary>
	public int Count => _entries.Count;

	/// <summary>
	///     Opens one entry for one joining player. Every volunteer in the plan becomes a path of the same entry, so a
	///     connection the node offered three volunteers opens three paths and uses the best one measured.
	/// </summary>
	/// <param name="plan">The volunteers and the join ticket both peers agreed.</param>
	/// <param name="failure">Why no entry was opened.</param>
	/// <returns>Whether the connection is being carried.</returns>
	public bool TryOpen(RelayConnectionPlan plan, out string failure)
	{
		failure = string.Empty;
		if (_disposed)
		{
			failure = "the match is no longer carrying relayed connections";
			return false;
		}

		Maintain(Environment.TickCount64, null);

		// One connection is seated once. A plan that arrives again names the same ticket and therefore the same token, and a
		// second entry for it would forward the same datagrams to the game port from another loopback address, which ENet
		// cannot tell from a second peer claiming the same seat.
		RelayConnectionToken token = RelayJoinSecret.DeriveToken(plan.JoinTicket);
		for (int index = 0; index < _entries.Count; index++)
		{
			if (_entries[index].Token != token) continue;

			failure = $"this connection is already carried at loopback port {_entries[index].Shim.Port}";
			return false;
		}

		if (_entries.Count >= MaximumEntries)
		{
			failure = "this match is carrying as many relayed connections as it holds";
			return false;
		}

		RelayPathMux? mux = RelayConnectionFactory.TryBuild(plan, isAuthority: true, out string muxFailure);
		if (mux == null)
		{
			failure = muxFailure;
			return false;
		}

		var shim = new RelayUdpShim(mux, _gamePort);
		if (!shim.Start(out string shimFailure))
		{
			shim.Dispose();
			failure = shimFailure;
			return false;
		}

		_entries.Add(new OpenEntry(shim, token, Environment.TickCount64));
		return true;
	}

	/// <summary>
	///     Gives up the entries that are holding a socket, a lane and a probe loop for nothing. An entry that carried a
	///     datagram and has stopped being answered is a connection that ended; an entry that carried nothing at all is a join
	///     that either has not arrived yet or never will, and it is judged on the far longer lifetime its own wait is worth.
	///     It is called by the clock that runs the match and by every join, so a freed entry is freed the moment it is known
	///     to be free rather than when the next join happens to ask.
	/// </summary>
	/// <param name="nowMilliseconds">The clock the entries' ages are read against.</param>
	/// <param name="reportChange">
	///     Where a connection's path change or its removal is stated, or null when nothing is listening. It is how a captured
	///     authority log explains why a peer moved rather than only which path it ends up on, and it is called only when
	///     something actually changed.
	/// </param>
	public void Maintain(long nowMilliseconds, Action<string>? reportChange)
	{
		if (_disposed) return;

		for (int index = _entries.Count - 1; index >= 0; index--)
		{
			OpenEntry entry = _entries[index];

			RelayPathSummary stated = entry.Shim.Summary;
			string change = RelayPathChanges.Describe(entry.LastSummary, stated);
			entry.LastSummary = stated;
			if (change.Length > 0) reportChange?.Invoke($"loopback port {entry.Shim.Port}: {change}");

			long age = nowMilliseconds - entry.OpenedAtMilliseconds;
			if (entry.Shim.DeliveredToTransport == 0)
			{
				if (age < WaitingEntryLifetimeMilliseconds) continue;
				reportChange?.Invoke($"loopback port {entry.Shim.Port}: given up, no join arrived on it within {WaitingEntryLifetimeMilliseconds / 1000} s");
			}
			else
			{
				if (age < DeadEntryLifetimeMilliseconds) continue;
				if (stated.Best != null) continue;
				reportChange?.Invoke($"loopback port {entry.Shim.Port}: given up, nothing has answered on it for {DeadEntryLifetimeMilliseconds / 1000} s");
			}

			entry.Shim.Dispose();
			_entries.RemoveAt(index);
		}
	}

	/// <summary>
	///     States every relayed connection this match is carrying, which path each one uses and what it measured. It is what
	///     the authority's log and its diagnostics report state, in a shape another host's output compares line by line.
	/// </summary>
	/// <returns>One block per connection, or a line stating that no connection is relayed.</returns>
	public string Describe()
	{
		if (_entries.Count == 0) return "relay carrier=direct";

		var builder = new StringBuilder();
		builder.Append("relay connections=").Append(_entries.Count).Append('\n');
		for (int index = 0; index < _entries.Count; index++)
		{
			builder.Append(_entries[index].Shim.Describe());
		}

		return builder.ToString();
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		for (int index = 0; index < _entries.Count; index++) _entries[index].Shim.Dispose();
		_entries.Clear();
	}

	/// <summary>One seated connection: the entry standing in front of the game port, and the connection it carries.</summary>
	private sealed class OpenEntry
	{
		/// <summary>Creates the record of one entry.</summary>
		/// <param name="shim">The loopback entry ENet speaks to for this connection.</param>
		/// <param name="token">The connection the entry carries, which is what keeps one connection seated once.</param>
		/// <param name="openedAtMilliseconds">When the entry was opened, which is what tells a young entry from a dead one.</param>
		public OpenEntry(RelayUdpShim shim, RelayConnectionToken token, long openedAtMilliseconds)
		{
			Shim = shim;
			Token = token;
			OpenedAtMilliseconds = openedAtMilliseconds;
		}

		/// <summary>The loopback entry ENet speaks to for this connection.</summary>
		public RelayUdpShim Shim { get; }

		/// <summary>The connection this entry carries, which is what keeps one connection seated once.</summary>
		public RelayConnectionToken Token { get; }

		/// <summary>When the entry was opened, which is what tells a young entry from a dead one.</summary>
		public long OpenedAtMilliseconds { get; }

		/// <summary>What this connection's paths last stated, which the next statement is compared against.</summary>
		public RelayPathSummary LastSummary { get; set; } = RelayPathSummary.Empty;
	}
}
