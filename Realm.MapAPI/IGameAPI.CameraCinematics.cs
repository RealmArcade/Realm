using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
///     The camera and cinematic surface a map may drive: taking the camera away from the player for a shot, moving it,
///     following a unit with it, and dressing the screen with the bars, subtitles and fades a cutscene needs. Every member
///     is opt-in - the engine never takes the camera away from a player on its own - so a map that says nothing about
///     cinematics leaves the camera exactly as the player expects it.
/// </summary>
/// <remarks>
///     <para>
///         A camera shot is a request the match sends to the screen sitting in the seat it names, on the reliable stream, so
///         a shot that is stated is a shot that arrives. A map driving a cutscene for one player names that player's slot;
///         one driving it for everybody, which is what a co-operative campaign or a cinematic between rounds wants, names
///         <see cref="EveryPlayer" />.
///     </para>
///     <para>
///         Taking the camera means taking it away from the person holding the mouse: <see cref="MovePlayerCameraTo" />,
///         <see cref="FollowUnitWithPlayerCamera" /> and <see cref="LockPlayerCamera" /> hold the player's own panning,
///         zooming, edge scrolling and rotation for as long as the shot runs, and <see cref="ReleasePlayerCamera" /> gives
///         it back. A map that takes the camera and never releases it leaves that player unable to look at their own base,
///         so every shot is expected to end. Nothing about the match changes while the camera is held: the player still
///         commands units, still builds, and still plays.
///     </para>
///     <para>
///         The camera is presentation only. What a screen can see is decided by the shroud and by what that player owns, so
///         a shot that flies over ground the player has not explored shows only what that player may already see. A map that
///         wants a cutscene to reveal the land itself calls <see cref="DisableShroud" /> or arranges its own reveal first.
///     </para>
/// </remarks>
public partial interface IGameAPI
{
	/// <summary>
	///     The player index that names every screen in the match, which is what a cutscene every player watches is addressed
	///     with.
	/// </summary>
	const int EveryPlayer = -1;

	/// <summary>
	///     Holds the player's own camera controls, or gives them back, without moving the camera.
	/// </summary>
	/// <param name="playerIndex">The player slot whose controls are held, or <see cref="EveryPlayer" />.</param>
	/// <param name="locked">True to hold the controls, false to give them back.</param>
	/// <remarks>
	///     This is the smallest part of the cinematic surface: a map that only wants to stop a player from panning away
	///     during a briefing uses this and nothing else. <see cref="ReleasePlayerCamera" /> also gives the controls back,
	///     along with everything else a shot changed.
	/// </remarks>
	void LockPlayerCamera(int playerIndex, bool locked);

	/// <summary>
	///     Sends the player's camera to a stated place, looking at a stated point, holding the player's own controls for the
	///     shot.
	/// </summary>
	/// <param name="playerIndex">The player slot whose camera is moved, or <see cref="EveryPlayer" />.</param>
	/// <param name="position">Where the camera goes, in world space.</param>
	/// <param name="lookAt">The point the camera is pointed at for the whole move, in world space.</param>
	/// <param name="durationSeconds">
	///     How long the move takes. Zero cuts straight to the shot, which is what a sequence of shots uses between its beats;
	///     a positive duration flies the camera there over that time, eased at both ends.
	/// </param>
	/// <remarks>
	///     A move is a shot rather than a pan: the camera is pointed at <paramref name="lookAt" /> from the first frame, so it
	///     reads as a camera moving through the world instead of a map being scrolled. The camera is kept above the ground so
	///     a shot never ends up inside a hill, and the player's controls stay held until the move finishes or
	///     <see cref="ReleasePlayerCamera" /> is called.
	/// </remarks>
	void MovePlayerCameraTo(int playerIndex, Vector3 position, Vector3 lookAt, float durationSeconds);

