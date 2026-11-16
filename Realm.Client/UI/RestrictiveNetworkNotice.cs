using Godot;

namespace Realm.Client.UI;

public static class RestrictiveNetworkNotice
{
	public const string Message = "Your ISP has a restrictive firewall that cannot be bypassed. You may have trouble hosting or joining lobbies. Contact our discord channel for troubleshooting advice.";

	public static string Text() => TranslationServer.Translate(Message);
}
