using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Shared.Steam;
using Steamworks;

namespace Realm.Client.Services.Steam;

/// <summary>
///     One Workshop item as Steam describes it, plus the identity Realm writes into its metadata.
/// </summary>
/// <param name="PublishedFileId">The Workshop item id, which is what a map is published, subscribed and downloaded by.</param>
/// <param name="Title">The item's title.</param>
/// <param name="Description">The item's description.</param>
/// <param name="AuthorSteamId">The SteamID64 of the account that owns the item.</param>
/// <param name="Identity">What the item states about the map it carries.</param>
/// <param name="FileSizeBytes">How large the item is.</param>
/// <param name="VotesUp">Up-votes Steam has recorded.</param>
/// <param name="VotesDown">Down-votes Steam has recorded.</param>
/// <param name="TimeCreated">When the item was first published, in Unix seconds.</param>
/// <param name="TimeUpdated">When the item was last updated, in Unix seconds.</param>
/// <param name="IsBanned">Whether Steam has removed the item from view.</param>
/// <param name="PreviewUrl">Where Steam hosts the item's thumbnail, or an empty string when it has none.</param>
public readonly record struct WorkshopMapEntry(
	ulong PublishedFileId,
	string Title,
	string Description,
	ulong AuthorSteamId,
	WorkshopMapMetadata Identity,
	int FileSizeBytes,
	uint VotesUp,
	uint VotesDown,
	uint TimeCreated,
	uint TimeUpdated,
	bool IsBanned,
	string PreviewUrl);

/// <summary>
///     Where a publish has reached, so the editor can say what Steam is doing.
/// </summary>
/// <param name="Fraction">How much of the upload Steam has taken, from 0 to 1.</param>
/// <param name="Stage">Which step the upload is on.</param>
public readonly record struct WorkshopPublishProgress(float Fraction, string Stage);

/// <summary>
///     What a publish ended as. A failure carries the reason the creator is shown, and whether Steam is waiting for them
///     to accept the Workshop legal agreement before the item can be created at all.
/// </summary>
/// <param name="Success">Whether the item now carries the uploaded content.</param>
/// <param name="PublishedFileId">The item the content was published to, or zero when nothing was created.</param>
/// <param name="FailureReason">Why the publish did not finish, empty on success.</param>
/// <param name="NeedsLegalAgreement">Whether Steam requires the creator to accept the Workshop agreement first.</param>
public readonly record struct WorkshopPublishResult(bool Success, ulong PublishedFileId, string FailureReason, bool NeedsLegalAgreement);

/// <summary>
///     The map Workshop, which is Steam's. It publishes a map folder as an item, finds items by their stated identity,
///     subscribes to them and reports where Steam installed them. Nothing here talks to a Realm server: an item is the
///     publication, a subscription is the download and Steam's vote and report surfaces are the rating and moderation.
/// </summary>
public static class SteamWorkshopService
{
	/// <summary>The key-value tag that carries the map's folder name, so a lookup does not depend on titles.</summary>
	public const string MapNameTagKey = "mapName";

	/// <summary>The key-value tag that states which game build the map was authored against.</summary>
	public const string GameBuildTagKey = "build";

	/// <summary>The tag that marks an item as playable content rather than the editor's shared art pack.</summary>
	public const string MapTag = "map";

	/// <summary>The tag that marks the editor's shared art-pack item.</summary>
	public const string AssetPackTag = "asset-pack";

	/// <summary>
	///     The key-value tag a map carries once enough different people have played it for its author to publish it publicly.
	///     It is self-declared, like everything else about a public release, so it is a hint for ordering and display and
	///     never a trust or security signal: the Verified mark is the one a creator cannot write.
	/// </summary>
	public const string GreenlitTagKey = "greenlit";

	/// <summary>The value the <see cref="GreenlitTagKey" /> tag carries.</summary>
	public const string GreenlitTagValue = "1";

	/// <summary>The most items one query page can return.</summary>
	public const uint ResultsPerPage = 50;

