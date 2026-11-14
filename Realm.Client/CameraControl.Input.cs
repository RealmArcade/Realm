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
	public override void _Ready()
	{
		RotationDegrees = new Vector3(-55.0f, 0.0f, 0.0f);
		Position = new Vector3(0.0f, CurrentHeight, 25.0f);
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	public override void _Notification(int what)
	{
		if (what == unchecked((int)Node.NotificationApplicationFocusOut))
		{
			_isDraggingMouse = false;
			_isAltRightDragging = false;
			if (Input.MouseMode == Input.MouseModeEnum.Captured)
			{
				Input.MouseMode = Input.MouseModeEnum.Visible;
			}

			if (GameContext.Host != null && GameContext.SessionState.IsMapEditorMode)
			{
				GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(false);
			}
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (IsInputBlocked()) return;

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			HandleCameraResetKey(keyEvent);
			return;
		}

		if (@event is InputEventMouseButton mouseBtn)
		{
			if (HandleMouseButton(mouseBtn)) return;
		}
		else if (@event is InputEventMouseMotion mouseMotion && (_isDraggingMouse || _isAltRightDragging))
		{
			HandleMouseMotion(mouseMotion);
		}
	}

	private bool IsInputBlocked()
	{
		return IsLocked || (GameContext.InGameHud != null && GameContext.InGameHud.IsChatActive) || GameContext.IsSettingsMenuOpen;
	}

	private void HandleCameraResetKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode == Key.Space && keyEvent.ShiftPressed)
		{
			ResetCamera();
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _Process(double delta)
	{
		float fDelta = (float)delta;

		if (!HasCameraState) return;

		// A map driving the camera owns it for as long as it says so: the player's panning, zooming, edge scrolling and
		// rotation are not read, and the shot places the camera itself.
		if (UpdateScriptCamera(fDelta)) return;

		ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
		state.MoveSpeed = 10.0f + (GameSettings.ScrollSpeed / 100.0f) * 50.0f;

		float terrainHeight = 0.0f;
		if (GameContext.Host?.GroundTerrain != null)
		{
			GameContext.Host.GroundTerrain.GetHeightAndNormal(Position.X, Position.Z, out terrainHeight, out _);
		}

		UpdateCameraHeight(ref state, fDelta, terrainHeight);

		bool isEditor = GameContext.Host != null && GameContext.SessionState.IsMapEditorMode;
		UpdateCameraRotation(ref state, fDelta, isEditor);

		if (state.IsLocked || (GameContext.InGameHud != null && GameContext.InGameHud.IsChatActive)) return;

		Vector3 velocity = CalculateCameraVelocity(ref state, isEditor);

		if (velocity != Vector3.Zero)
		{
			ApplyCameraVelocity(ref state, velocity, fDelta, isEditor);
		}
	}

	private bool IsModifyingHudSlider()
	{
		if (!Input.IsMouseButtonPressed(MouseButton.Left))
		{
			if (MapEditorHUD.IsDraggingSlider)
			{
				MapEditorHUD.IsDraggingSlider = false;
			}

			return false;
		}

		if (MapEditorHUD.IsDraggingSlider) return true;

		Viewport viewport = GetViewport();
		if (viewport != null)
		{
			if (viewport.GuiGetFocusOwner() is Slider) return true;
			if (viewport.GuiGetHoveredControl() is Slider)
			{
				MapEditorHUD.IsDraggingSlider = true;
				return true;
			}
		}

		return false;
	}

	private void ApplyCameraBounds(ref Vector3 pos, bool isEditor)
	{
		float minX, maxX, minZ, maxZ;

		if (isEditor)
		{
			if (GameContext.Host == null) return;

			float leftBound = GameContext.Host.EditorCameraBoundsLeft;
			float rightBound = GameContext.Host.EditorCameraBoundsRight;
			float topBound = GameContext.Host.EditorCameraBoundsTop;
			float bottomBound = GameContext.Host.EditorCameraBoundsBottom;

			float boundMinX = Mathf.Min(leftBound, rightBound);
			float boundMaxX = Mathf.Max(leftBound, rightBound);
			float boundMinZ = Mathf.Min(topBound, bottomBound);
			float boundMaxZ = Mathf.Max(topBound, bottomBound);

			float paddingX = (boundMaxX - boundMinX) * 0.25f;
			float paddingZ = (boundMaxZ - boundMinZ) * 0.25f;

			minX = boundMinX - paddingX;
			maxX = boundMaxX + paddingX;
			minZ = boundMinZ - paddingZ;
			maxZ = boundMaxZ + paddingZ;
		}
		else
		{
			float defaultLimit = (GameContext.Host?.GroundTerrain != null)
				? (GameContext.Host.GroundTerrain.Width * GameContext.Host.GroundTerrain.QuadSize * 0.5f) - 10.0f
				: MapLimit;

			float rawMinX = LimitLeft ?? -defaultLimit;
			float rawMaxX = LimitRight ?? defaultLimit;
			float rawMinZ = LimitTop ?? -defaultLimit;
			float rawMaxZ = LimitBottom ?? (defaultLimit + 30f);

			minX = Mathf.Min(rawMinX, rawMaxX);
			maxX = Mathf.Max(rawMinX, rawMaxX);
			minZ = Mathf.Min(rawMinZ, rawMaxZ);
			maxZ = Mathf.Max(rawMinZ, rawMaxZ);
		}

		pos.X = Mathf.Clamp(pos.X, minX, maxX);
		pos.Z = Mathf.Clamp(pos.Z, minZ, maxZ);
	}

	private bool HandleMouseButton(InputEventMouseButton mouseBtn)
	{
		if (mouseBtn.Pressed)
		{
			return HandleMouseButtonPressed(mouseBtn);
		}

		HandleMouseButtonReleased(mouseBtn);
		return false;
	}

	private bool HandleMouseButtonPressed(InputEventMouseButton mouseBtn)
	{
		if (IsMouseWheel(mouseBtn.ButtonIndex))
		{
			if (ShouldBlockMouseWheel(mouseBtn)) return true;
		}

		return ProcessMouseButtonAction(mouseBtn);
	}

	private bool IsMouseWheel(MouseButton buttonIndex)
	{
		return buttonIndex == MouseButton.WheelUp || buttonIndex == MouseButton.WheelDown;
	}

	private bool ShouldBlockMouseWheel(InputEventMouseButton mouseBtn)
	{
		if (mouseBtn.AltPressed) return true;

		bool isEditor = GameContext.Host != null && GameContext.SessionState.IsMapEditorMode;
		bool shiftOrCtrl = Input.IsKeyPressed(Key.Shift) || Input.IsKeyPressed(Key.Ctrl);

		if (isEditor && shiftOrCtrl) return true;

		if (IsMouseOverHUD(mouseBtn.Position, isEditor)) return true;

		return IsMouseOverOtherUI();
	}

	private bool IsMouseOverHUD(Vector2 position, bool isEditor)
	{
		if (GameContext.Host == null) return false;

		if (isEditor && GameContext.MapEditorHud != null)
			return GameContext.MapEditorHud.IsMouseOverUI(position);

		if (!isEditor && GameContext.InGameHud != null)
			return GameContext.InGameHud.IsMouseOverUI(position);

		return false;
	}

	private bool IsMouseOverOtherUI()
	{
		var hoveredControl = GetViewport()?.GuiGetHoveredControl();
		if (hoveredControl == null || hoveredControl.GetType().Name == "GameHost") return false;

		return GameContext.MapEditorHud == null || hoveredControl != GameContext.MapEditorHud;
	}

	private bool ProcessMouseButtonAction(InputEventMouseButton mouseBtn)
	{
		if (mouseBtn.ButtonIndex == MouseButton.WheelUp) ZoomIn();
		else if (mouseBtn.ButtonIndex == MouseButton.WheelDown) ZoomOut();
		else if (mouseBtn.ButtonIndex == MouseButton.Middle) HandleMiddleMousePress(mouseBtn);
		else if (mouseBtn.ButtonIndex == MouseButton.Right) HandleRightMousePress(mouseBtn);

		return false;
	}

	private void HandleMiddleMousePress(InputEventMouseButton mouseBtn)
	{
		_isDraggingMouse = true;
		_lastMousePosition = mouseBtn.Position;

		if (GameContext.Host != null && GameContext.SessionState.IsMapEditorMode)
		{
			GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(true);
		}
	}

	private void HandleRightMousePress(InputEventMouseButton mouseBtn)
	{
		if (!mouseBtn.AltPressed && !Input.IsKeyPressed(Key.Alt) && !IsFreeCamera) return;

		_isAltRightDragging = true;
		_lastMousePosition = mouseBtn.Position;

		if (IsFreeCamera) Input.MouseMode = Input.MouseModeEnum.Captured;
		GetViewport().SetInputAsHandled();
	}

	private void HandleMouseButtonReleased(InputEventMouseButton mouseBtn)
	{
		if (mouseBtn.ButtonIndex == MouseButton.Middle)
		{
			_isDraggingMouse = false;
			if (GameContext.Host != null && GameContext.SessionState.IsMapEditorMode)
			{
				GameContext.MapEditorHud?.Set3DInteractionActive_ViewLogic(false);
			}
		}
		else if (mouseBtn.ButtonIndex == MouseButton.Right && _isAltRightDragging)
		{
			_isAltRightDragging = false;
			if (Input.MouseMode == Input.MouseModeEnum.Captured)
			{
				Input.MouseMode = Input.MouseModeEnum.Visible;
			}

			GetViewport().SetInputAsHandled();
		}
	}

	private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
	{
		FollowTarget = null;
		Vector2 deltaMouse = Input.MouseMode == Input.MouseModeEnum.Captured ? mouseMotion.Relative : mouseMotion.Position - _lastMousePosition;
		_lastMousePosition = mouseMotion.Position;

		bool isAltHeld = mouseMotion.AltPressed || Input.IsKeyPressed(Key.Alt) || _isAltRightDragging;

		if (isAltHeld)
		{
			HandleAltMouseMotion(deltaMouse);
			return;
		}

		if (HandleEditorShiftMouseMotion(deltaMouse)) return;

		ApplyMouseMotionVelocity(deltaMouse);
	}

	private void HandleAltMouseMotion(Vector2 deltaMouse)
	{
		float minPitch = IsFreeCamera ? -89.9f : -85.0f;
		float maxPitch = IsFreeCamera ? 89.9f : -15.0f;

		_targetYaw = (_targetYaw - deltaMouse.X * 0.3f + 360.0f) % 360.0f;
		_targetPitch = Mathf.Clamp(_targetPitch + deltaMouse.Y * 0.3f, minPitch, maxPitch);
		if (_isAltRightDragging)
		{
			GetViewport().SetInputAsHandled();
		}
	}

	private bool HandleEditorShiftMouseMotion(Vector2 deltaMouse)
	{
		if (Input.IsKeyPressed(Key.Shift) && GameContext.Host != null && GameContext.SessionState.IsMapEditorMode)
		{
			_targetYaw = (_targetYaw + deltaMouse.X * 0.25f + 360.0f) % 360.0f;
			return true;
		}

		return false;
	}

	private void ApplyMouseMotionVelocity(Vector2 deltaMouse)
	{
		float sensFactor = 0.0005f + (GameSettings.MouseSens / 100.0f) * 0.003f;
		float moveX = -deltaMouse.X * sensFactor * CurrentHeight;
		float moveZ = deltaMouse.Y * sensFactor * CurrentHeight;

		float yawRad = Mathf.DegToRad(_currentYaw);
		Vector3 forwardXZ = new Vector3(-Mathf.Sin(yawRad), 0f, -Mathf.Cos(yawRad));
		Vector3 rightXZ = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));

		Vector3 velocity = (rightXZ * moveX) + (forwardXZ * moveZ);

		Vector3 newPos = Position + velocity;
		if (!IsFreeCamera)
		{
			bool isEditor = GameContext.Host != null && GameContext.SessionState.IsMapEditorMode;
			ApplyCameraBounds(ref newPos, isEditor);
		}

		Position = newPos;
	}

}
