using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
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

	public string RegistrationTicket { get; init; } = string.Empty;

	public string SuggestedDisplayName { get; init; } = string.Empty;

	public string SuggestedDisplayNameErrorCode { get; init; } = string.Empty;

	public string Provider { get; init; } = string.Empty;

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

	public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>
///     The client's view of the account it is signed in as. The session token is the only credential here; it is stored in the
///     user data directory, validated on every launch, and silently dropped when the authority no longer accepts it. Every name
///     this service reports is the authority's, so no screen has to invent one.
/// </summary>
public class AccountService
{
	private const string SessionFileName = "account_session.json";

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

	private readonly object _sessionLock = new();

	private HttpListener? _callbackListener;

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

	public static string AuthorityUrl => GameContext.Lobby?.RegistryServerUrl ?? ServersConfigHelper.GetDefaultServerUrl();

	public async Task<AccountOperationResult> RestoreAsync()
	{
		string? storedToken = ReadStoredToken();
		if (string.IsNullOrEmpty(storedToken)) return AccountOperationResult.Ok(null);

		AccountOperationResult validated = await ValidateTokenAsync(storedToken).ConfigureAwait(false);
		if (validated.Succeeded && validated.Session != null)
		{
			SetSession(validated.Session);
			return validated;
		}

		// Only the authority saying no ends the stored session. An unreachable authority is unknown, so the token is kept for the
		// next launch instead of being thrown away because the network was down.
		if (validated.Status == AccountOperationStatus.Unreachable) return AccountOperationResult.Ok(null);

		SetSession(null);
		return AccountOperationResult.Ok(null);
	}

	public async Task<AccountOperationResult> RegisterAsync(string displayName, string password)
	{
		return await PostAsync("/auth/basicauth/register", new RegisterBasicAuthRequest { DisplayName = displayName, Password = password }, null).ConfigureAwait(false);
	}

