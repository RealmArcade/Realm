namespace Realm.Client.Services;

/// <summary>
///     The Steam applications this build is shipped as. The demo exists only so the game can be installed and played before
///     the base game is released, and the base game is free to play, so the two applications differ in nothing but the id
///     Steam launches the process under. The same build is uploaded to both, which is why no feature may depend on which one
///     is running.
/// </summary>
public static class SteamApplicationIds
{
	public const uint FullGameAppId = 5372260;

	public const uint DemoAppId = 5537420;

	/// <summary>
	///     The cosmetic DLC that carries the supporter flair, or zero while no such product exists. Nothing in the game is
	///     gated on it: the flair is a badge a player may wear and every map, mode and seat stays free. A product that could
	///     grant a mode, a unit, a seat or an advantage would be a different thing, and the game reads this as appearance only.
	/// </summary>
	public const uint SupporterFlairDlcAppId = 0;
}
