using System;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     Builds the path layer of one relayed connection from the plan both peers were given. A carrier that cannot be opened
///     is left out and the connection carries on with the ones that could, which is what makes a volunteer that is full or
///     unreachable a missing path rather than a failed join.
/// </summary>
public static class RelayConnectionFactory
{
	/// <summary>
	///     Opens every path the plan offers and wraps them in one path layer. The keys are derived from the join ticket, so
	///     the authority builds the same layer a screen does while a volunteer learns neither.
	/// </summary>
	/// <param name="plan">The volunteers and the join ticket both peers agreed.</param>
	/// <param name="isAuthority">Whether this side is the authority, which decides the direction each key seals.</param>
	/// <param name="failure">Why no path layer was built.</param>
	/// <returns>The connection's path layer, or null when no volunteer could be opened.</returns>
	public static RelayPathMux? TryBuild(RelayConnectionPlan? plan, bool isAuthority, out string failure)
	{
		failure = string.Empty;
		if (plan == null || plan.Offers is not { Count: > 0 })
		{
			failure = "the join named no volunteer to carry it";
			return null;
		}

		// A ticket the join cannot derive keys from is a different refusal from a join that named no volunteer, and the log
		// this sentence reaches is the only place a screen learns which of the two happened.
		if (!RelayJoinSecret.IsUsable(plan.JoinTicket))
		{
			failure = $"the join ticket cannot name a connection, so the {plan.Offers.Count} volunteer(s) it named could not be sealed to it";
			return null;
		}

		byte[] secret = RelayJoinSecret.ReadSecret(plan.JoinTicket);
		RelayConnectionToken token = RelayJoinSecret.DeriveToken(plan.JoinTicket);
		if (secret.Length == 0 || !token.IsSome)
		{
			failure = "the join ticket cannot name a connection";
			return null;
		}

		var cipher = new RelayDatagramCipher(secret, token, isAuthority);
		var mux = new RelayPathMux(token, cipher, plan.DuplicationEnabled, plan.DuplicationBudgetBytesPerSecond);
		for (int index = 0; index < plan.Offers.Count; index++)
		{
			RelayPathOffer offer = plan.Offers[index];
			if (!offer.IsUsable) continue;

			if (!UdpTunnelCarrier.TryOpen(offer.Candidate.Address, offer.Candidate.Port, offer.PathId, offer.Candidate.MaximumPayloadBytes, out UdpTunnelCarrier? carrier, out string carrierFailure))
			{
				failure = carrierFailure;
				continue;
			}

			if (carrier != null) mux.AddPath(carrier);
		}

		if (mux.PathCount == 0)
		{
			mux.Dispose();
			return null;
		}

		return mux;
	}
}