	/// <summary>The number of pages a browse reads, which is what a player can scroll through in one listing.</summary>
	public const uint BrowsePageCount = 4;

	private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(60);

	private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

	/// <summary>How much of an item's metadata Steam is asked to hand back, which is far more than an identity needs.</summary>
	private const uint MaximumMetadataBytes = 8192;

	/// <summary>How much of a thumbnail's address Steam is asked to hand back.</summary>
	private const uint MaximumPreviewUrlBytes = 2048;

	/// <summary>Whether Steamworks is running, which is the only state in which Workshop items can be read or written.</summary>
	public static bool IsAvailable => GameContext.Steam.IsAvailable;

	/// <summary>The app whose Workshop is being read and written.</summary>
	private static AppId_t ConsumerAppId => new(GameContext.Steam.AppId);

	/// <summary>
	///     Publishes a prepared content folder as a Workshop item, creating the item the first time and updating it after
	///     that. The caller stages the folder, because what a map ships is the map's own packaging decision.
	/// </summary>
	/// <param name="contentFolder">The folder to upload, which must already be the canonical copy of the map.</param>
	/// <param name="previewImagePath">The thumbnail Steam shows in the browser, or null to leave the current one.</param>
	/// <param name="title">The item's title.</param>
	/// <param name="description">The item's description.</param>
	/// <param name="identity">What the item states about the map it carries.</param>
	/// <param name="visibility">Who Steam shows the item to.</param>
	/// <param name="existingPublishedFileId">The item to update, or zero to create one.</param>
	/// <param name="changeNote">
	///     What changed, which Steam records against the update. Steam's own Revert to this version is chosen by these notes,
	///     so a note that says which build and which package the upload was made from is what makes a rollback possible
	///     without any client code.
	/// </param>
	/// <param name="greenlit">Whether enough different people have played the map for this publish to be public.</param>
	/// <param name="progressCallback">Where the upload has reached.</param>
	/// <param name="cancellationToken">Gives up the upload.</param>
	/// <returns>What the publish ended as.</returns>
	public static async Task<WorkshopPublishResult> PublishAsync(
		string contentFolder,
		string? previewImagePath,
		string title,
		string description,
		WorkshopMapMetadata identity,
		ERemoteStoragePublishedFileVisibility visibility,
		ulong existingPublishedFileId,
		string changeNote,
		bool greenlit,
		Action<WorkshopPublishProgress>? progressCallback,
		CancellationToken cancellationToken)
	{
		if (!IsAvailable) return Failed(GameContext.Steam.UnavailableExplanation);
		if (!Directory.Exists(contentFolder)) return Failed($"The folder to publish does not exist: {contentFolder}");

		ulong publishedFileId = existingPublishedFileId;
		if (publishedFileId == 0)
		{
			progressCallback?.Invoke(new WorkshopPublishProgress(0f, "Creating the Workshop item..."));
			CreateItemResult_t created = await CreateItemAsync(cancellationToken);
			if (created.m_eResult != EResult.k_EResultOK)
			{
				return new WorkshopPublishResult(false, 0, DescribeResult(created.m_eResult), created.m_bUserNeedsToAcceptWorkshopLegalAgreement);
			}

			if (created.m_bUserNeedsToAcceptWorkshopLegalAgreement)
			{
				SteamUGC.ShowWorkshopEULA();
				return new WorkshopPublishResult(false, created.m_nPublishedFileId.m_PublishedFileId, "Steam requires you to accept the Workshop legal agreement before this map can be published.", true);
			}

			publishedFileId = created.m_nPublishedFileId.m_PublishedFileId;
		}

		UGCUpdateHandle_t updateHandle = SteamUGC.StartItemUpdate(ConsumerAppId, new PublishedFileId_t(publishedFileId));
		if (updateHandle == UGCUpdateHandle_t.Invalid)
		{
			return new WorkshopPublishResult(false, publishedFileId, "Steam refused to start an update for this item.", false);
		}

		SteamUGC.SetItemTitle(updateHandle, title);
		SteamUGC.SetItemDescription(updateHandle, description);
		SteamUGC.SetItemContent(updateHandle, contentFolder);
		SteamUGC.SetItemMetadata(updateHandle, identity.ToJson());
		SteamUGC.SetItemVisibility(updateHandle, visibility);

		if (!string.IsNullOrEmpty(previewImagePath) && File.Exists(previewImagePath))
		{
			SteamUGC.SetItemPreview(updateHandle, previewImagePath);
		}

		var tags = new List<string> { identity.IsAssetPack ? AssetPackTag : MapTag };
		SteamUGC.SetItemTags(updateHandle, tags, false);
		SteamUGC.AddItemKeyValueTag(updateHandle, MapNameTagKey, identity.MapName);
		if (!string.IsNullOrEmpty(identity.GameBuildNumber)) SteamUGC.AddItemKeyValueTag(updateHandle, GameBuildTagKey, identity.GameBuildNumber);
		if (greenlit) SteamUGC.AddItemKeyValueTag(updateHandle, GreenlitTagKey, GreenlitTagValue);

		SubmitItemUpdateResult_t submitted = await SubmitItemUpdateAsync(updateHandle, changeNote, progressCallback, cancellationToken);
		if (submitted.m_eResult != EResult.k_EResultOK)
		{
			return new WorkshopPublishResult(false, publishedFileId, DescribeResult(submitted.m_eResult), submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement);
		}

		return new WorkshopPublishResult(true, publishedFileId, string.Empty, false);
	}