	/// <summary>
	///     Makes the player's camera follow a unit, looking at it while it moves.
	/// </summary>
	/// <param name="playerIndex">The player slot whose camera follows, or <see cref="EveryPlayer" />.</param>
	/// <param name="unit">The unit to follow. An invalid unit stops the follow and holds the camera where it is.</param>
	/// <param name="distance">How far behind the unit the camera sits, in metres.</param>
	/// <param name="height">How far above the unit the camera sits, in metres.</param>
	/// <param name="yawOffsetDegrees">
	///     Which side of the unit the camera sits on. When <paramref name="alignToUnitFacing" /> is false this is a world
	///     direction, so the camera keeps its bearing however the unit turns; when it is true it is the angle behind the
	///     unit's own facing, so the camera stays behind it.
	/// </param>
	/// <param name="alignToUnitFacing">True to keep the camera behind the unit's facing, false to hold a fixed bearing.</param>
	/// <param name="smoothingSeconds">
	///     How quickly the camera catches up with the unit, in seconds of lag. Zero follows rigidly, which suits a racing
	///     camera; a value around a fifth of a second reads as a camera operator following a character.
	/// </param>
	/// <param name="lookAtHeight">How far above the unit's origin the camera looks, in metres, so the shot frames the body rather than the feet.</param>
	/// <remarks>
	///     The follow lasts until <see cref="StopPlayerCameraFollow" />, another shot replaces it, or
	///     <see cref="ReleasePlayerCamera" /> gives the camera back. A unit that dies while it is being followed leaves the
	///     camera where it stood rather than snapping to a corpse that no longer exists.
	/// </remarks>
	void FollowUnitWithPlayerCamera(int playerIndex, UnitId unit, float distance, float height, float yawOffsetDegrees, bool alignToUnitFacing, float smoothingSeconds, float lookAtHeight = 1.5f);

	/// <summary>
	///     Stops the camera following its unit and holds it where it stands.
	/// </summary>
	/// <param name="playerIndex">The player slot whose camera stops following, or <see cref="EveryPlayer" />.</param>
	/// <remarks>
	///     The player's own controls stay held: this ends the follow, not the cutscene. Use
	///     <see cref="ReleasePlayerCamera" /> to give the camera back.
	/// </remarks>
	void StopPlayerCameraFollow(int playerIndex);

	/// <summary>
	///     Gives the camera back to the player, ending any shot that was running.
	/// </summary>
	/// <param name="playerIndex">The player slot whose camera is given back, or <see cref="EveryPlayer" />.</param>
	/// <remarks>
	///     The camera keeps the place and angle the cutscene left it at, and the player's controls start from there: a map
	///     that wants the player to come back to their base moves the camera there before releasing it, and one that leaves
	///     it wherever the last shot ended hands the player a camera looking at whatever the story ended on.
	/// </remarks>
	void ReleasePlayerCamera(int playerIndex);

	/// <summary>
	///     Draws or clears the cinematic bars above and below the screen.
	/// </summary>
	/// <param name="playerIndex">The player slot whose screen is framed, or <see cref="EveryPlayer" />.</param>
	/// <param name="visible">True to draw the bars, false to clear them.</param>
	/// <remarks>
	///     The bars are only framing: they change nothing about the camera or the match, and a map that shows them is
	///     expected to clear them when its cutscene ends. They stay out of the way of the HUD, so a cutscene that leaves the
	///     player in full control still shows the controls they are using.
	/// </remarks>
	void SetCinematicBarsVisible(int playerIndex, bool visible);

	/// <summary>
	///     Shows one line of cinematic dialogue, with the speaker named above it.
	/// </summary>
	/// <param name="playerIndex">The player slot whose screen shows the line, or <see cref="EveryPlayer" />.</param>
	/// <param name="speaker">Who is speaking, as the map names them. Pass an empty string for narration.</param>
	/// <param name="line">The line itself, already translated by the map if it carries text from its own catalogs.</param>
	/// <param name="durationSeconds">How long the line stays on screen, in seconds. A duration of zero clears the line at once.</param>
	/// <remarks>
	///     A line replaces the one before it, so a map that shows lines in sequence shows each for its own duration and does
	///     not have to clear the previous one. Showing a line does not hold the player's camera controls; a map running a
	///     briefing pairs it with <see cref="LockPlayerCamera" />.
	/// </remarks>
	void ShowCinematicSubtitle(int playerIndex, string speaker, string line, float durationSeconds);

	/// <summary>
	///     Fades the screen toward black, or back from it.
	/// </summary>
	/// <param name="playerIndex">The player slot whose screen fades, or <see cref="EveryPlayer" />.</param>
	/// <param name="alpha">How opaque the fade is once it finishes: 0 is a clear screen and 1 is fully black.</param>
	/// <param name="durationSeconds">How long the fade takes, in seconds. Zero changes it at once.</param>
	/// <remarks>
	///     A fade covers the whole screen including the HUD, which is what a scene change needs; it does not stop the match,
	///     so a map that fades to black and moves the camera underneath is revealing a new place rather than pausing the
	///     game. The fade stays where it is left, so a map that faded in is expected to fade back out.
	/// </remarks>
	void SetScreenFade(int playerIndex, float alpha, float durationSeconds);
}
