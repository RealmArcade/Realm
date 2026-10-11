using Realm.Ecs.Services;
using Arch.Core;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.MapAPI;
using System;
using System.Collections.Generic;

namespace Realm.Client.Core;

public partial class GameHost
{
	private System.Collections.Generic.Dictionary<string, AbilityDefinition> _abilityDefinitions => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.RegistryService>().AbilityDefinitions;
	private Dictionary<(int UnitUniqueId, string AbilityId), (bool Disabled, bool Hidden)> _unitAbilityStates => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.RegistryService>().UnitAbilityStates;
	private Dictionary<(int UnitUniqueId, string AbilityId), float> _unitAbilityManaCosts => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.RegistryService>().UnitAbilityManaCosts;
	private Dictionary<string, string> _itemTooltips => Realm.Client.Services.ServiceLocator.Get<Realm.Client.Services.RegistryService>().ItemTooltips;
	private Dictionary<(int PlayerIndex, string TechId), int> _playerTechLevels => Realm.Client.Services.ServiceLocator.Get<Realm.Ecs.Services.WorldAccessor>().PlayerTechLevels;

	public void ResetAbilityCatalog()
	{
		_abilityDefinitions.Clear();
	}

	public void RegisterCustomAbilities(List<AbilityMetadata> customAbilities)
	{
		if (customAbilities == null) return;
		foreach (var meta in customAbilities)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;
			var def = new AbilityDefinition
			{
				Id = meta.TemplateID,
				DisplayName = meta.Name ?? "",
				Tooltip = meta.Description ?? "",
				IconPath = meta.IconPath ?? "",
				IsInstant = string.Equals(meta.AbilityType, "instant_spell", StringComparison.OrdinalIgnoreCase),
				ManaCost = meta.ManaCost,
				Cooldown = meta.Cooldown,
				TargetRange = meta.TargetRange,
				AreaOfEffectRadius = meta.AreaOfEffectRadius,
				Damage = meta.Damage,
				Healing = meta.Healing,
				VisualEffect = meta.VisualEffect,
				CastSound = meta.CastSound
			};
			_abilityDefinitions[meta.TemplateID] = def;
			if (meta.TemplateID.StartsWith("ability/", StringComparison.OrdinalIgnoreCase))
			{
				_abilityDefinitions[meta.TemplateID.Substring(8)] = def;
			}
		}
	}

	public AbilityDefinition GetAbilityDefinition(string abilityId)
	{
		if (string.IsNullOrEmpty(abilityId)) return null;
		_abilityDefinitions.TryGetValue(abilityId, out var def);
		return def;
	}

	void IGameAPI.RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.DisplayName = displayName ?? "";
		def.Tooltip = tooltip ?? "";
		if (!string.IsNullOrEmpty(iconPath)) def.IconPath = iconPath;
		def.IsInstant = isInstant;

		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientRegisterAbility), abilityId, def.DisplayName, def.Tooltip, def.IconPath, isInstant);
		}
	}

	void IGameAPI.SetAbilityInstant(string abilityId, bool isInstant)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.IsInstant = isInstant;
	}

	void IGameAPI.SetAbilityIcon(string abilityId, string iconPath)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.IconPath = iconPath ?? "";
	}

	void IGameAPI.SetAbilityTooltip(string abilityId, string tooltip)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.Tooltip = tooltip ?? "";
	}

	void IGameAPI.SetAbilityGridPosition(string abilityId, int x, int y)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.GridX = x;
		def.GridY = y;
	}

	void IGameAPI.SetAbilityManaCost(IUnit unit, string abilityId, float manaCost)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.ManaCost = manaCost;
	}

	void IGameAPI.ShowSummaryTable(string title, bool visible)
	{
		Callable.From(() => Realm.Client.UI.InGameHUD.Instance?.ShowSummaryTable(title, visible)).CallDeferred();
	}

	void IGameAPI.SetSummaryTableHeaders(params string[] columnHeaders)
	{
		Callable.From(() => Realm.Client.UI.InGameHUD.Instance?.SetSummaryTableHeaders(columnHeaders)).CallDeferred();
	}

	void IGameAPI.SetSummaryTableRow(string rowKey, params string[] cellValues)
	{
		Callable.From(() => Realm.Client.UI.InGameHUD.Instance?.SetSummaryTableRow(rowKey, cellValues)).CallDeferred();
	}

	void IGameAPI.ClearSummaryTable()
	{
		Callable.From(() => Realm.Client.UI.InGameHUD.Instance?.ClearSummaryTable()).CallDeferred();
	}

	string IGameAPI.GetPlayerLanguage(int playerIndex)
	{
		return LocalizationManager.GetCurrentLanguageCode();
	}

	string IGameAPI.Translate(string key, int playerIndex)
	{
		return LocalizationManager.TranslateKey(key);
	}

	void IGameAPI.TriggerMeshImpulse(IUnit unit, float strength, float duration, float frequency)
	{
		if (unit == null) return;
		unit.TriggerMeshImpulse(strength, duration, frequency);
	}

	void IGameAPI.TriggerResourceMeshImpulse(IResourceNode node, float strength, float duration, float frequency)
	{
		if (node == null) return;
		node.TriggerMeshImpulse(strength, duration, frequency);
	}

	void IGameAPI.SetAbilityHotkey(string abilityId, string hotkey)
	{
		if (string.IsNullOrEmpty(abilityId)) return;

		if (!_abilityDefinitions.TryGetValue(abilityId, out var def))
		{
			def = new AbilityDefinition { Id = abilityId };
			_abilityDefinitions[abilityId] = def;
		}

		def.Hotkey = hotkey ?? "";
	}

	void IGameAPI.SetAbilityState(IUnit unit, string abilityId, bool disabled, bool hidden)
	{
		if (unit == null || string.IsNullOrEmpty(abilityId)) return;
		_unitAbilityStates[(unit.UniqueId, abilityId)] = (disabled, hidden);
	}

	public bool IsAbilityDisabledForUnit(IUnit unit, string abilityId)
	{
		if (unit != null && _unitAbilityStates.TryGetValue((unit.UniqueId, abilityId), out var state))
		{
			return state.Disabled;
		}
		return false;
	}

	public bool IsAbilityHiddenForUnit(IUnit unit, string abilityId)
	{
		if (unit != null && _unitAbilityStates.TryGetValue((unit.UniqueId, abilityId), out var state))
		{
			return state.Hidden;
		}
		return false;
	}

	public float GetAbilityManaCostForUnit(IUnit unit, string abilityId)
	{
		if (unit != null && _unitAbilityManaCosts.TryGetValue((unit.UniqueId, abilityId), out float cost))
		{
			return cost;
		}
		var def = GetAbilityDefinition(abilityId);
		return def?.ManaCost ?? 0f;
	}

	void IGameAPI.SetItemTooltip(string itemId, string tooltip)
	{
		if (string.IsNullOrEmpty(itemId)) return;
		_itemTooltips[itemId] = tooltip ?? "";
		if (ItemRegistry.TryGetValue(itemId, out var itemMeta))
		{
			itemMeta.Description = tooltip ?? "";
		}
	}

	int IGameAPI.GetPlayerTechLevel(int playerIndex, string techId)
	{
		if (string.IsNullOrEmpty(techId)) return 0;
		return _playerTechLevels.TryGetValue((playerIndex, techId), out int level) ? level : 0;
	}

	void IGameAPI.SetPlayerTechLevel(int playerIndex, string techId, int level)
	{
		if (string.IsNullOrEmpty(techId)) return;
		_playerTechLevels[(playerIndex, techId)] = level;
	}

	void IGameAPI.AddPlayerTechLevel(int playerIndex, string techId, int delta)
	{
		if (string.IsNullOrEmpty(techId)) return;
		int current = ((IGameAPI)this).GetPlayerTechLevel(playerIndex, techId);
		_playerTechLevels[(playerIndex, techId)] = current + delta;
	}
	float IGameAPI.Gold
	{
		get
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity) &&
				EcsWorld.TryGet<PlayerResources>(_playerEntity, out var res) &&
				res.Value.TryGetValue(ServiceLocator.Get<PlayerResourceService>().GoldResourceId, out var val))
				return val;
			return _goldBackup;
		}
		set
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity))
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.ContainsKey(ServiceLocator.Get<PlayerResourceService>().GoldResourceId))
						r.Value[ServiceLocator.Get<PlayerResourceService>().GoldResourceId] = (int)value;
				});
			else
				_goldBackup = value;
			Realm.Client.UI.InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	float IGameAPI.Wood
	{
		get
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity) &&
				EcsWorld.TryGet<PlayerResources>(_playerEntity, out var res) &&
				res.Value.TryGetValue(ServiceLocator.Get<PlayerResourceService>().WoodResourceId, out var val))
				return val;
			return _woodBackup;
		}
		set
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity))
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.ContainsKey(ServiceLocator.Get<PlayerResourceService>().WoodResourceId))
						r.Value[ServiceLocator.Get<PlayerResourceService>().WoodResourceId] = (int)value;
				});
			else
				_woodBackup = value;
			Realm.Client.UI.InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	float IGameAPI.Stone
	{
		get
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity) &&
				EcsWorld.TryGet<PlayerResources>(_playerEntity, out var res) &&
				res.Value.TryGetValue(ServiceLocator.Get<PlayerResourceService>().StoneResourceId, out var val))
				return val;
			return _stoneBackup;
		}
		set
		{
			if (EcsWorld != null && _playerEntity != Entity.Null && EcsWorld.IsAlive(_playerEntity))
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.ContainsKey(ServiceLocator.Get<PlayerResourceService>().StoneResourceId))
						r.Value[ServiceLocator.Get<PlayerResourceService>().StoneResourceId] = (int)value;
				});
			else
				_stoneBackup = value;
			Realm.Client.UI.InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}
	}

	float IGameAPI.GameElapsedTime => GameElapsedTime;

	IUnit IGameAPI.SpawnUnit(string unitTypeId, System.Numerics.Vector3 position, bool isEnemy, bool bypassPopulation, bool executeSpawnShader)
	{
		var pos = new Vector3(position.X, GetTerrainHeightAt(new Vector3(position.X, position.Y, position.Z)), position.Z);

		bool isBuilding = false;
		if (!UnitRegistry.TryGetValue(unitTypeId, out var meta) && !(isBuilding = BuildingRegistry.TryGetValue(unitTypeId, out meta)))
			throw new ArgumentException($"Unit ID '{unitTypeId}' not found in registry.");

		int ownerPeerId = GetOwnerPeerId(isEnemy);
		bool actualIsEnemy = NetworkService.ArePeersEnemies(_localPeerId, ownerPeerId);
		var playerOwner = GetPlayerOwner(ownerPeerId, actualIsEnemy).AsPlayerEntity(EcsWorld);

		if (string.IsNullOrEmpty(meta.ModelPath)) throw new ArgumentException($"Unit ID '{unitTypeId}' has no assigned 3D model asset in registry.");
		string modelPath = _unitSpawnService.GetFallbackModelPath(meta.ModelPath, isBuilding);
		string name = actualIsEnemy ? _unitSpawnService.GetEnemyUnitName(unitTypeId, meta.Name) : meta.Name;

		var entity = CreateEcsUnit(unitTypeId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, pos, playerOwner);
		if (bypassPopulation) EcsWorld.Add(entity, new BypassPopulationTag());

		SpawnUnit3D(entity, unitTypeId, modelPath, pos, isBuilding, actualIsEnemy, bypassPopulation, -1, executeSpawnShader);
		return GetUnitWrapper(entity);
	}

	void IGameAPI.SpawnResourceNode(string resourceType, System.Numerics.Vector3 position, float amount)
	{
		var pos = new Vector3(position.X, position.Y, position.Z);
		var prop = SpawnPropExternal(resourceType, pos);
		if (prop != null)
		{
			prop.ResourceAmount = amount;
		}
	}

	IEnumerable<IUnit> IGameAPI.GetAllUnits()
	{
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && EcsWorld.IsAlive(u.Entity))
			{
				list.Add(GetUnitWrapper(u.Entity));
			}
		}
		return list;
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsInRadius(System.Numerics.Vector3 center, float radius)
	{
		var list = new List<IUnit>();
		var godotCenter = new Vector3(center.X, center.Y, center.Z);
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && EcsWorld.IsAlive(u.Entity) && u.GlobalPosition.DistanceTo(godotCenter) <= radius)
			{
				list.Add(GetUnitWrapper(u.Entity));
			}
		}
		return list;
	}
	int IGameAPI.ResourceNodeCount => GetCachedResourceNodes().Count;

	IResourceNode IGameAPI.GetResourceNode(int index)
	{
		var list = GetCachedResourceNodes();
		return (index >= 0 && index < list.Count) ? list[index] : null!;
	}

	void IGameAPI.ShowFeedbackText(string text, System.Numerics.Vector3 color)
	{
		if (Realm.Client.UI.InGameHUD.Instance != null)
		{
			var gColor = new Color(color.X, color.Y, color.Z);
			Realm.Client.UI.InGameHUD.Instance.CallDeferred(nameof(Realm.Client.UI.InGameHUD.ShowFeedbackText), text, gColor);
		}
		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientShowFeedbackText), text, new Vector3(color.X, color.Y, color.Z));
		}
	}


	void IGameAPI.TriggerVictory()
	{
		GD.Print("[GameHost] Victory triggered by map script!");
		IsGameOver = true;
		Realm.Client.UI.UIManager.Instance?.CallDeferred(nameof(Realm.Client.UI.UIManager.TransitionTo), (int)GameScreen.GameOver, true);
		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientGameOver), true);
		}
	}

	void IGameAPI.TriggerDefeat()
	{
		GD.Print("[GameHost] Defeat triggered by map script!");
		IsGameOver = true;
		Realm.Client.UI.UIManager.Instance?.CallDeferred(nameof(Realm.Client.UI.UIManager.TransitionTo), (int)GameScreen.GameOver, false);
		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientGameOver), false);
		}
	}

	IUnit? IGameAPI.GetCastle(bool isEnemy)
	{
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && u.UnitId == "castle" && u.IsEnemy == isEnemy && EcsWorld.IsAlive(u.Entity) && !EcsWorld.Has<Dead>(u.Entity))
			{
				return GetUnitWrapper(u.Entity);
			}
		}
		return null;
	}

	void IGameAPI.SpawnTargetIndicator(System.Numerics.Vector3 position, System.Numerics.Vector3 color)
	{
		SpawnTargetIndicator(new Godot.Vector3(position.X, position.Y, position.Z), new Godot.Color(color.X, color.Y, color.Z));
	}

	int IGameAPI.MaxPopulation
	{
		get => MaxPopulation;
		set => MaxPopulation = value;
	}

	int IGameAPI.CurrentPopulation => CurrentPopulation;

	IUnit? IGameAPI.GetUnitById(int uniqueId)
	{
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && u.Entity.Id == uniqueId && EcsWorld.IsAlive(u.Entity))
			{
				return GetUnitWrapper(u.Entity);
			}
		}
		return null;
	}

	IEnumerable<IUnit> IGameAPI.GetSelectedUnits()
	{
		var list = new List<IUnit>();
		foreach (var u in SelectedUnits)
		{
			if (GodotObject.IsInstanceValid(u) && EcsWorld.IsAlive(u.Entity))
			{
				list.Add(GetUnitWrapper(u.Entity));
			}
		}
		return list;
	}

	void IGameAPI.PingMinimap(System.Numerics.Vector3 position)
	{
		AddMinimapPing(new Vector3(position.X, position.Y, position.Z));
	}

	void IGameAPI.StartBuildingPlacement(string unitTypeId)
	{
		EnterBuildingPlacement(unitTypeId);
	}

	void IGameAPI.GenerateMapDirectory(string mapName, string? targetDirectory)
	{
		string parentDir = string.IsNullOrEmpty(targetDirectory) ? "user://maps" : targetDirectory;
		string globalParentDir = ProjectSettings.GlobalizePath(parentDir);
		string mapDir = System.IO.Path.Combine(globalParentDir, mapName);
		System.IO.Directory.CreateDirectory(mapDir);

		string projectRoot = PathUtils.GetProjectRoot();
		string templateDir = PathUtils.FindPath("MapTemplate");
		if (!System.IO.Directory.Exists(templateDir))
		{
			templateDir = System.IO.Path.Combine(projectRoot, "..", "MapTemplate");
		}
		string templateScriptPath = System.IO.Path.Combine(templateDir, "MapScript.cs");

		string scriptContent;
		if (System.IO.File.Exists(templateScriptPath))
		{
			scriptContent = System.IO.File.ReadAllText(templateScriptPath).Replace("__MAP_NAME__", mapName);
		}
		else
		{
			scriptContent = $@"namespace Realm.Maps;

using Realm.MapAPI;

public class {mapName} : IMapScript
{{
    public void Initialize(IGameAPI api)
    {{
    }}

    public void Update(IGameAPI api, float delta)
    {{
    }}
}}
";
		}
		System.IO.File.WriteAllText(System.IO.Path.Combine(mapDir, "MapScript.cs"), scriptContent);
		MapJsonFormatter.SaveFormattedJson(System.IO.Path.Combine(mapDir, "metadata.json"), "{}");
		MapJsonFormatter.SaveFormattedJson(System.IO.Path.Combine(mapDir, "terrain.json"), "{}");

		EnsureMapProjectFiles(mapDir);
	}

	void IGameAPI.WriteSavedData(string fileName, string content)
	{
		string mapNameOnly = System.IO.Path.GetFileNameWithoutExtension(ActiveMapName);
		_mapSaveDataService
			.WriteSavedData(mapNameOnly, fileName, content);
	}

	string IGameAPI.ReadSavedData(string fileName, string sourceMapName)
	{
		string targetMap = !string.IsNullOrWhiteSpace(sourceMapName) ? sourceMapName : ActiveMapName;
		string mapNameOnly = System.IO.Path.GetFileNameWithoutExtension(targetMap);
		return _mapSaveDataService
			.ReadSavedData(mapNameOnly, fileName);
	}
	event Action<IUnit>? IGameAPI.OnUnitCreated
	{
		add => OnUnitCreated += value;
		remove => OnUnitCreated -= value;
	}

	event Action<IUnit, IUnit?>? IGameAPI.OnUnitDied
	{
		add => OnUnitDied += value;
		remove => OnUnitDied -= value;
	}

	event Action<IUnit, IUnit, float>? IGameAPI.OnUnitDamaged
	{
		add => OnUnitDamaged += value;
		remove => OnUnitDamaged -= value;
	}

	event Action<IUnit?, string, System.Numerics.Vector3>? IGameAPI.OnSpellCast
	{
		add => OnSpellCast += value;
		remove => OnSpellCast -= value;
	}

	event Action<string, IUnit?>? IGameAPI.OnPlayerChatMessage
	{
		add => OnPlayerChatMessage += value;
		remove => OnPlayerChatMessage -= value;
	}

	event Action<IUnit>? IGameAPI.OnUnitSelected
	{
		add => OnUnitSelected += value;
		remove => OnUnitSelected -= value;
	}

	event Action<IUnit, int>? IGameAPI.OnUnitEnterZone
	{
		add => OnUnitEnterZone += value;
		remove => OnUnitEnterZone -= value;
	}

	event Action<IUnit, IUnit>? IGameAPI.OnUnitAttacked
	{
		add => OnUnitAttacked += value;
		remove => OnUnitAttacked -= value;
	}

	void IGameAPI.SpawnBarrageVolley(string effectTypeId, System.Numerics.Vector3 targetAreaCenter, float radius, int count, float intervalSeconds)
	{
		Callable.From(() =>
		{
			var center = new Vector3(targetAreaCenter.X, targetAreaCenter.Y, targetAreaCenter.Z);
			var rng = new Random();
			for (int i = 0; i < count; i++)
			{
				float delay = i * Math.Max(0.01f, intervalSeconds);
				var timer = GetTree().CreateTimer(delay);
				timer.Timeout += () =>
				{
					if (!GodotObject.IsInstanceValid(this)) return;
					float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
					float dist = (float)(rng.NextDouble() * radius);
					var impactPos = center + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
					((IGameAPI)this).SpawnVisualEffect(effectTypeId, new System.Numerics.Vector3(impactPos.X, impactPos.Y, impactPos.Z), 1.0f);
				};
			}
		}).CallDeferred();
	}

	void IGameAPI.SpawnChainBeam(string effectTypeId, System.Numerics.Vector3[] points, float jumpDelay, int forkCount, float fadeLifetime, float width, System.Numerics.Vector3? color)
	{
		Callable.From(() =>
		{
			if (points == null || points.Length < 2) return;
			var godotPoints = new Vector3[points.Length];
			for (int i = 0; i < points.Length; i++)
			{
				godotPoints[i] = new Vector3(points[i].X, points[i].Y, points[i].Z);
			}
			Color? beamCol = color.HasValue ? new Color(color.Value.X, color.Value.Y, color.Value.Z) : null;
			Realm.Client.VFX.ChainBeam3D.Create(this, godotPoints, jumpDelay, forkCount, fadeLifetime, width, beamCol);
		}).CallDeferred();
	}

	void IGameAPI.SpawnGroundShockwave(System.Numerics.Vector3 position, System.Numerics.Vector3 direction, float maxRadius, float speed, float duration, System.Numerics.Vector3? color)
	{
		Callable.From(() =>
		{
			var pos = new Vector3(position.X, position.Y, position.Z);
			var dir = new Vector3(direction.X, direction.Y, direction.Z);
			Color? waveCol = color.HasValue ? new Color(color.Value.X, color.Value.Y, color.Value.Z) : null;
			var shock = Realm.Client.VFX.GroundShockwave3D.Create(this, pos, Realm.Client.VFX.ShockwaveType.PlanarWave, maxRadius, speed, duration, waveCol);
			shock.Direction = dir;
		}).CallDeferred();
	}

	void IGameAPI.SpawnExpandingGroundRing(System.Numerics.Vector3 position, float maxRadius, float speed, float duration, System.Numerics.Vector3? color)
	{
		Callable.From(() =>
		{
			var pos = new Vector3(position.X, position.Y, position.Z);
			Color? ringCol = color.HasValue ? new Color(color.Value.X, color.Value.Y, color.Value.Z) : null;
			Realm.Client.VFX.GroundShockwave3D.Create(this, pos, Realm.Client.VFX.ShockwaveType.ExpandingGroundRing, maxRadius, speed, duration, ringCol);
		}).CallDeferred();
	}

	void IGameAPI.SpawnExpandingBurstSphere(System.Numerics.Vector3 position, float maxRadius, float speed, float duration, System.Numerics.Vector3? color)
	{
		Callable.From(() =>
		{
			var pos = new Vector3(position.X, position.Y, position.Z);
			Color? sphereCol = color.HasValue ? new Color(color.Value.X, color.Value.Y, color.Value.Z) : null;
			Realm.Client.VFX.GroundShockwave3D.Create(this, pos, Realm.Client.VFX.ShockwaveType.ExpandingBurstSphere, maxRadius, speed, duration, sphereCol);
		}).CallDeferred();
	}

	void IGameAPI.AttachPersistentAura(IUnit unit, string auraVfxId, float scale)
	{
		Callable.From(() =>
		{
			if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
			{
				if (Realm.Client.Core.GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
				{
					unit3D.SetSocketAttachment(Realm.Shared.Animation.HumanoidBone.Chest, auraVfxId, null, null, scale, null, null, false);
				}
			}
		}).CallDeferred();
	}

	public event Action<IUnit, string>? OnItemSold;
	event Action<IUnit, string>? IGameAPI.OnItemSold
	{
		add => OnItemSold += value;
		remove => OnItemSold -= value;
	}

	public event Action<IUnit>? OnConstructionFinished;
	event Action<IUnit>? IGameAPI.OnConstructionFinished
	{
		add => OnConstructionFinished += value;
		remove => OnConstructionFinished -= value;
	}

	public event Action<IUnit, string, System.Numerics.Vector3>? OnUnitOrdered;
	event Action<IUnit, string, System.Numerics.Vector3>? IGameAPI.OnUnitOrdered
	{
		add => OnUnitOrdered += value;
		remove => OnUnitOrdered -= value;
	}

	void IGameAPI.SetUnitFacing(IUnit unit, float facingRadians)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			EcsWorld.SetOrAdd(wrapper.Entity, new RotationY(facingRadians));
			if (EcsWorld.Has<InterpolationTarget>(wrapper.Entity))
			{
				ref var interp = ref EcsWorld.Get<InterpolationTarget>(wrapper.Entity);
				interp.RotationY = facingRadians;
			}
			if (Realm.Client.Core.GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				var rot = unit3D.Rotation;
				rot.Y = facingRadians;
				unit3D.Rotation = rot;
			}
		}
	}

	void IGameAPI.CreateFloatingText(string text, System.Numerics.Vector3 position, System.Numerics.Vector3 color, float duration)
	{
		var godotPos = new Vector3(position.X, position.Y, position.Z);
		var godotCol = new Color(color.X, color.Y, color.Z);
		CreateFloatingTextInternal(text, godotPos, godotCol, duration);
		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientCreateFloatingText), text, godotPos, new Vector3(color.X, color.Y, color.Z), duration);
		}
	}

	int IGameAPI.CreateStaticText(string text, System.Numerics.Vector3 position, System.Numerics.Vector3 color, int fontSize)
	{
		int handle = _nextStaticTextHandle++;
		Callable.From(() =>
		{
			var label = new Label3D();
			label.Text = text;
			label.Modulate = new Color(color.X, color.Y, color.Z);
			label.OutlineModulate = Colors.Black;
			label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
			label.Position = new Vector3(position.X, position.Y + 1.5f, position.Z);
			label.FontSize = fontSize;
			AddChild(label);
			_staticTextLabels[handle] = label;
		}).CallDeferred();
		return handle;
	}

	void IGameAPI.SetStaticText(int handle, string text)
	{
		Callable.From(() =>
		{
			if (_staticTextLabels.TryGetValue(handle, out var label))
			{
				label.Text = text;
			}
		}).CallDeferred();
	}

	void IGameAPI.SetStaticTextVisible(int handle, bool visible)
	{
		Callable.From(() =>
		{
			if (_staticTextLabels.TryGetValue(handle, out var label))
			{
				label.Visible = visible;
			}
		}).CallDeferred();
	}

	void IGameAPI.DestroyStaticText(int handle)
	{
		Callable.From(() =>
		{
			if (_staticTextLabels.Remove(handle, out var label))
			{
				label.QueueFree();
			}
		}).CallDeferred();
	}

	void IGameAPI.SpawnVisualEffect(string effectTypeId, System.Numerics.Vector3 position, float scale)
	{
		Callable.From(() =>
		{
			var pos = new Vector3(position.X, position.Y, position.Z);
			var def = GetAbilityDefinition(effectTypeId);
			if (def != null)
			{
				_fxService.SpawnAbilityEffect(this, def, pos, scale);
			}
			else
			{
				_fxService.SpawnSpritesheetEffect(this, effectTypeId, pos + new Vector3(0, 0.5f, 0), 4, 4, 0.05f, scale * 6f);
			}
		}).CallDeferred();
	}

	void IGameAPI.AddBuff(IUnit unit, string buffId, float duration)
	{
		unit.AddBuff(buffId, duration);
	}

	void IGameAPI.RegisterBuffModifier(string buffId, string statName, bool isPercentage, float value)
	{
		var statId = new Realm.Ecs.Common.StatId(statName);
		var modType = isPercentage ? Realm.Ecs.Components.Stats.ModifierType.Percentage : Realm.Ecs.Components.Stats.ModifierType.Flat;
		var modifier = new Realm.Ecs.Components.Stats.StatModifier(statId, modType, value);
		if (!Realm.Ecs.Common.BuffRegistry.BuffModifiers.TryGetValue(buffId, out var list))
		{
			list = new System.Collections.Generic.List<Realm.Ecs.Components.Stats.StatModifier>();
			Realm.Ecs.Common.BuffRegistry.BuffModifiers[buffId] = list;
		}
		list.Add(modifier);
	}

	void IGameAPI.CastAbility(IUnit unit, string abilityId, System.Numerics.Vector3 targetPosition)
	{
		var godotPos = new Godot.Vector3(targetPosition.X, targetPosition.Y, targetPosition.Z);
		var def = GetAbilityDefinition(abilityId);
		var casterEnt = unit is IEcsEntityWrapper w ? w.Entity : Entity.Null;

		if (def != null)
		{
			_fxService.SpawnAbilityEffect(this, def, godotPos);
			if (def.Damage > 0f)
			{
				float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;
				_simulationService.DealSpellDamageAOE(targetPosition, aoe, def.Damage, casterEnt);
			}
			else if (def.Healing > 0f)
			{
				float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;
				_simulationService.HealAOE(targetPosition, aoe, def.Healing);
			}
			else
			{
				OnSpellCast?.Invoke(unit, abilityId, targetPosition);
			}
		}
		else
		{
			OnSpellCast?.Invoke(unit, abilityId, targetPosition);
		}
	}

	float IGameAPI.GetAbilityCooldown(IUnit unit, string abilityId)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (EcsWorld.Has<Realm.Ecs.Components.Core.Cooldowns>(wrapper.Entity))
			{
				var cds = EcsWorld.Get<Realm.Ecs.Components.Core.Cooldowns>(wrapper.Entity).Value;
				if (cds.TryGetValue(abilityId, out var val))
				{
					return val;
				}
			}
			if (EcsWorld.Has<Realm.Ecs.Components.Core.SpellCooldowns>(wrapper.Entity))
			{
				var scds = EcsWorld.Get<Realm.Ecs.Components.Core.SpellCooldowns>(wrapper.Entity).Value;
				if (scds != null && scds.TryGetValue(abilityId, out var val))
				{
					return val;
				}
			}
		}
		return 0f;
	}

	void IGameAPI.SetAbilityCooldown(IUnit unit, string abilityId, float cooldown)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			System.Collections.Generic.Dictionary<string, float> dict;
			if (EcsWorld.Has<Realm.Ecs.Components.Core.Cooldowns>(wrapper.Entity))
			{
				dict = EcsWorld.Get<Realm.Ecs.Components.Core.Cooldowns>(wrapper.Entity).Value;
			}
			else
			{
				dict = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
				EcsWorld.Add(wrapper.Entity, new Realm.Ecs.Components.Core.Cooldowns(dict));
			}
			dict[abilityId] = cooldown;

			if (EcsWorld.Has<Realm.Ecs.Components.Core.SpellCooldowns>(wrapper.Entity))
			{
				var scd = EcsWorld.Get<Realm.Ecs.Components.Core.SpellCooldowns>(wrapper.Entity).Value;
				if (scd != null) scd[abilityId] = cooldown;
			}
			else
			{
				var scdDict = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase) { [abilityId] = cooldown };
				EcsWorld.Add(wrapper.Entity, new Realm.Ecs.Components.Core.SpellCooldowns(scdDict));
			}
		}
	}

	void IGameAPI.RemoveBuff(IUnit unit, string buffId)
	{
		unit.RemoveBuff(buffId);
	}

	System.Collections.Generic.IEnumerable<string> IGameAPI.GetModifiers(IUnit unit)
	{
		return unit.GetModifiers();
	}

	void IGameAPI.SpawnProjectile(string projectileTypeId, System.Numerics.Vector3 start, System.Numerics.Vector3 target, float speed)
	{
		Callable.From(() =>
		{
			var startPos = new Vector3(start.X, start.Y, start.Z);
			if (_replayService != null && _replayService.IsRecording)
			{
				_replayService.RecordProjectile(projectileTypeId, start, target);
			}
			var targetPos = new Vector3(target.X, target.Y, target.Z);
			SpawnWeaponProjectile(startPos, targetPos, projectileTypeId);
		}).CallDeferred();
	}

	void IGameAPI.SetLeaderboardVisible(string title, bool visible)
	{
		Callable.From(() => Realm.Client.UI.InGameHUD.Instance?.SetLeaderboardVisible(title, visible)).CallDeferred();
	}

	void IGameAPI.SetLeaderboardValue(string label, string value)
	{
		Callable.From(() => Realm.Client.UI.InGameHUD.Instance?.SetLeaderboardValue(label, value)).CallDeferred();
	}

	void IGameAPI.ClearLeaderboard()
	{
		Callable.From(() => Realm.Client.UI.InGameHUD.Instance?.ClearLeaderboard()).CallDeferred();
	}

	void IGameAPI.StartCountdownTimer(float duration, string label)
	{
		Callable.From(() => Realm.Client.UI.InGameHUD.Instance?.StartCountdownTimer(duration, label)).CallDeferred();
		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientStartCountdownTimer), duration, label);
		}
	}

	void IGameAPI.StopCountdownTimer()
	{
		Callable.From(() => Realm.Client.UI.InGameHUD.Instance?.StopCountdownTimer()).CallDeferred();
		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientStopCountdownTimer));
		}
	}

	void IGameAPI.ShakeCamera(float intensity, float duration)
	{
		Callable.From(() =>
		{
			var camera = MainCamera;
			if (camera == null) return;

			var startPos = camera.Position;
			var tween = CreateTween();
			float stepDuration = duration / 20f;
			for (int i = 0; i < 20; i++)
			{
				var offset = new Vector3(
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1),
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1),
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1)
				);
				tween.TweenProperty(camera, "position", startPos + offset, stepDuration);
			}
			tween.TweenProperty(camera, "position", startPos, stepDuration);
		}).CallDeferred();
	}

	void IGameAPI.PanCameraTo(System.Numerics.Vector3 position, float duration)
	{
		PanCameraInternal(new Vector3(position.X, position.Y, position.Z), duration);
	}

	void IGameAPI.SetTimeOfDay(float time)
	{
		Callable.From(() =>
		{

			float clampedTime = Mathf.Clamp(time, 0f, 24f);

			int index;
			if (clampedTime >= 5f && clampedTime < 6f) index = 3;
			else if (clampedTime >= 6f && clampedTime < 18f) index = 0;
			else if (clampedTime >= 18f && clampedTime < 20f) index = 1;
			else index = 2;

			float timer = (clampedTime / 24f) * TimeOfDayCycleDuration;
			UpdateDayNightVisuals(clampedTime / 24f);

			EcsWorld?.Mutate<WorldState>(_worldEntity, (ref WorldState state) =>
				EcsWorld.Set(_worldEntity, new WorldState(state.GameElapsedTime, index, timer, state.DayNightCycleEnabled)));
		}).CallDeferred();
	}

	void IGameAPI.SetDayNightCycleEnabled(bool enabled)
	{
		DayNightCycleEnabled = enabled;
	}

	void IGameAPI.SetEnvironmentPreset(string presetId)
	{
		Callable.From(() =>
		{
			_environmentService?.ApplyPresetById(this, presetId);
			if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
			{
				Rpc(nameof(SyncEnvironmentPresetRpc), presetId, 0f);
			}
		}).CallDeferred();
	}

	void IGameAPI.TransitionEnvironmentPreset(string presetId, float durationSeconds)
	{
		Callable.From(() =>
		{
			_environmentService?.TransitionToPreset(this, presetId, durationSeconds);
			if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
			{
				Rpc(nameof(SyncEnvironmentPresetRpc), presetId, durationSeconds);
			}
		}).CallDeferred();
	}

	string IGameAPI.GetCurrentEnvironmentPreset()
	{
		return _environmentService?.GetCurrentPresetId() ?? "day";
	}

	void IGameAPI.SetWeather(string weatherType)
	{
		Callable.From(() =>
		{
			_environmentService?.SetCurrentWeather(weatherType);
			Realm.Client.UI.InGameHUD.Instance?.ApplyWeatherEffects(weatherType);
			if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
			{
				Rpc(nameof(SyncWeatherRpc), weatherType);
			}
		}).CallDeferred();
	}

	string IGameAPI.GetWeather()
	{
		return _environmentService?.GetCurrentWeather() ?? "clear";
	}

	void IGameAPI.DisableShroud()
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllShroudStateQuery;
		EcsWorld.Query(in query, ent => worldEntity = ent);

		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<ShroudState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<ShroudState>(worldEntity);
			state.ShroudType = "visible";
		}
		if (_shroudService != null)
		{
			_shroudService.TriggerImmediateUpdate();
		}
	}

	void IGameAPI.SetUnitAnimation(IUnit unit, string animationName)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (Realm.Client.Core.GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				unit3D.PlayAnimation(animationName);
			}
		}
	}

	void IGameAPI.SetUnitHandAttachment(IUnit unit, string hand, string? attachmentId)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (Realm.Client.Core.GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				var boneHand = HumanoidBone.RightHand;
				if (!string.IsNullOrEmpty(hand) && (hand.Equals("LeftHand", StringComparison.OrdinalIgnoreCase) || hand.Equals("left", StringComparison.OrdinalIgnoreCase) || hand.Equals("hand_l", StringComparison.OrdinalIgnoreCase)))
				{
					boneHand = HumanoidBone.LeftHand;
				}
				unit3D.SetHandAttachment(boneHand, attachmentId);
			}
		}
	}

	void IGameAPI.KillUnit(IUnit unit, bool executeDespawnShader, bool playDeathAnimation)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (Realm.Client.Core.GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D))
			{
				if (GodotObject.IsInstanceValid(unit3D))
				{
					if (!EcsWorld.Has<Dead>(wrapper.Entity))
					{
						EcsWorld.Add<Dead>(wrapper.Entity);
						Callable.From(() => KillUnit(unit3D, executeDespawnShader, playDeathAnimation)).CallDeferred();
					}
				}
			}
		}
	}

	void IGameAPI.DestroyUnit(IUnit unit, bool executeDespawnShader, bool playDeathAnimation)
	{
		if (unit is not IEcsEntityWrapper wrapper || !EcsWorld.IsAlive(wrapper.Entity)) return;
		if (!Realm.Client.Core.GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D) || !GodotObject.IsInstanceValid(unit3D)) return;

		RemoveUnitFromCollections(unit3D, wrapper.Entity);

		if (unit3D.IsBuilding)
		{
			float radius = EcsWorld.Has<CollisionRadius>(wrapper.Entity) ? EcsWorld.Get<CollisionRadius>(wrapper.Entity).Value : 2.0f;
			var unitPos = EcsWorld.Has<Position>(wrapper.Entity) ? EcsWorld.Get<Position>(wrapper.Entity).Value : new System.Numerics.Vector3(unit3D.Position.X, unit3D.Position.Y, unit3D.Position.Z);
			UncarveObstacle(unitPos, radius);
		}

		UpdateMultiplayerMapping(unit3D.Entity.Id);

		int id = wrapper.Entity.Id;
		_unitWrapperCache.Remove(id);
		EcsWorld.Destroy(wrapper.Entity);

		unit3D.CollisionLayer = 0;
		unit3D.CollisionMask = 0;

		if (playDeathAnimation) unit3D.PlayAnimation("Death");

		HandleUnitDespawn(unit3D, executeDespawnShader, playDeathAnimation);
	}

	private void RemoveUnitFromCollections(Unit3D unit3D, in Arch.Core.Entity entity)
	{
		SelectedUnits.Remove(unit3D);
		AllUnits.Remove(unit3D);
		EntityToUnit3D.Remove(entity);
		if (unit3D.UnitId == "castle")
		{
			_castlesList.Remove(unit3D);
		}
	}

	private void UpdateMultiplayerMapping(int entityId)
	{
		if (!_multiplayerActive) return;

		if (_clientToServerEntityMap.TryGetValue(entityId, out int serverId))
		{
			_serverToClientEntityMap.Remove(serverId);
		}
		_clientToServerEntityMap.Remove(entityId);
	}

	private void HandleUnitDespawn(Unit3D unit3D, bool executeDespawnShader, bool playDeathAnimation)
	{
		string deathShader = executeDespawnShader ? GetModelDeathShader(unit3D.UnitId) : "";
		if (executeDespawnShader && string.IsNullOrEmpty(deathShader))
		{
			deathShader = GetModelDeathShader(unit3D);
		}

		if (!string.IsNullOrEmpty(deathShader))
		{
			SpawnDeathShaderManager.AnimateTransition(unit3D, deathShader, false, null, () =>
			{
				if (GodotObject.IsInstanceValid(unit3D)) unit3D.QueueFree();
			});
		}
		else if (playDeathAnimation)
		{
			var tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(unit3D, "position:y", -3.0f, 1.0f);
			tween.TweenProperty(unit3D, "scale", Vector3.Zero, 1.0f);
			tween.Chain().TweenCallback(Callable.From(unit3D.QueueFree));
		}
		else
		{
			unit3D.QueueFree();
		}
	}

	int IGameAPI.PlayerCount
	{
		get
		{
			if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true)
			{
				int count = 0;
				foreach (var p in playersState.Players)
				{
					if (p.Active) count++;
				}
				return Math.Max(1, count);
			}
			return 1;
		}
	}

	string IGameAPI.GetPlayerName(int playerIndex)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
		    && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Name;
		}
		return $"Player {playerIndex + 1}";
	}

	bool IGameAPI.IsPlayerActive(int playerIndex)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
		    && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Active;
		}
		return playerIndex == 0;
	}

	float IGameAPI.GetPlayerGold(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).Gold;
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
		    && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Gold;
		}
		return 0f;
	}

	void IGameAPI.SetPlayerGold(int playerIndex, float amount)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Gold = amount; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Gold = Math.Max(0f, amount);
		}
		SyncPlayerResourceEcs(playerIndex, amount);
	}

	void IGameAPI.AdjustPlayerGold(int playerIndex, float delta)
	{
		float current = ((IGameAPI)this).GetPlayerGold(playerIndex);
		float newAmount = Math.Max(0f, current + delta);
		((IGameAPI)this).SetPlayerGold(playerIndex, newAmount);
	}

	IUnit IGameAPI.SpawnUnitForPlayer(string unitTypeId, System.Numerics.Vector3 position, int playerIndex, bool executeSpawnShader)
	{
		bool isEnemy = NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex);
		var unit = ((IGameAPI)this).SpawnUnit(unitTypeId, position, isEnemy, false, executeSpawnShader);
		unit.Player = playerIndex;
		if (playerIndex > 0 && _multiplayerActive && IsServerActive() && Realm.Client.Network.LobbyManager.Instance != null)
		{
			var p = Realm.Client.Network.LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
			if (p != null && p.PeerId > 1)
			{
				RpcId(p.PeerId, nameof(ClientPanCameraTo), new Vector3(position.X, position.Y, position.Z), 0f);
			}
		}
		return unit;
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsOwnedByPlayer(int playerIndex)
	{
		foreach (var unit in ((IGameAPI)this).GetAllUnits())
		{
			if (unit.Player == playerIndex) yield return unit;
		}
	}

	void IGameAPI.TriggerPlayerDefeat(int playerIndex, string reason)
	{
		if (!_multiplayerActive && playerIndex == 0)
		{
			((IGameAPI)this).TriggerDefeat();
		}
	}

	void IGameAPI.TriggerPlayerVictory(int playerIndex)
	{
		if (playerIndex == 0) ((IGameAPI)this).TriggerVictory();
	}

	void IGameAPI.BroadcastMessage(string message)
	{
		string formatted = $"[HOST BROADCAST] {message}";
		GD.Print(formatted);
		WasmRuntime.LogToConsole(formatted);
		((IGameAPI)this).ShowFeedbackText(message, new System.Numerics.Vector3(0.9f, 0.9f, 0.9f));
		if (_multiplayerActive && IsServerActive())
		{
			Realm.Client.Network.LobbyManager.Instance?.SendChatMessage("System", message, false);
		}
	}

	void IGameAPI.SendMessageToPlayer(int playerIndex, string message)
	{
		string formatted = $"[HOST MESSAGE P{playerIndex}] {message}";
		WasmRuntime.LogToConsole(formatted);
		if (playerIndex == 0)
		{
			if (Realm.Client.UI.InGameHUD.Instance != null)
			{
				Realm.Client.UI.InGameHUD.Instance.CallDeferred(nameof(Realm.Client.UI.InGameHUD.ShowFeedbackText), message, new Color(0.9f, 0.9f, 0.9f));
			}
		}
		else if (_multiplayerActive && IsServerActive() && Realm.Client.Network.LobbyManager.Instance != null)
		{
			var p = Realm.Client.Network.LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
			if (p != null && p.PeerId > 1)
			{
				RpcId(p.PeerId, nameof(ClientShowFeedbackText), message, new Vector3(0.9f, 0.9f, 0.9f));
			}
		}
	}

	private event Action<int>? _onTimerExpired;
	event Action<int>? IGameAPI.OnTimerExpired
	{
		add => _onTimerExpired += value;
		remove => _onTimerExpired -= value;
	}

	int IGameAPI.ScheduleTimer(float delay)
	{
		int handle = _nextTimerHandle++;
		_scheduledTimers[handle] = (delay, delay, false, () => _onTimerExpired?.Invoke(handle));
		return handle;
	}

	int IGameAPI.ScheduleRepeatingTimer(float interval)
	{
		int handle = _nextTimerHandle++;
		_scheduledTimers[handle] = (interval, interval, true, () => _onTimerExpired?.Invoke(handle));
		return handle;
	}

	void IGameAPI.CancelTimer(int timerHandle)
	{
		_scheduledTimers.Remove(timerHandle);
	}

	int IGameAPI.RandomInt(int min, int max) => Rng.Next(min, max + 1);

	float IGameAPI.RandomFloat(float min, float max) => min + (float)Rng.NextDouble() * (max - min);

	System.Numerics.Vector3 IGameAPI.GetPlayerStartLocation(int playerIndex)
	{
		return System.Numerics.Vector3.Zero;
	}

	void IGameAPI.SetPlayerTeam(int playerIndex, int teamIndex)
	{
	}

	int IGameAPI.GetPlayerTeam(int playerIndex)
	{
		return 0;
	}

	void IGameAPI.SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied)
	{
	}

	bool IGameAPI.IsPlayerComputer(int playerIndex)
	{
		if (playerIndex == 0) return false;
		if (_multiplayerActive && Realm.Client.Network.LobbyManager.Instance != null)
		{
			var p = Realm.Client.Network.LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
			if (p != null)
			{
				return p.PeerId < 0;
			}
		}
		return playerIndex == 1;
	}

	void IGameAPI.SetUnitColor(IUnit unit, System.Numerics.Vector3 color)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (Realm.Client.Core.GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
				unit3D.ApplyModelTint(new Godot.Color(color.X, color.Y, color.Z));
		}
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsInRadius(System.Numerics.Vector3 center, float radius, System.Func<IUnit, bool> filter)
	{
		var godotCenter = new Vector3(center.X, center.Y, center.Z);
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(u) || !EcsWorld.IsAlive(u.Entity)) continue;
			if (u.GlobalPosition.DistanceTo(godotCenter) > radius) continue;
			var w = GetUnitWrapper(u.Entity);
			if (filter(w)) list.Add(w);
		}
		return list;
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsOwnedByPlayer(int playerIndex, System.Func<IUnit, bool> filter)
	{
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(u) || !EcsWorld.IsAlive(u.Entity)) continue;
			if (u.Player != playerIndex) continue;
			var w = GetUnitWrapper(u.Entity);
			if (filter(w)) list.Add(w);
		}
		return list;
	}

	void IGameAPI.IssueAttackMoveOrder(IUnit unit, System.Numerics.Vector3 destination)
	{
		if (unit != null)
		{
			unit.AttackMove(destination);
			OnUnitOrdered?.Invoke(unit, "AttackMove", destination);
		}
	}

	void IGameAPI.IssueCastOrder(IUnit caster, string abilityId, IUnit target)
	{
		if (caster != null)
		{
			caster.Attack(target);
			System.Numerics.Vector3 pos = target?.Position ?? System.Numerics.Vector3.Zero;
			OnUnitOrdered?.Invoke(caster, abilityId ?? "Cast", pos);
		}
	}

	void IGameAPI.IssueCastOrderAt(IUnit caster, string abilityId, System.Numerics.Vector3 position)
	{
		if (caster != null)
		{
			caster.AttackMove(position);
			OnUnitOrdered?.Invoke(caster, abilityId ?? "CastAt", position);
		}
	}

	void IGameAPI.SetAbilityAutoCast(IUnit unit, string abilityId, bool active)
	{
	}

	void IGameAPI.SetPlayerComputerControlled(int playerIndex, bool isProxy)
	{
	}

	void IGameAPI.AddLeaderboardRow(string label, string value, System.Numerics.Vector3? color)
	{
		Realm.Client.UI.InGameHUD.Instance?.SetLeaderboardValue(label, value);
	}

	float IGameAPI.GetPlayerWood(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).Wood;
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Wood;
		}
		return 0f;
	}

	void IGameAPI.SetPlayerWood(int playerIndex, float amount)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Wood = amount; return; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Wood = Math.Max(0f, amount);
		}
	}

	void IGameAPI.AdjustPlayerWood(int playerIndex, float delta)
	{
		if (playerIndex == 0) { ((IGameAPI)this).Wood += delta; return; }
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Wood = Math.Max(0f, playersState.Players[playerIndex].Wood + delta);
		}
	}

	void IGameAPI.SetUnitOwner(IUnit unit, int playerIndex)
	{
		unit.Player = playerIndex;
	}

	int IGameAPI.GetPlayerCurrentPopulation(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).CurrentPopulation;
		return 0;
	}

	int IGameAPI.GetPlayerMaxPopulation(int playerIndex)
	{
		if (playerIndex == 0) return ((IGameAPI)this).MaxPopulation;
		return 200;
	}

	void IGameAPI.SetPlayerMaxPopulation(int playerIndex, int max)
	{
		if (playerIndex == 0) ((IGameAPI)this).MaxPopulation = max;
	}

	void IGameAPI.SetCountdownTimerLabel(string label)
	{
		Realm.Client.UI.InGameHUD.Instance?.UpdateCountdownLabel(label);
	}

	void IGameAPI.IssueAttackMoveOrderToPlayer(int playerIndex, System.Numerics.Vector3 destination)
	{
		foreach (var unit in ((IGameAPI)this).GetUnitsOwnedByPlayer(playerIndex))
		{
			if (!unit.IsDead && !unit.IsBuilding)
				unit.AttackMove(destination);
		}
	}

	int IGameAPI.CountUnitsOwnedByPlayer(int playerIndex)
	{
		int count = 0;
		foreach (var unit in ((IGameAPI)this).GetUnitsOwnedByPlayer(playerIndex))
		{
			if (!unit.IsDead)
				count++;
		}
		return count;
	}

	int IGameAPI.DefineZone(float minX, float minZ, float maxX, float maxZ)
	{
		if (EcsWorld?.TryGet<ScriptZonesState>(_worldEntity, out var zonesState) == true)
		{
			int handle = zonesState.Zones.Count;
			float cx = (minX + maxX) * 0.5f;
			float cz = (minZ + maxZ) * 0.5f;
			zonesState.Zones.Add(new ZoneBounds
			{
				MinX = minX,
				MinZ = minZ,
				MaxX = maxX,
				MaxZ = maxZ,
				Center = new System.Numerics.Vector3(cx, 0f, cz)
			});
			return handle;
		}
		return -1;
	}

	System.Numerics.Vector3 IGameAPI.GetZoneCenter(int zoneHandle)
	{
		if (EcsWorld?.TryGet<ScriptZonesState>(_worldEntity, out var zonesState) == true
		    && zoneHandle >= 0 && zoneHandle < zonesState.Zones.Count)
		{
			return zonesState.Zones[zoneHandle].Center;
		}
		return System.Numerics.Vector3.Zero;
	}

	void IGameAPI.SetUnitRouteState(IUnit unit, int state)
	{
		unit.SetCustomData("__routeState", state);
	}

	int IGameAPI.GetUnitRouteState(IUnit unit)
	{
		string? data = unit.GetCustomData("__routeState");
		return int.TryParse(data, out int i) ? i : 0;
	}

	void IGameAPI.SetUnitLevel(IUnit unit, int level)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			EcsWorld.SetOrAdd(wrapper.Entity, new Realm.Ecs.Components.Meta.Level(level));
		}
	}

	int IGameAPI.GetPlayerKills(int playerIndex)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].KillCount;
		}
		return 0;
	}

	void IGameAPI.SetPlayerKills(int playerIndex, int kills)
	{
		if (EcsWorld?.TryGet<ScriptPlayersState>(_worldEntity, out var playersState) == true
			&& playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].KillCount = kills;
		}
	}

	void IGameAPI.IssueMoveOrder(IUnit unit, System.Numerics.Vector3 destination)
	{
		if (unit != null)
		{
			unit.MoveTo(destination);
			OnUnitOrdered?.Invoke(unit, "Move", destination);
		}
	}

	void IGameAPI.SetUnitSpellImmune(IUnit unit, bool immune)
	{
		if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
		{
			if (immune)
			{
				if (!EcsWorld.Has<Realm.Ecs.Components.Tags.SpellImmune>(wrapper.Entity))
					EcsWorld.Add(wrapper.Entity, new Realm.Ecs.Components.Tags.SpellImmune());
			}
			else
			{
				if (EcsWorld.Has<Realm.Ecs.Components.Tags.SpellImmune>(wrapper.Entity))
					EcsWorld.Remove<Realm.Ecs.Components.Tags.SpellImmune>(wrapper.Entity);
			}
		}
	}

	void IGameAPI.SelectUnit(IUnit unit)
	{
		Callable.From(() =>
		{
			if (unit is IEcsEntityWrapper wrapper && EcsWorld.IsAlive(wrapper.Entity))
			{
				if (Realm.Client.Core.GameHost.TryGetUnit3D(wrapper.Entity, out var unit3D))
				{
					if (GodotObject.IsInstanceValid(unit3D))
					{
						ClearSelection();
						SelectUnit(unit3D);
						Realm.Client.UI.InGameHUD.Instance?.RefreshUI(SelectedUnits);
					}
				}
			}
		}).CallDeferred();
	}

	void IGameAPI.ClearSelection()
	{
		Callable.From(() =>
		{
			ClearSelection();
			Realm.Client.UI.InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}).CallDeferred();
	}

	bool IGameAPI.HasCoordinate(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return false;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	System.Numerics.Vector3 IGameAPI.GetCoordinateMin(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return System.Numerics.Vector3.Zero;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return new System.Numerics.Vector3(r.MinX, 0f, r.MinZ);
		}
		return System.Numerics.Vector3.Zero;
	}

	System.Numerics.Vector3 IGameAPI.GetCoordinateMax(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return System.Numerics.Vector3.Zero;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return new System.Numerics.Vector3(r.MaxX, 0f, r.MaxZ);
		}
		return System.Numerics.Vector3.Zero;
	}

	bool IGameAPI.IsPositionInCoordinate(System.Numerics.Vector3 position, string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return false;
		string searchName = coordinateName.Trim();
		foreach (var r in EditorCoordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
			{
				float minX = Math.Min(r.MinX, r.MaxX);
				float maxX = Math.Max(r.MinX, r.MaxX);
				float minZ = Math.Min(r.MinZ, r.MaxZ);
				float maxZ = Math.Max(r.MinZ, r.MaxZ);
				return position.X >= minX && position.X <= maxX && position.Z >= minZ && position.Z <= maxZ;
			}
		}
		return false;
	}

	void IGameAPI.AddUnitTypeAbility(string unitTypeId, string abilityId)
	{
		if (UnitRegistry.TryGetValue(unitTypeId, out var meta))
		{
			var abilities = meta.Abilities != null
				? new List<string>(meta.Abilities)
				: new List<string>();
			if (!abilities.Contains(abilityId))
			{
				abilities.Add(abilityId);
				meta.Abilities = abilities.ToArray();
				UnitRegistry[unitTypeId] = meta;
			}
		}
	}

	void IGameAPI.PlayWarningSound()
	{
		_audioService.PlayWarningSound();
	}

	void IGameAPI.PlayClickSound()
	{
		_audioService.PlayClickSound();
	}

	void IGameAPI.SetPlayerBotProfile(int playerIndex, string profileJson)
	{
		if (string.IsNullOrWhiteSpace(profileJson)) return;
		try
		{
			var profile = Realm.Ecs.AI.Policy.BotProfile.FromJson(profileJson);
			_simulationService.SetBotProfile(playerIndex, profile);
		}
		catch { }
	}

	string IGameAPI.GetPlayerBotProfile(int playerIndex)
	{
		var profile = _simulationService.GetBotProfile(playerIndex);
		return profile != null ? profile.ToJson() : string.Empty;
	}

	void IGameAPI.SetPlayerBotGenre(int playerIndex, string genreName, string? configJson)
	{
		_simulationService.SetBotGenre(playerIndex, genreName, configJson);
	}

	string IGameAPI.GetPlayerBotGenre(int playerIndex)
	{
		return _simulationService.GetBotGenre(playerIndex);
	}

	void IGameAPI.RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, System.Numerics.Vector3 position, string payload)
	{
		_simulationService.RegisterCustomBotDecision(playerIndex, actionId, intent, featureVector, position, payload);
	}

	void IGameAPI.ClearCustomBotDecisions(int playerIndex)
	{
		_simulationService.ClearCustomBotDecisions(playerIndex);
	}

	event Action<int, string, string, System.Numerics.Vector3, string>? IGameAPI.OnBotCustomActionExecuted
	{
		add => _simulationService.OnBotCustomActionExecuted += value;
		remove => _simulationService.OnBotCustomActionExecuted -= value;
	}

	string IGameAPI.TrainBotProfile(string mapName, int generations, int populationSize, int matchesPerEvaluation, string genre)
	{
		var genreProvider = Realm.Ecs.AI.Genres.AiGenreRegistry.Get(genre);
		var trainer = new Realm.Ecs.AI.Training.SelfPlayTrainer();
		var profile = trainer.TrainSelfPlay(mapName, generations, populationSize, matchesPerEvaluation, genreProvider: genreProvider);
		return profile.ToJson();
	}
}
