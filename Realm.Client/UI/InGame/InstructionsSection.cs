namespace Realm.Client.UI.InGame;

/// <summary>
///     One page of the in-match Instructions menu: the heading the selector lists and the text it shows when chosen.
/// </summary>
public readonly record struct InstructionsSection(string Title, string Body);
