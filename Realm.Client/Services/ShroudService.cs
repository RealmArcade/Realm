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
	private const float PresentationInterval = 0.1f;
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

	private byte[] _shroudPixelBuffer = Array.Empty<byte>();
	private byte[] _shroudMinimapPixelBuffer = Array.Empty<byte>();
	private int _textureWidth;
	private int _textureHeight;

	private float _shroudTimer;
	private int _observedGroup = -1;
	private int _observedPlayerIndex = -1;
	private int _lastAppliedVersion = -1;
	private int _lastAppliedGroup = -2;
	private bool _isRevealAll = true;
	private bool _hasPresentedShroud;
	private bool _hasAppliedRevealAllPresentation;
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

		EnsureTextures(1, 1);
		RenderingServer.GlobalShaderParameterSet(ShroudLerpParameter, 0f);
		_shroudTimer = PresentationInterval;
		_lastAppliedVersion = -1;
		_lastAppliedGroup = -2;
		_hasPresentedShroud = false;
		_hasAppliedRevealAllPresentation = false;
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
		if (_isPresentationSuspended)
		{
			RenderingServer.GlobalShaderParameterSet(ShroudLerpParameter, 1f);
			return;
		}

		if (!_hasPresentedShroud)
		{
			_shroudTimer = PresentationInterval;
		}

		_shroudTimer += delta;
		if (_shroudTimer < PresentationInterval)
		{
			RenderingServer.GlobalShaderParameterSet(ShroudLerpParameter, Mathf.Clamp(_shroudTimer / PresentationInterval, 0f, 1f));
			return;
		}

		_shroudTimer = 0f;
		int observedPlayerIndex = ResolveObservedPlayerIndex(spectatorPerspective, isPlayingReplay, isSpectator);
		if (observedPlayerIndex != _observedPlayerIndex)
		{
			_lastAppliedVersion = -1;
			_lastAppliedGroup = -2;
		}

		_observedPlayerIndex = observedPlayerIndex;
		UpdateShroud(allUnits, allProps, allDecals, observedPlayerIndex);
	}

	private void UpdateShroud(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int observedPlayerIndex)
	{
		_hasPresentedShroud = true;

		var world = GameContext.World;
		if (world == null)
		{
			ApplyRevealAllPresentation(allUnits, allProps, allDecals);
			return;
		}

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !world.IsAlive(worldEntity) || !world.Has<ShroudState>(worldEntity))
		{
			ApplyRevealAllPresentation(allUnits, allProps, allDecals);
			return;
		}

		ref var shroud = ref world.Get<ShroudState>(worldEntity);
		bool isOmniscient = observedPlayerIndex < 0
		                    || (uint)observedPlayerIndex >= ShroudGrid.MaxGroups
		                    || GameContext.SessionState.IsMapEditorMode
		                    || shroud.VisibilityMode == ShroudVisibilityMode.Revealed
		                    || !shroud.IsAllocated;

		if (isOmniscient)
		{
			ApplyRevealAllPresentation(allUnits, allProps, allDecals);
			return;
		}

		_isRevealAll = false;
		_hasAppliedRevealAllPresentation = false;
		_observedGroup = shroud.GetGroupIndex(observedPlayerIndex);
		SetGlobalBounds(shroud.WorldMinX, shroud.WorldMinZ, shroud.WorldWidth, shroud.WorldDepth);
		ApplyGlobalTexture();

		bool contentChanged = _observedGroup != _lastAppliedGroup
		                     || shroud.Version != _lastAppliedVersion
		                     || shroud.GridWidth != _textureWidth
		                     || shroud.GridHeight != _textureHeight;

		if (contentChanged)
		{
			EnsureTextures(shroud.GridWidth, shroud.GridHeight);
			WriteShroudPixels(in shroud);
			UploadShroudTexture();
			WriteMinimapPixels(in shroud);
			UploadMinimapTexture();
			_lastAppliedGroup = _observedGroup;
			_lastAppliedVersion = shroud.Version;
		}

		ApplyEntityVisibility(in shroud, allUnits, allProps, allDecals);
	}

	private void ApplyRevealAllPresentation(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals)
	{
		_isRevealAll = true;
		_observedGroup = -1;
		_lastAppliedGroup = -1;
		_lastAppliedVersion = -1;

		SetNodesVisible(allUnits);
		SetNodesVisible(allProps);
		SetNodesVisible(allDecals);

		SetGlobalBounds(0f, 0f, 0f, 0f);
		ApplyGlobalTexture();

		if (_hasAppliedRevealAllPresentation) return;
		_hasAppliedRevealAllPresentation = true;

		EnsureTextures(1, 1);
		WriteRevealedShroudPixels();
		UploadShroudTexture();
		WriteRevealedMinimapPixels();
		UploadMinimapTexture();
	}

	private static void SetNodesVisible<T>(List<T> nodes) where T : Node3D
	{
		for (int index = 0; index < nodes.Count; index++)
		{
			var node = nodes[index];
			if (node == null || !GodotObject.IsInstanceValid(node)) continue;
			if (node.Visible) continue;
			node.Visible = true;
		}
	}

	private void ApplyEntityVisibility(in ShroudState shroud, List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals)
	{
		var world = GameContext.World;
		int observedGroup = _observedGroup;

		for (int index = 0; index < allUnits.Count; index++)
		{
			var unit = allUnits[index];
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;

			bool shouldBeVisible = IsPlayerInObservedGroup(in shroud, unit.Player, observedGroup);
			if (!shouldBeVisible && unit.Entity != Entity.Null && world.IsAlive(unit.Entity) && world.Has<Position>(unit.Entity))
			{
				ref var position = ref world.Get<Position>(unit.Entity);
				shouldBeVisible = shroud.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex)
				                  && shroud.IsVisible(cellIndex, observedGroup);
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
				shouldBeVisible = shroud.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex)
				                  && shroud.IsExplored(cellIndex, observedGroup);
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
				shouldBeVisible = shroud.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex)
				                  && shroud.IsExplored(cellIndex, observedGroup);
			}

			if (decal.Visible != shouldBeVisible)
			{
				decal.Visible = shouldBeVisible;
			}
		}
	}

	private static unsafe bool IsPlayerInObservedGroup(in ShroudState shroud, int playerIndex, int observedGroup)
	{
		if ((uint)playerIndex >= ShroudGrid.MaxGroups) return true;
		return shroud.GroupOfPlayer[playerIndex] == observedGroup;
	}

	public bool IsUnitVisibleOnMinimap(Unit3D unit)
	{
		if (unit == null || !GodotObject.IsInstanceValid(unit)) return false;
		if (_isRevealAll || !_hasPresentedShroud) return true;

		var world = GameContext.World;
		if (world == null) return true;

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !world.IsAlive(worldEntity) || !world.Has<ShroudState>(worldEntity)) return true;

		ref var shroud = ref world.Get<ShroudState>(worldEntity);
		if (!shroud.IsAllocated) return true;
		if (IsPlayerInObservedGroup(in shroud, unit.Player, _observedGroup)) return true;
		if (unit.Entity == Entity.Null || !world.IsAlive(unit.Entity) || !world.Has<Position>(unit.Entity)) return true;

		ref var position = ref world.Get<Position>(unit.Entity);
		return shroud.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex)
		       && shroud.IsVisible(cellIndex, _observedGroup);
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

	private unsafe void WriteShroudPixels(in ShroudState shroud)
	{
		byte[] pixels = _shroudPixelBuffer;
		int groupOffset = _observedGroup * shroud.CellCount;

		for (int cellIndex = 0; cellIndex < shroud.CellCount; cellIndex++)
		{
			int sourceIndex = groupOffset + cellIndex;
			bool isExplored = shroud.ExploredCells[sourceIndex] != 0;
			bool isVisible = shroud.VisibleCells[sourceIndex] != 0;
			int byteIndex = cellIndex * 4;

			pixels[byteIndex + 2] = pixels[byteIndex];
			pixels[byteIndex + 3] = pixels[byteIndex + 1];
			pixels[byteIndex] = isExplored ? ExploredChannelValue : (byte)0;
			pixels[byteIndex + 1] = isVisible ? ExploredChannelValue : (byte)0;
		}
	}

	private unsafe void WriteMinimapPixels(in ShroudState shroud)
	{
		byte[] pixels = _shroudMinimapPixelBuffer;
		int groupOffset = _observedGroup * shroud.CellCount;

		for (int cellIndex = 0; cellIndex < shroud.CellCount; cellIndex++)
		{
			int sourceIndex = groupOffset + cellIndex;
			bool isExplored = shroud.ExploredCells[sourceIndex] != 0;
			bool isVisible = shroud.VisibleCells[sourceIndex] != 0;
			int byteIndex = cellIndex * 4;

			pixels[byteIndex] = 0;
			pixels[byteIndex + 1] = 0;
			pixels[byteIndex + 2] = 0;
			pixels[byteIndex + 3] = !isExplored ? ExplorationShroudMinimapAlpha : isVisible ? (byte)0 : VisionShroudMinimapAlpha;
		}
	}

	private void WriteRevealedShroudPixels()
	{
		Array.Fill(_shroudPixelBuffer, (byte)255);
	}

	private void WriteRevealedMinimapPixels()
	{
		Array.Clear(_shroudMinimapPixelBuffer, 0, _shroudMinimapPixelBuffer.Length);
	}

	private void EnsureTextures(int width, int height)
	{
		if (width < 1) width = 1;
		if (height < 1) height = 1;

		int byteLength = width * height * 4;
		if (_shroudPixelBuffer.Length != byteLength)
		{
			_shroudPixelBuffer = new byte[byteLength];
		}

		if (_shroudMinimapPixelBuffer.Length != byteLength)
		{
			_shroudMinimapPixelBuffer = new byte[byteLength];
		}

		if (_shroudImage == null || _shroudTexture == null || !GodotObject.IsInstanceValid(_shroudTexture) || _textureWidth != width || _textureHeight != height)
		{
			_textureWidth = width;
			_textureHeight = height;
			_shroudImage = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
			_shroudImage.SetData(width, height, false, Image.Format.Rgba8, _shroudPixelBuffer);
			_shroudTexture = ImageTexture.CreateFromImage(_shroudImage);
		}

		if (_shroudMinimapImage == null || _shroudMinimapTexture == null || !GodotObject.IsInstanceValid(_shroudMinimapTexture))
		{
			_shroudMinimapImage = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
			_shroudMinimapImage.SetData(width, height, false, Image.Format.Rgba8, _shroudMinimapPixelBuffer);
			_shroudMinimapTexture = ImageTexture.CreateFromImage(_shroudMinimapImage);
		}
	}

	private void UploadShroudTexture()
	{
		EnsureTextures(_textureWidth, _textureHeight);
		_shroudImage.SetData(_textureWidth, _textureHeight, false, Image.Format.Rgba8, _shroudPixelBuffer);
		_shroudTexture.Update(_shroudImage);
		ApplyGlobalTexture();
		RenderingServer.GlobalShaderParameterSet(ShroudLerpParameter, 0f);
	}

	private void UploadMinimapTexture()
	{
		EnsureTextures(_textureWidth, _textureHeight);
		_shroudMinimapImage.SetData(_textureWidth, _textureHeight, false, Image.Format.Rgba8, _shroudMinimapPixelBuffer);
		_shroudMinimapTexture.Update(_shroudMinimapImage);
	}

	private void ApplyGlobalTexture()
	{
		Texture2D texture = _isPresentationSuspended || _isRevealAll || _shroudTexture == null ? GetFullyRevealedTexture() : _shroudTexture;
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

		_shroudTimer = PresentationInterval;
		_lastAppliedVersion = -1;
		_lastAppliedGroup = -2;
		_hasAppliedRevealAllPresentation = false;

		var perspectiveFilter = GameContext.PerspectiveFilter;
		bool isSpectator = perspectiveFilter != null && perspectiveFilter.IsConfigured && perspectiveFilter.IsSpectating;
		bool isReplay = ReplayPlaybackManager.Instance.IsPlayingReplay;
		int spectatorPerspective = GameContext.SpectatorService?.GetSpectatorPerspective() ?? -1;
		int observedPlayerIndex = ResolveObservedPlayerIndex(spectatorPerspective, isReplay, isSpectator);
		_observedPlayerIndex = observedPlayerIndex;

		UpdateShroud(GameHost.Instance.AllUnits, GameHost.Instance.AllProps, GameHost.Instance.AllDecals, observedPlayerIndex);
		InGameHUD.Instance?.QueueMinimapRedraw();
	}

	public void CleanUp()
	{
		_hasPresentedShroud = false;
		_hasAppliedRevealAllPresentation = false;
		_isPresentationSuspended = false;
		_isRevealAll = true;
		_observedGroup = -1;
		_observedPlayerIndex = -1;
		_lastAppliedVersion = -1;
		_lastAppliedGroup = -2;
		_shroudTimer = 0f;
		_cachedWorldEntity = Entity.Null;

		_shroudImage = null;
		_shroudTexture = null;
		_shroudMinimapImage = null;
		_shroudMinimapTexture = null;
		_textureWidth = 0;
		_textureHeight = 0;
		_appliedGlobalTexture = null;
		_appliedGlobalBounds = Vector4.Zero;

		RenderingServer.GlobalShaderParameterSet(ShroudWorldBoundsParameter, Vector4.Zero);
		RenderingServer.GlobalShaderParameterSet(ShroudLerpParameter, 1f);
		RenderingServer.GlobalShaderParameterSet(ShroudTextureParameter, GetFullyRevealedTexture());
	}

	public static ImageTexture GetFullyRevealedTexture()
	{
		if (_fullyRevealedTexture != null && GodotObject.IsInstanceValid(_fullyRevealedTexture))
		{
			return _fullyRevealedTexture;
		}

		var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
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
