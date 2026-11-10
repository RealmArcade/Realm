using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Realm.Ecs;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Terrain;
using Realm.Shared.Textures;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;


public partial class GameHost
{
	private void InstantiateMapSpawnRequests()
	{
		if (_mapWorldLoadTerrainOnly) return;

		GameContext.SaveLoadService.IsLoadingMap = true;
		try
		{
			ProcessSpawnRequests();
		}
		finally
		{
			GameContext.SaveLoadService.IsLoadingMap = false;
		}
	}

	private void ProcessSpawnRequests()
	{
		ProcessUnitSpawnRequests();
		ProcessPropSpawnRequests();
		GameContext.PropMultiMesh?.RebuildAll();
		ProcessDecalSpawnRequests();
	}

	private void ProcessUnitSpawnRequests()
	{
		GameContext.UnitSpawnService.ProcessUnitSpawnRequestsDomain((unitId, pos, rotY, scale, player) => {
			SpawnUnitExternal(unitId, new Godot.Vector3(pos.X, pos.Y, pos.Z), rotY, scale, player);
		});
	}

	private void ProcessPropSpawnRequests()
	{
		GameContext.UnitSpawnService.ProcessPropSpawnRequests((propId, pos, rotY, scale) => {
			SpawnPropExternalWithParams(propId, new Godot.Vector3(pos.X, pos.Y, pos.Z), rotY, scale);
		});
	}

	private void ProcessDecalSpawnRequests()
	{
		GameContext.UnitSpawnService.ProcessDecalSpawnRequests((decalId, pos, rot, scale) => {
			SpawnDecalExternalWithParams(decalId, new Godot.Vector3(pos.X, pos.Y, pos.Z), new Godot.Vector3(rot.X, rot.Y, rot.Z), scale);
		});
	}

	private void RestoreLoadedCoordinates()
	{
		GameContext.EditorService.RestoreLoadedCoordinates();
		RebuildAllCoordinatePersistentMeshes();
		GameContext.MapEditorHud?.RefreshCoordinateListExternal();
	}
}
