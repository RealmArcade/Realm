using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	public void RaiseUnitCreated(int unitId)
	{
		if (unitId <= 0) return;

		ApplyResearchedUpgrades(unitId);

		OnUnitCreated?.Invoke(new UnitId(unitId));
	}

	/// <summary>
	///     Research belongs to a player's army rather than to the units that happened to exist when it was bought, so a
	///     unit that appears afterwards - trained, built or spawned by the map - is given the same modifiers now.
	/// </summary>
	private void ApplyResearchedUpgrades(int unitId)
	{
		var entity = new Entity(unitId);
		if (!_context.World.IsAlive(entity)) return;
		if (!_context.World.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer owner)) return;

		TechLevels.ApplyAllToEntity(owner.Value, entity);
	}

	public void RaiseUnitDied(int unitId, int killerUnitId)
	{
		if (unitId <= 0) return;

		UnitId killer = killerUnitId > 0 && _context.World.IsAlive(new Entity(killerUnitId)) ? new UnitId(killerUnitId) : UnitId.Invalid;
		OnUnitDied?.Invoke(new UnitId(unitId), killer);
	}

	public void RaiseUnitDamaged(int unitId, int attackerUnitId, float damage, float damageBeforeMitigation)
	{
		if (unitId <= 0) return;

		UnitId victim = new(unitId);
		UnitId attacker = attackerUnitId > 0 ? new UnitId(attackerUnitId) : UnitId.Invalid;
		OnUnitDamaged?.Invoke(victim, attacker, damage);
		OnUnitDamagedBeforeMitigation?.Invoke(victim, attacker, damageBeforeMitigation);
	}

	public void RaiseUnitAttacked(int attackerUnitId, int targetUnitId)
	{
		if (attackerUnitId <= 0) return;

		OnUnitAttacked?.Invoke(new UnitId(attackerUnitId), new UnitId(targetUnitId));
	}

	public void RaiseConstructionFinished(int unitId)
	{
		if (unitId <= 0) return;

		OnConstructionFinished?.Invoke(new UnitId(unitId));
	}

	public void RaisePlayerChatMessage(int playerIndex, string message, int senderUnitId)
	{
		if (string.IsNullOrWhiteSpace(message)) return;

		UnitId sender = senderUnitId > 0 && _context.World.IsAlive(new Entity(senderUnitId)) ? new UnitId(senderUnitId) : UnitId.Invalid;
		OnPlayerChatMessage?.Invoke(message, sender, playerIndex);
		_log.Write($"player {playerIndex} sent the map a console command");
	}

	public void RaiseUnitSelected(int unitId)
	{
		if (unitId <= 0) return;

		OnUnitSelected?.Invoke(new UnitId(unitId));
	}

	public void RaiseConsumableChanged(int unitId, string consumableId)
	{
		if (unitId <= 0) return;

		OnConsumableChanged?.Invoke(new UnitId(unitId), consumableId ?? string.Empty);
	}

	public void RaiseUnitOrdered(int unitId, string order, Vector3 position)
	{
		if (unitId <= 0) return;

		OnUnitOrdered?.Invoke(new UnitId(unitId), order, position);
	}
}
