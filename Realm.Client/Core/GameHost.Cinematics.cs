using System;
using Godot;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.Client.Core;

/// <summary>
///     The client side of the cinematic camera surface: turning a camera request from the map into a shot on this screen's
///     camera, and dressing the screen while it runs. A request addressed to one seat is applied only by the screen sitting
///     in it; one addressed to every player is applied by every screen in the match.
/// </summary>
public partial class GameHost
{
	private CinematicOverlay? _cinematicOverlay;

	/// <summary>
	///     The overlay this match has been given, created the first time a map frames or narrates the screen. It is a child of
	///     this host, so it leaves the tree with the match that asked for it.
	/// </summary>
	private CinematicOverlay Cinematic
	{
		get
		{
			if (GodotObject.IsInstanceValid(_cinematicOverlay)) return _cinematicOverlay!;

			var overlay = new CinematicOverlay
			{
				Name = "CinematicOverlay_CanvasLayer",
				Layer = 90
			};

			AddChild(overlay);
			_cinematicOverlay = overlay;
			return overlay;
		}
	}

	private CameraControl? CinematicCamera => MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");

	/// <summary>
	///     Holds this screen's camera for a cutscene, or gives the player's controls back.
	/// </summary>
	/// <param name="held">True to hold the camera.</param>
	public void HoldCinematicCameraInternal(bool held)
	{
		Callable.From(() => CinematicCamera?.SetScriptCameraHeld(held)).CallDeferred();
	}

	/// <summary>
	///     Sends this screen's camera to a stated place, looking at a stated point.
	/// </summary>
	/// <param name="position">Where the camera goes.</param>
	/// <param name="lookAt">The point it is pointed at.</param>
	/// <param name="durationSeconds">How long the move takes, or zero for a cut.</param>
	public void MoveCinematicCameraInternal(Vector3 position, Vector3 lookAt, float durationSeconds)
	{
		Callable.From(() => CinematicCamera?.BeginScriptCameraMoveTo(position, lookAt, durationSeconds)).CallDeferred();
	}

	/// <summary>
	///     Makes this screen's camera follow a unit from the server, looking at it as it moves.
	/// </summary>
	/// <param name="serverUnitId">The unit to follow, as the authority names it.</param>
	/// <param name="distance">How far behind the unit the camera sits.</param>
	/// <param name="height">How far above the unit the camera sits.</param>
	/// <param name="yawOffsetDegrees">Which side of the unit the camera sits on.</param>
	/// <param name="alignToUnitFacing">True to stay behind the unit's own facing.</param>
	/// <param name="smoothingSeconds">How quickly the camera catches up.</param>
	/// <param name="lookAtHeight">How far above the unit's origin the camera looks.</param>
	public void FollowUnitCinematicCameraInternal(int serverUnitId, float distance, float height, float yawOffsetDegrees, bool alignToUnitFacing, float smoothingSeconds, float lookAtHeight)
	{
		Callable.From(() =>
		{
			CameraControl? camera = CinematicCamera;
			if (camera == null) return;

			if (!TryResolveCinematicFollowUnit(serverUnitId, out Unit3D? unit))
			{
				// A unit this screen has not been told about yet keeps the request's framing; the next description of the unit
				// resolves it, so the shot does not have to be asked for twice.
				camera.SetPendingScriptFollow(serverUnitId, distance, height, yawOffsetDegrees, alignToUnitFacing, smoothingSeconds, lookAtHeight);
				return;
			}

			camera.BeginScriptCameraFollowUnit(unit, distance, height, yawOffsetDegrees, alignToUnitFacing, smoothingSeconds, lookAtHeight);
		}).CallDeferred();
	}

	/// <summary>
	///     Stops this screen's camera following its unit and holds it where it stands.
	/// </summary>
	public void StopCinematicCameraFollowInternal()
	{
		Callable.From(() => CinematicCamera?.StopScriptCameraFollow()).CallDeferred();
	}

	/// <summary>
	///     Gives this screen's camera back to the player.
	/// </summary>
	public void ReleaseCinematicCameraInternal()
	{
		Callable.From(() => CinematicCamera?.ReleaseScriptCamera()).CallDeferred();
	}

	/// <summary>
	///     Frames this screen with the cinematic bars, or clears them.
	/// </summary>
	/// <param name="visible">True to draw the bars.</param>
	public void SetCinematicBarsInternal(bool visible)
	{
		Callable.From(() => Cinematic.SetBarsVisible(visible)).CallDeferred();
	}

