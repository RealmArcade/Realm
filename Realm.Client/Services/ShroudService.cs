using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Client.Services;

public class ShroudService
{
	private const float ShroudTickInterval = 0.1f;
	private const byte ExploredChannelValue = 255;
	private const byte VisionShroudMinimapAlpha = 84;
	private const byte ExplorationShroudMinimapAlpha = 255;

	private static readonly StringName ShroudTextureParameter = new("rts_shroud_texture");
	private static readonly StringName ShroudWorldBoundsParameter = new("rts_shroud_world_bounds");
	private static readonly StringName ShroudLerpParameter = new("rts_shroud_lerp");

	private Image _shroudImage;
	private ImageTexture _shroudTexture;
	private Image _shroudMinimapImage;
	private ImageTexture _shroudMinimapTexture;
	private Texture2D _appliedGlobalTexture;
	private Vector4 _appliedGlobalBounds;
	private static ImageTexture _fullyRevealedTexture;

	private readonly byte[] _shroudPixelBuffer = new byte[ShroudGrid.CellCount * 4];
	private readonly byte[] _shroudMinimapPixelBuffer = new byte[ShroudGrid.CellCount * 4];

	private float _shroudTimer;
	private ulong _lastShroudChecksum;
	private ulong _lastMinimapChecksum;
	private ushort _observedMask = ShroudGrid.AllPlayerMask;
	private ushort _hostileMask;
	private int _observedPlayerIndex = -1;
	private bool _isRevealAll = true;
	private bool _hasPresentedShroud;
	private bool _isMapEditorPresentationInitialized;
	private bool _isPresentationSuspended;
	private Entity _cachedWorldEntity = Entity.Null;

	public ImageTexture? ShroudTexture => _shroudTexture;
	public ImageTexture? ShroudMinimapTexture => _shroudMinimapTexture;
	public bool IsRevealAll => _isRevealAll;

	public void Initialize(Node mainNode)
	{
		if (mainNode != null)
		{
			RemoveLegacyShroudNodes(mainNode);
		}

		EnsureTextures();
		ApplyGlobalTexture();
		RenderingServer.GlobalShaderParameterSet(ShroudLerpParameter, 0f);
		_shroudTimer = 0f;
		_lastShroudChecksum = 0UL;
		_lastMinimapChecksum = 0UL;
		_hasPresentedShroud = false;
		TriggerImmediateUpdate();
	}

	private static void RemoveLegacyShroudNodes(Node mainNode)
	{
		RemoveLegacyNode(mainNode, "3DShroudMesh");
		RemoveLegacyNode(mainNode, "3DFogMesh");
	}

	private static void RemoveLegacyNode(Node mainNode, string nodeName)
	{
		var legacyNode = mainNode.GetNodeOrNull<Node3D>(nodeName);
		if (legacyNode == null || !GodotObject.IsInstanceValid(legacyNode)) return;
		mainNode.RemoveChild(legacyNode);
		legacyNode.QueueFree();
	}

	public void Tick(float delta, List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		if (_isPresentationSuspended) return;

		if (GameContext.SessionState.IsMapEditorMode)
		{
			TickMapEditorMode(allUnits, allProps, allDecals);
			return;
		}

		if (_isMapEditorPresentationInitialized)
		{
			_isMapEditorPresentationInitialized = false;
			_hasPresentedShroud = false;
		}

		int observedPlayerIndex = ResolveObservedPlayerIndex(spectatorPerspective, isPlayingReplay, isSpectator);
		_shroudTimer += delta;
		if (!_hasPresentedShroud || observedPlayerIndex != _observedPlayerIndex)
		{
			_shroudTimer = ShroudTickInterval;
		}

		if (_shroudTimer >= ShroudTickInterval)
		{
			_shroudTimer = 0f;
			UpdateShroud(allUnits, allProps, allDecals, observedPlayerIndex);
		}

		RenderingServer.GlobalShaderParameterSet(ShroudLerpParameter, Mathf.Clamp(_shroudTimer / ShroudTickInterval, 0f, 1f));
	}

