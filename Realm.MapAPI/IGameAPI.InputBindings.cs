namespace Realm.MapAPI;

/// <summary>
///     The key-binding surface a map may drive: which physical key casts which ability on which unit, so a mode whose
///     control scheme is a key held down - steering a vehicle, holding a throttle, charging a shot - is something the map
///     states for itself instead of something the engine assumes about the map it is running.
/// </summary>
/// <remarks>
///     <para>
///         Every member is opt-in: a map that binds no key leaves the player's keyboard exactly as it was, and the engine
///         itself never binds a key. A binding belongs to one player's screen, so the seat racing a vehicle keeps its own
///         controls while the seats watching are unaffected.
///     </para>
///     <para>
///         A binding is a pair of abilities rather than one: pressing the key casts the first and letting it go casts the
///         second, which is what a control that acts for as long as it is held needs. Either id may be empty, and an empty
///         id casts nothing for that half of the press.
///     </para>
///     <para>
///         A binding casts on the unit the map names, through the same cast an ability card uses, so what the ability
///         costs, how long it takes to come back and what it does are all settled by the authority as they always are. A
///         binding lasts until <see cref="ClearPlayerKeyBindings" /> is called or the same key is bound again for that
///         player, so a map that binds a control for a race clears it before the seat plays anything else.
///     </para>
/// </remarks>
public partial interface IGameAPI
{
	/// <summary>
	///     Binds one key, on one player's screen, to a pair of abilities cast on a unit.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index whose screen reads the key.</param>
	/// <param name="unit">The unit both abilities are cast on.</param>
	/// <param name="key">
	///     The key by the name printed on it: a single letter or digit, or a name such as Space, Left, Right, Up, Down,
	///     Shift, Tab, Enter, Escape, or F1 to F12. A name the screen does not recognise is refused and logged, so a
	///     binding is never silently placed on a key the player did not expect.
	/// </param>
	/// <param name="pressAbilityId">The ability cast while the key goes down, or an empty string to cast nothing.</param>
	/// <param name="releaseAbilityId">The ability cast when the key comes back up, or an empty string to cast nothing.</param>
	/// <remarks>
	///     Binding a key that is already bound replaces that binding, which is how a map moves a control from one ability
	///     to another without clearing the whole set. The key is read as a plain press: a combination that starts with
	///     Ctrl or Alt belongs to the player's own commands and is left alone.
	/// </remarks>
	void BindPlayerKeyToAbility(int playerIndex, UnitId unit, string key, string pressAbilityId, string releaseAbilityId);

	/// <summary>
	///     Clears every key this map bound for one player's screen.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index whose bindings are cleared.</param>
	/// <remarks>
	///     Clearing is the map giving the keyboard back: the keys it bound return to whatever the player's own commands do
	///     with them. A map that binds keys for a section of play is expected to clear them when that section ends.
	/// </remarks>
	void ClearPlayerKeyBindings(int playerIndex);
}
