using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;
using Realm.MapAPI;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	private const string DefaultLeaderboardBoardId = "default";

	private const int AllPlayers = ServerPresentationEvent.Everyone;

	private readonly Dictionary<int, ScriptStaticText> _staticTexts = new();
	private readonly Dictionary<int, List<int>> _selectedUnitsByPlayer = new();
	private int _nextStaticTextHandle = 1;
	private string _currentEnvironmentPreset = "day";
	private string _currentWeather = "clear";

	public void SpawnVisualEffect(string effectTypeId, Vector3 position, float scale = 1.0f)
	{
		if (string.IsNullOrWhiteSpace(effectTypeId)) return;

		_presentation.Write(PresentationKind.VisualEffect, AllPlayers, 0, effectTypeId, string.Empty, string.Empty, string.Empty, null,
			position, Vector3.Zero, Vector3.Zero, 0, 0, scale, 0f, 0f, false);
	}

	public void SpawnBarrageVolley(string effectTypeId, Vector3 targetAreaCenter, float radius, int count, float intervalSeconds)
	{
		if (string.IsNullOrWhiteSpace(effectTypeId)) return;

		_presentation.Write(PresentationKind.BarrageVolley, AllPlayers, 0, effectTypeId, string.Empty, string.Empty, string.Empty, null,
			targetAreaCenter, Vector3.Zero, Vector3.Zero, count, 0, 0f, radius, intervalSeconds, false);
	}

	public void SpawnExpandingGroundRing(Vector3 position, float maxRadius = 8.0f, float speed = 12.0f, float duration = 1.0f, Vector3? color = null)
	{
		_presentation.Write(PresentationKind.GroundRing, AllPlayers, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			position, Vector3.Zero, color ?? Vector3.One, 0, 0, speed, maxRadius, duration, false);
	}

	public void SpawnExpandingBurstSphere(Vector3 position, float maxRadius = 6.0f, float speed = 15.0f, float duration = 0.8f, Vector3? color = null)
	{
		_presentation.Write(PresentationKind.BurstSphere, AllPlayers, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			position, Vector3.Zero, color ?? Vector3.One, 0, 0, speed, maxRadius, duration, false);
	}

	public void DrawLineOnTerrain(Vector2 start, Vector2 end, Vector3? color = null, float duration = 1.0f, float width = 0.2f)
	{
		_presentation.Write(PresentationKind.TerrainLine, AllPlayers, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			new Vector3(start.X, 0f, start.Y), new Vector3(end.X, 0f, end.Y), color ?? Vector3.One, 0, 0, width, 0f, duration, false);
	}

	public void AttachPersistentAura(UnitId unit, string auraVfxId, float scale = 1.0f)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (string.IsNullOrWhiteSpace(auraVfxId)) return;

		_presentation.Write(PresentationKind.PersistentAura, AllPlayers, unit.Value, auraVfxId, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, scale, 0f, 0f, false);
	}

	public void SpawnProjectile(string projectileTypeId, Vector3 start, Vector3 target, float speed)
	{
		if (string.IsNullOrWhiteSpace(projectileTypeId)) return;

		_presentation.Write(PresentationKind.Projectile, AllPlayers, 0, projectileTypeId, string.Empty, string.Empty, string.Empty, null,
			start, target, Vector3.Zero, 0, 0, speed, 0f, 0f, false);
	}

	public void SetLeaderboardVisible(string title, bool visible)
	{
		_presentation.Write(PresentationKind.LeaderboardVisible, AllPlayers, 0, title, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, visible);
	}

	public void SetLeaderboardValue(string label, string value)
	{
		_presentation.Write(PresentationKind.LeaderboardValue, AllPlayers, 0, label, value, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, true);
	}

	public void ClearLeaderboard()
	{
		_presentation.Write(PresentationKind.LeaderboardCleared, AllPlayers, Vector3.Zero);
	}

	public void SetLeaderboardBoard(string boardId, string title, bool visible)
	{
		if (string.IsNullOrWhiteSpace(boardId)) return;

		_presentation.Write(PresentationKind.LeaderboardBoard, AllPlayers, 0, boardId, title, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, visible);
	}

	public void ClearLeaderboardBoard(string boardId)
	{
		if (string.IsNullOrWhiteSpace(boardId)) return;

		_presentation.Write(PresentationKind.LeaderboardBoardCleared, AllPlayers, 0, boardId, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void SetLeaderboardBoardGroup(string boardId, int groupPlayerIndex, string groupTitle)
	{
		if (string.IsNullOrWhiteSpace(boardId)) return;

		_presentation.Write(PresentationKind.LeaderboardBoardGroup, AllPlayers, 0, boardId, groupTitle, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, groupPlayerIndex, 0, 0f, 0f, 0f, false);
	}

	public void AddLeaderboardBoardRow(string boardId, int groupPlayerIndex, string groupTitle, string label, string value)
	{
		if (string.IsNullOrWhiteSpace(boardId)) return;

		_presentation.Write(PresentationKind.LeaderboardBoardRow, AllPlayers, 0, boardId, groupTitle, label, value, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, groupPlayerIndex, 0, 0f, 0f, 0f, false);
	}

	public void AddLeaderboardRow(string label, string value, Vector3? color = null)
	{
		_presentation.Write(PresentationKind.LeaderboardRow, AllPlayers, 0, DefaultLeaderboardBoardId, label, value, string.Empty, null,
			Vector3.Zero, Vector3.Zero, color ?? Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void ShowSummaryTable(string title, bool visible)
	{
		_presentation.Write(PresentationKind.SummaryTableVisible, AllPlayers, 0, title, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, visible);
	}

	public void SetSummaryTableHeaders(params string[] columnHeaders)
	{
		_presentation.Write(PresentationKind.SummaryTableHeaders, AllPlayers, 0, string.Empty, string.Empty, string.Empty, string.Empty, columnHeaders ?? Array.Empty<string>(),
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void SetSummaryTableRow(string rowKey, params string[] cellValues)
	{
		_presentation.Write(PresentationKind.SummaryTableRow, AllPlayers, 0, rowKey, string.Empty, string.Empty, string.Empty, cellValues ?? Array.Empty<string>(),
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void ClearSummaryTable()
	{
		_presentation.Write(PresentationKind.SummaryTableCleared, AllPlayers, Vector3.Zero);
	}

	public void StartCountdownTimer(float duration, string label)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity != Entity.Null && _context.World.IsAlive(worldEntity))
		{
			_context.World.SetOrAdd(worldEntity, new CountdownState(true, Math.Max(0f, duration), label ?? string.Empty));
		}

		_presentation.Write(PresentationKind.CountdownStarted, AllPlayers, 0, label ?? string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, Math.Max(0f, duration), 0f, 0f, true);
	}

	public void StopCountdownTimer()
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity != Entity.Null && _context.World.IsAlive(worldEntity) && _context.World.Has<CountdownState>(worldEntity))
		{
			CountdownState countdown = _context.World.Get<CountdownState>(worldEntity);
			_context.World.Set(worldEntity, new CountdownState(false, 0f, countdown.Text));
		}

		_presentation.Write(PresentationKind.CountdownStopped, AllPlayers, Vector3.Zero);
	}

	public void SetCountdownTimerLabel(string label)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity != Entity.Null && _context.World.IsAlive(worldEntity) && _context.World.Has<CountdownState>(worldEntity))
		{
			CountdownState countdown = _context.World.Get<CountdownState>(worldEntity);
			_context.World.Set(worldEntity, new CountdownState(countdown.Active, countdown.Duration, label ?? string.Empty));
		}

		_presentation.Write(PresentationKind.CountdownLabel, AllPlayers, 0, label ?? string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void ShakeCamera(float intensity, float duration)
	{
		_presentation.Write(PresentationKind.CameraShaken, AllPlayers, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, intensity, 0f, duration, false);
	}

	public void ShakePlayerCamera(int playerIndex, float intensity, float duration)
	{
		_presentation.Write(PresentationKind.CameraShaken, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, intensity, 0f, duration, false);
	}

	public void PanPlayerCameraTo(int playerIndex, Vector3 position, float duration = 0f)
	{
		_presentation.Write(PresentationKind.CameraPanned, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			position, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, duration, false);
	}

	public void SetPlayerCameraBounds(int playerIndex, float minX, float minZ, float maxX, float maxZ)
	{
		_presentation.Write(PresentationKind.CameraBoundsSet, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			new Vector3(minX, 0f, minZ), new Vector3(maxX, 0f, maxZ), Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void ResetPlayerCameraBounds(int playerIndex)
	{
		_presentation.Write(PresentationKind.CameraBoundsReset, playerIndex, Vector3.Zero);
	}

	public void SetPlayerHudElementVisible(int playerIndex, HudElement element, bool visible)
	{
		_presentation.Write(PresentationKind.HudElementVisible, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, (int)element, 0, 0f, 0f, 0f, visible);
	}

	public void SelectPlayerUnit(int playerIndex, UnitId unit)
	{
		if (!unit.IsValid) return;

		if (!_selectedUnitsByPlayer.TryGetValue(playerIndex, out List<int>? selected))
		{
			selected = new List<int>();
			_selectedUnitsByPlayer[playerIndex] = selected;
		}

		if (!selected.Contains(unit.Value)) selected.Add(unit.Value);

		_presentation.Write(PresentationKind.SelectionChanged, playerIndex, unit.Value, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, true);
	}

	public void ClearPlayerSelection(int playerIndex)
	{
		if (_selectedUnitsByPlayer.TryGetValue(playerIndex, out List<int>? selected)) selected.Clear();

		_presentation.Write(PresentationKind.SelectionChanged, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public ReadOnlySpan<UnitId> GetPlayerSelectedUnits(int playerIndex)
	{
		if (!_selectedUnitsByPlayer.TryGetValue(playerIndex, out List<int>? selected) || selected.Count == 0) return ReadOnlySpan<UnitId>.Empty;

		if (selected.Count > SelectionBufferCapacity) selected.RemoveRange(SelectionBufferCapacity, selected.Count - SelectionBufferCapacity);

		for (int index = 0; index < selected.Count; index++) _selectionBuffer[index] = (UnitId)selected[index];

		return new ReadOnlySpan<UnitId>(_selectionBuffer, 0, selected.Count);
	}

	private const int SelectionBufferCapacity = 256;
	private readonly UnitId[] _selectionBuffer = new UnitId[SelectionBufferCapacity];

	public void PlayPlayerWarningSound(int playerIndex)
	{
		_presentation.Write(PresentationKind.WarningSound, playerIndex, Vector3.Zero);
	}

	public void PlayPlayerClickSound(int playerIndex)
	{
		_presentation.Write(PresentationKind.ClickSound, playerIndex, Vector3.Zero);
	}

	public void StartPlayerBuildingPlacement(int playerIndex, string unitTypeId)
	{
		if (string.IsNullOrWhiteSpace(unitTypeId)) return;
		if (_map.Catalog.FindBuilding(unitTypeId) == null) return;

		_presentation.Write(PresentationKind.BuildingPlacementStarted, playerIndex, 0, unitTypeId, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, true);
	}

	public int CreateStaticText(string text, Vector3 position, Vector3 color, int fontSize = 48)
	{
		int handle = _nextStaticTextHandle++;

		_staticTexts[handle] = new ScriptStaticText(text ?? string.Empty, position, color, fontSize, true);
		_presentation.Write(PresentationKind.StaticTextCreated, AllPlayers, handle, string.Empty, text ?? string.Empty, string.Empty, string.Empty, null,
			position, Vector3.Zero, color, fontSize, 0, 0f, 0f, 0f, true);

		return handle;
	}

	public void SetStaticText(int handle, string text)
	{
		if (!_staticTexts.TryGetValue(handle, out ScriptStaticText existing)) return;

		_staticTexts[handle] = existing with { Text = text ?? string.Empty };
		_presentation.Write(PresentationKind.StaticTextUpdated, AllPlayers, handle, string.Empty, text ?? string.Empty, string.Empty, string.Empty, null,
			existing.Position, Vector3.Zero, existing.Color, existing.FontSize, 0, 0f, 0f, 0f, existing.Visible);
	}

	public void SetStaticTextColor(int handle, Vector3 color)
	{
		if (!_staticTexts.TryGetValue(handle, out ScriptStaticText existing)) return;

		_staticTexts[handle] = existing with { Color = color };
		_presentation.Write(PresentationKind.StaticTextColor, AllPlayers, handle, string.Empty, string.Empty, string.Empty, string.Empty, null,
			existing.Position, Vector3.Zero, color, existing.FontSize, 0, 0f, 0f, 0f, existing.Visible);
	}

	public void SetStaticTextVisible(int handle, bool visible)
	{
		if (!_staticTexts.TryGetValue(handle, out ScriptStaticText existing)) return;

		_staticTexts[handle] = existing with { Visible = visible };
		_presentation.Write(PresentationKind.StaticTextVisible, AllPlayers, handle, string.Empty, string.Empty, string.Empty, string.Empty, null,
			existing.Position, Vector3.Zero, existing.Color, existing.FontSize, 0, 0f, 0f, 0f, visible);
	}

	public void DestroyStaticText(int handle)
	{
		if (!_staticTexts.Remove(handle)) return;

		_presentation.Write(PresentationKind.StaticTextDestroyed, AllPlayers, handle, Vector3.Zero);
	}

	public void AppendStaticTextDescription(List<ServerPresentationEvent> destination)
	{
		foreach (KeyValuePair<int, ScriptStaticText> entry in _staticTexts)
		{
			ScriptStaticText label = entry.Value;
			destination.Add(new ServerPresentationEvent(
				PresentationKind.StaticTextCreated,
				AllPlayers,
				entry.Key,
				string.Empty,
				label.Text,
				string.Empty,
				string.Empty,
				null,
				label.Position,
				Vector3.Zero,
				label.Color,
				label.FontSize,
				0,
				0f,
				0f,
				0f,
				label.Visible));
		}
	}

	public void SetTimeOfDay(float time)
	{
		SetWorldTimeOfDay(time);

		_presentation.Write(PresentationKind.TimeOfDaySet, AllPlayers, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, time, 0f, 0f, false);
	}

	public void SetDayNightCycleEnabled(bool enabled)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity != Entity.Null && _context.World.IsAlive(worldEntity) && _context.World.Has<WorldState>(worldEntity))
		{
			WorldState state = _context.World.Get<WorldState>(worldEntity);
			_context.World.Set(worldEntity, new WorldState(state.GameElapsedTime, state.TimeOfDayIndex, state.TimeOfDayTimer, enabled));
		}

		_presentation.Write(PresentationKind.DayNightCycleSet, AllPlayers, 0, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, enabled);
	}

	public void SetEnvironmentPreset(string presetId)
	{
		if (string.IsNullOrWhiteSpace(presetId)) return;

		_currentEnvironmentPreset = presetId;
		_presentation.Write(PresentationKind.EnvironmentPresetSet, AllPlayers, 0, presetId, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void TransitionEnvironmentPreset(string presetId, float durationSeconds)
	{
		if (string.IsNullOrWhiteSpace(presetId)) return;

		_currentEnvironmentPreset = presetId;
		_presentation.Write(PresentationKind.EnvironmentPresetTransitioned, AllPlayers, 0, presetId, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, durationSeconds, false);
	}

	public string GetCurrentEnvironmentPreset() => _currentEnvironmentPreset;

	public void SetWeather(string weatherType)
	{
		if (string.IsNullOrWhiteSpace(weatherType)) return;

		_currentWeather = weatherType;
		_presentation.Write(PresentationKind.WeatherSet, AllPlayers, 0, weatherType, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public string GetWeather() => _currentWeather;

	public void DisableShroud() => _context.ShroudDisabled = true;

	public void SetUnitColor(UnitId unit, Vector3 color)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_presentation.Write(PresentationKind.UnitColored, AllPlayers, unit.Value, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, color, 0, 0, 0f, 0f, 0f, false);
	}

	public void SetUnitAnimation(UnitId unit, string animationName)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (string.IsNullOrWhiteSpace(animationName)) return;

		_presentation.Write(PresentationKind.UnitAnimationSet, AllPlayers, unit.Value, animationName, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void SetUnitHandAttachment(UnitId unit, string hand, string? attachmentId)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (string.IsNullOrWhiteSpace(hand)) return;

		_presentation.Write(PresentationKind.UnitHandAttachmentSet, AllPlayers, unit.Value, hand, attachmentId ?? string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void TriggerMeshImpulse(UnitId unit, float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;

		_presentation.Write(PresentationKind.UnitMeshImpulse, AllPlayers, unit.Value, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, strength, frequency, duration, false);
	}

	public void TriggerResourceMeshImpulse(ResourceNodeId node, float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f)
	{
		if (!node.IsValid) return;

		_presentation.Write(PresentationKind.ResourceMeshImpulse, AllPlayers, node.Value, string.Empty, string.Empty, string.Empty, string.Empty, null,
			Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, strength, frequency, duration, false);
	}

	private void SetWorldTimeOfDay(float time)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return;
		if (!_context.World.Has<WorldState>(worldEntity)) return;

		WorldState state = _context.World.Get<WorldState>(worldEntity);
		float normalized = time - MathF.Floor(time);
		int slot = (int)(normalized * WorldStateSlots) % WorldStateSlots;
		_context.World.Set(worldEntity, new WorldState(state.GameElapsedTime, slot, normalized * DayNightCycleLengthSeconds, state.DayNightCycleEnabled));
	}

	private const int WorldStateSlots = 4;
	private const float DayNightCycleLengthSeconds = 90f;

	private readonly record struct ScriptStaticText(string Text, Vector3 Position, Vector3 Color, int FontSize, bool Visible);
}
