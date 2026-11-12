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
	private void UpdateCameraHeight(ref CameraState state, float fDelta, float terrainHeight)
	{
		if (!IsFreeCamera)
		{
			if (FollowTarget != null && GodotObject.IsInstanceValid(FollowTarget))
			{
				if (_hasScriptFollowConfig)
				{
					UpdateScriptFollowCamera(ref state, fDelta);
					return;
				}

				float followMinHeight = terrainHeight + state.MinZoom;
				if (state.TargetHeight < followMinHeight)
				{
					state.TargetHeight = followMinHeight;
				}

				float followZoomRate = state.ZoomSpeed > 0.01f ? (state.ZoomSpeed * 0.55f) : 5.5f;
				float followSmoothY = Mathf.Lerp(Position.Y, state.TargetHeight, followZoomRate * fDelta);
				if (Mathf.Abs(followSmoothY - state.TargetHeight) < 0.015f)
				{
					followSmoothY = state.TargetHeight;
				}

				state.CurrentHeight = followSmoothY;
				Position = new Vector3(FollowTarget.Position.X, followSmoothY, FollowTarget.Position.Z + 25.0f);
				return;
			}

			if (!_hasScriptViewActive)
			{
				float minAllowedTargetHeight = terrainHeight + state.MinZoom;
				if (state.TargetHeight < minAllowedTargetHeight)
				{
					state.TargetHeight = minAllowedTargetHeight;
				}
			}

			float zoomRate = state.ZoomSpeed > 0.01f ? (state.ZoomSpeed * 0.55f) : 5.5f;
			float smoothY = Mathf.Lerp(Position.Y, state.TargetHeight, zoomRate * fDelta);
			if (Mathf.Abs(smoothY - state.TargetHeight) < 0.015f)
			{
				smoothY = state.TargetHeight;
			}

			state.CurrentHeight = smoothY;
			Position = new Vector3(Position.X, smoothY, Position.Z);
		}
		else
		{
			if (state.TargetHeight < 0.1f)
			{
				state.TargetHeight = 0.1f;
			}

			state.CurrentHeight = Position.Y;
			state.TargetHeight = Position.Y;
		}
	}

	private void UpdateScriptFollowCamera(ref CameraState state, float fDelta)
	{
		if (FollowTarget == null || !GodotObject.IsInstanceValid(FollowTarget)) return;

		float desiredYawDeg = _scriptFollowYawOffsetDegrees;
		if (_scriptFollowAlignToUnitFacing)
		{
			float unitYawRad = FollowTarget is Unit3D unit3D ? unit3D.CachedRotationY : FollowTarget.Rotation.Y;
			desiredYawDeg = Mathf.RadToDeg(unitYawRad) + 180.0f + _scriptFollowYawOffsetDegrees;
		}

		state.TargetYaw = desiredYawDeg;
		state.TargetPitch = _scriptFollowPitchDegrees;

		if (_scriptFollowNeedsSnap || _scriptFollowSmoothing <= 0f)
		{
			state.CurrentYaw = state.TargetYaw;
			state.CurrentPitch = state.TargetPitch;
			_scriptFollowNeedsSnap = false;

			float snapYawRad = Mathf.DegToRad(state.CurrentYaw);
			Vector3 snapForwardXZ = new Vector3(-Mathf.Sin(snapYawRad), 0f, -Mathf.Cos(snapYawRad));
			Vector3 snapPos = FollowTarget.Position - snapForwardXZ * _scriptFollowDistance + new Vector3(0f, _scriptFollowHeightOffset, 0f);
			if (GameContext.Host?.GroundTerrain != null)
			{
				GameContext.Host.GroundTerrain.GetHeightAndNormal(snapPos.X, snapPos.Z, out float groundY, out _);
				if (snapPos.Y < groundY + 1.5f) snapPos.Y = groundY + 1.5f;
			}
			Position = snapPos;
			state.CurrentHeight = Position.Y;
			state.TargetHeight = Position.Y;
			return;
		}

		float rotSmoothing = _scriptFollowSmoothing > 0f ? _scriptFollowSmoothing : 3.5f;
		float rotAlpha = 1.0f - Mathf.Exp(-rotSmoothing * fDelta);

		float curYawRad = Mathf.DegToRad(state.CurrentYaw);
		float tgtYawRad = Mathf.DegToRad(state.TargetYaw);
		float yawDiffRad = Mathf.AngleDifference(curYawRad, tgtYawRad);

		float maxYawStepRad = Mathf.DegToRad(150.0f) * fDelta;
		float yawStepRad = Mathf.Clamp(yawDiffRad * rotAlpha, -maxYawStepRad, maxYawStepRad);

		state.CurrentYaw = Mathf.RadToDeg(curYawRad + yawStepRad);
		state.CurrentPitch = Mathf.Lerp(state.CurrentPitch, state.TargetPitch, rotAlpha);

		float camYawRad = Mathf.DegToRad(state.CurrentYaw);
		Vector3 forwardXZ = new Vector3(-Mathf.Sin(camYawRad), 0f, -Mathf.Cos(camYawRad));
		Vector3 desiredPos = FollowTarget.Position - forwardXZ * _scriptFollowDistance + new Vector3(0f, _scriptFollowHeightOffset, 0f);

		if (GameContext.Host?.GroundTerrain != null)
		{
			GameContext.Host.GroundTerrain.GetHeightAndNormal(desiredPos.X, desiredPos.Z, out float camGroundY, out _);
			if (desiredPos.Y < camGroundY + 1.5f) desiredPos.Y = camGroundY + 1.5f;
		}

		if ((Position - desiredPos).LengthSquared() > 400.0f)
		{
			Position = desiredPos;
		}
		else
		{
			float posSmoothing = _scriptFollowSmoothing * 1.5f;
			float posAlpha = 1.0f - Mathf.Exp(-posSmoothing * fDelta);
			Position = Position.Lerp(desiredPos, posAlpha);
		}

		state.CurrentHeight = Position.Y;
		state.TargetHeight = Position.Y;
	}

	private void UpdateCameraRotation(ref CameraState state, float fDelta, bool isEditor)
	{
		if (isEditor)
		{
			HandleEditorCameraRotation(ref state, fDelta);
		}
		else
		{
			HandleGameCameraRotation(ref state, fDelta);
		}

		ApplyCameraRotation(ref state, fDelta);
	}

	private void HandleEditorCameraRotation(ref CameraState state, float fDelta)
	{
		if (Input.IsKeyPressed(Key.Comma)) state.TargetYaw = (state.TargetYaw - 90.0f * fDelta + 360.0f) % 360.0f;
		if (Input.IsKeyPressed(Key.Period)) state.TargetYaw = (state.TargetYaw + 90.0f * fDelta) % 360.0f;
		state.YawSwing = 0.0f;
		state.PitchSwing = 0.0f;
	}

	private void HandleGameCameraRotation(ref CameraState state, float fDelta)
	{
		bool isInputBlocked = state.IsLocked || (GameContext.InGameHud != null && GameContext.InGameHud.IsChatActive);
		if (isInputBlocked)
		{
			state.YawSwing = Mathf.MoveToward(state.YawSwing, 0.0f, 45.0f * fDelta);
			state.PitchSwing = Mathf.MoveToward(state.PitchSwing, 0.0f, 22.5f * fDelta);
			return;
		}

		UpdateCameraSwings(ref state, fDelta);
	}

	private void UpdateCameraSwings(ref CameraState state, float fDelta)
	{
		if (Input.IsKeyPressed(Key.Insert)) state.YawSwing = Mathf.MoveToward(state.YawSwing, 90.0f, 45.0f * fDelta);
		else if (Input.IsKeyPressed(Key.Delete)) state.YawSwing = Mathf.MoveToward(state.YawSwing, -90.0f, 45.0f * fDelta);
		else state.YawSwing = Mathf.MoveToward(state.YawSwing, 0.0f, 45.0f * fDelta);

		if (Input.IsKeyPressed(Key.Pageup)) state.PitchSwing = Mathf.MoveToward(state.PitchSwing, 45.0f, 22.5f * fDelta);
		else if (Input.IsKeyPressed(Key.Pagedown)) state.PitchSwing = Mathf.MoveToward(state.PitchSwing, -45.0f, 22.5f * fDelta);
		else state.PitchSwing = Mathf.MoveToward(state.PitchSwing, 0.0f, 22.5f * fDelta);
	}

	private void ApplyCameraRotation(ref CameraState state, float fDelta)
	{
		if (_hasScriptFollowConfig)
		{
			RotationDegrees = new Vector3(state.CurrentPitch + state.PitchSwing, state.CurrentYaw + state.YawSwing, 0.0f);
			return;
		}

		float rate = 10.0f;
		state.CurrentYaw = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(state.CurrentYaw), Mathf.DegToRad(state.TargetYaw), rate * fDelta));
		state.CurrentPitch = Mathf.Lerp(state.CurrentPitch, state.TargetPitch, rate * fDelta);
		RotationDegrees = new Vector3(state.CurrentPitch + state.PitchSwing, state.CurrentYaw + state.YawSwing, 0.0f);
	}

	private Vector3 CalculateCameraVelocity(ref CameraState state, bool isEditor)
	{
		Vector3 velocity = Vector3.Zero;
		float yawRad = Mathf.DegToRad(state.CurrentYaw);
		float pitchRad = Mathf.DegToRad(state.CurrentPitch);

		Vector3 forwardXZ = new Vector3(-Mathf.Sin(yawRad), 0f, -Mathf.Cos(yawRad));
		Vector3 rightXZ = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
		Vector3 forward3D = new Vector3(
			-Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
			Mathf.Sin(pitchRad),
			-Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
		);

		bool isTyping = GetViewport()?.GuiGetFocusOwner() is LineEdit or TextEdit;
		if (!isTyping)
		{
			velocity += GetKeyboardVelocity(forwardXZ, rightXZ, forward3D);
		}

		if (ShouldApplyEdgePanning(ref state, isEditor))
		{
			velocity += GetEdgePanVelocity(ref state, forwardXZ, rightXZ);
		}

		return velocity;
	}

	private Vector3 GetKeyboardVelocity(Vector3 forwardXZ, Vector3 rightXZ, Vector3 forward3D)
	{
		if (IsFreeCamera)
			return GetFreeCameraVelocity(forward3D, rightXZ);

		return GetStandardCameraVelocity(forwardXZ, rightXZ);
	}

	private static Vector3 GetFreeCameraVelocity(Vector3 forward3D, Vector3 rightXZ)
	{
		Vector3 velocity = Vector3.Zero;

		if (IsForwardPressedFree()) velocity += forward3D;
		if (IsBackwardPressedFree()) velocity -= forward3D;
		if (IsLeftPressedFree()) velocity -= rightXZ;
		if (IsRightPressedFree()) velocity += rightXZ;
		if (IsUpPressedFree()) velocity += Vector3.Up;
		if (IsDownPressedFree()) velocity -= Vector3.Up;

		return velocity;
	}

	private static Vector3 GetStandardCameraVelocity(Vector3 forwardXZ, Vector3 rightXZ)
	{
		Vector3 velocity = Vector3.Zero;

		if (Input.IsKeyPressed(Key.Up)) velocity += forwardXZ;
		if (Input.IsKeyPressed(Key.Down)) velocity -= forwardXZ;
		if (Input.IsKeyPressed(Key.Left)) velocity -= rightXZ;
		if (Input.IsKeyPressed(Key.Right)) velocity += rightXZ;

		return velocity;
	}

	private static bool IsForwardPressedFree() => Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.W);
	private static bool IsBackwardPressedFree() => Input.IsKeyPressed(Key.Down) || Input.IsKeyPressed(Key.S);
	private static bool IsLeftPressedFree() => Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A);
	private static bool IsRightPressedFree() => Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D);
	private static bool IsUpPressedFree() => Input.IsKeyPressed(Key.Pageup) || Input.IsKeyPressed(Key.E) || Input.IsKeyPressed(Key.Space);
	private static bool IsDownPressedFree() => Input.IsKeyPressed(Key.Pagedown) || Input.IsKeyPressed(Key.Q) || Input.IsKeyPressed(Key.C);

	private bool ShouldApplyEdgePanning(ref CameraState state, bool isEditor)
	{
		if (!state.EnableEdgePanning || Input.MouseMode != Input.MouseModeEnum.Visible || _isDraggingMouse || _isAltRightDragging)
			return false;

		bool isModifyingSlider = isEditor && Input.IsMouseButtonPressed(MouseButton.Left) && IsModifyingHudSlider();
		return !isModifyingSlider;
	}

	private Vector3 GetEdgePanVelocity(ref CameraState state, Vector3 forwardXZ, Vector3 rightXZ)
	{
		Vector3 velocity = Vector3.Zero;
		Vector2 mousePos = GetViewport().GetMousePosition();
		Vector2 windowSize = GetViewport().GetVisibleRect().Size;

		if (!IsMouseWithinWindow(mousePos, windowSize)) return velocity;

		if (mousePos.X < state.EdgePanMargin) velocity -= rightXZ;
		else if (mousePos.X > windowSize.X - state.EdgePanMargin) velocity += rightXZ;

		if (mousePos.Y < state.EdgePanMargin) velocity += forwardXZ;
		else if (mousePos.Y > windowSize.Y - state.EdgePanMargin) velocity -= forwardXZ;

		return velocity;
	}

	private bool IsMouseWithinWindow(Vector2 mousePos, Vector2 windowSize)
	{
		return mousePos.X >= 0 && mousePos.X < windowSize.X && mousePos.Y >= 0 && mousePos.Y < windowSize.Y;
	}

	private void ApplyCameraVelocity(ref CameraState state, Vector3 velocity, float fDelta, bool isEditor)
	{
		FollowTarget = null;
		_pendingFollowUnitServerId = 0;
		_hasScriptFollowConfig = false;
		float speedMult = Input.IsKeyPressed(Key.Shift) ? 2.5f : (Input.IsKeyPressed(Key.Ctrl) ? 0.35f : 1.0f);
		velocity = velocity.Normalized() * state.MoveSpeed * speedMult * fDelta;

		float maxZoom = isEditor ? state.MaxZoom * 5.0f : state.MaxZoom;
		float zoomFactor = Mathf.Clamp(state.CurrentHeight / maxZoom, 0.2f, 3.0f);
		velocity *= Mathf.Lerp(0.5f, 1.5f, zoomFactor);

		Vector3 newPos = Position + velocity;
		if (IsFreeCamera)
		{
			Position = newPos;
			state.CurrentHeight = Position.Y;
			state.TargetHeight = Position.Y;
		}
		else
		{
			ApplyCameraBounds(ref newPos, isEditor);
			Position = newPos;
		}
	}
}
