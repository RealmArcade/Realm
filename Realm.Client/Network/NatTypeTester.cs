using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Godot;

namespace Realm.Client.Network;

public class NatTypeTester
{
	private const int StunPort = 19302;
	private const int StunResponseTimeoutMilliseconds = 1000;

	private const int StunBindingSuccessResponseType = 0x0101;
	private const int StunBindingErrorResponseType = 0x0111;

	private const int StunAttributeMappedAddress = 0x0001;
	private const int StunAttributeXorMappedAddress = 0x0020;
	private const int StunAttributeChangedAddress = 0x0005;

	private const byte StunAddressFamilyIpv4 = 0x01;

	private static readonly IPAddress GoogleStunAlternateServerAddress = IPAddress.Parse("74.125.200.127");

	public static async Task<NatType> DetermineNatTypeAsync(int testLocalPort = 0)
	{
		try
		{
			var stunAddresses = await Dns.GetHostAddressesAsync("stun.l.google.com");
			if (stunAddresses.Length == 0) return NatType.Open;

			var primaryServerAddress = stunAddresses[0];
			var alternateServerAddress = stunAddresses.Length > 1 ? stunAddresses[1] : GoogleStunAlternateServerAddress;

			return await EvaluateNatTypeAsync(primaryServerAddress, alternateServerAddress, testLocalPort);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[NatTypeTester] Error checking NAT: {ex.Message}");
			return NatType.PortRestrictedCone;
		}
	}

	public static async Task<StunResult> QueryStunOnPortAsync(IPAddress serverIp, int serverPort, int localPort)
	{
		return await QueryStunAsync(serverIp, serverPort, localPort);
	}

	private static async Task<NatType> EvaluateNatTypeAsync(IPAddress primaryServerAddress, IPAddress alternateServerAddress, int testLocalPort)
	{
		var test1 = await QueryStunAsync(primaryServerAddress, StunPort, testLocalPort);
		if (!test1.Success) return NatType.RestrictedCone;

		int boundPort = test1.LocalEndPoint != null ? test1.LocalEndPoint.Port : testLocalPort;

		if (IsSameEndPoint(test1.MappedEndPoint, test1.LocalEndPoint))
		{
			var test2Open = await QueryStunWithChangeRequestAsync(primaryServerAddress, StunPort, boundPort, changeIP: true, changePort: true);
			return test2Open.Success ? NatType.Open : NatType.Symmetric;
		}

		var test2 = await QueryStunWithChangeRequestAsync(primaryServerAddress, StunPort, boundPort, changeIP: true, changePort: true);
		if (test2.Success) return NatType.FullCone;

		var test1Alt = await QueryStunAsync(alternateServerAddress, StunPort, boundPort);
		if (test1Alt.Success && !IsSameEndPoint(test1.MappedEndPoint, test1Alt.MappedEndPoint))
		{
			return NatType.Symmetric;
		}

		var test3 = await QueryStunWithChangeRequestAsync(primaryServerAddress, StunPort, boundPort, changeIP: false, changePort: true);
		if (test3.Success) return NatType.RestrictedCone;

		return NatType.PortRestrictedCone;
	}

	private static bool IsSameEndPoint(IPEndPoint ep1, IPEndPoint ep2)
	{
		if (ep1 == null || ep2 == null) return false;
		return ep1.Address.Equals(ep2.Address) && ep1.Port == ep2.Port;
	}

	private static async Task<StunResult> QueryStunAsync(IPAddress serverIp, int serverPort, int localPort)
	{
		var result = new StunResult();
		using var udp = new UdpClient();
		udp.ExclusiveAddressUse = false;
		udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

		try
		{
			udp.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
			result.LocalEndPoint = (IPEndPoint)udp.Client.LocalEndPoint!;

			byte[] request = CreateStunBindingRequest();
			var serverEp = new IPEndPoint(serverIp, serverPort);

			await udp.SendAsync(request, request.Length, serverEp);

			var receiveTask = udp.ReceiveAsync();
			var timeoutTask = Task.Delay(StunResponseTimeoutMilliseconds);

			if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
			{
				var response = await receiveTask;
				return ParseStunResponse(response.Buffer, result);
			}
		}
		catch
		{
		}

		result.Success = false;
		return result;
	}

