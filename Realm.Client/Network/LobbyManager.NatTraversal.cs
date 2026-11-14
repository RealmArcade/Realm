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
using Realm.Shared.Reachability;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Network;
public partial class LobbyManager
{
	private const string LoopbackAddress = "127.0.0.1";

	private const int LoopbackAnswerWaitMilliseconds = 3000;

	private const int LoopbackPollMilliseconds = 25;

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

	/// <summary>
	///     Proves this screen can play the seat it is holding: its own client must be seated over the loopback address, and the
	///     authority must have described the lobby back to it. A host that cannot reach its own lobby cannot control its own
	///     player, so the lobby is not worth publishing for anybody, and this is checked before anything is opened or
	///     registered.
	/// </summary>
	private async Task<HostReachabilityResult> VerifyLobbyLoopbackAsync(ClientServerSession session)
	{
		bool connectedOverLoopback = session.ConnectedOverLoopback;

		if (!connectedOverLoopback)
		{
			GameLog.LogError("[Reachability] this screen's own seat is not held over loopback, so the host could not control its own player.");
			return HostReachabilityResult.Refused(HostReachabilityFailure.LoopbackNotOnLocalAddress, "this screen's own seat is not held over the loopback address");
		}

		long deadlineMilliseconds = Environment.TickCount64 + LoopbackAnswerWaitMilliseconds;
		while (!HasLobbyState && Environment.TickCount64 < deadlineMilliseconds) await Task.Delay(LoopbackPollMilliseconds);

		LoopbackJoinOutcome loopback = LobbyJoinability.DescribeLoopback(connectedOverLoopback, HasLobbyState);
		HostReachabilityFailure decision = LobbyJoinability.Decide(publicLobby: false, loopback, HostReachabilityFailure.None);
		if (decision != HostReachabilityFailure.None)
		{
			GameLog.LogError($"[Reachability] the authority did not describe the lobby back to this screen over loopback ({loopback}).");
			return HostReachabilityResult.Refused(decision, $"the authority did not describe the lobby back over loopback ({loopback})");
		}

		GameLog.LogToConsole($"[Reachability] this screen's own seat is held over loopback on port {session.Port} and the lobby answered.");
		return new HostReachabilityResult(HostReachabilityFailure.None, string.Empty, LoopbackAddress, session.Port, $"the lobby answered this screen's own client over loopback on port {session.Port}");
	}

	/// <summary>
	///     Rehearses the join the lobby is about to offer, with the registry node that is about to publish it playing the part
	///     of the joining player. Nothing is registered until this answers, because a lobby other players can see and cannot
	///     reach is worse than a lobby that states what is wrong on the machine that tried to host it.
	/// </summary>
	private async Task<HostReachabilityResult> VerifyLobbyReachabilityAsync(string registryUrl, int lobbyPort)
	{
		var verifier = new HostReachabilityVerifier(_httpClient, message => GameLog.LogToConsole($"[Reachability] {message}"));
		HostReachabilityResult result = await verifier.VerifyAsync(registryUrl, _hostPublicIp ?? string.Empty, lobbyPort, PunchProbeEndpointAsync);
		if (result.Verified)
		{
			GameLog.LogToConsole($"[Reachability] The registry reached this lobby's game port and measured {result.PublicIp}:{result.PublicPort}.");
		}
		else
		{
			GameLog.LogError($"[Reachability] {result.Failure}: {result.Detail}");
		}

		return result;
	}

	/// <summary>
	///     Asks the authority to send a datagram from the port the lobby listens on to the endpoint the registry is probing
	///     from, which is what opens a router that drops unsolicited traffic for the probes that follow it.
	/// </summary>
	private bool PunchProbeEndpointAsync(string address, int port)
	{
		ClientServerSession? session = _lobbySession;
		if (session == null) return false;

		Callable.From(() => session.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.PunchAddress,
			Address = address,
			Port = port
		})).CallDeferred();

		return true;
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
