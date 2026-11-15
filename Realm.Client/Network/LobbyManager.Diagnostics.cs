using System.Text;
using Realm.Client.UI;
using Realm.Shared;
using Realm.Shared.Network;
using Realm.Shared.Reachability;

namespace Realm.Client.Network;

/// <summary>
///     The page a host copies when a lobby has to be explained to somebody who cannot see the machine it runs on. Every
///     number a later reader needs to work out why a player could not get in is on it, and every number on it is one this
///     screen actually measured, or the statement that nothing measured it.
/// </summary>
public partial class LobbyManager
{
	/// <summary>
	///     The address a STUN query saw this machine's router map its game port to, or an empty string when nothing was
	///     measured. It is what the connectivity help reads to say whether a second router or the provider's own translation
	///     stands in front of this machine, so it is the measurement itself rather than the address the game offers.
	/// </summary>
	public string MeasuredOutsideAddress => _hostPublicIp ?? string.Empty;

	/// <summary>
	///     The step the last join reached, named from <see cref="JoinPhases" />. A failure names the step that broke, which is
	///     what tells a joining screen whether the thing to change is on this machine or on the host's.
	/// </summary>
	public string LastJoinPhase => _joinPhase;

	/// <summary>Whether this screen's own NAT was classified, which is what makes a symmetric classification worth reading.</summary>
	public bool HasLocalNatType { get; private set; }

	/// <summary>
	///     Writes the diagnostics block, in the same shape every time so that two hosts' pages can be compared line by line.
	///     The game port is stated twice on purpose: the port a lobby is listed at and the port its transport holds being
	///     different is the failure this page exists to make visible, and a page that showed one of them would hide it.
	/// </summary>
	/// <returns>The block, one statement per line.</returns>
	public string DescribeNetworkDiagnostics()
	{
		string listedPort = LobbyPort > 0 ? LobbyPort.ToString() : "none";
		string firewall = LastFirewallGrant.Detail;
		if (string.IsNullOrEmpty(firewall)) firewall = "not asked for";

		var block = new StringBuilder();
		block.AppendLine("Realm lobby network diagnostics");
		block.AppendLine($"game version: {RealmVersion.GameBinaryVersion}");
		block.AppendLine($"lobby id: {ActiveLobbyId ?? "none"}");
		block.AppendLine($"lobby server: {RegistryServerUrl}");
		block.AppendLine($"game port (listed): {listedPort}");
		block.AppendLine($"game port (transport holds): {BoundPort}");
		block.AppendLine($"address on this network: {DirectConnectAddress}");
		block.AppendLine($"address measured for this machine: {MeasuredPublicConnectAddress ?? "not measured"}");
		block.AppendLine($"address confirmed from outside: {PublicConnectAddress ?? "not confirmed by anything outside this network"}");
		block.AppendLine($"reachability class: {ReachabilityClass}");
		block.AppendLine($"reachability re-checks: {_reachabilityReverifyCount} (last: {_lastReverifyOutcome})");
		block.AppendLine($"router mapping: {MappingBehaviour}");
		block.AppendLine($"local NAT: {LocalNatType}");
		block.AppendLine($"router: {RouterReachability}");
		block.AppendLine($"firewall: {firewall}");
		block.AppendLine($"last hosting failure: {LastHostError ?? "none"}");
		block.AppendLine($"last player join failure: {LastPlayerJoinFailure ?? "none"}");
		block.AppendLine($"last warning shown: {LastLobbyWarning ?? "none"}");
		AppendHandoverDiagnostics(block);
		return block.ToString().TrimEnd();
	}

	/// <summary>
	///     States the lobby's host handover in the same shape on every screen that can report it, so the page a screen that
	///     offered a handover copies and the page the screen it was offered to copies describe one transfer rather than two
	///     accounts of it. A lobby nobody offered a handover in states the stage it is at and no numbers.
	/// </summary>
	/// <param name="block">The page being written.</param>
	private void AppendHandoverDiagnostics(StringBuilder block)
	{
		block.AppendLine($"handover stage: {HandoverStage}");
		block.AppendLine($"handover candidate: {DescribeSeatByPeerId(HandoverCandidatePeerId)}");
		block.AppendLine($"handover seconds remaining: {HandoverSecondsRemaining}");
		block.AppendLine($"handover suggested candidate: {DescribeSeatByPeerId(SuggestedCandidatePeerId)}");
		block.AppendLine($"handover notice: {(HandoverNoticeKey.Length == 0 ? "none" : HandoverNoticeKey)}");

		if (!HasHandoverReport)
		{
			block.AppendLine("handover measurements: none");
			return;
		}

		LobbyHandoverReport report = HandoverReport;
		LobbyReportSummary summary = LobbyReportSummary.Read(report);
		block.AppendLine($"handover verdict: {(LobbyHandoverVerdict)report.Verdict}");
		block.AppendLine($"handover reason: {(string.IsNullOrEmpty(report.Failure) ? "none" : report.Failure)}");
		block.AppendLine($"handover round trip now (best/worst/mean): {summary.CurrentBest}/{summary.CurrentWorst}/{summary.CurrentMean} ms");
		block.AppendLine($"handover round trip proposed (best/worst/mean): {summary.CandidateBest}/{summary.CandidateWorst}/{summary.CandidateMean} ms");
		block.AppendLine($"handover machine stability now: {DescribeStabilityRank(report.CurrentHostStabilityRank)}");
		block.AppendLine($"handover machine stability proposed: {DescribeStabilityRank(report.CandidateStabilityRank)}");
		block.AppendLine($"handover new endpoint measured from outside: {(report.CandidateEndpointVerified ? "yes" : "not measured")}");
		block.AppendLine($"handover map held at the lobby's version: {(report.MapMatched ? "yes" : "no")}");

		LobbyHandoverSeatMetric[] seats = report.Seats ?? [];
		for (int index = 0; index < seats.Length; index++)
		{
			LobbyHandoverSeatMetric seat = seats[index];
			block.AppendLine($"handover seat {seat.Slot} ({seat.Name}): now {DescribeDiagnosticLink(seat.CurrentHostMeasured, seat.CurrentHostRoundTripMilliseconds, seat.CurrentHostJitterMilliseconds, seat.CurrentHostPacketLossPercent)}; proposed {DescribeDiagnosticLink(seat.CandidateMeasured, seat.CandidateRoundTripMilliseconds, seat.CandidateJitterMilliseconds, seat.CandidatePacketLossPercent)}");
		}
	}

	private static string DescribeDiagnosticLink(bool measured, int roundTripMilliseconds, int jitterMilliseconds, float packetLossPercent)
	{
		return measured ? $"{roundTripMilliseconds} ms / {jitterMilliseconds} ms jitter / {packetLossPercent:0.#}% loss" : "not measured";
	}

	private static string DescribeStabilityRank(int rank)
	{
		return rank switch
		{
			HostStabilityRanks.Excellent => "excellent",
			HostStabilityRanks.Average => "average",
			HostStabilityRanks.Poor => "poor",
			_ => "not measured"
		};
	}

	private string DescribeSeatByPeerId(int peerId)
	{
		if (peerId <= 0) return "none";
		foreach (PlayerInfo player in PlayerList)
		{
			if (player.PeerId == peerId) return player.Name;
		}

		return "not seated";
	}
}
