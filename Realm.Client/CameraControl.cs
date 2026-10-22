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
	public CameraControl()
	{
	}

	private bool HasCameraState
	{
		get
		{
			GameHost tempQualifier = GameContext.Host;
			return (tempQualifier != null ? GameContext.World : null) != null &&
				   GameContext.World.IsAlive(ECSContext.WorldEntity) &&
				   GameContext.World.Has<CameraState>(ECSContext.WorldEntity);
		}
	}

	public float MoveSpeed
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).MoveSpeed : 35.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.MoveSpeed = value;
			}
		}
	}

	public float ZoomSpeed
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).ZoomSpeed : 10.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.ZoomSpeed = value;
			}
		}
	}

	public float MinZoom
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).MinZoom : 10.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.MinZoom = value;
			}
		}
	}

	public float MaxZoom
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).MaxZoom : 90.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.MaxZoom = value;
			}
		}
	}

	public float ZoomStep
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).ZoomStep : 4.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.ZoomStep = value;
			}
		}
	}

	public float EdgePanMargin
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).EdgePanMargin : 20.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.EdgePanMargin = value;
			}
		}
	}

	public bool EnableEdgePanning
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).EnableEdgePanning : true;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.EnableEdgePanning = value;
			}
		}
	}

	public bool IsLocked
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).IsLocked : false;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.IsLocked = value;
			}
		}
	}

	public bool IsFreeCamera
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).IsFreeCamera : _isFreeCamera;
		set
		{
			_isFreeCamera = value;
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.IsFreeCamera = value;
			}
		}
	}

	private bool _isFreeCamera = false;

	public Node3D FollowTarget { get; set; } = null;

	public float? LimitLeft
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).LimitLeft : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.LimitLeft = value;
			}
		}
	}

	public float? LimitRight
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).LimitRight : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.LimitRight = value;
			}
		}
	}

	public float? LimitTop
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).LimitTop : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.LimitTop = value;
			}
		}
	}

	public float? LimitBottom
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).LimitBottom : null;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.LimitBottom = value;
			}
		}
	}

	public float TargetHeight
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).TargetHeight : 35.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.TargetHeight = value;
			}
		}
	}

	public float CurrentHeight
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).CurrentHeight : 35.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.CurrentHeight = value;
			}
		}
	}

	private bool _isDraggingMouse
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).IsDraggingMouse : false;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
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
				var pos = GameContext.World.Get<CameraState>(ECSContext.WorldEntity).LastMousePosition;
				return new Vector2(pos.X, pos.Y);
			}

			return Vector2.Zero;
		}
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.LastMousePosition = new System.Numerics.Vector2(value.X, value.Y);
			}
		}
	}

	private float _targetYaw
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).TargetYaw : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.TargetYaw = value;
			}
		}
	}

	private float _currentYaw
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).CurrentYaw : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.CurrentYaw = value;
			}
		}
	}

	private float _targetPitch
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).TargetPitch : -55.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.TargetPitch = value;
			}
		}
	}

	private float _currentPitch
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).CurrentPitch : -55.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.CurrentPitch = value;
			}
		}
	}

	private bool _isTopDown
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).IsTopDown : false;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.IsTopDown = value;
			}
		}
	}

	private float _yawSwing
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).YawSwing : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.YawSwing = value;
			}
		}
	}

	private float _pitchSwing
	{
		get => HasCameraState ? GameContext.World.Get<CameraState>(ECSContext.WorldEntity).PitchSwing : 0.0f;
		set
		{
			if (HasCameraState)
			{
				ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
				state.PitchSwing = value;
			}
		}
	}

	private bool _isAltRightDragging = false;

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
		if (keyEvent.Keycode == Key.Home || (keyEvent.Keycode == Key.Space && keyEvent.ShiftPressed))
		{
			ResetCamera();
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _Process(double delta)
	{
		float fDelta = (float)delta;

		if (!HasCameraState) return;

		ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
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

	private void UpdateCameraHeight(ref CameraState state, float fDelta, float terrainHeight)
	{
		if (!IsFreeCamera)
		{
			float minAllowedTargetHeight = terrainHeight + state.MinZoom;
			if (state.TargetHeight < minAllowedTargetHeight)
			{
				state.TargetHeight = minAllowedTargetHeight;
			}

			float zoomRate = state.ZoomSpeed > 0.01f ? (state.ZoomSpeed * 0.55f) : 5.5f;
			float smoothY = Mathf.Lerp(Position.Y, state.TargetHeight, zoomRate * fDelta);
			if (Mathf.Abs(smoothY - state.TargetHeight) < 0.015f)
			{
				smoothY = state.TargetHeight;
			}

			state.CurrentHeight = smoothY;

			if (FollowTarget != null && GodotObject.IsInstanceValid(FollowTarget))
			{
				Position = new Vector3(FollowTarget.Position.X, smoothY, FollowTarget.Position.Z + 25.0f);
			}
			else
			{
				Position = new Vector3(Position.X, smoothY, Position.Z);
			}
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
		state.CurrentYaw = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(state.CurrentYaw), Mathf.DegToRad(state.TargetYaw), 10.0f * fDelta));
		state.CurrentPitch = Mathf.Lerp(state.CurrentPitch, state.TargetPitch, 10.0f * fDelta);
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
