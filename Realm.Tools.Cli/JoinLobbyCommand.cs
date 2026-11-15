using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Realm.Shared.Reachability;

namespace Realm.Tools.Cli;

/// <summary>
///     Rehearses a lobby join from this machine and reports each phase separately, which is the only way a join failure can
///     be attributed to a step instead of to a timeout.
/// </summary>
/// <remarks>
///     This proves the phases that can be proved without the game: where the lobby is, whether this machine's router
///     exposed an endpoint, whether a punch left, and whether the authority answered a connection attempt. It does not take
///     a seat, and it never reports that it joined: the connection attempt is abandoned once the answer arrives, so the
///     deepest thing it can state is that the authority is reachable from here.
/// </remarks>
public static class JoinLobbyCommand
{
	private const int RegistryPhaseExitCode = 10;
	private const int ResolvePhaseExitCode = 11;
	private const int ReportPhaseExitCode = 12;
	private const int PunchPhaseExitCode = 13;
	private const int AnswerPhaseExitCode = 14;
	private const int ArgumentExitCode = 2;

	private const int AttemptIntervalMilliseconds = 250;
	private const int ReportSettleMilliseconds = 300;
	private const uint ConnectTicket = 0x5245414C;

	public static int Execute(JoinLobbyOptions options)
	{
		if (string.IsNullOrWhiteSpace(options.LobbyId) == string.IsNullOrWhiteSpace(options.Address))
		{
			Console.Error.WriteLine("State exactly one of --lobby or --address.");
			return ArgumentExitCode;
		}

		if (!string.IsNullOrWhiteSpace(options.LobbyId) && string.IsNullOrWhiteSpace(options.Registry))
		{
			Console.Error.WriteLine("--lobby needs --registry, which says which lobby server to ask.");
			return ArgumentExitCode;
		}

		var report = new JoinReport();
		Stopwatch clock = Stopwatch.StartNew();

		(string host, int port, int punchPort, string punchToken) target = default;

		if (!string.IsNullOrWhiteSpace(options.LobbyId))
		{
			long started = clock.ElapsedMilliseconds;
			string? failure = TryResolveThroughRegistry(options, out target, out string registryDetail);
			if (failure != null)
			{
				report.Add("registry", "refused", clock.ElapsedMilliseconds - started, options.Registry, registryDetail, failure);
				return Finish(report, options);
			}

			report.Add("registry", "answered", clock.ElapsedMilliseconds - started, options.Registry, registryDetail, string.Empty);
		}
		else
		{
			long started = clock.ElapsedMilliseconds;
			if (!TryParseAddress(options.Address, out string address, out int port, out string parseFailure))
			{
				report.Add("resolve", "invalid", clock.ElapsedMilliseconds - started, options.Address, string.Empty, parseFailure);
				return Finish(report, options);
			}

			target = (address, port, 0, string.Empty);
			report.Add("registry", "skipped", clock.ElapsedMilliseconds - started, "no registry was asked", "the address was stated on the command line", string.Empty);
		}

		long resolveStarted = clock.ElapsedMilliseconds;
		if (!IPAddress.TryParse(target.host, out IPAddress? hostAddress) || hostAddress == null)
		{
			try
			{
				IPAddress[] addresses = Dns.GetHostAddresses(target.host);
				hostAddress = Array.Find(addresses, candidate => candidate.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
			}
			catch (SocketException exception)
			{
				report.Add("resolve", "unresolved", clock.ElapsedMilliseconds - resolveStarted, $"{target.host}:{target.port}", string.Empty, exception.Message);
				return Finish(report, options);
			}
		}

		if (hostAddress == null)
		{
			report.Add("resolve", "unresolved", clock.ElapsedMilliseconds - resolveStarted, $"{target.host}:{target.port}", string.Empty, "the lobby stated an address this machine could not resolve");
			return Finish(report, options);
		}

		var endpoint = new IPEndPoint(hostAddress, target.port);
		report.Add("resolve", "resolved", clock.ElapsedMilliseconds - resolveStarted, endpoint.ToString(), string.Empty, string.Empty);

		using var socket = new UdpClient(AddressFamily.InterNetwork);
		socket.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
		int localPort = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;

		long reportStarted = clock.ElapsedMilliseconds;
		bool reported = false;
		string reportDetail = "the registry node stated no reporting endpoint, so this machine was not introduced to the host";
		bool hasReportTicket = target.punchPort > 0 && !string.IsNullOrWhiteSpace(target.punchToken);
		if (hasReportTicket)
		{
			reportDetail = TryReportEndpoint(socket, options.Registry, target.punchPort, target.punchToken);
			reported = reportDetail.Length == 0;
		}

		report.Add("report", hasReportTicket ? reported ? "sent" : "refused" : "skipped", clock.ElapsedMilliseconds - reportStarted, hasReportTicket ? $"{options.Registry}:{target.punchPort}" : "no registry stated a reporting endpoint", reportDetail, hasReportTicket && !reported ? reportDetail : string.Empty);

		long punchStarted = clock.ElapsedMilliseconds;
		bool punched = TrySend(socket, endpoint, ReachabilityPunch.Datagram);
		report.Add("punch", punched ? "sent" : "refused", clock.ElapsedMilliseconds - punchStarted, endpoint.ToString(), $"from local port {localPort}", punched ? string.Empty : "the socket could not send to the lobby");

		long answerStarted = clock.ElapsedMilliseconds;
		(bool answered, bool refused, string answerDetail) = WaitForAnswer(socket, endpoint, TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));
		string answerOutcome = answered ? "answered" : refused ? "refused" : "silent";
		report.Add("answer", answerOutcome, clock.ElapsedMilliseconds - answerStarted, endpoint.ToString(), answerDetail, answered ? string.Empty : "the authority did not answer a connection attempt from this machine");

		return Finish(report, options);
	}

