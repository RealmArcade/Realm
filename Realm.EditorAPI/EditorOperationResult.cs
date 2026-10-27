namespace Realm.EditorAPI;

/// <summary>
/// What became of one editor operation. An operation is asked for by something that may not be able to see the
/// editor's screen — a tool, a script, or a second process over the editor's API — so a refusal is reported to the
/// caller rather than shown only as a line of feedback in the editor.
/// </summary>
public readonly record struct EditorOperationResult(bool Success, string Error)
{
	/// <summary>An operation that did what it was asked to do.</summary>
	public static EditorOperationResult Succeeded() => new(true, string.Empty);

	/// <summary>An operation that was refused, and why, in words the caller can act on.</summary>
	public static EditorOperationResult Failed(string error) => new(false, error ?? string.Empty);
}
