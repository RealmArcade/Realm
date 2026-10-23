using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.MapAPI;
using Realm.Ecs.Replication;
using PathingFlags = Realm.MapAPI.PathingFlags;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;

public partial class GameHost
{
	private readonly UnitId[] _cachedUnitsBuffer = new UnitId[4096];
	private readonly LootItemId[] _cachedLootBuffer = new LootItemId[512];
	private readonly PlayerId[] _cachedPlayersBuffer = new PlayerId[25];
	private readonly UnitId[] _cachedSelectedUnitsBuffer = new UnitId[512];
	private readonly string[] _cachedModifiersBuffer = new string[128];
	private readonly string[] _cachedItemsBuffer = new string[32];

	private const string DefaultLeaderboardBoardId = "default";

	private enum LeaderboardOperationKind : byte
	{
		ClearAll,
		ClearBoard,
		SetBoard,
		SetGroup,
		AddRow
	}

	private struct LeaderboardOperation
	{
		public LeaderboardOperationKind Kind;
		public string BoardId;
		public string Title;
		public string GroupTitle;
		public string Label;
		public string Value;
		public int GroupPlayerIndex;
		public bool Visible;
	}

	private readonly List<LeaderboardOperation> _pendingLeaderboardOperations = new();
	private bool _leaderboardFlushQueued;

	public void ResetAbilityCatalog()
	{
		GameContext.RegistryService.ResetAbilityCatalog();
	}

	public void RegisterCustomAbilities(List<AbilityMetadata> customAbilities)
	{
		GameContext.RegistryService.RegisterCustomAbilities(customAbilities);
	}

	public AbilityDefinition GetAbilityDefinition(string abilityId)
	{
		return GameContext.RegistryService.GetAbilityDefinition(abilityId);
	}

	bool IGameAPI.IsPredictiveFrame => GameSettings.MovementPrediction && (GameContext.MovementPredictionService?.IsPredictiveFrameContext ?? false);
	bool IGameAPI.IsAuthoritativeFrame => !((IGameAPI)this).IsPredictiveFrame;

