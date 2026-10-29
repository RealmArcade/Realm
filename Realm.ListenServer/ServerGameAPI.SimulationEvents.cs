using System.Numerics;
using Realm.Ecs;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	internal void RaiseUnitCreated(int unitId)
	{
		if (unitId <= 0) return;

		OnUnitCreated?.Invoke(new UnitId(unitId));
	}

	internal void RaiseUnitDied(int unitId, int killerUnitId)
	{
		if (unitId <= 0) return;

		UnitId killer = killerUnitId > 0 && _context.World.IsAlive(new Entity(killerUnitId)) ? new UnitId(killerUnitId) : UnitId.Invalid;
		OnUnitDied?.Invoke(new UnitId(unitId), killer);
	}

	internal void RaiseUnitDamaged(int unitId, int attackerUnitId, float damage)
	{
		if (unitId <= 0) return;

		OnUnitDamaged?.Invoke(new UnitId(unitId), attackerUnitId > 0 ? new UnitId(attackerUnitId) : UnitId.Invalid, damage);
	}

	internal void RaiseUnitAttacked(int attackerUnitId, int targetUnitId)
	{
		if (attackerUnitId <= 0) return;

		OnUnitAttacked?.Invoke(new UnitId(attackerUnitId), new UnitId(targetUnitId));
	}

	internal void RaiseConstructionFinished(int unitId)
	{
		if (unitId <= 0) return;

		OnConstructionFinished?.Invoke(new UnitId(unitId));
	}

	internal void RaisePlayerChatMessage(int playerIndex, string message, int senderUnitId)
	{
		if (string.IsNullOrWhiteSpace(message)) return;

		UnitId sender = senderUnitId > 0 && _context.World.IsAlive(new Entity(senderUnitId)) ? new UnitId(senderUnitId) : UnitId.Invalid;
		OnPlayerChatMessage?.Invoke(message, sender);
		_log.Write($"player {playerIndex} sent the map a console command");
	}

	internal void RaiseUnitSelected(int unitId)
	{
		if (unitId <= 0) return;

		OnUnitSelected?.Invoke(new UnitId(unitId));
	}

	internal void RaiseUnitOrdered(int unitId, string order, Vector3 position)
	{
		if (unitId <= 0) return;

		OnUnitOrdered?.Invoke(new UnitId(unitId), order, position);
	}
}
