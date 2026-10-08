using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Godot.ReplaySystem;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Common;
using System;
using System.Collections.Generic;

public class ShroudService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;
	private MeshInstance3D _shroudMeshInstance;
	private ShaderMaterial _shroudMeshMaterial;
	private float _shroudUpdateTimer;
	private Image _shroudImage;
	private ImageTexture _shroudTexture;
	private bool _isEditorShroudInitialized;

	public ShroudService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	private Entity _cachedWorldEntity = Entity.Null;

	private Entity FindWorldEntity()
	{
		if (_cachedWorldEntity != Entity.Null && EcsWorld.IsAlive(_cachedWorldEntity) && EcsWorld.Has<ShroudState>(_cachedWorldEntity))
		{
			return _cachedWorldEntity;
		}

		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllShroudStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		_cachedWorldEntity = worldEntity;
		return _cachedWorldEntity;
	}

	private byte[,] ShroudGrid
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<ShroudState>(worldEntity))
				return EcsWorld.Get<ShroudState>(worldEntity).ShroudGrid;
			return new byte[32, 32];
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<ShroudState>(worldEntity))
			{
				ref var state = ref EcsWorld.Get<ShroudState>(worldEntity);
				state.ShroudGrid = value;
			}
		}
	}

	private string ShroudType
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<ShroudState>(worldEntity))
				return EcsWorld.Get<ShroudState>(worldEntity).ShroudType;
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
		var shader = new Shader();
		shader.Code = @"
shader_type spatial;
render_mode unshaded, depth_draw_never, cull_disabled, blend_mix;

uniform sampler2D shroud_texture : hint_default_black;
uniform vec2 shroud_world_min = vec2(-125.0, -125.0);
uniform vec2 shroud_world_size = vec2(250.0, 250.0);

varying vec3 v_world_pos;

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX += NORMAL * 0.05;
}

