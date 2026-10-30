using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Realm.Shared.Accounts;

namespace Realm.AdminServer.Accounts;

public class AuthFlowRecord
{
	public string State { get; init; } = string.Empty;

	public AccountProviderKind Provider { get; init; }

	public int LoopbackPort { get; init; }

	public string ClientState { get; init; } = string.Empty;

	public long LinkToAccountId { get; init; }

	public string CodeVerifier { get; init; } = string.Empty;

	public string ProviderReturnUrl { get; init; } = string.Empty;

	public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public class PendingProviderRegistration
{
	public string Ticket { get; init; } = string.Empty;

	public AccountProviderKind Provider { get; init; }

	public string ProviderAccountToken { get; init; } = string.Empty;

	public string SuggestedDisplayName { get; init; } = string.Empty;

	public string? SuggestedDisplayNameErrorCode { get; init; }

	public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
///     The short-lived state behind a browser sign-in: an unguessable <c>state</c> bound to the client's waiting loopback
///     listener and to the client's own opaque state, a bounded lifetime, and one-shot consumption of the state and the
///     authorization code. A provider identity that is not linked yet becomes a pending registration keyed by a one-shot
///     ticket, so the client never receives the provider's account token.
/// </summary>
public class AuthFlowService
{
	private readonly AccountAuthorityOptions _options;
	private readonly ConcurrentDictionary<string, AuthFlowRecord> _flows = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, PendingProviderRegistration> _pendingRegistrations = new(StringComparer.Ordinal);

	public AuthFlowService(AccountAuthorityOptions options)
	{
		_options = options;
	}

	public bool TryStart(AccountProviderKind provider, int loopbackPort, string? clientState, long linkToAccountId, out AuthFlowRecord? flow, out string errorCode)
	{
		flow = null;
		errorCode = string.Empty;

		if (loopbackPort is < 1024 or > 65535)
		{
			errorCode = AccountErrorCodes.RequestMalformed;
			return false;
		}

		PruneExpired();

		string state = CreateOpaqueValue();
		string codeVerifier = provider == AccountProviderKind.Discord ? CreateOpaqueValue(48) : string.Empty;
		string providerReturnUrl = provider == AccountProviderKind.Steam
			? BuildSteamReturnUrl(state)
			: _options.Discord.RedirectUri;

		flow = new AuthFlowRecord
		{
			State = state,
			Provider = provider,
			LoopbackPort = loopbackPort,
			ClientState = clientState?.Trim() ?? string.Empty,
			LinkToAccountId = linkToAccountId,
			CodeVerifier = codeVerifier,
			ProviderReturnUrl = providerReturnUrl,
			CreatedAtUtc = DateTime.UtcNow
		};

		_flows[state] = flow;
		return true;
	}

	public bool TryConsume(string? state, out AuthFlowRecord? flow)
	{
		flow = null;
		if (string.IsNullOrWhiteSpace(state)) return false;
		if (!_flows.TryRemove(state.Trim(), out AuthFlowRecord? consumed)) return false;
		if (IsExpired(consumed.CreatedAtUtc)) return false;

		flow = consumed;
		return true;
	}

	public string CreatePendingRegistration(AuthFlowRecord flow, string providerAccountToken, string suggestedDisplayName, string? suggestedDisplayNameErrorCode)
	{
		PruneExpired();

		string ticket = CreateOpaqueValue();
		_pendingRegistrations[ticket] = new PendingProviderRegistration
		{
			Ticket = ticket,
			Provider = flow.Provider,
			ProviderAccountToken = providerAccountToken,
			SuggestedDisplayName = suggestedDisplayName,
			SuggestedDisplayNameErrorCode = suggestedDisplayNameErrorCode,
			CreatedAtUtc = DateTime.UtcNow
		};

		return ticket;
	}

	public bool TryTakePendingRegistration(string? ticket, out PendingProviderRegistration? pending)
	{
		pending = null;
		if (string.IsNullOrWhiteSpace(ticket)) return false;
		if (!_pendingRegistrations.TryRemove(ticket.Trim(), out PendingProviderRegistration? taken)) return false;
		if (IsExpired(taken.CreatedAtUtc)) return false;

		pending = taken;
		return true;
	}

	public string BuildLoopbackCallbackUrl(AuthFlowRecord flow, string query)
	{
		return $"http://localhost:{flow.LoopbackPort}/auth/callback/?{query}";
	}

	public string BuildLoopbackFailureUrl(AuthFlowRecord flow, string errorCode)
	{
		return BuildLoopbackCallbackUrl(flow, $"{AccountQueryParameters.ClientState}={Uri.EscapeDataString(flow.ClientState)}&{AccountQueryParameters.ErrorCode}={Uri.EscapeDataString(errorCode)}");
	}

	public static string CreateOpaqueValue(int byteCount = 32)
	{
		return Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	public static string CreatePkceChallenge(string codeVerifier)
	{
		return Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	private string BuildSteamReturnUrl(string state)
	{
		if (string.IsNullOrEmpty(_options.Steam.ReturnUrl)) return string.Empty;
		string separator = _options.Steam.ReturnUrl.Contains('?') ? "&" : "?";
		return $"{_options.Steam.ReturnUrl}{separator}state={Uri.EscapeDataString(state)}";
	}

	private bool IsExpired(DateTime createdAtUtc)
	{
		return DateTime.UtcNow - createdAtUtc > TimeSpan.FromSeconds(_options.ProviderFlowTimeoutSeconds);
	}

	private void PruneExpired()
	{
		foreach (var pair in _flows)
		{
			if (IsExpired(pair.Value.CreatedAtUtc)) _flows.TryRemove(pair.Key, out _);
		}

		foreach (var pair in _pendingRegistrations)
		{
			if (IsExpired(pair.Value.CreatedAtUtc)) _pendingRegistrations.TryRemove(pair.Key, out _);
		}
	}
}
