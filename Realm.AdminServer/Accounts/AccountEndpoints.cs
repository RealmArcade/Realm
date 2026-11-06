using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     The account authority's HTTP surface. Every mutation either succeeds with an account or a session, or answers a typed
///     error code; nothing here returns a provider secret, a Steam ticket or a credential.
/// </summary>
public static class AccountEndpoints
{
	public static void MapAccountEndpoints(WebApplication app, AccountAuthorityOptions options, HashSet<string> adminPublicKeys)
	{
		app.MapGet("/auth/providers", (AccountStoreService store) => Results.Ok(BuildProviders(options, store)));

		app.MapPost("/auth/steam/ticket", async (SteamTicketSignInRequest signIn, SteamTicketService steam, AccountStoreService store, CancellationToken cancellationToken) =>
		{
			if (!store.IsAuthority) return Refusal(AccountMutationResult.Refused(AccountMutationOutcome.RefusedNotAuthority, "This node is not the configured account authority."));

			string? steamId = await steam.ResolveSteamIdAsync(signIn.AppId, signIn.Ticket, cancellationToken);
			if (steamId == null)
			{
				return Results.Json(
					AccountErrorDto.Of(AccountErrorCodes.CredentialsInvalid, "Steam did not accept this sign-in. Start the game through Steam and try again."),
					statusCode: StatusCodes.Status401Unauthorized);
			}

			AccountMutationResult result = store.SignInOrRegisterProvider(AccountProviderKind.Steam, steamId, signIn.DisplayName);
			return result.Succeeded && result.Session != null ? Results.Ok(result.Session) : Refusal(result);
		});

		app.MapGet("/auth/session", (HttpRequest request, AccountStoreService store) =>
		{
			string? token = ReadSessionToken(request);
			if (!store.TryValidateSession(token, out AccountRecord? account, out AccountSessionDto? session, out _) || account == null || session == null)
			{
				return Results.Json(AccountErrorDto.Of(AccountErrorCodes.SessionInvalid, "The session is no longer valid."), statusCode: StatusCodes.Status401Unauthorized);
			}

			return Results.Ok(new
			{
				AccountId = account.AccountId.ToString(CultureInfo.InvariantCulture),
				account.DisplayName,
				LinkedProviders = account.LinkedProviderNames(),
				account.CreatedAtUtc,
				session.ExpiresAtUtc
			});
		});

		app.MapGet("/profile/stats", (HttpRequest request, AccountStoreService store) =>
		{
			if (!TryReadSignedInAccount(request, store, out AccountRecord statsAccount))
			{
				return Results.Json(AccountErrorDto.Of(AccountErrorCodes.SessionInvalid, "The session is no longer valid."), statusCode: StatusCodes.Status401Unauthorized);
			}

			return Results.Ok(store.GetMatchStats(statsAccount.AccountId));
		});

		app.MapPost("/profile/match_result", (HttpRequest request, MatchResultReport report, AccountStoreService store) =>
		{
			if (!store.IsAuthority) return Refusal(AccountMutationResult.Refused(AccountMutationOutcome.RefusedNotAuthority, "This node is not the configured account authority."));
			if (!TryReadSignedInAccount(request, store, out AccountRecord playingAccount))
			{
				return Results.Json(AccountErrorDto.Of(AccountErrorCodes.SessionInvalid, "The session is no longer valid."), statusCode: StatusCodes.Status401Unauthorized);
			}

			if (!store.TryRecordMatchResult(playingAccount.AccountId, report.MatchId, report.Victory, out PlayerMatchStatsDto stats))
			{
				return Results.BadRequest(AccountErrorDto.Of(AccountErrorCodes.RequestMalformed, "A match result has to name the match it belongs to."));
			}

			return Results.Ok(stats);
		});

		app.MapPost("/auth/rename", (HttpRequest request, RenameAccountRequest rename, AccountStoreService store) =>
		{
			if (!TryReadSignedInAccount(request, store, out AccountRecord account)) return Results.Json(AccountErrorDto.Of(AccountErrorCodes.SessionInvalid, "The session is no longer valid."), statusCode: StatusCodes.Status401Unauthorized);
			if (!store.HasRecentCredentialProof(ReadSessionToken(request)))
			{
				return Results.Json(AccountErrorDto.Of(AccountErrorCodes.StepUpRequired, "Sign in with Steam again before changing the account you are signed in to."), statusCode: StatusCodes.Status401Unauthorized);
			}

			AccountMutationResult result = store.Rename(account.AccountId, rename.DisplayName, $"account:{account.AccountId}", null, rename.AuthorshipPublicKey, rename.AuthorshipSignature);
			if (!result.Succeeded || result.Account == null) return Refusal(result);

			string? previousToken = ReadSessionToken(request);
			if (!string.IsNullOrEmpty(previousToken)) store.RevokeSession(previousToken);

			return Results.Ok(store.IssueSession(result.Account));
		});

		app.MapPost("/auth/logout", (HttpRequest request, AccountStoreService store) =>
		{
			string? token = ReadSessionToken(request);
			store.RevokeSession(token);
			return Results.Ok(new { Status = "SignedOut" });
		});

		app.MapPost("/auth/admin/rename", (AdminRenameAccountRequest rename, AccountStoreService store) =>
		{
			if (!store.IsAuthority) return Refusal(AccountMutationResult.Refused(AccountMutationOutcome.RefusedNotAuthority, "This node is not the configured account authority."));
			if (adminPublicKeys.Count == 0) return Results.Json(AccountErrorDto.Of(AccountErrorCodes.AdminProofInvalid, "No admin public keys are configured."), statusCode: StatusCodes.Status403Forbidden);

			if (rename.AccountId <= 0 || string.IsNullOrWhiteSpace(rename.Nonce) || string.IsNullOrWhiteSpace(rename.Signature))
			{
				return Results.BadRequest(AccountErrorDto.Of(AccountErrorCodes.AdminProofMalformed, "The administrator proof is missing an account, a nonce or a signature."));
			}

			if (!DisplayNameRules.TryCanonicalize(rename.DisplayName, out string canonical, out DisplayNameError nameError))
			{
				return Results.BadRequest(AccountErrorDto.Of(AccountErrorCodes.ForDisplayNameError(nameError), DisplayNameRules.DescribeError(nameError)));
			}

			if (!AccountAdminRenameProof.IsTimestampFresh(rename.TimestampUnixSeconds, DateTime.UtcNow))
			{
				return Results.BadRequest(AccountErrorDto.Of(AccountErrorCodes.AdminProofStale, "The administrator proof is outside the accepted clock window."));
			}

			if (!AccountAdminRenameProof.VerifyAny(adminPublicKeys, rename.AdminPublicKey, rename.AccountId, canonical, rename.Nonce, rename.TimestampUnixSeconds, rename.Signature))
			{
				return Results.Json(AccountErrorDto.Of(AccountErrorCodes.AdminProofInvalid, "The administrator proof did not verify."), statusCode: StatusCodes.Status403Forbidden);
			}

			AccountMutationResult result = store.Rename(rename.AccountId, canonical, "admin", rename.Nonce);
			if (!result.Succeeded || result.Account == null) return Refusal(result);

			return Results.Ok(new
			{
				AccountId = result.Account.AccountId.ToString(CultureInfo.InvariantCulture),
				result.Account.DisplayName
			});
		});
	}

