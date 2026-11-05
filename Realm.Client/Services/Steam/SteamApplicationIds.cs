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
}
