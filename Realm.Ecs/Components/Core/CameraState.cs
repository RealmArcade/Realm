using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Holds the state and configuration of the game camera.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct CameraState
{
	public float MoveSpeed;
	public float ZoomSpeed;
	public float MinZoom;
	public float MaxZoom;
	public float ZoomStep;
	public float EdgePanMargin;
	public byte EnableEdgePanningByte;

	public bool EnableEdgePanning
	{
		get => EnableEdgePanningByte != 0;
		set => EnableEdgePanningByte = value ? (byte)1 : (byte)0;
	}

	public byte IsLockedByte;

	public bool IsLocked
	{
		get => IsLockedByte != 0;
		set => IsLockedByte = value ? (byte)1 : (byte)0;
	}

	public byte IsFreeCameraByte;

	public bool IsFreeCamera
	{
		get => IsFreeCameraByte != 0;
		set => IsFreeCameraByte = value ? (byte)1 : (byte)0;
	}

	public float? LimitLeft;
	public float? LimitRight;
	public float? LimitTop;
	public float? LimitBottom;

	public float TargetHeight;
	public float CurrentHeight;
	public byte IsDraggingMouseByte;

	public bool IsDraggingMouse
	{
		get => IsDraggingMouseByte != 0;
		set => IsDraggingMouseByte = value ? (byte)1 : (byte)0;
	}

	public Vector2 LastMousePosition;
	public float TargetYaw;
	public float CurrentYaw;
	public float TargetPitch;
	public float CurrentPitch;
	public byte IsTopDownByte;

	public bool IsTopDown
	{
		get => IsTopDownByte != 0;
		set => IsTopDownByte = value ? (byte)1 : (byte)0;
	}

	public float YawSwing;
	public float PitchSwing;

	public CameraLocationSlot LocationSlot1;
	public CameraLocationSlot LocationSlot2;
	public CameraLocationSlot LocationSlot3;
	public CameraLocationSlot LocationSlot4;
}