	/// <summary>
	///     Reads one item, so a publisher can tell whether the item a map remembers still exists and what it states.
	/// </summary>
	/// <param name="publishedFileId">The item to read.</param>
	/// <param name="cancellationToken">Gives up the query.</param>
	/// <returns>The item, or null when Steam could not answer or the item is gone.</returns>
	public static async Task<WorkshopMapEntry?> GetItemAsync(ulong publishedFileId, CancellationToken cancellationToken)
	{
		if (!IsAvailable || publishedFileId == 0) return null;

		var ids = new[] { new PublishedFileId_t(publishedFileId) };
		UGCQueryHandle_t handle = SteamUGC.CreateQueryUGCDetailsRequest(ids, 1);
		if (handle == UGCQueryHandle_t.Invalid) return null;

		var entries = await RunQueryAsync(handle, cancellationToken);
		return entries.Count > 0 ? entries[0] : null;
	}

	/// <summary>
	///     Reads every item the Workshop offers for this app, newest first, which is what the map library browses.
	/// </summary>
	/// <param name="searchText">Text to match against titles and descriptions, or null for everything.</param>
	/// <param name="pageCount">How many pages to read.</param>
	/// <param name="cancellationToken">Gives up the query.</param>
	/// <returns>The items Steam returned, in the order it returned them.</returns>
	public static async Task<IReadOnlyList<WorkshopMapEntry>> BrowseAsync(string? searchText, uint pageCount, CancellationToken cancellationToken)
	{
		if (!IsAvailable) return Array.Empty<WorkshopMapEntry>();

		var entries = new List<WorkshopMapEntry>();
		for (uint page = 1; page <= Math.Max(1, pageCount); page++)
		{
			UGCQueryHandle_t handle = SteamUGC.CreateQueryAllUGCRequest(
				searchText == null ? EUGCQuery.k_EUGCQuery_RankedByPublicationDate : EUGCQuery.k_EUGCQuery_RankedByTextSearch,
				EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items,
				ConsumerAppId,
				ConsumerAppId,
				page);

			if (handle == UGCQueryHandle_t.Invalid) break;

			if (!string.IsNullOrWhiteSpace(searchText)) SteamUGC.SetSearchText(handle, searchText);

			var pageEntries = await RunQueryAsync(handle, cancellationToken);
			entries.AddRange(pageEntries);

			if (pageEntries.Count < ResultsPerPage) break;
		}

		return entries;
	}