	void IGameAPI.RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (GameContext.RegistryService.RegisterAbility(abilityId, displayName, tooltip, iconPath, isInstant))
		{
			RegisterAbilityView(abilityId, displayName, tooltip, iconPath, isInstant);
		}
	}

	void IGameAPI.RegisterPlayerAbility(int playerIndex, string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		GameContext.ClientRPC.RegisterAbility(playerIndex, abilityId, displayName, tooltip, iconPath, isInstant);
	}

	private void RegisterAbilityView(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientRegisterAbility), abilityId, displayName ?? "", tooltip ?? "", iconPath ?? "", isInstant);
		}
	}

	void IGameAPI.SetAbilityInstant(string abilityId, bool isInstant)
	{
		GameContext.RegistryService.SetAbilityInstant(abilityId, isInstant);
	}

	void IGameAPI.SetAbilityIcon(string abilityId, string iconPath)
	{
		GameContext.RegistryService.SetAbilityIcon(abilityId, iconPath);
	}

	void IGameAPI.SetAbilityTooltip(string abilityId, string tooltip)
	{
		GameContext.RegistryService.SetAbilityTooltip(abilityId, tooltip);
	}

	void IGameAPI.SetAbilityGridPosition(string abilityId, int x, int y)
	{
		GameContext.RegistryService.SetAbilityGridPosition(abilityId, x, y);
	}

	void IGameAPI.SetAbilityManaCost(UnitId unit, string abilityId, float manaCost)
	{
		GameContext.RegistryService.SetAbilityManaCost(unit, abilityId, manaCost);
	}

	void IGameAPI.ShowSummaryTable(string title, bool visible)
	{
		Callable.From(() => InGameHUD.Instance?.ShowSummaryTable(title, visible)).CallDeferred();
	}

	void IGameAPI.SetSummaryTableHeaders(params string[] columnHeaders)
	{
		Callable.From(() => InGameHUD.Instance?.SetSummaryTableHeaders(columnHeaders)).CallDeferred();
	}

	void IGameAPI.SetSummaryTableRow(string rowKey, params string[] cellValues)
	{
		Callable.From(() => InGameHUD.Instance?.SetSummaryTableRow(rowKey, cellValues)).CallDeferred();
	}

	void IGameAPI.ClearSummaryTable()
	{
		Callable.From(() => InGameHUD.Instance?.ClearSummaryTable()).CallDeferred();
	}

	string IGameAPI.GetPlayerLanguage(int playerIndex)
	{
		return LocalizationManager.GetCurrentLanguageCode();
	}

	string IGameAPI.Translate(string key, int playerIndex)
	{
		return LocalizationManager.TranslateKey(key);
	}

	void IGameAPI.TriggerMeshImpulse(UnitId unit, float strength, float duration, float frequency)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && TryGetUnit3D(entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
		{
			Callable.From(() => unit3D.TriggerImpulse(strength, duration, frequency)).CallDeferred();
		}
	}

	void IGameAPI.TriggerResourceMeshImpulse(ResourceNodeId node, float strength, float duration, float frequency)
	{
		if (TryGetResourceProp(node, out var prop) && GodotObject.IsInstanceValid(prop))
		{
			Callable.From(() => prop.TriggerImpulse(strength, duration, frequency)).CallDeferred();
		}
	}

	void IGameAPI.SetAbilityHotkey(string abilityId, string hotkey)
	{
		GameContext.RegistryService.SetAbilityHotkey(abilityId, hotkey);
	}

	void IGameAPI.SetAbilityState(UnitId unit, string abilityId, bool disabled, bool hidden)
	{
		GameContext.RegistryService.SetAbilityState(unit, abilityId, disabled, hidden);
	}

	public bool IsAbilityHiddenForUnit(UnitId unit, string abilityId)
	{
		return GameContext.RegistryService.IsAbilityHiddenForUnit(unit, abilityId);
	}

	public float GetAbilityManaCostForUnit(UnitId unit, string abilityId)
	{
		return GameContext.RegistryService.GetAbilityManaCostForUnit(unit, abilityId);
	}

	void IGameAPI.SetItemTooltip(string itemId, string tooltip)
	{
		GameContext.RegistryService.SetItemTooltip(itemId, tooltip);
	}

	int IGameAPI.GetPlayerTechLevel(int playerIndex, string techId)
	{
		return GameContext.TechTreeService.GetPlayerTechLevel(playerIndex, techId);
	}

	void IGameAPI.SetPlayerTechLevel(int playerIndex, string techId, int level)
	{
		GameContext.TechTreeService.SetPlayerTechLevel(playerIndex, techId, level);
	}

	void IGameAPI.AddPlayerTechLevel(int playerIndex, string techId, int delta)
	{
		GameContext.TechTreeService.AddPlayerTechLevel(playerIndex, techId, delta);
	}

	private UnitId SpawnUnitInternal(string unitTypeId, Vector3 position, int playerIndex, bool bypassPopulation, bool executeSpawnShader)
	{
		var pos = new Godot.Vector3(position.X, GetTerrainHeightAt(new Godot.Vector3(position.X, position.Y, position.Z)), position.Z);

		bool isBuilding = false;
		if (!UnitRegistry.TryGetValue(unitTypeId, out var meta) && !(isBuilding = BuildingRegistry.TryGetValue(unitTypeId, out meta)))
			throw new ArgumentException($"Unit ID '{unitTypeId}' not found in registry.");

		var playerOwner = GetPlayerEntityForPlayerIndex(playerIndex).AsPlayerEntity(GameContext.World);

		if (string.IsNullOrEmpty(meta.ModelPath)) throw new ArgumentException($"Unit ID '{unitTypeId}' has no assigned 3D model asset in registry.");
		string modelPath = GameContext.UnitSpawnService.GetFallbackModelPath(meta.ModelPath, isBuilding);
		string name = meta.Name;

		bool hasShields = GameContext.World?.GetFieldOrDefault<PlayerUpgrades, bool>(playerOwner.Value, u => u.ShieldsUpgrade) ?? false;
		bool hasWeapons = GameContext.World?.GetFieldOrDefault<PlayerUpgrades, bool>(playerOwner.Value, u => u.WeaponsUpgrade) ?? false;

		var entity = GameContext.UnitSpawnService.SpawnUnitDomain(unitTypeId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), playerOwner, bypassPopulation, playerOwner.Value, hasShields, hasWeapons);

		SpawnUnit3D(entity, unitTypeId, modelPath, pos, isBuilding, playerIndex, bypassPopulation, executeSpawnShader);
		return (UnitId)entity.Id;
	}

	void IGameAPI.SpawnResourceNode(string resourceType, Vector3 position, float amount)
	{
		var pos = new Godot.Vector3(position.X, position.Y, position.Z);
		var prop = SpawnPropExternal(resourceType, pos);
		if (prop != null)
		{
			prop.ResourceAmount = amount;
		}
	}

	ReadOnlySpan<UnitId> IGameAPI.GetAllUnits()
	{
		int count = 0;
		for (int i = 0; i < AllUnits.Count && count < _cachedUnitsBuffer.Length; i++)
		{
			var u = AllUnits[i];
			if (GodotObject.IsInstanceValid(u) && GameContext.World != null && GameContext.World.IsAlive(u.Entity))
			{
				_cachedUnitsBuffer[count++] = (UnitId)u.Entity.Id;
			}
		}

		return new ReadOnlySpan<UnitId>(_cachedUnitsBuffer, 0, count);
	}

	ReadOnlySpan<UnitId> IGameAPI.GetUnitsInRadius(Vector3 center, float radius)
	{
		int count = 0;
		float rSq = radius * radius;
		for (int i = 0; i < AllUnits.Count && count < _cachedUnitsBuffer.Length; i++)
		{
			var u = AllUnits[i];
			if (GodotObject.IsInstanceValid(u) && GameContext.World != null && GameContext.World.IsAlive(u.Entity))
			{
				float dx = u.GlobalPosition.X - center.X;
				float dy = u.GlobalPosition.Y - center.Y;
				float dz = u.GlobalPosition.Z - center.Z;
				if (dx * dx + dy * dy + dz * dz <= rSq)
				{
					_cachedUnitsBuffer[count++] = (UnitId)u.Entity.Id;
				}
			}
		}

		return new ReadOnlySpan<UnitId>(_cachedUnitsBuffer, 0, count);
	}

	int IGameAPI.ResourceNodeCount => GetCachedResourceNodes().Length;

	ResourceNodeId IGameAPI.GetResourceNode(int index)
	{
		var span = GetCachedResourceNodes();
		return (index >= 0 && index < span.Length) ? span[index] : ResourceNodeId.Invalid;
	}

	ReadOnlySpan<ResourceNodeId> IGameAPI.GetResourceNodes() => GetCachedResourceNodes();

	void IGameAPI.ShowFeedbackText(string text, Vector3 color)
	{
		GameContext.ClientRPC.BroadcastFeedbackText(text, color);
	}

	void IGameAPI.TriggerVictory()
	{
		GameContext.ClientRPC.BroadcastVictory();
	}

	void IGameAPI.TriggerDefeat()
	{
		GameContext.ClientRPC.BroadcastDefeat();
	}

	void IGameAPI.SpawnTargetIndicator(Vector3 position, Vector3 color)
	{
		SpawnTargetIndicator(new Godot.Vector3(position.X, position.Y, position.Z), new Color(color.X, color.Y, color.Z));
	}

	UnitId IGameAPI.GetUnitById(int uniqueId)
	{
		if (EntityIdToUnit3D.TryGetValue(uniqueId, out var u) && GodotObject.IsInstanceValid(u) && GameContext.World != null && GameContext.World.IsAlive(u.Entity))
		{
			return (UnitId)uniqueId;
		}

		return UnitId.Invalid;
	}

	void IGameAPI.GenerateMapDirectory(string mapName, string? targetDirectory)
	{
		string parentDir = string.IsNullOrEmpty(targetDirectory) ? "user://maps" : targetDirectory;
		string globalParentDir = ProjectSettings.GlobalizePath(parentDir);
		string mapDir = Path.Combine(globalParentDir, mapName);
		
		GameContext.EditorService.GenerateMapDirectoryDomain(mapName, mapDir, PathUtils.GetProjectRoot(), PathUtils.FindPath("MapTemplate"));
		
		EnsureMapProjectFiles(mapDir);
	}

	void IGameAPI.WriteSavedData(string fileName, string content)
	{
		string mapNameOnly = Path.GetFileNameWithoutExtension(ECSContext.ActiveMapName);
		GameContext.MapSaveDataService
			.WriteSavedData(mapNameOnly, fileName, content);
	}

	string IGameAPI.ReadSavedData(string fileName, string sourceMapName)
	{
		string targetMap = !string.IsNullOrWhiteSpace(sourceMapName) ? sourceMapName : ECSContext.ActiveMapName;
		string mapNameOnly = Path.GetFileNameWithoutExtension(targetMap);
		return GameContext.MapSaveDataService
			.ReadSavedData(mapNameOnly, fileName);
	}

	event Action<UnitId>? IGameAPI.OnUnitCreated
	{
		add => OnUnitCreated += value;
		remove => OnUnitCreated -= value;
	}

	event Action<UnitId, UnitId>? IGameAPI.OnUnitDied
	{
		add => OnUnitDied += value;
		remove => OnUnitDied -= value;
	}

	event Action<UnitId, UnitId, float>? IGameAPI.OnUnitDamaged
	{
		add => OnUnitDamaged += value;
		remove => OnUnitDamaged -= value;
	}

	event Action<UnitId, string, Vector3>? IGameAPI.OnSpellCast
	{
		add => OnSpellCast += value;
		remove => OnSpellCast -= value;
	}

	event Action<string, UnitId>? IGameAPI.OnPlayerChatMessage
	{
		add => OnPlayerChatMessage += value;
		remove => OnPlayerChatMessage -= value;
	}

	event Action<UnitId>? IGameAPI.OnUnitSelected
	{
		add => OnUnitSelected += value;
		remove => OnUnitSelected -= value;
	}

	event Action<UnitId, int>? IGameAPI.OnUnitEnterZone
	{
		add => OnUnitEnterZone += value;
		remove => OnUnitEnterZone -= value;
	}

	event Action<UnitId, UnitId>? IGameAPI.OnUnitAttacked
	{
		add => OnUnitAttacked += value;
		remove => OnUnitAttacked -= value;
	}

	event Action<LootItemId, UnitId>? IGameAPI.OnLootItemCollected
	{
		add => OnLootItemCollected += value;
		remove => OnLootItemCollected -= value;
	}

	void IGameAPI.SpawnBarrageVolley(string effectTypeId, Vector3 targetAreaCenter, float radius, int count, float intervalSeconds)
	{
		Callable.From(() =>
		{
			var center = new Godot.Vector3(targetAreaCenter.X, targetAreaCenter.Y, targetAreaCenter.Z);
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
					var impactPos = center + new Godot.Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
					((IGameAPI)this).SpawnVisualEffect(effectTypeId, new Vector3(impactPos.X, impactPos.Y, impactPos.Z), 1.0f);
				};
			}
		}).CallDeferred();
	}

	void IGameAPI.SpawnChainBeam(string effectTypeId, Vector3[] points, float jumpDelay, int forkCount, float fadeLifetime, float width, Vector3? color)
	{
		Callable.From(() =>
		{
			if (points == null || points.Length < 2) return;
			var godotPoints = new Godot.Vector3[points.Length];
			for (int i = 0; i < points.Length; i++)
			{
				godotPoints[i] = new Godot.Vector3(points[i].X, points[i].Y, points[i].Z);
			}

			Color? beamCol = color.HasValue ? new Color(color.Value.X, color.Value.Y, color.Value.Z) : null;
			ChainBeam3D.Create(this, godotPoints, jumpDelay, forkCount, fadeLifetime, width, beamCol);
		}).CallDeferred();
	}

	void IGameAPI.SpawnGroundShockwave(Vector3 position, Vector3 direction, float maxRadius, float speed, float duration, Vector3? color)
	{
		Callable.From(() =>
		{
			var pos = new Godot.Vector3(position.X, position.Y, position.Z);
			var dir = new Godot.Vector3(direction.X, direction.Y, direction.Z);
			Color? waveCol = color.HasValue ? new Color(color.Value.X, color.Value.Y, color.Value.Z) : null;
			var shock = GroundShockwave3D.Create(this, pos, ShockwaveType.PlanarWave, maxRadius, speed, duration, waveCol);
			shock.Direction = dir;
		}).CallDeferred();
	}

	void IGameAPI.SpawnExpandingGroundRing(Vector3 position, float maxRadius, float speed, float duration, Vector3? color)
	{
		Callable.From(() =>
		{
			var pos = new Godot.Vector3(position.X, position.Y, position.Z);
			Color? ringCol = color.HasValue ? new Color(color.Value.X, color.Value.Y, color.Value.Z) : null;
			GroundShockwave3D.Create(this, pos, ShockwaveType.ExpandingGroundRing, maxRadius, speed, duration, ringCol);
		}).CallDeferred();
	}

	void IGameAPI.DrawLineOnTerrain(System.Numerics.Vector2 start, System.Numerics.Vector2 end, Vector3? color, float duration, float width)
	{
		Callable.From(() =>
		{
			Color lineCol = color.HasValue ? new Color(color.Value.X, color.Value.Y, color.Value.Z) : Colors.White;
			TerrainLine3D.Create(this, start, end, lineCol, duration, width);
		}).CallDeferred();
	}

	void IGameAPI.SpawnExpandingBurstSphere(Vector3 position, float maxRadius, float speed, float duration, Vector3? color)
	{
		Callable.From(() =>
		{
			var pos = new Godot.Vector3(position.X, position.Y, position.Z);
			Color? sphereCol = color.HasValue ? new Color(color.Value.X, color.Value.Y, color.Value.Z) : null;
			GroundShockwave3D.Create(this, pos, ShockwaveType.ExpandingBurstSphere, maxRadius, speed, duration, sphereCol);
		}).CallDeferred();
	}

	void IGameAPI.AttachPersistentAura(UnitId unit, string auraVfxId, float scale)
	{
		Callable.From(() =>
		{
			if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			{
				if (TryGetUnit3D(entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
				{
					unit3D.SetSocketAttachment(HumanoidBone.Chest, auraVfxId, null, null, scale, null, null, false);
				}
			}
		}).CallDeferred();
	}

	public event Action<UnitId, string>? OnItemSold;

	event Action<UnitId, string>? IGameAPI.OnItemSold
	{
		add => OnItemSold += value;
		remove => OnItemSold -= value;
	}

	public event Action<UnitId>? OnConstructionFinished;

	event Action<UnitId>? IGameAPI.OnConstructionFinished
	{
		add => OnConstructionFinished += value;
		remove => OnConstructionFinished -= value;
	}

	public event Action<UnitId, string, Vector3>? OnUnitOrdered;

	event Action<UnitId, string, Vector3>? IGameAPI.OnUnitOrdered
	{
		add => OnUnitOrdered += value;
		remove => OnUnitOrdered -= value;
	}

	void IGameAPI.SetUnitFacing(UnitId unit, float facingRadians)
	{
		GameContext.SimulationService.SetUnitFacingDomain(unit, facingRadians);

		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			SetUnitFacingViewLogic(entity, facingRadians);
		}
	}

	private void SetUnitFacingViewLogic(Entity entity, float facingRadians)
	{
		if (TryGetUnit3D(entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
		{
			var rot = unit3D.Rotation;
			rot.Y = facingRadians;
			unit3D.Rotation = rot;
		}
	}

	void IGameAPI.CreateFloatingText(string text, Vector3 position, Vector3 color, float duration)
	{
		var godotPos = new Godot.Vector3(position.X, position.Y, position.Z);
		var godotCol = new Color(color.X, color.Y, color.Z);
		CreateFloatingTextInternal(text, godotPos, godotCol, duration);
		if (_multiplayerActive && IsServerActive())
		{
			Rpc(nameof(ClientCreateFloatingText), text, godotPos, new Godot.Vector3(color.X, color.Y, color.Z), duration);
		}
	}

	int IGameAPI.CreateStaticText(string text, Vector3 position, Vector3 color, int fontSize)
	{
		int handle = _nextStaticTextHandle++;
		var state = new StaticTextState
		{
			Text = text,
			Position = position,
			Color = color,
			FontSize = fontSize,
			Visible = true
		};
		_staticTextStates[handle] = state;
		_dirtyStaticTextHandles.Add(handle);
		ApplyStaticTextDelta(ToStaticTextDelta(handle, state, false));
		return handle;
	}

	void IGameAPI.SetStaticText(int handle, string text)
	{
		if (!_staticTextStates.TryGetValue(handle, out var state)) return;
		state.Text = text;
		_staticTextStates[handle] = state;
		_dirtyStaticTextHandles.Add(handle);
		ApplyStaticTextDelta(ToStaticTextDelta(handle, state, false));
	}

	void IGameAPI.SetStaticTextColor(int handle, Vector3 color)
	{
		if (!_staticTextStates.TryGetValue(handle, out var state)) return;
		state.Color = color;
		_staticTextStates[handle] = state;
		_dirtyStaticTextHandles.Add(handle);
		ApplyStaticTextDelta(ToStaticTextDelta(handle, state, false));
	}

	void IGameAPI.SetStaticTextVisible(int handle, bool visible)
	{
		if (!_staticTextStates.TryGetValue(handle, out var state)) return;
		state.Visible = visible;
		_staticTextStates[handle] = state;
		_dirtyStaticTextHandles.Add(handle);
		ApplyStaticTextDelta(ToStaticTextDelta(handle, state, false));
	}

	void IGameAPI.DestroyStaticText(int handle)
	{
		_staticTextStates.Remove(handle);
		_dirtyStaticTextHandles.Remove(handle);
		_destroyedStaticTextHandles.Add(handle);
		ApplyStaticTextDelta(ToStaticTextDelta(handle, default, true));
	}

	public void ApplyStaticTextDelta(StaticTextDelta delta)
	{
		if (delta.Destroyed != 0)
		{
			_dirtyStaticTextHandles.Remove(delta.Handle);
			int destroyGeneration = _staticTextGeneration;
			Callable.From(() =>
			{
				if (destroyGeneration != _staticTextGeneration) return;
				if (_staticTextLabels.Remove(delta.Handle, out var removedLabel))
				{
					removedLabel.QueueFree();
				}
			}).CallDeferred();
			return;
		}

		int generation = _staticTextGeneration;
		Callable.From(() =>
		{
			if (generation != _staticTextGeneration) return;
			if (!_staticTextLabels.TryGetValue(delta.Handle, out var label) || !GodotObject.IsInstanceValid(label))
			{
				label = new Label3D
				{
					Name = $"StaticText_{delta.Handle}",
					Billboard = BaseMaterial3D.BillboardModeEnum.Enabled
				};
				label.OutlineModulate = Colors.Black;
				AddChild(label);
				_staticTextLabels[delta.Handle] = label;
			}

			label.Text = delta.Text;
			label.Modulate = new Color(delta.ColorX, delta.ColorY, delta.ColorZ);
			label.FontSize = (int)delta.FontSize;
			label.Position = new Godot.Vector3(delta.PositionX, delta.PositionY + 1.5f, delta.PositionZ);
			label.Visible = delta.Visible != 0;
		}).CallDeferred();
	}

	public List<StaticTextDelta> CollectStaticTextDeltas(bool includeAll, bool clearDirty = true)
	{
		var deltas = new List<StaticTextDelta>();
		if (includeAll)
		{
			foreach (var kvp in _staticTextStates)
			{
				deltas.Add(ToStaticTextDelta(kvp.Key, kvp.Value, false));
			}
			if (clearDirty)
			{
				_dirtyStaticTextHandles.Clear();
				_destroyedStaticTextHandles.Clear();
			}
			return deltas;
		}

		foreach (int handle in _destroyedStaticTextHandles)
		{
			deltas.Add(ToStaticTextDelta(handle, default, true));
		}
		foreach (int handle in _dirtyStaticTextHandles)
		{
			if (_staticTextStates.TryGetValue(handle, out var state))
			{
				deltas.Add(ToStaticTextDelta(handle, state, false));
			}
		}
		if (clearDirty)
		{
			_dirtyStaticTextHandles.Clear();
			_destroyedStaticTextHandles.Clear();
		}
		return deltas;
	}

	private static StaticTextDelta ToStaticTextDelta(int handle, StaticTextState state, bool destroyed)
	{
		return new StaticTextDelta
		{
			Handle = handle,
			Text = state.Text ?? string.Empty,
			PositionX = state.Position.X,
			PositionY = state.Position.Y,
			PositionZ = state.Position.Z,
			ColorX = state.Color.X,
			ColorY = state.Color.Y,
			ColorZ = state.Color.Z,
			FontSize = state.FontSize,
			Visible = state.Visible ? (byte)1 : (byte)0,
			Destroyed = destroyed ? (byte)1 : (byte)0
		};
	}

	public List<AbilityStateDelta> CollectAbilityStateDeltas(bool includeAll, bool clearDirty = true)
	{
		var deltas = new List<AbilityStateDelta>();
		var registry = GameContext.RegistryService;
		if (registry == null) return deltas;

		if (includeAll)
		{
			foreach (var kvp in registry.UnitAbilityStates)
			{
				deltas.Add(ToAbilityStateDelta(kvp.Key.UnitUniqueId, kvp.Key.AbilityId, kvp.Value.Disabled, kvp.Value.Hidden));
			}
			if (clearDirty) registry.ClearAbilityStateDirty();
			return deltas;
		}

		foreach (var key in registry.DirtyAbilityStates)
		{
			if (registry.UnitAbilityStates.TryGetValue(key, out var state))
			{
				deltas.Add(ToAbilityStateDelta(key.UnitUniqueId, key.AbilityId, state.Disabled, state.Hidden));
			}
		}
		if (clearDirty) registry.ClearAbilityStateDirty();
		return deltas;
	}

	private static AbilityStateDelta ToAbilityStateDelta(int unitUniqueId, string abilityId, bool disabled, bool hidden)
	{
		return new AbilityStateDelta
		{
			ServerEntityId = unitUniqueId,
			AbilityId = abilityId,
			Flags = (byte)((disabled ? 1 : 0) | (hidden ? 2 : 0))
		};
	}

	void IGameAPI.SpawnVisualEffect(string effectTypeId, Vector3 position, float scale)
	{
		SpawnVisualEffectViewLogic(effectTypeId, position, scale);
	}

	private void SpawnVisualEffectViewLogic(string effectTypeId, Vector3 position, float scale)
	{
		Callable.From(() =>
		{
			var pos = new Godot.Vector3(position.X, position.Y, position.Z);
			var def = GetAbilityDefinition(effectTypeId);
			if (def != null)
			{
				GameContext.FXService.SpawnAbilityEffect(this, def, pos, scale);
			}
			else
			{
				GameContext.FXService.SpawnSpritesheetEffect(this, effectTypeId, pos + new Godot.Vector3(0, 0.5f, 0), 4, 4, 0.05f, scale * 6f);
			}
		}).CallDeferred();
	}

	void IGameAPI.AddBuff(UnitId unit, string buffId, float duration)
	{
		GameContext.SimulationService.AddBuffDomain(unit, buffId, duration);
	}

	void IGameAPI.RegisterBuffModifier(string buffId, string statName, bool isPercentage, float value)
	{
		GameContext.SimulationService.RegisterBuffModifierDomain(buffId, statName, isPercentage, value);
	}

	void IGameAPI.RegisterPlacementRadius(string definitionKey, float radius)
	{
		GameContext.RegistryService.RegisterPlacementRadius(definitionKey, radius);
	}

	void IGameAPI.RegisterResourceDropoffBuilding(string definitionKey)
	{
		GameContext.RegistryService.RegisterResourceDropoffBuilding(definitionKey);
	}

	void IGameAPI.RegisterResourceWorkerUnit(string unitKey)
	{
		GameContext.RegistryService.RegisterResourceWorkerUnit(unitKey);
	}

	void IGameAPI.RegisterPrimaryBuilding(string definitionId)
	{
		GameContext.RegistryService.RegisterPrimaryBuilding(definitionId);
	}

	void IGameAPI.RegisterResourceNodeType(string nodeDefinitionId, string resourceType)
	{
		GameContext.RegistryService.RegisterResourceNodeType(nodeDefinitionId, resourceType);
	}

	void IGameAPI.CastAbility(UnitId unit, string abilityId, Vector3 targetPosition)
	{
		var def = GetAbilityDefinition(abilityId);
		GameContext.SimulationService.CastAbility(
			unit, abilityId, targetPosition, def,
			(u, a, t) => OnSpellCast?.Invoke(u, a, t),
			(d, pos) => GameContext.FXService.SpawnAbilityEffect(this, d, pos)
		);
	}

	float IGameAPI.GetAbilityCooldown(UnitId unit, string abilityId)
	{
		return GameContext.SimulationService.GetAbilityCooldown(unit, abilityId);
	}

	void IGameAPI.SetAbilityCooldown(UnitId unit, string abilityId, float cooldown)
	{
		GameContext.SimulationService.SetAbilityCooldown(unit, abilityId, cooldown);
	}

	void IGameAPI.RemoveBuff(UnitId unit, string buffId)
	{
		GameContext.SimulationService.RemoveBuff(unit, buffId);
	}

	IEnumerable<string> IGameAPI.GetModifiers(UnitId unit)
	{
		return GameContext.SimulationService.GetModifiers(unit);
	}

	void IGameAPI.SpawnProjectile(string projectileTypeId, Vector3 start, Vector3 target, float speed)
	{
		GameContext.SimulationService.SpawnProjectile(
			projectileTypeId, start, target, speed,
			(tId, s, t, sp) =>
			{
				Callable.From(() =>
				{
					var startPos = new Godot.Vector3(s.X, s.Y, s.Z);
					var targetPos = new Godot.Vector3(t.X, t.Y, t.Z);
					SpawnWeaponProjectile(startPos, targetPos, tId);
				}).CallDeferred();
			}
		);
	}

	void IGameAPI.SetLeaderboardBoard(string boardId, string title, bool visible)
	{
		QueueLeaderboardOperation(new LeaderboardOperation
		{
			Kind = LeaderboardOperationKind.SetBoard,
			BoardId = boardId,
			Title = title,
			Visible = visible
		});
	}

	void IGameAPI.ClearLeaderboardBoard(string boardId)
	{
		QueueLeaderboardOperation(new LeaderboardOperation
		{
			Kind = LeaderboardOperationKind.ClearBoard,
			BoardId = boardId
		});
	}

	void IGameAPI.SetLeaderboardBoardGroup(string boardId, int groupPlayerIndex, string groupTitle)
	{
		QueueLeaderboardOperation(new LeaderboardOperation
		{
			Kind = LeaderboardOperationKind.SetGroup,
			BoardId = boardId,
			GroupPlayerIndex = groupPlayerIndex,
			GroupTitle = groupTitle
		});
	}

	void IGameAPI.AddLeaderboardBoardRow(string boardId, int groupPlayerIndex, string groupTitle, string label, string value)
	{
		QueueLeaderboardOperation(new LeaderboardOperation
		{
			Kind = LeaderboardOperationKind.AddRow,
			BoardId = boardId,
			GroupPlayerIndex = groupPlayerIndex,
			GroupTitle = groupTitle,
			Label = label,
			Value = value
		});
	}

	void IGameAPI.SetLeaderboardVisible(string title, bool visible)
	{
		((IGameAPI)this).SetLeaderboardBoard(DefaultLeaderboardBoardId, title, visible);
	}

	void IGameAPI.SetLeaderboardValue(string label, string value)
	{
		((IGameAPI)this).AddLeaderboardBoardRow(DefaultLeaderboardBoardId, -1, string.Empty, label, value);
	}

	void IGameAPI.ClearLeaderboard()
	{
		QueueLeaderboardOperation(new LeaderboardOperation { Kind = LeaderboardOperationKind.ClearAll });
	}

	private void QueueLeaderboardOperation(LeaderboardOperation operation)
	{
		_pendingLeaderboardOperations.Add(operation);
		QueueLeaderboardFlush();
	}

	private void QueueLeaderboardFlush()
	{
		if (_leaderboardFlushQueued) return;

		_leaderboardFlushQueued = true;
		Callable.From(FlushLeaderboardUpdates).CallDeferred();
	}

	private void FlushLeaderboardUpdates()
	{
		_leaderboardFlushQueued = false;

		var hud = InGameHUD.Instance;
		if (hud == null || !GodotObject.IsInstanceValid(hud))
		{
			_pendingLeaderboardOperations.Clear();
			return;
		}

		for (int i = 0; i < _pendingLeaderboardOperations.Count; i++)
		{
			LeaderboardOperation operation = _pendingLeaderboardOperations[i];
			switch (operation.Kind)
			{
				case LeaderboardOperationKind.ClearAll:
					hud.ClearLeaderboardBoards();
					break;
				case LeaderboardOperationKind.ClearBoard:
					hud.ClearLeaderboardBoard(operation.BoardId);
					break;
				case LeaderboardOperationKind.SetBoard:
					hud.SetLeaderboardBoard(operation.BoardId, operation.Title, operation.Visible);
					break;
				case LeaderboardOperationKind.SetGroup:
					hud.SetLeaderboardBoardGroup(operation.BoardId, operation.GroupPlayerIndex, operation.GroupTitle);
					break;
				case LeaderboardOperationKind.AddRow:
					hud.AddLeaderboardBoardRow(operation.BoardId, operation.GroupPlayerIndex, operation.GroupTitle, operation.Label, operation.Value);
					break;
			}
		}

		_pendingLeaderboardOperations.Clear();
	}

	void IGameAPI.StartCountdownTimer(float duration, string label)
	{
		GameContext.NetworkService.StartCountdownTimer(
			duration, label, _multiplayerActive, IsServerActive(),
			(d, l) => Callable.From(() => InGameHUD.Instance?.StartCountdownTimer(d, l)).CallDeferred(),
			(d, l) => Rpc(nameof(ClientStartCountdownTimer), d, l)
		);
	}

	private void StopCountdownTimerViewLogic()
	{
		Callable.From(() => InGameHUD.Instance?.StopCountdownTimer()).CallDeferred();
	}

	void IGameAPI.StopCountdownTimer()
	{
		StopCountdownTimerViewLogic();
		GameContext.NetworkService?.StopCountdownTimerDomain(_multiplayerActive && IsServerActive(), () => Rpc(nameof(ClientStopCountdownTimer)));
	}

	void IGameAPI.ShakeCamera(float intensity, float duration)
	{
		GameContext.ClientRPC.BroadcastShakeCamera(intensity, duration);
	}

	private void SetTimeOfDayViewLogic(float normalizedTime)
	{
		Callable.From(() => { UpdateDayNightVisuals(normalizedTime); }).CallDeferred();
	}

	void IGameAPI.SetTimeOfDay(float time)
	{
		float normalizedTime = GameContext.EnvironmentService.ProcessTimeOfDay(time);
		SetTimeOfDayViewLogic(normalizedTime);
	}

	private void SetDayNightCycleEnabledViewLogic(bool enabled)
	{
		DayNightCycleEnabled = enabled;
	}

	void IGameAPI.SetDayNightCycleEnabled(bool enabled)
	{
		SetDayNightCycleEnabledViewLogic(enabled);
	}

	private void SetEnvironmentPresetViewLogic(string presetId)
	{
		Callable.From(() => { GameContext.EnvironmentService?.ApplyPresetById(this, presetId); }).CallDeferred();
	}

	void IGameAPI.SetEnvironmentPreset(string presetId)
	{
		GameContext.NetworkService?.SetEnvironmentPreset(
			presetId, Multiplayer.MultiplayerPeer != null, Multiplayer.IsServer(),
			p => SetEnvironmentPresetViewLogic(p),
			p => Rpc(nameof(SyncEnvironmentPresetRpc), p, 0f)
		);
	}

	private void TransitionEnvironmentPresetViewLogic(string presetId, float durationSeconds)
	{
		Callable.From(() => { GameContext.EnvironmentService?.TransitionToPreset(this, presetId, durationSeconds); }).CallDeferred();
	}

	void IGameAPI.TransitionEnvironmentPreset(string presetId, float durationSeconds)
	{
		GameContext.NetworkService?.TransitionEnvironmentPreset(
			presetId, durationSeconds, Multiplayer.MultiplayerPeer != null, Multiplayer.IsServer(),
			(p, d) => TransitionEnvironmentPresetViewLogic(p, d),
			(p, d) => Rpc(nameof(SyncEnvironmentPresetRpc), p, d)
		);
	}

	string IGameAPI.GetCurrentEnvironmentPreset()
	{
		return GameContext.EnvironmentService?.GetCurrentEnvironmentPresetDomain() ?? "day";
	}

	private void SetWeatherViewLogic(string weatherType)
	{
		Callable.From(() =>
		{
			GameContext.EnvironmentService?.SetCurrentWeather(weatherType);
			InGameHUD.Instance?.ApplyWeatherEffects(weatherType);
		}).CallDeferred();
	}

	void IGameAPI.SetWeather(string weatherType)
	{
		GameContext.NetworkService?.SetWeather(
			weatherType, Multiplayer.MultiplayerPeer != null, Multiplayer.IsServer(),
			w => SetWeatherViewLogic(w),
			w => Rpc(nameof(SyncWeatherRpc), w)
		);
	}

	string IGameAPI.GetWeather()
	{
		return GameContext.EnvironmentService?.GetWeatherDomain() ?? "clear";
	}

	void IGameAPI.DisableShroud()
	{
		GameContext.SimulationService?.DisableShroud(() => GameContext.ShroudService?.TriggerImmediateUpdate()
		);
	}

	void IGameAPI.SetUnitAnimation(UnitId unit, string animationName)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			if (TryGetUnit3D(entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				unit3D.PlayAnimation(animationName);
			}
		}
	}

	void IGameAPI.SetUnitHandAttachment(UnitId unit, string hand, string? attachmentId)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			if (TryGetUnit3D(entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
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

	void IGameAPI.KillUnit(UnitId unit, bool executeDespawnShader, bool playDeathAnimation)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			if (TryGetUnit3D(entity, out var unit3D))
			{
				if (GodotObject.IsInstanceValid(unit3D))
				{
					GameContext.SimulationService?.DomainKillUnit(entity, () => { Callable.From(() => KillUnit(unit3D, executeDespawnShader, playDeathAnimation)).CallDeferred(); });
				}
			}
		}
	}

	void IGameAPI.DestroyUnit(UnitId unit, bool executeDespawnShader, bool playDeathAnimation)
	{
		if (!GameContext.SimulationService.TryGetEntity(unit, out var entity) || !GameContext.World.IsAlive(entity)) return;
		if (!TryGetUnit3D(entity, out var unit3D) || !GodotObject.IsInstanceValid(unit3D)) return;

		bool isBuilding = unit3D.IsBuilding;
		if (UnitRegistry.TryGetValue(unit3D.UnitId, out var destroyMeta) && destroyMeta.PopCost > 0)
		{
			int curPop = GameContext.ResourceEconomyService?.GetPlayerCurrentPopulation(unit3D.Player) ?? 0;
			GameContext.ResourceEconomyService?.SetPlayerCurrentPopulation(unit3D.Player, Math.Max(0, curPop - destroyMeta.PopCost));
		}
		float radius = GameContext.World.Has<CollisionRadius>(entity) ? GameContext.World.Get<CollisionRadius>(entity).Value : 2.0f;
		var unitPos = GameContext.World.Has<Position>(entity) ? GameContext.World.Get<Position>(entity).Value : new Vector3(unit3D.Position.X, unit3D.Position.Y, unit3D.Position.Z);

		RemoveUnitFromCollections(unit3D, entity);

		GameContext.SimulationService?.DomainDestroyUnit(entity, isBuilding, radius, unitPos, () =>
		{
			if (GodotObject.IsInstanceValid(unit3D))
			{

				if (playDeathAnimation) unit3D.PlayAnimation("Death");

				HandleUnitDespawn(unit3D, executeDespawnShader, playDeathAnimation);
			}
		});
	}

	private void RemoveUnitFromCollections(Unit3D unit3D, in Entity entity)
	{
		SelectedUnits.Remove(unit3D);
		AllUnits.Remove(unit3D);
		EntityToUnit3D.Remove(entity);
		EntityIdToUnit3D.Remove(entity.Id);
		if (PlayerToUnits.TryGetValue(unit3D.Player, out var pList))
		{
			pList.Remove(unit3D);
		}
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
			tween.TweenProperty(unit3D, "scale", Godot.Vector3.Zero, 1.0f);
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
			if (GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<ScriptPlayersState>(ECSContext.WorldEntity))
			{
				ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(ECSContext.WorldEntity);
				int count = 0;
				for (int playerIndex = 0; playerIndex < ScriptPlayersState.SlotCount; playerIndex++)
				{
					if (playersState.Get(playerIndex).Active) count++;
				}

				return Math.Max(1, count);
			}

			return 1;
		}
	}

	string IGameAPI.GetPlayerName(int playerIndex)
	{
		return GameContext.SimulationService.GetPlayerNameDomain(playerIndex);
	}

	string IGameAPI.GetPlayerColorHex(int playerIndex)
	{
		return PlayerColorConfig.GetColorHex(playerIndex);
	}

	Vector3 IGameAPI.GetPlayerColor(int playerIndex)
	{
		Color color = PlayerColorConfig.GetColorForOwner(playerIndex);
		return new Vector3(color.R, color.G, color.B);
	}



	void IGameAPI.SetPlayerGold(int playerIndex, float amount)
	{
		GameContext.SimulationService.SetPlayerGoldDomain(playerIndex, amount, SyncPlayerResourceEcs);
	}

	void IGameAPI.AdjustPlayerGold(int playerIndex, float delta)
	{
		float current = GameContext.SimulationService.GetPlayerGoldDomain(playerIndex);
		float newAmount = Math.Max(0f, current + delta);
		((IGameAPI)this).SetPlayerGold(playerIndex, newAmount);
	}

	UnitId IGameAPI.SpawnUnitForPlayer(string unitTypeId, Vector2 position, int playerIndex, bool executeSpawnShader, bool bypassPopulation)
	{
		var position3D = new Vector3(position.X, 0f, position.Y);
		return SpawnUnitInternal(unitTypeId, position3D, playerIndex, bypassPopulation, executeSpawnShader);
	}

	ReadOnlySpan<UnitId> IGameAPI.GetUnitsOwnedByPlayer(int playerIndex)
	{
		int count = 0;
		if (PlayerToUnits.TryGetValue(playerIndex, out var pList))
		{
			for (int i = 0; i < pList.Count && count < _cachedUnitsBuffer.Length; i++)
			{
				var u = pList[i];
				if (GodotObject.IsInstanceValid(u) && GameContext.World != null && GameContext.World.IsAlive(u.Entity))
				{
					_cachedUnitsBuffer[count++] = (UnitId)u.Entity.Id;
				}
			}
		}

		return new ReadOnlySpan<UnitId>(_cachedUnitsBuffer, 0, count);
	}

	void IGameAPI.TriggerPlayerDefeat(int playerIndex, string reason)
	{
		GameContext.ClientRPC.TriggerDefeat(playerIndex, reason);
	}

	void IGameAPI.TriggerPlayerVictory(int playerIndex)
	{
		GameContext.ClientRPC.TriggerVictory(playerIndex);
	}

	void IGameAPI.BroadcastMessage(string message)
	{
		GameContext.ClientRPC.BroadcastMessage(message);
	}

	void IGameAPI.SendMessageToPlayer(int playerIndex, string message)
	{
		GameContext.ClientRPC.SendMessage(playerIndex, message);
	}

	private event Action<int>? _onTimerExpired;

	event Action<int>? IGameAPI.OnTimerExpired
	{
		add => _onTimerExpired += value;
		remove => _onTimerExpired -= value;
	}

	int IGameAPI.ScheduleTimer(float delay)
	{
		int handle = GameContext.TimerService.NextTimerHandle++;
		GameContext.TimerService.ScheduledTimers[handle] = (delay, delay, false, () => _onTimerExpired?.Invoke(handle));
		return handle;
	}

	int IGameAPI.ScheduleRepeatingTimer(float interval)
	{
		int handle = GameContext.TimerService.NextTimerHandle++;
		GameContext.TimerService.ScheduledTimers[handle] = (interval, interval, true, () => _onTimerExpired?.Invoke(handle));
		return handle;
	}

	void IGameAPI.CancelTimer(int timerHandle)
	{
		GameContext.TimerService.ScheduledTimers.Remove(timerHandle);
	}

	int IGameAPI.RandomInt(int min, int max) => GameContext.RandomService.Rng.Next(min, max + 1);

	float IGameAPI.RandomFloat(float min, float max) => min + (float)GameContext.RandomService.Rng.NextDouble() * (max - min);

	Vector3 IGameAPI.GetPlayerStartLocation(int playerIndex)
	{
		return GameContext.SimulationService.GetPlayerStartLocation(playerIndex);
	}

	void IGameAPI.SetPlayerTeam(int playerIndex, int teamIndex)
	{
		GameContext.SimulationService.SetPlayerTeam(playerIndex, teamIndex);
	}


	void IGameAPI.SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied)
	{
		GameContext.SimulationService.SetPlayersAllied(playerIndex, otherPlayerIndex, allied);
	}


	bool IGameAPI.ArePlayersAllied(int playerIndex, int otherPlayerIndex)
	{
		return GameContext.SimulationService.ArePlayersAllied(playerIndex, otherPlayerIndex);
	}

	void IGameAPI.SetPlayerActive(int playerIndex, bool active)
	{
		GameContext.SimulationService.SetPlayerActiveDomain(playerIndex, active);
	}

	void IGameAPI.KickPlayer(int playerIndex, string reason)
	{
		if (playerIndex >= 0)
		{
			GameContext.SimulationService.SetPlayerActiveDomain(playerIndex, false);
			if (LobbyManager.Instance != null)
			{
				var p = LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
				if (p != null)
				{
					LobbyManager.Instance.BootPlayer(p.PeerId);
				}
			}
			OnPlayerLeft?.Invoke(playerIndex);
		}
	}

	bool IGameAPI.AddComputerPlayer(int playerIndex, string? name, int teamIndex)
	{
		if (playerIndex <= 0 || playerIndex >= 25) return false;
		string botName = string.IsNullOrWhiteSpace(name) ? $"Bot {playerIndex}" : name;
		int botTeam = teamIndex >= 0 ? teamIndex : playerIndex;
		UpdateScriptPlayerState(playerIndex, botName, active: true, isComputer: true);
		GameContext.SimulationService.SetPlayerTeam(playerIndex, botTeam);

		if (LobbyManager.Instance != null)
		{
			var existing = LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
			if (existing == null)
			{
				var botPlayer = new PlayerInfo
				{
					PeerId = -(playerIndex + 100),
					Slot = playerIndex,
					Name = botName,
					Color = PlayerColorConfig.GetColor(playerIndex),
					Faction = "AI",
					Team = $"Team {botTeam}",
					IsHost = false
				};
				LobbyManager.Instance.PlayerList.Add(botPlayer);
			}
			else
			{
				existing.Name = botName;
			}
		}

		return true;
	}

	public bool IsPlayerComputer(int playerIndex)
	{
		if (playerIndex == 0) return true;
		if (GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<ScriptPlayersState>(ECSContext.WorldEntity))
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(ECSContext.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).IsComputer;
		}
		if (LobbyManager.Instance != null)
		{
			var p = LobbyManager.Instance.PlayerList.Find(x => x.Slot == playerIndex);
			if (p != null)
			{
				return p.PeerId < 0;
			}
		}

		return false;
	}

	void IGameAPI.SetUnitColor(UnitId unit, Vector3 color)
	{
		SetUnitColor(unit, color);
	}

	public void SetUnitColor(UnitId unit, Vector3 color)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			if (TryGetUnit3D(entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
				unit3D.ApplyModelTint(new Color(color.X, color.Y, color.Z));
		}
	}

	void IGameAPI.IssueAttackMoveOrder(UnitId unit, Vector3 destination)
	{
		GameContext.SimulationService.IssueAttackMoveOrder(unit, destination, (u, c, d) => OnUnitOrdered?.Invoke(u, c, d));
	}

	void IGameAPI.IssueCastOrder(UnitId caster, string abilityId, UnitId target)
	{
		GameContext.SimulationService.IssueCastOrder(caster, abilityId, target, (u, c, d) => OnUnitOrdered?.Invoke(u, c, d));
	}

	void IGameAPI.IssueCastOrderAt(UnitId caster, string abilityId, Vector3 position)
	{
		GameContext.SimulationService.IssueCastOrderAt(caster, abilityId, position, OnUnitOrdered);
	}

	void IGameAPI.SetAbilityAutoCast(UnitId unit, string abilityId, bool active)
	{
		GameContext.SimulationService.SetAbilityAutoCast(unit, abilityId, active);
	}

	void IGameAPI.SetPlayerComputerControlled(int playerIndex, bool isProxy)
	{
		GameContext.SimulationService.SetPlayerComputerControlled(playerIndex, isProxy);
	}

	void IGameAPI.AddLeaderboardRow(string label, string value, Vector3? color)
	{
		((IGameAPI)this).AddLeaderboardBoardRow(DefaultLeaderboardBoardId, -1, string.Empty, label, value);
	}


	void IGameAPI.SetPlayerWood(int playerIndex, float amount)
	{
		GameContext.ResourceEconomyService.SetPlayerWood(playerIndex, amount);
	}

	void IGameAPI.AdjustPlayerWood(int playerIndex, float delta)
	{
		GameContext.ResourceEconomyService.AdjustPlayerWood(playerIndex, delta);
	}

	void IGameAPI.SetUnitOwner(UnitId unit, int playerIndex)
	{
		GameContext.SimulationService.SetUnitOwner(unit, playerIndex);
	}



	void IGameAPI.SetPlayerMaxPopulation(int playerIndex, int max)
	{
		GameContext.ResourceEconomyService.SetPlayerMaxPopulation(playerIndex, max);
	}

	void IGameAPI.SetCountdownTimerLabel(string label)
	{
		SetCountdownTimerLabelView(label);
	}

	private void SetCountdownTimerLabelView(string label)
	{
		InGameHUD.Instance?.UpdateCountdownLabel(label);
	}

	void IGameAPI.IssueAttackMoveOrderToPlayer(int playerIndex, Vector3 destination)
	{
		GameContext.SimulationService.IssueAttackMoveOrderToPlayer(destination, ((IGameAPI)this).GetUnitsOwnedByPlayer(playerIndex));
	}

	int IGameAPI.CountUnitsOwnedByPlayer(int playerIndex)
	{
		return GameContext.SimulationService.CountUnitsOwnedByPlayer(((IGameAPI)this).GetUnitsOwnedByPlayer(playerIndex));
	}

	int IGameAPI.DefineZone(float minX, float minZ, float maxX, float maxZ)
	{
		return GameContext.MapScriptService.DefineZone(minX, minZ, maxX, maxZ);
	}

	Vector3 IGameAPI.GetZoneCenter(int zoneHandle)
	{
		return GameContext.MapScriptService.GetZoneCenter(zoneHandle);
	}

	void IGameAPI.SetUnitRouteState(UnitId unit, int state)
	{
		GameContext.SimulationService.SetUnitRouteState(unit, state);
	}

	int IGameAPI.GetUnitRouteState(UnitId unit)
	{
		return GameContext.SimulationService.GetUnitRouteState(unit);
	}

	void IGameAPI.SetUnitLevel(UnitId unit, int level)
	{
		GameContext.SimulationService.SetUnitLevel(unit, level);
	}


	void IGameAPI.SetPlayerKills(int playerIndex, int kills)
	{
		GameContext.SimulationService.SetPlayerKillsDomain(playerIndex, kills);
	}

	void IGameAPI.IssueMoveOrder(UnitId unit, Vector3 destination)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
		{
			var wrapper = GameContext.SimulationService.GetUnitWrapper(entity);
			wrapper.MoveTo(destination);
			OnUnitOrdered?.Invoke(unit, "Move", destination);
		}
	}

	void IGameAPI.SetUnitSpellImmune(UnitId unit, bool immune)
	{
		GameContext.SimulationService.SetUnitSpellImmune(unit, immune);
	}


	void IGameAPI.SetPlayerStone(int playerIndex, float amount)
	{
		GameContext.ResourceEconomyService.SetPlayerStone(playerIndex, amount);
	}

	void IGameAPI.AdjustPlayerStone(int playerIndex, float delta)
	{
		GameContext.ResourceEconomyService.AdjustPlayerStone(playerIndex, delta);
	}

	void IGameAPI.PanPlayerCameraTo(int playerIndex, Vector3 position, float duration)
	{
		GameContext.ClientRPC.PanCameraTo(playerIndex, position, duration);
	}

	void IGameAPI.ShakePlayerCamera(int playerIndex, float intensity, float duration)
	{
		GameContext.ClientRPC.ShakeCamera(playerIndex, intensity, duration);
	}

	void IGameAPI.SetPlayerCameraBounds(int playerIndex, float minX, float minZ, float maxX, float maxZ)
	{
		GameContext.ClientRPC.SetPlayerCameraBounds(playerIndex, minX, minZ, maxX, maxZ);
	}

	void IGameAPI.ResetPlayerCameraBounds(int playerIndex)
	{
		GameContext.ClientRPC.ResetPlayerCameraBounds(playerIndex);
	}

	void IGameAPI.SetPlayerHudElementVisible(int playerIndex, HudElement element, bool visible)
	{
		GameContext.ClientRPC.SetPlayerHudElementVisible(playerIndex, element, visible);
	}

	void IGameAPI.SelectPlayerUnit(int playerIndex, UnitId unit)
	{
		GameContext.ClientRPC.SelectUnit(playerIndex, unit);
	}

	void IGameAPI.ClearPlayerSelection(int playerIndex)
	{
		GameContext.ClientRPC.ClearSelection(playerIndex);
	}

	ReadOnlySpan<UnitId> IGameAPI.GetPlayerSelectedUnits(int playerIndex)
	{
		int count = 0;
		if (GameContext.PerspectiveFilter.CanControl(playerIndex))
		{
			for (int i = 0; i < SelectedUnits.Count && count < _cachedSelectedUnitsBuffer.Length; i++)
			{
				var u = SelectedUnits[i];
				if (GodotObject.IsInstanceValid(u) && GameContext.World != null && GameContext.World.IsAlive(u.Entity))
				{
					_cachedSelectedUnitsBuffer[count++] = (UnitId)u.Entity.Id;
				}
			}
		}
		return new ReadOnlySpan<UnitId>(_cachedSelectedUnitsBuffer, 0, count);
	}

	void IGameAPI.ShowPlayerFeedbackText(int playerIndex, string text, Vector3 color)
	{
		GameContext.ClientRPC.ShowFeedbackText(playerIndex, text, color);
	}

	void IGameAPI.PingPlayerMinimap(int playerIndex, Vector3 position)
	{
		GameContext.ClientRPC.PingMinimap(playerIndex, position);
	}

	void IGameAPI.PlayPlayerWarningSound(int playerIndex)
	{
		GameContext.ClientRPC.PlayWarningSound(playerIndex);
	}

	void IGameAPI.PlayPlayerClickSound(int playerIndex)
	{
		GameContext.ClientRPC.PlayClickSound(playerIndex);
	}

	void IGameAPI.StartPlayerBuildingPlacement(int playerIndex, string unitTypeId)
	{
		GameContext.ClientRPC.StartBuildingPlacement(playerIndex, unitTypeId);
	}

	bool IGameAPI.HasCoordinate(string coordinateName)
	{
		return GameContext.EditorService.HasCoordinate(coordinateName);
	}

	Vector3 IGameAPI.GetCoordinateMin(string coordinateName)
	{
		return GameContext.EditorService.GetCoordinateMin(coordinateName);
	}

	Vector3 IGameAPI.GetCoordinateMax(string coordinateName)
	{
		return GameContext.EditorService.GetCoordinateMax(coordinateName);
	}

	bool IGameAPI.IsPositionInCoordinate(Vector3 position, string coordinateName)
	{
		return GameContext.EditorService.IsPositionInCoordinate(position, coordinateName);
	}

	void IGameAPI.AddUnitTypeAbility(string unitTypeId, string abilityId)
	{
		GameContext.RegistryService.AddUnitTypeAbility(unitTypeId, abilityId);
	}

	IReadOnlyList<string> IGameAPI.GetUnitTypeAbilities(string unitTypeId)
	{
		return GameContext.RegistryService.GetUnitTypeAbilities(unitTypeId);
	}

	string IGameAPI.GetUnitName(string unitTypeId)
	{
		return GameContext.RegistryService.GetUnitName(unitTypeId);
	}

	string IGameAPI.GetAbilityName(string abilityId)
	{
		return GameContext.RegistryService.GetAbilityName(abilityId);
	}

	void IGameAPI.EnableUnitAutoCombat(UnitId unit, bool enabled)
	{
		GameContext.SimulationService.EnableUnitAutoCombat(unit, enabled);
	}

	void IGameAPI.SetPlayerBotProfile(int playerIndex, string profileJson)
	{
		GameContext.SimulationService.SetPlayerBotProfileDomain(playerIndex, profileJson);
	}

	string IGameAPI.GetPlayerBotProfile(int playerIndex)
	{
		return GameContext.SimulationService.GetPlayerBotProfileDomain(playerIndex);
	}

	void IGameAPI.SetPlayerBotGenre(int playerIndex, string genreName, string? configJson)
	{
		GameContext.SimulationService.SetPlayerBotGenreDomain(playerIndex, genreName, configJson);
	}

	string IGameAPI.GetPlayerBotGenre(int playerIndex)
	{
		return GameContext.SimulationService.GetPlayerBotGenreDomain(playerIndex);
	}

	void IGameAPI.RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload)
	{
		GameContext.SimulationService.RegisterCustomBotDecisionDomain(playerIndex, actionId, intent, featureVector, position, payload);
	}

	void IGameAPI.ClearCustomBotDecisions(int playerIndex)
	{
		GameContext.SimulationService.ClearCustomBotDecisions(playerIndex);
	}

	event Action<int, string, string, Vector3, string>? IGameAPI.OnBotCustomActionExecuted
	{
		add => GameContext.SimulationService.OnBotCustomActionExecuted += value;
		remove => GameContext.SimulationService.OnBotCustomActionExecuted -= value;
	}

	string IGameAPI.TrainBotProfile(string mapName, int generations, int populationSize, int matchesPerEvaluation, string genre)
	{
		return GameContext.SimulationService.TrainBotProfile(mapName, generations, populationSize, matchesPerEvaluation, genre);
	}

	void IGameAPI.SetRegionPathing(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags, bool blocked)
	{
		SetRegionPathing(minX, minZ, maxX, maxZ, pathingFlags, blocked);
	}

	public void SetRegionPathing(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags, bool blocked)
	{
		ApplyRegionPathingInternal(minX, minZ, maxX, maxZ, (int)pathingFlags, blocked);
		if (IsMultiplayerServerActive())
		{
			Rpc(nameof(ClientUpdateRegionPathing), minX, minZ, maxX, maxZ, (int)pathingFlags, blocked);
		}
	}

	void IGameAPI.SetRegionPathing(string coordinateName, PathingFlags pathingFlags, bool blocked)
	{
		SetRegionPathing(coordinateName, pathingFlags, blocked);
	}

	public void SetRegionPathing(string coordinateName, PathingFlags pathingFlags, bool blocked)
	{
		if (((IGameAPI)this).TryGetCoordinate(coordinateName, out var coord))
		{
			SetRegionPathing(
				MathF.Min(coord.Min.X, coord.Max.X),
				MathF.Min(coord.Min.Z, coord.Max.Z),
				MathF.Max(coord.Min.X, coord.Max.X),
				MathF.Max(coord.Min.Z, coord.Max.Z),
				pathingFlags,
				blocked);
		}
	}

	PathingFlags IGameAPI.GetRegionPathing(float minX, float minZ, float maxX, float maxZ)
	{
		return GetRegionPathing(minX, minZ, maxX, maxZ);
	}

	public PathingFlags GetRegionPathing(float minX, float minZ, float maxX, float maxZ)
	{
		var world = GameContext.World;
		var worldEntity = ECSContext.WorldEntity;
		if (world != null && world.IsAlive(worldEntity) && world.Has<TerrainState>(worldEntity))
		{
			ref var state = ref world.Get<TerrainState>(worldEntity);
			int code = ECSContext.TerrainNavMeshService?.GetRegionPathing(in state, minX, minZ, maxX, maxZ) ?? (int)PathingFlags.All;
			return (PathingFlags)code;
		}
		return PathingFlags.All;
	}

	PathingFlags IGameAPI.GetRegionPathing(string coordinateName)
	{
		return GetRegionPathing(coordinateName);
	}

	public PathingFlags GetRegionPathing(string coordinateName)
	{
		if (((IGameAPI)this).TryGetCoordinate(coordinateName, out var coord))
		{
			return GetRegionPathing(
				MathF.Min(coord.Min.X, coord.Max.X),
				MathF.Min(coord.Min.Z, coord.Max.Z),
				MathF.Max(coord.Min.X, coord.Max.X),
				MathF.Max(coord.Min.Z, coord.Max.Z));
		}
		return PathingFlags.None;
	}
	ReadOnlySpan<PlayerId> IGameAPI.GetActivePlayers(int exclusiveUpperBound)
	{
		int count = 0;
		if (GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<ScriptPlayersState>(ECSContext.WorldEntity))
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(ECSContext.WorldEntity);
			int maxIndex = exclusiveUpperBound < 0 ? ScriptPlayersState.SlotCount : Math.Min(exclusiveUpperBound, ScriptPlayersState.SlotCount);
			for (int i = 0; i < maxIndex && count < _cachedPlayersBuffer.Length; i++)
			{
				if (playersState.Get(i).Active)
				{
					_cachedPlayersBuffer[count++] = (PlayerId)i;
				}
			}
		}
		return new ReadOnlySpan<PlayerId>(_cachedPlayersBuffer, 0, count);
	}

	LootItemId IGameAPI.SpawnLootItem(string itemTypeId, Vector3 position, int charges, float lifetime)
	{
		return SpawnLootItem(itemTypeId, position, charges, lifetime);
	}

	public LootItemId SpawnLootItem(string itemTypeId, Vector3 position, int charges = 1, float lifetime = 0f)
	{
		return SpawnLootItemInternal(itemTypeId, position, charges, lifetime, 2.0f, true, -1);
	}

	public LootItemId SpawnLootItemInternal(string itemTypeId, Vector3 position, int charges, float lifetime, float pickupRadius, bool autoPickup, int ownerPlayerIndex)
	{
		if (string.IsNullOrWhiteSpace(itemTypeId)) return LootItemId.Invalid;

		float terrainY = GetTerrainHeightAt(new Godot.Vector3(position.X, position.Y, position.Z));
		var finalPos = new Godot.Vector3(position.X, terrainY + 0.1f, position.Z);

		var entity = GameContext.World.Create(
			new Position(new Vector3(finalPos.X, finalPos.Y, finalPos.Z)),
			new GroundLoot(0, itemTypeId, Math.Max(1, charges), lifetime, pickupRadius, autoPickup, ownerPlayerIndex)
		);

		ref var lootComp = ref GameContext.World.Get<GroundLoot>(entity);
		lootComp.UniqueId = entity.Id;

		var lootNode = new LootItem3D();
		lootNode.Entity = entity;
		lootNode.Position = finalPos;
		lootNode.Initialize(entity.Id, itemTypeId, Math.Max(1, charges), lifetime, pickupRadius, autoPickup, ownerPlayerIndex);

		AddChild(lootNode);
		AllLootItems.Add(lootNode);

		var runtime = new LootItem_WasmRuntime(entity.Id, lootNode);
		IdToLootItem[entity.Id] = runtime;

		if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
		{
			Rpc(nameof(ClientSpawnLootItemRpc), entity.Id, itemTypeId, finalPos, Math.Max(1, charges));
		}

		return (LootItemId)entity.Id;
	}

	LootItemId IGameAPI.GetLootItemById(int uniqueId) => GetLootItemById(uniqueId);

	public LootItemId GetLootItemById(int uniqueId)
	{
		return IdToLootItem.TryGetValue(uniqueId, out var loot) && !loot.IsDespawned ? (LootItemId)uniqueId : LootItemId.Invalid;
	}

	ReadOnlySpan<LootItemId> IGameAPI.GetAllLootItems() => GetAllLootItems();

	public ReadOnlySpan<LootItemId> GetAllLootItems()
	{
		int count = 0;
		for (int i = 0; i < AllLootItems.Count && count < _cachedLootBuffer.Length; i++)
		{
			var node = AllLootItems[i];
			if (GodotObject.IsInstanceValid(node) && !node.IsDespawned && IdToLootItem.TryGetValue(node.UniqueId, out _))
			{
				_cachedLootBuffer[count++] = (LootItemId)node.UniqueId;
			}
		}
		return new ReadOnlySpan<LootItemId>(_cachedLootBuffer, 0, count);
	}

	ReadOnlySpan<LootItemId> IGameAPI.GetLootItemsInRadius(Vector3 center, float radius) => GetLootItemsInRadius(center, radius);

	public ReadOnlySpan<LootItemId> GetLootItemsInRadius(Vector3 center, float radius)
	{
		int count = 0;
		float rSq = radius * radius;
		for (int i = 0; i < AllLootItems.Count && count < _cachedLootBuffer.Length; i++)
		{
			var node = AllLootItems[i];
			if (GodotObject.IsInstanceValid(node) && !node.IsDespawned)
			{
				float dx = node.GlobalPosition.X - center.X;
				float dy = node.GlobalPosition.Y - center.Y;
				float dz = node.GlobalPosition.Z - center.Z;
				if (dx * dx + dy * dy + dz * dz <= rSq && IdToLootItem.TryGetValue(node.UniqueId, out _))
				{
					_cachedLootBuffer[count++] = (LootItemId)node.UniqueId;
				}
			}
		}
		return new ReadOnlySpan<LootItemId>(_cachedLootBuffer, 0, count);
	}

	bool IGameAPI.RemoveLootItem(int lootItemId) => RemoveLootItem(lootItemId);

	public bool RemoveLootItem(int lootItemId)
	{
		if (!IdToLootItem.ContainsKey(lootItemId)) return false;
		RemoveLootItemLocal(lootItemId);

		if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
		{
			Rpc(nameof(ClientDespawnLootItemRpc), lootItemId);
		}
		return true;
	}

	public void RemoveLootItemLocal(int lootItemId)
	{
		IdToLootItem.Remove(lootItemId);
		for (int i = AllLootItems.Count - 1; i >= 0; i--)
		{
			var node = AllLootItems[i];
			if (GodotObject.IsInstanceValid(node) && node.UniqueId == lootItemId)
			{
				node.IsDespawned = true;
				if (GameContext.World != null && GameContext.World.IsAlive(node.Entity))
				{
					GameContext.World.Destroy(node.Entity);
				}
				node.QueueFree();
				AllLootItems.RemoveAt(i);
				break;
			}
		}
	}

	bool IGameAPI.CollectLootItem(int lootItemId, UnitId unit) => CollectLootItem(lootItemId, unit);

	public bool CollectLootItem(int lootItemId, UnitId unit)
	{
		if (!unit.IsValid) return false;
		if (!IdToLootItem.TryGetValue(lootItemId, out var loot) || loot.IsDespawned) return false;

		if (!EntityIdToUnit3D.TryGetValue(unit.Value, out var u3d) || !GodotObject.IsInstanceValid(u3d)) return false;
		var unitEntity = u3d.Entity;
		if (!GameContext.World.IsAlive(unitEntity) || GameContext.World.Has<Dead>(unitEntity)) return false;

		var wrapper = GameContext.SimulationService.GetUnitWrapper(unitEntity);
		if (loot.OwnerPlayerIndex >= 0 && wrapper.Player != loot.OwnerPlayerIndex) return false;

		if (GameContext.World.Has<Inventory>(unitEntity))
		{
			var inv = GameContext.World.Get<Inventory>(unitEntity);
			if (inv.Count >= 6 && !inv.HasItem(loot.ItemTypeId))
				return false;
		}

		bool equipped = ItemGrantingUtility.EquipItem(GameContext.World, unitEntity, loot.ItemTypeId, loot.Charges);
		if (!equipped) return false;

		if (InGameHUD.Instance != null && SelectedUnits.Contains(u3d))
		{
			InGameHUD.Instance.RefreshUI(SelectedUnits);
		}

		OnLootItemCollected?.Invoke((LootItemId)lootItemId, unit);
		RemoveLootItem(lootItemId);
		return true;
	}

	bool IGameAPI.CollectLootItemByPlayer(int lootItemId, int playerIndex) => CollectLootItemByPlayer(lootItemId, playerIndex);

	public bool CollectLootItemByPlayer(int lootItemId, int playerIndex)
	{
		if (!IdToLootItem.TryGetValue(lootItemId, out var loot) || loot.IsDespawned) return false;

		var pos = loot.Position;
		UnitId bestUnit = UnitId.Invalid;
		float bestDistSq = float.MaxValue;

		for (int i = 0; i < AllUnits.Count; i++)
		{
			var u = AllUnits[i];
			if (!GodotObject.IsInstanceValid(u) || u.IsBuilding || u.Player != playerIndex) continue;
			if (!GameContext.World.IsAlive(u.Entity) || GameContext.World.Has<Dead>(u.Entity)) continue;

			if (GameContext.World.Has<Inventory>(u.Entity))
			{
				var inv = GameContext.World.Get<Inventory>(u.Entity);
				if (inv.Count >= 6 && !inv.HasItem(loot.ItemTypeId))
					continue;
			}

			float dx = u.Position.X - pos.X;
			float dy = u.Position.Y - pos.Y;
			float dz = u.Position.Z - pos.Z;
			float dsq = dx * dx + dy * dy + dz * dz;

			if (dsq < bestDistSq)
			{
				bestDistSq = dsq;
				bestUnit = (UnitId)u.Entity.Id;
			}
		}

		if (bestUnit.IsValid)
		{
			return CollectLootItem(lootItemId, bestUnit);
		}

		return false;
	}


	void IGameAPI.SetUnitHealth(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Health = value;
	}


	void IGameAPI.SetUnitMaxHealth(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).MaxHealth = value;
	}


	void IGameAPI.SetUnitMana(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Mana = value;
	}


	void IGameAPI.SetUnitMaxMana(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).MaxMana = value;
	}


	void IGameAPI.SetUnitManaRegen(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).ManaRegen = value;
	}


	void IGameAPI.SetUnitDamage(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Damage = value;
	}


	void IGameAPI.SetUnitRange(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Range = value;
	}


	void IGameAPI.SetUnitArmor(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Armor = value;
	}


	void IGameAPI.SetUnitSpeed(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Speed = value;
	}


	void IGameAPI.SetUnitAttackSpeed(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).AttackSpeed = value;
	}


	string IGameAPI.GetUnitInstanceName(UnitId unit)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).Name;
		return string.Empty;
	}

	void IGameAPI.SetUnitName(UnitId unit, string value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Name = value;
	}

	string IGameAPI.GetUnitTemplateId(UnitId unit)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).TemplateID;
		return string.Empty;
	}






	void IGameAPI.SetUnitExperience(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Experience = value;
	}





	void IGameAPI.SetUnitScale(UnitId unit, float value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Scale = value;
	}


	void IGameAPI.SetUnitInvulnerable(UnitId unit, bool value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Invulnerable = value;
	}

	void IGameAPI.SetUnitAnimationsFrozen(UnitId unit, bool value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).AnimationsFrozen = value;
	}

	void IGameAPI.SetUnitCommandsFrozen(UnitId unit, bool value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).CommandsFrozen = value;
	}

	void IGameAPI.SetUnitFrozen(UnitId unit, bool value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Frozen = value;
	}

	void IGameAPI.SetUnitHideHealthAndManaBars(UnitId unit, bool value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).HideHealthAndManaBars = value;
	}


	void IGameAPI.IssueAttackOrder(UnitId unit, UnitId target)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Attack(target);
	}

	void IGameAPI.IssueGatherOrder(UnitId unit, ResourceNodeId resourceNode)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Gather(resourceNode);
	}

	void IGameAPI.IssueStopOrder(UnitId unit)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Stop();
	}

	void IGameAPI.IssueHoldPositionOrder(UnitId unit)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).HoldPosition();
	}

	void IGameAPI.TeleportUnit(UnitId unit, Vector3 position)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Teleport(position);
	}

	void IGameAPI.StunUnit(UnitId unit, float duration)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Stun(duration);
	}

	void IGameAPI.SilenceUnit(UnitId unit, float duration)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).Silence(duration);
	}

	bool IGameAPI.AddItemToUnit(UnitId unit, string itemId)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).AddItem(itemId);
		return false;
	}

	bool IGameAPI.AddItemToUnitWithCharges(UnitId unit, string itemId, int charges)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).AddItem(itemId, Math.Max(1, charges));
		return false;
	}

	bool IGameAPI.RemoveItemFromUnit(UnitId unit, string itemId)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).RemoveItem(itemId);
		return false;
	}

	bool IGameAPI.UnitHasItem(UnitId unit, string itemId)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).HasItem(itemId);
		return false;
	}

	IReadOnlyList<string> IGameAPI.GetUnitItems(UnitId unit)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).GetItems();
		return Array.Empty<string>();
	}

	int IGameAPI.GetUnitItemCharges(UnitId unit, string itemId)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).GetItemCharges(itemId);
		return 0;
	}

	void IGameAPI.SetUnitItemCharges(UnitId unit, string itemId, int charges)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).SetItemCharges(itemId, charges);
	}

	bool IGameAPI.UnitHasBuff(UnitId unit, string buffId)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).HasBuff(buffId);
		return false;
	}

	void IGameAPI.SetUnitCustomData(UnitId unit, string key, string value)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			GameContext.SimulationService.GetUnitWrapper(entity).SetCustomData(key, value);
	}

	string? IGameAPI.GetUnitCustomData(UnitId unit, string key)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).GetCustomDataString(key);
		return null;
	}

	bool IGameAPI.RemoveUnitCustomData(UnitId unit, string key)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).RemoveCustomData(key);
		return false;
	}

	bool IGameAPI.UnitHasCustomData(UnitId unit, string key)
	{
		if (GameContext.SimulationService.TryGetEntity(unit, out var entity) && GameContext.World.IsAlive(entity))
			return GameContext.SimulationService.GetUnitWrapper(entity).HasCustomData(key);
		return false;
	}

	string IGameAPI.GetLootItemTypeId(LootItemId item)
	{
		return IdToLootItem.TryGetValue(item.Value, out var loot) ? loot.ItemTypeId : string.Empty;
	}

	Vector3 IGameAPI.GetLootItemPosition(LootItemId item)
	{
		return IdToLootItem.TryGetValue(item.Value, out var loot) ? loot.Position : Vector3.Zero;
	}

	int IGameAPI.GetLootItemCharges(LootItemId item)
	{
		return IdToLootItem.TryGetValue(item.Value, out var loot) ? loot.Charges : 0;
	}

	float IGameAPI.GetLootItemLifetime(LootItemId item)
	{
		return IdToLootItem.TryGetValue(item.Value, out var loot) ? loot.Lifetime : 0f;
	}

	float IGameAPI.GetLootItemPickupRadius(LootItemId item)
	{
		return IdToLootItem.TryGetValue(item.Value, out var loot) ? loot.PickupRadius : 0f;
	}

	bool IGameAPI.IsLootItemAutoPickup(LootItemId item)
	{
		return IdToLootItem.TryGetValue(item.Value, out var loot) && loot.AutoPickup;
	}

	int IGameAPI.GetLootItemOwner(LootItemId item)
	{
		return IdToLootItem.TryGetValue(item.Value, out var loot) ? loot.OwnerPlayerIndex : -1;
	}

	bool IGameAPI.IsLootItemDespawned(LootItemId item)
	{
		return !IdToLootItem.TryGetValue(item.Value, out var loot) || loot.IsDespawned;
	}

	string IGameAPI.GetResourceNodeType(ResourceNodeId node)
	{
		if (TryGetResourceProp(node, out var prop) && GodotObject.IsInstanceValid(prop))
		{
			return prop.PropId switch
			{
				"goldmine" => "gold",
				"tree" => "wood",
				"rock" => "stone",
				_ => prop.PropId ?? string.Empty
			};
		}
		return string.Empty;
	}

	Vector3 IGameAPI.GetResourceNodePosition(ResourceNodeId node)
	{
		if (TryGetResourceProp(node, out var prop) && GodotObject.IsInstanceValid(prop))
		{
			var pos = prop.GlobalPosition;
			return new Vector3(pos.X, pos.Y, pos.Z);
		}
		return Vector3.Zero;
	}

	float IGameAPI.GetResourceNodeAmount(ResourceNodeId node)
	{
		if (TryGetResourceProp(node, out var prop) && GodotObject.IsInstanceValid(prop))
		{
			return prop.ResourceAmount;
		}
		return 0f;
	}

	void IGameAPI.SetResourceNodeAmount(ResourceNodeId node, float value)
	{
		if (TryGetResourceProp(node, out var prop) && GodotObject.IsInstanceValid(prop))
		{
			prop.ResourceAmount = value;
		}
	}

	bool IGameAPI.IsResourceNodeDepleted(ResourceNodeId node)
	{
		if (TryGetResourceProp(node, out var prop) && GodotObject.IsInstanceValid(prop))
		{
			return prop.ResourceAmount <= 0f;
		}
		return true;
	}
}