using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public class NatTypeTester
{
    public class StunResult
    {
        public bool Success { get; set; }
        public IPEndPoint LocalEndPoint { get; set; }
        public IPEndPoint MappedEndPoint { get; set; }
        public IPEndPoint ChangedEndPoint { get; set; }
    }

    public static async Task<NatType> DetermineNatTypeAsync(int testLocalPort = 0)
    {
        try
        {
            var stunAddresses = await Dns.GetHostAddressesAsync("stun.l.google.com");
            if (stunAddresses.Length == 0) return NatType.Open;

            var serverIp1 = stunAddresses[0];
            var serverIp2 = stunAddresses.Length > 1 ? stunAddresses[1] : IPAddress.Parse("74.125.200.127"); // google stun alternate

            return await EvaluateNatTypeAsync(serverIp1, serverIp2, testLocalPort);
        }
        catch (Exception ex)
        {
            Godot.GD.PrintErr($"[NatTypeTester] Error checking NAT: {ex.Message}");
            return NatType.PortRestrictedCone; // Safe default
        }
    }

    private static async Task<NatType> EvaluateNatTypeAsync(IPAddress serverIp1, IPAddress serverIp2, int testLocalPort)
    {
        var test1 = await QueryStunAsync(serverIp1, 19302, testLocalPort);
        if (!test1.Success) return NatType.RestrictedCone;

        int boundPort = test1.LocalEndPoint != null ? test1.LocalEndPoint.Port : testLocalPort;

        if (IsSameEndPoint(test1.MappedEndPoint, test1.LocalEndPoint))
        {
            var test2Open = await QueryStunWithChangeRequestAsync(serverIp1, 19302, boundPort, changeIP: true, changePort: true);
            return test2Open.Success ? NatType.Open : NatType.Symmetric; // Symmetric UDP Firewall
        }

        var test2 = await QueryStunWithChangeRequestAsync(serverIp1, 19302, boundPort, changeIP: true, changePort: true);
        if (test2.Success) return NatType.FullCone;

        var test1Alt = await QueryStunAsync(serverIp2, 19302, boundPort);
        if (test1Alt.Success && !IsSameEndPoint(test1.MappedEndPoint, test1Alt.MappedEndPoint))
        {
            return NatType.Symmetric;
        }

        var test3 = await QueryStunWithChangeRequestAsync(serverIp1, 19302, boundPort, changeIP: false, changePort: true);
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
            var timeoutTask = Task.Delay(1000);
            
            if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
            {
                var response = await receiveTask;
                return ParseStunResponse(response.Buffer, result);
            }
        }
        catch { /* Ignore errors and return success=false */ }
        
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
            var timeoutTask = Task.Delay(1000); // 1s timeout
            
            if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
            {
                var response = await receiveTask;
                return ParseStunResponse(response.Buffer, result);
            }
        }
        catch { /* Ignore */ }
        
        result.Success = false;
        return result;
    }

    private static byte[] CreateStunBindingRequest()
    {
        byte[] packet = new byte[20];

        packet[0] = 0x00; packet[1] = 0x01;

        packet[2] = 0x00; packet[3] = 0x00;
        

        var rand = new Random();
        rand.NextBytes(new Span<byte>(packet, 4, 16));
        
        return packet;
    }

    private static byte[] CreateStunChangeRequest(bool changeIP, bool changePort)
    {
        byte[] packet = new byte[28];

        packet[0] = 0x00; packet[1] = 0x01;

        packet[2] = 0x00; packet[3] = 0x08;
        

        var rand = new Random();
        rand.NextBytes(new Span<byte>(packet, 4, 16));
        

        packet[20] = 0x00; packet[21] = 0x03;
        packet[22] = 0x00; packet[23] = 0x04;
        

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


        int type = (response[0] << 8) | response[1];
        if (type != 0x0101 && type != 0x0111) // standard response or XOR response
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

            if (attrType == 0x0001 || attrType == 0x0020) // MAPPED-ADDRESS or XOR-MAPPED-ADDRESS
            {
                result.MappedEndPoint = ParseAddress(response, offset, attrLength, attrType == 0x0020);
            }
            else if (attrType == 0x0005) // CHANGED-ADDRESS
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
        
        byte family = response[offset + 1];
        if (family != 0x01) return null; // IPv4 only for now

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
