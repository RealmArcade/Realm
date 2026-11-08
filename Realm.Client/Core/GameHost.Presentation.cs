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
public partial class GameHost
{
	public void CreateFloatingTextInternal(string text, Vector3 position, Color color, float duration)
	{
		Callable.From(() =>
		{
			var label = new Label3D();
			label.Name = "FloatingText_Label3D";
			label.Text = text;
			label.Modulate = color;
			label.OutlineModulate = Colors.Black;
			label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
			label.Position = new Vector3(position.X, position.Y + 1.5f, position.Z);
			label.FontSize = 48;
			AddChild(label);

			var tween = CreateTween();
			if (tween != null)
			{
				tween.SetParallel(true);
				tween.TweenProperty(label, "position", label.Position + new Vector3(0, 2.0f, 0), duration);
				tween.TweenProperty(label, "modulate:a", 0.0f, duration);
				tween.Chain().TweenCallback(Callable.From(label.QueueFree));
			}
		}).CallDeferred();
	}

	private Dictionary<int, Label3D> _staticTextLabels { get; set; } = new();
	private readonly Dictionary<int, StaticTextState> _staticTextStates = new();
	private readonly HashSet<int> _dirtyStaticTextHandles = new();
	private readonly HashSet<int> _destroyedStaticTextHandles = new();
	private int _nextStaticTextHandle { get; set; } = 1;
	private int _staticTextGeneration { get; set; } = 0;

	public void ClearStaticText()
	{
		_staticTextGeneration++;
		foreach (var label in _staticTextLabels.Values)
		{
			if (GodotObject.IsInstanceValid(label))
			{
				label.QueueFree();
			}
		}
		_staticTextLabels.Clear();
		_nextStaticTextHandle = 1;
		SharedShadowGradient = null;
	}

	public static void ClearCache()
	{
		SharedShadowGradient = null;
	}

	public void PanCameraInternal(Vector3 position, float duration)
	{
		Callable.From(() =>
		{
			var camera = MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");
			if (camera == null) return;

			var targetPos = new Vector3(position.X, camera.Position.Y, position.Z + 15.0f);
			if (duration <= 0.05f)
			{
				camera.Position = targetPos;
			}
			else
			{
				var tween = CreateTween();
				tween?.TweenProperty(camera, "position", targetPos, duration);
			}
		}).CallDeferred();
	}

