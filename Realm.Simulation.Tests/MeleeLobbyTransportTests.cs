using MemoryPack;
using NUnit.Framework;
using Realm.Ecs;
using Realm.ListenServer;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Walks the path a host's match settings take before a match exists: the map declares them, the lobby admits the host,
///     the host's command is applied to the lobby's own state, a member's command is refused, the lobby republishes the
///     settings to every seat, and the authority that starts the match hands those settings to the map script that plays it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeLobbyTransportTests
{
	private readonly List<(int PeerId, NetworkFrameKind Kind, byte[] Payload)> _frames = new();

	private string _workDirectory = null!;
	private ServerLog _log = null!;
	private ServerLobby _lobby = null!;

	[SetUp]
	public void SetUp()
	{
		MeleeTestMaps.RequireMaps();

		_workDirectory = Path.Combine(Path.GetTempPath(), $"realm-lobby-transport-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_workDirectory);
		_log = new ServerLog(Path.Combine(_workDirectory, "authority.log"));

		_lobby = new ServerLobby(
			new ServerArguments(
				MapPath: MeleeTestMaps.BoardFolder,
				Port: 0,
				Token: string.Empty,
				LogPath: Path.Combine(_workDirectory, "authority.log"),
				DataPath: _workDirectory,
				Seed: 1,
				Lobby: true),
			_log,
			(peerId, kind, payload, delivery) => _frames.Add((peerId, kind, payload)),
			_ => { },
			(roster, slots, factions, seed) => null,
			() => { },
			(address, port) => { },
			_ => null,
			() => null);

		// The map's lobby script declares its own settings when it runs; this stands in for that, because the script is
		// started by the host rather than by the lobby a test builds directly.
		DeclareDraftCount();
		DeclareMirroredDraft();

		Assert.That(_lobby.AdmitPeer(1, "Host", 1001, string.Empty, steamId: 0, proveAdmin: true, out bool hostAdministers), Is.Empty, "The host has to be admitted.");
		Assert.That(hostAdministers, Is.True, "The peer that proved the ticket administers the lobby.");

		Assert.That(_lobby.AdmitPeer(2, "Member", 1002, string.Empty, steamId: 0, proveAdmin: false, out bool memberAdministers), Is.Empty, "A second peer has to be admitted.");
		Assert.That(memberAdministers, Is.False, "A peer that proved no ticket is a member rather than an administrator.");
	}

	[TearDown]
	public void TearDown()
	{
		_log.Dispose();

		try
		{
			Directory.Delete(_workDirectory, recursive: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}

	private void DeclareDraftCount()
	{
		_lobby.DeclareMatchSetting(MeleeLobbySettings.DraftOptions, LobbySettingControl.Count, "Buildings per draft", "How many buildings a draw offers.", 3);
		_lobby.DeclareMatchSettingRange(MeleeLobbySettings.DraftOptions, 1, 6);
	}

	private void DeclareMirroredDraft()
	{
		_lobby.DeclareMatchSetting(MeleeLobbySettings.MirroredDraft, LobbySettingControl.Toggle, "Mirrored draft", "Whether every player is offered the same buildings.", 0);
	}

	/// <summary>
	///     The Steam id a seat states travels to every other seat, because that is the only thing the end-of-match table has
	///     to name when a player asks Steam to add somebody they just played with. It is published like the name it plays
	///     under, and unlike the seat secret it is not blanked for the peers that do not hold the seat.
	/// </summary>
	[Test]
	public void ASeatsStatedSteamIdIsPublishedToEverySeat()
	{
		const ulong steamId = 76561198000000001;

		Assert.That(_lobby.AdmitPeer(3, "Friend", 1003, string.Empty, steamId, proveAdmin: false, out _), Is.Empty, "The third peer has to be admitted.");

		_frames.Clear();
		_lobby.BroadcastState();

		foreach (int receiverPeerId in new[] { 1, 2, 3 })
		{
			LobbyStateMessage? published = LatestStateSentTo(receiverPeerId);
			Assert.That(published, Is.Not.Null, $"peer {receiverPeerId} has to be told the seats");

			LobbySeat? friendSeat = null;
			foreach (LobbySeat seat in published!.Value.Seats)
			{
				if (seat.PeerId == 3) friendSeat = seat;
			}

			Assert.That(friendSeat, Is.Not.Null, $"peer {receiverPeerId} has to be told the third seat");
			Assert.That(friendSeat!.Value.SteamId, Is.EqualTo(steamId), $"peer {receiverPeerId} has to read the Steam id the third seat stated");
		}
	}

	private LobbyStateMessage? LatestStateSentTo(int peerId)
	{
		LobbyStateMessage? state = null;
		foreach ((int framePeerId, NetworkFrameKind kind, byte[] payload) in _frames)
		{
			if (framePeerId != peerId || kind != NetworkFrameKind.LobbyState) continue;
			state = MemoryPackSerializer.Deserialize<LobbyStateMessage>(payload);
		}

		return state;
	}

	[Test]
	public void TheHostsSettingLandsInTheAuthorityThatWillPlayIt()
	{
		_lobby.HandleCommand(1, SetDraftCount(1));

		Assert.That(
			_lobby.MatchSettingValues.Get(MeleeLobbySettings.DraftOptions),
			Is.EqualTo(1),
			"The command a host sends is applied to the lobby's own state, which is what the match is built from.");
	}

	[Test]
	public void AMembersSettingIsRefused()
	{
		_lobby.HandleCommand(1, SetDraftCount(2));
		_lobby.HandleCommand(2, SetDraftCount(6));

		Assert.That(
			_lobby.MatchSettingValues.Get(MeleeLobbySettings.DraftOptions),
			Is.EqualTo(2),
			"A member may not change the settings the host stated.");
	}

	[Test]
	public void ASettingNoMatchCouldPlayIsDroppedRatherThanClamped()
	{
		_lobby.HandleCommand(1, SetDraftCount(25));

		Assert.That(
			_lobby.MatchSettingValues.Get(MeleeLobbySettings.DraftOptions),
			Is.EqualTo(3),
			"A value past the range the map stated leaves the setting at the map's own default rather than being clamped into a number the host never asked for.");
	}

	[Test]
	public void ASettingTheMapNeverDeclaredIsRefused()
	{
		_lobby.HandleCommand(1, new LobbyCommand { Kind = LobbyCommandKind.SetLobbySetting, SettingId = 99, SettingValue = 1 });

		Assert.That(_lobby.MatchSettings.ContainsKey(99), Is.False, "A lobby only holds the settings its own map declared.");
	}

	[Test]
	public void EverySeatIsToldTheSettingsTheHostStated()
	{
		_lobby.HandleCommand(1, SetDraftCount(5));
		_lobby.HandleCommand(1, new LobbyCommand { Kind = LobbyCommandKind.SetShroudMode, Progress = (int)MatchShroudMode.None });

		_frames.Clear();
		_lobby.SendStateTo(2);

		(int PeerId, NetworkFrameKind Kind, byte[] Payload) frame = _frames.Single(entry => entry.Kind == NetworkFrameKind.LobbyState);
		Assert.That(frame.PeerId, Is.EqualTo(2), "The state goes to the seat that asked for it.");

		LobbyStateMessage state = MemoryPackSerializer.Deserialize<LobbyStateMessage>(frame.Payload);
		Assert.That(
			state.Settings,
			Has.Some.Matches<LobbyMatchSettingMessage>(setting => setting.SettingId == MeleeLobbySettings.DraftOptions && setting.Value == 5),
			"A seat reads the draft size the host stated, which is how it shows the setting back to the host.");
		Assert.That(
			state.Settings,
			Has.Some.Matches<LobbyMatchSettingMessage>(setting => setting.SettingId == MeleeLobbySettings.MirroredDraft && setting.Control == (int)LobbySettingControl.Toggle),
			"A setting the map declared as a toggle travels with the control it is edited by, so the panel draws it as a checkbox.");
		Assert.That(state.ShroudMode, Is.EqualTo((int)MatchShroudMode.None), "A seat reads the shroud mode the host stated.");
	}

	[Test]
	public void TheHostClosesAndReopensAPlayerSlot()
	{
		_frames.Clear();
		_lobby.HandleCommand(1, new LobbyCommand { Kind = LobbyCommandKind.SetSlotOpen, Slot = 5, Flag = false });

		Assert.That(_lobby.IsSlotOpen(5), Is.False, "The host closes a player slot directly, with no player count to set.");

		LobbyStateMessage? published = LatestStateSentTo(2);
		Assert.That(published, Is.Not.Null, "The seats are told the slot closed.");
		Assert.That(published!.Value.OpenSlots[5], Is.False, "A closed slot keeps its row and stops accepting a player.");

		_lobby.HandleCommand(1, new LobbyCommand { Kind = LobbyCommandKind.SetSlotOpen, Slot = 5, Flag = true });
		Assert.That(_lobby.IsSlotOpen(5), Is.True, "The host reopens the slot it closed.");
	}

	[Test]
	public void TheSettingsTheLobbyHoldsAreTheOnesTheScriptPlays()
	{
		_lobby.HandleCommand(1, SetDraftCount(1));

		using MeleeMatch match = MeleeMatch.StartDuel(lobbySettings: _lobby.MatchSettingValues, shroudMode: MatchShroudMode.None);

		Assert.That(match.Api.GetLobbySetting(MeleeLobbySettings.DraftOptions), Is.EqualTo(1), "The script reads the draft size the host stated.");
		Assert.That(match.Simulation.Map.Simulation.ShroudDisabled, Is.True, "The host's shroud mode stands over the one the map states.");

		DraftOffer offer = match.BeginConstruction(1, new System.Numerics.Vector3(-50f, 0f, -50f));
		Assert.That(offer.AbilityIds.Length, Is.EqualTo(1), "The draw the host configured offers exactly one building.");
	}

	private static LobbyCommand SetDraftCount(int value)
	{
		return new LobbyCommand
		{
			Kind = LobbyCommandKind.SetLobbySetting,
			SettingId = MeleeLobbySettings.DraftOptions,
			SettingValue = value
		};
	}
}
