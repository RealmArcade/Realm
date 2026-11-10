using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Maps;

namespace Realm.Client.Testing;


public partial class MultiplayerSimulationHarness : Node
{
	private async Task RunPauseScenarioAsync(ClientServerSession session)
	{
		string path = Path.Combine(_outputDirectory, "pause.txt");
		try
		{
			bool reachedPauseTime = await WaitUntilAsync(() => (GameContext.Host?.GameElapsedTime ?? 0f) >= _pauseAtSeconds, 180f);
			if (!reachedPauseTime)
			{
				File.WriteAllText(path, "paused=0\nreason=game time never reached the pause point\n");
				return;
			}

			int obstacles = session.LatestNavMeshObstacles?.Positions?.Length ?? -1;
			GameLog.LogToConsole($"[MP_HARNESS] This client holds {obstacles} obstacle volumes from the match.");

			bool requested = session.RequestPause(true);
			bool paused = await WaitUntilAsync(() => GameContext.NetworkService.IsPaused, 30f);
			GameLog.LogToConsole($"[MP_HARNESS] Pause requested={requested}; the match reports paused={paused} by player {GameContext.NetworkService.PausedByPlayerIndex}.");

			await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);

			bool resumeRequested = session.RequestPause(false);
			bool resumed = await WaitUntilAsync(() => !GameContext.NetworkService.IsPaused, 30f);
			GameLog.LogToConsole($"[MP_HARNESS] Resume requested={resumeRequested}; the match reports paused={GameContext.NetworkService.IsPaused}.");

			File.WriteAllText(
				path,
				$"paused={(paused ? 1 : 0)}\nresumed={(resumed ? 1 : 0)}\nobstacle_volumes={obstacles}\nrequested_by={GameContext.NetworkService.PausedByPlayerIndex}\n");
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MP_HARNESS] Pause scenario failed: {exception.Message}");
			try
			{
				File.WriteAllText(path, $"paused=0\nreason={exception.Message}\n");
			}
			catch
			{
			}
		}
	}

	private async Task RunSnapshotDiscardScenarioAsync()
	{
		string path = Path.Combine(_outputDirectory, "snapshot_discard.txt");
		try
		{
			bool reachedDiscardTime = await WaitUntilAsync(() => (GameContext.Host?.GameElapsedTime ?? 0f) >= _discardAtSeconds, 120f);
			if (!reachedDiscardTime)
			{
				File.WriteAllText(path, "discarded=0\nreason=game time never reached the discard point\n");
				return;
			}

			int appliedBefore = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
			int baselineBefore = GameContext.NetworkService.LastAppliedBaselineSequenceForDiagnostics;
			float discardGameTime = GameContext.Host?.GameElapsedTime ?? 0f;
			MultiplayerSyncDiagnostics.RequestSnapshotDiscard();

			bool discarded = await WaitUntilAsync(() => MultiplayerSyncDiagnostics.DiscardedSnapshotSequence >= 0, 30f);
			int discardedSequence = MultiplayerSyncDiagnostics.DiscardedSnapshotSequence;
			var baselinesAfter = new List<int>();
			if (discarded)
			{
				int lastBaseline = baselineBefore;
				ulong observationDeadline = Time.GetTicksMsec() + RepairObservationMilliseconds;
				while (Time.GetTicksMsec() < observationDeadline)
				{
					int currentBaseline = GameContext.NetworkService.LastAppliedBaselineSequenceForDiagnostics;
					if (currentBaseline > lastBaseline)
					{
						lastBaseline = currentBaseline;
						baselinesAfter.Add(currentBaseline);
					}

					await ToSignal(GetTree().CreateTimer(0.05f), SceneTreeTimer.SignalName.Timeout);
				}
			}

			File.WriteAllText(
				path,
				$"discarded={(discarded ? 1 : 0)}\ndiscarded_sequence={discardedSequence}\napplied_before={appliedBefore}\nbaseline_before={baselineBefore}\nbaseline_count_after={baselinesAfter.Count}\nbaselines_after={string.Join(',', baselinesAfter)}\ndiscard_game_time={discardGameTime.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");
			GameLog.LogToConsole($"[MP_HARNESS] Snapshot {discardedSequence} was discarded at game time {discardGameTime:F2}s; the authority described this peer from scratch {baselinesAfter.Count} time(s) afterwards.");
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MP_HARNESS] Snapshot discard scenario failed: {exception.Message}");
			try
			{
				File.WriteAllText(path, $"discarded=0\nreason={exception.Message}\n");
			}
			catch
			{
			}
		}
	}

	private const ulong RepairObservationMilliseconds = 4000;

	private async Task RunConnectionLossScenarioAsync(ClientServerSession session)
	{
		string path = Path.Combine(_outputDirectory, "client_drop.txt");
		try
		{
			bool reachedDropTime = await WaitUntilAsync(() => (GameContext.Host?.GameElapsedTime ?? 0f) >= _dropAtSeconds, 120f);
			if (!reachedDropTime)
			{
				File.WriteAllText(path, "dropped=0\nreason=game time never reached the drop point\n");
				return;
			}

			int dropSequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
			float dropGameTime = GameContext.Host?.GameElapsedTime ?? 0f;
			GameLog.LogToConsole($"[MP_HARNESS] Dropping the client connection at game time {dropGameTime:F2}s, applied sequence {dropSequence}.");
			session.DropConnectionForHarness();

			bool resumed = await WaitUntilAsync(
				() => GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics > dropSequence
					&& GameContext.NetworkService.LastAppliedBaselineSequenceForDiagnostics > dropSequence,
				60f);

			int resumeSequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
			int resumeBaselineSequence = GameContext.NetworkService.LastAppliedBaselineSequenceForDiagnostics;
			float resumeGameTime = GameContext.Host?.GameElapsedTime ?? 0f;
			File.WriteAllText(
				path,
				$"dropped=1\nresumed={(resumed ? 1 : 0)}\ndrop_sequence={dropSequence}\nresume_sequence={resumeSequence}\nresume_baseline_sequence={resumeBaselineSequence}\noutage_sequences={Math.Max(0, resumeSequence - dropSequence)}\ndrop_game_time={dropGameTime.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nresume_game_time={resumeGameTime.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");
			GameLog.LogToConsole($"[MP_HARNESS] Client connection loss scenario resumed={resumed} at sequence {resumeSequence} after {Math.Max(0, resumeSequence - dropSequence)} sequences.");
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MP_HARNESS] Connection loss scenario failed: {exception.Message}");
			try
			{
				File.WriteAllText(path, $"dropped=1\nresumed=0\nreason={exception.Message}\n");
			}
			catch
			{
			}
		}
	}

}
