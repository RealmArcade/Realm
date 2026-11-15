namespace Realm.MapAPI;

/// <summary>
///     The Steam surface a map may call: achievements, stats and per-player cloud files. Every member is opt-in - the
///     engine never unlocks an achievement, records a stat or touches a cloud file on its own - so a map that says nothing
///     about Steam behaves exactly as it did before this surface existed.
/// </summary>
/// <remarks>
///     <para>
///         A map script runs in the authority, while Steam runs inside each player's own screen, so these calls describe an
///         intent to the screen of the player the call names. The description travels on the reliable stream and is applied
///         by the screen sitting in that player slot: no other screen, and no spectator or ally, applies it.
///     </para>
///     <para>
///         Every identifier a map states here is namespaced by the map it comes from, in the screen that talks to Steam, so
///         two maps may both call an achievement <c>chapter_one</c> without touching the same Steam achievement. The name
///         Steam is given is <c>realm_&lt;map&gt;_&lt;identifier&gt;</c>, with everything that is not a letter, a digit or an
///         underscore removed and the rest lowercased. That name, and any stat name derived the same way, has to exist in
///         the application's Steamworks configuration before it means anything; a name Steam does not know is refused by
///         Steam without failing the match.
///     </para>
///     <para>
///         Nothing here blocks. Achievements, stats and cloud writes are handed to Steam and forgotten, and cloud reads come
///         out of the copy the screen uploaded when the map asked for it, which is why <see cref="RequestSteamCloudFiles" />
///         and <see cref="IsSteamCloudSynced" /> exist. A match without Steam, a seat that is a computer player and a screen
///         whose Steam is not running all answer <see cref="IsSteamAvailable" /> with false and quietly ignore the rest, so a
///         map may call these members without asking first.
///     </para>
/// </remarks>
public partial interface IGameAPI
{
	/// <summary>
	///     Whether the seat at the given player index is a person whose screen is running Steam, and may therefore receive
	///     achievements, stats and cloud files.
	/// </summary>
	/// <param name="playerIndex">The player slot to test.</param>
	/// <returns>True when that seat belongs to a person playing inside Steam.</returns>
	bool IsSteamAvailable(int playerIndex);

	/// <summary>
	///     Unlocks a Steam achievement for the player at the given index.
	/// </summary>
	/// <param name="playerIndex">The player whose Steam account earns the achievement.</param>
	/// <param name="achievementId">
	///     The map's own identifier for the achievement, with no namespace of its own; see the remarks on
	///     <see cref="IGameAPI" /> for how it is turned into a Steam achievement name.
	/// </param>
	/// <remarks>
	///     Unlocking an achievement the player already holds is harmless: Steam keeps the first time it was earned. The call
	///     is the map's statement that the player met the condition, so a map that awards an achievement for something it
	///     tracks itself does not have to ask Steam whether it was already earned.
	/// </remarks>
	void UnlockSteamAchievement(int playerIndex, string achievementId);

	/// <summary>
	///     Shows the player how far they are toward an achievement Steam treats as a progress achievement, without unlocking
	///     it.
	/// </summary>
	/// <param name="playerIndex">The player to show the progress to.</param>
	/// <param name="achievementId">The map's own identifier for the achievement.</param>
	/// <param name="progress">How much of the achievement is done, in the units its Steam configuration counts in.</param>
	/// <param name="maxProgress">The amount that completes the achievement.</param>
	/// <remarks>
	///     This is Steam's own progress indicator, which is a display of progress rather than a stat: a map that tracks
	///     counts over a longer life than one match normally records them with <see cref="SetSteamStatInt" /> and calls this
	///     only when the count changes.
	/// </remarks>
	void IndicateSteamAchievementProgress(int playerIndex, string achievementId, int progress, int maxProgress);

	/// <summary>
	///     Records an integer Steam stat for the player at the given index, which is how a map keeps a count that
	///     achievements and Steam's own stat pages read.
	/// </summary>
	/// <param name="playerIndex">The player whose Steam account records the stat.</param>
	/// <param name="statId">The map's own identifier for the stat.</param>
	/// <param name="value">The value to store.</param>
	void SetSteamStatInt(int playerIndex, string statId, int value);

	/// <summary>
	///     Adds to an integer Steam stat for the player at the given index and stores the result, which keeps a running
	///     count without the map having to know the value it had.
	/// </summary>
	/// <param name="playerIndex">The player whose Steam account records the stat.</param>
	/// <param name="statId">The map's own identifier for the stat.</param>
	/// <param name="delta">The amount to add, which may be negative.</param>
	/// <remarks>
	///     The addition happens in the player's screen, on the value Steam holds at that moment, so a count kept across
	///     several matches stays correct even when this match never read the old value.
	/// </remarks>
	void AddSteamStatInt(int playerIndex, string statId, int delta);

