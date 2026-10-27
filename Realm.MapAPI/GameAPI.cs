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
		public void SetUnitHealth(UnitId unit, float value) => gameApi.SetUnitHealth(unit, value);

		public void SetUnitMaxHealth(UnitId unit, float value) => gameApi.SetUnitMaxHealth(unit, value);

		public void SetUnitMana(UnitId unit, float value) => gameApi.SetUnitMana(unit, value);

		public void SetUnitMaxMana(UnitId unit, float value) => gameApi.SetUnitMaxMana(unit, value);

		public void SetUnitManaRegen(UnitId unit, float value) => gameApi.SetUnitManaRegen(unit, value);

		public void SetUnitDamage(UnitId unit, float value) => gameApi.SetUnitDamage(unit, value);

		public void SetUnitRange(UnitId unit, float value) => gameApi.SetUnitRange(unit, value);

		public void SetUnitArmor(UnitId unit, float value) => gameApi.SetUnitArmor(unit, value);

		public void SetUnitSpeed(UnitId unit, float value) => gameApi.SetUnitSpeed(unit, value);

		public void SetUnitAttackSpeed(UnitId unit, float value) => gameApi.SetUnitAttackSpeed(unit, value);

		public void SetUnitOwner(UnitId unit, int playerIndex) => gameApi.SetUnitOwner(unit, playerIndex);

		public void SetUnitLevel(UnitId unit, int level) => gameApi.SetUnitLevel(unit, level);

		public void SetUnitExperience(UnitId unit, float value) => gameApi.SetUnitExperience(unit, value);

		public void SetUnitScale(UnitId unit, float value) => gameApi.SetUnitScale(unit, value);

		public void SetUnitInvulnerable(UnitId unit, bool value) => gameApi.SetUnitInvulnerable(unit, value);

		public void SetUnitAnimationsFrozen(UnitId unit, bool value) => gameApi.SetUnitAnimationsFrozen(unit, value);

		public void SetUnitCommandsFrozen(UnitId unit, bool value) => gameApi.SetUnitCommandsFrozen(unit, value);

		public void SetUnitFrozen(UnitId unit, bool value) => gameApi.SetUnitFrozen(unit, value);

		public void SetUnitHideHealthAndManaBars(UnitId unit, bool value) => gameApi.SetUnitHideHealthAndManaBars(unit, value);

		public void SetUnitFacing(UnitId unit, float facingRadians) => gameApi.SetUnitFacing(unit, facingRadians);

		public void SetUnitName(UnitId unit, string value) => gameApi.SetUnitName(unit, value);

		public string GetUnitInstanceName(UnitId unit) => gameApi.GetUnitInstanceName(unit);

		public void SetPlayerComputerControlled(int playerIndex, bool isProxy) => gameApi.SetPlayerComputerControlled(playerIndex, isProxy);

		public void SetPlayerTeam(int playerIndex, int teamIndex) => gameApi.SetPlayerTeam(playerIndex, teamIndex);

		public void SetPlayerGold(int playerIndex, float amount) => gameApi.SetPlayerGold(playerIndex, amount);

		public void SetPlayerWood(int playerIndex, float amount) => gameApi.SetPlayerWood(playerIndex, amount);

		public void SetPlayerStone(int playerIndex, float amount) => gameApi.SetPlayerStone(playerIndex, amount);

		public void SetPlayerMaxPopulation(int playerIndex, int max) => gameApi.SetPlayerMaxPopulation(playerIndex, max);

		public void SetPlayerKills(int playerIndex, int kills) => gameApi.SetPlayerKills(playerIndex, kills);

		public void SetResourceNodeAmount(ResourceNodeId node, float value) => gameApi.SetResourceNodeAmount(node, value);

		public float GetResourceNodeAmount(ResourceNodeId node) => gameApi.GetResourceNodeAmount(node);
	}
}
