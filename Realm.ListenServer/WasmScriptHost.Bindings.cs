using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Realm.MapAPI;
using Realm.Ecs;
using Realm.Ecs.Services;
using Wasmtime;
using Engine = Wasmtime.Engine;
using FileAccess = System.IO.FileAccess;

namespace Realm.ListenServer;

/// <summary>the partial binding hooks and the hand-written linker registrations.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	private partial void InitializeAutoBindings();
	private partial void InitializeAutoEvents();
	private partial void SubscribeAutoEvents(IGameAPI api);
	private partial void UnsubscribeAutoEvents();
	private partial void InitializeManualBindings();

	private const string LobbyImportModule = "custom:game/game-api";

	/// <summary>
	///     Binds the roster orders a lobby script places. They are hand written because the roster itself is published in
	///     shared memory the script reads directly, so only the orders it places cross into this process.
	/// </summary>
	private partial void InitializeManualBindings()
	{
		_linker.DefineFunction(LobbyImportModule, "add-lobby-faction", (Caller caller, int namePtr, int nameLength) =>
			LobbyApi?.AddFaction(ReadGuestString(caller, namePtr, nameLength)) == true ? 1 : 0);

		_linker.DefineFunction(LobbyImportModule, "set-lobby-maximum-player-count", (int maximumPlayerCount) =>
			LobbyApi?.SetMaximumPlayerCount(maximumPlayerCount));

		_linker.DefineFunction(LobbyImportModule, "set-lobby-team-count", (int teamCount) =>
			LobbyApi?.SetTeamCount(teamCount));

		_linker.DefineFunction(LobbyImportModule, "set-lobby-player-faction", (Caller caller, int playerIndex, int namePtr, int nameLength) =>
			LobbyApi?.SetPlayerFaction(playerIndex, ReadGuestString(caller, namePtr, nameLength)) == true ? 1 : 0);

		_linker.DefineFunction(LobbyImportModule, "set-lobby-player-team", (int playerIndex, int teamIndex) =>
			LobbyApi?.SetPlayerTeam(playerIndex, teamIndex) == true ? 1 : 0);

		_linker.DefineFunction(LobbyImportModule, "set-lobby-player-color", (int playerIndex, int colorRgba) =>
			LobbyApi?.SetPlayerColor(playerIndex, (uint)colorRgba) == true ? 1 : 0);

		_linker.DefineFunction(LobbyImportModule, "set-lobby-faction-locked", (int playerIndex, int locked) =>
			LobbyApi?.SetFactionLocked(playerIndex, locked != 0));

		_linker.DefineFunction(LobbyImportModule, "set-lobby-team-locked", (int playerIndex, int locked) =>
			LobbyApi?.SetTeamLocked(playerIndex, locked != 0));

		_linker.DefineFunction(LobbyImportModule, "set-lobby-color-locked", (int playerIndex, int locked) =>
			LobbyApi?.SetColorLocked(playerIndex, locked != 0));

		_linker.DefineFunction(LobbyImportModule, "set-lobby-teams-together-default", (int teamsTogether) =>
			LobbyApi?.SetTeamsTogetherDefault(teamsTogether != 0));

		_linker.DefineFunction(LobbyImportModule, "set-lobby-team-spawn-coordinate", (Caller caller, int teamIndex, int namePtr, int nameLength) =>
			LobbyApi?.SetTeamSpawnCoordinate(teamIndex, ReadGuestString(caller, namePtr, nameLength)) == true ? 1 : 0);
	}
}