	/// <summary>
	///     Records a floating point Steam stat for the player at the given index, which is how a map keeps a rate, a
	///     distance or a best time.
	/// </summary>
	/// <param name="playerIndex">The player whose Steam account records the stat.</param>
	/// <param name="statId">The map's own identifier for the stat.</param>
	/// <param name="value">The value to store.</param>
	void SetSteamStatFloat(int playerIndex, string statId, float value);

	/// <summary>
	///     Asks the screen of the player at the given index to upload the cloud files this map keeps for them, so the map
	///     can read them from here.
	/// </summary>
	/// <param name="playerIndex">The player whose cloud files the map wants.</param>
	/// <remarks>
	///     <para>
	///         A map script cannot read Steam directly, so cloud files arrive asynchronously: call this once (a saved-data
	///         load during match start is the usual place), then poll <see cref="IsSteamCloudSynced" /> and read what arrived
	///         with <see cref="GetSteamCloudFileCount" />, <see cref="GetSteamCloudFileName" /> and
	///         <see cref="ReadSteamCloudFile" />. Asking again replaces the copy the map holds, so a map that wants to pick up
	///         changes a screen made while the match runs may ask again later.
	///     </para>
	///     <para>
	///         Asking a seat that is not a person, or whose screen is not running Steam, settles the request immediately with
	///         nothing in it: <see cref="IsSteamCloudSynced" /> then answers true and the file count is zero.
	///     </para>
	/// </remarks>
	void RequestSteamCloudFiles(int playerIndex);

	/// <summary>
	///     Whether the cloud files of the player at the given index have arrived, which means the map may read them.
	/// </summary>
	/// <param name="playerIndex">The player to test.</param>
	/// <returns>True when a request has settled, whether or not it carried any file.</returns>
	bool IsSteamCloudSynced(int playerIndex);

	/// <summary>
	///     How many cloud files the player at the given index holds for this map.
	/// </summary>
	/// <param name="playerIndex">The player whose files are counted.</param>
	/// <returns>The number of files the last upload carried.</returns>
	int GetSteamCloudFileCount(int playerIndex);

	/// <summary>
	///     The name of one of the cloud files the player at the given index holds for this map.
	/// </summary>
	/// <param name="playerIndex">The player whose files are listed.</param>
	/// <param name="index">The position in the list, from zero to <see cref="GetSteamCloudFileCount" /> minus one.</param>
	/// <returns>The file's name, or an empty string for a position the list does not hold.</returns>
	string GetSteamCloudFileName(int playerIndex, int index);

	/// <summary>
	///     Whether the player at the given index holds a cloud file with this name for this map.
	/// </summary>
	/// <param name="playerIndex">The player whose files are read.</param>
	/// <param name="fileName">The file's name, as the map named it.</param>
	/// <returns>True when a file with that name is held.</returns>
	bool HasSteamCloudFile(int playerIndex, string fileName);

	/// <summary>
	///     Reads a cloud file the player at the given index holds for this map.
	/// </summary>
	/// <param name="playerIndex">The player whose file is read.</param>
	/// <param name="fileName">The file's name, as the map named it.</param>
	/// <returns>The file's content, or an empty string when that player holds no such file.</returns>
	string ReadSteamCloudFile(int playerIndex, string fileName);

	/// <summary>
	///     Writes a cloud file for the player at the given index, replacing the one the map wrote before it.
	/// </summary>
	/// <param name="playerIndex">The player whose Steam account keeps the file.</param>
	/// <param name="fileName">
	///     The file's name, which is a plain name: up to 64 characters of letters, digits, dots, dashes and underscores, with
	///     no folder in it, because the screen places the file inside the part of Steam's cloud that belongs to this map.
	/// </param>
	/// <param name="content">The file's content, up to 32768 characters.</param>
	/// <returns>
	///     True when the write was accepted for that player, which is immediately true for the copy the map reads back and
	///     later for Steam; false when the name, the size or the seat is one this rule refuses.
	/// </returns>
	/// <remarks>
	///     The write reaches Steam inside the player's screen, and Steam uploads it on its own schedule, so a map that wants
	///     a save to be safely away before the match ends should write it when the player reaches the point being saved
	///     rather than only at the end. A copy written during the match is what a later <see cref="ReadSteamCloudFile" />
	///     answers, so a map can read back what it just wrote.
	/// </remarks>
	bool WriteSteamCloudFile(int playerIndex, string fileName, string content);

	/// <summary>
	///     Removes a cloud file the player at the given index holds for this map.
	/// </summary>
	/// <param name="playerIndex">The player whose Steam account deletes the file.</param>
	/// <param name="fileName">The file's name, as the map named it.</param>
	/// <returns>
	///     True when the delete was accepted, which removes it from the copy the map reads as well as asking that player's
	///     screen to delete the file in Steam.
	/// </returns>
	bool DeleteSteamCloudFile(int playerIndex, string fileName);
}
