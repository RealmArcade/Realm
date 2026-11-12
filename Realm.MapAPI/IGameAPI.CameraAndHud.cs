// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Smoothly pans the specified player's camera to the given 3D world position.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="position">The target position in 3D world space.</param>
	/// <param name="duration">The pan duration in seconds.</param>
	void PanPlayerCameraTo(int playerIndex, Vector3 position, float duration = 0f);

	/// <summary>
	/// Shakes the specified player's camera to simulate an explosion or impact.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="intensity">The shake strength multiplier.</param>
	/// <param name="duration">The time in seconds the shake lasts.</param>
	void ShakePlayerCamera(int playerIndex, float intensity, float duration);

	/// <summary>
	/// Restricts the specified player's camera movement limits in world space coordinates.
	/// Clamps the camera position immediately if currently out of bounds.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="minX">The minimum X world boundary.</param>
	/// <param name="minZ">The minimum Z world boundary.</param>
	/// <param name="maxX">The maximum X world boundary.</param>
	/// <param name="maxZ">The maximum Z world boundary.</param>
	void SetPlayerCameraBounds(int playerIndex, float minX, float minZ, float maxX, float maxZ);

	/// <summary>
	/// Restricts the specified player's camera movement limits to the specified named coordinate marker.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="coordinateName">The name of the authored coordinate marker defining the camera bounds.</param>
	void SetPlayerCameraBounds(int playerIndex, string coordinateName)
	{
		if (TryGetCoordinate(coordinateName, out var coord))
		{
			SetPlayerCameraBounds(playerIndex, coord);
		}
	}

	/// <summary>
	/// Restricts the specified player's camera movement limits to the specified coordinate bounding region.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="coordinate">The coordinate bounding region defining the camera bounds.</param>
	void SetPlayerCameraBounds(int playerIndex, Coordinate coordinate)
	{
		SetPlayerCameraBounds(
			playerIndex,
			MathF.Min(coordinate.Min.X, coordinate.Max.X),
			MathF.Min(coordinate.Min.Z, coordinate.Max.Z),
			MathF.Max(coordinate.Min.X, coordinate.Max.X),
			MathF.Max(coordinate.Min.Z, coordinate.Max.Z));
	}

	/// <summary>
	/// Restricts the specified player's camera movement limits in world space coordinates (alias for <see cref="SetPlayerCameraBounds(int, float, float, float, float)"/>).
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="minX">The minimum X world boundary.</param>
	/// <param name="minZ">The minimum Z world boundary.</param>
	/// <param name="maxX">The maximum X world boundary.</param>
	/// <param name="maxZ">The maximum Z world boundary.</param>
	void SetMapCameraBoundaries(int playerIndex, float minX, float minZ, float maxX, float maxZ) =>
		SetPlayerCameraBounds(playerIndex, minX, minZ, maxX, maxZ);

	/// <summary>
	/// Resets the specified player's camera movement boundaries back to the map default boundaries defined in metadata.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void ResetPlayerCameraBounds(int playerIndex);

	/// <summary>
	/// Resets the specified player's camera movement boundaries back to the map default boundaries (alias for <see cref="ResetPlayerCameraBounds(int)"/>).
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void ResetMapCameraBoundaries(int playerIndex) => ResetPlayerCameraBounds(playerIndex);

	/// <summary>
	/// Attaches the specified player's camera to follow a target unit with configurable distance, height offset, angles, and smoothing.
	/// Pass <see cref="UnitId.Invalid"/> to stop following while keeping current camera settings, or call <see cref="ResetPlayerCamera(int)"/> to restore defaults.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="unit">The unit to follow, or <see cref="UnitId.Invalid"/> to clear the follow target.</param>
	/// <param name="distance">Horizontal distance away from the followed unit in world units.</param>
	/// <param name="heightOffset">Vertical offset above the followed unit's position in world units.</param>
	/// <param name="pitchDegrees">Camera pitch angle in degrees (negative values look downward, e.g. -25 for chase view or -55 for RTS view).</param>
	/// <param name="yawOffsetDegrees">Horizontal yaw angle in degrees (world yaw when <paramref name="alignToUnitFacing"/> is false, or offset relative to unit facing when true).</param>
	/// <param name="alignToUnitFacing">True to orient the camera behind the unit's facing direction; false to use a fixed world yaw.</param>
	/// <param name="smoothing">Follow interpolation responsiveness in units per second; zero or negative snaps immediately.</param>
	/// <param name="lockCameraInput">True to prevent manual player camera panning, zooming, and rotation while active.</param>
	void SetPlayerCameraFollowUnit(
		int playerIndex,
		UnitId unit,
		float distance = 18.0f,
		float heightOffset = 8.0f,
		float pitchDegrees = -30.0f,
		float yawOffsetDegrees = 0.0f,
		bool alignToUnitFacing = false,
		float smoothing = 8.0f,
		bool lockCameraInput = false);

	/// <summary>
	/// Configures the specified player's camera orientation, height, and input lock state without attaching to a unit.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="yawDegrees">Target horizontal yaw angle in degrees.</param>
	/// <param name="pitchDegrees">Target vertical pitch angle in degrees.</param>
	/// <param name="height">Target camera height in world units.</param>
	/// <param name="lockCameraInput">True to prevent manual player camera panning, zooming, and rotation.</param>
	void SetPlayerCameraView(int playerIndex, float yawDegrees, float pitchDegrees, float height, bool lockCameraInput = false);

	/// <summary>
	/// Resets the specified player's camera follow target, orientation, height, and input lock back to default RTS controls.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void ResetPlayerCamera(int playerIndex);

	/// <summary>
	/// Sets whether a specific built-in HUD element is visible for the designated player.
	/// Persists across HUD refreshes and reconnects.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="element">The HUD element to show or hide.</param>
	/// <param name="visible">True to make the element visible; false to hide it.</param>
	void SetPlayerHudElementVisible(int playerIndex, HudElement element, bool visible);

	/// <summary>
	/// Toggles whether a specific built-in HUD element is visible for the designated player (alias for <see cref="SetPlayerHudElementVisible(int, HudElement, bool)"/>).
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="element">The HUD element to show or hide.</param>
	/// <param name="visible">True to make the element visible; false to hide it.</param>
	void ToggleUserInterfaceElement(int playerIndex, HudElement element, bool visible) =>
		SetPlayerHudElementVisible(playerIndex, element, visible);

	/// <summary>
	/// Selects the specified unit on the given player's client, clearing their previous selection.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="unit">The unit to select.</param>
	void SelectPlayerUnit(int playerIndex, UnitId unit);

	/// <summary>
	/// Clears the unit selection on the specified player's client.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void ClearPlayerSelection(int playerIndex);

	/// <summary>
	/// Retrieves all units currently selected by the specified player.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>A read-only span of units currently selected by that player.</returns>
	ReadOnlySpan<UnitId> GetPlayerSelectedUnits(int playerIndex);

	/// <summary>
	/// Displays floating feedback text on the specified player's screen.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="text">The text message to display.</param>
	/// <param name="color">The color of the text in RGB format.</param>
	void ShowPlayerFeedbackText(int playerIndex, string text, Vector3 color);

	/// <summary>
	/// Spawns a minimap ping on the specified player's client at the given 3D world position.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="position">The target position in 3D world space.</param>
	void PingPlayerMinimap(int playerIndex, Vector3 position);

	/// <summary>
	/// Plays the standard warning/alert audio sound on the specified player's client.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void PlayPlayerWarningSound(int playerIndex);

	/// <summary>
	/// Plays the standard button/command click audio sound on the specified player's client.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void PlayPlayerClickSound(int playerIndex);

	/// <summary>
	/// Enters building placement mode on the specified player's client for the given structure type.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="unitTypeId">The unit type ID of the building to place.</param>
	void StartPlayerBuildingPlacement(int playerIndex, string unitTypeId);
}
