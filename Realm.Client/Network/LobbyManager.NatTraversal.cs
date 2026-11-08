using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Network;
public partial class LobbyManager
{
	private void LoadServersConfig()
	{
		var config = ServersConfigHelper.Load();
		OfficialServers = config.Servers;
		_currentServerIndex = 0;
		GameLog.LogToConsole($"[LobbyManager] Loaded servers: {string.Join(", ", RegistryServers)}");
	}

	public async Task RunNatTypeTestAsync()
	{
		GameLog.LogToConsole("[LobbyManager] Starting STUN NAT Type Test...");
		LocalNatType = await NatTypeTester.DetermineNatTypeAsync(0);
		GameLog.LogToConsole($"[LobbyManager] NAT Type Classified: {LocalNatType}");

		await ResolvePublicEndpointAsync(ENetPort);
		CallDeferred(nameof(EmitNatTestCompleted));
	}

	private async Task ResolvePublicEndpointAsync(int localPort)
	{
		_hostPublicIp = null;
		_hostPublicPort = 0;

		try
		{
			var dnsAddresses = await Dns.GetHostAddressesAsync("stun.l.google.com");
			if (dnsAddresses.Length == 0) return;

			StunResult? result = null;
			try
			{
				result = await NatTypeTester.QueryStunOnPortAsync(dnsAddresses[0], StunPort, localPort);
			}
			catch (Exception ex)
			{
				GameLog.LogToConsole($"[LobbyManager] Direct STUN on port {localPort} failed: {ex.Message}");
			}

			if (result == null || !result.Success || result.MappedEndPoint == null)
			{
				using var udp = new UdpClient();
				udp.ExclusiveAddressUse = false;
				udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

				var serverEp = new IPEndPoint(dnsAddresses[0], StunPort);
				byte[] req = new byte[20];
				req[0] = 0x00;
				req[1] = 0x01;
				new Random().NextBytes(new Span<byte>(req, 4, 16));

				await udp.SendAsync(req, req.Length, serverEp);
				var receiveTask = udp.ReceiveAsync();
				var timeoutTask = Task.Delay(1000);
				if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
				{
					var response = await receiveTask;
					result = new StunResult();
					NatTypeTester.ParseStunResponse(response.Buffer, result);
				}
			}

			if (result == null || !result.Success || result.MappedEndPoint is not IPEndPoint mappedEndPoint) return;

			bool mappedFromBoundPort = result.LocalEndPoint is IPEndPoint localEndPoint && localEndPoint.Port == localPort;
			_hostPublicIp = mappedEndPoint.Address.ToString();
			_hostPublicPort = mappedFromBoundPort ? mappedEndPoint.Port : localPort;
			GameLog.LogToConsole($"[STUN] Local {localPort} -> Public {_hostPublicIp}:{_hostPublicPort}");
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Failed to get public IP: {ex.Message}");
		}
	}

	public void RunNatTypeTest()
	{
		_natTypeTest = Task.Run(RunNatTypeTestAsync);
	}

	private Task? _natTypeTest;

	/// <summary>
	///     The public endpoint this screen advertises is measured once when it starts and reused until it is missing, so
	///     joining a lobby does not first wait out a NAT test: the join is the screen's own business, and the test that
	///     was already running is joined rather than repeated.
	/// </summary>
	private async Task EnsureNatTypeMeasuredAsync()
	{
		if (!string.IsNullOrEmpty(_hostPublicIp)) return;

		Task? running = _natTypeTest;
		if (running is { IsCompleted: false })
		{
			await running;
			if (!string.IsNullOrEmpty(_hostPublicIp)) return;
		}

		await RunNatTypeTestAsync();
	}

	private void EmitNatTestCompleted()
	{
		NatTestCompleted?.Invoke();
	}

	private void ReportHostLobbyStage(HostLobbyStage stage)
	{
		Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion())
			{
				HostLobbyStageChanged?.Invoke(stage);
			}
		}).CallDeferred();
	}

}