	private static string? TryResolveThroughRegistry(JoinLobbyOptions options, out (string Host, int Port, int PunchPort, string PunchToken) target, out string detail)
	{
		target = default;
		detail = string.Empty;

		try
		{
			using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(3, options.TimeoutSeconds)) };
			string registry = options.Registry.TrimEnd('/');
			var payload = new StringContent(JsonSerializer.Serialize(new { LobbyId = options.LobbyId, ClientPublicIP = string.Empty, ClientPublicPort = 0 }), Encoding.UTF8, "application/json");
			using HttpResponseMessage response = client.PostAsync($"{registry}/lobbies/join", payload).GetAwaiter().GetResult();
			string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (!response.IsSuccessStatusCode)
			{
				detail = $"{response.StatusCode}: {body}";
				return "the lobby server refused to say where this lobby is";
			}

			using JsonDocument document = JsonDocument.Parse(body);
			JsonElement root = document.RootElement;
			string host = root.TryGetProperty("hostIP", out JsonElement hostElement) ? hostElement.GetString() ?? string.Empty : string.Empty;
			int port = root.TryGetProperty("hostPort", out JsonElement portElement) ? portElement.GetInt32() : 0;
			int punchPort = root.TryGetProperty("punchPort", out JsonElement punchPortElement) && punchPortElement.ValueKind == JsonValueKind.Number ? punchPortElement.GetInt32() : 0;
			string punchToken = root.TryGetProperty("punchToken", out JsonElement tokenElement) ? tokenElement.GetString() ?? string.Empty : string.Empty;

			if (string.IsNullOrWhiteSpace(host) || port <= 0)
			{
				detail = body;
				return "the lobby server answered without stating where the lobby is";
			}

