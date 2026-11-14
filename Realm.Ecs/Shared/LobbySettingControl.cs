namespace Realm.Ecs;

/// <summary>
///     The kind of control a lobby offers a host for one match setting a map declared, which decides how that setting is
///     edited on screen and what its value means.
/// </summary>
public enum LobbySettingControl : byte
{
	/// <summary>A checkbox; the value is zero for unchecked and one for checked.</summary>
	Toggle = 0,

	/// <summary>A dropdown; the value is the index of the option the host chose.</summary>
	Choice = 1,

	/// <summary>A number spinner; the value lies between the setting's own minimum and maximum.</summary>
	Count = 2
}
