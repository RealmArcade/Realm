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

	public void GenerateMapDirectory(string mapName, string? targetDirectory = null) => throw new NotSupportedException("GenerateMapDirectory is not answered by the authority yet");
	public void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition) => throw new NotSupportedException("CastAbility is not answered by the authority yet");

	public string GetPlayerLanguage(int playerIndex) => throw new NotSupportedException("GetPlayerLanguage is not answered by the authority yet");
	public string Translate(string key, int playerIndex) => throw new NotSupportedException("Translate is not answered by the authority yet");
	public void KickPlayer(int playerIndex, string reason = "Kicked by host") => throw new NotSupportedException("KickPlayer is not answered by the authority yet");
	public bool AddComputerPlayer(int playerIndex, string? name = null, int teamIndex = -1) => throw new NotSupportedException("AddComputerPlayer is not answered by the authority yet");
	public void IssueCastOrder(UnitId caster, string abilityId, UnitId target) => throw new NotSupportedException("IssueCastOrder is not answered by the authority yet");
	public void IssueCastOrderAt(UnitId caster, string abilityId, Vector3 position) => throw new NotSupportedException("IssueCastOrderAt is not answered by the authority yet");

	public void SetPlayerComputerControlled(int playerIndex, bool isProxy) => throw new NotSupportedException("SetPlayerComputerControlled is not answered by the authority yet");

	public int DefineZone(float minX, float minZ, float maxX, float maxZ) => throw new NotSupportedException("DefineZone is not answered by the authority yet");
	public Vector3 GetZoneCenter(int zoneHandle) => throw new NotSupportedException("GetZoneCenter is not answered by the authority yet");

	public bool HasCoordinate(string coordinateName) => throw new NotSupportedException("HasCoordinate is not answered by the authority yet");
	public Vector3 GetCoordinateMin(string coordinateName) => throw new NotSupportedException("GetCoordinateMin is not answered by the authority yet");
	public Vector3 GetCoordinateMax(string coordinateName) => throw new NotSupportedException("GetCoordinateMax is not answered by the authority yet");
	public bool IsPositionInCoordinate(Vector3 position, string coordinateName) => throw new NotSupportedException("IsPositionInCoordinate is not answered by the authority yet");
	public void SetRegionPathing(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags, bool blocked) => throw new NotSupportedException("SetRegionPathing is not answered by the authority yet");
	public PathingFlags GetRegionPathing(float minX, float minZ, float maxX, float maxZ) => throw new NotSupportedException("GetRegionPathing is not answered by the authority yet");
	public void AddUnitTypeAbility(string unitTypeId, string abilityId) => throw new NotSupportedException("AddUnitTypeAbility is not answered by the authority yet");
	public IReadOnlyList<string> GetUnitTypeAbilities(string unitTypeId) => throw new NotSupportedException("GetUnitTypeAbilities is not answered by the authority yet");
	public string GetUnitName(string unitTypeId) => throw new NotSupportedException("GetUnitName is not answered by the authority yet");
	public string GetAbilityName(string abilityId) => throw new NotSupportedException("GetAbilityName is not answered by the authority yet");
	public string GetStringText(int stringId) => throw new NotSupportedException("GetStringText is not answered by the authority yet");
	public void WriteSavedData(string fileName, string content) => throw new NotSupportedException("WriteSavedData is not answered by the authority yet");
	public string ReadSavedData(string fileName, string sourceMapName = "") => throw new NotSupportedException("ReadSavedData is not answered by the authority yet");
	public int GetPlayerTechLevel(int playerIndex, string techId) => throw new NotSupportedException("GetPlayerTechLevel is not answered by the authority yet");
	public void SetPlayerTechLevel(int playerIndex, string techId, int level) => throw new NotSupportedException("SetPlayerTechLevel is not answered by the authority yet");
	public void AddPlayerTechLevel(int playerIndex, string techId, int delta = 1) => throw new NotSupportedException("AddPlayerTechLevel is not answered by the authority yet");
	public void RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath = "", bool isInstant = false) => throw new NotSupportedException("RegisterAbility is not answered by the authority yet");
	public void RegisterPlayerAbility(int playerIndex, string abilityId, string displayName, string tooltip, string iconPath = "", bool isInstant = false) => throw new NotSupportedException("RegisterPlayerAbility is not answered by the authority yet");
	public void SetAbilityInstant(string abilityId, bool isInstant) => throw new NotSupportedException("SetAbilityInstant is not answered by the authority yet");
	public void SetAbilityIcon(string abilityId, string iconPath) => throw new NotSupportedException("SetAbilityIcon is not answered by the authority yet");
	public void SetAbilityTooltip(string abilityId, string tooltip) => throw new NotSupportedException("SetAbilityTooltip is not answered by the authority yet");
	public void SetItemTooltip(string itemId, string tooltip) => throw new NotSupportedException("SetItemTooltip is not answered by the authority yet");
	public void SetAbilityTooltipValue(string abilityId, string key, float value) => throw new NotSupportedException("SetAbilityTooltipValue is not answered by the authority yet");
	public void SetAbilityTooltipText(string abilityId, string key, string value) => throw new NotSupportedException("SetAbilityTooltipText is not answered by the authority yet");
	public void SetUnitTooltipValue(UnitId unit, string key, float value) => throw new NotSupportedException("SetUnitTooltipValue is not answered by the authority yet");
	public void SetUnitTooltipText(UnitId unit, string key, string value) => throw new NotSupportedException("SetUnitTooltipText is not answered by the authority yet");
	public void SetItemTooltipValue(string itemId, string key, float value) => throw new NotSupportedException("SetItemTooltipValue is not answered by the authority yet");
	public void SetUpgradeTooltipValue(string upgradeId, string key, float value) => throw new NotSupportedException("SetUpgradeTooltipValue is not answered by the authority yet");
	public void SetAbilityGridPosition(string abilityId, int x, int y) => throw new NotSupportedException("SetAbilityGridPosition is not answered by the authority yet");
	public void SetAbilityHotkey(string abilityId, string hotkey) => throw new NotSupportedException("SetAbilityHotkey is not answered by the authority yet");
	public void SetAbilityState(UnitId unit, string abilityId, bool disabled, bool hidden) => throw new NotSupportedException("SetAbilityState is not answered by the authority yet");
	public void SetAbilityManaCost(UnitId unit, string abilityId, float manaCost) => throw new NotSupportedException("SetAbilityManaCost is not answered by the authority yet");
	public void SetPlayerBotProfile(int playerIndex, string profileJson) => throw new NotSupportedException("SetPlayerBotProfile is not answered by the authority yet");
	public string GetPlayerBotProfile(int playerIndex) => throw new NotSupportedException("GetPlayerBotProfile is not answered by the authority yet");
	public void SetPlayerBotGenre(int playerIndex, string genreName, string? configJson = null) => throw new NotSupportedException("SetPlayerBotGenre is not answered by the authority yet");
	public string GetPlayerBotGenre(int playerIndex) => throw new NotSupportedException("GetPlayerBotGenre is not answered by the authority yet");
	public void ClearCustomBotDecisions(int playerIndex) => throw new NotSupportedException("ClearCustomBotDecisions is not answered by the authority yet");
	public string TrainBotProfile(string mapName, int generations = 5, int populationSize = 8, int matchesPerEvaluation = 4, string genre = "rts") => throw new NotSupportedException("TrainBotProfile is not answered by the authority yet");
	public string GetPlayerColorHex(int playerIndex) => throw new NotSupportedException("GetPlayerColorHex is not answered by the authority yet");
	public Vector3 GetPlayerColor(int playerIndex) => throw new NotSupportedException("GetPlayerColor is not answered by the authority yet");
}
