using System;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Shared.Maps;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

/// <summary>
/// The editor's own state as it lives on its entity: the block and water tool settings, the camera
/// bounds, the skybox and the unsaved-changes flag. Every member here is a straight read or write of
/// that <see cref="EditorState"/> component, defaulting the way the editor opens a fresh map.
/// </summary>
internal static class EditorEntityState
{
	private static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("EditorService used before a world existed.");

	public static bool GetBlockMode(Entity worldEntity)
	{
		return World.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.BlockMode, true);
	}

	public static void SetBlockMode(Entity worldEntity, bool value)
	{
		World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.BlockMode = value);
	}

	public static float GetBlockLevelHeight(Entity worldEntity)
	{
		return World.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.BlockLevelHeight, 3.0f);
	}

	public static void SetBlockLevelHeight(Entity worldEntity, float value)
	{
		float clamped = Math.Clamp(value, 0.0f, 50.0f);
		World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.BlockLevelHeight = clamped);
	}

	public static WaterType GetWaterMode(Entity worldEntity)
	{
		return World.GetFieldOrDefault<EditorState, WaterType>(worldEntity, s => s.WaterMode, WaterType.None);
	}

	public static void SetWaterMode(Entity worldEntity, WaterType value)
	{
		if (GameContext.World != null && World.IsAlive(worldEntity))
		{
			if (World.Has<EditorState>(worldEntity))
			{
				ref var state = ref World.Get<EditorState>(worldEntity);
				state.WaterMode = value;
			}
			else
			{
				World.Add(worldEntity, new EditorState(true, 3.0f, -95.0f, 95.0f, -95.0f, 125.0f, "", false, MirrorMode.None, value, 0));
			}
		}
	}

	public static byte GetWaterProfileIndex(Entity worldEntity)
	{
		return World.GetFieldOrDefault<EditorState, byte>(worldEntity, s => s.WaterProfileIndex, (byte)0);
	}

	public static void SetWaterProfileIndex(Entity worldEntity, byte value)
	{
		if (GameContext.World != null && World.IsAlive(worldEntity))
		{
			if (World.Has<EditorState>(worldEntity))
			{
				ref var state = ref World.Get<EditorState>(worldEntity);
				state.WaterProfileIndex = value;
			}
			else
			{
				World.Add(worldEntity, new EditorState(true, 3.0f, -95.0f, 95.0f, -95.0f, 125.0f, "", false, MirrorMode.None, WaterType.None, value));
			}
		}
	}

	public static float GetCameraBoundsLeft(Entity worldEntity)
	{
		return World.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsLeft, -95.0f);
	}

	public static void SetCameraBoundsLeft(Entity worldEntity, float value)
	{
		World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsLeft = value);
	}

	public static float GetCameraBoundsRight(Entity worldEntity)
	{
		return World.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsRight, 95.0f);
	}

	public static void SetCameraBoundsRight(Entity worldEntity, float value)
	{
		World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsRight = value);
	}

	public static float GetCameraBoundsTop(Entity worldEntity)
	{
		return World.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsTop, -95.0f);
	}

	public static void SetCameraBoundsTop(Entity worldEntity, float value)
	{
		World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsTop = value);
	}

	public static float GetCameraBoundsBottom(Entity worldEntity)
	{
		return World.GetFieldOrDefault<EditorState, float>(worldEntity, s => s.CameraBoundsBottom, 125.0f);
	}

	public static void SetCameraBoundsBottom(Entity worldEntity, float value)
	{
		World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.CameraBoundsBottom = value);
	}

	public static string GetSkyboxPath(Entity worldEntity)
	{
		return World.GetFieldOrDefault<EditorState, string>(worldEntity, s => s.SkyboxPath, "");
	}

	public static void SetSkyboxPath(Entity worldEntity, string value)
	{
		World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.SkyboxPath = value);
	}

	public static bool GetHasUnsavedChanges(Entity worldEntity)
	{
		return World.GetFieldOrDefault<EditorState, bool>(worldEntity, s => s.HasUnsavedChanges, false);
	}

	public static void SetHasUnsavedChanges(Entity worldEntity, bool value)
	{
		if (value) EditorService.NoteEditorChange();
		World.Mutate<EditorState>(worldEntity, (ref EditorState s) => s.HasUnsavedChanges = value);
	}

	internal static void SetCameraBounds(Entity worldEntity, MapCameraBounds cameraBounds)
	{
		if (GameContext.World == null || !World.IsAlive(worldEntity)) return;

		World.Mutate<EditorState>(worldEntity, (ref EditorState state) =>
		{
			state.CameraBoundsLeft = cameraBounds.Left;
			state.CameraBoundsRight = cameraBounds.Right;
			state.CameraBoundsTop = cameraBounds.Top;
			state.CameraBoundsBottom = cameraBounds.Bottom;
		});

		if (World.Has<CameraState>(worldEntity))
		{
			World.Mutate<CameraState>(worldEntity, (ref CameraState cameraState) =>
			{
				cameraState.LimitLeft = cameraBounds.Left;
				cameraState.LimitRight = cameraBounds.Right;
				cameraState.LimitTop = cameraBounds.Top;
				cameraState.LimitBottom = cameraBounds.Bottom;
			});
		}
	}
}
