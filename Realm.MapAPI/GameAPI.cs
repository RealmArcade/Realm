using System.Runtime.InteropServices;
using Realm.Ecs;

namespace Realm.MapAPI;

/// <summary>
/// Provides global access to the current <see cref="IGameAPI"/> instance within the WebAssembly or host runtime execution context.
/// </summary>
public static class GameAPI
{
	private static IGameAPI _current = null!;

	/// <summary>
	/// Gets or sets the active <see cref="IGameAPI"/> instance.
	/// </summary>
	/// <remarks>
	/// Setting the instance also gives the shared handles somewhere to send an assignment, and lets the text table
	/// answer identifiers this process never registered, which only the sandbox can do: its host owns every text a
	/// map script may be asked to render. A process that hosts the world itself registers the text it writes and
	/// keeps no answerer.
	/// </remarks>
	public static IGameAPI Current
	{
		get => _current;

		set
		{
			_current = value;
			HandleCommands.Current = value == null ? null : new HandleCommandSink(value);
			StringRegistry.UnregisteredTextSource =
				value != null && RuntimeInformation.ProcessArchitecture == Architecture.Wasm ? value.GetStringText : null;
		}
	}

	private class HandleCommandSink(IGameAPI gameApi) : IHandleCommands
	{
		/// <summary>Sets the unit's current health points.</summary>
		public void SetUnitHealth(UnitId unit, float value) => gameApi.SetUnitHealth(unit, value);

		/// <summary>Sets the unit's maximum health points.</summary>
		public void SetUnitMaxHealth(UnitId unit, float value) => gameApi.SetUnitMaxHealth(unit, value);

		/// <summary>Sets the unit's current mana points.</summary>
		public void SetUnitMana(UnitId unit, float value) => gameApi.SetUnitMana(unit, value);

		/// <summary>Sets the unit's maximum mana points.</summary>
		public void SetUnitMaxMana(UnitId unit, float value) => gameApi.SetUnitMaxMana(unit, value);

		/// <summary>Sets how much mana the unit regenerates per second.</summary>
		public void SetUnitManaRegen(UnitId unit, float value) => gameApi.SetUnitManaRegen(unit, value);

		/// <summary>Sets the unit's attack damage.</summary>
		public void SetUnitDamage(UnitId unit, float value) => gameApi.SetUnitDamage(unit, value);

		/// <summary>Sets the unit's attack range in metres.</summary>
		public void SetUnitRange(UnitId unit, float value) => gameApi.SetUnitRange(unit, value);

		/// <summary>Sets the unit's armor value.</summary>
		public void SetUnitArmor(UnitId unit, float value) => gameApi.SetUnitArmor(unit, value);

		/// <summary>Sets the unit's movement speed in metres per second.</summary>
		public void SetUnitSpeed(UnitId unit, float value) => gameApi.SetUnitSpeed(unit, value);

		/// <summary>Sets how many attacks per second the unit makes.</summary>
		public void SetUnitAttackSpeed(UnitId unit, float value) => gameApi.SetUnitAttackSpeed(unit, value);

		/// <summary>Sets which player slot owns the unit.</summary>
		public void SetUnitOwner(UnitId unit, int playerIndex) => gameApi.SetUnitOwner(unit, playerIndex);

		/// <summary>Sets the unit's level.</summary>
		public void SetUnitLevel(UnitId unit, int level) => gameApi.SetUnitLevel(unit, level);

		/// <summary>Sets the unit's accumulated experience.</summary>
		public void SetUnitExperience(UnitId unit, float value) => gameApi.SetUnitExperience(unit, value);

		/// <summary>Sets the unit's visual scale multiplier.</summary>
		public void SetUnitScale(UnitId unit, float value) => gameApi.SetUnitScale(unit, value);

		/// <summary>Turns damage immunity on or off for the unit.</summary>
		public void SetUnitInvulnerable(UnitId unit, bool value) => gameApi.SetUnitInvulnerable(unit, value);