void fragment() {
	vec2 shroud_uv = (v_world_pos.xz - shroud_world_min) / shroud_world_size;
	float shroud_alpha = texture(shroud_texture, clamp(shroud_uv, 0.0, 1.0)).r;
	ALBEDO = vec3(0.0, 0.0, 0.0);
	ALPHA = shroud_alpha;
	if (shroud_alpha < 0.01) { discard; }
}
";
		shaderMaterial.Shader = shader;
		shroudMesh.MaterialOverride = shaderMaterial;
		mainNode.AddChild(shroudMesh);
		shroudMesh.GlobalPosition = new Vector3(0, 60.0f, 0);
		_shroudMeshInstance = shroudMesh;
		_shroudMeshMaterial = shaderMaterial;
	}

	public void Tick(float delta, List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
		{
			TickMapEditorMode(allUnits, allProps, allDecals);
			return;
		}

		TickGameplayMode(delta, allUnits, allProps, allDecals, spectatorPerspective, isPlayingReplay, isSpectator);
	}

	private void TickMapEditorMode(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals)
	{
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
			if (!_isEditorShroudInitialized)
			{
				InitializeEditorShroud();
			}
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
		if (_shroudImage == null)
		{
			_shroudImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
		}
		_shroudImage.Fill(new Color(0f, 0f, 0f, 1f));
		if (_shroudTexture == null)
		{
			_shroudTexture = ImageTexture.CreateFromImage(_shroudImage);
		}
		else
		{
			_shroudTexture.Update(_shroudImage);
		}
		GameHost.Instance.GroundTerrain.SetShroudTexture(_shroudTexture);
		Realm.Godot.Utils.ModelShaderManager.SetShroudParameters(null, Vector2.Zero, Vector2.Zero, false);
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
		if (GameHost.Instance != null && !GameHost.Instance.IsMapEditorMode)
		{
			int specPerspective = GameHost.Instance.SpectatorService?.GetSpectatorPerspective() ?? -1;
			UpdateShroud(GameHost.Instance.AllUnits, GameHost.Instance.AllProps, GameHost.Instance.AllDecals, specPerspective, ReplayPlaybackManager.Instance.IsPlayingReplay, LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator");
			InGameHUD.Instance?.QueueMinimapRedraw();
		}
	}

	public void CleanUp()
	{
		_isEditorShroudInitialized = false;
		if (GodotObject.IsInstanceValid(_shroudMeshInstance))
		{
			_shroudMeshInstance.QueueFree();
			_shroudMeshInstance = null;
		}
		_shroudImage = null;
		_shroudTexture = null;
		_cachedWorldEntity = Entity.Null;
	}

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

		UpdateVisionGridForTeam(allUnits, worldWidth, worldDepth);
		ApplyVisionGridToEntities(allUnits, allProps, allDecals, worldWidth, worldDepth);
		Update3DShroudMesh();
	}

	private void HandleReplayOrSpectatorShroud(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int spectatorPerspective, bool isPlayingReplay, float worldWidth, float worldDepth)
	{
		int targetOwnerId = isPlayingReplay ? ReplayPlaybackManager.Instance.SpectatorPerspective : spectatorPerspective;
		if (targetOwnerId == -1)
		{
			SetAllVisible(allUnits, allProps, allDecals);
			return;
		}
		
		UpdateVisionGridForSpecificTeam(allUnits, targetOwnerId, worldWidth, worldDepth);
		ApplyVisionGridToEntitiesForTargetTeam(allUnits, allProps, allDecals, targetOwnerId, worldWidth, worldDepth);
		Update3DShroudMesh();
	}

	private void SetAllVisible(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals)
	{
		ResetGridToState(ShroudState.Visible);
		SetVisible(allUnits);
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
			if (unit == null || !GodotObject.IsInstanceValid(unit) || unit.IsEnemy) continue;
			RevealAreaInGrid(unit, shroudGrid, worldWidth, worldDepth);
		}

		ShroudGrid = shroudGrid;
	}

	private void UpdateVisionGridForSpecificTeam(List<Unit3D> allUnits, int targetOwnerId, float worldWidth, float worldDepth)
	{
		ResetGridToState(ShroudState.VisionShroud, ShroudState.Visible);
		var shroudGrid = ShroudGrid;

		foreach (var unit in allUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
			if (GameHost.Instance.GetOwnerPeerId(unit.Entity) != targetOwnerId) continue;
			RevealAreaInGrid(unit, shroudGrid, worldWidth, worldDepth);
		}

		ShroudGrid = shroudGrid;
	}

	private void RevealAreaInGrid(Unit3D unit, byte[,] shroudGrid, float worldWidth, float worldDepth)
	{
		Vector3 pos = unit.GlobalPosition;
		int gx = (int)Mathf.Clamp((pos.X / worldWidth + 0.5f) * 32, 0, 31);
		int gz = (int)Mathf.Clamp((pos.Z / worldDepth + 0.5f) * 32, 0, 31);

		float scanRadius = (EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<Realm.Ecs.Components.Combat.ScanRadius>(unit.Entity))
			? EcsWorld.Get<Realm.Ecs.Components.Combat.ScanRadius>(unit.Entity).Value
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
			bool shouldBeVisible = !unit.IsEnemy || IsVisibleOnGrid(unit.GlobalPosition, shroudGrid, worldWidth, worldDepth);
			if (unit.Visible != shouldBeVisible) unit.Visible = shouldBeVisible;
		}

		ApplyGridVisibility(allProps, shroudGrid, worldWidth, worldDepth, ShroudState.Visible);
		ApplyGridVisibility(allDecals, shroudGrid, worldWidth, worldDepth, ShroudState.ExplorationShroud, true);
	}

	private void ApplyVisionGridToEntitiesForTargetTeam(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int targetOwnerId, float worldWidth, float worldDepth)
	{
		var shroudGrid = ShroudGrid;

		foreach (var unit in allUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
			bool shouldBeVisible = GameHost.Instance.GetOwnerPeerId(unit.Entity) == targetOwnerId || IsVisibleOnGrid(unit.GlobalPosition, shroudGrid, worldWidth, worldDepth);
			if (unit.Visible != shouldBeVisible) unit.Visible = shouldBeVisible;
		}

		ApplyGridVisibility(allProps, shroudGrid, worldWidth, worldDepth, ShroudState.Visible);
		ApplyGridVisibility(allDecals, shroudGrid, worldWidth, worldDepth, ShroudState.ExplorationShroud, true);
	}

	private bool IsVisibleOnGrid(Vector3 pos, byte[,] shroudGrid, float worldWidth, float worldDepth)
	{
		int gx = (int)Mathf.Clamp((pos.X / worldWidth + 0.5f) * 32, 0, 31);
		int gz = (int)Mathf.Clamp((pos.Z / worldDepth + 0.5f) * 32, 0, 31);
		return shroudGrid[gx, gz] == ShroudState.Visible;
	}

	private void ApplyGridVisibility<T>(List<T> entities, byte[,] shroudGrid, float worldWidth, float worldDepth, byte targetState, bool notEqual = false) where T : Node3D
	{
		foreach (var entity in entities)
		{
			if (entity == null || !GodotObject.IsInstanceValid(entity)) continue;
			Vector3 pos = entity.GlobalPosition;
			int gx = (int)Mathf.Clamp((pos.X / worldWidth + 0.5f) * 32, 0, 31);
			int gz = (int)Mathf.Clamp((pos.Z / worldDepth + 0.5f) * 32, 0, 31);
			
			bool shouldBeVisible = notEqual ? shroudGrid[gx, gz] != targetState : shroudGrid[gx, gz] == targetState;
			if (entity.Visible != shouldBeVisible)
			{
				entity.Visible = shouldBeVisible;
			}
		}
	}

	private void Update3DShroudMesh()
	{
		ResizeShroudMesh();
		UpdateShroudImagePixels();
		ApplyShroudTextureAndShader();
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

	private void UpdateShroudImagePixels()
	{
		if (_shroudImage == null)
		{
			_shroudImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
		}

		var shroudGrid = ShroudGrid;
		for (int gz = 0; gz < 32; gz++)
		{
			for (int gx = 0; gx < 32; gx++)
			{
				byte val = shroudGrid[gx, gz];
				float alpha = val switch
				{
					ShroudState.ExplorationShroud => 1.0f,
					ShroudState.VisionShroud => 0.48f,
					ShroudState.Visible => 0.0f,
					_ => 1.0f
				};
				_shroudImage.SetPixel(gx, gz, new Color(alpha, alpha, alpha, 1f));
			}
		}

		if (_shroudTexture == null)
		{
			_shroudTexture = ImageTexture.CreateFromImage(_shroudImage);
		}
		else
		{
			_shroudTexture.Update(_shroudImage);
		}
	}

	private void ApplyShroudTextureAndShader()
	{
		float halfW = 125.0f;
		float halfD = 125.0f;
		bool shroudEnabled = GameHost.Instance == null || !GameHost.Instance.IsMapEditorMode;
		
		if (GameHost.Instance?.GroundTerrain != null)
		{
			halfW = (GameHost.Instance.GroundTerrain.Width * GameHost.Instance.GroundTerrain.QuadSize) * 0.5f;
			halfD = (GameHost.Instance.GroundTerrain.Depth * GameHost.Instance.GroundTerrain.QuadSize) * 0.5f;
			GameHost.Instance.GroundTerrain.SetShroudTexture(_shroudTexture);
			GameHost.Instance.GroundTerrain.SetShroudEnabled(shroudEnabled);
		}

		Vector2 worldMin = new Vector2(-halfW, -halfD);
		Vector2 worldSize = new Vector2(halfW * 2f, halfD * 2f);
		Realm.Godot.Utils.ModelShaderManager.SetShroudParameters(_shroudTexture, worldMin, worldSize, shroudEnabled);

		_shroudMeshMaterial?.SetShaderParameter("shroud_texture", _shroudTexture);
	}
}
