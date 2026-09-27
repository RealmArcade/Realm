using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents a saved camera location and zoom level.
/// </summary>
internal struct CameraLocationSlot
{
	public Vector3 Position;
	public float ZoomLevel;
	public bool IsSet;
}

/// <summary>
///     Holds the state and configuration of the game camera.
/// </summary>
internal struct CameraState
{
	public float MoveSpeed;
	public float ZoomSpeed;
	public float MinZoom;
	public float MaxZoom;
	public float ZoomStep;
	public float EdgePanMargin;
	public bool EnableEdgePanning;
	public bool IsLocked;
	public bool IsFreeCamera;

	public float? LimitLeft;
	public float? LimitRight;
	public float? LimitTop;
	public float? LimitBottom;

	public float TargetHeight;
	public float CurrentHeight;
	public bool IsDraggingMouse;
	public Vector2 LastMousePosition;
	public float TargetYaw;
	public float CurrentYaw;
	public float TargetPitch;
	public float CurrentPitch;
	public bool IsTopDown;
	public float YawSwing;
	public float PitchSwing;

	public CameraLocationSlot LocationSlot1;
	public CameraLocationSlot LocationSlot2;
	public CameraLocationSlot LocationSlot3;
	public CameraLocationSlot LocationSlot4;
}