	public void ShakeCameraInternal(float intensity, float duration)
	{
		Callable.From(() =>
		{
			var camera = MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");
			if (camera == null) return;

			var startPos = camera.Position;
			var tween = CreateTween();
			if (tween == null) return;
			float stepDuration = duration / 20f;
			for (int i = 0; i < 20; i++)
			{
				var offset = new Vector3(
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1),
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1),
					(float)(GD.RandRange(-1.0, 1.0) * intensity * 0.1)
				);
				tween.TweenProperty(camera, "position", startPos + offset, stepDuration);
			}

			tween.TweenProperty(camera, "position", startPos, stepDuration);
		}).CallDeferred();
	}

	private bool _defaultCameraBoundsCaptured = false;
	public float? DefaultCameraBoundsLeft { get; private set; }
	public float? DefaultCameraBoundsRight { get; private set; }
	public float? DefaultCameraBoundsTop { get; private set; }
	public float? DefaultCameraBoundsBottom { get; private set; }
	private readonly Dictionary<int, (float MinX, float MinZ, float MaxX, float MaxZ)> _playerCameraBoundsOverrides = new();

	public void CaptureDefaultCameraBounds()
	{
		if (_defaultCameraBoundsCaptured) return;
		if (GameContext.Simulation.WorldEntity != Entity.Null && GameContext.World != null && World.IsAlive(GameContext.Simulation.WorldEntity) && World.Has<CameraState>(GameContext.Simulation.WorldEntity))
		{
			ref var cam = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
			DefaultCameraBoundsLeft = cam.LimitLeft;
			DefaultCameraBoundsRight = cam.LimitRight;
			DefaultCameraBoundsTop = cam.LimitTop;
			DefaultCameraBoundsBottom = cam.LimitBottom;
			_defaultCameraBoundsCaptured = true;
		}
	}

	private void EnsureDefaultCameraBoundsCaptured(CameraControl? camera)
	{
		if (_defaultCameraBoundsCaptured) return;
		CaptureDefaultCameraBounds();
		if (!_defaultCameraBoundsCaptured && camera != null)
		{
			DefaultCameraBoundsLeft = camera.LimitLeft;
			DefaultCameraBoundsRight = camera.LimitRight;
			DefaultCameraBoundsTop = camera.LimitTop;
			DefaultCameraBoundsBottom = camera.LimitBottom;
			_defaultCameraBoundsCaptured = true;
		}
	}

	public void SetCameraBoundsInternal(float minX, float minZ, float maxX, float maxZ)
	{
		Callable.From(() =>
		{
			var camera = MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");
			if (camera != null)
			{
				EnsureDefaultCameraBoundsCaptured(camera);
				camera.LimitLeft = minX;
				camera.LimitRight = maxX;
				camera.LimitTop = minZ;
				camera.LimitBottom = maxZ;
				camera.ClampToValidGameCamera();
			}
		}).CallDeferred();
	}

	public void ResetCameraBoundsInternal()
	{
		Callable.From(() =>
		{
			var camera = MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");
			if (camera != null)
			{
				if (_defaultCameraBoundsCaptured)
				{
					camera.LimitLeft = DefaultCameraBoundsLeft;
					camera.LimitRight = DefaultCameraBoundsRight;
					camera.LimitTop = DefaultCameraBoundsTop;
					camera.LimitBottom = DefaultCameraBoundsBottom;
				}
				camera.ClampToValidGameCamera();
			}
		}).CallDeferred();
	}

	public void RecordPlayerCameraBoundsOverride(int playerIndex, float minX, float minZ, float maxX, float maxZ)
	{
		_playerCameraBoundsOverrides[playerIndex] = (minX, minZ, maxX, maxZ);
	}

	public void ClearPlayerCameraBoundsOverride(int playerIndex)
	{
		_playerCameraBoundsOverrides.Remove(playerIndex);
	}

	public Dictionary<HudElement, bool> LocalHudVisibilityOverrides { get; } = new();
	private readonly Dictionary<int, Dictionary<HudElement, bool>> _playerHudVisibilityOverrides = new();

	public void RecordPlayerHudVisibility(int playerIndex, HudElement element, bool visible)
	{
		if (!_playerHudVisibilityOverrides.TryGetValue(playerIndex, out var dict))
		{
			dict = new Dictionary<HudElement, bool>();
			_playerHudVisibilityOverrides[playerIndex] = dict;
		}
		dict[element] = visible;
	}

	public void SetHudElementVisibleInternal(HudElement element, bool visible)
	{
		LocalHudVisibilityOverrides[element] = visible;
		Callable.From(() =>
		{
			GameContext.InGameHud?.SetHudElementVisible(element, visible);
		}).CallDeferred();
	}

	public void SelectUnitInternal(UnitId unit)
	{
		Callable.From(() =>
		{
			for (int i = 0; i < AllUnits.Count; i++)
			{
				var u = AllUnits[i];
				if (GodotObject.IsInstanceValid(u) && u.Entity.Id == unit.Value && GameContext.World != null && World.IsAlive(u.Entity))
				{
					ClearSelection();
					SelectUnit(u);
					GameContext.InGameHud?.RefreshUI(SelectedUnits);
					break;
				}
			}
		}).CallDeferred();
	}

	public void ClearSelectionInternal()
	{
		Callable.From(() =>
		{
			ClearSelection();
			GameContext.InGameHud?.RefreshUI(SelectedUnits);
		}).CallDeferred();
	}

	public event Action<UnitId, int>? OnUnitEnterZone;
}
