// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Declares the default leaderboard board used by maps that publish a single table.
	/// </summary>
	/// <param name="title">The header title of the leaderboard.</param>
	/// <param name="visible">True to display the leaderboard, false to hide it.</param>
	void SetLeaderboardVisible(string title, bool visible);

	/// <summary>
	/// Sets or updates a value on the leaderboard.
	/// </summary>
	/// <param name="label">The row label (e.g. player name or score metric).</param>
	/// <param name="value">The value to display.</param>
	void SetLeaderboardValue(string label, string value);

	/// <summary>
	/// Clears every leaderboard board and its rows.
	/// </summary>
	void ClearLeaderboard();

	/// <summary>
	/// Declares or updates a named leaderboard board that every client can switch between.
	/// Clients render one visible board at a time and keep their own board selection, so publishing
	/// several boards never changes what another player is looking at.
	/// </summary>
	/// <param name="boardId">Stable identifier for the board, reused across updates.</param>
	/// <param name="title">Header title shown for the board; the client passes it through localisation.</param>
	/// <param name="visible">True to offer the board to clients, false to withdraw it.</param>
	void SetLeaderboardBoard(string boardId, string title, bool visible);

	/// <summary>
	/// Clears every row of one leaderboard board while keeping its title and visibility.
	/// </summary>
	/// <param name="boardId">Identifier of the board to clear.</param>
	void ClearLeaderboardBoard(string boardId);

	/// <summary>
	/// Declares a player group on a leaderboard board so it appears in the client group selector even when it has no rows.
	/// Clients default the group selector for a board to their own player slot.
	/// </summary>
	/// <param name="boardId">Identifier of the board that owns the group.</param>
	/// <param name="groupPlayerIndex">Zero-based player slot the group represents.</param>
	/// <param name="groupTitle">Label shown for the group in the client selector.</param>
	void SetLeaderboardBoardGroup(string boardId, int groupPlayerIndex, string groupTitle);

	/// <summary>
	/// Appends a row to a leaderboard board, optionally scoped to a player group.
	/// Boards whose rows use groups gain a second client-side selector, and each client defaults that
	/// selector to its own player slot.
	/// </summary>
	/// <param name="boardId">Identifier of the board that receives the row.</param>
	/// <param name="groupPlayerIndex">Zero-based player slot the row belongs to, or -1 for an ungrouped row.</param>
	/// <param name="groupTitle">Label shown for the group in the client selector.</param>
	/// <param name="label">Row label text.</param>
	/// <param name="value">Row value text.</param>
	void AddLeaderboardBoardRow(string boardId, int groupPlayerIndex, string groupTitle, string label, string value);

	/// <summary>
	/// Displays or hides the multi-column stats summary table on all players' screens.
	/// </summary>
	/// <remarks>
	/// Every client draws the table itself and may collapse it to its title bar, which is that player's own view of it
	/// rather than something this call decides.
	/// </remarks>
	/// <param name="title">Header title of the summary table.</param>
	/// <param name="visible">True to display the table, false to hide it.</param>
	void ShowSummaryTable(string title, bool visible);

	/// <summary>
	/// Configures custom column header labels for the stats summary table.
	/// Headers[0] labels the row identifier/player column. Subsequent elements label the metric columns.
	/// Headers must be passed already localized by the caller.
	/// </summary>
	/// <param name="columnHeaders">Array of header titles to display at the top of the table.</param>
	void SetSummaryTableHeaders(params string[] columnHeaders);

	/// <summary>
	/// Sets or updates a row in the stats summary table.
	/// </summary>
	/// <remarks>
	/// The player column is drawn as rich text, so a row key may carry BBCode such as the markup
	/// <see cref="FormatColoredPlayerName"/> produces. The metric columns are drawn as plain text, so numbers stay
	/// readable in fixed-width columns whatever the localised digit glyphs are.
	/// </remarks>
	/// <param name="rowKey">Unique row identifier or player label (corresponds to Headers[0]).</param>
	/// <param name="cellValues">Metric values corresponding to Headers[1..N].</param>
	void SetSummaryTableRow(string rowKey, params string[] cellValues);

	/// <summary>
	/// Clears all rows from the active stats summary table.
	/// </summary>
	void ClearSummaryTable();

	/// <summary>
	/// Adds one titled page to the Instructions menu that every player can open from the in-game toolbar.
	/// </summary>
	/// <remarks>
	/// Pages are ordered by the order these calls are made, so a script that explains its rules in a fixed sequence
	/// registers them in that sequence, typically once from <see cref="IMapScript.Initialize(IGameAPI)"/>: the first call becomes
	/// the entry the menu selects when it opens. Publishing a title that is already registered replaces that page's text
	/// where it stands, so a rule that changes during a match is restated rather than duplicated. The toolbar button stays
	/// hidden until at least one page exists, and a player who joins late is sent every page registered so far.
	/// </remarks>
	/// <param name="title">Short heading shown in the menu's ordering selector, such as "Drafting".</param>
	/// <param name="body">The page text shown under the selector. BBCode is allowed, so a page can bold a term or list its rules.</param>
	void AddInstructionsSection(string title, string body);

	/// <summary>
	/// Appends a new row to the default leaderboard board with the given label and value.
	/// Must call <see cref="SetLeaderboardVisible"/> first to make the panel visible.
	/// </summary>
	/// <param name="label">Row label text.</param>
	/// <param name="value">Row value text.</param>
	/// <param name="color">Optional RGB tint for the label text.</param>
	void AddLeaderboardRow(string label, string value, Vector3? color = null);
}
