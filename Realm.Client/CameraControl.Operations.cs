using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;

namespace Realm.Client;

public partial class CameraControl : Camera3D
{
	public void ResetCamera()
	{
		if (IsLocked) return;
		_targetYaw = 0.0f;
		_targetPitch = -55.0f;
		TargetHeight = 35.0f;
		_isTopDown = false;
		FollowTarget = null;
		if (IsFreeCamera)
		{
			SetFreeCamera(false);
		}
	}

	public void ToggleFreeCamera()
	{
		SetFreeCamera(!IsFreeCamera);
	}

	public void SetFreeCamera(bool enabled)
	{
		if (IsFreeCamera == enabled) return;
		IsFreeCamera = enabled;

		if (!enabled)
		{
			ClampToValidGameCamera();
		}

		GameContext.MapEditorHud?.UpdateFreeCameraExternal(enabled);
	}

	public void ClampToValidGameCamera()
	{
		if (Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
		}

		_isAltRightDragging = false;
		_isDraggingMouse = false;

		if (GameContext.Host != null && GameContext.SessionState.IsMapEditorMode)
		{
			GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(false);
		}

		_targetPitch = _isTopDown ? -90.0f : -55.0f;
		_currentPitch = _targetPitch;
		_targetYaw = 0.0f;
		_currentYaw = 0.0f;
		_yawSwing = 0.0f;
		_pitchSwing = 0.0f;
		RotationDegrees = new Vector3(_currentPitch, _currentYaw, 0.0f);

		float terrainHeight = 0.0f;
		if (GameContext.Host?.GroundTerrain != null)
		{
			GameContext.Host.GroundTerrain.GetHeightAndNormal(Position.X, Position.Z, out terrainHeight, out _);
		}

		float minAllowedHeight = terrainHeight + MinZoom;
		float maxAllowedHeight = GetMaxZoom();

		TargetHeight = Mathf.Clamp(TargetHeight, minAllowedHeight, maxAllowedHeight);
		CurrentHeight = Mathf.Clamp(CurrentHeight, minAllowedHeight, maxAllowedHeight);

		Vector3 clampedPos = Position;
		clampedPos.Y = CurrentHeight;

		bool isEditor = GameContext.Host != null && GameContext.SessionState.IsMapEditorMode;
		ApplyCameraBounds(ref clampedPos, isEditor);

		Position = clampedPos;
	}

	public void ToggleTopDown()
	{
		_isTopDown = !_isTopDown;
		_targetPitch = _isTopDown ? -90.0f : -55.0f;
	}

	public bool IsTopDown()
	{
		return _isTopDown;
	}

	public void Rotate90Degrees()
	{
		_targetYaw = (_targetYaw + 90.0f) % 360.0f;
	}

	public void CycleZoom()
	{
		if (IsLocked) return;

		if (TargetHeight < 25.0f)
		{
			TargetHeight = 35.0f;
		}
		else if (TargetHeight < 45.0f)
		{
			TargetHeight = 55.0f;
		}
		else
		{
			TargetHeight = 15.0f;
		}
	}

	public void ResetRotationAndCycleZoom()
	{
		_targetYaw = 0.0f;
		CycleZoom();
	}

	private float GetMaxZoom()
	{
		if (IsFreeCamera)
		{
			return 2000.0f;
		}

		if (GameContext.Host != null && GameContext.SessionState.IsMapEditorMode)
		{
			return MaxZoom * 5.0f;
		}

		return MaxZoom;
	}

	private float GetMinZoom()
	{
		if (IsFreeCamera)
		{
			return 0.1f;
		}

		return MinZoom;
	}

	public void ZoomIn()
	{
		if (IsLocked) return;
		if (IsFreeCamera)
		{
			float yawRad = Mathf.DegToRad(_currentYaw);
			float pitchRad = Mathf.DegToRad(_currentPitch);
			Vector3 forward3D = new Vector3(
				-Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
				Mathf.Sin(pitchRad),
				-Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
			);
			float step = Mathf.Max(1.0f, Position.Y * 0.15f);
			Position += forward3D * step;
			TargetHeight = Position.Y;
			CurrentHeight = Position.Y;
			return;
		}

		TargetHeight = Mathf.Clamp(TargetHeight - ZoomStep, GetMinZoom(), GetMaxZoom());
	}

	public void ZoomOut()
	{
		if (IsLocked) return;
		if (IsFreeCamera)
		{
			float yawRad = Mathf.DegToRad(_currentYaw);
			float pitchRad = Mathf.DegToRad(_currentPitch);
			Vector3 forward3D = new Vector3(
				-Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
				Mathf.Sin(pitchRad),
				-Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
			);
			float step = Mathf.Max(1.0f, Position.Y * 0.15f);
			Position -= forward3D * step;
			TargetHeight = Position.Y;
			CurrentHeight = Position.Y;
			return;
		}

		TargetHeight = Mathf.Clamp(TargetHeight + ZoomStep, GetMinZoom(), GetMaxZoom());
	}

	private const float MapLimit = 95f;

	public void FocusOnPosition(Vector3 targetPos)
	{
		float yawRadians = Mathf.DegToRad(_currentYaw);
		float pitchRadians = Mathf.DegToRad(_currentPitch);
		Vector3 forwardXZ = new Vector3(-Mathf.Sin(yawRadians), 0.0f, -Mathf.Cos(yawRadians));
		float heightAboveTarget = Mathf.Max(0.0f, Position.Y - targetPos.Y);
		float pitchTangent = Mathf.Tan(Mathf.Abs(pitchRadians));
		float horizontalDistance = pitchTangent > 0.0001f ? heightAboveTarget / pitchTangent : 0.0f;
		Vector3 focusPosition = new Vector3(targetPos.X, Position.Y, targetPos.Z) - (forwardXZ * horizontalDistance);
		Position = focusPosition;
	}

}
