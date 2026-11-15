using System.Net.NetworkInformation;

namespace Realm.Shared.Reachability;

/// <summary>
///     Reads whether this machine has a VPN or tunnel interface up. It is the only part of a VPN a machine can see for
///     itself: the routes a tunnel installs are ordinary routes, and a virtual adapter that is not a tunnel at all looks
///     like a network card. What it buys is a host's own evidence that a tunnel is present while a measurement is failing,
///     which is what makes "a VPN may be in the way" a statement about this machine rather than a guess about the world.
/// </summary>
public static class LocalTunnelAdapters
{
	/// <summary>
	///     Whether an interface that carries tunnelled or dial-up traffic is up. Tunnel adapters (VPN, WireGuard, a tap
	///     driver) and point-to-point adapters (modem, mobile broadband) are the two families the operating system distinguishes
	///     for this, and both are read.
	/// </summary>
	/// <returns>Whether such an interface is up right now.</returns>
	public static bool AnyUp()
	{
		NetworkInterface[] interfaces;
		try
		{
			interfaces = NetworkInterface.GetAllNetworkInterfaces();
		}
		catch (NetworkInformationException)
		{
			// An operating system that will not enumerate its interfaces leaves this unanswered rather than answered wrongly.
			return false;
		}

		for (int index = 0; index < interfaces.Length; index++)
		{
			NetworkInterface networkInterface = interfaces[index];
			if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
			if (networkInterface.NetworkInterfaceType is not (NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2)) continue;

			return true;
		}

		return false;
	}
}