	private static async Task<StunResult> QueryStunWithChangeRequestAsync(IPAddress serverIp, int serverPort, int localPort, bool changeIP, bool changePort)
	{
		var result = new StunResult();
		using var udp = new UdpClient();
		udp.ExclusiveAddressUse = false;
		udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

		try
		{
			udp.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
			result.LocalEndPoint = (IPEndPoint)udp.Client.LocalEndPoint!;

			byte[] request = CreateStunChangeRequest(changeIP, changePort);
			var serverEp = new IPEndPoint(serverIp, serverPort);

			await udp.SendAsync(request, request.Length, serverEp);

			var receiveTask = udp.ReceiveAsync();
			var timeoutTask = Task.Delay(StunResponseTimeoutMilliseconds);

			if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
			{
				var response = await receiveTask;
				return ParseStunResponse(response.Buffer, result);
			}
		}
		catch
		{
		}

		result.Success = false;
		return result;
	}

	private static byte[] CreateStunBindingRequest()
	{
		byte[] packet = new byte[20];

		packet[0] = 0x00;
		packet[1] = 0x01;

		packet[2] = 0x00;
		packet[3] = 0x00;


		var rand = new Random();
		rand.NextBytes(new Span<byte>(packet, 4, 16));

		return packet;
	}

	private static byte[] CreateStunChangeRequest(bool changeIP, bool changePort)
	{
		byte[] packet = new byte[28];

		packet[0] = 0x00;
		packet[1] = 0x01;

		packet[2] = 0x00;
		packet[3] = 0x08;


		var rand = new Random();
		rand.NextBytes(new Span<byte>(packet, 4, 16));


		packet[20] = 0x00;
		packet[21] = 0x03;
		packet[22] = 0x00;
		packet[23] = 0x04;


		int flags = 0;
		if (changeIP) flags |= 0x02;
		if (changePort) flags |= 0x04;

		packet[27] = (byte)flags;

		return packet;
	}

	public static StunResult ParseStunResponse(byte[] response, StunResult result)
	{
		if (response.Length < 20)
		{
			result.Success = false;
			return result;
		}


		int messageType = (response[0] << 8) | response[1];
		if (messageType != StunBindingSuccessResponseType && messageType != StunBindingErrorResponseType)
		{
			result.Success = false;
			return result;
		}

		int length = (response[2] << 8) | response[3];
		int offset = 20;

		while (offset + 4 <= response.Length && offset < 20 + length)
		{
			int attrType = (response[offset] << 8) | response[offset + 1];
			int attrLength = (response[offset + 2] << 8) | response[offset + 3];
			offset += 4;

			if (offset + attrLength > response.Length)
				break;

			if (attrType == StunAttributeMappedAddress || attrType == StunAttributeXorMappedAddress)
			{
				result.MappedEndPoint = ParseAddress(response, offset, attrLength, attrType == StunAttributeXorMappedAddress);
			}
			else if (attrType == StunAttributeChangedAddress)
			{
				result.ChangedEndPoint = ParseAddress(response, offset, attrLength, false);
			}


			offset += (attrLength + 3) & ~3;
		}

		result.Success = result.MappedEndPoint != null;
		return result;
	}

	private static IPEndPoint ParseAddress(byte[] response, int offset, int length, bool isXor)
	{
		if (length < 8) return null;

		byte addressFamily = response[offset + 1];
		if (addressFamily != StunAddressFamilyIpv4) return null;

		int port = (response[offset + 2] << 8) | response[offset + 3];
		if (isXor)
		{
			port ^= 0x2112;
		}

		byte[] ipBytes = new byte[4];
		Array.Copy(response, offset + 4, ipBytes, 0, 4);

		if (isXor)
		{
			ipBytes[0] ^= 0x21;
			ipBytes[1] ^= 0x12;
			ipBytes[2] ^= 0xA4;
			ipBytes[3] ^= 0x42;
		}

		var ip = new IPAddress(ipBytes);
		return new IPEndPoint(ip, port);
	}
}