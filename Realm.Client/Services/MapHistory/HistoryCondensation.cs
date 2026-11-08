using System;
using System.Collections.Generic;
using LibGit2Sharp;
using Realm.Client.Core;

namespace Realm.Client.Services.MapHistory;

/// <summary>
///     Making the stored history less granular. A plan keeps the pinned versions and, for everything else,
///     the newest commit of each tree plus the first commit inside each age bracket, and the rewrite then
///     re-parents the kept commits into one chain and re-applies the labels that pointed at them.
/// </summary>
internal static class HistoryCondensation
{
	internal static void TrimHistoryAfterCommit(MapHistoryStoreSession session)
	{
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		MapHistoryService.MapHistoryStateFile state = MapHistoryService.LoadState(session.Layout);
		if (state.LastCondenseAt.HasValue && DateTimeOffset.Now - state.LastCondenseAt.Value < MapHistoryPolicy.MinimumTimeBetweenCondenses) return;

		MapHistoryCondensePlan plan = BuildCondensePlan(repository, state);
		if (plan.DroppedCount < MapHistoryPolicy.MinimumDroppedVersionsPerCondense)
		{
			state.LastCondenseAt = DateTimeOffset.Now;
			MapHistoryService.SaveState(session.Layout, state);
			return;
		}

		MapHistoryCleanupResult cleanup = MakeHistoryLessGranularInStore(session, repository, state, plan);
		if (cleanup.DroppedAnything)
		{
			GameLog.LogToConsole($"[MapHistoryService] Condensed {cleanup.DroppedVersionCount} older version(s) while saving.");
		}
	}

	internal static MapHistoryCleanupResult CondenseHistoryInStore(MapHistoryStoreSession session)
	{
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		MapHistoryService.MapHistoryStateFile state = MapHistoryService.LoadState(session.Layout);
		MapHistoryCondensePlan plan = BuildCondensePlan(repository, state);
		return MakeHistoryLessGranularInStore(session, repository, state, plan);
	}

	internal static MapHistoryCondensePlan BuildCondensePlan(MapGitRepository repository, MapHistoryService.MapHistoryStateFile state)
	{
		using Repository versionScope = repository.OpenRepositoryScope();
		IReadOnlyList<MapGitCommitInfo> commitsNewestFirst = repository.ReadVersionHistory(int.MaxValue);
		var entryByCommitId = new Dictionary<string, MapHistoryService.MapHistoryStateEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (MapHistoryService.MapHistoryStateEntry stateEntry in state.Entries)
		{
			entryByCommitId[stateEntry.CommitId] = stateEntry;
		}

		var slotsNewestFirst = new List<MapVersionSlot>(commitsNewestFirst.Count);
		for (int rank = 0; rank < commitsNewestFirst.Count; rank++)
		{
			MapGitCommitInfo commit = commitsNewestFirst[rank];
			entryByCommitId.TryGetValue(commit.Id, out MapHistoryService.MapHistoryStateEntry? entry);
			string? noteLabel = repository.ReadVersionLabel(commit.Id, versionScope);
			string? label = entry?.Label ?? noteLabel;
			long saveVersion = entry != null && entry.SaveVersion > 0 ? entry.SaveVersion : commitsNewestFirst.Count - rank;
			slotsNewestFirst.Add(new MapVersionSlot(commit, entry, label, saveVersion, !string.IsNullOrWhiteSpace(noteLabel)));
		}

		var keptNewestFirst = new List<MapVersionSlot>(slotsNewestFirst.Count);
		var keptTreeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		DateTimeOffset now = DateTimeOffset.Now;
		foreach (MapVersionSlot slot in slotsNewestFirst)
		{
			bool isPinned = IsPinnedVersion(slot);
			if (keptNewestFirst.Count > 0)
			{
				if (!isPinned && keptTreeIds.Contains(slot.Commit.TreeId)) continue;

				if (!isPinned)
				{
					MapVersionSlot newestKeptSlot = keptNewestFirst[^1];
					TimeSpan age = now - slot.Commit.CommittedAt;
					TimeSpan granularity = MapHistoryPolicy.GetGranularity(age);
					if (newestKeptSlot.Commit.CommittedAt - slot.Commit.CommittedAt < granularity) continue;
				}
			}

			keptNewestFirst.Add(slot);
			keptTreeIds.Add(slot.Commit.TreeId);
		}

		keptNewestFirst.Reverse();
		return new MapHistoryCondensePlan(keptNewestFirst, slotsNewestFirst.Count - keptNewestFirst.Count);
	}

