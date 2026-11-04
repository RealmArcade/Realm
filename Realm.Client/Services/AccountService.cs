using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Services;

public enum AccountOperationStatus
{
	Success = 0,

	Rejected = 1,

	Unreachable = 2,

	Cancelled = 3
}

/// <summary>
///     What one account request ended as. A refusal carries the typed code the authority answered with; an unreachable
///     authority is its own status so the menu can say so instead of waiting forever.
/// </summary>
public class AccountOperationResult
{
	public AccountOperationStatus Status { get; init; } = AccountOperationStatus.Success;

	public string ErrorCode { get; init; } = string.Empty;

	public string Message { get; init; } = string.Empty;

	public AccountSession? Session { get; init; }

	public bool Succeeded => Status == AccountOperationStatus.Success;

	public static AccountOperationResult Ok(AccountSession? session) => new() { Session = session };

	public static AccountOperationResult Rejected(string errorCode, string message) => new()
	{
		Status = AccountOperationStatus.Rejected,
		ErrorCode = errorCode,
		Message = message
	};

	public static AccountOperationResult Unreachable(string message) => new()
	{
		Status = AccountOperationStatus.Unreachable,
		Message = message
	};
}

public class AccountSession
{
	public string Token { get; set; } = string.Empty;

	public long AccountId { get; set; }

	public string DisplayName { get; set; } = string.Empty;

	public string[] LinkedProviders { get; set; } = Array.Empty<string>();

	/// <summary>
	///     When the session stops being accepted, or <c>null</c> when it lasts until it is revoked. An ordinary sign-in is the
	///     second case, so the stored token keeps working on the next launch.
	/// </summary>
	public DateTime? ExpiresAtUtc { get; set; }
}

/// <summary>
///     The client's view of the account it is signed in as. The session token is the only credential here; it and the account it
///     describes are stored in the user data directory and restored at launch without asking the authority anything, because an
///     unreachable authority must not leave a player unable to play, and nothing an unconfirmed token can reach is sensitive.
///     Every name this service reports is still the authority's, and the first answer that says the session is gone drops it.
///     Signing in needs nothing but a Steam ticket: Steamworks mints it on this machine and the authority exchanges it for the
///     SteamID64 it links, so no browser, redirect or password is involved at any point.
/// </summary>
public class AccountService
{
	private const string SessionFileName = "account_session.json";

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

	private readonly object _sessionLock = new();

	private AccountSession? _session;

	public event Action? Changed;

	public bool IsSignedIn
	{
		get
		{
			lock (_sessionLock) return _session != null;
		}
	}

	public long AccountId
	{
		get
		{
			lock (_sessionLock) return _session?.AccountId ?? 0;
		}
	}

	public string DisplayName
	{
		get
		{
			lock (_sessionLock) return _session?.DisplayName ?? string.Empty;
		}
	}

	public string SessionToken
	{
		get
		{
			lock (_sessionLock) return _session?.Token ?? string.Empty;
		}
	}

	public string[] LinkedProviders
	{
		get
		{
			lock (_sessionLock) return _session?.LinkedProviders ?? Array.Empty<string>();
		}
	}

	/// <summary>
	///     The one node this client signs in against, resolved once from `servers.json` rather than from whichever bootstrap
	///     server the lobby list happened to pick.
	/// </summary>
	public static string AuthorityUrl { get; } = ServersConfigHelper.GetAccountAuthorityUrl();

	/// <summary>
	///     Adopts the session this client stored when it signed in, without a request. The stored copy carries its own expiry, so a
	///     session that has aged out is dropped here; everything else waits until the authority is actually consulted, which is when
	///     the player opens the account screen or asks for something that outlives the session.
	/// </summary>
	public void Restore()
	{
		AccountSession? stored = ReadStoredSession();
		if (stored == null) return;

		if (stored.ExpiresAtUtc.HasValue && stored.ExpiresAtUtc.Value <= DateTime.UtcNow)
		{
			ClearStoredToken();
			return;
		}

		SetSession(stored);
	}

	/// <summary>
	///     Asks the authority whether the session is still accepted and adopts what it answers. Opened by the account screen rather
	///     than at launch, so a renamed account or a session revoked elsewhere is noticed exactly when the player looks at it.
	/// </summary>
	public async Task<AccountOperationResult> RefreshAsync()
	{
		string token = SessionToken;
		if (string.IsNullOrEmpty(token)) return AccountOperationResult.Ok(null);

		AccountOperationResult validated = await ValidateTokenAsync(token).ConfigureAwait(false);
		if (validated.Succeeded && validated.Session != null) SetSession(validated.Session);

		return validated;
	}

