using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Components.Core;

namespace Realm.Client;

/// <summary>
///     The camera while a map is driving it: a shot that flies to a place and looks at a point, a shot that follows a unit,
///     or a camera the map has simply held. It is a mode of its own rather than a tween of the player's camera, because a
///     shot points the camera at what it is framing instead of scrolling a view around, and because the player's own
///     panning, zooming, edge scrolling and rotation have to be out of the way for as long as it runs.
/// </summary>
public partial class CameraControl : Camera3D
{
	private const float ScriptCameraMinimumGroundClearance = 1.5f;

	private enum ScriptCameraMode
	{
		/// <summary>The player drives the camera.</summary>
		None,

		/// <summary>The camera flies to a stated place, looking at a stated point.</summary>
		MoveTo,

		/// <summary>The camera sits behind a unit and looks at it.</summary>
		FollowUnit
	}

	private ScriptCameraMode _scriptCameraMode = ScriptCameraMode.None;
	private bool _scriptCameraHeld;

	private Vector3 _scriptMoveStartPosition;
	private Vector3 _scriptMoveEndPosition;
	private Vector3 _scriptMoveStartLookAt;
	private Vector3 _scriptMoveEndLookAt;
	private float _scriptMoveElapsed;
	private float _scriptMoveDuration;
	private Vector3 _scriptMoveLookAt;

	private int _pendingFollowServerUnitId;
	private float _pendingFollowDistance;
	private float _pendingFollowHeight;
	private float _pendingFollowYawOffsetDegrees;
	private float _pendingFollowSmoothingSeconds;
	private float _pendingFollowLookAtHeight;
	private bool _pendingFollowAlignsToFacing;

	private Unit3D? _scriptFollowTarget;
	private float _scriptFollowDistance;
	private float _scriptFollowHeight;
	private float _scriptFollowYawOffsetDegrees;
	private float _scriptFollowSmoothingSeconds;
	private float _scriptFollowLookAtHeight;
	private bool _scriptFollowAlignsToFacing;

	/// <summary>
	///     Whether a map is driving this camera, which is the state in which the player's own controls are not read.
	/// </summary>
	public bool IsScriptControlled => _scriptCameraMode != ScriptCameraMode.None || _scriptCameraHeld || _pendingFollowServerUnitId > 0;

	/// <summary>
	///     Whether a map is driving this camera, stated as a call so a tool or a script inspecting the camera can read it.
	/// </summary>
	/// <returns>True when the player's own camera controls are not being read.</returns>
	public bool IsScriptCameraControlled() => IsScriptControlled;

	/// <summary>
	///     Holds the player's own camera controls, or gives them back, without moving the camera. A camera a map has taken
	///     is held until it is released, whether or not a shot is running.
	/// </summary>
	/// <param name="held">True to hold the controls.</param>
	public void SetScriptCameraHeld(bool held)
	{
		_scriptCameraHeld = held;
		if (held) return;

		if (_scriptCameraMode == ScriptCameraMode.None) AdoptCurrentCameraReading();
	}

	/// <summary>
	///     Sends the camera to a stated place, looking at a stated point, over a stated time. A duration of zero is a cut.
	/// </summary>
	/// <param name="position">Where the camera goes.</param>
	/// <param name="lookAt">The point the camera is pointed at for the whole move.</param>
	/// <param name="durationSeconds">How long the move takes, or zero for a cut.</param>
	public void BeginScriptCameraMoveTo(Vector3 position, Vector3 lookAt, float durationSeconds)
	{
		_scriptCameraHeld = true;
		_scriptFollowTarget = null;
		_scriptCameraMode = ScriptCameraMode.MoveTo;

		_scriptMoveStartPosition = Position;
		_scriptMoveEndPosition = KeepCameraAboveGround(position);
		_scriptMoveStartLookAt = _scriptMoveLookAt;
		bool hasCurrentShot = _scriptMoveStartLookAt != Vector3.Zero;
		_scriptMoveStartLookAt = hasCurrentShot ? _scriptMoveStartLookAt : lookAt;
		_scriptMoveEndLookAt = lookAt;
		_scriptMoveElapsed = 0f;
		_scriptMoveDuration = Mathf.Max(durationSeconds, 0f);

		if (_scriptMoveDuration <= 0.0001f)
		{
			// A cut arrives at once, so the shot is over before the next frame: the camera stays held by the map, and holds
			// exactly where the shot put it, until another shot or a release replaces it.
			Position = _scriptMoveEndPosition;
			_scriptMoveLookAt = lookAt;
			LookAt(_scriptMoveLookAt, Vector3.Up);
			AdoptCurrentCameraReading();
			_scriptCameraMode = ScriptCameraMode.None;
			return;
		}

		_scriptMoveLookAt = _scriptMoveStartLookAt;
		LookAt(_scriptMoveLookAt, Vector3.Up);
	}

