using System;
using Godot;
using Steamworks;

namespace Realm.Client.Services;

/// <summary>
///     Owns the one Steamworks context this process ever has. Every account feature works without it, so a build that is not
///     launched by Steam, a machine without the Steam client and a development run that has no native library all continue
///     with <see cref="IsAvailable" /> false instead of failing. While it is false, Steam is not offered as a sign-in provider at
///     all, because the only identity the browser hand-off could link is one this process cannot confirm it is playing on.
/// </summary>
public class SteamService
{
	private const string NativeLibraryName = "steam_api64";

	private bool _initialized;

	private bool _stopped;

	public bool IsAvailable => _initialized;

	public bool RestartRequested { get; private set; }

	public bool CanOpenStorePage => _initialized && SteamApplicationIds.FullGameAppId != 0;

	public void Start(bool developmentRun)
	{
		if (_initialized || RestartRequested) return;

		try
		{
			if (!developmentRun && SteamAPI.RestartAppIfNecessary(new AppId_t(SteamApplicationIds.DemoAppId)))
			{
				RestartRequested = true;
				return;
			}

			if (!SteamAPI.Init())
			{
				GD.Print("[Steam] the Steam client did not accept this process, so the game continues without Steamworks");
				return;
			}

			_initialized = true;
			GD.Print($"[Steam] running as application {SteamUtils.GetAppID().m_AppId}");
		}
		catch (Exception exception) when (IsMissingNativeLibrary(exception))
		{
			GD.Print($"[Steam] {NativeLibraryName} is not shipped with this build, so Steamworks stays off and Steam is not offered as a sign-in provider");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] Steamworks is not usable in this build: {exception.Message}");
		}
	}

	/// <summary>
	///     Whether this process failed because the native library is not there, which is an ordinary property of a build that does
	///     not ship Steamworks rather than something that went wrong. The check runs on the call itself instead of on a probe of its
	///     own, because only the loader that the P/Invoke uses knows where it looks.
	/// </summary>
	private static bool IsMissingNativeLibrary(Exception exception)
	{
		return exception is DllNotFoundException
		       || (exception is TypeInitializationException && exception.InnerException is DllNotFoundException);
	}

	public void Pump()
	{
		if (!_initialized || _stopped) return;

		try
		{
			SteamAPI.RunCallbacks();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] callbacks stopped: {exception.Message}");
			_initialized = false;
		}
	}

	public void Stop()
	{
		if (!_initialized || _stopped) return;

		_stopped = true;

		try
		{
			SteamAPI.Shutdown();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] shutdown failed: {exception.Message}");
		}

		_initialized = false;
	}

	public string GetPersonaName()
	{
		if (!_initialized) return string.Empty;

		try
		{
			return SteamFriends.GetPersonaName();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] the persona name could not be read: {exception.Message}");
			return string.Empty;
		}
	}

	public bool OpenFullGameStorePage()
	{
		if (!CanOpenStorePage) return false;

		try
		{
			SteamFriends.ActivateGameOverlayToStore(new AppId_t(SteamApplicationIds.FullGameAppId), EOverlayToStoreFlag.k_EOverlayToStoreFlag_None);
			return true;
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] the store overlay could not be opened: {exception.Message}");
			return false;
		}
	}
}
