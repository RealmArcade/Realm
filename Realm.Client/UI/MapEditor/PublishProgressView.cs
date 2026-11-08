using Godot;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The progress row of the publish dialog: the bar, the one-line status and the close button that only appears once
/// the run has finished. The publish flow reports the same three things on every path, so the wording and colour of
/// each outcome is written once here instead of being repeated at every failure site.
/// </summary>
internal sealed class PublishProgressView
{
	private readonly ProgressBar _progress;
	private readonly Label _status;
	private readonly Button _close;

	public PublishProgressView(ProgressBar progress, Label status, Button close)
	{
		_progress = progress;
		_status = status;
		_close = close;
	}

	/// <summary>True while the dialog the view points at is still alive.</summary>
	public bool IsAlive =>
		GodotObject.IsInstanceValid(_progress) &&
		GodotObject.IsInstanceValid(_status) &&
		GodotObject.IsInstanceValid(_close);

	/// <summary>
	/// Moves the bar to a stage and states what is happening. Deliberately does not touch the text colour: every
	/// stage before an outcome leaves the theme colour alone, and no stage runs after a terminal one.
	/// </summary>
	public void Stage(int value, string text)
	{
		if (!IsAlive) return;
		_progress.Value = value;
		_status.Text = text;
	}

	/// <summary>Terminal failure: full bar, red text, and the close button so the user can dismiss the run.</summary>
	public void Fail(string text)
	{
		if (!IsAlive) return;
		_progress.Value = 100;
		_status.Text = "❌ " + text;
		_status.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
		_close.Visible = true;
	}

	/// <summary>
	/// Terminal success. The dialog is freed a moment later, so the close button is intentionally left as it was.
	/// </summary>
	public void Succeed(string text)
	{
		if (!IsAlive) return;
		_progress.Value = 100;
		_status.Text = "✓ " + text;
		_status.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
	}
}
