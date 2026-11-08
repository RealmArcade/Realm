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
	private EditorSaveState CaptureEditorState()
	{
		var camera = MainCamera;
		bool hasCameraState = GameContext.World != null && GameContext.World.IsAlive(GameContext.Simulation.WorldEntity) && GameContext.World.Has<CameraState>(GameContext.Simulation.WorldEntity);

		return new EditorSaveState
		{
			WaterProfileIndex = ActiveWaterProfileIndex,
			WaterMode = EditorWaterMode,
			WaterHeight = GameContext.EditorService.Tools.WaterHeight,
			BlockMode = EditorBlockMode,
			BlockLevelHeight = EditorBlockLevelHeight,
			ExactHeight = EditorExactHeight,
			PaintTextureIndex = GameContext.EditorService.Tools.PaintTextureIndex,
			CliffPaintTextureIndex = GameContext.EditorService.Tools.CliffPaintTextureIndex,
			ActiveTool = ActiveEditorTool,
			ActivePlaceId = ActivePlaceId,
			CopiedArea = GameContext.EditorService.Clipboard.CopiedArea,
			SelectionStart = GameContext.EditorService.Interaction.SelectionStart,
			SelectionEnd = GameContext.EditorService.Interaction.SelectionEnd,
			HasCameraPosition = camera != null,
			CameraPosition = camera != null ? camera.Position : default,
			HasCameraState = hasCameraState,
			CameraStateSnapshot = hasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity) : default
		};
	}

	private string[] GetSplatData()
	{
		return GameContext.SaveLoadService.GetSplatData(RequireGroundTerrain().SplatMap);
	}

	private (Entity Entity, float RotationY, float Scale)[] GetUnitsData()
	{
		var unitsData = new List<(Entity Entity, float RotationY, float Scale)>();
		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit) || !World.IsAlive(unit.Entity)) continue;

			float rotY = GetUnitPropRotationY(unit.Entity, unit.RotationDegrees.Y);
			float scale = GetUnitPropScale(unit.Entity, unit.Scale.X);
			unitsData.Add((unit.Entity, rotY, scale));
		}

		return unitsData.ToArray();
	}

	private (Entity Entity, float RotationY, float Scale)[] GetPropsData()
	{
		var propsData = new List<(Entity Entity, float RotationY, float Scale)>();
		foreach (var prop in AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop) || !World.IsAlive(prop.Entity)) continue;

			float rotY = GetUnitPropRotationY(prop.Entity, prop.RotationDegrees.Y);
			float scale = GetUnitPropScale(prop.Entity, prop.Scale.X);
			propsData.Add((prop.Entity, rotY, scale));
		}

		return propsData.ToArray();
	}

	private float GetUnitPropRotationY(Entity entity, float defaultRotY)
	{
		return GameContext.SaveLoadService.GetUnitPropRotationY(entity, defaultRotY, World);
	}

	private float GetUnitPropScale(Entity entity, float defaultScale)
	{
		return GameContext.SaveLoadService.GetUnitPropScale(entity, defaultScale, World);
	}

	private (Entity Entity, Vector3 Position, float RotationY, float Scale)[] GetDecalsData()
	{
		var decalsData = new List<(Entity Entity, Vector3 Position, float RotationY, float Scale)>();
		foreach (var decal in AllDecals)
		{
			if (!GodotObject.IsInstanceValid(decal) || !(decal is Decal3D decal3D)) continue;

			EnsureDecalEntity(decal3D);
			float rotY = GetUnitPropRotationY(decal3D.Entity, decal.RotationDegrees.Y);
			float scale = GetDecalScale(decal3D);
			Vector3 pos = GetDecalPosition(decal3D);

			decalsData.Add((decal3D.Entity, pos, rotY, scale));
		}

		return decalsData.ToArray();
	}

	private void EnsureDecalEntity(Decal3D decal3D)
	{
		decal3D.Entity = GameContext.EditorService.EnsureDecalEntity(decal3D.Entity, decal3D.DecalId, new Vector3(decal3D.Position.X, decal3D.Position.Y, decal3D.Position.Z), decal3D.RotationDegrees.Y, decal3D.Scale.X);
	}

	private float GetDecalScale(Decal3D decal3D)
	{
		return GameContext.SaveLoadService.GetDecalScale(decal3D.Entity, decal3D.Scale.X, decal3D.Size.X);
	}

	private Vector3 GetDecalPosition(Decal3D decal3D)
	{
		return GameContext.SaveLoadService.GetDecalPosition(decal3D.Entity, new Vector3(decal3D.Position.X, decal3D.Position.Y, decal3D.Position.Z));
	}

	private List<CoordinateSaveData> GetCoordinatesData()
	{
		return GameContext.SaveLoadService.GetCoordinatesData();
	}

	private string[] GetCliffSplatData()
	{
		if (RequireGroundTerrain().CliffSplatMap == null)
		{
			InitializeCliffSplatMap();
		}

		return SerializeCliffSplatMap();
	}

	private void InitializeCliffSplatMap()
	{
		if (GroundTerrain is not { } ground) return;
		ground.CliffSplatMap = GameContext.EditorService.GenerateDefaultCliffSplatMap(ground.Width, ground.Depth);
	}

	private string[] SerializeCliffSplatMap()
	{
		return GameContext.SaveLoadService.SerializeCliffSplatMap(RequireGroundTerrain().CliffSplatMap);
	}

	private void RestoreEditorStateAfterSave(EditorSaveState savedState)
	{
		RestoreCameraAfterSave(savedState);

		ActiveWaterProfileIndex = savedState.WaterProfileIndex;
		EditorWaterMode = savedState.WaterMode;
		EditorBlockMode = savedState.BlockMode;
		EditorBlockLevelHeight = savedState.BlockLevelHeight;
		EditorExactHeight = savedState.ExactHeight;

		GameContext.EditorService?.RestoreEditorStateDomain(
			savedState.WaterHeight,
			savedState.PaintTextureIndex,
			savedState.CliffPaintTextureIndex,
			savedState.CopiedArea,
			savedState.SelectionStart != null ? new System.Numerics.Vector3(savedState.SelectionStart.Value.X, 0, savedState.SelectionStart.Value.Y) : (System.Numerics.Vector3?)null,
			savedState.SelectionEnd != null ? new System.Numerics.Vector3(savedState.SelectionEnd.Value.X, 0, savedState.SelectionEnd.Value.Y) : (System.Numerics.Vector3?)null
		);

		GameContext.MapEditorHud?.SelectToolFromHotkey(savedState.ActiveTool);
		GameContext.MapEditorHud?.UpdateTextureLabels();
		GameContext.MapEditorHud?.RefreshWaterSwatches();
		GameContext.MapEditorHud?.UpdateMapNameHeader();
		GameContext.MapEditorHud?.ShowFeedback(TranslationServer.Translate("Map saved"));
	}

	private void RestoreCameraAfterSave(EditorSaveState savedState)
	{
		if (savedState.HasCameraPosition && MainCamera != null)
		{
			MainCamera.Position = savedState.CameraPosition;
		}

		bool hasCameraState = GameContext.World != null && GameContext.World.IsAlive(GameContext.Simulation.WorldEntity) && GameContext.World.Has<CameraState>(GameContext.Simulation.WorldEntity);
		if (savedState.HasCameraState && hasCameraState)
		{
			ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
			state = savedState.CameraStateSnapshot;
		}
	}
}
