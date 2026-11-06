using System;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.Network;

public class UpnpService
{
	private Upnp? _upnp;
	public bool IsPortMapped { get; private set; }

	public async Task<bool> OpenPortAsync(int port)
	{
		return await Task.Run(() =>
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
		});
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