	/// <summary>
	///     Makes the camera sit behind a unit and look at it, keeping the map's framing for as long as the unit lives.
	/// </summary>
	/// <param name="followTarget">The unit to follow.</param>
	/// <param name="distance">How far behind the unit the camera sits.</param>
	/// <param name="height">How far above the unit the camera sits.</param>
	/// <param name="yawOffsetDegrees">Which side of the unit the camera sits on, in degrees.</param>
	/// <param name="alignToUnitFacing">True to stay behind the unit's own facing, false to hold a fixed bearing.</param>
	/// <param name="smoothingSeconds">How quickly the camera catches up, in seconds of lag.</param>
	/// <param name="lookAtHeight">How far above the unit's origin the camera looks.</param>
	public void BeginScriptCameraFollowUnit(Unit3D followTarget, float distance, float height, float yawOffsetDegrees, bool alignToUnitFacing, float smoothingSeconds, float lookAtHeight)
	{
		_pendingFollowServerUnitId = 0;
		_scriptCameraHeld = true;
		_scriptCameraMode = ScriptCameraMode.FollowUnit;
		_scriptFollowTarget = followTarget;
		_scriptFollowDistance = Mathf.Max(distance, 0.5f);
		_scriptFollowHeight = height;
		_scriptFollowYawOffsetDegrees = yawOffsetDegrees;
		_scriptFollowAlignsToFacing = alignToUnitFacing;
		_scriptFollowSmoothingSeconds = Mathf.Max(smoothingSeconds, 0f);
		_scriptFollowLookAtHeight = lookAtHeight;

		UpdateScriptFollowCamera(0f, snapToTarget: true);
	}

	/// <summary>
	///     Remembers a follow request for a unit this screen is not drawing yet. A unit a map spawned in the same tick it asked
	///     for the shot reaches this screen in a description that has not been applied when the request lands, so the request
	///     waits for that description instead of being dropped or asked for twice.
	/// </summary>
	/// <param name="serverUnitId">The unit the map named.</param>
	/// <param name="distance">How far behind the unit the camera sits.</param>
	/// <param name="height">How far above the unit the camera sits.</param>
	/// <param name="yawOffsetDegrees">Which side of the unit the camera sits on.</param>
	/// <param name="alignToUnitFacing">True to stay behind the unit's own facing.</param>
	/// <param name="smoothingSeconds">How quickly the camera catches up.</param>
	/// <param name="lookAtHeight">How far above the unit's origin the camera looks.</param>
	public void SetPendingScriptFollow(int serverUnitId, float distance, float height, float yawOffsetDegrees, bool alignToUnitFacing, float smoothingSeconds, float lookAtHeight)
	{
		_scriptCameraHeld = true;
		_scriptCameraMode = ScriptCameraMode.None;
		_scriptFollowTarget = null;
		_pendingFollowServerUnitId = serverUnitId;
		_pendingFollowDistance = distance;
		_pendingFollowHeight = height;
		_pendingFollowYawOffsetDegrees = yawOffsetDegrees;
		_pendingFollowAlignsToFacing = alignToUnitFacing;
		_pendingFollowSmoothingSeconds = smoothingSeconds;
		_pendingFollowLookAtHeight = lookAtHeight;
	}