	/// <summary>
	///     Reads the items one creator published for this game, newest first, which is the strip the creator panel shows.
	///     The query names the running AppID, because a SteamID is global and an unfiltered query would offer the creator's
	///     items for other games as if they were maps here.
	/// </summary>
	/// <param name="creatorSteamId">The creator's SteamID64, as the Workshop item states its owner.</param>
	/// <param name="maximumCount">How many items to show at most.</param>
	/// <param name="cancellationToken">Gives up the query.</param>
	/// <returns>The creator's items for this game, newest first.</returns>
	public static async Task<IReadOnlyList<WorkshopMapEntry>> BrowseByAuthorAsync(ulong creatorSteamId, uint maximumCount, CancellationToken cancellationToken)
	{
		if (!IsAvailable || creatorSteamId == 0) return Array.Empty<WorkshopMapEntry>();

		UGCQueryHandle_t handle = SteamUGC.CreateQueryUserUGCRequest(
			new AccountID_t((uint)(creatorSteamId & 0xFFFFFFFF)),
			EUserUGCList.k_EUserUGCList_Published,
			EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items,
			EUserUGCListSortOrder.k_EUserUGCListSortOrder_CreationOrderDesc,
			ConsumerAppId,
			ConsumerAppId,
			1);

		if (handle == UGCQueryHandle_t.Invalid) return Array.Empty<WorkshopMapEntry>();

		SteamUGC.SetReturnTotalOnly(handle, false);
		IReadOnlyList<WorkshopMapEntry> entries = await RunQueryAsync(handle, cancellationToken);
		return entries
			.Where(entry => !entry.IsBanned)
			.Take((int)Math.Max(1, maximumCount))
			.ToList();
	}

	/// <summary>
	///     Finds the newest item that states a given map folder name. This is how a lobby names a map and the screen
	///     turns that name into a subscription, without either of them trusting a title.
	/// </summary>
	/// <param name="mapName">The map's folder name.</param>
	/// <param name="cancellationToken">Gives up the query.</param>
	/// <returns>The newest matching item, or null when nothing published states that name.</returns>
	public static async Task<WorkshopMapEntry?> FindByMapNameAsync(string mapName, CancellationToken cancellationToken)
	{
		if (!IsAvailable || string.IsNullOrWhiteSpace(mapName)) return null;

		string normalizedName = NormalizeMapName(mapName);
		var entries = await BrowseAsync(mapName, BrowsePageCount, cancellationToken);

		return entries
			.Where(entry => !entry.IsBanned)
			.Where(entry => MatchesMapName(entry, normalizedName))
			.OrderByDescending(entry => entry.TimeUpdated)
			.FirstOrDefault();
	}

	/// <summary>
	///     Finds the newest item a given account published for a map, which is the item a re-publish updates. A map name is
	///     not reserved by Steam: two creators can publish maps whose names match, and an update can only be aimed at an
	///     item the caller owns, so the search is limited to that account's own items.
	/// </summary>
	/// <param name="mapName">The map's folder name.</param>
	/// <param name="authorSteamId">The SteamID64 that must own the item, or zero when this screen has no Steam identity.</param>
	/// <param name="cancellationToken">Gives up the query.</param>
	/// <returns>The newest matching item this account owns, or null when it has published none.</returns>
	public static async Task<WorkshopMapEntry?> FindOwnedByMapNameAsync(string mapName, ulong authorSteamId, CancellationToken cancellationToken)
	{
		if (!IsAvailable || authorSteamId == 0 || string.IsNullOrWhiteSpace(mapName)) return null;

		string normalizedName = NormalizeMapName(mapName);
		var entries = await BrowseAsync(mapName, BrowsePageCount, cancellationToken);

		return entries
			.Where(entry => !entry.IsBanned)
			.Where(entry => entry.AuthorSteamId == authorSteamId)
			.Where(entry => MatchesMapName(entry, normalizedName))
			.OrderByDescending(entry => entry.TimeUpdated)
			.FirstOrDefault();
	}

