namespace Realm.Client.Services;

/// <summary>
///     The Steam applications this build belongs to. A demo is an application of its own with its own depot, so the id the
///     process is launched and licensed as is never the id a wishlist button opens.
/// </summary>
public static class SteamApplicationIds
{
	public const uint DemoAppId = 1416294;

	public const uint FullGameAppId = 0;
}