	/// <summary>
	///     Starts the remembered follow once the unit it names reaches this screen.
	/// </summary>
	/// <returns>True when a follow started this frame.</returns>
	private bool TryStartPendingFollow()
	{
		if (_pendingFollowServerUnitId <= 0) return false;
		if (GameContext.Host == null) return false;
		if (!GameContext.Host.TryGetLocalEntity(_pendingFollowServerUnitId, out Realm.Ecs.Entity localEntity)) return false;
		if (!GameHost.TryGetUnit3D(localEntity, out Unit3D? unit) || !GodotObject.IsInstanceValid(unit)) return false;

		int serverUnitId = _pendingFollowServerUnitId;
		_pendingFollowServerUnitId = 0;

		float distance = _pendingFollowDistance;
		float height = _pendingFollowHeight;
		float yawOffsetDegrees = _pendingFollowYawOffsetDegrees;
		bool alignToUnitFacing = _pendingFollowAlignsToFacing;
		float smoothingSeconds = _pendingFollowSmoothingSeconds;
		float lookAtHeight = _pendingFollowLookAtHeight;

		BeginScriptCameraFollowUnit(unit, distance, height, yawOffsetDegrees, alignToUnitFacing, smoothingSeconds, lookAtHeight);
		GameLog.LogToConsole($"[Cinematic] the camera is following unit {serverUnitId}, which reached this screen");
		return true;
	}

	/// <summary>
	///     Stops following the unit and holds the camera where it stands. The camera stays held by the map, so the player
	///     does not take over until it is released.
	/// </summary>
	public void StopScriptCameraFollow()
	{
		if (_scriptCameraMode != ScriptCameraMode.FollowUnit) return;

		_scriptCameraMode = ScriptCameraMode.None;
		_scriptFollowTarget = null;
		_pendingFollowServerUnitId = 0;
		AdoptCurrentCameraReading();
	}

	/// <summary>
	///     Gives the camera back to the player, ending any shot that was running. The camera keeps the place and angle the
	///     cutscene left it at, and the player's controls start from there.
	/// </summary>
	public void ReleaseScriptCamera()
	{
		_scriptCameraMode = ScriptCameraMode.None;
		_scriptCameraHeld = false;
		_scriptFollowTarget = null;
		_pendingFollowServerUnitId = 0;
		AdoptCurrentCameraReading();
	}

	/// <summary>
	///     Runs the shot that is running, and reports whether this frame belongs to it. The caller skips the player's own
	///     camera handling for that frame, which is what keeps a held camera out of the player's hands.
	/// </summary>
	/// <param name="deltaSeconds">How long the drawn frame took.</param>
	/// <returns>True when a map is driving the camera.</returns>
	private bool UpdateScriptCamera(float deltaSeconds)
	{
		if (TryStartPendingFollow()) return true;

		switch (_scriptCameraMode)
		{
			case ScriptCameraMode.MoveTo:
				UpdateScriptMoveCamera(deltaSeconds);
				return true;

			case ScriptCameraMode.FollowUnit:
				UpdateScriptFollowCamera(deltaSeconds, snapToTarget: false);
				return true;

			default:
				return _scriptCameraHeld;
		}
	}

	private void UpdateScriptMoveCamera(float deltaSeconds)
	{
		_scriptMoveElapsed += deltaSeconds;
		float progress = _scriptMoveDuration <= 0.0001f ? 1f : Mathf.Clamp(_scriptMoveElapsed / _scriptMoveDuration, 0f, 1f);

		// Eased at both ends, so a shot starts and stops as a camera operator would rather than as a lerp.
		float eased = progress * progress * (3f - 2f * progress);
		Position = KeepCameraAboveGround(_scriptMoveStartPosition.Lerp(_scriptMoveEndPosition, eased));
		_scriptMoveLookAt = _scriptMoveStartLookAt.Lerp(_scriptMoveEndLookAt, eased);
		LookAt(_scriptMoveLookAt, Vector3.Up);
		AdoptCurrentCameraReading();

		if (progress < 1f) return;

		// The shot holds where it arrived until another one replaces it or the camera is released.
		_scriptCameraMode = ScriptCameraMode.None;
	}

