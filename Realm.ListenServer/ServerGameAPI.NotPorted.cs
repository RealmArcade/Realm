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
	public void SpawnVisualEffect(string effectTypeId, Vector3 position, float scale = 1.0f) => throw new NotSupportedException("SpawnVisualEffect is not answered by the authority yet");
	public void SpawnBarrageVolley(string effectTypeId, Vector3 targetAreaCenter, float radius, int count, float intervalSeconds) => throw new NotSupportedException("SpawnBarrageVolley is not answered by the authority yet");
	public void SpawnExpandingGroundRing(Vector3 position, float maxRadius = 8.0f, float speed = 12.0f, float duration = 1.0f, Vector3? color = null) => throw new NotSupportedException("SpawnExpandingGroundRing is not answered by the authority yet");
	public void DrawLineOnTerrain(Vector2 start, Vector2 end, Vector3? color = null, float duration = 1.0f, float width = 0.2f) => throw new NotSupportedException("DrawLineOnTerrain is not answered by the authority yet");
	public void SpawnExpandingBurstSphere(Vector3 position, float maxRadius = 6.0f, float speed = 15.0f, float duration = 0.8f, Vector3? color = null) => throw new NotSupportedException("SpawnExpandingBurstSphere is not answered by the authority yet");
	public void AttachPersistentAura(UnitId unit, string auraVfxId, float scale = 1.0f) => throw new NotSupportedException("AttachPersistentAura is not answered by the authority yet");




	public void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition) => throw new NotSupportedException("CastAbility is not answered by the authority yet");




	public void SpawnProjectile(string projectileTypeId, Vector3 start, Vector3 target, float speed) => throw new NotSupportedException("SpawnProjectile is not answered by the authority yet");
	public void SetLeaderboardVisible(string title, bool visible) => throw new NotSupportedException("SetLeaderboardVisible is not answered by the authority yet");
	public void SetLeaderboardValue(string label, string value) => throw new NotSupportedException("SetLeaderboardValue is not answered by the authority yet");
	public void ClearLeaderboard() => throw new NotSupportedException("ClearLeaderboard is not answered by the authority yet");
	public void SetLeaderboardBoard(string boardId, string title, bool visible) => throw new NotSupportedException("SetLeaderboardBoard is not answered by the authority yet");
	public void ClearLeaderboardBoard(string boardId) => throw new NotSupportedException("ClearLeaderboardBoard is not answered by the authority yet");
	public void SetLeaderboardBoardGroup(string boardId, int groupPlayerIndex, string groupTitle) => throw new NotSupportedException("SetLeaderboardBoardGroup is not answered by the authority yet");
	public void AddLeaderboardBoardRow(string boardId, int groupPlayerIndex, string groupTitle, string label, string value) => throw new NotSupportedException("AddLeaderboardBoardRow is not answered by the authority yet");
	public void ShowSummaryTable(string title, bool visible) => throw new NotSupportedException("ShowSummaryTable is not answered by the authority yet");
	public void SetSummaryTableHeaders(params string[] columnHeaders) => throw new NotSupportedException("SetSummaryTableHeaders is not answered by the authority yet");
	public void SetSummaryTableRow(string rowKey, params string[] cellValues) => throw new NotSupportedException("SetSummaryTableRow is not answered by the authority yet");
	public void ClearSummaryTable() => throw new NotSupportedException("ClearSummaryTable is not answered by the authority yet");
	public string GetPlayerLanguage(int playerIndex) => throw new NotSupportedException("GetPlayerLanguage is not answered by the authority yet");
	public string Translate(string key, int playerIndex) => throw new NotSupportedException("Translate is not answered by the authority yet");
	public void StartCountdownTimer(float duration, string label) => throw new NotSupportedException("StartCountdownTimer is not answered by the authority yet");
	public void StopCountdownTimer() => throw new NotSupportedException("StopCountdownTimer is not answered by the authority yet");
	public void ShakeCamera(float intensity, float duration) => throw new NotSupportedException("ShakeCamera is not answered by the authority yet");
	public void SetTimeOfDay(float time) => throw new NotSupportedException("SetTimeOfDay is not answered by the authority yet");
	public void SetDayNightCycleEnabled(bool enabled) => throw new NotSupportedException("SetDayNightCycleEnabled is not answered by the authority yet");
	public void KickPlayer(int playerIndex, string reason = "Kicked by host") => throw new NotSupportedException("KickPlayer is not answered by the authority yet");
	public bool AddComputerPlayer(int playerIndex, string? name = null, int teamIndex = -1) => throw new NotSupportedException("AddComputerPlayer is not answered by the authority yet");
	public void SetUnitColor(UnitId unit, Vector3 color) => throw new NotSupportedException("SetUnitColor is not answered by the authority yet");

	public void IssueCastOrder(UnitId caster, string abilityId, UnitId target) => throw new NotSupportedException("IssueCastOrder is not answered by the authority yet");
	public void IssueCastOrderAt(UnitId caster, string abilityId, Vector3 position) => throw new NotSupportedException("IssueCastOrderAt is not answered by the authority yet");

	public void SetPlayerComputerControlled(int playerIndex, bool isProxy) => throw new NotSupportedException("SetPlayerComputerControlled is not answered by the authority yet");
	public void AddLeaderboardRow(string label, string value, Vector3? color = null) => throw new NotSupportedException("AddLeaderboardRow is not answered by the authority yet");
	public void SetCountdownTimerLabel(string label) => throw new NotSupportedException("SetCountdownTimerLabel is not answered by the authority yet");

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
	public void SetUnitAnimation(UnitId unit, string animationName) => throw new NotSupportedException("SetUnitAnimation is not answered by the authority yet");
	public void SetUnitHandAttachment(UnitId unit, string hand, string? attachmentId) => throw new NotSupportedException("SetUnitHandAttachment is not answered by the authority yet");

	public void TriggerMeshImpulse(UnitId unit, float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f) => throw new NotSupportedException("TriggerMeshImpulse is not answered by the authority yet");
	public void TriggerResourceMeshImpulse(ResourceNodeId node, float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f) => throw new NotSupportedException("TriggerResourceMeshImpulse is not answered by the authority yet");
	public void SetPlayerBotProfile(int playerIndex, string profileJson) => throw new NotSupportedException("SetPlayerBotProfile is not answered by the authority yet");
	public string GetPlayerBotProfile(int playerIndex) => throw new NotSupportedException("GetPlayerBotProfile is not answered by the authority yet");
	public void SetPlayerBotGenre(int playerIndex, string genreName, string? configJson = null) => throw new NotSupportedException("SetPlayerBotGenre is not answered by the authority yet");
	public string GetPlayerBotGenre(int playerIndex) => throw new NotSupportedException("GetPlayerBotGenre is not answered by the authority yet");
	public void ClearCustomBotDecisions(int playerIndex) => throw new NotSupportedException("ClearCustomBotDecisions is not answered by the authority yet");
	public string TrainBotProfile(string mapName, int generations = 5, int populationSize = 8, int matchesPerEvaluation = 4, string genre = "rts") => throw new NotSupportedException("TrainBotProfile is not answered by the authority yet");
	public void SetEnvironmentPreset(string presetId) => throw new NotSupportedException("SetEnvironmentPreset is not answered by the authority yet");
	public void TransitionEnvironmentPreset(string presetId, float durationSeconds) => throw new NotSupportedException("TransitionEnvironmentPreset is not answered by the authority yet");
	public string GetCurrentEnvironmentPreset() => throw new NotSupportedException("GetCurrentEnvironmentPreset is not answered by the authority yet");
	public void SetWeather(string weatherType) => throw new NotSupportedException("SetWeather is not answered by the authority yet");
	public string GetWeather() => throw new NotSupportedException("GetWeather is not answered by the authority yet");
	public void DisableShroud() => throw new NotSupportedException("DisableShroud is not answered by the authority yet");
	public int CreateStaticText(string text, Vector3 position, Vector3 color, int fontSize = 48) => throw new NotSupportedException("CreateStaticText is not answered by the authority yet");
	public void SetStaticText(int handle, string text) => throw new NotSupportedException("SetStaticText is not answered by the authority yet");
	public void SetStaticTextColor(int handle, Vector3 color) => throw new NotSupportedException("SetStaticTextColor is not answered by the authority yet");
	public void SetStaticTextVisible(int handle, bool visible) => throw new NotSupportedException("SetStaticTextVisible is not answered by the authority yet");
	public void DestroyStaticText(int handle) => throw new NotSupportedException("DestroyStaticText is not answered by the authority yet");
	public void PanPlayerCameraTo(int playerIndex, Vector3 position, float duration = 0f) => throw new NotSupportedException("PanPlayerCameraTo is not answered by the authority yet");
	public void ShakePlayerCamera(int playerIndex, float intensity, float duration) => throw new NotSupportedException("ShakePlayerCamera is not answered by the authority yet");
	public void SetPlayerCameraBounds(int playerIndex, float minX, float minZ, float maxX, float maxZ) => throw new NotSupportedException("SetPlayerCameraBounds is not answered by the authority yet");
	public void ResetPlayerCameraBounds(int playerIndex) => throw new NotSupportedException("ResetPlayerCameraBounds is not answered by the authority yet");
	public void SetPlayerHudElementVisible(int playerIndex, HudElement element, bool visible) => throw new NotSupportedException("SetPlayerHudElementVisible is not answered by the authority yet");
	public void SelectPlayerUnit(int playerIndex, UnitId unit) => throw new NotSupportedException("SelectPlayerUnit is not answered by the authority yet");
	public void ClearPlayerSelection(int playerIndex) => throw new NotSupportedException("ClearPlayerSelection is not answered by the authority yet");
	public ReadOnlySpan<UnitId> GetPlayerSelectedUnits(int playerIndex) => throw new NotSupportedException("GetPlayerSelectedUnits is not answered by the authority yet");
	public void PlayPlayerWarningSound(int playerIndex) => throw new NotSupportedException("PlayPlayerWarningSound is not answered by the authority yet");
	public void PlayPlayerClickSound(int playerIndex) => throw new NotSupportedException("PlayPlayerClickSound is not answered by the authority yet");
	public void StartPlayerBuildingPlacement(int playerIndex, string unitTypeId) => throw new NotSupportedException("StartPlayerBuildingPlacement is not answered by the authority yet");
	public string GetPlayerColorHex(int playerIndex) => throw new NotSupportedException("GetPlayerColorHex is not answered by the authority yet");
	public Vector3 GetPlayerColor(int playerIndex) => throw new NotSupportedException("GetPlayerColor is not answered by the authority yet");
	public LootItemId SpawnLootItem(string itemTypeId, Vector3 position, int charges = 1, float lifetime = 0f) => throw new NotSupportedException("SpawnLootItem is not answered by the authority yet");
	public LootItemId GetLootItemById(int uniqueId) => throw new NotSupportedException("GetLootItemById is not answered by the authority yet");
	public ReadOnlySpan<LootItemId> GetAllLootItems() => throw new NotSupportedException("GetAllLootItems is not answered by the authority yet");
	public ReadOnlySpan<LootItemId> GetLootItemsInRadius(Vector3 center, float radius) => throw new NotSupportedException("GetLootItemsInRadius is not answered by the authority yet");
	public bool RemoveLootItem(int lootItemId) => throw new NotSupportedException("RemoveLootItem is not answered by the authority yet");
	public bool CollectLootItem(int lootItemId, UnitId unit) => throw new NotSupportedException("CollectLootItem is not answered by the authority yet");
	public bool CollectLootItemByPlayer(int lootItemId, int playerIndex) => throw new NotSupportedException("CollectLootItemByPlayer is not answered by the authority yet");









	public bool AddItemToUnit(UnitId unit, string itemId) => throw new NotSupportedException("AddItemToUnit is not answered by the authority yet");
	public bool AddItemToUnitWithCharges(UnitId unit, string itemId, int charges) => throw new NotSupportedException("AddItemToUnitWithCharges is not answered by the authority yet");
	public bool RemoveItemFromUnit(UnitId unit, string itemId) => throw new NotSupportedException("RemoveItemFromUnit is not answered by the authority yet");
	public bool UnitHasItem(UnitId unit, string itemId) => throw new NotSupportedException("UnitHasItem is not answered by the authority yet");
	public IReadOnlyList<string> GetUnitItems(UnitId unit) => throw new NotSupportedException("GetUnitItems is not answered by the authority yet");
	public int GetUnitItemCharges(UnitId unit, string itemId) => throw new NotSupportedException("GetUnitItemCharges is not answered by the authority yet");
	public void SetUnitItemCharges(UnitId unit, string itemId, int charges) => throw new NotSupportedException("SetUnitItemCharges is not answered by the authority yet");





	public string GetLootItemTypeId(LootItemId item) => throw new NotSupportedException("GetLootItemTypeId is not answered by the authority yet");
	public Vector3 GetLootItemPosition(LootItemId item) => throw new NotSupportedException("GetLootItemPosition is not answered by the authority yet");
	public int GetLootItemCharges(LootItemId item) => throw new NotSupportedException("GetLootItemCharges is not answered by the authority yet");
	public float GetLootItemLifetime(LootItemId item) => throw new NotSupportedException("GetLootItemLifetime is not answered by the authority yet");
	public float GetLootItemPickupRadius(LootItemId item) => throw new NotSupportedException("GetLootItemPickupRadius is not answered by the authority yet");
	public bool IsLootItemAutoPickup(LootItemId item) => throw new NotSupportedException("IsLootItemAutoPickup is not answered by the authority yet");
	public int GetLootItemOwner(LootItemId item) => throw new NotSupportedException("GetLootItemOwner is not answered by the authority yet");
	public bool IsLootItemDespawned(LootItemId item) => throw new NotSupportedException("IsLootItemDespawned is not answered by the authority yet");





}