	public async Task<AccountOperationResult> SignInAsync(string displayName, string password)
	{
		return await PostAsync("/auth/basicauth/login", new LoginBasicAuthRequest { DisplayName = displayName, Password = password }, null).ConfigureAwait(false);
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
	///     Opens the provider's sign-in page in the browser and waits for the authority to hand the result back to a loopback
	///     listener this call has just claimed. The port is chosen now from the free ephemeral range, the request carries an
	///     opaque client state that the answer must echo, and the listener only ever accepts that one callback.
	/// </summary>
	public async Task<AccountOperationResult> StartProviderSignInAsync(string provider, bool linkToCurrentAccount)
	{
		if (!TryOpenCallbackListener(out int port))
		{
			return AccountOperationResult.Unreachable("A local sign-in listener could not be opened.");
		}

		string clientState = AccountClientState.CreateOpaqueValue();
		string token = linkToCurrentAccount ? SessionToken : string.Empty;

		(AccountOperationResult started, AuthFlowStartResponse? startResponse) = await PostForJsonAsync<AuthFlowStartRequest, AuthFlowStartResponse>("/auth/providers/start", new AuthFlowStartRequest
		{
			Provider = provider,
			LoopbackPort = port,
			ClientState = clientState
		}, token).ConfigureAwait(false);

		if (!started.Succeeded)
		{
			CloseCallbackListener();
			return started;
		}

		if (startResponse == null || string.IsNullOrEmpty(startResponse.AuthorizationUrl))
		{
			CloseCallbackListener();
			return AccountOperationResult.Rejected(AccountErrorCodes.ProviderUnavailable, "The authority did not return a sign-in address.");
		}

		OS.ShellOpen(startResponse.AuthorizationUrl);

		return await AwaitProviderCallbackAsync(clientState).ConfigureAwait(false);
	}

	public async Task<AccountOperationResult> CompleteProviderRegistrationAsync(string registrationTicket, string displayName)
	{
		return await PostAsync("/auth/providers/register", new ProviderRegistrationRequest
		{
			RegistrationTicket = registrationTicket,
			DisplayName = displayName
		}, null).ConfigureAwait(false);
	}

	public async Task<AccountOperationResult> ValidateTokenAsync(string token)
	{
		if (string.IsNullOrWhiteSpace(token)) return AccountOperationResult.Rejected(AccountErrorCodes.SessionRequired, "No session was presented.");

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, $"{AuthorityUrl}/auth/session");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			using HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
			string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

			if (!response.IsSuccessStatusCode) return RejectedOrUnreachable(response, json);

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

	private async Task<AccountOperationResult> AwaitProviderCallbackAsync(string clientState)
	{
		HttpListener? listener = _callbackListener;
		if (listener == null) return AccountOperationResult.Unreachable("The local sign-in listener is not running.");

		try
		{
			Task<HttpListenerContext> contextTask = listener.GetContextAsync();
			Task finished = await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(5))).ConfigureAwait(false);
			if (finished != contextTask)
			{
				CloseCallbackListener();
				return AccountOperationResult.Rejected(AccountErrorCodes.AuthorizationStateExpired, "The sign-in timed out.");
			}

			HttpListenerContext context = await contextTask.ConfigureAwait(false);
			return await HandleCallbackAsync(context, clientState).ConfigureAwait(false);
		}
		catch (HttpListenerException)
		{
			return AccountOperationResult.Unreachable("The local sign-in listener stopped before the answer arrived.");
		}
		catch (ObjectDisposedException)
		{
			return AccountOperationResult.Unreachable("The local sign-in listener stopped before the answer arrived.");
		}
		finally
		{
			CloseCallbackListener();
		}
	}

	private async Task<AccountOperationResult> HandleCallbackAsync(HttpListenerContext context, string clientState)
	{
		try
		{
			string returnedState = context.Request.QueryString[AccountQueryParameters.ClientState] ?? string.Empty;
			if (!string.Equals(returnedState, clientState, StringComparison.Ordinal))
			{
				WriteCallbackPage(context.Response, false, "This answer does not belong to the sign-in this game started.");
				return AccountOperationResult.Rejected(AccountErrorCodes.AuthorizationStateInvalid, "The sign-in answer did not belong to this request.");
			}

			string errorCode = context.Request.QueryString[AccountQueryParameters.ErrorCode] ?? string.Empty;
			if (errorCode.Length > 0)
			{
				WriteCallbackPage(context.Response, false, "The provider did not confirm the sign-in.");
				return AccountOperationResult.Rejected(errorCode, "The provider did not confirm the sign-in.");
			}

			string token = context.Request.QueryString[AccountQueryParameters.SessionToken] ?? string.Empty;
			if (token.Length > 0)
			{
				AccountOperationResult validated = await ValidateTokenAsync(token).ConfigureAwait(false);
				if (!validated.Succeeded || validated.Session == null)
				{
					WriteCallbackPage(context.Response, false, "The account authority did not accept the new session.");
					return validated.Status == AccountOperationStatus.Unreachable ? validated : AccountOperationResult.Rejected(AccountErrorCodes.SessionInvalid, "The account authority did not accept the new session.");
				}

				SetSession(validated.Session);
				WriteCallbackPage(context.Response, true, "You can close this tab and return to the game.");
				return validated;
			}

			string ticket = context.Request.QueryString["registration_ticket"] ?? string.Empty;
			if (ticket.Length > 0)
			{
				WriteCallbackPage(context.Response, true, "Choose a display name in the game to finish registering.");
				return new AccountOperationResult
				{
					Status = AccountOperationStatus.Success,
					RegistrationTicket = ticket,
					Provider = context.Request.QueryString[AccountQueryParameters.Provider] ?? string.Empty,
					SuggestedDisplayName = context.Request.QueryString["suggested_display_name"] ?? string.Empty,
					SuggestedDisplayNameErrorCode = context.Request.QueryString["suggested_display_name_error"] ?? string.Empty
				};
			}

			WriteCallbackPage(context.Response, false, "The answer did not carry a session or a registration.");
			return AccountOperationResult.Rejected(AccountErrorCodes.RequestMalformed, "The sign-in answer carried nothing this build can use.");
		}
		finally
		{
			context.Response.Close();
		}
	}

	private async Task<(AccountOperationResult Result, TResponse? Body)> PostForJsonAsync<TRequest, TResponse>(string path, TRequest body, string? sessionToken)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, $"{AuthorityUrl}{path}");
			if (!string.IsNullOrEmpty(sessionToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
			request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

			using HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
			string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) return (RejectedOrUnreachable(response, json), default);

			TResponse? parsed = JsonSerializer.Deserialize<TResponse>(json, JsonOptions);
			return (parsed == null
				? AccountOperationResult.Rejected(AccountErrorCodes.RequestMalformed, "The account authority answered something this build cannot read.")
				: AccountOperationResult.Ok(null), parsed);
		}
		catch (HttpRequestException)
		{
			return (AccountOperationResult.Unreachable("The account authority could not be reached."), default);
		}
		catch (TaskCanceledException)
		{
			return (AccountOperationResult.Unreachable("The account authority did not answer in time."), default);
		}
		catch (JsonException)
		{
			return (AccountOperationResult.Rejected(AccountErrorCodes.RequestMalformed, "The account authority answered something this build cannot read."), default);
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
			if (!response.IsSuccessStatusCode) return RejectedOrUnreachable(response, json);

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
		else WriteStoredToken(session.Token);

		Changed?.Invoke();
	}

	private static string SessionFilePath => Path.Combine(OS.GetUserDataDir(), "auth", SessionFileName);

	private static string? ReadStoredToken()
	{
		try
		{
			string path = SessionFilePath;
			if (!File.Exists(path)) return null;

			StoredSession? stored = JsonSerializer.Deserialize<StoredSession>(File.ReadAllText(path), JsonOptions);
			return string.IsNullOrWhiteSpace(stored?.SessionToken) ? null : stored.SessionToken;
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

	private static void WriteStoredToken(string token)
	{
		try
		{
			string path = SessionFilePath;
			string? directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
			File.WriteAllText(path, JsonSerializer.Serialize(new StoredSession { SessionToken = token }));
		}
		catch (IOException exception)
		{
			GD.PrintErr($"[AccountService] Could not persist the session token: {exception.Message}");
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

	private bool TryOpenCallbackListener(out int port)
	{
		port = 0;
		CloseCallbackListener();

		int freePort = FindFreeLoopbackPort();
		if (freePort <= 0) return false;

		var listener = new HttpListener();
		listener.Prefixes.Add($"http://localhost:{freePort}/auth/callback/");
		try
		{
			listener.Start();
		}
		catch (HttpListenerException)
		{
			return false;
		}

		_callbackListener = listener;
		port = freePort;
		return true;
	}

	private static int FindFreeLoopbackPort()
	{
		try
		{
			var probe = new TcpListener(IPAddress.Loopback, 0);
			probe.Start();
			int port = ((IPEndPoint)probe.LocalEndpoint).Port;
			probe.Stop();
			return port;
		}
		catch (SocketException)
		{
			return 0;
		}
	}

	private void CloseCallbackListener()
	{
		HttpListener? listener = _callbackListener;
		_callbackListener = null;
		if (listener == null) return;

		try
		{
			listener.Stop();
			listener.Close();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private static void WriteCallbackPage(HttpListenerResponse response, bool succeeded, string message)
	{
		string title = succeeded ? "Realm" : "Realm sign-in";
		string html = $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>{title}</title></head>"
		              + "<body style=\"background:#0f111a;color:#e2e8f0;font-family:Segoe UI,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0\">"
		              + $"<div style=\"max-width:420px;text-align:center\"><h2 style=\"color:#ffd700\">{title}</h2><p>{message}</p></div></body></html>";

		byte[] buffer = Encoding.UTF8.GetBytes(html);
		response.ContentType = "text/html; charset=utf-8";
		response.ContentLength64 = buffer.Length;
		response.OutputStream.Write(buffer, 0, buffer.Length);
	}

	private class StoredSession
	{
		public string SessionToken { get; set; } = string.Empty;
	}

	private class SessionValidation
	{
		public string AccountId { get; set; } = string.Empty;

		public string DisplayName { get; set; } = string.Empty;

		public string[]? LinkedProviders { get; set; }

		public DateTime ExpiresAtUtc { get; set; } = DateTime.MaxValue;
	}
}

/// <summary>
///     The opaque value a flow is bound to. The authority mints the state it hands to the provider; the client only needs a
///     value of its own for the answer to echo, so that a callback for another request is refused.
/// </summary>
public static class AccountClientState
{
	public static string CreateOpaqueValue()
	{
		return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}
}