	private void UpdateScriptFollowCamera(float deltaSeconds, bool snapToTarget)
	{
		Unit3D? target = _scriptFollowTarget;
		if (target == null || !GodotObject.IsInstanceValid(target) || !GameContext.World.IsAlive(target.Entity))
		{
			// A unit that leaves the world leaves the camera where it stood rather than snapping it nowhere.
			_scriptCameraMode = ScriptCameraMode.None;
			_scriptFollowTarget = null;
			AdoptCurrentCameraReading();
			return;
		}

		float bearingDegrees = _scriptFollowYawOffsetDegrees;
		if (_scriptFollowAlignsToFacing)
		{
			float unitYawDegrees = Mathf.RadToDeg(target.Rotation.Y);
			bearingDegrees += unitYawDegrees + 180f;
		}

		float bearingRadians = Mathf.DegToRad(bearingDegrees);
		Vector3 behind = new Vector3(-Mathf.Sin(bearingRadians), 0f, -Mathf.Cos(bearingRadians));
		Vector3 desiredPosition = KeepCameraAboveGround(target.Position - behind * _scriptFollowDistance + Vector3.Up * _scriptFollowHeight);

		if (!snapToTarget && _scriptFollowSmoothingSeconds > 0.0001f)
		{
			// The lag is stated in seconds, which reads the same whatever the frame rate: a fifth of a second means the same
			// thing at 30 frames a second and at 144.
			float followRate = 1f - Mathf.Exp(-deltaSeconds / Mathf.Max(_scriptFollowSmoothingSeconds, 0.0001f));
			Position = Position.Lerp(desiredPosition, followRate);
		}
		else
		{
			Position = desiredPosition;
		}

		Vector3 lookAt = target.Position + Vector3.Up * _scriptFollowLookAtHeight;
		LookAt(lookAt, Vector3.Up);
		AdoptCurrentCameraReading();
	}

	/// <summary>
	///     Keeps a camera position out of the ground, which is the one thing a shot must never do: a camera under the terrain
	///     looks through it and shows the player the inside of the board.
	/// </summary>
	/// <param name="position">The position a shot asked for.</param>
	/// <returns>The same position, raised above the terrain where it was too low.</returns>
	private Vector3 KeepCameraAboveGround(Vector3 position)
	{
		RuntimeTerrain? terrain = GameContext.Host?.GroundTerrain;
		if (terrain == null) return position;

		terrain.GetHeightAndNormal(position.X, position.Z, out float groundHeight, out _);
		float floor = groundHeight + ScriptCameraMinimumGroundClearance;
		if (position.Y >= floor) return position;

		position.Y = floor;
		return position;
	}

	/// <summary>
	///     Writes the camera's current place and angle into the shared camera state, so the readings the HUD and the minimap
	///     take from that state match what is on screen, and so the player's own controls start from the shot's last frame
	///     when the camera is given back instead of snapping to where the camera was before the cutscene.
	/// </summary>
	private void AdoptCurrentCameraReading()
	{
		if (!HasCameraState) return;

		ref CameraState state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
		state.CurrentYaw = Mathf.PosMod(RotationDegrees.Y, 360f);
		state.TargetYaw = state.CurrentYaw;
		state.CurrentPitch = Mathf.Clamp(RotationDegrees.X, -89f, 89f);
		state.TargetPitch = state.CurrentPitch;
		state.CurrentHeight = Position.Y;
		state.TargetHeight = Position.Y;
		state.YawSwing = 0f;
		state.PitchSwing = 0f;
	}
}
