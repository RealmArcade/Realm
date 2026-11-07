using System.Collections.Generic;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     Holds the units a description announced but this screen has not built yet, keyed by server entity id, so a unit
///     that arrives twice only keeps one pending description and the newest one wins.
/// </summary>
internal sealed class PendingUnitSpawnQueue
{
	private readonly Dictionary<int, UnitSnapshot> _pending = new();

	internal int Count => _pending.Count;

	internal bool Contains(int serverEntityId) => _pending.ContainsKey(serverEntityId);

	internal void Clear() => _pending.Clear();

	internal void Remove(int serverEntityId) => _pending.Remove(serverEntityId);

	/// <summary>
	///     Holds a unit this screen has not built yet until it has the budget to build it. A description of a unit that
	///     already has a definition on the wire leaves it out, so a unit waiting to be built keeps the one it arrived with
	///     rather than being queued a second time without it.
	/// </summary>
	internal void Queue(UnitSnapshot snap)
	{
		if (snap.UnitId.Length == 0 && _pending.TryGetValue(snap.EntityId, out UnitSnapshot queued)) snap.UnitId = queued.UnitId;

		_pending[snap.EntityId] = snap;
	}

	internal bool TryTakeAny(out UnitSnapshot snapshot)
	{
		snapshot = default;
		if (_pending.Count == 0) return false;

		int serverEntityId = 0;
		foreach (int candidate in _pending.Keys)
		{
			serverEntityId = candidate;
			break;
		}

		return _pending.Remove(serverEntityId, out snapshot);
	}
}