	/// <summary>
	///     Finds every item this account has subscribed to whose map states a given folder name, which is what a screen
	///     checks when it already holds a map and needs the item id back for a re-publish.
	/// </summary>
	/// <param name="mapName">The map's folder name.</param>
	/// <param name="cancellationToken">Gives up the query.</param>
	/// <returns>The newest subscribed item stating that name, or null when none does.</returns>
	public static async Task<WorkshopMapEntry?> FindSubscribedByMapNameAsync(string mapName, CancellationToken cancellationToken)
	{
		if (!IsAvailable || string.IsNullOrWhiteSpace(mapName)) return null;

		string normalizedName = NormalizeMapName(mapName);
		ulong[] subscribedIds = GetSubscribedItemIds();
		if (subscribedIds.Length == 0) return null;

		var entries = new List<WorkshopMapEntry>();
		foreach (ulong[] batch in Batch(subscribedIds, ResultsPerPage))
		{
			UGCQueryHandle_t handle = SteamUGC.CreateQueryUGCDetailsRequest(batch.Select(id => new PublishedFileId_t(id)).ToArray(), (uint)batch.Length);
			if (handle == UGCQueryHandle_t.Invalid) continue;
			entries.AddRange(await RunQueryAsync(handle, cancellationToken));
		}

		return entries
			.Where(entry => MatchesMapName(entry, normalizedName))
			.OrderByDescending(entry => entry.TimeUpdated)
			.FirstOrDefault();
	}

	/// <summary>
	///     Subscribes to an item and waits for Steam to finish downloading and installing it, reporting how far it has got.
	/// </summary>
	/// <param name="publishedFileId">The item to install.</param>
	/// <param name="progressCallback">How much of the download Steam has taken, from 0 to 1.</param>
	/// <param name="cancellationToken">Gives up waiting, which leaves Steam downloading in the background.</param>
	/// <returns>Whether the item is installed on this machine afterwards.</returns>
	public static async Task<bool> InstallAsync(ulong publishedFileId, Action<float>? progressCallback, CancellationToken cancellationToken)
	{
		if (!IsAvailable || publishedFileId == 0) return false;

		var id = new PublishedFileId_t(publishedFileId);
		if (!IsInstalled(publishedFileId))
		{
			await SubscribeAsync(id, cancellationToken);

			if ((SteamUGC.GetItemState(id) & (uint)EItemState.k_EItemStateSubscribed) == 0)
			{
				GameLog.LogError($"[SteamWorkshop] Steam did not keep the subscription to item {publishedFileId}.");
				return false;
			}

			if (!SteamUGC.DownloadItem(id, true))
			{
				GameLog.LogError($"[SteamWorkshop] Steam refused to download item {publishedFileId}.");
				return false;
			}
		}

		long deadline = System.Environment.TickCount64 + (long)DownloadTimeout.TotalMilliseconds;
		while (!IsInstalled(publishedFileId))
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (System.Environment.TickCount64 > deadline)
			{
				GameLog.LogError($"[SteamWorkshop] Item {publishedFileId} did not finish downloading.");
				return false;
			}

			if (progressCallback != null && SteamUGC.GetItemDownloadInfo(id, out ulong downloaded, out ulong total) && total > 0)
			{
				progressCallback((float)downloaded / total);
			}

			await Task.Delay(200, cancellationToken);
		}

