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
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Ecs.Replication;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Realm.Shared.Abilities;

namespace Realm.Client.Core;

public partial class GameHost
{
	private readonly UnitId[] _cachedUnitsBuffer = new UnitId[4096];
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

	public bool IsAbilityHiddenForUnit(UnitId unit, string abilityId)
	{
		return GameContext.RegistryService.IsAbilityHiddenForUnit(unit, abilityId);
	}

	public float GetAbilityManaCostForUnit(UnitId unit, string abilityId)
	{
		return GameContext.RegistryService.GetAbilityManaCostForUnit(unit, abilityId);
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

	public UnitId SpawnAbilitySummon(string unitTypeId, Vector3 position, int playerIndex)
	{
		try
		{
			return SpawnUnitInternal(unitTypeId, position, playerIndex, bypassPopulation: true, executeSpawnShader: true);
		}
		catch (Exception exception)
		{
			GD.PushWarning($"[Ability] Summon '{unitTypeId}' could not spawn: {exception.Message}");
			return UnitId.Invalid;
		}
	}

	public event Action<UnitId, string>? OnItemSold;

	public event Action<UnitId>? OnConstructionFinished;

	public event Action<UnitId, string, Vector3>? OnUnitOrdered;

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
					Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
					NoDepthTest = true
				};
				label.OutlineModulate = Colors.Black;
				AddChild(label);
				_staticTextLabels[delta.Handle] = label;
			}

			label.Text = delta.Text;
			label.Modulate = new Color(delta.ColorX, delta.ColorY, delta.ColorZ);
			label.FontSize = (int)delta.FontSize;
			label.Position = new Godot.Vector3(delta.PositionX, delta.PositionY + 3.5f, delta.PositionZ);
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

	private readonly Dictionary<int, int> _abilityStateHandles = new();
	private readonly Dictionary<int, int> _publishedAbilityStateHandles = new();
	private readonly HashSet<(int UnitUniqueId, short AbilityIndex)> _abilityDeltaKeys = new();
	private bool _abilityStateHandlesInitialized;

	public List<AbilityStateDelta> CollectAbilityStateDeltas(bool includeAll, bool clearDirty = true)
	{
		var deltas = new List<AbilityStateDelta>();
		var registry = GameContext.RegistryService;
		if (registry == null) return deltas;

		CollectAbilityStateHandles();
		bool abilityListsChanged = _abilityStateHandlesInitialized && HasAnyAbilityListChanged();
		_abilityStateHandlesInitialized = true;

		_abilityDeltaKeys.Clear();
		if (includeAll || abilityListsChanged)
		{
			foreach (var kvp in registry.UnitAbilityStates)
			{
				AppendAbilityStateDelta(deltas, kvp.Key.UnitUniqueId, kvp.Key.AbilityId, kvp.Value.Disabled, kvp.Value.Hidden, exceptionsOnly: true, warnOnMiss: false);
			}
		}
		else
		{
			foreach (var key in registry.DirtyAbilityStates)
			{
				if (registry.UnitAbilityStates.TryGetValue(key, out var state))
				{
					AppendAbilityStateDelta(deltas, key.UnitUniqueId, key.AbilityId, state.Disabled, state.Hidden, exceptionsOnly: false, warnOnMiss: true);
				}
			}
		}

		_publishedAbilityStateHandles.Clear();
		foreach (var kvp in _abilityStateHandles)
		{
			_publishedAbilityStateHandles[kvp.Key] = kvp.Value;
		}

		if (clearDirty) registry.ClearAbilityStateDirty();
		return deltas;
	}

	private void CollectAbilityStateHandles()
	{
		_abilityStateHandles.Clear();
		foreach (var entity in GameContext.World.Query<AbilityState>())
		{
			ref var abilityState = ref GameContext.World.Get<AbilityState>(entity);
			_abilityStateHandles[entity.Id] = abilityState.Handle;
		}
	}

	private bool HasAnyAbilityListChanged()
	{
		if (_abilityStateHandles.Count != _publishedAbilityStateHandles.Count) return true;

		foreach (var kvp in _abilityStateHandles)
		{
			if (!_publishedAbilityStateHandles.TryGetValue(kvp.Key, out int publishedHandle)) return true;
			if (publishedHandle != kvp.Value) return true;
		}

		return false;
	}

	private void AppendAbilityStateDelta(List<AbilityStateDelta> deltas, int unitUniqueId, string abilityId, bool disabled, bool hidden, bool exceptionsOnly, bool warnOnMiss)
	{
		byte flags = (byte)((disabled ? 1 : 0) | (hidden ? 2 : 0));
		if (exceptionsOnly && flags == 0) return;

		if (!TryResolveAbilityIndex(unitUniqueId, abilityId, out short abilityIndex))
		{
			if (warnOnMiss)
			{
				GD.PushWarning($"[Replication] Ability state for entity {unitUniqueId} names '{abilityId}', which is not in that unit's ability list, so no peer can apply it.");
			}

			return;
		}

		if (!_abilityDeltaKeys.Add((unitUniqueId, abilityIndex))) return;

		deltas.Add(new AbilityStateDelta
		{
			ServerEntityId = unitUniqueId,
			AbilityIndex = abilityIndex,
			Flags = flags
		});
	}

	private bool TryResolveAbilityIndex(int unitUniqueId, string abilityId, out short abilityIndex)
	{
		abilityIndex = -1;
		Entity entity = unitUniqueId;
		if (!GameContext.World.IsAlive(entity) || !GameContext.World.Has<AbilityState>(entity)) return false;

		var abilityState = GameContext.World.Get<AbilityState>(entity);
		for (int index = 0; index < abilityState.Count; index++)
		{
			string? listedAbilityId = abilityState.Get(index);
			if (listedAbilityId == null) continue;
			if (!IsSameAbilityId(listedAbilityId, abilityId)) continue;

			abilityIndex = (short)index;
			return true;
		}

		return false;
	}

	private static bool IsSameAbilityId(string left, string right)
	{
		return AbilityIdCore(left).Equals(AbilityIdCore(right), StringComparison.OrdinalIgnoreCase);
	}

	private static ReadOnlySpan<char> AbilityIdCore(string abilityId)
	{
		ReadOnlySpan<char> span = abilityId.AsSpan();
		return span.StartsWith("ability/", StringComparison.OrdinalIgnoreCase) ? span.Slice(8) : span;
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

		var hud = GameContext.InGameHud;
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

	private void SetTimeOfDayViewLogic(float normalizedTime)
	{
		Callable.From(() => { UpdateDayNightVisuals(normalizedTime); }).CallDeferred();
	}

	private void SetDayNightCycleEnabledViewLogic(bool enabled)
	{
		DayNightCycleEnabled = enabled;
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

	private event Action<int>? _onTimerExpired;

	public bool IsPlayerComputer(int playerIndex)
	{
		if (playerIndex == 0) return true;
		if (GameContext.World != null && GameContext.World.IsAlive(GameContext.Simulation.WorldEntity) && GameContext.World.Has<ScriptPlayersState>(GameContext.Simulation.WorldEntity))
		{
			ref var playersState = ref GameContext.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).IsComputer;
		}
		if (GameContext.Lobby != null)
		{
			var p = GameContext.Lobby.PlayerList.Find(x => x.Slot == playerIndex);
			if (p != null)
			{
				return p.PeerId < 0;
			}
		}

		return false;
	}

	private void SetCountdownTimerLabelView(string label)
	{
		GameContext.InGameHud?.UpdateCountdownLabel(label);
	}

	public void RemoveLootItemLocal(int lootItemId)
	{
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

	public void SyncReplicatedLootNode(int lootItemId, Entity localEntity)
	{
		if (GameContext.World == null || !GameContext.World.IsAlive(localEntity)) return;
		if (!GameContext.World.Has<GroundLoot>(localEntity)) return;

		var loot = GameContext.World.Get<GroundLoot>(localEntity);
		float x = 0f;
		float y = 0f;
		float z = 0f;
		if (GameContext.World.Has<Position>(localEntity))
		{
			var position = GameContext.World.Get<Position>(localEntity).Value;
			x = position.X;
			y = position.Y;
			z = position.Z;
		}

		for (int i = 0; i < AllLootItems.Count; i++)
		{
			var existing = AllLootItems[i];
			if (!GodotObject.IsInstanceValid(existing) || existing.UniqueId != lootItemId) continue;

			existing.Entity = localEntity;
			existing.Charges = loot.Charges;
			existing.Lifetime = loot.Lifetime;
			existing.RemainingLifetime = loot.RemainingLifetime;
			existing.PickupRadius = loot.PickupRadius;
			existing.AutoPickup = loot.AutoPickup;
			existing.OwnerPlayerIndex = loot.OwnerPlayerIndex;
			existing.Position = new Godot.Vector3(x, y, z);
			return;
		}

		var lootNode = new LootItem3D();
		lootNode.Entity = localEntity;
		lootNode.Position = new Godot.Vector3(x, y, z);
		lootNode.Initialize(lootItemId, loot.ItemTypeId, loot.Charges, loot.Lifetime, loot.PickupRadius, loot.AutoPickup, loot.OwnerPlayerIndex);

		AddChild(lootNode);
		AllLootItems.Add(lootNode);
	}

}