		/// <summary>Turns animation playback on or off for the unit.</summary>
		public void SetUnitAnimationsFrozen(UnitId unit, bool value) => gameApi.SetUnitAnimationsFrozen(unit, value);

		/// <summary>Turns order acceptance on or off for the unit.</summary>
		public void SetUnitCommandsFrozen(UnitId unit, bool value) => gameApi.SetUnitCommandsFrozen(unit, value);

		/// <summary>Locks the unit to the map script that drives it, or hands it back to its player.</summary>
		public void SetUnitPlayerOrdersLocked(UnitId unit, bool value) => gameApi.SetUnitPlayerOrdersLocked(unit, value);

		/// <summary>States what one gathering swing takes from a node and how long the swing lasts.</summary>
		public void SetUnitGatherInterval(UnitId unit, float swingSeconds) => gameApi.SetUnitGatherInterval(unit, swingSeconds);

		/// <summary>States the share of each delivered swing the map keeps.</summary>
		public void SetUnitGatherTaxRate(UnitId unit, float taxRate) => gameApi.SetUnitGatherTaxRate(unit, taxRate);

		/// <summary>Turns all unit activity on or off, covering movement, orders and animation together.</summary>
		public void SetUnitFrozen(UnitId unit, bool value) => gameApi.SetUnitFrozen(unit, value);

		/// <summary>Hides or shows the unit's health and mana bars.</summary>
		public void SetUnitHideHealthAndManaBars(UnitId unit, bool value) => gameApi.SetUnitHideHealthAndManaBars(unit, value);

		/// <summary>Sets the unit's facing direction in radians.</summary>
		public void SetUnitFacing(UnitId unit, float facingRadians) => gameApi.SetUnitFacing(unit, facingRadians);

		/// <summary>Sets the unit's display name.</summary>
		public void SetUnitName(UnitId unit, string value) => gameApi.SetUnitName(unit, value);

		/// <summary>Answers the unit's instance name, which distinguishes two units of the same template.</summary>
		public string GetUnitInstanceName(UnitId unit) => gameApi.GetUnitInstanceName(unit);

		/// <summary>Turns computer control on or off for a player slot.</summary>
		public void SetPlayerComputerControlled(int playerIndex, bool isProxy) => gameApi.SetPlayerComputerControlled(playerIndex, isProxy);

		/// <summary>Assigns the player slot to a team.</summary>
		public void SetPlayerTeam(int playerIndex, int teamIndex) => gameApi.SetPlayerTeam(playerIndex, teamIndex);

		/// <summary>Sets the player slot's gold.</summary>
		public void SetPlayerGold(int playerIndex, float amount) => gameApi.SetPlayerGold(playerIndex, amount);

		/// <summary>Sets the player slot's wood.</summary>
		public void SetPlayerWood(int playerIndex, float amount) => gameApi.SetPlayerWood(playerIndex, amount);

		/// <summary>Sets the player slot's stone.</summary>
		public void SetPlayerStone(int playerIndex, float amount) => gameApi.SetPlayerStone(playerIndex, amount);

		/// <summary>Sets the player slot's population cap.</summary>
		public void SetPlayerMaxPopulation(int playerIndex, int max) => gameApi.SetPlayerMaxPopulation(playerIndex, max);

		/// <summary>Sets the player slot's kill count.</summary>
		public void SetPlayerKills(int playerIndex, int kills) => gameApi.SetPlayerKills(playerIndex, kills);

		/// <summary>Sets how much of the resource remains in the node.</summary>
		public void SetResourceNodeAmount(ResourceNodeId node, float value) => gameApi.SetResourceNodeAmount(node, value);

		/// <summary>Answers how much of the resource remains in the node.</summary>
		public float GetResourceNodeAmount(ResourceNodeId node) => gameApi.GetResourceNodeAmount(node);
	}
}
