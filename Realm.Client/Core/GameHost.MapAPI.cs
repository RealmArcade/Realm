using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
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
using Realm.Ecs.Services;
using Realm.MapAPI;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;

public partial class GameHost
{
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

	void IGameAPI.RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (GameContext.RegistryService.RegisterAbility(abilityId, displayName, tooltip, iconPath, isInstant))
		{
			RegisterAbilityView(abilityId, displayName, tooltip, iconPath, isInstant);
		}
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

	void IGameAPI.SetAbilityManaCost(IUnit unit, string abilityId, float manaCost)
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
		GameContext.RegistryService.SetAbilityHotkey(abilityId, hotkey);
	}

	void IGameAPI.SetAbilityState(IUnit unit, string abilityId, bool disabled, bool hidden)
	{
		GameContext.RegistryService.SetAbilityState(unit, abilityId, disabled, hidden);
	}

	public bool IsAbilityHiddenForUnit(IUnit unit, string abilityId)
	{
		return GameContext.RegistryService.IsAbilityHiddenForUnit(unit, abilityId);
	}

	public float GetAbilityManaCostForUnit(IUnit unit, string abilityId)
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

	private IUnit SpawnUnitInternal(string unitTypeId, Vector3 position, int playerIndex, bool bypassPopulation, bool executeSpawnShader)
	{
		var pos = new Godot.Vector3(position.X, GetTerrainHeightAt(new Godot.Vector3(position.X, position.Y, position.Z)), position.Z);

		bool isBuilding = false;
		if (!UnitRegistry.TryGetValue(unitTypeId, out var meta) && !(isBuilding = BuildingRegistry.TryGetValue(unitTypeId, out meta)))
			throw new ArgumentException($"Unit ID '{unitTypeId}' not found in registry.");

		int localPlayerIndex = LobbyManager.Instance?.LocalPlayer?.Slot ?? 1;
		bool actualIsEnemy = NetworkService.ArePlayerIndicesEnemies(localPlayerIndex, playerIndex);
		var playerOwner = GetPlayerEntityForPlayerIndex(playerIndex).AsPlayerEntity(GameContext.World);

		if (string.IsNullOrEmpty(meta.ModelPath)) throw new ArgumentException($"Unit ID '{unitTypeId}' has no assigned 3D model asset in registry.");
		string modelPath = GameContext.UnitSpawnService.GetFallbackModelPath(meta.ModelPath, isBuilding);
		string name = actualIsEnemy ? GameContext.UnitSpawnService.GetEnemyUnitName(unitTypeId, meta.Name) : meta.Name;

		var entity = GameContext.UnitSpawnService.SpawnUnitDomain(unitTypeId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, new System.Numerics.Vector3(pos.X, pos.Y, pos.Z), playerOwner, bypassPopulation, playerOwner.Value, HasShieldsUpgrade, HasWeaponsUpgrade);

		SpawnUnit3D(entity, unitTypeId, modelPath, pos, isBuilding, actualIsEnemy, bypassPopulation, playerIndex, executeSpawnShader);
		return GameContext.SimulationService.GetUnitWrapper(entity);
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

	IEnumerable<IUnit> IGameAPI.GetAllUnits()
	{
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && GameContext.World.IsAlive(u.Entity))
			{
				list.Add(GameContext.SimulationService.GetUnitWrapper(u.Entity));
			}
		}

		return list;
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsInRadius(Vector3 center, float radius)
	{
		var list = new List<IUnit>();
		var godotCenter = new Godot.Vector3(center.X, center.Y, center.Z);
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && GameContext.World.IsAlive(u.Entity) && u.GlobalPosition.DistanceTo(godotCenter) <= radius)
			{
				list.Add(GameContext.SimulationService.GetUnitWrapper(u.Entity));
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

	IUnit? IGameAPI.GetUnitById(int uniqueId)
	{
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u) && u.Entity.Id == uniqueId && GameContext.World.IsAlive(u.Entity))
			{
				return GameContext.SimulationService.GetUnitWrapper(u.Entity);
			}
		}

		return null;
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

	event Action<IUnit?, string, Vector3>? IGameAPI.OnSpellCast
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

	void IGameAPI.AttachPersistentAura(IUnit unit, string auraVfxId, float scale)
	{
		Callable.From(() =>
		{
			if (unit is IEcsEntityWrapper wrapper && GameContext.World.IsAlive(wrapper.Entity))
			{
				if (TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
				{
					unit3D.SetSocketAttachment(HumanoidBone.Chest, auraVfxId, null, null, scale, null, null, false);
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

	public event Action<IUnit, string, Vector3>? OnUnitOrdered;

	event Action<IUnit, string, Vector3>? IGameAPI.OnUnitOrdered
	{
		add => OnUnitOrdered += value;
		remove => OnUnitOrdered -= value;
	}

	void IGameAPI.SetUnitFacing(IUnit unit, float facingRadians)
	{
		GameContext.SimulationService.SetUnitFacingDomain(unit, facingRadians);

		if (unit is IEcsEntityWrapper wrapper && GameContext.World.IsAlive(wrapper.Entity))
		{
			SetUnitFacingViewLogic(wrapper.Entity, facingRadians);
		}
	}

	private void SetUnitFacingViewLogic(Arch.Core.Entity entity, float facingRadians)
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
		int gen = _staticTextGeneration;
		Callable.From(() =>
		{
			if (gen != _staticTextGeneration) return;
			var label = new Label3D();
			label.Text = text;
			label.Modulate = new Color(color.X, color.Y, color.Z);
			label.OutlineModulate = Colors.Black;
			label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
			label.Position = new Godot.Vector3(position.X, position.Y + 1.5f, position.Z);
			label.FontSize = fontSize;
			AddChild(label);
			_staticTextLabels[handle] = label;
		}).CallDeferred();
		return handle;
	}

	void IGameAPI.SetStaticText(int handle, string text)
	{
		SetStaticTextViewLogic(handle, text);
	}

	private void SetStaticTextViewLogic(int handle, string text)
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
		SetStaticTextVisibleViewLogic(handle, visible);
	}

	private void SetStaticTextVisibleViewLogic(int handle, bool visible)
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
		DestroyStaticTextViewLogic(handle);
	}

	private void DestroyStaticTextViewLogic(int handle)
	{
		Callable.From(() =>
		{
			if (_staticTextLabels.Remove(handle, out var label))
			{
				label.QueueFree();
			}
		}).CallDeferred();
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

	void IGameAPI.AddBuff(IUnit unit, string buffId, float duration)
	{
		GameContext.SimulationService.AddBuffDomain(unit, buffId, duration);
	}

	void IGameAPI.RegisterBuffModifier(string buffId, string statName, bool isPercentage, float value)
	{
		GameContext.SimulationService.RegisterBuffModifierDomain(buffId, statName, isPercentage, value);
	}

	void IGameAPI.CastAbility(IUnit unit, string abilityId, Vector3 targetPosition)
	{
		var def = GetAbilityDefinition(abilityId);
		GameContext.SimulationService.CastAbility(
			unit, abilityId, targetPosition, def,
			(u, a, t) => OnSpellCast?.Invoke(u, a, t),
			(d, pos) => GameContext.FXService.SpawnAbilityEffect(this, d, pos)
		);
	}

	float IGameAPI.GetAbilityCooldown(IUnit unit, string abilityId)
	{
		return GameContext.SimulationService.GetAbilityCooldown(unit, abilityId);
	}

	void IGameAPI.SetAbilityCooldown(IUnit unit, string abilityId, float cooldown)
	{
		GameContext.SimulationService.SetAbilityCooldown(unit, abilityId, cooldown);
	}

	void IGameAPI.RemoveBuff(IUnit unit, string buffId)
	{
		GameContext.SimulationService.RemoveBuff(unit, buffId);
	}

	IEnumerable<string> IGameAPI.GetModifiers(IUnit unit)
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

	void IGameAPI.SetLeaderboardVisible(string title, bool visible)
	{
		Callable.From(() => InGameHUD.Instance?.SetLeaderboardVisible(title, visible)).CallDeferred();
	}

	void IGameAPI.SetLeaderboardValue(string label, string value)
	{
		Callable.From(() => InGameHUD.Instance?.SetLeaderboardValue(label, value)).CallDeferred();
	}

	void IGameAPI.ClearLeaderboard()
	{
		Callable.From(() => InGameHUD.Instance?.ClearLeaderboard()).CallDeferred();
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

	void IGameAPI.SetUnitAnimation(IUnit unit, string animationName)
	{
		if (unit is IEcsEntityWrapper wrapper && GameContext.World.IsAlive(wrapper.Entity))
		{
			if (TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				unit3D.PlayAnimation(animationName);
			}
		}
	}

	void IGameAPI.SetUnitHandAttachment(IUnit unit, string hand, string? attachmentId)
	{
		if (unit is IEcsEntityWrapper wrapper && GameContext.World.IsAlive(wrapper.Entity))
		{
			if (TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
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
		if (unit is IEcsEntityWrapper wrapper && GameContext.World.IsAlive(wrapper.Entity))
		{
			if (TryGetUnit3D(wrapper.Entity, out var unit3D))
			{
				if (GodotObject.IsInstanceValid(unit3D))
				{
					GameContext.SimulationService?.DomainKillUnit(wrapper.Entity, () => { Callable.From(() => KillUnit(unit3D, executeDespawnShader, playDeathAnimation)).CallDeferred(); });
				}
			}
		}
	}

	void IGameAPI.DestroyUnit(IUnit unit, bool executeDespawnShader, bool playDeathAnimation)
	{
		if (unit is not IEcsEntityWrapper wrapper || !GameContext.World.IsAlive(wrapper.Entity)) return;
		if (!TryGetUnit3D(wrapper.Entity, out var unit3D) || !GodotObject.IsInstanceValid(unit3D)) return;

		bool isBuilding = unit3D.IsBuilding;
		if (!unit3D.IsEnemy && UnitRegistry.TryGetValue(unit3D.UnitId, out var destroyMeta) && destroyMeta.PopCost > 0)
		{
			CurrentPopulation = Math.Max(0, CurrentPopulation - destroyMeta.PopCost);
		}
		float radius = GameContext.World.Has<CollisionRadius>(wrapper.Entity) ? GameContext.World.Get<CollisionRadius>(wrapper.Entity).Value : 2.0f;
		var unitPos = GameContext.World.Has<Position>(wrapper.Entity) ? GameContext.World.Get<Position>(wrapper.Entity).Value : new Vector3(unit3D.Position.X, unit3D.Position.Y, unit3D.Position.Z);

		RemoveUnitFromCollections(unit3D, wrapper.Entity);

		GameContext.SimulationService?.DomainDestroyUnit(wrapper.Entity, isBuilding, radius, unitPos, () =>
		{
			if (GodotObject.IsInstanceValid(unit3D))
			{
				unit3D.CollisionLayer = 0;
				unit3D.CollisionMask = 0;

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
		if (unit3D.UnitId == "castle")
		{
			_castlesList.Remove(unit3D);
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
			if (GameContext.World?.TryGet<ScriptPlayersState>(ECSContext.WorldEntity, out var playersState) == true)
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
		return GameContext.SimulationService.GetPlayerNameDomain(playerIndex);
	}

	string IGameAPI.GetPlayerColorHex(int playerIndex)
	{
		return PlayerColorConfig.GetColorHex(playerIndex);
	}

	bool IGameAPI.IsPlayerActive(int playerIndex)
	{
		return GameContext.SimulationService.IsPlayerActiveDomain(playerIndex);
	}

	float IGameAPI.GetPlayerGold(int playerIndex)
	{
		return GameContext.SimulationService.GetPlayerGoldDomain(playerIndex);
	}

	void IGameAPI.SetPlayerGold(int playerIndex, float amount)
	{
		GameContext.SimulationService.SetPlayerGoldDomain(playerIndex, amount, SyncPlayerResourceEcs);
	}

	void IGameAPI.AdjustPlayerGold(int playerIndex, float delta)
	{
		float current = ((IGameAPI)this).GetPlayerGold(playerIndex);
		float newAmount = Math.Max(0f, current + delta);
		((IGameAPI)this).SetPlayerGold(playerIndex, newAmount);
	}

	IUnit IGameAPI.SpawnUnitForPlayer(string unitTypeId, Vector2 position, int playerIndex, bool executeSpawnShader, bool bypassPopulation)
	{
		var position3D = new Vector3(position.X, 0f, position.Y);
		return SpawnUnitInternal(unitTypeId, position3D, playerIndex, bypassPopulation, executeSpawnShader);
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

	int IGameAPI.GetPlayerTeam(int playerIndex)
	{
		return GameContext.SimulationService.GetPlayerTeam(playerIndex);
	}

	void IGameAPI.SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied)
	{
		GameContext.SimulationService.SetPlayersAllied(playerIndex, otherPlayerIndex, allied);
	}

	bool IGameAPI.IsPlayerComputer(int playerIndex)
	{
		return IsPlayerComputer(playerIndex);
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
		if (GameContext.World?.TryGet<ScriptPlayersState>(ECSContext.WorldEntity, out var playersState) == true)
		{
			if (playerIndex >= 0 && playerIndex < playersState.Players.Length)
			{
				return playersState.Players[playerIndex].IsComputer;
			}
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

	void IGameAPI.SetUnitColor(IUnit unit, Vector3 color)
	{
		SetUnitColor(unit, color);
	}

	public void SetUnitColor(IUnit unit, Vector3 color)
	{
		if (unit is IEcsEntityWrapper wrapper && GameContext.World.IsAlive(wrapper.Entity))
		{
			if (TryGetUnit3D(wrapper.Entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
				unit3D.ApplyModelTint(new Color(color.X, color.Y, color.Z));
		}
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsInRadius(Vector3 center, float radius, Func<IUnit, bool> filter)
	{
		return GetUnitsInRadius(center, radius, filter);
	}

	public IEnumerable<IUnit> GetUnitsInRadius(Vector3 center, float radius, Func<IUnit, bool> filter)
	{
		var godotCenter = new Godot.Vector3(center.X, center.Y, center.Z);
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(u) || !GameContext.World.IsAlive(u.Entity)) continue;
			if (u.GlobalPosition.DistanceTo(godotCenter) > radius) continue;
			var w = GameContext.SimulationService.GetUnitWrapper(u.Entity);
			if (filter(w)) list.Add(w);
		}

		return list;
	}

	IEnumerable<IUnit> IGameAPI.GetUnitsOwnedByPlayer(int playerIndex, Func<IUnit, bool> filter)
	{
		return GetUnitsOwnedByPlayer(playerIndex, filter);
	}

	public IEnumerable<IUnit> GetUnitsOwnedByPlayer(int playerIndex, Func<IUnit, bool> filter)
	{
		var list = new List<IUnit>();
		foreach (var u in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(u) || !GameContext.World.IsAlive(u.Entity)) continue;
			if (u.Player != playerIndex) continue;
			var w = GameContext.SimulationService.GetUnitWrapper(u.Entity);
			if (filter(w)) list.Add(w);
		}

		return list;
	}

	void IGameAPI.IssueAttackMoveOrder(IUnit unit, Vector3 destination)
	{
		GameContext.SimulationService.IssueAttackMoveOrder(unit, destination, (u, c, d) => OnUnitOrdered?.Invoke(u, c, d));
	}

	void IGameAPI.IssueCastOrder(IUnit caster, string abilityId, IUnit target)
	{
		GameContext.SimulationService.IssueCastOrder(caster, abilityId, target, (u, c, d) => OnUnitOrdered?.Invoke(u, c, d));
	}

	void IGameAPI.IssueCastOrderAt(IUnit caster, string abilityId, Vector3 position)
	{
		GameContext.SimulationService.IssueCastOrderAt(caster, abilityId, position, OnUnitOrdered);
	}

	void IGameAPI.SetAbilityAutoCast(IUnit unit, string abilityId, bool active)
	{
		GameContext.SimulationService.SetAbilityAutoCast(unit, abilityId, active);
	}

	void IGameAPI.SetPlayerComputerControlled(int playerIndex, bool isProxy)
	{
		GameContext.SimulationService.SetPlayerComputerControlled(playerIndex, isProxy);
	}

	void IGameAPI.AddLeaderboardRow(string label, string value, Vector3? color)
	{
		InGameHUD.Instance?.SetLeaderboardValue(label, value);
	}

	float IGameAPI.GetPlayerWood(int playerIndex)
	{
		return GameContext.ResourceEconomyService.GetPlayerWood(playerIndex);
	}

	void IGameAPI.SetPlayerWood(int playerIndex, float amount)
	{
		GameContext.ResourceEconomyService.SetPlayerWood(playerIndex, amount);
	}

	void IGameAPI.AdjustPlayerWood(int playerIndex, float delta)
	{
		GameContext.ResourceEconomyService.AdjustPlayerWood(playerIndex, delta);
	}

	void IGameAPI.SetUnitOwner(IUnit unit, int playerIndex)
	{
		GameContext.SimulationService.SetUnitOwner(unit, playerIndex);
	}

	int IGameAPI.GetPlayerCurrentPopulation(int playerIndex)
	{
		return GameContext.ResourceEconomyService.GetPlayerCurrentPopulation(playerIndex);
	}

	int IGameAPI.GetPlayerMaxPopulation(int playerIndex)
	{
		return GameContext.ResourceEconomyService.GetPlayerMaxPopulation(playerIndex);
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

	void IGameAPI.SetUnitRouteState(IUnit unit, int state)
	{
		GameContext.SimulationService.SetUnitRouteState(unit, state);
	}

	int IGameAPI.GetUnitRouteState(IUnit unit)
	{
		return GameContext.SimulationService.GetUnitRouteState(unit);
	}

	void IGameAPI.SetUnitLevel(IUnit unit, int level)
	{
		GameContext.SimulationService.SetUnitLevel(unit, level);
	}

	int IGameAPI.GetPlayerKills(int playerIndex)
	{
		return GameContext.SimulationService.GetPlayerKillsDomain(playerIndex);
	}

	void IGameAPI.SetPlayerKills(int playerIndex, int kills)
	{
		GameContext.SimulationService.SetPlayerKillsDomain(playerIndex, kills);
	}

	void IGameAPI.IssueMoveOrder(IUnit unit, Vector3 destination)
	{
		if (unit != null)
		{
			unit.MoveTo(destination);
			OnUnitOrdered?.Invoke(unit, "Move", destination);
		}
	}

	void IGameAPI.SetUnitSpellImmune(IUnit unit, bool immune)
	{
		GameContext.SimulationService.SetUnitSpellImmune(unit, immune);
	}

	float IGameAPI.GetPlayerStone(int playerIndex)
	{
		return GameContext.ResourceEconomyService.GetPlayerStone(playerIndex);
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

	void IGameAPI.SelectPlayerUnit(int playerIndex, IUnit unit)
	{
		GameContext.ClientRPC.SelectUnit(playerIndex, unit);
	}

	void IGameAPI.ClearPlayerSelection(int playerIndex)
	{
		GameContext.ClientRPC.ClearSelection(playerIndex);
	}

	IEnumerable<IUnit> IGameAPI.GetPlayerSelectedUnits(int playerIndex)
	{
		return GameContext.ClientRPC.GetSelectedUnits(playerIndex);
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

	void IGameAPI.EnableUnitAutoCombat(IUnit unit, bool enabled)
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
}