	private static AccountProvidersDto BuildProviders(AccountAuthorityOptions options, AccountStoreService store)
	{
		var warnings = new List<string>();
		foreach (string missing in options.Steam.MissingSettings()) warnings.Add($"provider_not_configured:{AccountProviderNames.Steam}:{missing}");
		if (!store.IsAuthority) warnings.Add("account_node_is_not_the_authority");

		return new AccountProvidersDto
		{
			Providers = new[]
			{
				new AccountProviderInfoDto { Kind = AccountProviderNames.Steam, Available = options.Steam.IsConfigured }
			},
			Warnings = warnings.ToArray()
		};
	}

	private static bool TryReadSignedInAccount(HttpRequest request, AccountStoreService store, out AccountRecord account)
	{
		account = new AccountRecord();
		string? token = ReadSessionToken(request);
		if (!store.TryValidateSession(token, out AccountRecord? validated, out _, out _) || validated == null) return false;

		account = validated;
		return true;
	}

	public static string? ReadSessionToken(HttpRequest request)
	{
		string authorization = request.Headers.Authorization.ToString();
		if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			string bearer = authorization[7..].Trim();
			if (bearer.Length > 0) return bearer;
		}

		string direct = request.Headers["X-Session-Token"].ToString();
		return string.IsNullOrWhiteSpace(direct) ? null : direct.Trim();
	}

	private static IResult Refusal(AccountMutationResult result)
	{
		string code = result.Outcome switch
		{
			AccountMutationOutcome.DisplayNameInvalid or AccountMutationOutcome.DisplayNameTaken or AccountMutationOutcome.DisplayNameVisuallySimilar => AccountErrorCodes.ForDisplayNameError(result.DisplayNameError),
			AccountMutationOutcome.RefusedNotAuthority => AccountErrorCodes.AccountNotAuthority,
			AccountMutationOutcome.CredentialsInvalid => AccountErrorCodes.CredentialsInvalid,
			AccountMutationOutcome.SessionInvalid => AccountErrorCodes.SessionInvalid,
			AccountMutationOutcome.AccountNotFound => AccountErrorCodes.AccountNotFound,
			AccountMutationOutcome.AdminProofMalformed => AccountErrorCodes.AdminProofMalformed,
			AccountMutationOutcome.AdminProofInvalid => AccountErrorCodes.AdminProofInvalid,
			AccountMutationOutcome.AdminProofStale => AccountErrorCodes.AdminProofStale,
			AccountMutationOutcome.AdminProofReplayed => AccountErrorCodes.AdminProofReplayed,
			AccountMutationOutcome.StorageFailure => AccountErrorCodes.AccountStoreUnavailable,
			_ => AccountErrorCodes.RequestMalformed
		};

		int statusCode = result.Outcome switch
		{
			AccountMutationOutcome.DisplayNameTaken or AccountMutationOutcome.DisplayNameVisuallySimilar => StatusCodes.Status409Conflict,
			AccountMutationOutcome.CredentialsInvalid or AccountMutationOutcome.SessionInvalid => StatusCodes.Status401Unauthorized,
			AccountMutationOutcome.RefusedNotAuthority => StatusCodes.Status403Forbidden,
			AccountMutationOutcome.AccountNotFound => StatusCodes.Status404NotFound,
			AccountMutationOutcome.StorageFailure => StatusCodes.Status503ServiceUnavailable,
			_ => StatusCodes.Status400BadRequest
		};

		return Results.Json(AccountErrorDto.Of(code, result.Message), statusCode: statusCode);
	}
}
