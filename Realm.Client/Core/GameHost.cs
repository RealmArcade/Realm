using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Realm.Ecs;
using Realm.Ecs.Replication;
using DotRecast.Detour;
using Godot;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = Godot.Environment;
using FileAccess = System.IO.FileAccess;
using Realm.Shared.Abilities;

namespace Realm.Client.Core;

[GlobalClass]
public partial class GameHost : Node3D
{
	/// <summary>
	/// The world this host drives. Every simulation, entity and replication path below runs
	/// only while a world exists, so reaching one without a world is a programming error;
	/// stating that once keeps each path from re-deriving it from a property getter.
	/// </summary>
	private static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("GameHost used before a world existed.");

	public GameHost()
	{
		GameContext.RegisterHost(this);
	}

	public CameraControl MainCamera { get; set; } = null!;
	public Node MainNode { get; set; } = null!;

	public int LocalPlayerIndex => GameContext.PerspectiveFilter.ControlledPlayerIndex;

		public (int minX, int minZ, int maxX, int maxZ) GetClampedCoordinateBounds(EditorCoordinate coord)
		{
			if (GroundTerrain == null) return (0, 0, 0, 0);
			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;
			float quadSize = GroundTerrain.QuadSize;

			int minX = Mathf.Clamp((int)Mathf.Round(coord.MinX / quadSize + width / 2.0f), 0, width);
			int minZ = Mathf.Clamp((int)Mathf.Round(coord.MinZ / quadSize + depth / 2.0f), 0, depth);
			int maxX = Mathf.Clamp((int)Mathf.Round(coord.MaxX / quadSize + width / 2.0f), 0, width);
			int maxZ = Mathf.Clamp((int)Mathf.Round(coord.MaxZ / quadSize + depth / 2.0f), 0, depth);

			return (minX, minZ, maxX, maxZ);
		}

	private float _fDelta { get; set; }
	private WorldEnvironment? _cachedWorldEnvironment;
	private UnitOcclusionOutlineService _unitOcclusionOutlineService = null!;

	private bool _multiplayerActive => Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;

	private int _localPeerId
	{
		get => GameContext.NetworkService.LocalPeerId;
		set
		{
			if (GameContext.NetworkService != null) GameContext.NetworkService.LocalPeerId = value;
		}
	}

	public int LocalPeerId => _localPeerId;

	private const string SpectatorTeamName = "Spectator";

	private void ConfigurePerspective()
	{
		var filter = GameContext.PerspectiveFilter;

		if (GameContext.ReplayPlayback.IsPlayingReplay)
		{
			filter.ConfigureSpectator();
			return;
		}

		// The authority's seat decides what this client plays as, and it can arrive after this scene does: until it
		// does, the screen stays with the first player and moves to the seat when the authority answers.
		ClientServerSession? session = GameContext.ServerSession;
		if (session != null)
		{
			filter.ConfigureParticipant(session.LocalPlayerSlot > 0 ? session.LocalPlayerSlot : 1);
			return;
		}

		var lobbyPlayers = GameContext.Lobby?.PlayerList;
		if (lobbyPlayers == null || lobbyPlayers.Count == 0)
		{
			filter.ConfigureParticipant(1);
			return;
		}

		int peerId = _localPeerId;
		var entry = _multiplayerActive
			? lobbyPlayers.Find(x => x.PeerId == peerId)
			: lobbyPlayers.Find(x => x.PeerId >= 0 && x.Team != SpectatorTeamName);

		if (entry == null)
		{
			if (_multiplayerActive)
				throw new InvalidOperationException($"Local peer {peerId} is not present in the lobby player list.");

			filter.ConfigureParticipant(1);
			return;
		}

		if (entry.Team == SpectatorTeamName) filter.ConfigureSpectator();
		else filter.ConfigureParticipant(entry.Slot);
	}

