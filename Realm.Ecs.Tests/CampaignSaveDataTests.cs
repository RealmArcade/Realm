using NUnit.Framework;
using Realm.MapAPI;
using System;
using System.Collections.Generic;

namespace Realm.Ecs.Tests;

[TestFixture]
public class CampaignSaveDataTests
{
    private class MockGameApi : IGameAPI
    {
        public string CurrentMapName { get; set; } = "Campaign_Scenario1";
        public Dictionary<string, Dictionary<string, string>> SavedDataStorage { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int PlayerCount => 1;
        public string GetPlayerName(int playerIndex) => "Player1";

        public void WriteSavedData(string fileName, string content)
        {
            if (!SavedDataStorage.TryGetValue(CurrentMapName, out var mapStorage))
            {
                mapStorage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                SavedDataStorage[CurrentMapName] = mapStorage;
            }
            mapStorage[fileName] = content;
        }

        public string ReadSavedData(string fileName, string sourceMapName = "")
        {
            string targetMap = !string.IsNullOrWhiteSpace(sourceMapName) ? sourceMapName : CurrentMapName;
            if (SavedDataStorage.TryGetValue(targetMap, out var mapStorage) && mapStorage.TryGetValue(fileName, out var content))
            {
                return content;
            }
            return string.Empty;
        }

        public float Gold { get; set; }
        public float Wood { get; set; }
        public float Stone { get; set; }
        public int MaxPopulation { get; set; }
        public int CurrentPopulation => 0;
        public float GameElapsedTime => 0f;

        public event Action<IUnit>? OnUnitCreated { add { } remove { } }
        public event Action<IUnit, IUnit?>? OnUnitDied { add { } remove { } }
        public event Action<IUnit, IUnit, float>? OnUnitDamaged { add { } remove { } }
        public event Action<IUnit?, string, System.Numerics.Vector3>? OnSpellCast { add { } remove { } }
        public event Action<string, IUnit?>? OnPlayerChatMessage { add { } remove { } }
        public event Action<IUnit>? OnUnitSelected { add { } remove { } }
        public event Action<IUnit, IUnit>? OnUnitAttacked { add { } remove { } }
        public event Action<int>? OnTimerExpired { add { } remove { } }
        public event Action<IUnit, int>? OnUnitEnterZone { add { } remove { } }
        public event Action<int>? OnPlayerLeft { add { } remove { } }
        public event Action<IUnit, string>? OnItemSold { add { } remove { } }
        public event Action<IUnit>? OnConstructionFinished { add { } remove { } }
        public event Action<IUnit, string, System.Numerics.Vector3>? OnUnitOrdered { add { } remove { } }

        public IUnit SpawnUnit(string unitTypeId, System.Numerics.Vector3 position, bool isEnemy, bool bypassPopulation = false, bool executeSpawnShader = true) => throw new NotImplementedException();
        public void SpawnResourceNode(string resourceType, System.Numerics.Vector3 position, float amount) { }
        public void ShowFeedbackText(string text, System.Numerics.Vector3 color) { }
        public void AddBuff(IUnit unit, string buffId, float duration) { }
        public void AddLeaderboardRow(string label, string value, System.Numerics.Vector3? color = null) { }
        public void AddUnitTypeAbility(string unitTypeId, string abilityId) { }
        public void AdjustPlayerGold(int playerIndex, float delta) { }
        public void AdjustPlayerWood(int playerIndex, float delta) { }
        public void BroadcastMessage(string message) { }
        public void CancelTimer(int timerHandle) { }
        public void CastAbility(IUnit unit, string abilityId, System.Numerics.Vector3 targetPosition) { }
        public void ClearLeaderboard() { }
        public void ClearSelection() { }
        public void ClearSummaryTable() { }
        public int CountUnitsOwnedByPlayer(int playerIndex) => 0;
        public void CreateFloatingText(string text, System.Numerics.Vector3 position, System.Numerics.Vector3 color, float duration) { }
        public int DefineZone(float minX, float minZ, float maxX, float maxZ) => 0;
        public void DestroyUnit(IUnit unit, bool executeDespawnShader = false, bool playDeathAnimation = false) { }
        public void GenerateMapDirectory(string mapName, string? targetDirectory = null) { }
        public float GetAbilityCooldown(IUnit unit, string abilityId) => 0f;
        public IEnumerable<IUnit> GetAllUnits() => Array.Empty<IUnit>();
        public IUnit? GetCastle(bool isEnemy) => null;
        public System.Numerics.Vector3 GetCoordinateMax(string coordinateName) => System.Numerics.Vector3.Zero;
        public System.Numerics.Vector3 GetCoordinateMin(string coordinateName) => System.Numerics.Vector3.Zero;
        public IEnumerable<string> GetModifiers(IUnit unit) => Array.Empty<string>();
        public string GetPlayerBotProfile(int playerIndex) => string.Empty;
        public int GetPlayerCurrentPopulation(int playerIndex) => 0;
        public float GetPlayerGold(int playerIndex) => 0f;
        public int GetPlayerKills(int playerIndex) => 0;
        public string GetPlayerLanguage(int playerIndex) => "en";
        public int GetPlayerMaxPopulation(int playerIndex) => 0;
        public System.Numerics.Vector3 GetPlayerStartLocation(int playerIndex) => System.Numerics.Vector3.Zero;
        public int GetPlayerTeam(int playerIndex) => 0;
        public float GetPlayerWood(int playerIndex) => 0f;
        public IResourceNode GetResourceNode(int index) => throw new NotImplementedException();
        public int ResourceNodeCount => 0;
        public IEnumerable<IUnit> GetSelectedUnits() => Array.Empty<IUnit>();
        public IUnit? GetUnitById(int uniqueId) => null;
        public int GetUnitRouteState(IUnit unit) => 0;
        public IEnumerable<IUnit> GetUnitsInRadius(System.Numerics.Vector3 center, float radius) => Array.Empty<IUnit>();
        public IEnumerable<IUnit> GetUnitsInRadius(System.Numerics.Vector3 center, float radius, Func<IUnit, bool>? filter = null) => Array.Empty<IUnit>();
        public IEnumerable<IUnit> GetUnitsOwnedByPlayer(int playerIndex) => Array.Empty<IUnit>();
        public IEnumerable<IUnit> GetUnitsOwnedByPlayer(int playerIndex, Func<IUnit, bool>? filter = null) => Array.Empty<IUnit>();
        public System.Numerics.Vector3 GetZoneCenter(int zoneHandle) => System.Numerics.Vector3.Zero;
        public bool HasCoordinate(string coordinateName) => false;
        public bool IsPlayerActive(int playerIndex) => true;
        public bool IsPlayerComputer(int playerIndex) => false;
        public bool IsPositionInCoordinate(System.Numerics.Vector3 position, string coordinateName) => false;
        public void IssueAttackMoveOrder(IUnit unit, System.Numerics.Vector3 destination) { }
        public void IssueAttackMoveOrderToPlayer(int playerIndex, System.Numerics.Vector3 destination) { }
        public void IssueCastOrder(IUnit caster, string abilityId, IUnit target) { }
        public void IssueCastOrderAt(IUnit caster, string abilityId, System.Numerics.Vector3 position) { }
        public void IssueMoveOrder(IUnit unit, System.Numerics.Vector3 destination) { }
        public void KillUnit(IUnit unit, bool executeDespawnShader = false, bool playDeathAnimation = false) { }
        public void PanCameraTo(System.Numerics.Vector3 position, float duration) { }
        public void PingMinimap(System.Numerics.Vector3 position) { }
        public void PlayClickSound() { }
        public void PlayWarningSound() { }
        public float RandomFloat(float min, float max) => min;
        public int RandomInt(int min, int max) => min;
        public void RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant) { }
        public void RegisterBuffModifier(string buffId, string statName, bool isPercentage, float value) { }
        public void RemoveBuff(IUnit unit, string buffId) { }
        public int ScheduleRepeatingTimer(float interval) => 0;
        public int ScheduleTimer(float delay) => 0;
        public void SelectUnit(IUnit unit) { }
        public void SendMessageToPlayer(int playerIndex, string message) { }
        public void SetAbilityAutoCast(IUnit unit, string abilityId, bool active) { }
        public void SetAbilityCooldown(IUnit unit, string abilityId, float cooldown) { }
        public void SetAbilityGridPosition(string abilityId, int x, int y) { }
        public void SetAbilityIcon(string abilityId, string iconPath) { }
        public void SetAbilityInstant(string abilityId, bool isInstant) { }
        public void SetAbilityTooltip(string abilityId, string tooltip) { }
        public void SetCountdownTimerLabel(string label) { }
        public void SetDayNightCycleEnabled(bool enabled) { }
        public void SetLeaderboardRow(string label, string value, System.Numerics.Vector3? color = null) { }
        public void SetLeaderboardValue(string label, string value) { }
        public void SetLeaderboardVisible(string title, bool visible) { }
        public void SetPlayerBotProfile(int playerIndex, string profileJson) { }
        public void SetPlayerColor(int playerIndex, System.Numerics.Vector3 color) { }
        public void SetPlayerComputerControlled(int playerIndex, bool isProxy) { }
        public void SetPlayerGold(int playerIndex, float amount) { }
        public void SetPlayerKills(int playerIndex, int kills) { }
        public void SetPlayerMaxPopulation(int playerIndex, int max) { }
        public void SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied) { }
        public void SetPlayerTeam(int playerIndex, int teamIndex) { }
        public void SetPlayerWood(int playerIndex, float amount) { }
        public void SetSummaryTableRow(int rowIndex, string label, string value, System.Numerics.Vector3? color = null) { }
        public void SetSummaryTableRow(string rowKey, params string[] values) { }
        public void SetSummaryTableHeaders(params string[] headers) { }
        public void SetSummaryTableVisible(string title, bool visible) { }
        public void ShowSummaryTable(string title, bool visible) { }
        public void SpawnVisualEffect(string vfxId, System.Numerics.Vector3 position, float duration = 1.0f) { }
        public void TriggerMeshImpulse(IUnit unit, float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f) { }
        public void TriggerResourceMeshImpulse(IResourceNode resourceNode, float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f) { }
        public void SetTimeOfDay(float time) { }
        public void SetUnitAnimation(IUnit unit, string animationName) { }
        public void SetUnitColor(IUnit unit, System.Numerics.Vector3 color) { }
        public void SetUnitHandAttachment(IUnit unit, string hand, string? attachmentId) { }
        public void SetUnitLevel(IUnit unit, int level) { }
        public void SetUnitOwner(IUnit unit, int playerIndex) { }
        public void SetUnitRouteState(IUnit unit, int state) { }
        public void SetUnitSpellImmune(IUnit unit, bool immune) { }
        public void ShakeCamera(float intensity, float duration) { }
        public void SpawnProjectile(string projectileTypeId, System.Numerics.Vector3 start, System.Numerics.Vector3 target, float speed) { }
        public void SpawnTargetIndicator(System.Numerics.Vector3 position, System.Numerics.Vector3 color) { }
        public IUnit SpawnUnitForPlayer(string unitTypeId, System.Numerics.Vector3 position, int playerIndex, bool executeSpawnShader = false) => throw new NotImplementedException();
        public void StartBuildingPlacement(string unitTypeId) { }
        public void StartCountdownTimer(float duration, string label) { }
        public void StopCountdownTimer() { }
        public string TrainBotProfile(string mapName, int generations = 5, int populationSize = 8, int matchesPerEvaluation = 4, string genre = "rts") => string.Empty;
        public string Translate(string key, int playerIndex = -1) => key;
        public void TriggerDefeat() { }
        public void TriggerPlayerDefeat(int playerIndex, string reason) { }
        public void TriggerPlayerVictory(int playerIndex) { }
        public void TriggerVictory() { }
        public void SetEnvironmentPreset(string presetId) { }
        public void TransitionEnvironmentPreset(string presetId, float durationSeconds) { }
        public string GetCurrentEnvironmentPreset() => "day";
        public void SetWeather(string weatherType) { }
        public string GetWeather() => "clear";
        public int CreateStaticText(string text, System.Numerics.Vector3 position, System.Numerics.Vector3 color, int fontSize = 48) => 0;
        public void SetStaticText(int handle, string text) { }
        public void SetStaticTextVisible(int handle, bool visible) { }
        public void DestroyStaticText(int handle) { }
    }

    private class TestUnit : IUnit
    {
        public int UniqueId { get; set; } = 1;
        public string UnitId { get; set; } = "hero_paladin";
        public string Name { get; set; } = "Sir Arthur";
        public bool IsEnemy { get; set; } = false;
        public int Player { get; set; } = 0;
        public bool IsBuilding => false;
        public bool IsHero => true;
        public int Level { get; set; } = 5;
        public float Experience { get; set; } = 2500f;
        public float XpBounty => 50f;
        public float GoldBounty => 25f;
        public float Health { get; set; } = 800f;
        public float MaxHealth { get; set; } = 1000f;
        public float Damage { get; set; } = 75f;
        public float Range { get; set; } = 1.5f;
        public float Armor { get; set; } = 12f;
        public float AttackSpeed { get; set; } = 1f;
        public float ManaRegen { get; set; } = 0f;
        public float Speed { get; set; } = 4.5f;
        public float Mana { get; set; } = 300f;
        public float MaxMana { get; set; } = 400f;
        public float Scale { get; set; } = 1f;
        public bool Invulnerable { get; set; } = false;
        public bool IsDead => false;
        public System.Numerics.Vector3 Position { get; set; } = System.Numerics.Vector3.Zero;

        private readonly Dictionary<string, string> _customData = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _items = new();
        private readonly HashSet<string> _buffs = new(StringComparer.OrdinalIgnoreCase);

        public void SetCustomData(string key, string value) => _customData[key] = value;
        public string? GetCustomData(string key) => _customData.TryGetValue(key, out var val) ? val : null;
        public bool RemoveCustomData(string key) => _customData.Remove(key);
        public bool HasCustomData(string key) => _customData.ContainsKey(key);
        public IEnumerable<string> GetItems() => _items;
        public bool AddItem(string itemId)
        {
            _items.Add(itemId);
            return true;
        }
        public bool RemoveItem(string itemId) => _items.Remove(itemId);
        public bool HasItem(string itemId) => _items.Contains(itemId);
        public void AddBuff(string buffId, float duration) => _buffs.Add(buffId);
        public void RemoveBuff(string buffId) => _buffs.Remove(buffId);
        public bool HasBuff(string buffId) => _buffs.Contains(buffId);
        public IEnumerable<string> GetModifiers() => Array.Empty<string>();
        public void MoveTo(System.Numerics.Vector3 destination) { }
        public void AttackMove(System.Numerics.Vector3 destination) { }
        public void Attack(IUnit target) { }
        public void Gather(IResourceNode resourceNode) { }
        public void Teleport(System.Numerics.Vector3 position) { }
        public void Stop() { }
        public void HoldPosition() { }
        public void Stun(float duration) { }
        public void Silence(float duration) { }
        public void TriggerMeshImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f) { }
    }

    private class CampaignSavePayloadV1
    {
        public string CampaignTag { get; set; } = string.Empty;
        public HeroSaveData? MainHero { get; set; }
        public List<InventoryItemSaveData> Inventory { get; set; } = new();
    }

    private class CampaignSavePayloadV2
    {
        public string CampaignTag { get; set; } = string.Empty;
        public HeroSaveData? MainHero { get; set; }
        public List<InventoryItemSaveData> Inventory { get; set; } = new();
        public int CompletedScenarioCount { get; set; }
    }

    [Test]
    public void TestCrossMapReadingAndStrictWriteIsolation()
    {
        var api = new MockGameApi();
        api.CurrentMapName = "Campaign_Scenario1";

        var unit = new TestUnit();
        unit.AddItem("item_healing_potion");
        unit.AddItem("item_iron_sword");

        var payload1 = new CampaignSavePayloadV1
        {
            CampaignTag = "KingdomWar",
            MainHero = unit.SnapshotHero(experience: 1200f, skillPoints: 3),
            Inventory = unit.SnapshotInventory()
        };

        api.SaveData("campaign_state.rsav", payload1, dataVersion: 1, mapName: "Campaign_Scenario1");

        Assert.That(api.SavedDataStorage.ContainsKey("Campaign_Scenario1"), Is.True);
        Assert.That(api.SavedDataStorage["Campaign_Scenario1"].ContainsKey("campaign_state.rsav"), Is.True);

        api.CurrentMapName = "Campaign_Scenario2";

        bool loadDirectWithoutSourceFails = api.TryLoadData<CampaignSavePayloadV1>("campaign_state.rsav", out var loadedDirect);
        Assert.That(loadDirectWithoutSourceFails, Is.False);
        Assert.That(loadedDirect, Is.Null);

        bool loadFromScenario1Succeeds = api.TryLoadData<CampaignSavePayloadV1>(
            "campaign_state.rsav",
            out var loadedFromScenario1,
            targetVersion: 1,
            mapName: "Campaign_Scenario1");

        Assert.That(loadFromScenario1Succeeds, Is.True);
        Assert.That(loadedFromScenario1, Is.Not.Null);
        Assert.That(loadedFromScenario1!.CampaignTag, Is.EqualTo("KingdomWar"));
        Assert.That(loadedFromScenario1.MainHero, Is.Not.Null);
        Assert.That(loadedFromScenario1.MainHero!.Name, Is.EqualTo("Sir Arthur"));
        Assert.That(loadedFromScenario1.Inventory.Count, Is.EqualTo(2));

        var restoredUnit = new TestUnit
        {
            Name = "Default",
            Level = 1,
            Health = 100f
        };
        restoredUnit.RestoreHero(loadedFromScenario1.MainHero);
        restoredUnit.RestoreInventory(loadedFromScenario1.Inventory);

        Assert.That(restoredUnit.Name, Is.EqualTo("Sir Arthur"));
        Assert.That(restoredUnit.Level, Is.EqualTo(5));
        Assert.That(restoredUnit.Health, Is.EqualTo(800f));
        Assert.That(restoredUnit.HasItem("item_iron_sword"), Is.True);
        Assert.That(restoredUnit.HasItem("item_healing_potion"), Is.True);

        api.SaveData("campaign_state.rsav", loadedFromScenario1, dataVersion: 1, mapName: "Campaign_Scenario2");

        Assert.That(api.SavedDataStorage.ContainsKey("Campaign_Scenario2"), Is.True);
        Assert.That(api.SavedDataStorage["Campaign_Scenario2"].ContainsKey("campaign_state.rsav"), Is.True);
    }

    [Test]
    public void TestCrossMapSchemaMigration()
    {
        var api = new MockGameApi();
        api.CurrentMapName = "Campaign_Scenario1";

        var payloadV1 = new CampaignSavePayloadV1
        {
            CampaignTag = "DragonQuest",
            MainHero = new HeroSaveData { Name = "Elven Mage", Level = 10, Health = 500f },
            Inventory = new List<InventoryItemSaveData> { new() { ItemId = "item_mana_crystal", SlotIndex = 0 } }
        };

        api.SaveData("hero_progression.rsav", payloadV1, dataVersion: 1, mapName: "Campaign_Scenario1");

        api.CurrentMapName = "Campaign_Scenario2";

        var registry = new SaveMigrationRegistry();
        registry.RegisterMigration(1, 2, json =>
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var newObj = new Dictionary<string, object?>
            {
                ["CampaignTag"] = root.GetProperty("CampaignTag").GetString(),
                ["MainHero"] = System.Text.Json.JsonSerializer.Deserialize<HeroSaveData>(root.GetProperty("MainHero").GetRawText()),
                ["Inventory"] = System.Text.Json.JsonSerializer.Deserialize<List<InventoryItemSaveData>>(root.GetProperty("Inventory").GetRawText()),
                ["CompletedScenarioCount"] = 1
            };
            return System.Text.Json.JsonSerializer.Serialize(newObj);
        });

        bool loadMigrated = api.TryLoadData<CampaignSavePayloadV2>(
            "hero_progression.rsav",
            out var upgradedPayload,
            targetVersion: 2,
            migrations: registry,
            mapName: "Campaign_Scenario1");

        Assert.That(loadMigrated, Is.True);
        Assert.That(upgradedPayload, Is.Not.Null);
        Assert.That(upgradedPayload!.CompletedScenarioCount, Is.EqualTo(1));
        Assert.That(upgradedPayload.MainHero!.Name, Is.EqualTo("Elven Mage"));

        api.SaveData("hero_progression.rsav", upgradedPayload, dataVersion: 2, mapName: "Campaign_Scenario2");

        bool loadScenario2 = api.TryLoadData<CampaignSavePayloadV2>(
            "hero_progression.rsav",
            out var scenario2Payload,
            targetVersion: 2);

        Assert.That(loadScenario2, Is.True);
        Assert.That(scenario2Payload, Is.Not.Null);
        Assert.That(scenario2Payload!.CompletedScenarioCount, Is.EqualTo(1));
    }

    [Test]
    public void TestCrossMapLoadDataDirect()
    {
        var api = new MockGameApi();
        api.CurrentMapName = "Campaign_Scenario1";

        var payload = new CampaignSavePayloadV1
        {
            CampaignTag = "HeroQuest",
            MainHero = new HeroSaveData { Name = "Ranger", Level = 3 }
        };

        api.SaveData("ranger.rsav", payload, mapName: "Campaign_Scenario1");

        api.CurrentMapName = "Campaign_Scenario2";

        var loaded = api.LoadData<CampaignSavePayloadV1>("ranger.rsav", mapName: "Campaign_Scenario1");
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.CampaignTag, Is.EqualTo("HeroQuest"));
        Assert.That(loaded.MainHero!.Name, Is.EqualTo("Ranger"));
    }

    [Test]
    public void TestCrossMapArcadeProfileLoading()
    {
        var api = new MockGameApi();
        api.CurrentMapName = "Campaign_Scenario1";

        var profile = new ArcadeProfileData
        {
            PlayerName = "Player1",
            MatchesPlayed = 1,
            TotalPlaytimeSeconds = 120f,
            Heroes = new List<HeroSaveData>
            {
                new() { HeroTypeId = "hero_paladin", Name = "Sir Arthur", Level = 5 }
            },
            ActiveInventory = new List<InventoryItemSaveData>
            {
                new() { ItemId = "item_sword", SlotIndex = 0 }
            }
        };

        api.SaveArcadeProfile("profile.rsav", profile, dataVersion: 1, mapName: "Campaign_Scenario1");

        api.CurrentMapName = "Campaign_Scenario2";

        bool tryLoadSucceeded = api.TryLoadArcadeProfile(
            "profile.rsav",
            out var loadedProfile,
            targetVersion: 1,
            mapName: "Campaign_Scenario1");

        Assert.That(tryLoadSucceeded, Is.True);
        Assert.That(loadedProfile, Is.Not.Null);
        Assert.That(loadedProfile!.Heroes.Count, Is.EqualTo(1));
        Assert.That(loadedProfile.Heroes[0].Name, Is.EqualTo("Sir Arthur"));
        Assert.That(loadedProfile.ActiveInventory.Count, Is.EqualTo(1));

        var directLoaded = api.LoadArcadeProfile("profile.rsav", mapName: "Campaign_Scenario1");
        Assert.That(directLoaded, Is.Not.Null);
        Assert.That(directLoaded!.Heroes.Count, Is.EqualTo(1));
    }

    [Test]
    public void TestTamperDetectionFailsCrossMapLoad()
    {
        var api = new MockGameApi();
        api.CurrentMapName = "Campaign_Scenario1";

        var payload = new CampaignSavePayloadV1
        {
            CampaignTag = "LegitimateSave",
            MainHero = new HeroSaveData { Name = "Knight", Level = 2 }
        };

        api.SaveData("tamper_test.rsav", payload, mapName: "Campaign_Scenario1");

        string rawJson = api.SavedDataStorage["Campaign_Scenario1"]["tamper_test.rsav"];
        string tamperedJson = rawJson.Replace("Knight", "SuperHackedKnight");
        api.SavedDataStorage["Campaign_Scenario1"]["tamper_test.rsav"] = tamperedJson;

        api.CurrentMapName = "Campaign_Scenario2";

        bool loadTampered = api.TryLoadData<CampaignSavePayloadV1>(
            "tamper_test.rsav",
            out var tamperedPayload,
            mapName: "Campaign_Scenario1");

        Assert.That(loadTampered, Is.False);
        Assert.That(tamperedPayload, Is.Null);
    }
}
