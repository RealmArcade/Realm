using System.Collections.Generic;

namespace Realm.Shared.Reachability;

/// <summary>
///     A check one registry node asked another node to run for the same lobby, so that a lobby is never judged on one
///     vantage point alone. The node that lists the lobby stays the one that answers the host; the other nodes' answers
///     arrive as second opinions about the same game port.
/// </summary>
public readonly record struct ReachabilityPeerCheck(string NodeUrl, string ProbeId, string ProbeIp, int ProbePort)
{
	/// <summary>The endpoint the host must punch so the peer node's probe can reach its game port.</summary>
	public ReachabilityPeerProbe Probe => new(ProbeIp, ProbePort, ProbeId);
}

/// <summary>
///     A peer node's probe socket, named so the host can punch it. A node checking a lobby behind a router that admits
///     nothing unsolicited has the same problem a player does: its probe is dropped until the host sends it something first,
///     and the host is the only party that can send from the port being measured.
/// </summary>
public readonly record struct ReachabilityPeerProbe(string Ip, int Port, string ProbeId);

/// <summary>
///     Combines what a registry node measured with what its peer nodes measured about the same game port. A peer node is a
///     second destination the host's router had to translate for, so its observation answers the question a single node
///     cannot: whether the outside port the game port answered from is the same one every destination gets. It also widens
///     the claim from "this node can reach the lobby" to "these nodes can", which is as close to "a player elsewhere can"
///     as one deployment can measure.
/// </summary>
public static class SecondVantage
{
	/// <summary>
	///     Merges peer observations into the local one. The local node's own endpoint and answer stand: it is the node the
	///     lobby is listed on, and a peer's view of an address this node never reached is not an address it may publish. What
	///     the peers contribute is a second destination (when this node did not measure one itself), the weakest cold answer
	///     any of them got, and how many sources said what.
	/// </summary>
	/// <param name="local">What this node measured.</param>
	/// <param name="peerReports">What the peer nodes measured, in the order they were asked.</param>
	/// <returns>The report this node answers with.</returns>
	public static ReachabilityReport Merge(ReachabilityReport local, IReadOnlyList<ReachabilityReport> peerReports)
	{
		int sourcesAnswered = local.Answered ? 1 : 0;
		string secondObservedIp = local.SecondObservedIp;
		int secondObservedPort = local.SecondObservedPort;
		bool coldAnswered = local.ColdAnswered;

		for (int index = 0; index < peerReports.Count; index++)
		{
			ReachabilityReport peer = peerReports[index];
			if (peer == null || !peer.Answered || peer.ObservedPort <= 0) continue;

			sourcesAnswered++;

			// The first second opinion is the second destination. A peer that saw a different outside port than the one this
			// node measured is not recorded here: the pair this node already has is what states the mapping, and a third
			// number would only restate it. What a peer adds is the destination this node could not measure itself.
			if (secondObservedPort == 0)
			{
				secondObservedPort = peer.ObservedPort;
				secondObservedIp = peer.ObservedIp;
			}

			// A port that answered a cold socket at one node and only a punched one at another is reachable without a punch
			// for some players and not for others, which is a lobby that needs a punch. The weakest answer is the claim.
			coldAnswered &= peer.ColdAnswered;
		}

		return local with
		{
			ColdAnswered = coldAnswered,
			SecondObservedIp = secondObservedIp,
			SecondObservedPort = secondObservedPort,
			SourcesChecked = 1 + peerReports.Count,
			SourcesAnswered = sourcesAnswered
		};
	}
}