	private void TickMapEditorMode(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals)
	{
		if (_isMapEditorPresentationInitialized) return;
		_isMapEditorPresentationInitialized = true;
		_hasPresentedShroud = true;
		_isRevealAll = true;
		_observedMask = ShroudGrid.AllPlayerMask;
		_observedPlayerIndex = -1;
		_hostileMask = 0;

		SetNodesVisible(allUnits);
		SetNodesVisible(allProps);
		SetNodesVisible(allDecals);

		EnsureTextures();
		WriteRevealedShroudPixels();
		UploadShroudTexture();
		WriteRevealedMinimapPixels();
		UploadMinimapTexture();

		if (GameHost.Instance?.GroundTerrain != null)
		{
			float worldWidth = GameHost.Instance.GroundTerrain.Width * GameHost.Instance.GroundTerrain.QuadSize;
			float worldDepth = GameHost.Instance.GroundTerrain.Depth * GameHost.Instance.GroundTerrain.QuadSize;
			SetGlobalBounds(worldWidth * -0.5f, worldDepth * -0.5f, worldWidth, worldDepth);
		}

		RenderingServer.GlobalShaderParameterSet(ShroudLerpParameter, 1f);
	}

	private void UpdateShroud(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int observedPlayerIndex)
	{
		if (GameHost.Instance == null || GameContext.World == null) return;

		float worldWidth = GameHost.Instance.GroundTerrain != null ? GameHost.Instance.GroundTerrain.Width * GameHost.Instance.GroundTerrain.QuadSize : 250f;
		float worldDepth = GameHost.Instance.GroundTerrain != null ? GameHost.Instance.GroundTerrain.Depth * GameHost.Instance.GroundTerrain.QuadSize : 250f;
		ShroudGrid.ConfigureWorld(worldWidth, worldDepth);

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<ShroudState>(worldEntity))
		{
			ApplyRevealAllPresentation(allUnits, allProps, allDecals);
			SetGlobalBounds(worldWidth * -0.5f, worldDepth * -0.5f, worldWidth, worldDepth);
			_hasPresentedShroud = true;
			return;
		}

		ref var shroud = ref GameContext.World.Get<ShroudState>(worldEntity);
		shroud.EnsureAllocated(worldWidth, worldDepth);
		ShroudSimulationService.Tick(GameContext.World, worldEntity);

		_observedPlayerIndex = observedPlayerIndex;
		_observedMask = BuildObservedMask(worldEntity, observedPlayerIndex);
		_hostileMask = BuildHostileMask(worldEntity, observedPlayerIndex);
		_isRevealAll = observedPlayerIndex < 0;
		_hasPresentedShroud = true;

		SetGlobalBounds(shroud.WorldMinX, shroud.WorldMinZ, shroud.WorldWidth, shroud.WorldDepth);
		EnsureTextures();

		if (_isRevealAll)
		{
			ApplyRevealAllPresentation(allUnits, allProps, allDecals);
			WriteRevealedShroudPixels();
			UploadShroudTexture();
			WriteRevealedMinimapPixels();
			UploadMinimapTexture();
			return;
		}

