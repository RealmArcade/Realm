// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Applies an RGBA color tint to the specified unit's 3D mesh.
	/// Use <see cref="Vector3.One"/> (white) to reset to the default appearance.
	/// </summary>
	/// <param name="unit">The unit to tint.</param>
	/// <param name="color">RGB color values in the range [0, 1].</param>
	void SetUnitColor(UnitId unit, Vector3 color);

	/// <summary>
	/// Sets the rotation angle or facing direction of the unit in radians or towards a target direction.
	/// </summary>
	/// <param name="unit">The unit whose facing direction is being set.</param>
	/// <param name="facingRadians">The target facing angle in radians around the vertical axis.</param>
	void SetUnitFacing(UnitId unit, float facingRadians);

	/// <summary>
	/// Sets the facing direction of the unit to look at the specified target position.
	/// </summary>
	/// <param name="unit">The unit to rotate.</param>
	/// <param name="targetPosition">The 3D coordinates in world space to face towards.</param>
	void SetUnitFacing(UnitId unit, Vector3 targetPosition)
	{
		Vector3 delta = targetPosition - unit.Position;
		float angle = MathF.Atan2(delta.X, delta.Z);
		SetUnitFacing(unit, angle);
	}

	/// <summary>
	/// Orders the unit to attack-move to the specified world position.
	/// The unit will attack any enemies it encounters along the way.
	/// </summary>
	/// <param name="unit">The unit to command.</param>
	/// <param name="destination">The destination position in world coordinates.</param>
	void IssueAttackMoveOrder(UnitId unit, Vector3 destination);

	/// <summary>
	/// Orders the unit to cast a targeted spell on a specific enemy unit.
	/// Does nothing if the unit does not have the specified ability.
	/// </summary>
	/// <param name="caster">The casting unit.</param>
	/// <param name="abilityId">The ability identifier string.</param>
	/// <param name="target">The target unit.</param>
	void IssueCastOrder(UnitId caster, string abilityId, UnitId target);

	/// <summary>
	/// Orders the unit to cast a ground-targeted spell at the specified world position.
	/// Does nothing if the unit does not have the specified ability.
	/// </summary>
	/// <param name="caster">The casting unit.</param>
	/// <param name="abilityId">The ability identifier string.</param>
	/// <param name="position">The target position in world coordinates.</param>
	void IssueCastOrderAt(UnitId caster, string abilityId, Vector3 position);

	/// <summary>
	/// Orders the unit to activate or deactivate an auto-cast ability.
	/// </summary>
	/// <param name="unit">The unit that owns the ability.</param>
	/// <param name="abilityId">The ability identifier string.</param>
	/// <param name="active">True to activate auto-cast; false to deactivate.</param>
	void SetAbilityAutoCast(UnitId unit, string abilityId, bool active);

	/// <summary>
	/// Transfers ownership of the specified unit to a different player slot.
	/// </summary>
	/// <param name="unit">The unit to transfer.</param>
	/// <param name="playerIndex">Zero-based player slot index of the new owner.</param>
	void SetUnitOwner(UnitId unit, int playerIndex);

	/// <summary>
	/// Plays a named visual animation on the unit's 3D model and equips any configured hand attachments for that animation.
	/// </summary>
	/// <param name="unit">The unit to animate.</param>
	/// <param name="animationName">The animation name (e.g., "Idle", "Labor", "Attack").</param>
	void SetUnitAnimation(UnitId unit, string animationName);

	/// <summary>
	/// Attaches or removes a 3D model object on the specified hand bone of the unit's rigged skeleton.
	/// </summary>
	/// <param name="unit">The target unit.</param>
	/// <param name="hand">The target hand ("RightHand" or "LeftHand").</param>
	/// <param name="attachmentId">The attachment identifier or model filename, or null/empty to clear the hand attachment.</param>
	void SetUnitHandAttachment(UnitId unit, string hand, string? attachmentId);

	/// <summary>
	/// Orders the unit to move to a world position without attacking along the way.
	/// Generic alias for <see cref="IssueMoveOrder(UnitId, Vector3)"/>.
	/// </summary>
	void MoveUnit(UnitId unit, Vector3 destination) => IssueMoveOrder(unit, destination);

	/// <summary>
	/// Returns living units within <paramref name="radius"/> of <paramref name="center"/>.
	/// Generic alias for <see cref="GetUnitsInRadius(Vector3, float)"/>.
	/// </summary>
	ReadOnlySpan<UnitId> GetUnitsInRange(Vector3 center, float radius) =>
		GetUnitsInRadius(center, radius);

	/// <summary>
	/// Subtracts <paramref name="damage"/> from the target's health. Kills the target at 0 HP.
	/// Does not apply armor. Safe to call from any map script.
	/// </summary>
	void DealDamage(UnitId attacker, UnitId target, float damage)
	{
		if (!target.IsValid || target.IsDead || damage <= 0f)
			return;
		_ = attacker;
		float next = target.Health - damage;
		if (next <= 0f)
		{
			target.Health = 0f;
			KillUnit(target);
			return;
		}

		target.Health = next;
	}

	/// <summary>
	/// Applies damage through the combat resolver, so the target's armor, the attacker's armor
	/// penetration and the damage/armor type matrix all apply. Use this for ability damage instead
	/// of setting health, which bypasses mitigation.
	/// </summary>
	/// <param name="attacker">The unit dealing the damage; its armor penetration applies.</param>
	/// <param name="target">The unit receiving the damage; its armor applies.</param>
	/// <param name="damage">The unmitigated damage amount, before armor and the type matrix.</param>
	/// <param name="damageType">Damage type key, such as normal, pierce, magic, fire or frost.</param>
	void DealTypedDamage(UnitId attacker, UnitId target, float damage, string damageType);

	/// <summary>
	/// Spawns a replacement unit of the same type at <paramref name="position"/> for the given player.
	/// Use this when the previous instance may already be destroyed (typical hero respawn).
	/// </summary>
	/// <param name="unitTypeId">The type identifier of the unit to spawn.</param>
	/// <param name="position">The spawn coordinates in 3D world space.</param>
	/// <param name="playerIndex">The zero-based player index to own the revived unit.</param>
	/// <returns>The revived unit instance.</returns>
	UnitId ReviveUnit(string unitTypeId, Vector3 position, int playerIndex) =>
		SpawnUnitForPlayer(unitTypeId, new Vector2(position.X, position.Z), playerIndex);

	/// <summary>
	/// Destroys <paramref name="unit"/> and spawns a new copy of its type at <paramref name="position"/> with the same owner player index.
	/// </summary>
	/// <param name="unit">The existing unit to replace.</param>
	/// <param name="position">The spawn coordinates in 3D world space.</param>
	/// <returns>The revived unit instance.</returns>
	UnitId ReviveUnit(UnitId unit, Vector3 position)
	{
		string typeId = unit.TemplateID;
		int playerIndex = unit.Player;
		DestroyUnit(unit);
		return SpawnUnitForPlayer(typeId, new Vector2(position.X, position.Z), playerIndex);
	}

	/// <summary>
	/// Issues an attack order targeting a specific enemy unit.
	/// </summary>
	/// <param name="unit">The attacking unit.</param>
	/// <param name="target">The target enemy unit.</param>
	void IssueAttackOrder(UnitId unit, UnitId target);

	/// <summary>
	/// Issues a resource gathering order targeting a harvestable resource node.
	/// </summary>
	/// <param name="unit">The harvesting unit.</param>
	/// <param name="resourceNode">The target resource node.</param>
	void IssueGatherOrder(UnitId unit, ResourceNodeId resourceNode);

	/// <summary>
	/// Orders the unit to stop its current action immediately.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	void IssueStopOrder(UnitId unit);

	/// <summary>
	/// Orders the unit to hold its ground and defend itself without moving.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	void IssueHoldPositionOrder(UnitId unit);

	/// <summary>
	/// Teleports the specified unit to the given coordinates in 3D world space.
	/// </summary>
	/// <param name="unit">The unit to teleport.</param>
	/// <param name="position">The destination coordinates in 3D world space.</param>
	void TeleportUnit(UnitId unit, Vector3 position);

	/// <summary>
	/// Stuns the specified unit for the given duration in seconds.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="duration">The stun duration in seconds.</param>
	void StunUnit(UnitId unit, float duration);

	/// <summary>
	/// Silences the specified unit, preventing ability casting for the given duration in seconds.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="duration">The silence duration in seconds.</param>
	void SilenceUnit(UnitId unit, float duration);
}
