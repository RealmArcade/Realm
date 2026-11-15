using System;
using System.Numerics;
using Realm.Ecs;
using Realm.MapAPI;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The cinematic surface of the map API: camera shots, camera holds and the presentation that dresses a cutscene. The
///     authority owns no camera, so each call is written as a request the addressed screens apply; a request naming
///     <see cref="IGameAPI.EveryPlayer" /> travels to every screen in the match.
/// </summary>
public partial class ServerGameAPI
{
	public void LockPlayerCamera(int playerIndex, bool locked)
	{
		if (!CanAddressCamera(playerIndex, "hold the camera")) return;

		_presentation.Write(PresentationKind.CameraScriptLockChanged, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, locked);
	}

	public void MovePlayerCameraTo(int playerIndex, Vector3 position, Vector3 lookAt, float durationSeconds)
	{
		if (!CanAddressCamera(playerIndex, "move the camera")) return;
		if (!IsFinite(position) || !IsFinite(lookAt))
		{
			_log.Write($"player {playerIndex} was sent a camera shot to a position that is not a place on the board");
			return;
		}

		_presentation.Write(PresentationKind.CameraScriptMoveTo, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty,
			null, position, lookAt, Vector3.Zero, 0, 0, 0f, 0f, MathF.Max(durationSeconds, 0f), false);
	}

	public void FollowUnitWithPlayerCamera(int playerIndex, UnitId unit, float distance, float height, float yawOffsetDegrees, bool alignToUnitFacing, float smoothingSeconds, float lookAtHeight = 1.5f)
	{
		if (!CanAddressCamera(playerIndex, "follow a unit with the camera")) return;

		Entity followTarget = unit.IsValid ? ResolveUnit(unit) : Entity.Null;
		if (followTarget == Entity.Null)
		{
			// An invalid unit is the documented way to stop following, which holds the camera where it stands rather than
			// leaving a shot running after the unit a map meant to follow is gone.
			StopPlayerCameraFollow(playerIndex);
			return;
		}

		_presentation.Write(PresentationKind.CameraScriptFollowUnit, playerIndex, followTarget.Id, string.Empty, string.Empty, string.Empty, string.Empty,
			null, new Vector3(MathF.Max(distance, 0f), height, yawOffsetDegrees), Vector3.Zero, Vector3.Zero, 0, 0, lookAtHeight, 0f,
			MathF.Max(smoothingSeconds, 0f), alignToUnitFacing);
	}

	public void StopPlayerCameraFollow(int playerIndex)
	{
		if (!CanAddressCamera(playerIndex, "stop following with the camera")) return;

		_presentation.Write(PresentationKind.CameraScriptFollowStopped, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void ReleasePlayerCamera(int playerIndex)
	{
		if (!CanAddressCamera(playerIndex, "release the camera")) return;

		_presentation.Write(PresentationKind.CameraScriptReleased, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, false);
	}

	public void SetCinematicBarsVisible(int playerIndex, bool visible)
	{
		if (!CanAddressPresentation(playerIndex, "frame the screen")) return;

		_presentation.Write(PresentationKind.CinematicBarsVisibility, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, 0f, visible);
	}

	public void ShowCinematicSubtitle(int playerIndex, string speaker, string line, float durationSeconds)
	{
		if (!CanAddressPresentation(playerIndex, "show a subtitle")) return;

		_presentation.Write(PresentationKind.CinematicSubtitle, playerIndex, 0, string.Empty, speaker ?? string.Empty, line ?? string.Empty,
			string.Empty, null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0f, 0f, MathF.Max(durationSeconds, 0f), false);
	}

	public void SetScreenFade(int playerIndex, float alpha, float durationSeconds)
	{
		if (!CanAddressPresentation(playerIndex, "fade the screen")) return;

		float clampedAlpha = float.IsFinite(alpha) ? Math.Clamp(alpha, 0f, 1f) : 0f;
		_presentation.Write(PresentationKind.ScreenFadeSet, playerIndex, 0, string.Empty, string.Empty, string.Empty, string.Empty,
			null, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, clampedAlpha, 0f, MathF.Max(durationSeconds, 0f), false);
	}

	/// <summary>
	///     Whether a call that takes a player's camera can be addressed anywhere: a camera belongs to a seat, so a call naming
	///     nobody, or a slot beyond the ones a match may seat, is dropped rather than queued for a screen that will never exist.
	///     A computer seat is accepted and simply has no screen to apply it.
	/// </summary>
	/// <param name="playerIndex">The seat the call names, or <see cref="IGameAPI.EveryPlayer" />.</param>
	/// <param name="description">What the call does, for the log line.</param>
	/// <returns>True when the call may be sent.</returns>
	private bool CanAddressCamera(int playerIndex, string description) => CanAddressSeat(playerIndex, description);

	/// <summary>
	///     Whether a call that dresses the screen can be addressed anywhere. Framing and dialogue belong to a screen rather
	///     than to a camera, so a computer seat is accepted: nothing is drawn for it and nothing is lost.
	/// </summary>
	/// <param name="playerIndex">The seat the call names, or <see cref="IGameAPI.EveryPlayer" />.</param>
	/// <param name="description">What the call does, for the log line.</param>
	/// <returns>True when the call may be sent.</returns>
	private bool CanAddressPresentation(int playerIndex, string description) => CanAddressSeat(playerIndex, description);

	/// <summary>
	///     Whether a request can be addressed to the seat it names, or to every seat in the match.
	/// </summary>
	/// <param name="playerIndex">The seat the request names, or <see cref="IGameAPI.EveryPlayer" />.</param>
	/// <param name="description">What the request does, for the log line.</param>
	/// <returns>True when the request may be sent.</returns>
	private bool CanAddressSeat(int playerIndex, string description)
	{
		if (playerIndex == IGameAPI.EveryPlayer) return true;
		if (playerIndex > 0 && playerIndex < EcsSharedMemory.MAX_PLAYERS) return true;

		_log.Write($"a map asked player {playerIndex} to {description}, which is not a seat a match can have");
		return false;
	}

	private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
