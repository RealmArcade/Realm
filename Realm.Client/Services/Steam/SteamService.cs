using System;
using Godot;
using Steamworks;

namespace Realm.Client.Services;

/// <summary>
///     Owns the one Steamworks context this process ever has. Every account feature works without it, so a build that is not
///     launched by Steam, a machine without the Steam client and a development run that has no native library all continue
///     with <see cref="IsAvailable" /> false instead of failing.
/// </summary>
public class SteamService
{
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
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] Steamworks is not usable in this build: {exception.Message}");
		}
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
