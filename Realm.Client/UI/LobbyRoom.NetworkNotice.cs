using System;
using Godot;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

public partial class LobbyRoom
{
	private string _postedNetworkNotice = string.Empty;

	private bool _publicationRefused;

	private void PostNetworkNotice()
	{
		if (!Lobby.IsHost || Lobby.IsSinglePlayer) return;
		if (_chatLog == null || !IsInstanceValid(_chatLog)) return;
		if (Lobby.ActiveLobbyId is { Length: > 0 } && !Lobby.IsUnlisted) return;

		bool measuredFailure = _publicationRefused || Lobby.LastHostFailure is { Failure: not HostReachabilityFailure.None };
		bool playerReported = !string.IsNullOrWhiteSpace(Lobby.LastPlayerJoinFailure);
		if (!measuredFailure && !playerReported) return;

		string line = RestrictiveNetworkDialog.Text();
		if (string.Equals(line, _postedNetworkNotice, StringComparison.Ordinal)) return;
		_postedNetworkNotice = line;

		_chatLog.Text += $"[color=#ffd700]{TranslationServer.Translate("System")}[/color]: {line}\n";
	}

	private void RefreshNetworkNotice()
	{
		PostNetworkNotice();
	}
}
