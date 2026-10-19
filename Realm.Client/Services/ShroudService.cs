using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

public class ShroudService
{
	private MeshInstance3D _shroudMeshInstance;
	private ShaderMaterial _shroudMeshMaterial;
	private float _shroudUpdateTimer;
	private Image _shroudImage;
	private ImageTexture _shroudTexture;
	private Image _shroudMinimapImage;
	private ImageTexture _shroudMinimapTexture;
	private bool _isEditorShroudInitialized;
	private Shader _shroudShader;

	private readonly byte[] _shroudByteBuffer = new byte[32 * 32 * 4];
	private readonly byte[] _shroudMinimapByteBuffer = new byte[32 * 32 * 4];
	private ulong _lastShroudChecksum;
	private bool _isShroudTextureDirty = true;

	public ImageTexture? ShroudTexture => _shroudTexture;
	public ImageTexture? ShroudMinimapTexture => _shroudMinimapTexture;

	public ShroudService()
	{
	}

	private Entity _cachedWorldEntity = Entity.Null;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector3 GetUnitPosition(Unit3D unit)
	{
		if (unit != null && unit.Entity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(unit.Entity) && GameContext.World.Has<Position>(unit.Entity))
		{
			var p = GameContext.World.Get<Position>(unit.Entity).Value;
			return new Vector3(p.X, p.Y, p.Z);
		}

		return unit != null ? unit.GlobalPosition : Vector3.Zero;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector3 GetPropPosition(Prop3D prop)
	{
		if (prop != null && prop.Entity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(prop.Entity) && GameContext.World.Has<Position>(prop.Entity))
		{
			var p = GameContext.World.Get<Position>(prop.Entity).Value;
			return new Vector3(p.X, p.Y, p.Z);
		}

		return prop != null ? prop.GlobalPosition : Vector3.Zero;
	}

	private Entity FindWorldEntity()
	{
		if (_cachedWorldEntity != Entity.Null && GameContext.World.IsAlive(_cachedWorldEntity) && GameContext.World.Has<ShroudState>(_cachedWorldEntity))
		{
			return _cachedWorldEntity;
		}

		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllShroudStateQuery;
		GameContext.World.Query(in query, (Entity entity) => worldEntity = entity);
		_cachedWorldEntity = worldEntity;
		return _cachedWorldEntity;
	}

	private byte[,] ShroudGrid
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && GameContext.World.Has<ShroudState>(worldEntity))
				return GameContext.World.Get<ShroudState>(worldEntity).ShroudGrid;
			return new byte[32, 32];
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && GameContext.World.Has<ShroudState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<ShroudState>(worldEntity);
				state.ShroudGrid = value;
			}
		}
	}

	private string ShroudType
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && GameContext.World.Has<ShroudState>(worldEntity))
				return GameContext.World.Get<ShroudState>(worldEntity).ShroudType;
			return "VisionShroud";
		}
	}

	public void Initialize(Node mainNode)
	{
		if (mainNode == null) return;

		var existingShroud = mainNode.GetNodeOrNull<MeshInstance3D>("3DShroudMesh");
		if (GodotObject.IsInstanceValid(existingShroud))
		{
			mainNode.RemoveChild(existingShroud);
			existingShroud.QueueFree();
		}

		var existingFog = mainNode.GetNodeOrNull<MeshInstance3D>("3DFogMesh");
		if (GodotObject.IsInstanceValid(existingFog))
		{
			mainNode.RemoveChild(existingFog);
			existingFog.QueueFree();
		}

		var shroudMesh = new MeshInstance3D();
		shroudMesh.Name = "3DShroudMesh";
		shroudMesh.Mesh = new ArrayMesh();

		var shaderMaterial = new ShaderMaterial();
		_shroudShader ??= GD.Load<Shader>("res://Assets/shaders/shroud_fog.gdshader");
		shaderMaterial.Shader = _shroudShader;
		shroudMesh.MaterialOverride = shaderMaterial;
		mainNode.AddChild(shroudMesh);
		shroudMesh.GlobalPosition = new Vector3(0, 60.0f, 0);
		_shroudMeshInstance = shroudMesh;
		_shroudMeshMaterial = shaderMaterial;
	}

	public void Tick(float delta, List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		if (GameHost.Instance != null && GameContext.SessionState.IsMapEditorMode)
		{
			TickMapEditorMode(allUnits, allProps, allDecals);
			return;
		}

		TickGameplayMode(delta, allUnits, allProps, allDecals, spectatorPerspective, isPlayingReplay, isSpectator);
	}

	private void TickMapEditorMode(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals)
	{
		if (_isEditorShroudInitialized) return;

		if (GodotObject.IsInstanceValid(_shroudMeshInstance) && _shroudMeshInstance.Visible)
		{
			_shroudMeshInstance.Visible = false;
		}

		SetVisible(allUnits);
		SetVisible(allProps);
		SetVisible(allDecals);

		if (GameHost.Instance?.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.SetShroudEnabled(false);
			InitializeEditorShroud();
		}
	}

	private void SetVisible<T>(List<T> items) where T : Node3D
	{
		foreach (var item in items)
		{
			if (item != null && GodotObject.IsInstanceValid(item) && !item.Visible)
			{
				item.Visible = true;
			}
		}
	}

	private void InitializeEditorShroud()
	{
		_isEditorShroudInitialized = true;

		Span<float> shroudFloats = MemoryMarshal.Cast<byte, float>(_shroudByteBuffer.AsSpan());
		shroudFloats.Fill(1.0f);

		if (_shroudImage == null)
		{
			_shroudImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
		}
		_shroudImage.SetData(32, 32, false, Image.Format.Rf, _shroudByteBuffer);

		if (_shroudTexture == null)
		{
			_shroudTexture = ImageTexture.CreateFromImage(_shroudImage);
		}
		else
		{
			_shroudTexture.Update(_shroudImage);
		}

		Array.Clear(_shroudMinimapByteBuffer, 0, _shroudMinimapByteBuffer.Length);

		if (_shroudMinimapImage == null)
		{
			_shroudMinimapImage = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
		}
		_shroudMinimapImage.SetData(32, 32, false, Image.Format.Rgba8, _shroudMinimapByteBuffer);

		if (_shroudMinimapTexture == null)
		{
			_shroudMinimapTexture = ImageTexture.CreateFromImage(_shroudMinimapImage);
		}
		else
		{
			_shroudMinimapTexture.Update(_shroudMinimapImage);
		}

		GameHost.Instance.GroundTerrain.SetShroudTexture(_shroudTexture);
		ModelShaderManager.SetShroudParameters(null, Vector2.Zero, Vector2.Zero, false);
	}

	private void TickGameplayMode(float delta, List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		_isEditorShroudInitialized = false;
		if (GameHost.Instance?.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.SetShroudEnabled(true);
		}

		if (GodotObject.IsInstanceValid(_shroudMeshInstance))
		{
			_shroudMeshInstance.Visible = ShouldMeshBeVisible(spectatorPerspective, isPlayingReplay, isSpectator);
		}

		_shroudUpdateTimer += delta;
		if (_shroudUpdateTimer >= 0.1f)
		{
			_shroudUpdateTimer = 0f;
			UpdateShroud(allUnits, allProps, allDecals, spectatorPerspective, isPlayingReplay, isSpectator);
		}
	}

	private bool ShouldMeshBeVisible(int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		string shroudType = ShroudType;
		bool shouldMeshBeVisible = !string.Equals(shroudType, "visible", StringComparison.OrdinalIgnoreCase) && !EditableTerrain.IsMinimapRendering;

		if (!shouldMeshBeVisible || !(isPlayingReplay || isSpectator))
		{
			return shouldMeshBeVisible;
		}

		int targetOwnerId = isPlayingReplay ? ReplayPlaybackManager.Instance.SpectatorPerspective : spectatorPerspective;
		if (targetOwnerId == -1)
		{
			return false;
		}

		return shouldMeshBeVisible;
	}

	public void TriggerImmediateUpdate()
	{
		_isShroudTextureDirty = true;
		if (GameHost.Instance != null && !GameContext.SessionState.IsMapEditorMode)
		{
			int specPerspective = GameContext.SpectatorService?.GetSpectatorPerspective() ?? -1;
			UpdateShroud(GameHost.Instance.AllUnits, GameHost.Instance.AllProps, GameHost.Instance.AllDecals, specPerspective, ReplayPlaybackManager.Instance.IsPlayingReplay, GameContext.PerspectiveFilter.IsSpectating);
			InGameHUD.Instance?.QueueMinimapRedraw();
		}
	}

	public void CleanUp()
	{
		_isEditorShroudInitialized = false;
		_isShroudTextureDirty = true;
		_lastShroudChecksum = 0;
		if (GodotObject.IsInstanceValid(_shroudMeshInstance))
		{
			_shroudMeshInstance.QueueFree();
			_shroudMeshInstance = null;
		}

		_shroudImage = null;
		_shroudTexture = null;
		_shroudMinimapImage = null;
		_shroudMinimapTexture = null;
		_cachedWorldEntity = Entity.Null;
		_isAllVisibleApplied = false;
	}

	private bool _isAllVisibleApplied;

	private void UpdateShroud(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		if (GameHost.Instance == null) return;

		float worldWidth = GameHost.Instance.GroundTerrain != null ? GameHost.Instance.GroundTerrain.Width * GameHost.Instance.GroundTerrain.QuadSize : 250f;
		float worldDepth = GameHost.Instance.GroundTerrain != null ? GameHost.Instance.GroundTerrain.Depth * GameHost.Instance.GroundTerrain.QuadSize : 250f;

		if (isPlayingReplay || isSpectator)
		{
			HandleReplayOrSpectatorShroud(allUnits, allProps, allDecals, spectatorPerspective, isPlayingReplay, worldWidth, worldDepth);
			return;
		}

		if (string.Equals(ShroudType, "visible", StringComparison.OrdinalIgnoreCase))
		{
			SetAllVisible(allUnits, allProps, allDecals);
			return;
		}

		_isAllVisibleApplied = false;
		UpdateVisionGridForTeam(allUnits, worldWidth, worldDepth);
		ApplyVisionGridToEntities(allUnits, allProps, allDecals, worldWidth, worldDepth);
		Update3DShroudMesh();
	}

	private void HandleReplayOrSpectatorShroud(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int spectatorPerspective, bool isPlayingReplay, float worldWidth, float worldDepth)
	{
		if (isPlayingReplay)
		{
			int targetPeerId = ReplayPlaybackManager.Instance.SpectatorPerspective;
			if (targetPeerId == -1)
			{
				SetAllVisible(allUnits, allProps, allDecals);
				return;
			}

			_isAllVisibleApplied = false;
			UpdateVisionGridForReplayPeer(allUnits, targetPeerId, worldWidth, worldDepth);
			ApplyVisionGridToEntitiesForReplayPeer(allUnits, allProps, allDecals, targetPeerId, worldWidth, worldDepth);
			Update3DShroudMesh();
			return;
		}

		if (spectatorPerspective <= 0)
		{
			SetAllVisible(allUnits, allProps, allDecals);
			return;
		}

		_isAllVisibleApplied = false;
		UpdateVisionGridForSpecificTeam(allUnits, spectatorPerspective, worldWidth, worldDepth);
		ApplyVisionGridToEntitiesForTargetTeam(allUnits, allProps, allDecals, spectatorPerspective, worldWidth, worldDepth);
		Update3DShroudMesh();
	}

	private void SetAllVisible(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals)
	{
		SetVisible(allUnits);
		if (_isAllVisibleApplied)
		{
			return;
		}

		_isAllVisibleApplied = true;
		ResetGridToState(ShroudState.Visible);
		SetVisible(allProps);
		SetVisible(allDecals);
		Update3DShroudMesh();
	}

	private void ResetGridToState(byte stateToSet, byte? onlyIfState = null)
	{
		var shroudGrid = ShroudGrid;
		for (int x = 0; x < 32; x++)
		{
			for (int z = 0; z < 32; z++)
			{
				if (onlyIfState == null || shroudGrid[x, z] == onlyIfState.Value)
				{
					shroudGrid[x, z] = stateToSet;
				}
			}
		}

		ShroudGrid = shroudGrid;
	}

	private void UpdateVisionGridForTeam(List<Unit3D> allUnits, float worldWidth, float worldDepth)
	{
		ResetGridToState(ShroudState.VisionShroud, ShroudState.Visible);
		var shroudGrid = ShroudGrid;

		foreach (var unit in allUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit) || GameContext.PerspectiveFilter.IsHostileToObserved(unit.Player)) continue;
			RevealAreaInGrid(unit, shroudGrid, worldWidth, worldDepth);
		}

		ShroudGrid = shroudGrid;
	}

	private void UpdateVisionGridForReplayPeer(List<Unit3D> allUnits, int targetPeerId, float worldWidth, float worldDepth)
	{
		ResetGridToState(ShroudState.VisionShroud, ShroudState.Visible);
		var shroudGrid = ShroudGrid;

		foreach (var unit in allUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
			if (GameHost.Instance.GetOwnerPeerId(unit.Entity) != targetPeerId) continue;
			RevealAreaInGrid(unit, shroudGrid, worldWidth, worldDepth);
		}

		ShroudGrid = shroudGrid;
	}

	private void UpdateVisionGridForSpecificTeam(List<Unit3D> allUnits, int targetPlayerIndex, float worldWidth, float worldDepth)
	{
		ResetGridToState(ShroudState.VisionShroud, ShroudState.Visible);
		var shroudGrid = ShroudGrid;

		foreach (var unit in allUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
			if (unit.Player != targetPlayerIndex && NetworkService.ArePlayerIndicesEnemies(targetPlayerIndex, unit.Player)) continue;
			RevealAreaInGrid(unit, shroudGrid, worldWidth, worldDepth);
		}

		ShroudGrid = shroudGrid;
	}

	private void RevealAreaInGrid(Unit3D unit, byte[,] shroudGrid, float worldWidth, float worldDepth)
	{
		Vector3 pos = GetUnitPosition(unit);
		int gx = (int)Mathf.Clamp((pos.X / worldWidth + 0.5f) * 32, 0, 31);
		int gz = (int)Mathf.Clamp((pos.Z / worldDepth + 0.5f) * 32, 0, 31);

		float scanRadius = (GameContext.World.IsAlive(unit.Entity) && GameContext.World.Has<ScanRadius>(unit.Entity))
			? GameContext.World.Get<ScanRadius>(unit.Entity).Value
			: 15.0f;

		int rGrid = (int)Math.Max(1, Math.Ceiling(scanRadius / (worldWidth / 32f)));
		for (int dx = -rGrid; dx <= rGrid; dx++)
		{
			for (int dz = -rGrid; dz <= rGrid; dz++)
			{
				int nx = gx + dx;
				int nz = gz + dz;
				if (nx >= 0 && nx < 32 && nz >= 0 && nz < 32 && dx * dx + dz * dz <= rGrid * rGrid)
				{
					shroudGrid[nx, nz] = ShroudState.Visible;
				}
			}
		}
	}

	private void ApplyVisionGridToEntities(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, float worldWidth, float worldDepth)
	{
		var shroudGrid = ShroudGrid;

		foreach (var unit in allUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
			bool isEnemy = GameContext.PerspectiveFilter.IsHostileToObserved(unit.Player);
			Vector3 pos = GetUnitPosition(unit);
			bool shouldBeVisible = !isEnemy || IsVisibleOnGrid(pos, shroudGrid, worldWidth, worldDepth);
			if (unit.Visible != shouldBeVisible) unit.Visible = shouldBeVisible;
		}

		ApplyPropGridVisibility(allProps, shroudGrid, worldWidth, worldDepth, ShroudState.Visible);
		ApplyDecalGridVisibility(allDecals, shroudGrid, worldWidth, worldDepth, ShroudState.ExplorationShroud, true);
	}

	private void ApplyVisionGridToEntitiesForReplayPeer(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int targetPeerId, float worldWidth, float worldDepth)
	{
		var shroudGrid = ShroudGrid;

		foreach (var unit in allUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
			Vector3 pos = GetUnitPosition(unit);
			bool shouldBeVisible = GameHost.Instance.GetOwnerPeerId(unit.Entity) == targetPeerId || IsVisibleOnGrid(pos, shroudGrid, worldWidth, worldDepth);
			if (unit.Visible != shouldBeVisible) unit.Visible = shouldBeVisible;
		}

		ApplyPropGridVisibility(allProps, shroudGrid, worldWidth, worldDepth, ShroudState.Visible);
		ApplyDecalGridVisibility(allDecals, shroudGrid, worldWidth, worldDepth, ShroudState.ExplorationShroud, true);
	}

	private void ApplyVisionGridToEntitiesForTargetTeam(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int targetPlayerIndex, float worldWidth, float worldDepth)
	{
		var shroudGrid = ShroudGrid;

		foreach (var unit in allUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
			bool isFriendlyToTarget = unit.Player == targetPlayerIndex || !NetworkService.ArePlayerIndicesEnemies(targetPlayerIndex, unit.Player);
			Vector3 pos = GetUnitPosition(unit);
			bool shouldBeVisible = isFriendlyToTarget || IsVisibleOnGrid(pos, shroudGrid, worldWidth, worldDepth);
			if (unit.Visible != shouldBeVisible) unit.Visible = shouldBeVisible;
		}

		ApplyPropGridVisibility(allProps, shroudGrid, worldWidth, worldDepth, ShroudState.Visible);
		ApplyDecalGridVisibility(allDecals, shroudGrid, worldWidth, worldDepth, ShroudState.ExplorationShroud, true);
	}

	private bool IsVisibleOnGrid(Vector3 pos, byte[,] shroudGrid, float worldWidth, float worldDepth)
	{
		int gx = (int)Mathf.Clamp((pos.X / worldWidth + 0.5f) * 32, 0, 31);
		int gz = (int)Mathf.Clamp((pos.Z / worldDepth + 0.5f) * 32, 0, 31);
		return shroudGrid[gx, gz] == ShroudState.Visible;
	}

	private void ApplyPropGridVisibility(List<Prop3D> props, byte[,] shroudGrid, float worldWidth, float worldDepth, byte targetState, bool notEqual = false)
	{
		foreach (var prop in props)
		{
			if (prop == null || !GodotObject.IsInstanceValid(prop)) continue;
			Vector3 pos = GetPropPosition(prop);
			int gx = (int)Mathf.Clamp((pos.X / worldWidth + 0.5f) * 32, 0, 31);
			int gz = (int)Mathf.Clamp((pos.Z / worldDepth + 0.5f) * 32, 0, 31);

			bool shouldBeVisible = notEqual ? shroudGrid[gx, gz] != targetState : shroudGrid[gx, gz] == targetState;
			if (prop.Visible != shouldBeVisible)
			{
				prop.Visible = shouldBeVisible;
			}
		}
	}

	private void ApplyDecalGridVisibility(List<Decal> decals, byte[,] shroudGrid, float worldWidth, float worldDepth, byte targetState, bool notEqual = false)
	{
		foreach (var decal in decals)
		{
			if (decal == null || !GodotObject.IsInstanceValid(decal)) continue;
			Vector3 pos = decal.GlobalPosition;
			int gx = (int)Mathf.Clamp((pos.X / worldWidth + 0.5f) * 32, 0, 31);
			int gz = (int)Mathf.Clamp((pos.Z / worldDepth + 0.5f) * 32, 0, 31);

			bool shouldBeVisible = notEqual ? shroudGrid[gx, gz] != targetState : shroudGrid[gx, gz] == targetState;
			if (decal.Visible != shouldBeVisible)
			{
				decal.Visible = shouldBeVisible;
			}
		}
	}

	private void Update3DShroudMesh()
	{
		ResizeShroudMesh();
		UpdateShroudImagePixels();
	}

	private void ResizeShroudMesh()
	{
		if (!GodotObject.IsInstanceValid(_shroudMeshInstance) || GameHost.Instance?.GroundTerrain == null)
		{
			return;
		}

		Vector2 targetSize = new Vector2(
			GameHost.Instance.GroundTerrain.Width * GameHost.Instance.GroundTerrain.QuadSize,
			GameHost.Instance.GroundTerrain.Depth * GameHost.Instance.GroundTerrain.QuadSize
		);

		if (_shroudMeshInstance.Mesh is PlaneMesh planeMesh)
		{
			if (planeMesh.Size != targetSize)
			{
				planeMesh.Size = targetSize;
			}
		}
		else
		{
			_shroudMeshInstance.Mesh = new PlaneMesh { Size = targetSize };
		}
	}

	private static ulong CalculateGridChecksum(byte[,] shroudGrid)
	{
		ulong hash = 14695981039346656037UL;
		for (int gz = 0; gz < 32; gz++)
		{
			for (int gx = 0; gx < 32; gx++)
			{
				hash = (hash ^ shroudGrid[gx, gz]) * 1099511628211UL;
			}
		}

		return hash;
	}

	private void UpdateShroudImagePixels()
	{
		var shroudGrid = ShroudGrid;
		ulong checksum = CalculateGridChecksum(shroudGrid);

		if (checksum == _lastShroudChecksum && !_isShroudTextureDirty)
		{
			return;
		}

		_lastShroudChecksum = checksum;
		_isShroudTextureDirty = false;

		Span<float> shroudFloats = MemoryMarshal.Cast<byte, float>(_shroudByteBuffer.AsSpan());

		for (int gz = 0; gz < 32; gz++)
		{
			for (int gx = 0; gx < 32; gx++)
			{
				int pixelIdx = gz * 32 + gx;
				byte val = shroudGrid[gx, gz];

				float alpha = val switch
				{
					ShroudState.ExplorationShroud => 1.0f,
					ShroudState.VisionShroud => 0.48f,
					ShroudState.Visible => 0.0f,
					_ => 1.0f
				};
				shroudFloats[pixelIdx] = alpha;

				float minimapAlpha = val switch
				{
					ShroudState.ExplorationShroud => 1.0f,
					ShroudState.VisionShroud => 0.33f,
					ShroudState.Visible => 0.0f,
					_ => 1.0f
				};

				int byteIdx = pixelIdx * 4;
				_shroudMinimapByteBuffer[byteIdx] = 0;
				_shroudMinimapByteBuffer[byteIdx + 1] = 0;
				_shroudMinimapByteBuffer[byteIdx + 2] = 0;
				_shroudMinimapByteBuffer[byteIdx + 3] = (byte)Math.Clamp((int)MathF.Round(minimapAlpha * 255.0f), 0, 255);
			}
		}

		if (_shroudImage == null)
		{
			_shroudImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
		}
		_shroudImage.SetData(32, 32, false, Image.Format.Rf, _shroudByteBuffer);

		if (_shroudMinimapImage == null)
		{
			_shroudMinimapImage = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
		}
		_shroudMinimapImage.SetData(32, 32, false, Image.Format.Rgba8, _shroudMinimapByteBuffer);

		if (_shroudTexture == null)
		{
			_shroudTexture = ImageTexture.CreateFromImage(_shroudImage);
		}
		else
		{
			_shroudTexture.Update(_shroudImage);
		}

		if (_shroudMinimapTexture == null)
		{
			_shroudMinimapTexture = ImageTexture.CreateFromImage(_shroudMinimapImage);
		}
		else
		{
			_shroudMinimapTexture.Update(_shroudMinimapImage);
		}

		ApplyShroudTextureAndShader();
	}

	private void ApplyShroudTextureAndShader()
	{
		float halfW = 125.0f;
		float halfD = 125.0f;
		bool shroudEnabled = GameHost.Instance == null || !GameContext.SessionState.IsMapEditorMode;

		if (GameHost.Instance?.GroundTerrain != null)
		{
			halfW = (GameHost.Instance.GroundTerrain.Width * GameHost.Instance.GroundTerrain.QuadSize) * 0.5f;
			halfD = (GameHost.Instance.GroundTerrain.Depth * GameHost.Instance.GroundTerrain.QuadSize) * 0.5f;
			GameHost.Instance.GroundTerrain.SetShroudTexture(_shroudTexture);
			GameHost.Instance.GroundTerrain.SetShroudEnabled(shroudEnabled);
		}

		Vector2 worldMin = new Vector2(-halfW, -halfD);
		Vector2 worldSize = new Vector2(halfW * 2f, halfD * 2f);
		ModelShaderManager.SetShroudParameters(_shroudTexture, worldMin, worldSize, shroudEnabled);

		_shroudMeshMaterial?.SetShaderParameter("shroud_texture", _shroudTexture);
	}
}