	private int _nextCommandId
	{
		get => GameContext.World?.GetFieldOrDefault<NetworkState, int>(GameContext.Simulation.WorldEntity, s => s.NextCommandId, 1) ?? 1;
		set => GameContext.World?.Mutate(GameContext.Simulation.WorldEntity, (ref NetworkState s) => s.NextCommandId = value);
	}

	private int _snapshotSequence
	{
		get => GameContext.World?.GetFieldOrDefault<NetworkState, int>(GameContext.Simulation.WorldEntity, s => s.SnapshotSequence) ?? 0;
		set => GameContext.World?.Mutate(GameContext.Simulation.WorldEntity, (ref NetworkState s) => s.SnapshotSequence = value);
	}

	private int _lastReceivedBaselineSeq
	{
		get => GameContext.World?.GetFieldOrDefault<NetworkState, int>(GameContext.Simulation.WorldEntity, s => s.LastReceivedBaselineSeq, -1) ?? -1;
		set => GameContext.World?.Mutate(GameContext.Simulation.WorldEntity, (ref NetworkState s) => s.LastReceivedBaselineSeq = value);
	}

	private bool _hasReceivedInitialBaseline
	{
		get => GameContext.World?.GetFieldOrDefault<NetworkState, bool>(GameContext.Simulation.WorldEntity, s => s.HasReceivedInitialBaseline) ?? false;
		set => GameContext.World?.Mutate(GameContext.Simulation.WorldEntity, (ref NetworkState s) => s.HasReceivedInitialBaseline = value);
	}

	private int _lastAppliedSnapshotSequence
	{
		get => GameContext.World?.GetFieldOrDefault<NetworkState, int>(GameContext.Simulation.WorldEntity, s => s.LastAppliedSnapshotSequence, -1) ?? -1;
		set => GameContext.World?.Mutate(GameContext.Simulation.WorldEntity, (ref NetworkState s) => s.LastAppliedSnapshotSequence = value);
	}

	private ulong _lastSnapshotReceivedTime
	{
		get => GameContext.NetworkService?.LastSnapshotReceivedTime ?? 0;
		set
		{
			if (GameContext.NetworkService != null) GameContext.NetworkService.LastSnapshotReceivedTime = value;
		}
	}

	private bool _wasClientInMultiplayer => GameContext.NetworkService?.WasClientInMultiplayer ?? false;
	public bool IsConnectionLost => GameContext.NetworkService?.IsConnectionLost ?? false;

	private Dictionary<int, Entity> _peerIdToPlayerEntityMap
		=> World.GetFieldOrDefault<NetworkMappingState, Dictionary<int, Entity>>(GameContext.Simulation.WorldEntity, s => s.PeerIdToPlayerEntityMap);

	private Dictionary<int, Entity> _serverToClientEntityMap
		=> World.GetFieldOrDefault<NetworkMappingState, Dictionary<int, Entity>>(GameContext.Simulation.WorldEntity, s => s.ServerToClientEntityMap);

	private Dictionary<int, int> _clientToServerEntityMap
		=> World.GetFieldOrDefault<NetworkMappingState, Dictionary<int, int>>(GameContext.Simulation.WorldEntity, s => s.ClientToServerEntityMap);

	public List<Unit3D> SelectedUnits { get; set; } = new List<Unit3D>();
	private readonly List<Unit3D> _activeImpulseUnits = new();

	public void RegisterActiveImpulseUnit(Unit3D unit)
	{
		if (unit != null && !_activeImpulseUnits.Contains(unit))
		{
			_activeImpulseUnits.Add(unit);
		}
	}

	private void UpdateSelectedAndImpulseUnitVisuals(float fDelta)
	{
		for (int i = 0; i < SelectedUnits.Count; i++)
		{
			var selected = SelectedUnits[i];
			if (GodotObject.IsInstanceValid(selected))
			{
				selected.UpdateSelectedVisuals(fDelta);
			}
		}

		for (int i = _activeImpulseUnits.Count - 1; i >= 0; i--)
		{
			var unit = _activeImpulseUnits[i];
			if (!GodotObject.IsInstanceValid(unit) || !unit.HasActiveImpulse)
			{
				_activeImpulseUnits.RemoveAt(i);
				continue;
			}

			unit.UpdateImpulseVisuals(fDelta);
		}
	}

