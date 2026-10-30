using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;

namespace Realm.AdminServer.Accounts;

/// <summary>
///     The account authority's HTTP surface. Every mutation either succeeds with an account or a session, or answers a typed
///     error code; nothing here returns a password, a provider secret or a credential hash.
/// </summary>
public static class AccountEndpoints
{
	public static void MapAccountEndpoints(WebApplication app, AccountAuthorityOptions options, HashSet<string> adminPublicKeys)
	{
		app.MapGet("/auth/providers", (AccountStoreService store) => Results.Ok(BuildProviders(options, store)));

		app.MapPost("/auth/basicauth/register", (RegisterBasicAuthRequest request, AccountStoreService store) =>
		{
			AccountMutationResult result = store.RegisterBasicAuth(request.DisplayName, request.Password);
			return result.Succeeded && result.Session != null ? Results.Ok(result.Session) : Refusal(result);
		});

		app.MapPost("/auth/basicauth/login", (LoginBasicAuthRequest request, HttpContext context, AccountStoreService store) =>
		{
			string sourceKey = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";
			AccountMutationResult result = store.SignInBasicAuth(request.DisplayName, request.Password, sourceKey);
			return result.Succeeded && result.Session != null ? Results.Ok(result.Session) : Refusal(result);
		});

		app.MapPost("/auth/providers/start", (AuthFlowStartRequest request, HttpRequest httpRequest, AuthFlowService flows, DiscordOAuthService discord, SteamOpenIdService steam, AccountStoreService store) =>
		{
			if (!store.IsAuthority) return Refusal(AccountMutationResult.Refused(AccountMutationOutcome.RefusedNotAuthority, "This node is not the configured account authority."));
			if (!AccountProviderNames.TryParse(request.Provider, out AccountProviderKind provider) || provider == AccountProviderKind.BasicAuth)
			{
				return Results.BadRequest(AccountErrorDto.Of(AccountErrorCodes.ProviderUnknown, "Only Discord and Steam sign-in open a browser flow."));
			}

			long linkToAccountId = 0;
			string? sessionToken = ReadSessionToken(httpRequest);
			if (!string.IsNullOrEmpty(sessionToken))
			{
				if (!store.TryValidateSession(sessionToken, out AccountRecord? linkAccount, out _, out _) || linkAccount == null)
				{
					return Results.Json(AccountErrorDto.Of(AccountErrorCodes.SessionInvalid, "The session is no longer valid."), statusCode: StatusCodes.Status401Unauthorized);
				}

				linkToAccountId = linkAccount.AccountId;
			}

			bool providerConfigured = provider == AccountProviderKind.Discord ? options.Discord.IsConfigured : options.Steam.IsConfigured;
			if (!providerConfigured)
			{
				return Results.Json(AccountErrorDto.Of(AccountErrorCodes.ProviderUnavailable, "This provider is not configured on the account authority."), statusCode: StatusCodes.Status503ServiceUnavailable);
			}

			if (!flows.TryStart(provider, request.LoopbackPort, request.ClientState, linkToAccountId, out AuthFlowRecord? flow, out string errorCode) || flow == null)
			{
				return Results.BadRequest(AccountErrorDto.Of(errorCode, "The sign-in flow could not be started."));
			}

			string authorizationUrl = provider == AccountProviderKind.Discord ? discord.BuildAuthorizationUrl(flow) : steam.BuildAuthorizationUrl(flow);
			return Results.Ok(new AuthFlowStartResponse
			{
				Provider = provider.ToWireName(),
				AuthorizationUrl = authorizationUrl,
				State = flow.State
			});
		});

		app.MapGet("/auth/discord/callback", async (string? code, string? state, string? error, AuthFlowService flows, DiscordOAuthService discord, AccountStoreService store, CancellationToken cancellationToken) =>
		{
			if (!flows.TryConsume(state, out AuthFlowRecord? flow) || flow == null || flow.Provider != AccountProviderKind.Discord)
			{
				return ProviderCallbackPage(false, "This sign-in could not be verified. Start it again from the game.");
			}

			if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code))
			{
				return Results.Redirect(flows.BuildLoopbackFailureUrl(flow, AccountErrorCodes.ProviderUnavailable));
			}

			ProviderIdentity? identity = await discord.ExchangeCodeAsync(code, flow, cancellationToken);
			if (identity == null)
			{
				return Results.Redirect(flows.BuildLoopbackFailureUrl(flow, AccountErrorCodes.ProviderUnavailable));
			}

			return CompleteProviderCallback(flow, AccountProviderKind.Discord, identity.Value.ProviderAccountToken, identity.Value.DisplayName, flows, store);
		});

		app.MapGet("/auth/steam/callback", async (HttpRequest request, AuthFlowService flows, SteamOpenIdService steam, AccountStoreService store, CancellationToken cancellationToken) =>
		{
			string? state = request.Query["state"];
			if (!flows.TryConsume(state, out AuthFlowRecord? flow) || flow == null || flow.Provider != AccountProviderKind.Steam)
			{
				return ProviderCallbackPage(false, "This sign-in could not be verified. Start it again from the game.");
			}

			var assertion = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string key in request.Query.Keys)
			{
				if (key.StartsWith("openid.", StringComparison.Ordinal)) assertion[key] = request.Query[key].ToString();
			}

			ProviderIdentity? identity = await steam.VerifyAssertionAsync(assertion, flow, cancellationToken);
			if (identity == null)
			{
				return Results.Redirect(flows.BuildLoopbackFailureUrl(flow, AccountErrorCodes.ProviderUnavailable));
			}

			return CompleteProviderCallback(flow, AccountProviderKind.Steam, identity.Value.ProviderAccountToken, identity.Value.DisplayName, flows, store);
		});

		app.MapPost("/auth/providers/register", (ProviderRegistrationRequest request, AuthFlowService flows, AccountStoreService store) =>
		{
			if (!store.IsAuthority) return Refusal(AccountMutationResult.Refused(AccountMutationOutcome.RefusedNotAuthority, "This node is not the configured account authority."));

			if (!flows.TryTakePendingRegistration(request.RegistrationTicket, out PendingProviderRegistration? pending) || pending == null)
			{
				return Results.BadRequest(AccountErrorDto.Of(AccountErrorCodes.AuthorizationStateInvalid, "That registration has expired. Start the sign-in again."));
			}

			AccountMutationResult result = store.SignInOrRegisterProvider(pending.Provider, pending.ProviderAccountToken, request.DisplayName);
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

		app.MapPost("/auth/rename", (HttpRequest request, RenameAccountRequest rename, AccountStoreService store) =>
		{
			if (!TryReadSignedInAccount(request, store, out AccountRecord account)) return Results.Json(AccountErrorDto.Of(AccountErrorCodes.SessionInvalid, "The session is no longer valid."), statusCode: StatusCodes.Status401Unauthorized);

			AccountMutationResult result = store.Rename(account.AccountId, rename.DisplayName, $"account:{account.AccountId}");
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
		var warnings = new List<string>
		{
			"basicauth_has_no_password_reset",
			"basicauth_has_no_two_factor",
			"basicauth_lost_password_without_linked_provider_makes_the_account_unreachable"
		};

		if (!options.Discord.IsConfigured) warnings.Add($"provider_not_configured:{AccountProviderNames.Discord}");
		if (!options.Steam.IsConfigured) warnings.Add($"provider_not_configured:{AccountProviderNames.Steam}");
		if (!store.IsAuthority) warnings.Add("account_node_is_not_the_authority");

		return new AccountProvidersDto
		{
			Providers = new[]
			{
				new AccountProviderInfoDto { Kind = AccountProviderNames.Discord, Available = options.Discord.IsConfigured, Recommended = true },
				new AccountProviderInfoDto { Kind = AccountProviderNames.Steam, Available = options.Steam.IsConfigured, Recommended = true },
				new AccountProviderInfoDto { Kind = AccountProviderNames.BasicAuth, Available = true, Recommended = false }
			},
			Warnings = warnings.ToArray()
		};
	}

	private static IResult CompleteProviderCallback(AuthFlowRecord flow, AccountProviderKind provider, string providerAccountToken, string providerDisplayName, AuthFlowService flows, AccountStoreService store)
	{
		if (flow.LinkToAccountId != 0)
		{
			AccountMutationResult linkResult = store.LinkProvider(flow.LinkToAccountId, provider, providerAccountToken);
			if (!linkResult.Succeeded || linkResult.Account == null)
			{
				string errorCode = linkResult.Outcome switch
				{
					AccountMutationOutcome.ProviderAlreadyLinked => AccountErrorCodes.ProviderAlreadyLinked,
					AccountMutationOutcome.ProviderLinkOwnedByAnotherAccount => AccountErrorCodes.ProviderLinkOwnedByAnotherAccount,
					_ => AccountErrorCodes.ProviderUnavailable
				};

				return Results.Redirect(flows.BuildLoopbackFailureUrl(flow, errorCode));
			}

			AccountSessionDto session = store.IssueSession(linkResult.Account);
			return Results.Redirect(BuildSignedInCallbackUrl(flows, flow, session));
		}

		AccountRecord? existing = store.FindAccountByProviderLink(provider, providerAccountToken);
		if (existing != null)
		{
			return Results.Redirect(BuildSignedInCallbackUrl(flows, flow, store.IssueSession(existing)));
		}

		bool nameIsUsable = DisplayNameRules.TryCanonicalize(providerDisplayName, out string suggestedName, out DisplayNameError nameError);
		string ticket = flows.CreatePendingRegistration(flow, providerAccountToken, nameIsUsable ? suggestedName : string.Empty, nameIsUsable ? null : AccountErrorCodes.ForDisplayNameError(nameError));

		string query = $"{AccountQueryParameters.ClientState}={Uri.EscapeDataString(flow.ClientState)}"
		               + $"&{AccountQueryParameters.Provider}={Uri.EscapeDataString(provider.ToWireName())}"
		               + $"&registration_ticket={Uri.EscapeDataString(ticket)}"
		               + $"&suggested_display_name={Uri.EscapeDataString(nameIsUsable ? suggestedName : string.Empty)}";

		if (!nameIsUsable) query += $"&suggested_display_name_error={Uri.EscapeDataString(AccountErrorCodes.ForDisplayNameError(nameError))}";

		return Results.Redirect(flows.BuildLoopbackCallbackUrl(flow, query));
	}

	private static string BuildSignedInCallbackUrl(AuthFlowService flows, AuthFlowRecord flow, AccountSessionDto session)
	{
		string query = $"{AccountQueryParameters.ClientState}={Uri.EscapeDataString(flow.ClientState)}"
		               + $"&{AccountQueryParameters.SessionToken}={Uri.EscapeDataString(session.SessionToken)}"
		               + $"&{AccountQueryParameters.AccountId}={Uri.EscapeDataString(session.AccountId.ToString(CultureInfo.InvariantCulture))}"
		               + $"&{AccountQueryParameters.DisplayName}={Uri.EscapeDataString(session.DisplayName)}";

		return flows.BuildLoopbackCallbackUrl(flow, query);
	}

	private static IResult ProviderCallbackPage(bool succeeded, string message)
	{
		string title = succeeded ? "Signed in to Realm" : "Realm sign-in failed";
		string html = $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>{title}</title></head>"
		              + "<body style=\"background:#0f111a;color:#e2e8f0;font-family:Segoe UI,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0\">"
		              + $"<div style=\"max-width:420px;text-align:center\"><h2 style=\"color:#ffd700\">{title}</h2><p>{message}</p></div></body></html>";

		return Results.Content(html, "text/html; charset=utf-8", statusCode: succeeded ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
	}

	private static bool TryReadSignedInAccount(HttpRequest request, AccountStoreService store, out AccountRecord account)
	{
		account = new AccountRecord();
		string? token = ReadSessionToken(request);
		if (!store.TryValidateSession(token, out AccountRecord? validated, out _, out _) || validated == null) return false;

		account = validated;
		return true;
	}

	private static string? ReadSessionToken(HttpRequest request)
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
			AccountMutationOutcome.PasswordTooShort => AccountErrorCodes.PasswordTooShort,
			AccountMutationOutcome.Throttled => AccountErrorCodes.Throttled,
			AccountMutationOutcome.RefusedNotAuthority => AccountErrorCodes.AccountNotAuthority,
			AccountMutationOutcome.CredentialsInvalid => AccountErrorCodes.CredentialsInvalid,
			AccountMutationOutcome.SessionInvalid => AccountErrorCodes.SessionInvalid,
			AccountMutationOutcome.ProviderAlreadyLinked => AccountErrorCodes.ProviderAlreadyLinked,
			AccountMutationOutcome.ProviderLinkOwnedByAnotherAccount => AccountErrorCodes.ProviderLinkOwnedByAnotherAccount,
			AccountMutationOutcome.AccountNotFound => AccountErrorCodes.AccountNotFound,
			AccountMutationOutcome.AdminProofMalformed => AccountErrorCodes.AdminProofMalformed,
			AccountMutationOutcome.AdminProofInvalid => AccountErrorCodes.AdminProofInvalid,
			AccountMutationOutcome.AdminProofStale => AccountErrorCodes.AdminProofStale,
			AccountMutationOutcome.AdminProofReplayed => AccountErrorCodes.AdminProofReplayed,
			_ => AccountErrorCodes.RequestMalformed
		};

		int statusCode = result.Outcome switch
		{
			AccountMutationOutcome.DisplayNameTaken or AccountMutationOutcome.DisplayNameVisuallySimilar => StatusCodes.Status409Conflict,
			AccountMutationOutcome.ProviderLinkOwnedByAnotherAccount or AccountMutationOutcome.ProviderAlreadyLinked => StatusCodes.Status409Conflict,
			AccountMutationOutcome.CredentialsInvalid or AccountMutationOutcome.SessionInvalid => StatusCodes.Status401Unauthorized,
			AccountMutationOutcome.RefusedNotAuthority => StatusCodes.Status403Forbidden,
			AccountMutationOutcome.AccountNotFound => StatusCodes.Status404NotFound,
			AccountMutationOutcome.Throttled => StatusCodes.Status429TooManyRequests,
			_ => StatusCodes.Status400BadRequest
		};

		return Results.Json(AccountErrorDto.Of(code, result.Message), statusCode: statusCode);
	}
}
