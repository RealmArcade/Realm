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

	/// <summary>
	///     Measures the endpoint this machine's router maps a local port to. Nothing is confirmed by it: a router reports the
	///     mapping it would give the port whether or not anything on its outside face forwards traffic, so the address is
	///     remembered as a measurement until a registry node reaches the port through it or the router accepts a mapping.
	/// </summary>
	/// <param name="localPort">The local port the lobby listens on.</param>
	private async Task ResolvePublicEndpointAsync(int localPort)
	{
		// The external port a router hands out belongs to one local port. Measuring again for a different port must not
		// leave the last answer behind: a failed measurement would otherwise advertise the previous port for a lobby that
		// is listening somewhere else.
		if (_hostPublicPortMeasuredForLocalPort != localPort) _hostPublicPort = 0;

		_routerReachability = RouterReachability.NoRouterForward;
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

			if ((result == null || !result.Success || result.MappedEndPoint == null) && string.IsNullOrEmpty(_hostPublicIp))
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

			if (result != null && result.Success && result.MappedEndPoint is IPEndPoint mappedEndPoint)
			{
				bool mappedFromBoundPort = result.LocalEndPoint is IPEndPoint localEndPoint && localEndPoint.Port == localPort;
				_hostPublicIp = mappedEndPoint.Address.ToString();
				if (mappedFromBoundPort)
				{
					_hostPublicPort = mappedEndPoint.Port;
					_hostPublicPortMeasuredForLocalPort = localPort;
				}
				else if (_hostPublicPort <= 0)
				{
					_hostPublicPort = localPort;
					_hostPublicPortMeasuredForLocalPort = localPort;
				}
				GameLog.LogToConsole($"[STUN] Local {localPort} -> Public {_hostPublicIp}:{_hostPublicPort}");
			}
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
	/// <param name="registryUrl">The registry node about to publish the lobby.</param>
	/// <param name="lobbyPort">The game port the lobby listens on.</param>
	/// <returns>What the lobby is verified at, or what stopped it being published.</returns>
	private async Task<LobbyReachabilitySet> RehearsePublishedEndpointsAsync(string registryUrl, int lobbyPort)
	{
		RegistryCapabilities capabilities = await ReadRegistryCapabilitiesAsync(registryUrl, CancellationToken.None);
		LobbyReachabilitySet set = await VerifyPublishedEndpointsAsync(registryUrl, lobbyPort, capabilities, CancellationToken.None);

		if (set.Verified.Count == 0)
		{
			HostReachabilityResult refused = DescribeReachabilityRefusal(registryUrl, set);
			GameLog.LogError($"[Reachability] {refused.Failure}: {refused.Detail}");
			return set;
		}

		ApplyPublishedEndpoints(set.Verified);

		// Stated only for a lobby offered over more than one family: a machine with no IPv6 addresses nobody, and adding a
		// line to its log for a path it does not have is a change to a working machine for no reason.
		if (set.Verified.Count > 1 || set.Verified[0].FamilyValue != LobbyAddressFamily.IPv4)
		{
			GameLog.LogToConsole($"[Reachability] This lobby is listed over {DescribeFamilyList(OfferedFamilies(set))} at {DescribeVerifiedEndpoints(set)}.");
		}

		return set;
	}

	/// <summary>
	///     States why a lobby that offered endpoints verified none of them. A family this node cannot check at all is named
	///     as the node's own limit rather than as this machine's network, because the two send the host to different places.
	/// </summary>
	/// <param name="registryUrl">The registry node that was asked.</param>
	/// <param name="set">What the rehearsal produced.</param>
	/// <param name="decision">The refusal the lobby's joinability came to, which says which half of the rehearsal it is about.</param>
	/// <returns>The refusal to show the host.</returns>
	private static HostReachabilityResult DescribeReachabilityRefusal(string registryUrl, LobbyReachabilitySet set, HostReachabilityFailure decision = HostReachabilityFailure.ProbeUnanswered)
	{
		if (decision == HostReachabilityFailure.ProbeFromOwnNetwork)
		{
			return HostReachabilityResult.Refused(decision, $"the registry node '{registryUrl}' measured this lobby at {DescribeVerifiedEndpoints(set)}, which is not an address a player elsewhere can be sent to");
		}

		if (set.UncheckedFamilies.Count == 0) return set.FirstFailure ?? HostReachabilityResult.Refused(decision, "no endpoint of this lobby was measured");

		string families = string.Join(" + ", set.UncheckedFamilies);
		return HostReachabilityResult.Refused(
			HostReachabilityFailure.ProbeFamilyUnavailable,
			$"the lobby server '{registryUrl}' has no socket to check a {families} endpoint from, so this lobby could not be verified there");
	}

	/// <summary>The families this lobby is published over, in the order they were measured.</summary>
	/// <param name="set">What the rehearsal produced.</param>
	/// <returns>The distinct families that answered.</returns>
	private static List<LobbyAddressFamily> OfferedFamilies(LobbyReachabilitySet set)
	{
		var families = new List<LobbyAddressFamily>(set.Verified.Count);
		for (int index = 0; index < set.Verified.Count; index++)
		{
			LobbyAddressFamily family = set.Verified[index].FamilyValue;
			if (!families.Contains(family)) families.Add(family);
		}

		return families;
	}

	/// <summary>The measured endpoints, written the way a player reads an address.</summary>
	/// <param name="set">What the rehearsal produced.</param>
	/// <returns>One address per verified endpoint.</returns>
	private static string DescribeVerifiedEndpoints(LobbyReachabilitySet set)
	{
		var described = new List<string>(set.Verified.Count);
		for (int index = 0; index < set.Verified.Count; index++) described.Add(set.Verified[index].Describe());
		return string.Join(" and ", described);
	}

	/// <summary>
	///     Asks the authority to send a datagram from the port the lobby listens on to the endpoint the registry is probing
	///     from, which is what opens a router that drops unsolicited traffic for the probes that follow it. The datagram
	///     names the check it is for, so a registry node checking two hosts behind one router can tell their punches apart.
	/// </summary>
	private bool PunchProbeEndpointAsync(string address, int port, string probeId)
	{
		ClientServerSession? session = _lobbySession;
		if (session == null) return false;

		Callable.From(() =>
		{
			if (!ReferenceEquals(session, _lobbySession)) return;

			session.SendLobbyCommand(new LobbyCommand
			{
				Kind = LobbyCommandKind.PunchAddress,
				Address = address,
				Port = port,
				Text = probeId
			});
		}).CallDeferred();

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