	public List<Unit3D> AllUnits { get; set; } = new List<Unit3D>();
	public List<Prop3D> AllProps { get; set; } = new List<Prop3D>();
	public List<Decal> AllDecals { get; set; } = new List<Decal>();
	public List<ProceduralVfxInstance3D> AllVfx { get; set; } = new List<ProceduralVfxInstance3D>();
	public List<LootItem3D> AllLootItems { get; set; } = new();
	
	public static Dictionary<Entity, Unit3D> EntityToUnit3D { get; set; } = new();
	public static Dictionary<int, Unit3D> EntityIdToUnit3D { get; set; } = new();
	public static Dictionary<int, List<Unit3D>> PlayerToUnits { get; set; } = new();
	public static Dictionary<Entity, Prop3D> EntityToProp3D { get; set; } = new();
	public static Dictionary<Entity, ProceduralVfxInstance3D> EntityToVfx3D { get; set; } = new();

	public static bool TryGetUnit3D(Entity entity, [NotNullWhen(true)] out Unit3D? unit)
	{
		return EntityToUnit3D.TryGetValue(entity, out unit);
	}

	public static bool TryGetProp3D(Entity entity, [NotNullWhen(true)] out Prop3D? prop)
	{
		return EntityToProp3D.TryGetValue(entity, out prop);
	}

	public static bool TryGetVfx3D(Entity entity, [NotNullWhen(true)] out ProceduralVfxInstance3D? vfx)
	{
		return EntityToVfx3D.TryGetValue(entity, out vfx);
	}

	public Entity GetPlayerEntityForPlayerIndex(int playerIndex)
	{
		if (GameContext.Simulation.WorldEntity != Entity.Null && GameContext.World != null && World.IsAlive(GameContext.Simulation.WorldEntity) && World.Has<NetworkMappingState>(GameContext.Simulation.WorldEntity))
		{
			var playerEntities = World.Get<NetworkMappingState>(GameContext.Simulation.WorldEntity).PlayerEntities;
			if (playerEntities != null && playerIndex >= 0 && playerIndex < playerEntities.Length)
			{
				var pe = playerEntities[playerIndex];
				if (pe != Entity.Null && World.IsAlive(pe))
					return pe;
			}
		}
		return TryGetMultiplayerEntity(playerIndex);
	}

	private Entity TryGetMultiplayerEntity(int playerIndex)
	{
		if (!_multiplayerActive || GameContext.Lobby == null || GameContext.Lobby.PlayerList.Count <= 0) return Entity.Null;
		var p = GameContext.Lobby.PlayerList.Find(x => x.Slot == playerIndex);
		if (p == null || _peerIdToPlayerEntityMap?.TryGetValue(p.PeerId, out var pe) != true || !World.IsAlive(pe)) return Entity.Null;
		return pe;
	}

	private int _replayTickCounter
	{
		get => GameContext.World?.GetFieldOrDefault<ReplayState, int>(GameContext.Simulation.WorldEntity, s => s.ReplayTickCounter) ?? 0;
		set => GameContext.World?.Mutate<ReplayState>(GameContext.Simulation.WorldEntity, (ref ReplayState s) => s.ReplayTickCounter = value);
	}

	public string? ActiveSpellTargeting
	{
		get => GameContext.InputService?.ActiveSpellTargeting;
		set
		{
			if (GameContext.InputService != null) GameContext.InputService.ActiveSpellTargeting = value;
		}
	}

	public string? ActiveCommandTargeting
	{
		get => GameContext.InputService?.ActiveCommandTargeting;
		set
		{
			if (GameContext.InputService != null) GameContext.InputService.ActiveCommandTargeting = value;
		}
	}

	public Prop3D? SelectedProp { get; set; }

