namespace Realm.Client.Services;

public enum SteamUnavailableReason
{
	Available,
	NotStarted,
	SteamClientNotRunning,
	NativeLibraryMissing,
	NativeLibraryMismatch,
	SteamRejectedThisProcess,
	InitializationFailed
}
