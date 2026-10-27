namespace Realm.Ecs;

/// <summary>
/// The commands a handle property sends when a script assigns to it, implemented by whoever runs the map script.
/// </summary>
public interface IHandleCommands
{
	void SetUnitHealth(UnitId unit, float value);

	void SetUnitMaxHealth(UnitId unit, float value);

	void SetUnitMana(UnitId unit, float value);

	void SetUnitMaxMana(UnitId unit, float value);

	void SetUnitManaRegen(UnitId unit, float value);

	void SetUnitDamage(UnitId unit, float value);

	void SetUnitRange(UnitId unit, float value);

	void SetUnitArmor(UnitId unit, float value);

	void SetUnitSpeed(UnitId unit, float value);

	void SetUnitAttackSpeed(UnitId unit, float value);

	void SetUnitOwner(UnitId unit, int playerIndex);

	void SetUnitLevel(UnitId unit, int level);

	void SetUnitExperience(UnitId unit, float value);

	void SetUnitScale(UnitId unit, float value);

	void SetUnitInvulnerable(UnitId unit, bool value);

	void SetUnitAnimationsFrozen(UnitId unit, bool value);

	void SetUnitCommandsFrozen(UnitId unit, bool value);

	void SetUnitFrozen(UnitId unit, bool value);

	void SetUnitHideHealthAndManaBars(UnitId unit, bool value);

	void SetUnitFacing(UnitId unit, float facingRadians);

	void SetUnitName(UnitId unit, string value);

	string GetUnitInstanceName(UnitId unit);

	void SetPlayerComputerControlled(int playerIndex, bool isProxy);

	void SetPlayerTeam(int playerIndex, int teamIndex);

	void SetPlayerGold(int playerIndex, float amount);

	void SetPlayerWood(int playerIndex, float amount);

	void SetPlayerStone(int playerIndex, float amount);

	void SetPlayerMaxPopulation(int playerIndex, int max);

	void SetPlayerKills(int playerIndex, int kills);

	void SetResourceNodeAmount(ResourceNodeId node, float value);

	float GetResourceNodeAmount(ResourceNodeId node);
}

/// <summary>
/// Holds the command sink that handle properties reach, which is the map script api of the process running them.
/// </summary>
public static class HandleCommands
{
	public static IHandleCommands? Current { get; set; }
}