	/// <summary>
	///     Signs in with a Steam ticket this machine minted. A SteamID64 no account has seen yet registers one under the name
	///     supplied here, so the same call also carries a player's chosen name on their first sign-in; the authority refuses a name
	///     it cannot accept and the player simply tries another, which costs one more ticket.
	/// </summary>
	public async Task<AccountOperationResult> SignInWithSteamAsync(string displayName, string ticket, uint appId)
	{
		if (string.IsNullOrEmpty(ticket)) return AccountOperationResult.Rejected(AccountErrorCodes.CredentialsInvalid, "Steam did not return a sign-in ticket.");

		return await PostAsync("/auth/steam/ticket", new SteamTicketSignInRequest
		{
			Ticket = ticket,
			AppId = appId,
			DisplayName = displayName ?? string.Empty
		}, null).ConfigureAwait(false);
	}

	public async Task<AccountOperationResult> RenameAsync(string displayName)
	{
		string token = SessionToken;
		if (string.IsNullOrEmpty(token)) return AccountOperationResult.Rejected(AccountErrorCodes.SessionRequired, "Sign in before changing your display name.");

		return await PostAsync("/auth/rename", new RenameAccountRequest { DisplayName = displayName }, token).ConfigureAwait(false);
	}

	public async Task SignOutAsync()
	{
		string token = SessionToken;
		ClearStoredToken();
		SetSession(null);

		if (string.IsNullOrEmpty(token)) return;

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, $"{AuthorityUrl}/auth/logout");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			using HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
			_ = response.IsSuccessStatusCode;
		}
		catch (HttpRequestException)
		{
		}
		catch (TaskCanceledException)
		{
		}
	}

	public async Task<AccountProvidersDto?> GetProvidersAsync()
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync($"{AuthorityUrl}/auth/providers").ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) return null;

			string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			return JsonSerializer.Deserialize<AccountProvidersDto>(json, JsonOptions);
		}
		catch (HttpRequestException)
		{
			return null;
		}
		catch (TaskCanceledException)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>
	///     The multiplayer record the authority holds for the account this screen is signed in as, or <c>null</c> when it
	///     cannot be told: signed out, unreachable, or an answer this build cannot read. Nothing the profile screen shows is
	///     ever guessed from a failure.
	/// </summary>
	public async Task<PlayerMatchStatsDto?> GetMatchStatsAsync()
	{
		string token = SessionToken;
		if (string.IsNullOrEmpty(token)) return null;

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, $"{AuthorityUrl}/profile/stats");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			using HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) return null;

			string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			return JsonSerializer.Deserialize<PlayerMatchStatsDto>(json, JsonOptions);
		}
		catch (HttpRequestException)
		{
			return null;
		}
		catch (TaskCanceledException)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>
	///     Reports one finished multiplayer match against the record of the account this screen is signed in as. The match id
	///     is what makes this safe to repeat: the authority counts a given match once, so a retried report does not move the
	///     record twice. The record as it now stands comes back, or <c>null</c> when nothing was recorded.
	/// </summary>
	public async Task<PlayerMatchStatsDto?> ReportMatchResultAsync(string matchId, bool victory)
	{
		string token = SessionToken;
		if (string.IsNullOrEmpty(token) || string.IsNullOrWhiteSpace(matchId)) return null;

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, $"{AuthorityUrl}/profile/match_result");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			request.Content = new StringContent(JsonSerializer.Serialize(new MatchResultReport { MatchId = matchId, Victory = victory }), Encoding.UTF8, "application/json");

			using HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) return null;

			string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			return JsonSerializer.Deserialize<PlayerMatchStatsDto>(json, JsonOptions);
		}
		catch (HttpRequestException)
		{
			return null;
		}
		catch (TaskCanceledException)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private async Task<AccountOperationResult> ValidateTokenAsync(string token)
	{
		if (string.IsNullOrWhiteSpace(token)) return AccountOperationResult.Rejected(AccountErrorCodes.SessionRequired, "No session was presented.");

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, $"{AuthorityUrl}/auth/session");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			using HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
			string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				AccountOperationResult rejection = RejectedOrUnreachable(response, json);
				if (rejection.ErrorCode == AccountErrorCodes.SessionInvalid) SetSession(null);
				return rejection;
			}

			SessionValidation? validation = JsonSerializer.Deserialize<SessionValidation>(json, JsonOptions);
			if (validation == null) return AccountOperationResult.Rejected(AccountErrorCodes.SessionInvalid, "The account authority answered a session this build cannot read.");

			return AccountOperationResult.Ok(new AccountSession
			{
				Token = token,
				AccountId = long.TryParse(validation.AccountId, out long accountId) ? accountId : 0,
				DisplayName = validation.DisplayName,
				LinkedProviders = validation.LinkedProviders ?? Array.Empty<string>(),
				ExpiresAtUtc = validation.ExpiresAtUtc
			});
		}
		catch (HttpRequestException)
		{
			return AccountOperationResult.Unreachable("The account authority could not be reached.");
		}
		catch (TaskCanceledException)
		{
			return AccountOperationResult.Unreachable("The account authority did not answer in time.");
		}
		catch (JsonException)
		{
			return AccountOperationResult.Rejected(AccountErrorCodes.RequestMalformed, "The account authority answered something this build cannot read.");
		}
	}

	private async Task<AccountOperationResult> PostAsync<TRequest>(string path, TRequest body, string? sessionToken)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, $"{AuthorityUrl}{path}");
			if (!string.IsNullOrEmpty(sessionToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
			request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

			using HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
			string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				AccountOperationResult rejection = RejectedOrUnreachable(response, json);
				if (rejection.ErrorCode == AccountErrorCodes.SessionInvalid) SetSession(null);
				return rejection;
			}

			AccountSessionDto? session = JsonSerializer.Deserialize<AccountSessionDto>(json, JsonOptions);
			if (session == null || string.IsNullOrEmpty(session.SessionToken))
			{
				return AccountOperationResult.Rejected(AccountErrorCodes.RequestMalformed, "The account authority answered no session this build can read.");
			}

			var accepted = new AccountSession
			{
				Token = session.SessionToken,
				AccountId = session.AccountId,
				DisplayName = session.DisplayName,
				LinkedProviders = session.LinkedProviders ?? Array.Empty<string>(),
				ExpiresAtUtc = session.ExpiresAtUtc
			};

			SetSession(accepted);
			return AccountOperationResult.Ok(accepted);
		}
		catch (HttpRequestException)
		{
			return AccountOperationResult.Unreachable("The account authority could not be reached.");
		}
		catch (TaskCanceledException)
		{
			return AccountOperationResult.Unreachable("The account authority did not answer in time.");
		}
		catch (JsonException)
		{
			return AccountOperationResult.Rejected(AccountErrorCodes.RequestMalformed, "The account authority answered something this build cannot read.");
		}
	}

	private static AccountOperationResult RejectedOrUnreachable(HttpResponseMessage response, string json)
	{
		if ((int)response.StatusCode >= 500) return AccountOperationResult.Unreachable("The account authority reported a problem.");

		try
		{
			AccountErrorDto? error = JsonSerializer.Deserialize<AccountErrorDto>(json, JsonOptions);
			if (error != null && error.Code.Length > 0) return AccountOperationResult.Rejected(error.Code, error.Message);
		}
		catch (JsonException)
		{
		}

		return AccountOperationResult.Rejected(AccountErrorCodes.RequestMalformed, "The account authority refused the request.");
	}

	private void SetSession(AccountSession? session)
	{
		lock (_sessionLock) _session = session;

		if (session == null) ClearStoredToken();
		else WriteStoredSession(session);

		Changed?.Invoke();
	}

	private static string SessionFilePath => Path.Combine(OS.GetUserDataDir(), "auth", SessionFileName);

	private static AccountSession? ReadStoredSession()
	{
		try
		{
			string path = SessionFilePath;
			if (!File.Exists(path)) return null;

			AccountSession? stored = JsonSerializer.Deserialize<AccountSession>(File.ReadAllText(path), JsonOptions);
			return string.IsNullOrWhiteSpace(stored?.Token) ? null : stored;
		}
		catch (IOException)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static void WriteStoredSession(AccountSession session)
	{
		try
		{
			string path = SessionFilePath;
			string? directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
			File.WriteAllText(path, JsonSerializer.Serialize(session, JsonOptions));
		}
		catch (IOException exception)
		{
			GD.PrintErr($"[AccountService] Could not persist the session: {exception.Message}");
		}
	}

	private static void ClearStoredToken()
	{
		try
		{
			string path = SessionFilePath;
			if (File.Exists(path)) File.Delete(path);
		}
		catch (IOException)
		{
		}
	}

	private class SessionValidation
	{
		public string AccountId { get; set; } = string.Empty;

		public string DisplayName { get; set; } = string.Empty;

		public string[]? LinkedProviders { get; set; }

		public DateTime? ExpiresAtUtc { get; set; }
	}
}
