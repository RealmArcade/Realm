using Godot;
using System;
using DotRecast.Detour;
using DotRecast.Core.Numerics;
using Realm.Godot.ReplaySystem;

public class MinimapPanel
{
	private Control _minimapArea;
	private MinimapCameraIndicator _cameraIndicator;
	private Camera3D _camera3D;
	private PanelContainer _minimapFrame;
	private bool _isRightClickPanning = false;
	private bool _isLeftClickDragging = false;
	private Vector3 _lastCameraPos;
	private Vector3 _lastCameraRot;
	private readonly Vector2[] _cachedMinimapPoints = new Vector2[4];

	public MinimapPanel(PanelContainer minimapFrame, Control minimapArea, MinimapCameraIndicator cameraIndicator, Camera3D camera3D)
	{
		_minimapFrame = minimapFrame;
		_minimapArea = minimapArea;
		_cameraIndicator = cameraIndicator;
		_camera3D = camera3D;

		SetupMinimap();
	}

	public void SetCamera(Camera3D camera)
	{
		_camera3D = camera;
	}

	private void SetupMinimap()
	{
		_minimapArea.GuiInput += OnMinimapGuiInput;
	}

	private void OnMinimapGuiInput(InputEvent @event)
	{
		bool isSpectator = LobbyManager.Instance?.LocalPlayer?.Team == "Spectator";

		if (ReplayPlaybackManager.Instance.IsPlayingReplay || isSpectator)
		{
			HandleReplayOrSpectatorInput(@event);
			return;
		}

		HandleActivePlayerInput(@event);
	}

	private void HandleReplayOrSpectatorInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton rMouseBtn)
		{
			HandleReplayMouseButton(rMouseBtn);
		}
		else if (@event is InputEventMouseMotion rMouseMotion)
		{
			HandleMouseMotion(rMouseMotion);
		}
	}

	private void HandleReplayMouseButton(InputEventMouseButton mouseBtn)
	{
		if (mouseBtn.ButtonIndex == MouseButton.Left)
		{
			_isLeftClickDragging = mouseBtn.Pressed;
			if (mouseBtn.Pressed) TeleportCameraToMinimapPos(mouseBtn.Position);
		}
		else if (mouseBtn.ButtonIndex == MouseButton.Right)
		{
			_isRightClickPanning = mouseBtn.Pressed;
			if (mouseBtn.Pressed) TeleportCameraToMinimapPos(mouseBtn.Position);
		}
	}

	private void HandleActivePlayerInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseBtn)
		{
			HandleActivePlayerMouseButton(mouseBtn);
		}
		else if (@event is InputEventMouseMotion mouseMotion)
		{
			HandleMouseMotion(mouseMotion);
		}
	}

	private void HandleActivePlayerMouseButton(InputEventMouseButton mouseBtn)
	{
		if (mouseBtn.ButtonIndex == MouseButton.Left)
		{
			HandleActivePlayerLeftClick(mouseBtn);
		}
		else if (mouseBtn.ButtonIndex == MouseButton.Right)
		{
			if (!mouseBtn.Pressed) return;
			var minimapWorldPos = MinimapHelper.MinimapToWorld(mouseBtn.Position, _minimapArea.Size);
			GameHost.Instance?.HandleMinimapRightClick(minimapWorldPos);
		}
	}

	private void HandleActivePlayerLeftClick(InputEventMouseButton mouseBtn)
	{
		if (!mouseBtn.Pressed)
		{
			_isLeftClickDragging = false;
			return;
		}

		var minimapWorldPos = MinimapHelper.MinimapToWorld(mouseBtn.Position, _minimapArea.Size);

		if (mouseBtn.AltPressed)
		{
			HandleAltLeftClick(minimapWorldPos);
			return;
		}

		HandleGameHostLeftClick(mouseBtn.Position, minimapWorldPos);
	}

	private void HandleAltLeftClick(Vector3 minimapWorldPos)
	{
		if (GameHost.Instance == null) return;

		if (GameHost.Instance.Multiplayer.MultiplayerPeer != null)
		{
			GameHost.Instance.Rpc("NetworkPingMinimap", minimapWorldPos);
		}
		else
		{
			GameHost.Instance.AddMinimapPing(minimapWorldPos);
		}
	}

	private void HandleGameHostLeftClick(Vector2 mousePosition, Vector3 minimapWorldPos)
	{
		if (GameHost.Instance == null) return;

		if (GameHost.Instance.ActivePingMode)
		{
			GameHost.Instance.AddMinimapPing(minimapWorldPos);
			GameHost.Instance.ActivePingMode = false;
			return;
		}

		if (GameHost.Instance.ActiveCommandTargeting != null)
		{
			HandleCommandTargeting(minimapWorldPos);
			return;
		}

		if (GameHost.Instance.ActiveSpellTargeting != null)
		{
			GameHost.Instance.CastSpellAt(GameHost.Instance.ActiveSpellTargeting, minimapWorldPos);
			GameHost.Instance.ClearTargetingModes();
			return;
		}

		if (GameHost.Instance.ActiveBuildingPlacementType != null)
		{
			GameHost.Instance.PlaceBuildingAt(GameHost.Instance.ActiveBuildingPlacementType, minimapWorldPos);
			GameHost.Instance.ClearTargetingModes();
			return;
		}

		TeleportCameraToMinimapPos(mousePosition);
		_isLeftClickDragging = true;
	}

	private void HandleCommandTargeting(Vector3 minimapWorldPos)
	{
		string cmd = GameHost.Instance.ActiveCommandTargeting;
		switch (cmd)
		{
			case "attack":
				GameHost.Instance.IssueAttackMoveCommand(minimapWorldPos);
				break;
			case "move":
				GameHost.Instance.IssueMoveCommand(minimapWorldPos, Input.IsKeyPressed(Key.Shift));
				break;
			case "patrol":
				GameHost.Instance.IssuePatrolCommand(minimapWorldPos);
				break;
			case "rally":
				HandleRallyCommand(minimapWorldPos);
				break;
		}
		GameHost.Instance.ClearTargetingModes();
	}

	private void HandleRallyCommand(Vector3 minimapWorldPos)
	{
		if (GameHost.Instance.SelectedUnits.Count != 1) return;
		var unit = GameHost.Instance.SelectedUnits[0];
		if (!unit.IsEnemy && unit.IsBuilding)
		{
			GameHost.Instance.SetRallyPoint(unit, minimapWorldPos);
		}
	}

	private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
	{
		if (_isLeftClickDragging || _isRightClickPanning)
		{
			TeleportCameraToMinimapPos(mouseMotion.Position);
		}
	}

	public void TeleportCameraToMinimapPos(Vector2 clickPos)
	{
		if (_minimapArea == null) return;
		var minimapWorldPos = MinimapHelper.MinimapToWorld(clickPos, _minimapArea.Size);

		if (_camera3D != null && GodotObject.IsInstanceValid(_camera3D))
		{
			_camera3D.GlobalPosition = new Vector3(minimapWorldPos.X, _camera3D.GlobalPosition.Y, minimapWorldPos.Z);
			InGameHUD.Instance?.ShowFeedbackText(string.Format(TranslationServer.Translate("Panned Camera on Minimap to: {0:F0}, {1:F0}"), minimapWorldPos.X, minimapWorldPos.Z), new Color(1, 0.85f, 0.5f));
		}
	}

	public void UpdateMinimapIndicator()
	{
		if (_camera3D == null || !GodotObject.IsInstanceValid(_camera3D) || _cameraIndicator == null || _minimapArea == null) return;

		Vector3 camPos = _camera3D.GlobalPosition;
		Vector3 camRot = _camera3D.GlobalRotation;
		if ((camPos - _lastCameraPos).LengthSquared() < 0.0001f && (camRot - _lastCameraRot).LengthSquared() < 0.0001f)
		{
			return;
		}
		_lastCameraPos = camPos;
		_lastCameraRot = camRot;

		MinimapHelper.CalculateCameraFrustumMinimapPoints(_camera3D, _minimapArea.Size, _cachedMinimapPoints);
		_cameraIndicator.SetPoints(_cachedMinimapPoints);
	}
}