			target = (host, port, punchPort, punchToken);
			detail = punchPort > 0 ? $"reported on UDP port {punchPort}" : "no reporting endpoint stated by this node";
			return null;
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
		{
			detail = exception.Message;
			return "the lobby server could not be reached";
		}
	}

	private static string TryReportEndpoint(UdpClient socket, string registry, int punchPort, string token)
	{
		try
		{
			string registryHost = new Uri(registry).Host;
			IPAddress[] addresses = Dns.GetHostAddresses(registryHost);
			IPAddress? address = Array.Find(addresses, candidate => candidate.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
			if (address == null) return "the lobby server's address could not be resolved";

			var datagram = new byte[JoinerPunch.MaximumDatagramBytes];
			int written = JoinerPunch.TryWrite(datagram, token);
			if (written == 0) return "the report could not be written";

			if (!TrySend(socket, new IPEndPoint(address, punchPort), datagram.AsSpan(0, written))) return "the socket could not send the report";

			Thread.Sleep(ReportSettleMilliseconds);
			return string.Empty;
		}
		catch (SocketException exception)
		{
			return exception.Message;
		}
	}

	private static (bool Answered, bool Refused, string Detail) WaitForAnswer(UdpClient socket, IPEndPoint endpoint, TimeSpan budget)
	{
		var connect = new byte[EnetConnectProbe.ConnectDatagramBytes];
		int written = EnetConnectProbe.WriteConnect(connect, ConnectTicket, 0, 0);
		if (written == 0) return (false, false, "the connection attempt could not be written");

		Stopwatch clock = Stopwatch.StartNew();
		int attempts = 0;
		while (clock.Elapsed < budget)
		{
			TrySend(socket, endpoint, connect.AsSpan(0, written));
			attempts++;

			long deadline = clock.ElapsedMilliseconds + AttemptIntervalMilliseconds;
			while (clock.ElapsedMilliseconds < deadline)
			{
				if (!socket.Client.Poll(AttemptIntervalMilliseconds * 1000, SelectMode.SelectRead)) continue;

				var received = new IPEndPoint(IPAddress.Any, 0);
				byte[] datagram;
				try
				{
					datagram = socket.Receive(ref received);
				}
				catch (SocketException receiveFailure)
				{
					// Windows reports an Internet Control Message Protocol port-unreachable as a reset on the socket that sent
					// to it. That is a diagnosis worth keeping: the connection attempt arrived and the port rejected it, which
					// is a different fault from a network that swallowed the packet.
					if (receiveFailure.SocketErrorCode == SocketError.ConnectionReset)
					{
						return (false, true, $"the port answered with an unreachable message after {attempts} tries, so nothing is listening on it");
					}

					continue;
				}

				if (!EnetConnectProbe.TryReadVerifyConnect(datagram, out EnetConnectReply reply)) continue;
				if (reply.ConnectId != ConnectTicket) continue;

				return (true, false, $"peer {reply.PeerId} accepted the attempt after {attempts} tries");
			}
		}

		return (false, false, $"no answer after {attempts} attempts");
	}

	private static bool TrySend(UdpClient socket, IPEndPoint destination, ReadOnlySpan<byte> payload)
	{
		try
		{
			socket.Send(payload.ToArray(), payload.Length, destination);
			return true;
		}
		catch (SocketException)
		{
			return false;
		}
	}

	private static bool TryParseAddress(string text, out string host, out int port, out string failure)
	{
		host = string.Empty;
		port = 0;
		failure = string.Empty;

		string trimmed = (text ?? string.Empty).Trim();
		if (trimmed.Length == 0)
		{
			failure = "no address was stated";
			return false;
		}

		string portText = string.Empty;
		if (trimmed[0] == '[')
		{
			int closingBracket = trimmed.IndexOf(']');
			if (closingBracket < 0)
			{
				failure = "the address opens a bracket it never closes";
				return false;
			}

			host = trimmed[1..closingBracket];
			if (closingBracket + 1 < trimmed.Length && trimmed[closingBracket + 1] == ':') portText = trimmed[(closingBracket + 2)..];
		}
		else
		{
			int colon = trimmed.LastIndexOf(':');
			bool hasOneColon = colon == trimmed.IndexOf(':');
			if (hasOneColon && colon > 0)
			{
				host = trimmed[..colon];
				portText = trimmed[(colon + 1)..];
			}
			else
			{
				host = trimmed;
			}
		}

		host = host.Trim();
		if (host.Length == 0)
		{
			failure = "the address states no host";
			return false;
		}

		if (portText.Length > 0 && (!int.TryParse(portText, out port) || port < 1 || port > 65535))
		{
			failure = "the address states a port that is not a port";
			return false;
		}

		if (port == 0) port = 8999;
		return true;
	}

	private static int Finish(JoinReport report, JoinLobbyOptions options)
	{
		if (options.Json)
		{
			Console.WriteLine(report.ToJson(options));
		}
		else
		{
			report.WriteHuman(options);
		}

		if (!string.IsNullOrWhiteSpace(options.Expect)) return report.MatchesExpectation(options.Expect) ? 0 : 1;
		return report.ExitCode;
	}

	private sealed class JoinReport
	{
		private readonly List<JoinPhaseResult> _phases = new();

		public int ExitCode { get; private set; }

		public void Add(string phase, string outcome, long elapsedMilliseconds, string endpoint, string detail, string failure)
		{
			_phases.Add(new JoinPhaseResult(phase, outcome, elapsedMilliseconds, endpoint, detail, failure));

			if (failure.Length > 0 && ExitCode == 0)
			{
				ExitCode = phase switch
				{
					"registry" => RegistryPhaseExitCode,
					"resolve" => ResolvePhaseExitCode,
					"report" => ReportPhaseExitCode,
					"punch" => PunchPhaseExitCode,
					"answer" => AnswerPhaseExitCode,
					_ => 1
				};
			}
		}

		public bool MatchesExpectation(string expectation)
		{
			int separator = expectation.IndexOf(':');
			if (separator <= 0) return false;

			string phase = expectation[..separator];
			string outcome = expectation[(separator + 1)..];
			return _phases.Exists(result => string.Equals(result.Phase, phase, StringComparison.OrdinalIgnoreCase) && string.Equals(result.Outcome, outcome, StringComparison.OrdinalIgnoreCase));
		}

		public void WriteHuman(JoinLobbyOptions options)
		{
			Console.WriteLine($"join {(string.IsNullOrWhiteSpace(options.LobbyId) ? options.Address : options.LobbyId)}");
			foreach (JoinPhaseResult phase in _phases)
			{
				Console.WriteLine($"  {phase.Phase,-9} {phase.Outcome,-9} {phase.ElapsedMilliseconds,6} ms  {phase.Endpoint}{(phase.Detail.Length > 0 ? $"  ({phase.Detail})" : string.Empty)}");
				if (phase.Failure.Length > 0) Console.WriteLine($"            {phase.Failure}");
			}

			JoinPhaseResult answer = _phases.Find(result => result.Phase == "answer");
			Console.WriteLine(answer.Phase == "answer" && answer.Outcome == "answered"
				? "If every phase passed, this machine can reach the lobby's authority. No seat was taken, so this is not a completed join."
				: "This machine could not complete the phases above, and the first one that failed names where the join stopped.");
		}

		public string ToJson(JoinLobbyOptions options) => JsonSerializer.Serialize(new
		{
			Target = string.IsNullOrWhiteSpace(options.LobbyId) ? options.Address : options.LobbyId,
			Tier = "transport-answer",
			SeatTaken = false,
			ExitCode,
			Phases = _phases.ConvertAll(phase => new
			{
				phase = phase.Phase,
				outcome = phase.Outcome,
				elapsedMilliseconds = phase.ElapsedMilliseconds,
				endpoint = phase.Endpoint,
				detail = phase.Detail,
				failure = phase.Failure
			})
		});
	}

	private readonly record struct JoinPhaseResult(string Phase, string Outcome, long ElapsedMilliseconds, string Endpoint, string Detail, string Failure);
}
