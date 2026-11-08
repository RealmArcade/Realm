using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;

namespace Realm.Client;

[GlobalClass]
public partial class CameraControl : Camera3D
{
	/// <summary>
	/// The world these camera-state reads and writes need. Every member below is reached only
	/// while HasCameraState holds, but nullable analysis cannot carry that through a property
	/// getter, so the invariant is stated once instead of re-derived at fifty call sites.
	/// </summary>
	private static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("CameraControl read the world before one existed.");

	private bool HasCameraState
	{
		get
		{
			var world = GameContext.World;
			return GameContext.Host != null && world != null &&
				   world.IsAlive(GameContext.Simulation.WorldEntity) &&
				   world.Has<CameraState>(GameContext.Simulation.WorldEntity);
		}
	}

	public float MoveSpeed
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).MoveSpeed : 35.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.MoveSpeed = value;
			}
		}
	}

	public float ZoomSpeed
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).ZoomSpeed : 10.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.ZoomSpeed = value;
			}
		}
	}

	public float MinZoom
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).MinZoom : 10.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.MinZoom = value;
			}
		}
	}

	public float MaxZoom
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).MaxZoom : 90.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.MaxZoom = value;
			}
		}
	}

	public float ZoomStep
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).ZoomStep : 4.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.ZoomStep = value;
			}
		}
	}

	public float EdgePanMargin
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).EdgePanMargin : 20.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.EdgePanMargin = value;
			}
		}
	}

	public bool EnableEdgePanning
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).EnableEdgePanning : true;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.EnableEdgePanning = value;
			}
		}
	}

	public bool IsLocked
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).IsLocked : false;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.IsLocked = value;
			}
		}
	}

	public bool IsFreeCamera
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).IsFreeCamera : _isFreeCamera;
		set
		{
			_isFreeCamera = value;
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.IsFreeCamera = value;
			}
		}
	}

	private bool _isFreeCamera = false;

	public Node3D? FollowTarget { get; set; }

	public float? LimitLeft
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).LimitLeft : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.LimitLeft = value;
			}
		}
	}

	public float? LimitRight
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).LimitRight : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.LimitRight = value;
			}
		}
	}

	public float? LimitTop
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).LimitTop : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.LimitTop = value;
			}
		}
	}

	public float? LimitBottom
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).LimitBottom : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.LimitBottom = value;
			}
		}
	}

	public float TargetHeight
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).TargetHeight : 35.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.TargetHeight = value;
			}
		}
	}

	public float CurrentHeight
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).CurrentHeight : 35.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.CurrentHeight = value;
			}
		}
	}

	private bool _isDraggingMouse
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).IsDraggingMouse : false;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.IsDraggingMouse = value;
			}
		}
	}

	private Vector2 _lastMousePosition
	{
		get
		{
			if (HasCameraState)
			{
				var pos = World.Get<CameraState>(GameContext.Simulation.WorldEntity).LastMousePosition;
				return new Vector2(pos.X, pos.Y);
			}

			return Vector2.Zero;
		}
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.LastMousePosition = new System.Numerics.Vector2(value.X, value.Y);
			}
		}
	}

	private float _targetYaw
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).TargetYaw : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.TargetYaw = value;
			}
		}
	}

	private float _currentYaw
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).CurrentYaw : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.CurrentYaw = value;
			}
		}
	}

	private float _targetPitch
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).TargetPitch : -55.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.TargetPitch = value;
			}
		}
	}

	private float _currentPitch
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).CurrentPitch : -55.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.CurrentPitch = value;
			}
		}
	}

	private bool _isTopDown
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).IsTopDown : false;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.IsTopDown = value;
			}
		}
	}

	private float _yawSwing
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).YawSwing : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.YawSwing = value;
			}
		}
	}

	private float _pitchSwing
	{
		get => HasCameraState ? World.Get<CameraState>(GameContext.Simulation.WorldEntity).PitchSwing : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
				state.PitchSwing = value;
			}
		}
	}

	private bool _isAltRightDragging = false;

}