	internal static bool IsPinnedVersion(MapVersionSlot slot)
	{
		if (slot.HasManualLabel) return true;
		if (slot.Entry?.Kind == MapVersionKind.RestoredVersion) return false;

		return !string.IsNullOrWhiteSpace(slot.Label) || slot.Entry?.Kind is MapVersionKind.BeforeRestore or MapVersionKind.BeforeNewMap;
	}

	internal static MapHistoryCleanupResult MakeHistoryLessGranularInStore(MapHistoryStoreSession session, MapGitRepository repository, MapHistoryService.MapHistoryStateFile state, MapHistoryCondensePlan plan)
	{
		if (plan.DroppedCount == 0)
		{
			return new MapHistoryCleanupResult();
		}

		var rewrittenEntries = new List<MapHistoryService.MapHistoryStateEntry>(plan.KeptOldestFirst.Count);
		var labelsToWrite = new List<(string CommitId, string Label)>();
		string? parentCommitId = null;

		foreach (MapVersionSlot slot in plan.KeptOldestFirst)
		{
			string commitId = slot.Commit.Id;
			if (!string.Equals(slot.Commit.ParentId, parentCommitId, StringComparison.OrdinalIgnoreCase))
			{
				string message = !string.IsNullOrWhiteSpace(slot.Label) ? slot.Label : slot.Commit.Message;
				var signature = new MapGitSignature(MapHistoryService.AuthorName, MapHistoryService.AuthorEmail, slot.Commit.CommittedAt);
				commitId = repository.WriteCommitForTree(slot.Commit.TreeId, parentCommitId, signature, message);
			}

			rewrittenEntries.Add(new MapHistoryService.MapHistoryStateEntry
			{
				CommitId = commitId,
				CreatedAt = slot.Commit.CommittedAt,
				Kind = slot.Entry?.Kind ?? MapVersionKind.ExternalChange,
				Label = slot.Label,
				SaveVersion = slot.SaveVersion,
				ChangedFileCount = slot.Entry?.ChangedFileCount ?? 0,
				SourceFolder = slot.Entry?.SourceFolder
			});

			if (!string.IsNullOrWhiteSpace(slot.Label))
			{
				labelsToWrite.Add((commitId, slot.Label));
			}

			parentCommitId = commitId;
		}

		repository.UpdateHead(parentCommitId ?? string.Empty);
		repository.ClearVersionLabels();
		var labelSignature = new MapGitSignature(MapHistoryService.AuthorName, MapHistoryService.AuthorEmail, DateTimeOffset.Now);
		foreach (var (labelCommitId, label) in labelsToWrite)
		{
			repository.WriteVersionLabel(labelCommitId, labelSignature, label);
		}

		repository.SweepUnreachableObjects();

		state.Entries = rewrittenEntries;
		state.LastCondenseAt = DateTimeOffset.Now;
		MapHistoryService.SaveState(session.Layout, state);

		return new MapHistoryCleanupResult
		{
			DroppedVersionCount = plan.DroppedCount
		};
	}

	internal readonly record struct MapVersionSlot(MapGitCommitInfo Commit, MapHistoryService.MapHistoryStateEntry? Entry, string? Label, long SaveVersion, bool HasManualLabel);

	internal sealed class MapHistoryCondensePlan
	{
		public MapHistoryCondensePlan(List<MapVersionSlot> keptOldestFirst, int droppedCount)
		{
			KeptOldestFirst = keptOldestFirst;
			DroppedCount = droppedCount;
		}

		public List<MapVersionSlot> KeptOldestFirst { get; }
		public int DroppedCount { get; }
	}
}
