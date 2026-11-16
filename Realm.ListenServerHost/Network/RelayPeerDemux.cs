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

		_entries.Add(new OpenEntry(shim, token));
		return true;
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
	/// <param name="Shim">The loopback entry ENet speaks to for this connection.</param>
	/// <param name="Token">The connection the entry carries, which is what keeps one connection seated once.</param>
	private readonly record struct OpenEntry(RelayUdpShim Shim, RelayConnectionToken Token);
}
