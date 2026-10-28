using System.Numerics;
using Realm.Ecs;

namespace Realm.ListenServer;

/// <summary>
///     The authority telling a map what its own simulation did: a unit entered the world, a unit died, damage landed, a
///     fight started, a site was finished, a player typed a line. A map reacts to the authority's account of the match
///     rather than to what one screen believed happened.
/// </summary>
public partial class ServerGameAPI
{
	/// <summary>Raises the event a map subscribed to for a unit the authority just created.</summary>
	internal void RaiseUnitCreated(int unitId)
	{
		if (unitId <= 0) return;

		OnUnitCreated?.Invoke(new UnitId(unitId));
	}

	/// <summary>Raises the event a map subscribed to for a unit the authority just killed, with what killed it.</summary>
	internal void RaiseUnitDied(int unitId, int killerUnitId)
	{
		if (unitId <= 0) return;

		UnitId killer = killerUnitId > 0 && _context.World.IsAlive(new Entity(killerUnitId)) ? new UnitId(killerUnitId) : UnitId.Invalid;
		OnUnitDied?.Invoke(new UnitId(unitId), killer);
	}

	/// <summary>Raises the event a map subscribed to for damage the authority resolved.</summary>
	internal void RaiseUnitDamaged(int unitId, int attackerUnitId, float damage)
	{
		if (unitId <= 0) return;

		OnUnitDamaged?.Invoke(new UnitId(unitId), attackerUnitId > 0 ? new UnitId(attackerUnitId) : UnitId.Invalid, damage);
	}

	/// <summary>Raises the event a map subscribed to for a fight the authority started.</summary>
	internal void RaiseUnitAttacked(int attackerUnitId, int targetUnitId)
	{
		if (attackerUnitId <= 0) return;

		OnUnitAttacked?.Invoke(new UnitId(attackerUnitId), new UnitId(targetUnitId));
	}

	/// <summary>Raises the event a map subscribed to for a building that reached its build time.</summary>
	internal void RaiseConstructionFinished(int unitId)
	{
		if (unitId <= 0) return;

		OnConstructionFinished?.Invoke(new UnitId(unitId));
	}

	/// <summary>
	///     Raises the event a map subscribed to for a line a player typed into its console, with the unit that player had
	///     selected so a command naming one is answered about the unit its sender meant.
	/// </summary>
	internal void RaisePlayerChatMessage(int playerIndex, string message, int senderUnitId)
	{
		if (string.IsNullOrWhiteSpace(message)) return;

		UnitId sender = senderUnitId > 0 && _context.World.IsAlive(new Entity(senderUnitId)) ? new UnitId(senderUnitId) : UnitId.Invalid;
		OnPlayerChatMessage?.Invoke(message, sender);
		_log.Write($"player {playerIndex} sent the map a console command");
	}

	/// <summary>Raises the event a map subscribed to for a unit a player selected on their own screen.</summary>
	internal void RaiseUnitSelected(int unitId)
	{
		if (unitId <= 0) return;

		OnUnitSelected?.Invoke(new UnitId(unitId));
	}

	/// <summary>Raises the event a map subscribed to for a unit a player ordered somewhere.</summary>
	internal void RaiseUnitOrdered(int unitId, string order, Vector3 position)
	{
		if (unitId <= 0) return;

		OnUnitOrdered?.Invoke(new UnitId(unitId), order, position);
	}
}