		progressCallback?.Invoke(1f);
		return true;
	}

	/// <summary>Whether Steam holds a complete copy of an item on this machine.</summary>
	/// <param name="publishedFileId">The item to ask about.</param>
	/// <returns>True when the item is installed and carries no pending update.</returns>
	public static bool IsInstalled(ulong publishedFileId)
	{
		if (!IsAvailable || publishedFileId == 0) return false;

		uint state = SteamUGC.GetItemState(new PublishedFileId_t(publishedFileId));
		bool installed = (state & (uint)EItemState.k_EItemStateInstalled) != 0;
		bool needsUpdate = (state & (uint)EItemState.k_EItemStateNeedsUpdate) != 0;
		return installed && !needsUpdate;
	}

	/// <summary>
	///     Where Steam put an item's content, which is the map folder the game then plays from.
	/// </summary>
	/// <param name="publishedFileId">The item to locate.</param>
	/// <returns>The folder Steam installed the item into, or null when it is not installed.</returns>
	public static string? GetInstalledFolder(ulong publishedFileId)
	{
		if (!IsAvailable || publishedFileId == 0) return null;

		if (!SteamUGC.GetItemInstallInfo(new PublishedFileId_t(publishedFileId), out _, out string folder, 1024, out _)) return null;
		return string.IsNullOrWhiteSpace(folder) ? null : folder;
	}

	/// <summary>Every item this account is subscribed to.</summary>
	/// <returns>The subscribed item ids, or an empty list when Steam is not running.</returns>
	public static ulong[] GetSubscribedItemIds()
	{
		if (!IsAvailable) return Array.Empty<ulong>();

		uint count = SteamUGC.GetNumSubscribedItems();
		if (count == 0) return Array.Empty<ulong>();

		var ids = new PublishedFileId_t[count];
		uint written = SteamUGC.GetSubscribedItems(ids, count);
		var result = new ulong[written];
		for (uint index = 0; index < written; index++)
		{
			result[index] = ids[index].m_PublishedFileId;
		}

		return result;
	}

	/// <summary>
	///     Drops this account's subscription, which removes the item from the library on the next Steam pass.
	/// </summary>
	/// <param name="publishedFileId">The item to unsubscribe from.</param>
	/// <returns>True when Steam accepted the request.</returns>
	public static async Task<bool> UnsubscribeAsync(ulong publishedFileId)
	{
		if (!IsAvailable || publishedFileId == 0) return false;

		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var call = new WorkshopCall<RemoteStorageUnsubscribePublishedFileResult_t>(pending);
		SteamAPICall_t apiCall = SteamUGC.UnsubscribeItem(new PublishedFileId_t(publishedFileId));
		if (!call.Track(apiCall)) return false;

		return await WithTimeout(pending.Task, CallTimeout).ConfigureAwait(true);
	}

	private static bool Failed(string reason, ulong publishedFileId = 0)
	{
		GameLog.LogError($"[SteamWorkshop] {reason}");
		return false;
	}

	private static WorkshopPublishResult Failed(string reason)
	{
		GameLog.LogError($"[SteamWorkshop] {reason}");
		return new WorkshopPublishResult(false, 0, reason, false);
	}

	private static async Task<CreateItemResult_t> CreateItemAsync(CancellationToken cancellationToken)
	{
		var pending = new TaskCompletionSource<CreateItemResult_t>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var call = new WorkshopCall<CreateItemResult_t>(pending, result => pending.TrySetResult(result));

		SteamAPICall_t apiCall = SteamUGC.CreateItem(ConsumerAppId, EWorkshopFileType.k_EWorkshopFileTypeCommunity);
		if (!call.Track(apiCall))
		{
			return new CreateItemResult_t { m_eResult = EResult.k_EResultFail };
		}

		try
		{
			return await WithTimeout(pending.Task, CallTimeout, cancellationToken).ConfigureAwait(true);
		}
		catch (TimeoutException)
		{
			return new CreateItemResult_t { m_eResult = EResult.k_EResultTimeout };
		}
	}

	private static async Task<SubmitItemUpdateResult_t> SubmitItemUpdateAsync(UGCUpdateHandle_t updateHandle, string changeNote, Action<WorkshopPublishProgress>? progressCallback, CancellationToken cancellationToken)
	{
		var pending = new TaskCompletionSource<SubmitItemUpdateResult_t>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var call = new WorkshopCall<SubmitItemUpdateResult_t>(pending, result => pending.TrySetResult(result));

		SteamAPICall_t apiCall = SteamUGC.SubmitItemUpdate(updateHandle, changeNote);
		if (!call.Track(apiCall))
		{
			return new SubmitItemUpdateResult_t { m_eResult = EResult.k_EResultFail };
		}

		while (!pending.Task.IsCompleted)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (progressCallback != null)
			{
				EItemUpdateStatus status = SteamUGC.GetItemUpdateProgress(updateHandle, out ulong processed, out ulong total);
				if (status != EItemUpdateStatus.k_EItemUpdateStatusInvalid)
				{
					float fraction = total > 0 ? (float)processed / total : 0f;
					progressCallback(new WorkshopPublishProgress(fraction, DescribeUpdateStatus(status)));
				}
			}

			await Task.Delay(150, cancellationToken);
		}

		return await pending.Task.ConfigureAwait(true);
	}

	private static async Task<bool> SubscribeAsync(PublishedFileId_t publishedFileId, CancellationToken cancellationToken)
	{
		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var call = new WorkshopCall<RemoteStorageSubscribePublishedFileResult_t>(pending);
		SteamAPICall_t apiCall = SteamUGC.SubscribeItem(publishedFileId);
		if (!call.Track(apiCall)) return false;

		try
		{
			return await WithTimeout(pending.Task, CallTimeout, cancellationToken).ConfigureAwait(true);
		}
		catch (TimeoutException)
		{
			return false;
		}
	}

	private static async Task<IReadOnlyList<WorkshopMapEntry>> RunQueryAsync(UGCQueryHandle_t handle, CancellationToken cancellationToken)
	{
		SteamUGC.SetReturnMetadata(handle, true);
		SteamUGC.SetReturnKeyValueTags(handle, true);
		SteamUGC.SetReturnLongDescription(handle, true);
		SteamUGC.SetAllowCachedResponse(handle, 60);

		var pending = new TaskCompletionSource<SteamUGCQueryCompleted_t>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var call = new WorkshopCall<SteamUGCQueryCompleted_t>(pending, result => pending.TrySetResult(result));

		SteamAPICall_t apiCall = SteamUGC.SendQueryUGCRequest(handle);
		if (!call.Track(apiCall))
		{
			SteamUGC.ReleaseQueryUGCRequest(handle);
			return Array.Empty<WorkshopMapEntry>();
		}

		SteamUGCQueryCompleted_t completed;
		try
		{
			completed = await WithTimeout(pending.Task, CallTimeout, cancellationToken).ConfigureAwait(true);
		}
		catch (TimeoutException)
		{
			SteamUGC.ReleaseQueryUGCRequest(handle);
			return Array.Empty<WorkshopMapEntry>();
		}

		try
		{
			if (completed.m_eResult != EResult.k_EResultOK) return Array.Empty<WorkshopMapEntry>();

			var entries = new List<WorkshopMapEntry>((int)completed.m_unNumResultsReturned);
			for (uint index = 0; index < completed.m_unNumResultsReturned; index++)
			{
				if (!SteamUGC.GetQueryUGCResult(handle, index, out SteamUGCDetails_t details)) continue;

				string metadataText = SteamUGC.GetQueryUGCMetadata(handle, index, out string storedMetadata, MaximumMetadataBytes)
					? storedMetadata
					: string.Empty;

				entries.Add(new WorkshopMapEntry(
					details.m_nPublishedFileId.m_PublishedFileId,
					details.m_rgchTitle,
					details.m_rgchDescription,
					details.m_ulSteamIDOwner,
					WorkshopMapMetadata.FromJson(metadataText),
					details.m_nFileSize,
					details.m_unVotesUp,
					details.m_unVotesDown,
					details.m_rtimeCreated,
					details.m_rtimeUpdated,
					details.m_bBanned,
					SteamUGC.GetQueryUGCPreviewURL(handle, index, out string previewUrl, MaximumPreviewUrlBytes) ? previewUrl : string.Empty));
			}

			return entries;
		}
		finally
		{
			SteamUGC.ReleaseQueryUGCRequest(handle);
		}
	}

	private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, CancellationToken cancellationToken = default)
	{
		var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task completed = await Task.WhenAny(task, Task.Delay(timeout, delayCancellation.Token)).ConfigureAwait(true);
		delayCancellation.Cancel();

		if (completed != task) throw new TimeoutException();
		return await task.ConfigureAwait(true);
	}

	private static IEnumerable<ulong[]> Batch(ulong[] source, uint size)
	{
		for (int index = 0; index < source.Length; index += (int)size)
		{
			int count = Math.Min((int)size, source.Length - index);
			var batch = new ulong[count];
			Array.Copy(source, index, batch, 0, count);
			yield return batch;
		}
	}

	private static bool MatchesMapName(WorkshopMapEntry entry, string normalizedMapName)
	{
		if (NormalizeMapName(entry.Identity.MapName) == normalizedMapName) return true;
		return NormalizeMapName(entry.Title) == normalizedMapName;
	}

	private static string NormalizeMapName(string name)
	{
		return string.Concat(name.Where(character => !char.IsWhiteSpace(character) && character != '_')).ToLowerInvariant();
	}

	private static string DescribeUpdateStatus(EItemUpdateStatus status)
	{
		return status switch
		{
			EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => "Preparing the item's settings...",
			EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => "Preparing the map files...",
			EItemUpdateStatus.k_EItemUpdateStatusUploadingContent => "Uploading the map files...",
			EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => "Uploading the thumbnail...",
			EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => "Finishing the publish...",
			_ => "Publishing..."
		};
	}

	private static string DescribeResult(EResult result)
	{
		return result switch
		{
			EResult.k_EResultOK => string.Empty,
			EResult.k_EResultFail => "Steam refused the request.",
			EResult.k_EResultTimeout => "Steam did not answer in time.",
			EResult.k_EResultBusy => "Steam is busy with another request. Try again in a moment.",
			EResult.k_EResultFileNotFound => "Steam could not read the files being published.",
			EResult.k_EResultLimitExceeded => "The item is too large for Steam Workshop.",
			EResult.k_EResultAccessDenied => "This account is not allowed to publish this item.",
			EResult.k_EResultInvalidParam => "Steam rejected part of the item's description.",
			_ => $"Steam answered {result}."
		};
	}

	/// <summary>
	///     Keeps one Steam call result alive until Steam answers it. Steamworks.NET stops dispatching a call result the
	///     moment its <see cref="CallResult{T}" /> is collected, so every wait owns one of these for its lifetime.
	/// </summary>
	private class WorkshopCall<T> : IDisposable where T : struct
	{
		private readonly CallResult<T> _callResult;

		public WorkshopCall(TaskCompletionSource<bool> pending)
		{
			_callResult = CallResult<T>.Create((callbackData, ioFailure) => pending.TrySetResult(!ioFailure && IsOk(callbackData)));
		}

		public WorkshopCall(TaskCompletionSource<T> pending, Action<T>? onResult = null)
		{
			_callResult = CallResult<T>.Create((callbackData, ioFailure) =>
			{
				if (ioFailure)
				{
					pending.TrySetException(new InvalidOperationException($"Steam reported an I/O failure for {typeof(T).Name}."));
					return;
				}

				onResult?.Invoke(callbackData);
			});
		}

		public bool Track(SteamAPICall_t apiCall)
		{
			if (apiCall.m_SteamAPICall == 0) return false;

			_callResult.Set(apiCall);
			return true;
		}

		public void Dispose()
		{
			_callResult.Dispose();
		}

		private static bool IsOk(T callbackData)
		{
			return callbackData switch
			{
				RemoteStorageSubscribePublishedFileResult_t subscribed => subscribed.m_eResult == EResult.k_EResultOK,
				RemoteStorageUnsubscribePublishedFileResult_t unsubscribed => unsubscribed.m_eResult == EResult.k_EResultOK,
				_ => true
			};
		}
	}
}
