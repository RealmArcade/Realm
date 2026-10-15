using System;
using System.Threading.Tasks;
using Godot;

namespace Realm.Client.Network;

public class UpnpService
{
	public static UpnpService Instance { get; } = new();

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
					GD.Print($"[UpnpService] UPnP discover returned: {discoverResult}");
					return false;
				}

				var gateway = _upnp.GetGateway();
				if (gateway == null || !gateway.IsValidGateway())
				{
					GD.Print("[UpnpService] No valid UPnP gateway found.");
					return false;
				}

				int mapResult = _upnp.AddPortMapping(port, port, "Realm RTS", "UDP");
				if (mapResult == (int)Upnp.UpnpResult.Success)
				{
					IsPortMapped = true;
					GD.Print($"[UpnpService] Successfully mapped UDP port {port} on router via UPnP.");
					return true;
				}

				GD.Print($"[UpnpService] AddPortMapping returned: {mapResult}");
				return false;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[UpnpService] Error mapping port: {ex.Message}");
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
				GD.Print($"[UpnpService] Deleted UDP port mapping for {port}.");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[UpnpService] Error deleting port mapping: {ex.Message}");
			}
			finally
			{
				IsPortMapped = false;
				_upnp = null;
			}
		}
	}
}