		UpdateShroudTexture(in shroud);
		UpdateMinimapTexture(in shroud);
		ApplyEntityVisibility(in shroud, allUnits, allProps, allDecals);
	}

	private void ApplyRevealAllPresentation(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals)
	{
		_isRevealAll = true;
		SetNodesVisible(allUnits);
		SetNodesVisible(allProps);
		SetNodesVisible(allDecals);
	}

	private static void SetNodesVisible<T>(List<T> nodes) where T : Node3D
	{
		for (int index = 0; index < nodes.Count; index++)
		{
			var node = nodes[index];
			if (node == null || !GodotObject.IsInstanceValid(node) || node.Visible) continue;
			node.Visible = true;
		}
	}

	private void ApplyEntityVisibility(in ShroudState shroud, List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals)
	{
		var world = GameContext.World;

		for (int index = 0; index < allUnits.Count; index++)
		{
			var unit = allUnits[index];
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;

			bool shouldBeVisible = !IsUnitPlayerHostile(unit.Player);
			if (!shouldBeVisible && unit.Entity != Entity.Null && world.IsAlive(unit.Entity) && world.Has<Position>(unit.Entity))
			{
				ref var position = ref world.Get<Position>(unit.Entity);
				shouldBeVisible = shroud.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex) && shroud.IsVisible(cellIndex, _observedMask);
			}

			if (unit.Visible != shouldBeVisible)
			{
				unit.Visible = shouldBeVisible;
			}
		}

		for (int index = 0; index < allProps.Count; index++)
		{
			var prop = allProps[index];
			if (prop == null || !GodotObject.IsInstanceValid(prop)) continue;

			bool shouldBeVisible = true;
			if (prop.Entity != Entity.Null && world.IsAlive(prop.Entity) && world.Has<Position>(prop.Entity))
			{
				ref var position = ref world.Get<Position>(prop.Entity);
				shouldBeVisible = shroud.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex) && shroud.IsExplored(cellIndex, _observedMask);
			}

			if (prop.Visible != shouldBeVisible)
			{
				prop.Visible = shouldBeVisible;
			}
		}

		for (int index = 0; index < allDecals.Count; index++)
		{
			var decal = allDecals[index];
			if (decal == null || !GodotObject.IsInstanceValid(decal)) continue;

			bool shouldBeVisible = true;
			if (decal is Decal3D decal3D && decal3D.Entity != Entity.Null && world.IsAlive(decal3D.Entity) && world.Has<Position>(decal3D.Entity))
			{
				ref var position = ref world.Get<Position>(decal3D.Entity);
				shouldBeVisible = shroud.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex) && shroud.IsExplored(cellIndex, _observedMask);
			}

			if (decal.Visible != shouldBeVisible)
			{
				decal.Visible = shouldBeVisible;
			}
		}
	}

	private bool IsUnitPlayerHostile(int playerIndex)
	{
		if ((uint)playerIndex >= ShroudSimulationService.MaxBitmaskPlayers) return false;
		return (_hostileMask & (1 << playerIndex)) != 0;
	}

	public bool IsUnitVisibleOnMinimap(Unit3D unit)
	{
		if (unit == null || !GodotObject.IsInstanceValid(unit)) return false;
		if (_isRevealAll || !_hasPresentedShroud) return true;
		if (!IsUnitPlayerHostile(unit.Player)) return true;

		var world = GameContext.World;
		if (world == null || unit.Entity == Entity.Null || !world.IsAlive(unit.Entity) || !world.Has<Position>(unit.Entity)) return true;

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !world.IsAlive(worldEntity) || !world.Has<ShroudState>(worldEntity)) return true;

		ref var shroud = ref world.Get<ShroudState>(worldEntity);
		ref var position = ref world.Get<Position>(unit.Entity);
		return shroud.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex) && shroud.IsVisible(cellIndex, _observedMask);
	}

	private int ResolveObservedPlayerIndex(int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		if (isPlayingReplay) return ResolveReplayPerspectivePlayerIndex();
		if (isSpectator) return spectatorPerspective <= 0 ? -1 : spectatorPerspective;

		var perspectiveFilter = GameContext.PerspectiveFilter;
		if (perspectiveFilter == null || !perspectiveFilter.IsConfigured || perspectiveFilter.IsSpectating) return -1;
		return perspectiveFilter.ControlledPlayerIndex;
	}

	private static int ResolveReplayPerspectivePlayerIndex()
	{
		int peerId = ReplayPlaybackManager.Instance.SpectatorPerspective;
		if (peerId == -1) return -1;

		var lobby = LobbyManager.Instance;
		if (lobby != null)
		{
			var players = lobby.PlayerList;
			for (int index = 0; index < players.Count; index++)
			{
				if (players[index].PeerId == peerId) return players[index].Slot;
			}
		}

		var world = GameContext.World;
		Entity worldEntity = ECSContext.WorldEntity;
		if (world == null || worldEntity == Entity.Null || !world.IsAlive(worldEntity) || !world.Has<NetworkMappingState>(worldEntity)) return -1;

		var mapping = world.Get<NetworkMappingState>(worldEntity);
		if (mapping.PeerIdToPlayerEntityMap == null || !mapping.PeerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity)) return -1;
		if (!world.IsAlive(playerEntity) || !world.Has<UnitOwnerPlayer>(playerEntity)) return -1;

		return world.Get<UnitOwnerPlayer>(playerEntity).PlayerIndex;
	}

	private static ushort BuildObservedMask(Entity worldEntity, int observedPlayerIndex)
	{
		if ((uint)observedPlayerIndex >= ShroudSimulationService.MaxBitmaskPlayers) return ShroudGrid.AllPlayerMask;

		ushort observedMask = (ushort)(1 << observedPlayerIndex);
		var world = GameContext.World;
		if (world == null || !world.IsAlive(worldEntity) || !world.Has<PlayerRelationshipMatrix>(worldEntity)) return observedMask;

		ref var relationships = ref world.Get<PlayerRelationshipMatrix>(worldEntity);
		for (int playerIndex = 0; playerIndex < ShroudSimulationService.MaxBitmaskPlayers; playerIndex++)
		{
			if (relationships.AreEnemies(observedPlayerIndex, playerIndex)) continue;
			observedMask |= (ushort)(1 << playerIndex);
		}

		return observedMask;
	}

	private static ushort BuildHostileMask(Entity worldEntity, int observedPlayerIndex)
	{
		if ((uint)observedPlayerIndex >= ShroudSimulationService.MaxBitmaskPlayers) return 0;

		ushort hostileMask = 0;
		var world = GameContext.World;
		if (world == null || !world.IsAlive(worldEntity) || !world.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			for (int playerIndex = 0; playerIndex < ShroudSimulationService.MaxBitmaskPlayers; playerIndex++)
			{
				if (NetworkService.ArePlayerIndicesEnemies(observedPlayerIndex, playerIndex)) hostileMask |= (ushort)(1 << playerIndex);
			}
			return hostileMask;
		}

		ref var relationships = ref world.Get<PlayerRelationshipMatrix>(worldEntity);
		for (int playerIndex = 0; playerIndex < ShroudSimulationService.MaxBitmaskPlayers; playerIndex++)
		{
			if (relationships.AreEnemies(observedPlayerIndex, playerIndex)) hostileMask |= (ushort)(1 << playerIndex);
		}

		return hostileMask;
	}

	private unsafe void UpdateShroudTexture(in ShroudState shroud)
	{
		ulong checksum = ComputeMaskChecksum(shroud.ExploredBitmask, shroud.VisibleBitmask, _observedMask);
		if (checksum == _lastShroudChecksum) return;
		_lastShroudChecksum = checksum;

		unsafe
		{
			byte[] pixels = _shroudPixelBuffer;
			for (int cellIndex = 0; cellIndex < ShroudGrid.CellCount; cellIndex++)
			{
				bool isExplored = (shroud.ExploredBitmask[cellIndex] & _observedMask) != 0;
				bool isVisible = (shroud.VisibleBitmask[cellIndex] & _observedMask) != 0;
				int byteIndex = cellIndex * 4;

				pixels[byteIndex + 2] = pixels[byteIndex];
				pixels[byteIndex + 3] = pixels[byteIndex + 1];
				pixels[byteIndex] = isExplored ? ExploredChannelValue : (byte)0;
				pixels[byteIndex + 1] = isVisible ? ExploredChannelValue : (byte)0;
			}
		}

		UploadShroudTexture();
	}

	private void UpdateMinimapTexture(in ShroudState shroud)
	{
		unsafe
		{
			byte[] pixels = _shroudMinimapPixelBuffer;
			for (int cellIndex = 0; cellIndex < ShroudGrid.CellCount; cellIndex++)
			{
				bool isExplored = (shroud.ExploredBitmask[cellIndex] & _observedMask) != 0;
				bool isVisible = (shroud.VisibleBitmask[cellIndex] & _observedMask) != 0;
				int byteIndex = cellIndex * 4;

				pixels[byteIndex] = 0;
				pixels[byteIndex + 1] = 0;
				pixels[byteIndex + 2] = 0;
				pixels[byteIndex + 3] = !isExplored ? ExplorationShroudMinimapAlpha : isVisible ? (byte)0 : VisionShroudMinimapAlpha;
			}
		}

		ulong checksum = ComputeByteChecksum(_shroudMinimapPixelBuffer);
		if (checksum == _lastMinimapChecksum) return;
		_lastMinimapChecksum = checksum;
		UploadMinimapTexture();
	}

	private static unsafe ulong ComputeMaskChecksum(ushort* exploredBitmask, ushort* visibleBitmask, ushort observedMask)
	{
		ulong checksum = 14695981039346656037UL;
		for (int cellIndex = 0; cellIndex < ShroudGrid.CellCount; cellIndex++)
		{
			checksum = (checksum ^ (ushort)(exploredBitmask[cellIndex] & observedMask)) * 1099511628211UL;
			checksum = (checksum ^ (ushort)(visibleBitmask[cellIndex] & observedMask)) * 1099511628211UL;
		}

		return (checksum ^ observedMask) * 1099511628211UL;
	}

	private static ulong ComputeByteChecksum(byte[] buffer)
	{
		ulong checksum = 14695981039346656037UL;
		for (int index = 0; index < buffer.Length; index++)
		{
			checksum = (checksum ^ buffer[index]) * 1099511628211UL;
		}

		return checksum;
	}

	private void WriteRevealedShroudPixels()
	{
		byte[] pixels = _shroudPixelBuffer;
		for (int index = 0; index < pixels.Length; index++)
		{
			pixels[index] = 255;
		}
	}

	private void WriteRevealedMinimapPixels()
	{
		Array.Clear(_shroudMinimapPixelBuffer, 0, _shroudMinimapPixelBuffer.Length);
	}

	private void EnsureTextures()
	{
		if (_shroudImage == null || _shroudTexture == null || !GodotObject.IsInstanceValid(_shroudTexture))
		{
			_shroudImage = Image.CreateEmpty(ShroudGrid.Resolution, ShroudGrid.Resolution, false, Image.Format.Rgba8);
			_shroudImage.SetData(ShroudGrid.Resolution, ShroudGrid.Resolution, false, Image.Format.Rgba8, _shroudPixelBuffer);
			_shroudTexture = ImageTexture.CreateFromImage(_shroudImage);
		}

		if (_shroudMinimapImage == null || _shroudMinimapTexture == null || !GodotObject.IsInstanceValid(_shroudMinimapTexture))
		{
			_shroudMinimapImage = Image.CreateEmpty(ShroudGrid.Resolution, ShroudGrid.Resolution, false, Image.Format.Rgba8);
			_shroudMinimapImage.SetData(ShroudGrid.Resolution, ShroudGrid.Resolution, false, Image.Format.Rgba8, _shroudMinimapPixelBuffer);
			_shroudMinimapTexture = ImageTexture.CreateFromImage(_shroudMinimapImage);
		}
	}

	private void UploadShroudTexture()
	{
		EnsureTextures();
		_shroudImage.SetData(ShroudGrid.Resolution, ShroudGrid.Resolution, false, Image.Format.Rgba8, _shroudPixelBuffer);
		_shroudTexture.Update(_shroudImage);
		ApplyGlobalTexture();
	}

	private void UploadMinimapTexture()
	{
		EnsureTextures();
		_shroudMinimapImage.SetData(ShroudGrid.Resolution, ShroudGrid.Resolution, false, Image.Format.Rgba8, _shroudMinimapPixelBuffer);
		_shroudMinimapTexture.Update(_shroudMinimapImage);
	}

	private void ApplyGlobalTexture()
	{
		Texture2D texture = _isPresentationSuspended || _shroudTexture == null ? GetFullyRevealedTexture() : _shroudTexture;
		if (ReferenceEquals(_appliedGlobalTexture, texture)) return;
		_appliedGlobalTexture = texture;
		RenderingServer.GlobalShaderParameterSet(ShroudTextureParameter, texture);
	}

	private void SetGlobalBounds(float worldMinX, float worldMinZ, float worldWidth, float worldDepth)
	{
		var bounds = new Vector4(
			worldMinX,
			worldMinZ,
			worldWidth > 0.0001f ? 1f / worldWidth : 0f,
			worldDepth > 0.0001f ? 1f / worldDepth : 0f);

		if (bounds == _appliedGlobalBounds) return;
		_appliedGlobalBounds = bounds;
		RenderingServer.GlobalShaderParameterSet(ShroudWorldBoundsParameter, bounds);
	}

	public void SetPresentationSuspended(bool suspended)
	{
		if (_isPresentationSuspended == suspended) return;
		_isPresentationSuspended = suspended;
		_appliedGlobalTexture = null;
		ApplyGlobalTexture();

		if (suspended)
		{
			RenderingServer.GlobalShaderParameterSet(ShroudLerpParameter, 1f);
		}
	}

	public void TriggerImmediateUpdate()
	{
		if (GameHost.Instance == null || GameContext.World == null) return;
		if (GameContext.SessionState.IsMapEditorMode) return;

		_shroudTimer = 0f;
		_lastShroudChecksum = 0UL;
		_lastMinimapChecksum = 0UL;

		var perspectiveFilter = GameContext.PerspectiveFilter;
		bool isSpectator = perspectiveFilter != null && perspectiveFilter.IsConfigured && perspectiveFilter.IsSpectating;
		bool isReplay = ReplayPlaybackManager.Instance.IsPlayingReplay;
		int spectatorPerspective = GameContext.SpectatorService?.GetSpectatorPerspective() ?? -1;

		UpdateShroud(GameHost.Instance.AllUnits, GameHost.Instance.AllProps, GameHost.Instance.AllDecals, ResolveObservedPlayerIndex(spectatorPerspective, isReplay, isSpectator));
		InGameHUD.Instance?.QueueMinimapRedraw();
	}

	public void CleanUp()
	{
		ReleaseShroudGrid();

		_isMapEditorPresentationInitialized = false;
		_hasPresentedShroud = false;
		_isPresentationSuspended = false;
		_isRevealAll = true;
		_observedMask = ShroudGrid.AllPlayerMask;
		_hostileMask = 0;
		_observedPlayerIndex = -1;
		_shroudTimer = 0f;
		_lastShroudChecksum = 0UL;
		_lastMinimapChecksum = 0UL;
		_cachedWorldEntity = Entity.Null;

		_shroudImage = null;
		_shroudTexture = null;
		_shroudMinimapImage = null;
		_shroudMinimapTexture = null;
		_appliedGlobalTexture = null;
		_appliedGlobalBounds = Vector4.Zero;

		RenderingServer.GlobalShaderParameterSet(ShroudWorldBoundsParameter, Vector4.Zero);
		RenderingServer.GlobalShaderParameterSet(ShroudLerpParameter, 1f);
		RenderingServer.GlobalShaderParameterSet(ShroudTextureParameter, GetFullyRevealedTexture());
	}

	private void ReleaseShroudGrid()
	{
		var world = GameContext.World;
		if (world == null) return;

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !world.IsAlive(worldEntity) || !world.Has<ShroudState>(worldEntity)) return;
		world.Get<ShroudState>(worldEntity).Release();
	}

	public static ImageTexture GetFullyRevealedTexture()
	{
		if (_fullyRevealedTexture != null && GodotObject.IsInstanceValid(_fullyRevealedTexture))
		{
			return _fullyRevealedTexture;
		}

		var image = Image.CreateEmpty(ShroudGrid.Resolution, ShroudGrid.Resolution, false, Image.Format.Rgba8);
		image.Fill(Colors.White);
		_fullyRevealedTexture = ImageTexture.CreateFromImage(image);
		return _fullyRevealedTexture;
	}

	private Entity FindWorldEntity()
	{
		var world = GameContext.World;
		if (world == null) return Entity.Null;

		if (_cachedWorldEntity != Entity.Null && world.IsAlive(_cachedWorldEntity) && world.Has<ShroudState>(_cachedWorldEntity))
		{
			return _cachedWorldEntity;
		}

		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllShroudStateQuery;
		foreach (var entity in world.Query(in query))
		{
			worldEntity = entity;
			break;
		}

		_cachedWorldEntity = worldEntity;
		return worldEntity;
	}
}
