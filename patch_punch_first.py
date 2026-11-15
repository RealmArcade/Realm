path = 'Realm.Client/Network/LobbyManager.Joining.cs'
s = open(path, newline='', encoding='utf-8').read()

old = '''				if (!nearRoute && target.Punch.IsUsable)
				{
					long reportStarted = Environment.TickCount64;
					_joinPhase = JoinPhases.Report;
					joinLocalPort = await ReportJoinerEndpointFromJoinPortAsync(target.Punch);'''
new = '''				if (!nearRoute && target.Punch.IsUsable)
				{
					long reportStarted = Environment.TickCount64;
					_joinPhase = JoinPhases.Report;
					joinLocalPort = await ReportJoinerEndpointFromJoinPortAsync(target.Punch, connectIp, connectPort);'''
assert old in s
s = s.replace(old, new, 1)

old = '''	/// <summary>
	///     Reports this screen's endpoint to the registry node from the port the join will connect from, so that the endpoint
	///     the host is told about and the endpoint that connects are the same one, and returns that port.
	/// </summary>
	/// <param name="ticket">The ticket the join answer stated for this join.</param>
	/// <returns>The local port the report left from, which the join then binds, or zero when nothing was reported.</returns>
	private async Task<int> ReportJoinerEndpointFromJoinPortAsync(JoinerPunchTicket ticket)'''
new = '''	/// <summary>
	///     Reports this screen's endpoint to the registry node from the port the join will connect from, so that the endpoint
	///     the host is told about and the endpoint that connects are the same one, and returns that port.
	/// </summary>
	/// <remarks>
	///     A punch is sent toward the lobby from that same port before the report leaves. A router that refuses unsolicited
	///     traffic admits the host's answer only from an endpoint this side has already sent to, and the host's punch is sent
	///     the moment the registry node reads the report: without this the answer can arrive at a router that is still waiting
	///     for this side to speak first, and it is dropped. Both packets leave one socket in one call each, so the ordering
	///     costs the join nothing.
	/// </remarks>
	/// <param name="ticket">The ticket the join answer stated for this join.</param>
	/// <param name="lobbyIp">The lobby's address, which the punch is aimed at.</param>
	/// <param name="lobbyPort">The lobby's port, which the punch is aimed at.</param>
	/// <returns>The local port the report left from, which the join then binds, or zero when nothing was reported.</returns>
	private async Task<int> ReportJoinerEndpointFromJoinPortAsync(JoinerPunchTicket ticket, string lobbyIp, int lobbyPort)'''
assert old in s
s = s.replace(old, new, 1)

old = '''			using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(new IPEndPoint(IPAddress.Any, 0));
			socket.SendTo(datagram.AsSpan(0, written), new IPEndPoint(registryAddress, ticket.Port));

			int localPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
			GameLog.LogToConsole($"[LobbyManager] Reported this screen's endpoint to {ticket.Address}:{ticket.Port} from local port {localPort}, which this join then connects from.");
			return localPort;'''
new = '''			using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(new IPEndPoint(IPAddress.Any, 0));

			int localPort = ((IPEndPoint)socket.LocalEndPoint!).Port;

			if (IPAddress.TryParse(lobbyIp, out IPAddress? lobbyAddress))
			{
				socket.SendTo(ReachabilityPunch.Datagram, new IPEndPoint(lobbyAddress, lobbyPort));
			}

			socket.SendTo(datagram.AsSpan(0, written), new IPEndPoint(registryAddress, ticket.Port));

			GameLog.LogToConsole($"[LobbyManager] Punched the lobby at {lobbyIp}:{lobbyPort} and reported this screen's endpoint to {ticket.Address}:{ticket.Port}, both from local port {localPort}, which this join then connects from.");
			return localPort;'''
assert old in s
s = s.replace(old, new, 1)

open(path, 'w', newline='', encoding='utf-8').write(s)
print('joiner punches before reporting')
