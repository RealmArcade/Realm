using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.MapAPI;

namespace Realm.ListenServer;

/// <summary>
///     The part of the map scripting surface the authority does not answer yet. Every one of these members is
///     reached by a map script through a host import, so an unanswered call refuses loudly instead of quietly
///     changing nothing, and each is replaced by a real answer as the authority grows one.
/// </summary>
public partial class ServerGameAPI
{
	public event Action<UnitId>? OnUnitCreated;
	public event Action<UnitId, UnitId>? OnUnitDied;
	public event Action<UnitId, UnitId, float>? OnUnitDamaged;
	public event Action<UnitId, string, Vector3>? OnSpellCast;
	public event Action<string, UnitId>? OnPlayerChatMessage;
	public event Action<UnitId>? OnUnitSelected;
	public event Action<UnitId, string>? OnConsumableChanged;
	public event Action<UnitId, UnitId>? OnUnitAttacked;
	public event Action<LootItemId, UnitId>? OnLootItemCollected;
	public event Action<int>? OnTimerExpired;
	public event Action<UnitId, int>? OnUnitEnterZone;
	public event Action<int>? OnPlayerLeft;
	public event Action<UnitId, string>? OnItemSold;
	public event Action<UnitId>? OnConstructionFinished;
	public event Action<UnitId, string, Vector3>? OnUnitOrdered;
	public event Action<int, string, string, Vector3, string>? OnBotCustomActionExecuted;

	public void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition) => throw new NotSupportedException("CastAbility is not answered by the authority yet");

	public void IssueCastOrder(UnitId caster, string abilityId, UnitId target) => throw new NotSupportedException("IssueCastOrder is not answered by the authority yet");
	public void IssueCastOrderAt(UnitId caster, string abilityId, Vector3 position) => throw new NotSupportedException("IssueCastOrderAt is not answered by the authority yet");

	public void SetPlayerBotProfile(int playerIndex, string profileJson) => throw new NotSupportedException("SetPlayerBotProfile is not answered by the authority yet");
	public string GetPlayerBotProfile(int playerIndex) => throw new NotSupportedException("GetPlayerBotProfile is not answered by the authority yet");
	public void SetPlayerBotGenre(int playerIndex, string genreName, string? configJson = null) => throw new NotSupportedException("SetPlayerBotGenre is not answered by the authority yet");
	public string GetPlayerBotGenre(int playerIndex) => throw new NotSupportedException("GetPlayerBotGenre is not answered by the authority yet");
	public void ClearCustomBotDecisions(int playerIndex) => throw new NotSupportedException("ClearCustomBotDecisions is not answered by the authority yet");
	public string TrainBotProfile(string mapName, int generations = 5, int populationSize = 8, int matchesPerEvaluation = 4, string genre = "rts") => throw new NotSupportedException("TrainBotProfile is not answered by the authority yet");
}
