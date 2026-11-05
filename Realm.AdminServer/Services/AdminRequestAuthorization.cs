using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Realm.Shared.Distribution;

namespace Realm.AdminServer.Services;

/// <summary>
///     Decides whether an administrative request was signed by one of the node's configured admin keys. A header that merely
///     looks like a credential is not a credential, so every administrative route asks here instead of trusting the presence
///     of an Authorization header.
/// </summary>
public static class AdminRequestAuthorization
{
	public static bool IsAuthorized(HttpRequest request, IReadOnlySet<string> adminPublicKeys, string resource, string action)
	{
		if (request == null || adminPublicKeys == null || adminPublicKeys.Count == 0) return false;

		string bypassToken = request.Headers[AdminBypassAuth.BypassTokenHeaderName].ToString();
		if (AdminBypassAuth.VerifyBypassToken(adminPublicKeys, resource, action, bypassToken)) return true;

		string publicKey = request.Headers[AdminBypassAuth.PublicKeyHeaderName].ToString().Trim();
		string signature = request.Headers[AdminBypassAuth.SignatureHeaderName].ToString().Trim();
		if (publicKey.Length == 0 || signature.Length == 0) return false;
		if (!adminPublicKeys.Contains(publicKey)) return false;

		return AuthorSignatureHelper.VerifySignature(publicKey, BuildSignaturePayload(resource, action), signature);
	}

	public static string BuildSignaturePayload(string resource, string action)
	{
		return $"{NormalizeSegment(resource)}:{NormalizeSegment(action)}";
	}

	private static string NormalizeSegment(string segment)
	{
		return (segment ?? string.Empty).Trim().ToLowerInvariant();
	}
}
