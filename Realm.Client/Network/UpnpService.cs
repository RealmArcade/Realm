using System;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.Network;

public class UpnpService
{
	private static readonly TimeSpan GatewayDiscoveryBudget = TimeSpan.FromSeconds(5);

	private Upnp? _upnp;
	public bool IsPortMapped { get; private set; }

	public async Task<bool> OpenPortAsync(int port)
	{
		Task<bool> mapping = Task.Run(() => MapPortOrReport(port));

		try
		{
			return await mapping.WaitAsync(GatewayDiscoveryBudget);
		}
		catch (TimeoutException)
		{
			GameLog.LogToConsole($"[UpnpService] No router answered a gateway search within {GatewayDiscoveryBudget.TotalSeconds:0} s, so UDP port {port} is opened without a mapping.");
			return false;
		}
	}

	private bool MapPortOrReport(int port)
	{
			try
			{
				_upnp = new Upnp();
				int discoverResult = _upnp.Discover();
				if (discoverResult != (int)Upnp.UpnpResult.Success)
				{
					GameLog.LogToConsole($"[UpnpService] UPnP discover returned: {discoverResult}");
					return false;
				}

				var gateway = _upnp.GetGateway();
				if (gateway == null || !gateway.IsValidGateway())
				{
					GameLog.LogToConsole("[UpnpService] No valid UPnP gateway found.");
					return false;
				}

				int mapResult = _upnp.AddPortMapping(port, port, "Realm RTS", "UDP");
				if (mapResult == (int)Upnp.UpnpResult.Success)
				{
					IsPortMapped = true;
					GameLog.LogToConsole($"[UpnpService] Successfully mapped UDP port {port} on router via UPnP.");
					return true;
				}

				GameLog.LogToConsole($"[UpnpService] AddPortMapping returned: {mapResult}");
				return false;
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[UpnpService] Error mapping port: {ex.Message}");
				return false;
			}
	}

	/// <summary>
	///     The address the router gives its own outside face, which is the network the mapping a lobby created is reachable
	///     from. A router behind another one answers with an address of its own rather than the one a STUN query measured, and
	///     a mapping it accepts then moves nothing between the two networks.
	/// </summary>
	/// <returns>The address the router states, or an empty string when it maps no port or states nothing readable.</returns>
	public string QueryExternalAddress()
	{
		if (_upnp == null || !IsPortMapped) return string.Empty;

		try
		{
			return _upnp.GetGateway()?.QueryExternalAddress() ?? string.Empty;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[UpnpService] Error asking the router for its external address: {exception.Message}");
			return string.Empty;
		}
	}

	public void ClosePort(int port)
	{
		if (_upnp != null && IsPortMapped)
		{
			try
			{
				_upnp.DeletePortMapping(port, "UDP");
				GameLog.LogToConsole($"[UpnpService] Deleted UDP port mapping for {port}.");
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[UpnpService] Error deleting port mapping: {ex.Message}");
			}
			finally
			{
				IsPortMapped = false;
				_upnp = null;
			}
		}
	}
}