	private Node3D? _sharedPropSelectionRing;
	private Node3D? _sharedPropHoverRing;
	public Entity SelectedPropEntity { get; private set; } = Entity.Null;
	public string SelectedPropId { get; private set; } = "";
	public Entity HoveredPropEntity { get; private set; } = Entity.Null;
	public string HoveredPropId { get; private set; } = "";

	public void SetSelectedPropEntity(Entity entity, string propId)
	{
		SelectedPropEntity = entity;
		SelectedPropId = propId;
		if (TryUpdatePropRingState(entity, out var pos, out var scale))
		{
			EnsurePropSelectionRing();
			if (_sharedPropSelectionRing != null)
			{
				_sharedPropSelectionRing.Position = pos;
				_sharedPropSelectionRing.Scale = Vector3.One * scale;
				_sharedPropSelectionRing.Visible = true;
			}
		}
		else
		{
			ClearSelectedPropEntity();
		}
	}

	public void ClearSelectedPropEntity()
	{
		SelectedPropEntity = Entity.Null;
		SelectedPropId = "";
		if (_sharedPropSelectionRing != null)
		{
			_sharedPropSelectionRing.Visible = false;
		}
	}

	public void UpdateSelectedPropRingPosition(Vector3 pos)
	{
		if (_sharedPropSelectionRing != null && _sharedPropSelectionRing.Visible)
		{
			_sharedPropSelectionRing.Position = pos;
		}
	}

	public void SetHoveredPropEntity(Entity entity, string propId)
	{
		HoveredPropEntity = entity;
		HoveredPropId = propId;
		if (TryUpdatePropRingState(entity, out var pos, out var scale))
		{
			EnsurePropHoverRing();
			if (_sharedPropHoverRing != null)
			{
				_sharedPropHoverRing.Position = pos;
				_sharedPropHoverRing.Scale = Vector3.One * scale;
				_sharedPropHoverRing.Visible = true;
			}
		}
		else
		{
			if (_sharedPropHoverRing != null)
			{
				_sharedPropHoverRing.Visible = false;
			}
		}
	}

	private bool TryUpdatePropRingState(Entity entity, out Vector3 pos, out float scale)
	{
		pos = Vector3.Zero;
		scale = 1f;
		if (entity != Entity.Null && GameContext.World != null && World.IsAlive(entity) && World.Has<Position>(entity))
		{
			var posComp = World.Get<Position>(entity);
			pos = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z);
			scale = World.Has<ModelScale>(entity) ? World.Get<ModelScale>(entity).Value : 1f;
			return true;
		}
		return false;
	}

	private void EnsurePropSelectionRing()
	{
		if (_sharedPropSelectionRing != null && GodotObject.IsInstanceValid(_sharedPropSelectionRing)) return;
		_sharedPropSelectionRing = CreateRingNode(new Color(0.15f, 0.65f, 1.0f, 0.8f));
		_sharedPropSelectionRing.Name = "SharedPropSelectionRing";
		AddChild(_sharedPropSelectionRing);
		_sharedPropSelectionRing.Visible = false;
	}

	private void EnsurePropHoverRing()
	{
		if (_sharedPropHoverRing != null && GodotObject.IsInstanceValid(_sharedPropHoverRing)) return;
		_sharedPropHoverRing = CreateRingNode(new Color(1.0f, 0.85f, 0.2f, 0.6f));
		_sharedPropHoverRing.Name = "SharedPropHoverRing";
		AddChild(_sharedPropHoverRing);
		_sharedPropHoverRing.Visible = false;
	}

	private Node3D CreateRingNode(Color color)
	{
		var meshInst = new MeshInstance3D();
		meshInst.Name = "MeshInst_MeshInstance3D";
		var torus = new TorusMesh
		{
			InnerRadius = 0.95f,
			OuterRadius = 1.05f
		};
		meshInst.Mesh = torus;
		var mat = new StandardMaterial3D
		{
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			EmissionEnabled = true,
			Emission = color * 0.5f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		meshInst.MaterialOverride = mat;
		return meshInst;
	}
}