	/// <summary>
	///     Shows one line of cinematic dialogue on this screen.
	/// </summary>
	/// <param name="speaker">Who is speaking, or an empty string for narration.</param>
	/// <param name="line">The line itself.</param>
	/// <param name="durationSeconds">How long it stays on screen.</param>
	public void ShowCinematicSubtitleInternal(string speaker, string line, float durationSeconds)
	{
		Callable.From(() => Cinematic.ShowSubtitle(speaker, line, durationSeconds)).CallDeferred();
	}

	/// <summary>
	///     Fades this screen toward black, or back from it.
	/// </summary>
	/// <param name="alpha">How opaque the fade becomes.</param>
	/// <param name="durationSeconds">How long the fade takes.</param>
	public void SetCinematicFadeInternal(float alpha, float durationSeconds)
	{
		Callable.From(() => Cinematic.SetFade(alpha, durationSeconds)).CallDeferred();
	}

	/// <summary>
	///     Removes everything the cinematic surface put over this screen, which a match ending does.
	/// </summary>
	public void ClearCinematicOverlayInternal()
	{
		Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(_cinematicOverlay)) return;

			_cinematicOverlay!.ClearContent();
		}).CallDeferred();
	}

	/// <summary>
	///     Resolves the unit a camera shot is following, through the ids the authority names units by.
	/// </summary>
	/// <param name="serverUnitId">The unit the map named.</param>
	/// <param name="unit">The node drawing it on this screen.</param>
	/// <returns>True when this screen is drawing that unit.</returns>
	private bool TryResolveCinematicFollowUnit(int serverUnitId, out Unit3D? unit)
	{
		unit = null;
		if (serverUnitId <= 0) return false;
		if (!TryGetLocalEntity(serverUnitId, out Entity localEntity)) return false;
		if (!TryGetUnit3D(localEntity, out Unit3D? resolved)) return false;
		if (!GodotObject.IsInstanceValid(resolved)) return false;

		unit = resolved;
		return true;
	}

	/// <summary>
	///     Applies one camera or cinematic request the match delivered, addressing it the way the map did.
	/// </summary>
	/// <param name="message">The request.</param>
	private void ApplyCinematicPresentation(in PresentationMessage message)
	{
		bool everyPlayer = message.PlayerIndex < 0;

		switch ((PresentationKind)message.Kind)
		{
			case PresentationKind.CameraScriptLockChanged:
				if (everyPlayer || GameContext.PerspectiveFilter.CanControl(message.PlayerIndex)) HoldCinematicCameraInternal(message.Flag);
				break;

			case PresentationKind.CameraScriptMoveTo:
			{
				var position = new Vector3(message.Position.X, message.Position.Y, message.Position.Z);
				var lookAt = new Vector3(message.Secondary.X, message.Secondary.Y, message.Secondary.Z);
				if (everyPlayer || GameContext.PerspectiveFilter.CanControl(message.PlayerIndex)) MoveCinematicCameraInternal(position, lookAt, message.Duration);
				break;
			}

			case PresentationKind.CameraScriptFollowUnit:
				if (everyPlayer || GameContext.PerspectiveFilter.CanControl(message.PlayerIndex))
				{
					FollowUnitCinematicCameraInternal(message.Handle, message.Position.X, message.Position.Y, message.Position.Z, message.Flag, message.Duration, message.Value);
				}

				break;

			case PresentationKind.CameraScriptFollowStopped:
				if (everyPlayer || GameContext.PerspectiveFilter.CanControl(message.PlayerIndex)) StopCinematicCameraFollowInternal();
				break;

			case PresentationKind.CameraScriptReleased:
				if (everyPlayer || GameContext.PerspectiveFilter.CanControl(message.PlayerIndex)) ReleaseCinematicCameraInternal();
				break;

			case PresentationKind.CinematicBarsVisibility:
				SetCinematicBarsInternal(message.Flag);
				break;

			case PresentationKind.CinematicSubtitle:
				ShowCinematicSubtitleInternal(message.Text, message.Text2, message.Duration);
				break;

			case PresentationKind.ScreenFadeSet:
				SetCinematicFadeInternal(message.Value, message.Duration);
				break;
		}
	}
}
