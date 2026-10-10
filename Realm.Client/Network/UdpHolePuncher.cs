using Godot;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Realm.Client.Network;

public class UdpHolePuncher
{
	public static async Task PunchHoleAsync(string remoteIp, int remotePort, int localPort, int packetCount = 8)
	{
		GD.Print($"[HolePuncher] Punching hole to {remoteIp}:{remotePort} from local port {localPort}...");
        
		using var udp = new UdpClient();
		udp.ExclusiveAddressUse = false;
		udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        
		try
		{
			udp.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
            
			byte[] payload = Encoding.UTF8.GetBytes("PUNCH");
			var remoteEp = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
            
			for (int i = 0; i < packetCount; i++)
			{
				await udp.SendAsync(payload, payload.Length, remoteEp);
				await Task.Delay(25); // wait 25ms between packet bursts
			}
            
			GD.Print($"[HolePuncher] Hole punch bursts sent ({packetCount} packets).");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[HolePuncher] Error during hole punch: {ex.Message}");
		}
		finally
		{

			udp.Close();
		}
	}
}